using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DailyRecord.Core;

namespace DailyRecord.App;

public partial class MainWindow
{
    private DateOnly? _selectedMonth;
    private DateOnly? _renderedMonth;
    private long _monthDataRevision = -1;
    private long _monthHolidayRevision = -1;
    private DateOnly _monthToday;
    private TextBlock? _monthCount;
    private TextBlock? _monthNotice;
    private readonly Dictionary<DateOnly, MonthDayView> _monthDays = [];

    private void ShowMonth(DateOnly month)
    {
        _selectedMonth = new(month.Year, month.Month, 1); _year = month.Year;
        MonthScroller.ScrollToTop(); RefreshPage();
    }

    private void ShiftMonth(int offset)
    {
        var current = _selectedMonth!.Value;
        if (offset < 0 && current == new DateOnly(1901, 1, 1) || offset > 0 && current == new DateOnly(2100, 12, 1)) return;
        ShowMonth(current.AddMonths(offset));
    }

    private void BuildMonth()
    {
        var month = _selectedMonth!.Value;
        Header("细看这个月的安排", $"{month.Year} 年 {month.Month} 月", "日期下面显示任务标题。重要任务优先，已完成淡化保留；点击日期查看全部或编辑安排。");
        var back = Ui.Button("‹ 全年总览", () => Navigate(PageKind.Year)); back.Tag = "back-year";
        var previous = Ui.Button("‹", () => ShiftMonth(-1)); previous.Tag = "previous-month";
        previous.ToolTip = "上个月"; previous.IsEnabled = month != new DateOnly(1901, 1, 1);
        var current = Ui.Button("本月", () => ShowMonth(new(_service.Today.Year, _service.Today.Month, 1))); current.Tag = "current-month";
        var next = Ui.Button("›", () => ShiftMonth(1)); next.Tag = "next-month";
        next.ToolTip = "下个月"; next.IsEnabled = month != new DateOnly(2100, 12, 1);
        HeaderActions.Children.Add(back); HeaderActions.Children.Add(previous); HeaderActions.Children.Add(current); HeaderActions.Children.Add(next);
        HeaderActions.Children.Add(Ui.Button("＋ 安排事件", () => OpenEventEditor(month.Year == _service.Today.Year && month.Month == _service.Today.Month ? _service.Today : month), accent: true));

        if (_renderedMonth != month)
        {
            _renderedMonth = month; _monthDataRevision = -1; _monthHolidayRevision = -1; _monthDays.Clear();
            var body = new StackPanel();
            _monthCount = Ui.Text("", 11, Ui.Muted);
            var legend = Ui.Row(Ui.Badge("今天"), Ui.Badge("★ 重要任务", true), Ui.Badge("✓ 已完成"), _monthCount);
            legend.Margin = new Thickness(0, 0, 0, 16); body.Children.Add(legend);
            _monthNotice = Ui.Text($"{month.Year} 年放假调休数据尚未更新，农历和常见节日仍可查看。", 11, Ui.Amber);
            _monthNotice.Margin = new Thickness(0, 0, 0, 12); body.Children.Add(_monthNotice);
            var calendar = new StackPanel();
            var weekdays = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 8) };
            foreach (var weekday in new[] { "星期一", "星期二", "星期三", "星期四", "星期五", "星期六", "星期日" })
            {
                var label = Ui.Text(weekday, 11, Ui.Muted); label.HorizontalAlignment = HorizontalAlignment.Center;
                weekdays.Children.Add(label);
            }
            calendar.Children.Add(weekdays);
            var count = DateTime.DaysInMonth(month.Year, month.Month);
            var offset = ((int)month.DayOfWeek + 6) % 7;
            var rows = (offset + count + 6) / 7;
            var days = new UniformGrid { Columns = 7, Rows = rows };
            for (var index = 0; index < rows * 7; index++)
            {
                var number = index - offset + 1;
                if (number < 1 || number > count)
                {
                    days.Children.Add(new Border { Height = 132, Margin = new Thickness(2), CornerRadius = new CornerRadius(8), Background = Ui.BlankDay });
                    continue;
                }
                days.Children.Add(MonthDayButton(new(month.Year, month.Month, number)));
            }
            calendar.Children.Add(days);
            var card = Ui.Card(calendar, new Thickness(12)); card.Margin = new Thickness(0); body.Children.Add(card);
            MonthHost.Child = body;
        }
        RefreshMonthData();
    }

    private Button MonthDayButton(DateOnly date)
    {
        var heading = new Grid { Height = 21 };
        heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var number = Ui.Text(date.Day.ToString(), 16, bold: true); heading.Children.Add(number);
        var count = Ui.Text("", 9, Ui.Muted); Grid.SetColumn(count, 1); heading.Children.Add(count);
        var information = Ui.Text("", 9, Ui.Muted);
        information.TextWrapping = TextWrapping.NoWrap; information.TextTrimming = TextTrimming.CharacterEllipsis;
        var tasks = new StackPanel();
        var button = Ui.Button("", () => ShowDate(date)); button.Content = Ui.Stack(heading, information, tasks);
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.VerticalContentAlignment = VerticalAlignment.Top;
        button.Height = 132; button.Padding = new Thickness(8); button.Margin = new Thickness(2);
        button.Tag = "month-date:" + date.ToString("yyyy-MM-dd");
        _monthDays[date] = new(date, button, number, count, information, tasks);
        return button;
    }

    private void RefreshMonthData()
    {
        if (_monthDataRevision == _service.Revision && _monthHolidayRevision == _holidays.Revision && _monthToday == _service.Today) return;
        var month = _selectedMonth!.Value;
        var summaries = _service.CalendarForYear(month.Year);
        foreach (var pair in _monthDays)
        {
            var day = pair.Value;
            if (_monthHolidayRevision != _holidays.Revision || day.Information is null) day.Information = _holidays.ForDay(pair.Key);
            UpdateMonthDay(day, summaries.GetValueOrDefault(pair.Key, CalendarDaySummary.Empty));
        }
        var total = summaries.Where(p => p.Key.Month == month.Month).Sum(p => p.Value.ScheduledCount);
        _monthCount!.Text = $"本月 {total} 项安排 · 每天预览前三项";
        _monthNotice!.Visibility = _holidays.HasYear(month.Year) ? Visibility.Collapsed : Visibility.Visible;
        _monthDataRevision = _service.Revision; _monthHolidayRevision = _holidays.Revision; _monthToday = _service.Today;
    }

    private void UpdateMonthDay(MonthDayView day, CalendarDaySummary summary)
    {
        var today = day.Date == _service.Today; var info = day.Information!;
        if (day.LastInformation == info && day.WasToday == today && day.Previews is not null && day.Previews.SequenceEqual(summary.Tasks)) return;
        day.LastInformation = info; day.WasToday = today; day.Previews = summary.Tasks;
        day.Number.Foreground = today || info.IsOffDay ? Ui.Accent : Ui.Ink;
        var status = info.WorkStatus.StartsWith("调休") ? "班" : info.WorkStatus.StartsWith("法定") ? "休" : "";
        day.Count.Text = status + (summary.ScheduledCount > 0 ? $"  {summary.ScheduledCount} 项" : "");
        day.Caption.Text = info.Festivals.Length > 0 ? info.Festivals : info.Lunar.Replace("农历", "");
        day.Button.Background = today ? Ui.Selected : summary.HasImportantTask ? Ui.ImportantFill : info.IsOffDay ? Ui.SubtleSurface : Ui.Surface;
        day.Button.BorderBrush = today ? Ui.Accent : summary.HasImportantTask ? Ui.ImportantLine : Ui.Line;
        day.Tasks.Children.Clear();
        foreach (var task in summary.Tasks.Take(3))
        {
            var completed = task.State == TaskState.Completed;
            var prefix = (task.IsImportant ? "★ " : "") + (completed ? "✓ " : task.IsImportant ? "" : "· ");
            var label = Ui.Text(prefix + task.Title, 11, task.IsImportant ? Ui.Amber : Ui.Ink);
            label.Tag = "month-task"; label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.Margin = new Thickness(0, 4, 0, 0); label.ToolTip = task.Title;
            if (completed) { label.Opacity = 0.55; label.TextDecorations = TextDecorations.Strikethrough; }
            day.Tasks.Children.Add(label);
        }
        if (summary.Tasks.Count > 3)
        {
            var more = Ui.Text($"还有 {summary.Tasks.Count - 3} 项 · 点击查看", 9, Ui.Accent);
            more.Margin = new Thickness(0, 4, 0, 0); more.Tag = "month-more"; day.Tasks.Children.Add(more);
        }
        day.Button.ToolTip = $"{day.Date:yyyy-MM-dd dddd}\n{info.Lunar}\n{info.Festivals}\n{info.WorkStatus}" +
            (summary.Tasks.Count > 0 ? "\n" + string.Join("\n", summary.Tasks.Select(t => (t.IsImportant ? "★ " : "") + (t.State == TaskState.Completed ? "✓ " : "") + t.Title)) : "\n暂无任务") + "\n点击查看或编辑当天安排";
    }

    private sealed class MonthDayView(DateOnly date, Button button, TextBlock number, TextBlock count, TextBlock caption, StackPanel tasks)
    {
        public DateOnly Date { get; } = date;
        public Button Button { get; } = button;
        public TextBlock Number { get; } = number;
        public TextBlock Count { get; } = count;
        public TextBlock Caption { get; } = caption;
        public StackPanel Tasks { get; } = tasks;
        public DayInformation? Information { get; set; }
        public DayInformation? LastInformation { get; set; }
        public IReadOnlyList<CalendarTaskPreview>? Previews { get; set; }
        public bool WasToday { get; set; }
    }
}
