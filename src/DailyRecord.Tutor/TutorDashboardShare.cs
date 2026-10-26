using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DailyRecord.Core;

namespace DailyRecord.App;

public sealed partial class TutorWindow
{
    internal Action<DataObject> WriteImageClipboard { get; set; } = data => Clipboard.SetDataObject(data, true);
    internal Border BuildDashboardShareImage()
    {
        var from = _dashboardFrom ?? new DateOnly(Today.Year, Today.Month, 1); var to = _dashboardTo ?? Today;
        var report = TutorTeachingAnalytics.Build(_service, from, to, Today, _dashboardStudent);
        var body = Ui.Stack(Ui.Text("E²-Day Teacher · 教学概览" + (IsExample ? " · 虚构示例" : ""), 29, Ui.Accent, true),
            Ui.Text($"{from:yyyy/MM/dd} 至 {to:yyyy/MM/dd} · " + (_dashboardStudent is null ? "全部学生" : "所选学生（已匿名）"), 16, Ui.Muted));
        var metrics = new UniformGrid { Columns = 4, Margin = new Thickness(0, 22, 0, 8) };
        foreach (var item in new[] { ("已授课", report.Taught + " 节"), ("实际课时", Hours(report.Minutes) + " 小时"), ("待确认", report.Pending.Count + " 节"), ("待补反馈", report.MissingFeedback.Count + " 节") })
        { var card = Ui.Card(Ui.Stack(Ui.Text(item.Item1, 12, Ui.Muted), Ui.Text(item.Item2, 25, Ui.Accent, true)), new Thickness(16)); card.Margin = new Thickness(0, 0, 10, 12); metrics.Children.Add(card); }
        body.Children.Add(metrics);
        var scale = _service.State.HeatmapScale;
        var heat = Ui.Stack(Ui.Text("授课热力日历 · 实际授课小时", 18, bold: true));
        var compact = report.Days.Count <= 42;
        var offset = ((int)from.DayOfWeek + 6) % 7; var weeks = (offset + report.Days.Count + 6) / 7;
        var grid = new Grid { Margin = new Thickness(0, 16, 0, 14) };
        for (var i = 0; i < (compact ? 7 : weeks); i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < (compact ? weeks : 7); i++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(compact ? 45 : 18) });
        if (compact)
        {
            grid.RowDefinitions.Insert(0, new RowDefinition { Height = new GridLength(24) });
            for (var i = 0; i < 7; i++) { var label = Ui.Text(new[] { "一", "二", "三", "四", "五", "六", "日" }[i], 11, Ui.Muted); Grid.SetColumn(label, i); grid.Children.Add(label); }
        }
        foreach (var day in report.Days)
        {
            var index = offset + day.Date.DayNumber - from.DayNumber; var level = scale.Level(day.Minutes);
            var cell = new Border { Background = HeatColors[level], CornerRadius = new CornerRadius(3), Margin = new Thickness(1), Tag = "share-heat:" + day.Date.ToString("yyyy-MM-dd") };
            if (compact) cell.Child = Ui.Stack(Ui.Text(day.Date.ToString("M/d"), 10, level >= 3 ? Brushes.White : Ui.Ink), Ui.Text(Hours(day.Minutes) + "h", 10, level >= 3 ? Brushes.White : Ui.Ink));
            Grid.SetColumn(cell, compact ? index % 7 : index / 7); Grid.SetRow(cell, compact ? index / 7 + 1 : index % 7); grid.Children.Add(cell);
        }
        heat.Children.Add(grid); var legend = new WrapPanel();
        foreach (var (label, index) in scale.Labels().Select((s, i) => (s, i)))
        { var row = Ui.Row(new Border { Width = 12, Height = 12, Background = HeatColors[index], Margin = new Thickness(0, 0, 5, 0) }, Ui.Text(label, 11, Ui.Muted)); row.Margin = new Thickness(0, 0, 12, 0); legend.Children.Add(row); }
        heat.Children.Add(legend); body.Children.Add(Ui.Card(heat));
        var feedback = report.Taught == 0 ? "暂无已授课程" : $"已记录 {report.WithFeedback} / {report.Taught} 节 · {100m * report.WithFeedback / report.Taught:0}%";
        body.Children.Add(Ui.Card(Ui.Stack(Ui.Text("课程执行与反馈", 18, bold: true), Ui.Text($"已授 {report.Taught} 节 · 已取消 {report.Cancelled} 节 · 过去待确认 {report.Pending.Count} 节 · 今天及未来待上 {report.Planned} 节", 13), Ui.Text("反馈完成：" + feedback, 15, Ui.Accent))));
        var execution = Ui.Stack(Ui.Text("课程安排与执行 · " + (report.Days.Count <= 14 ? "按日" : report.Days.Count <= 93 ? "按周" : "按月"), 18, bold: true));
        var bars = new UniformGrid { Columns = report.Periods.Count, Margin = new Thickness(0, 14, 0, 0) }; var max = Math.Max(1, report.Periods.Max(p => p.Total));
        foreach (var period in report.Periods)
        {
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom }; stack.Children.Add(Ui.Text(period.Total.ToString(), 10, Ui.Muted));
            foreach (var (count, color) in new[] { (period.Planned, ChartBlue), (period.Pending, ChartAmber), (period.Cancelled, ChartGray), (period.Taught, ChartGreen) })
                if (count > 0) stack.Children.Add(new Border { Height = 100d * count / max, Background = color, Margin = new Thickness(2, 0, 2, 1) });
            var area = new Grid { Height = 128 }; area.Children.Add(stack); bars.Children.Add(Ui.Stack(area, Ui.Text(period.From.ToString("M/d"), 10, Ui.Muted)));
        }
        execution.Children.Add(bars); execution.Children.Add(Ui.Text("绿：已授课 · 灰：已取消 · 金：待确认 · 蓝：待上课", 11, Ui.Muted)); body.Children.Add(Ui.Card(execution));
        var ranking = Ui.Stack(Ui.Text("学生课时分布 · 匿名", 18, bold: true));
        if (report.Students.Count == 0) ranking.Children.Add(Ui.Text("所选范围暂无已授课记录。", 12, Ui.Muted));
        var maxMinutes = Math.Max(1, report.Students.Select(s => s.Minutes).DefaultIfEmpty().Max()); var number = 0;
        foreach (var student in report.Students.Take(10))
        { number++; ranking.Children.Add(DashboardBar("学生 " + number, student.Minutes, maxMinutes, $"{student.Count} 节 · {Hours(student.Minutes)} 小时", Ui.Accent)); }
        if (report.Students.Count > 10) ranking.Children.Add(Ui.Text($"其余 {report.Students.Count - 10} 位学生共 {Hours(report.Students.Skip(10).Sum(s => s.Minutes))} 小时", 12, Ui.Muted));
        body.Children.Add(Ui.Card(ranking));
        var needs = Ui.Stack(Ui.Text($"当前学习需求 · {report.ActiveStudents} 位在教学生", 18, bold: true), Ui.Text("当前档案标签，与日期范围无关；辅导重点可多选。", 11, Ui.Muted));
        var columns = new UniformGrid { Columns = 3, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var (title, values) in new[] { ("基础水平", report.Levels), ("学习节奏", report.Paces), ("辅导重点", report.Focuses) })
        { var panel = Ui.Stack(Ui.Text(title, 14, Ui.Accent, true)); foreach (var value in values) panel.Children.Add(Ui.Text($"{value.Label}：{value.Count} 人", 12)); columns.Children.Add(panel); }
        needs.Children.Add(columns); body.Children.Add(Ui.Card(needs));
        body.Children.Add(Ui.Text($"统计截至 {Today:yyyy/MM/dd} · 实际课时只含截至今天的已授课。\n仅分享汇总及匿名分布，不含学生姓名、学校、地点、反馈、图片、时薪、收入、账号或本机路径。", 11, Ui.Muted));
        var root = new Border { Width = 1040, Background = Ui.Page, Padding = new Thickness(28), Child = body };
        TextElement.SetFontFamily(root, new FontFamily("Microsoft YaHei UI, Segoe UI")); root.UseLayoutRounding = true; return root;
    }

    internal async Task CopyDashboardImageAsync(Button button)
    {
        button.IsEnabled = false;
        try
        {
            var root = BuildDashboardShareImage(); BitmapSource bitmap;
            try { bitmap = TutorImage.Render(root); } finally { RetireTree(root); }
            await TutorImage.CopyAsync(bitmap, WriteImageClipboard, _lifetime.Token);
            button.Content = "✓ 已复制图片"; _saved.Text = "已复制匿名教学概览，可粘贴分享";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is ExternalException or UnauthorizedAccessException or IOException or ArgumentException)
        { button.Content = "复制为图片"; _saved.Text = "图片复制未完成，请稍后重试"; }
        finally { button.IsEnabled = true; }
    }
}

internal static class TutorImage
{
    internal static BitmapSource Render(FrameworkElement root)
    {
        const double width = 1040; root.Measure(new Size(width, double.PositiveInfinity)); var height = Math.Max(1, Math.Ceiling(root.DesiredSize.Height));
        root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        var scale = Math.Min(1.5, Math.Min(16000 / height, Math.Sqrt(40_000_000 / (width * height))));
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(root); bitmap.Freeze(); return bitmap;
    }
    internal static async Task CopyAsync(BitmapSource bitmap, Action<DataObject> write, CancellationToken token)
    {
        using var png = new MemoryStream(); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(png); png.Position = 0;
        var data = new DataObject(); data.SetImage(bitmap); data.SetData("PNG", png);
        for (var attempt = 0; ; attempt++)
        { token.ThrowIfCancellationRequested(); try { write(data); return; } catch (ExternalException) when (attempt < 3) { await Task.Delay(100, token); } }
    }
}
