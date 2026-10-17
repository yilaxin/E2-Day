using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DailyRecord.Core;

namespace DailyRecord.App;

public partial class MainWindow
{
    private int _analyticsPeriod = 1;
    private int? _analyticsYear;
    private DateOnly? _analyticsHabitMonth;
    private string? _analyticsHabitId;
    internal Action<Window>? AnalyticsDetailsPresenter { get; set; }
    private static readonly Brush[] HeatColors = [Ui.SubtleSurface, Heat("#DED4F0"), Heat("#BBA5DC"), Heat("#9373C4"), Heat("#67459E")];
    private static Brush Heat(string hex) { var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!; brush.Freeze(); return brush; }
    private static Brush HeatColor(int count) => HeatColors[count switch { <= 0 => 0, 1 => 1, <= 3 => 2, <= 6 => 3, _ => 4 }];

    private void BuildAnalytics()
    {
        var today = _service.Today;
        _analyticsYear ??= today.Year; _analyticsHabitMonth ??= new DateOnly(today.Year, today.Month, 1);
        var data = new TaskAnalytics(_service.State, today);
        var from = _analyticsPeriod switch
        {
            0 => today.AddDays(-6), 1 => today.AddDays(-29), 2 => new DateOnly(today.Year, today.Month, 1),
            _ => new DateOnly(today.Year, 1, 1)
        };
        var entries = data.Between(from, today);
        Header("每一份完成，都有迹可循", "数据概览", "看见完成的轨迹，整理积压的事情，记录长期任务的坚持。");
        var filters = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
        var options = new[] { "近 7 天", "近 30 天", "本月", "今年" };
        for (var i = 0; i < options.Length; i++)
        {
            var period = i; var button = Ui.Button(options[i], () => { _analyticsPeriod = period; RefreshPage(); });
            button.Tag = "analytics-range:" + i; button.FontSize = 12;
            if (i == _analyticsPeriod) { button.Background = Ui.Selected; button.Foreground = Ui.Accent; button.FontWeight = FontWeights.SemiBold; }
            filters.Children.Add(button);
        }
        var range = Ui.Text($"{from:M/d} — {today:M/d}  ·  上方统计与趋势范围", 11, Ui.Muted);
        range.Margin = new Thickness(10, 0, 0, 0); filters.Children.Add(range); PageBody.Children.Add(filters);

        var metrics = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 16) };
        var numbers = new[] { ("本期完成", entries.Count, "项任务", "analytics-total"),
            ("其中重要", entries.Count(c => c.WasImportant), "以完成时的星标为准", "analytics-important"),
            ("当前逾期", data.OverdueCount, "不限上方时间范围", "analytics-overdue"),
            ("有完成记录", entries.Select(c => c.Day).Distinct().Count(), "天 · 在所选时间范围内", "analytics-days") };
        for (var i = 0; i < numbers.Length; i++)
        {
            var metric = numbers[i]; var value = Ui.Text(metric.Item2.ToString(), 29, i == 2 ? Ui.Amber : Ui.Accent, true);
            value.Tag = metric.Item4;
            var card = Ui.Card(Ui.Stack(Ui.Text(metric.Item1, 12, Ui.Muted), value, Ui.Text(metric.Item3, 10, Ui.Muted)), new Thickness(17, 14, 17, 14));
            card.Margin = new Thickness(0, 0, i == 3 ? 0 : 12, 0); metrics.Children.Add(card);
        }
        PageBody.Children.Add(metrics);
        var trend = BuildCompletionTrend(data, from, today);
        var backlog = BuildBacklog(data);
        if (_wide)
        {
            var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition());
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
            trend.Margin = new Thickness(0, 0, 14, 14); columns.Children.Add(trend); Grid.SetColumn(backlog, 1); columns.Children.Add(backlog);
            PageBody.Children.Add(columns);
        }
        else { PageBody.Children.Add(trend); PageBody.Children.Add(backlog); }
        PageBody.Children.Add(BuildCompletionHeatmap(data));
        PageBody.Children.Add(BuildHabitCalendar(data));
        var note = Ui.Text("统计来自当前账号的已有记录。看过提醒、取消和结束任务不算完成；撤销完成后相应统计会减少。热力图年份与打卡月份可单独切换。", 11, Ui.Muted);
        note.Margin = new Thickness(2, 0, 2, 12); PageBody.Children.Add(note);
    }

    private static Border AnalyticsSection(string title, string subtitle, UIElement content, UIElement? actions = null)
    {
        var header = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(Ui.Stack(Ui.Text(title, 17, bold: true), Ui.Text(subtitle, 11, Ui.Muted)));
        if (actions is not null) { Grid.SetColumn(actions, 1); header.Children.Add(actions); }
        return Ui.Card(Ui.Stack(header, content), new Thickness(20));
    }

    private Border BuildCompletionTrend(TaskAnalytics data, DateOnly from, DateOnly to)
    {
        var days = data.Daily(from, to);
        var bins = _analyticsPeriod == 3
            ? days.GroupBy(d => new DateOnly(d.Day.Year, d.Day.Month, 1)).Select(g => (Day: g.Key, End: g.Last().Day, Count: g.Sum(d => d.Count))).ToArray()
            : days.Select(d => (d.Day, End: d.Day, d.Count)).ToArray();
        var max = Math.Max(1, bins.Max(b => b.Count));
        var chart = new Grid { Height = 156 };
        for (var i = 0; i < bins.Length; i++)
        {
            var bin = bins[i]; chart.ColumnDefinitions.Add(new ColumnDefinition());
            var bars = new Grid(); bars.RowDefinitions.Add(new RowDefinition()); bars.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            var column = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(2, 0, 2, 0) };
            if (bin.Count > 0)
            { var count = Ui.Text(bin.Count.ToString(), 10, Ui.Accent, true); count.HorizontalAlignment = HorizontalAlignment.Center; count.TextWrapping = TextWrapping.NoWrap; column.Children.Add(count); }
            column.Children.Add(new Border { Height = bin.Count == 0 ? 3 : Math.Max(5, 105.0 * bin.Count / max), CornerRadius = new CornerRadius(4, 4, 0, 0),
                Background = bin.Count == 0 ? Ui.Selected : (Brush)Application.Current.FindResource("ActionBrush") });
            bars.Children.Add(column);
            var showLabel = bins.Length <= 12 || i % 5 == 0 || i == bins.Length - 1;
            var label = Ui.Text(showLabel ? bin.Day.ToString(_analyticsPeriod == 3 ? "M月" : "M/d") : "", 9, Ui.Muted);
            label.TextWrapping = TextWrapping.NoWrap; label.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetRow(label, 1); bars.Children.Add(label);
            var button = Ui.Button("", () => ShowAnalyticsDetails($"{bin.Day:yyyy/M/d}" + (bin.End != bin.Day ? $" — {bin.End:M/d}" : "") + " · 完成事项",
                () => new TaskAnalytics(_service.State, _service.Today).Between(bin.Day, bin.End).Select(c => c.Task).ToArray(), "按实际完成日期筛选，下方显示事项当前内容与状态。"));
            button.Content = bars; button.Padding = new Thickness(0); button.Margin = new Thickness(0); button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent; button.BorderThickness = new Thickness(0);
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.VerticalContentAlignment = VerticalAlignment.Stretch;
            button.ToolTip = $"{bin.Day:yyyy/M/d}" + (bin.End != bin.Day ? $" — {bin.End:M/d}" : "") + $"：实际完成 {bin.Count} 项，点击查看";
            button.Tag = "analytics-trend:" + bin.Day.ToString("yyyy-MM-dd");
            System.Windows.Automation.AutomationProperties.SetName(button, button.ToolTip.ToString());
            Grid.SetColumn(button, i); chart.Children.Add(button);
        }
        var caption = Ui.Text(days.Sum(d => d.Count) == 0 ? "这段时间还没有完成记录。完成一件任务后，这里就会留下轨迹。" : "点击柱形查看对应事项；同一任务反复完成与撤销不会重复计数。", 10, Ui.Muted);
        caption.Margin = new Thickness(0, 10, 0, 0);
        return AnalyticsSection("完成趋势", _analyticsPeriod == 3 ? "今年按月汇总 · 截至今天" : "每天实际完成的任务数量", Ui.Stack(chart, caption));
    }

    private Border BuildBacklog(TaskAnalytics data)
    {
        var panel = new StackPanel(); var maximum = Math.Max(1, data.Backlog.Max(g => g.Tasks.Count));
        foreach (var group in data.Backlog)
        {
            var heading = new Grid(); heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading.Children.Add(Ui.Text(group.Label, 11, group.Key == "today" ? Ui.Ink : Ui.Amber));
            var count = Ui.Text(group.Tasks.Count + " 项", 12, bold: true); Grid.SetColumn(count, 1); heading.Children.Add(count);
            var bar = new Grid { Height = 6, Margin = new Thickness(0, 6, 0, 0), Background = Ui.Selected };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(group.Tasks.Count, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(maximum - group.Tasks.Count, GridUnitType.Star) });
            bar.Children.Add(new Border { Background = group.Key == "today" ? Ui.Accent : Ui.ImportantLine, CornerRadius = new CornerRadius(3) });
            var button = Ui.Button("", () => ShowAnalyticsDetails(group.Label,
                () => new TaskAnalytics(_service.State, _service.Today).Backlog.Single(g => g.Key == group.Key).Tasks, "当前已到期的未完成事项；完成或改期后会重新统计。"));
            button.Content = Ui.Stack(heading, bar); button.Tag = "analytics-backlog:" + group.Key;
            button.Background = Brushes.Transparent; button.BorderBrush = Brushes.Transparent; button.Padding = new Thickness(0, 3, 0, 5);
            button.Margin = new Thickness(0, 0, 0, 4); button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.ToolTip = $"{group.Label}：{group.Tasks.Count} 项，点击查看并处理"; panel.Children.Add(button);
        }
        panel.Children.Add(Ui.Text("当前存量 · 不受顶部时间筛选影响\n未来提醒与过去的长期任务不计入积压。", 10, Ui.Muted));
        return AnalyticsSection("待办与积压", $"到期未完成 {data.Backlog.Sum(g => g.Tasks.Count)} 项 · 点击分组查看", panel);
    }

    private Border BuildCompletionHeatmap(TaskAnalytics data)
    {
        var year = _analyticsYear!.Value; var first = new DateOnly(year, 1, 1); var last = new DateOnly(year, 12, 31);
        var counts = data.Daily(first, last).ToDictionary(d => d.Day); var offset = ((int)first.DayOfWeek + 6) % 7;
        var columns = (offset + last.DayNumber - first.DayNumber + 7) / 7;
        var grid = new Grid { MinWidth = 650 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        for (var column = 0; column < columns; column++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(23) });
        for (var row = 0; row < 7; row++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(21) });
        for (var month = 1; month <= 12; month++)
        {
            var label = Ui.Text(month + "月", 10, Ui.Muted); label.TextWrapping = TextWrapping.NoWrap;
            Grid.SetColumn(label, 1 + (offset + new DateOnly(year, month, 1).DayNumber - first.DayNumber) / 7); Grid.SetColumnSpan(label, 3);
            grid.Children.Add(label);
        }
        foreach (var row in new[] { 0, 2, 4, 6 })
        { var label = Ui.Text(new[] { "一", "二", "三", "四", "五", "六", "日" }[row], 9, Ui.Muted); Grid.SetRow(label, row + 1); grid.Children.Add(label); }
        foreach (var point in counts.Values)
        {
            var day = point.Day; var index = offset + day.DayNumber - first.DayNumber;
            var button = new Button { Style = (Style)Application.Current.FindResource("AnalyticsHeatCell"), Background = HeatColor(point.Count),
                Margin = new Thickness(2), Tag = "analytics-heat:" + day.ToString("yyyy-MM-dd"), IsEnabled = day <= data.Today,
                ToolTip = day > data.Today ? $"{day:yyyy/M/d} · 未来日期" : $"{day:yyyy/M/d} · 实际完成 {point.Count} 项；点击查看" };
            button.Click += (_, _) => ShowAnalyticsDetails($"{day:yyyy/M/d} · 完成事项",
                () => new TaskAnalytics(_service.State, _service.Today).Between(day, day).Select(c => c.Task).ToArray(), "按实际完成日回看；看过提醒不计入完成数量。");
            System.Windows.Automation.AutomationProperties.SetName(button, button.ToolTip.ToString());
            ToolTipService.SetShowOnDisabled(button, true);
            Grid.SetColumn(button, 1 + index / 7); Grid.SetRow(button, 1 + index % 7); grid.Children.Add(button);
        }
        var legend = new WrapPanel { Margin = new Thickness(24, 10, 0, 0) };
        foreach (var pair in new[] { (0, "0 项"), (1, "1 项"), (2, "2–3 项"), (4, "4–6 项"), (7, "7+ 项") })
        {
            var swatch = new Border { Width = 12, Height = 12, Background = HeatColor(pair.Item1), CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 5, 0) };
            var item = Ui.Row(swatch, Ui.Text(pair.Item2, 10, Ui.Muted)); item.Margin = new Thickness(0, 0, 14, 0); legend.Children.Add(item);
        }
        var navigation = Ui.Row(Ui.Button("‹", () => { _analyticsYear--; RefreshPage(); }), Ui.Text(year + " 年", 12, bold: true), Ui.Button("›", () => { _analyticsYear++; RefreshPage(); }));
        ((Button)navigation.Children[0]).IsEnabled = year > 1901; ((Button)navigation.Children[2]).IsEnabled = year < 2100;
        ((FrameworkElement)navigation.Children[1]).Margin = new Thickness(4, 0, 12, 0);
        ((Button)navigation.Children[0]).Tag = "analytics-year-previous"; ((Button)navigation.Children[2]).Tag = "analytics-year-next";
        navigation.Children.Add(Ui.Button("今年", () => { _analyticsYear = data.Today.Year; RefreshPage(); }));
        var scroll = new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        return AnalyticsSection("完成热力图", $"全年完成 {counts.Values.Sum(d => d.Count)} 项 · {counts.Values.Count(d => d.Count > 0)} 天有完成记录", Ui.Stack(scroll, legend), navigation);
    }

    private sealed record HabitChoice(string Id, string Label) { public override string ToString() => Label; }
    private Border BuildHabitCalendar(TaskAnalytics data)
    {
        if (data.Habits.Count == 0)
            return AnalyticsSection("长期任务打卡", "记录每天的坚持", Ui.Text("还没有长期任务。添加任务时勾选“长期任务”，之后可在这里查看每日完成记录。", 12, Ui.Muted));
        var habit = data.Habits.FirstOrDefault(h => h.Definition.Id == _analyticsHabitId) ?? data.Habits[0];
        _analyticsHabitId = habit.Definition.Id;
        var month = _analyticsHabitMonth!.Value;
        var choices = data.Habits.Select(h => new HabitChoice(h.Definition.Id, h.Definition.Title + (h.Ended ? " · 已结束" : h.Definition.StartDate > data.Today ? " · 尚未开始" : ""))).ToArray();
        var picker = new ComboBox { ItemsSource = choices, SelectedItem = choices.Single(c => c.Id == _analyticsHabitId), MinWidth = 180,
            MaxWidth = 480, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12), Tag = "analytics-habit-picker" };
        picker.SelectionChanged += (_, _) => { if (picker.SelectedItem is HabitChoice selected) { _analyticsHabitId = selected.Id; RefreshPage(); } };
        var calendar = new UniformGrid { Columns = 7 };
        foreach (var weekday in new[] { "一", "二", "三", "四", "五", "六", "日" })
        { var text = Ui.Text(weekday, 10, Ui.Muted); text.HorizontalAlignment = HorizontalAlignment.Center; text.Margin = new Thickness(0, 0, 0, 7); calendar.Children.Add(text); }
        var offset = ((int)month.DayOfWeek + 6) % 7; var dayCount = DateTime.DaysInMonth(month.Year, month.Month);
        for (var index = 0; index < 42; index++)
        {
            var number = index - offset + 1;
            if (number < 1 || number > dayCount) { calendar.Children.Add(new Border { Height = 34 }); continue; }
            var day = new DateOnly(month.Year, month.Month, number); var done = habit.CompletedDays.ContainsKey(day);
            var inactive = day < habit.Definition.StartDate || day > habit.Definition.EndDate;
            var future = day > data.Today;
            var canCompleteToday = day == data.Today && !inactive && data.Backlog[0].Tasks.Any(t => t.EventId == habit.Definition.Id && t.OccurrenceDate == day);
            var hint = done ? (future ? "已提前完成" : "已完成") : canCompleteToday ? "今天待完成 · 点击打卡" : future ? "未来日期" : inactive ? "不在当前任务起止范围内" : "暂无完成记录";
            var button = Ui.Button(number + (done ? " ✓" : ""), () => ShowAnalyticsDetails($"{habit.Definition.Title} · {day:yyyy/M/d}",
                () => HabitDayTasks(habit.Definition.Id, day),
                "打卡按任务原安排日归属，补做仍计入原安排日；下方事项显示实际完成日期。"));
            button.FontSize = 11; button.Height = 32; button.Margin = new Thickness(2); button.Padding = new Thickness(2);
            button.Background = done ? HeatColors[3] : inactive || future ? Ui.BlankDay : Ui.SubtleSurface;
            button.Foreground = done ? Brushes.White : Ui.Muted; button.IsEnabled = done || canCompleteToday;
            button.Opacity = !done && (inactive || future) ? 0.45 : 1;
            button.BorderBrush = day == data.Today ? Ui.Accent : Ui.Line;
            button.ToolTip = $"{day:yyyy/M/d} · {hint}"; ToolTipService.SetShowOnDisabled(button, true);
            button.Tag = "analytics-habit:" + day.ToString("yyyy-MM-dd"); calendar.Children.Add(button);
        }
        var summary = new StackPanel { Margin = new Thickness(24, 0, 0, 0) };
        void Stat(string label, int value, string tag)
        {
            var number = Ui.Text(value + " 天", 22, Ui.Accent, true); number.Tag = tag;
            var stat = Ui.Stack(Ui.Text(label, 11, Ui.Muted), number); stat.Margin = new Thickness(0, 0, 0, 12); summary.Children.Add(stat);
        }
        Stat("所选月份完成", habit.CompletedInMonth(month, data.Today), "analytics-habit-month-count");
        Stat("当前连续", habit.CurrentStreak, "analytics-habit-current"); Stat("最长连续", habit.LongestStreak, "analytics-habit-longest");
        summary.Children.Add(Ui.Text(habit.Ended ? "已结束 · 完成记录保留" : habit.Definition.StartDate > data.Today ? $"{habit.Definition.StartDate:yyyy/M/d} 开始" : "今天尚未完成时，从昨天续算；\n最长连续按全部已有记录计算。", 10, Ui.Muted));
        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition()); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        columns.Children.Add(calendar); Grid.SetColumn(summary, 1); columns.Children.Add(summary);
        var nav = Ui.Row(Ui.Button("‹", () => { _analyticsHabitMonth = month.AddMonths(-1); RefreshPage(); }), Ui.Text(month.ToString("yyyy 年 M 月"), 12, bold: true),
            Ui.Button("›", () => { _analyticsHabitMonth = month.AddMonths(1); RefreshPage(); }));
        ((Button)nav.Children[0]).IsEnabled = month > new DateOnly(1901, 1, 1); ((Button)nav.Children[2]).IsEnabled = month < new DateOnly(2100, 12, 1);
        ((Button)nav.Children[0]).Tag = "analytics-habit-previous"; ((Button)nav.Children[2]).Tag = "analytics-habit-next";
        ((FrameworkElement)nav.Children[1]).Margin = new Thickness(4, 0, 12, 0);
        nav.Children.Add(Ui.Button("本月", () => { _analyticsHabitMonth = new DateOnly(data.Today.Year, data.Today.Month, 1); RefreshPage(); }));
        return AnalyticsSection("长期任务打卡", "按原安排日记一次 · 补做不重复计天数 · 未记录留空", Ui.Stack(picker, columns), nav);
    }

    private IReadOnlyList<TaskItem> HabitDayTasks(string eventId, DateOnly day)
    {
        var report = new TaskAnalytics(_service.State, _service.Today);
        var completed = report.Habits.FirstOrDefault(h => h.Definition.Id == eventId)?.CompletedDays.GetValueOrDefault(day) ?? [];
        var pending = day == _service.Today ? report.Backlog[0].Tasks.Where(t => t.EventId == eventId && t.OccurrenceDate == day) : [];
        return completed.Concat(pending).DistinctBy(t => t.Id).ToArray();
    }

    private void ShowAnalyticsDetails(string title, Func<IReadOnlyList<TaskItem>> query, string explanation)
    {
        var dialog = new Window { Owner = IsVisible ? this : null, Title = title, Width = 820, Height = 700, MinWidth = 660, MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new StackPanel { Margin = new Thickness(26) };
        void RenderDetails()
        {
            root.Children.Clear(); root.Children.Add(Ui.Text(title, 23, bold: true));
            var description = Ui.Text(explanation, 12, Ui.Muted); description.Margin = new Thickness(0, 8, 0, 16); root.Children.Add(description);
            var tasks = query(); root.Children.Add(Ui.Label($"共 {tasks.Count} 项"));
            if (tasks.Count == 0) root.Children.Add(Ui.Card(Ui.Text("这里暂时没有对应的事项。", 13, Ui.Muted)));
            foreach (var task in TaskService.Sort(tasks)) root.Children.Add(TaskRow(task, refresh: RenderDetails));
        }
        dialog.Content = new ScrollViewer { Content = root }; RenderDetails();
        if (AnalyticsDetailsPresenter is { } present) present(dialog); else dialog.ShowDialog();
    }
}
