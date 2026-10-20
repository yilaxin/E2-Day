using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DailyRecord.Core;

namespace DailyRecord.App;

public partial class MainWindow
{
    internal Action<Window>? HeatmapScalePresenter { get; set; }

    private void ShowHeatmapScaleEditor()
    {
        var scale = _service.State.Settings.CompletionHeatmapScale();
        var dialog = new Window { Owner = IsVisible ? this : null, Title = "热力图颜色范围", Width = 600, Height = 640,
            MinWidth = 540, MinHeight = 480, Background = Ui.Page, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var content = new StackPanel { Margin = new Thickness(28) };
        content.Children.Add(Ui.Text("让颜色适合你的节奏", 23, bold: true));
        var description = Ui.Text("0 项始终留白。设置前三档的上限，超过第三档的数量使用最深色。只调整颜色，不改变完成记录。", 12, Ui.Muted);
        description.Margin = new Thickness(0, 10, 0, 18); content.Children.Add(description);
        var inputs = new[] { scale.LightMax, scale.MediumMax, scale.DarkMax }.Select((n, i) => new TextBox
            { Text = n.ToString(), Width = 105, MaxLength = 5, Tag = "heatmap-limit:" + i }).ToArray();
        var form = new StackPanel();
        for (var i = 0; i < inputs.Length; i++)
        {
            var swatch = new Border { Width = 16, Height = 16, Background = HeatColors[i + 1], CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 10, 0) };
            var label = Ui.Text($"第 {i + 1} 档上限", 13); label.Width = 125;
            var row = Ui.Row(swatch, label, inputs[i], Ui.Text("  项（含）", 12, Ui.Muted)); row.Margin = new Thickness(0, 0, 12, 12); form.Children.Add(row);
            System.Windows.Automation.AutomationProperties.SetName(inputs[i], $"第 {i + 1} 档完成数量上限");
        }
        content.Children.Add(GlassCardBorder.Create(form));
        var preview = new WrapPanel { Margin = new Thickness(0, 0, 0, 12), Tag = "heatmap-range-preview" };
        var feedback = Ui.Text("", 12, Ui.Accent); feedback.Tag = "heatmap-range-error";
        Button? save = null;
        HeatmapScale? ReadScale()
        {
            if (inputs.Any(t => !int.TryParse(t.Text, out _))) return null;
            var result = new HeatmapScale(int.Parse(inputs[0].Text), int.Parse(inputs[1].Text), int.Parse(inputs[2].Text));
            return result.IsValid ? result : null;
        }
        void Preview()
        {
            preview.Children.Clear(); var value = ReadScale();
            if (save is not null) save.IsEnabled = value is not null;
            feedback.Text = value is null ? HeatmapScale.ValidationMessage : "保存到当前账号，热力图与分享图片同步生效。";
            if (value is null) return;
            foreach (var band in value.Bands())
            {
                var swatch = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = HeatColors[value.Level(band.Minimum)], Margin = new Thickness(0, 0, 5, 0) };
                var entry = Ui.Row(swatch, Ui.Text(band.Label, 11, Ui.Muted)); entry.Margin = new Thickness(0, 0, 14, 8); preview.Children.Add(entry);
            }
        }
        void Preset(HeatmapScale value)
        { inputs[0].Text = value.LightMax.ToString(); inputs[1].Text = value.MediumMax.ToString(); inputs[2].Text = value.DarkMax.ToString(); }
        var frequent = Ui.Button("高完成量预设 · 5 / 10 / 20", () => Preset(new(5, 10, 20))); frequent.Tag = "heatmap-preset-high";
        var reset = Ui.Button("恢复默认", () => Preset(new())); reset.Tag = "heatmap-preset-default";
        var presets = Ui.Row(frequent, reset); presets.Margin = new Thickness(0, 0, 0, 16); content.Children.Add(presets);
        content.Children.Add(preview); content.Children.Add(feedback);
        save = Ui.Button("保存范围", () =>
        {
            if (ReadScale() is not { } value) { Preview(); return; }
            try { _service.UpdateHeatmapScale(value); RefreshPage(); dialog.Close(); }
            catch (Exception ex) { feedback.Text = "保存未完成：" + ex.Message; UpdateSaveStatus(); }
        }, accent: true);
        save.Tag = "heatmap-range-save"; save.IsDefault = true;
        var cancel = Ui.Button("取消", () => dialog.Close()); cancel.IsCancel = true; cancel.Tag = "heatmap-range-cancel";
        var actions = Ui.Row(save, cancel); actions.Margin = new Thickness(0, 20, 0, 0); content.Children.Add(actions);
        foreach (var input in inputs) input.TextChanged += (_, _) => Preview();
        Preview(); dialog.Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        if (HeatmapScalePresenter is { } present) present(dialog); else dialog.ShowDialog();
    }
}
