using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DailyRecord.Core;

namespace DailyRecord.App;

public sealed partial class TutorWindow
{
    internal static Brush HeatColorForTest(int level) => HeatColors[level];
    internal Action<Window>? HeatmapScalePresenter { get; set; }
    private static readonly Brush[] HeatColors = new[] { "#EEEAF4", "#E0D5F3", "#BAA5E3", "#9674CD", "#6352A6" }.Select(c =>
    { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(c)); brush.Freeze(); return (Brush)brush; }).ToArray();

    private void ShowHeatmapScaleEditor()
    {
        var scale = _service.State.HeatmapScale;
        var dialog = new Window { Owner = IsVisible ? this : null, Title = "授课热力图颜色范围", Width = 600, Height = 660, MinWidth = 540, MinHeight = 480, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var content = Ui.Stack(Ui.Text("让颜色适合你的授课节奏", 23, bold: true),
            Ui.Text("设置前三档实际授课小时的上限，0 小时始终留白。仅调整颜色，课程与收入不会变化。", 12, Ui.Muted)); content.Margin = new Thickness(28);
        var inputs = new[] { scale.LightHours, scale.MediumHours, scale.DarkHours }.Select((n, i) => new TextBox
        { Text = n.ToString("0.##", CultureInfo.InvariantCulture), Width = 110, Tag = "heatmap-limit:" + i }).ToArray();
        var preview = new WrapPanel { Margin = new Thickness(0, 16, 0, 8), Tag = "heatmap-range-preview" };
        var feedback = Ui.Text("", 12, Ui.Accent); feedback.Tag = "heatmap-range-error";
        Button? save = null;
        TutorHeatmapScale? Read()
        {
            var values = new decimal[3];
            for (var i = 0; i < 3; i++) if (!decimal.TryParse(inputs[i].Text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out values[i])) return null;
            var result = new TutorHeatmapScale(values[0], values[1], values[2]); return result.IsValid ? result : null;
        }
        void Preview()
        {
            preview.Children.Clear(); var value = Read(); if (save is not null) save.IsEnabled = value is not null;
            feedback.Text = value is null ? TutorHeatmapScale.ValidationMessage : "保存到当前账号，页面与分享图片同步使用；备份保留此设置。";
            if (value is null) return;
            foreach (var (label, index) in value.Labels().Select((s, i) => (s, i)))
            { var entry = Ui.Row(new Border { Width = 12, Height = 12, Background = HeatColors[index], Margin = new Thickness(0, 0, 5, 0) }, Ui.Text(label, 11, Ui.Muted)); entry.Margin = new Thickness(0, 0, 10, 6); preview.Children.Add(entry); }
        }
        for (var i = 0; i < inputs.Length; i++)
        { var row = Ui.Row(Ui.Text($"第 {i + 1} 档上限  "), inputs[i], Ui.Text("  小时（含）", 12, Ui.Muted)); row.Margin = new Thickness(0, 18, 0, 0); content.Children.Add(row); }
        void Preset(TutorHeatmapScale v) { inputs[0].Text = v.LightHours.ToString("0.##"); inputs[1].Text = v.MediumHours.ToString("0.##"); inputs[2].Text = v.DarkHours.ToString("0.##"); }
        var high = Ui.Button("授课较多 · 2 / 4 / 8", () => Preset(new(2, 4, 8))); high.Tag = "heatmap-preset-high";
        var reset = Ui.Button("恢复默认 · 1 / 2 / 4", () => Preset(new())); reset.Tag = "heatmap-preset-default";
        var presets = new WrapPanel { Margin = new Thickness(0, 22, 0, 0) }; presets.Children.Add(high); presets.Children.Add(reset); content.Children.Add(presets);
        content.Children.Add(preview); content.Children.Add(feedback);
        save = Ui.Button("保存范围", () => { if (Read() is not { } value) return; try { _service.UpdateHeatmapScale(value); Render(); dialog.Close(); } catch (Exception ex) { feedback.Text = "保存未完成：" + ex.Message; } }, true); save.Tag = "heatmap-range-save";
        var cancel = Ui.Button("取消", dialog.Close); cancel.Tag = "heatmap-range-cancel"; cancel.IsCancel = true;
        var actions = Ui.Row(save, cancel); actions.Margin = new Thickness(0, 22, 0, 0); content.Children.Add(actions);
        foreach (var input in inputs) input.TextChanged += (_, _) => Preview(); Preview();
        dialog.Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        if (HeatmapScalePresenter is { } present) present(dialog); else dialog.ShowDialog();
    }
}
