using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using DailyRecord.Core;

namespace DailyRecord.App;

public partial class MainWindow
{
    internal const double AnalyticsShareWidth = 1040;

    internal Border BuildAnalyticsShareImage()
    {
        // Build a detached report: exporting must not resize the live page, change its filters or write records.
        var data = new TaskAnalytics(_service.State, _service.Today);
        var from = AnalyticsRangeStart(data.Today, _analyticsPeriod); var to = data.Today;
        var days = data.Daily(from, to); var entries = data.Between(from, to);
        var body = new StackPanel();
        body.Children.Add(Ui.Text("E²-Day  ·  看见完成的轨迹", 13, Ui.Accent));
        var title = Ui.Text("我的数据概览", 32, bold: true); title.Margin = new Thickness(0, 12, 0, 8); body.Children.Add(title);
        var range = Ui.Text($"{from:yyyy/MM/dd} — {to:yyyy/MM/dd}  ·  " + new[] { "近 7 天", "近 30 天", "本月", "今年" }[_analyticsPeriod], 16, Ui.Accent, true);
        range.Tag = "analytics-share-range"; body.Children.Add(range);
        var intro = Ui.Text("完成统计与热力图按实际完成日 · 长期打卡按原安排日 · 积压单独标明当前状态", 11, Ui.Muted);
        intro.Margin = new Thickness(0, 9, 0, 22); body.Children.Add(intro);
        body.Children.Add(BuildAnalyticsMetrics(data, entries, sharing: true));
        var charts = new Grid(); charts.ColumnDefinitions.Add(new ColumnDefinition());
        charts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        var trend = BuildCompletionTrend(data, from, to, sharing: true); trend.Margin = new Thickness(0, 0, 14, 14);
        charts.Children.Add(trend); var backlog = BuildBacklog(data, sharing: true); Grid.SetColumn(backlog, 1); charts.Children.Add(backlog); body.Children.Add(charts);
        body.Children.Add(AnalyticsSection("完成热力图 · 所选范围", $"共完成 {entries.Count} 项 · {days.Count(d => d.Count > 0)} 天有完成记录",
            ShareDayGrid(days, "analytics-share-heat:", habit: false)));
        var selected = data.Habits.FirstOrDefault(h => h.Definition.Id == _analyticsHabitId) ?? data.Habits.FirstOrDefault();
        if (selected is null)
            body.Children.Add(AnalyticsSection("长期任务打卡 · 所选范围", "按原安排日记一次", Ui.Text("当前没有长期任务打卡记录。", 12, Ui.Muted)));
        else
        {
            var points = days.Select(d => new CompletionDay(d.Day, selected.CompletedDays.ContainsKey(d.Day) ? 1 : 0, 0)).ToArray();
            var completed = points.Sum(d => d.Count); var longest = 0; var run = 0;
            foreach (var point in points) { run = point.Count > 0 ? run + 1 : 0; longest = Math.Max(longest, run); }
            var heading = Ui.Text(selected.Definition.Title, 16, Ui.Accent, true); heading.Tag = "analytics-share-habit-name";
            heading.Margin = new Thickness(0, 0, 0, 10);
            var numbers = Ui.Row(Ui.Badge($"本期打卡 {completed} 天"), Ui.Badge($"区间最长连续 {longest} 天"));
            numbers.Tag = "analytics-share-habit-summary"; numbers.Margin = new Thickness(0, 0, 0, 14);
            var note = Ui.Text("空白表示暂无完成记录；补做归入原安排日。此处只展示当前选中的长期任务。", 10, Ui.Muted);
            note.Margin = new Thickness(0, 10, 0, 0);
            body.Children.Add(AnalyticsSection("长期任务打卡 · 所选范围", $"{from:yyyy/M/d} — {to:yyyy/M/d}" + (selected.Ended ? " · 已结束，记录保留" : ""),
                Ui.Stack(heading, numbers, ShareDayGrid(points, "analytics-share-habit:", habit: true), note)));
        }
        var footer = Ui.Text($"E²-Day · 探索每一天    统计截至 {to:yyyy/MM/dd}\n看过提醒、取消和结束不算完成；已撤销完成不计入。", 11, Ui.Muted);
        footer.Tag = "analytics-share-footer"; footer.Margin = new Thickness(2, 6, 2, 0); body.Children.Add(footer);
        var root = new Border { Width = AnalyticsShareWidth, Background = Ui.Page, Padding = new Thickness(28), Child = body };
        TextElement.SetFontFamily(root, new FontFamily("Microsoft YaHei UI, Segoe UI"));
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Display); root.UseLayoutRounding = true;
        return root;
    }

    private static StackPanel ShareDayGrid(IReadOnlyList<CompletionDay> days, string tagPrefix, bool habit)
    {
        var grid = new Grid(); var from = days[0].Day; var offset = ((int)from.DayOfWeek + 6) % 7;
        var compact = days.Count > 31;
        if (compact)
        {
            var weeks = (offset + days.Count + 6) / 7;
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            for (var i = 0; i < weeks; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            for (var i = 0; i < 7; i++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
            foreach (var row in new[] { 0, 2, 4, 6 })
            { var label = Ui.Text(new[] { "一", "二", "三", "四", "五", "六", "日" }[row], 10, Ui.Muted); Grid.SetRow(label, row + 1); grid.Children.Add(label); }
            foreach (var point in days.Where(d => d.Day.Day == 1 || d.Day == from))
            {
                var label = Ui.Text(point.Day.ToString("M月"), 10, Ui.Muted); label.TextWrapping = TextWrapping.NoWrap;
                Grid.SetColumn(label, 1 + (offset + point.Day.DayNumber - from.DayNumber) / 7); Grid.SetColumnSpan(label, 3); grid.Children.Add(label);
            }
        }
        else
        {
            for (var i = 0; i < 7; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            foreach (var column in Enumerable.Range(0, 7))
            { var label = Ui.Text(new[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" }[column], 10, Ui.Muted); label.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetColumn(label, column); grid.Children.Add(label); }
            for (var i = 0; i < (offset + days.Count + 6) / 7; i++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        }
        foreach (var point in days)
        {
            var index = offset + point.Day.DayNumber - from.DayNumber;
            var color = habit ? (point.Count > 0 ? HeatColors[3] : HeatColors[0]) : HeatColor(point.Count);
            var cell = new Border { Background = color, CornerRadius = new CornerRadius(4), Margin = new Thickness(2),
                Tag = tagPrefix + point.Day.ToString("yyyy-MM-dd") };
            if (!compact)
            {
                var foreground = point.Count >= 4 || habit && point.Count > 0 ? Brushes.White : Ui.Ink;
                var label = Ui.Text(point.Day.ToString("M/d"), 10, foreground);
                var count = Ui.Text(habit ? point.Count > 0 ? "✓" : "—" : point.Count + " 项", 13, foreground, true);
                var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(label); Grid.SetColumn(count, 1); row.Children.Add(count); cell.Child = row; cell.Padding = new Thickness(9, 4, 9, 4);
            }
            Grid.SetColumn(cell, compact ? 1 + index / 7 : index % 7); Grid.SetRow(cell, compact ? 1 + index % 7 : 1 + index / 7); grid.Children.Add(cell);
        }
        var legend = new WrapPanel { Margin = new Thickness(2, 10, 0, 0) };
        foreach (var item in habit ? new[] { (0, "暂无完成记录"), (4, "已完成") } : new[] { (0, "0 项"), (1, "1 项"), (2, "2–3 项"), (4, "4–6 项"), (7, "7+ 项") })
        {
            var swatch = new Border { Width = 12, Height = 12, Background = habit && item.Item1 > 0 ? HeatColors[3] : HeatColor(item.Item1),
                CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 5, 0) };
            var entry = Ui.Row(swatch, Ui.Text(item.Item2, 10, Ui.Muted)); entry.Margin = new Thickness(0, 0, 16, 0); legend.Children.Add(entry);
        }
        return Ui.Stack(grid, legend);
    }
}
