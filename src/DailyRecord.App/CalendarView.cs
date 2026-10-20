using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DailyRecord.Core;

namespace DailyRecord.App;

public partial class MainWindow
{
    private StackPanel? _calendarBody;
    private UniformGrid? _calendarMonths;
    private TextBlock? _calendarNotice;
    private int _calendarYear;
    private long _calendarDataRevision = -1;
    private long _calendarHolidayRevision = -1;
    private DateOnly _calendarToday;
    private readonly Dictionary<DateOnly, CalendarDayView> _calendarDays = [];
    private readonly Dictionary<int, TextBlock> _calendarMonthCounts = [];

    private void BuildYear()
    {
        Header("全年安排，一眼可见", $"{_year} 年", "点击月份放大月历；点击日期查看任务或安排事件。数字表示当天安排的任务数量。");
        var previous = Ui.Button("‹", () => { if (_year > 1901) { _year--; RefreshPage(); } }); previous.Tag = "previous-year";
        var current = Ui.Button("回到今年", () => { _year = _service.Today.Year; RefreshPage(); }); current.Tag = "current-year";
        var next = Ui.Button("›", () => { if (_year < 2100) { _year++; RefreshPage(); } }); next.Tag = "next-year";
        HeaderActions.Children.Add(previous); HeaderActions.Children.Add(current); HeaderActions.Children.Add(next);
        HeaderActions.Children.Add(Ui.Button("＋ 安排事件", () => OpenEventEditor(_year == _service.Today.Year ? _service.Today : new DateOnly(_year, 1, 1)), accent: true));
        // Keep one year's controls attached so switching pages preserves layout and templates.
        if (_calendarBody is null || _calendarYear != _year)
        {
            _calendarYear = _year;
            _calendarDataRevision = -1; _calendarHolidayRevision = -1;
            _calendarDays.Clear(); _calendarMonthCounts.Clear();
            _calendarBody = new StackPanel();
            var legend = Ui.Row(Ui.Badge("今天"), Ui.Badge("★ 重要任务", true), Ui.Text("休：法定休息  /  班：调休上班", 10, Ui.Muted));
            legend.Margin = new Thickness(0, 0, 0, 17); _calendarBody.Children.Add(legend);
            _calendarNotice = Ui.Text($"{_year} 年放假调休数据尚未更新，农历和常见节日仍可查看。", 11, Ui.Amber);
            _calendarNotice.Margin = new Thickness(0, 0, 0, 12); _calendarBody.Children.Add(_calendarNotice);
            _calendarMonths = new UniformGrid();
            for (var month = 1; month <= 12; month++) _calendarMonths.Children.Add(MonthCard(_year, month));
            _calendarBody.Children.Add(_calendarMonths);
            CalendarHost.Child = _calendarBody;
        }
        _calendarMonths!.Columns = _wide ? 4 : 3;
        RefreshCalendarData();
    }

    private void RefreshCalendarData()
    {
        var dataChanged = _calendarDataRevision != _service.Revision;
        var holidaysChanged = _calendarHolidayRevision != _holidays.Revision;
        if (!dataChanged && !holidaysChanged && _calendarToday == _service.Today) return;
        var summaries = _service.CalendarForYear(_year);
        foreach (var pair in _calendarDays)
        {
            var day = pair.Value;
            if (holidaysChanged || day.Information is null) day.Information = _holidays.ForDay(pair.Key);
            UpdateDay(day, summaries.GetValueOrDefault(pair.Key, CalendarDaySummary.Empty));
        }
        var monthlyCounts = summaries.GroupBy(p => p.Key.Month).ToDictionary(g => g.Key, g => g.Sum(p => p.Value.RecordedCount));
        foreach (var pair in _calendarMonthCounts)
        {
            var count = monthlyCounts.GetValueOrDefault(pair.Key);
            pair.Value.Text = count > 0 ? $"{count} 项已记录" : "";
        }
        _calendarNotice!.Visibility = _holidays.HasYear(_year) ? Visibility.Collapsed : Visibility.Visible;
        _calendarDataRevision = _service.Revision;
        _calendarHolidayRevision = _holidays.Revision;
        _calendarToday = _service.Today;
    }

    private Border MonthCard(int year, int month)
    {
        var body = new StackPanel();
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(Ui.Text($"{month:00} 月", 15, bold: true));
        var badge = Ui.Text("", 9, Ui.Muted); _calendarMonthCounts[month] = badge;
        Grid.SetColumn(badge, 1); header.Children.Add(badge);
        var monthButton = Ui.Button("", () => ShowMonth(new DateOnly(year, month, 1)));
        monthButton.Content = header; monthButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        monthButton.Background = Brushes.Transparent; monthButton.BorderThickness = new Thickness(0);
        monthButton.Padding = new Thickness(0); monthButton.Margin = new Thickness(0, 0, 0, 9);
        monthButton.ToolTip = $"放大查看 {year} 年 {month} 月"; monthButton.Tag = $"month:{year}-{month:00}";
        body.Children.Add(monthButton);
        var weekdays = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 5) };
        foreach (var weekday in new[] { "一", "二", "三", "四", "五", "六", "日" })
        { var label = Ui.Text(weekday, 9, Ui.Muted); label.HorizontalAlignment = HorizontalAlignment.Center; weekdays.Children.Add(label); }
        body.Children.Add(weekdays);
        var days = new UniformGrid { Columns = 7, Rows = 6 };
        var first = new DateOnly(year, month, 1);
        var offset = ((int)first.DayOfWeek + 6) % 7;
        for (var index = 0; index < 42; index++)
        {
            var day = index - offset + 1;
            if (day < 1 || day > DateTime.DaysInMonth(year, month)) { days.Children.Add(new Border { Height = 25 }); continue; }
            var date = new DateOnly(year, month, day);
            days.Children.Add(DayButton(date));
        }
        body.Children.Add(days);
        var card = GlassCardBorder.Create(body, new Thickness(11)); card.Margin = new Thickness(0, 0, 10, 12); return card;
    }

    private Button DayButton(DateOnly date)
    {
        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition()); content.RowDefinitions.Add(new RowDefinition());
        var top = new Grid(); top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var number = Ui.Text(date.Day.ToString(), 10); top.Children.Add(number);
        var mark = Ui.Text("", 7);
        Grid.SetColumn(mark, 1); top.Children.Add(mark);
        content.Children.Add(top);
        var bottom = Ui.Text("", 7);
        bottom.TextWrapping = TextWrapping.NoWrap; bottom.TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetRow(bottom, 1); content.Children.Add(bottom);
        var button = Ui.Button("", () => ShowDate(date)); button.Content = content;
        button.BorderBrush = Brushes.Transparent;
        button.Padding = new Thickness(3, 1, 3, 1); button.Margin = new Thickness(1); button.Height = 25;
        _calendarDays[date] = new(date, button, number, mark, bottom);
        button.Tag = "date:" + date.ToString("yyyy-MM-dd"); return button;
    }

    private void UpdateDay(CalendarDayView day, CalendarDaySummary summary)
    {
        var date = day.Date; var info = day.Information!;
        var count = summary.ScheduledCount; var titles = summary.EventTitles;
        var today = date == _service.Today; var important = summary.HasImportantTask;
        var marker = info.WorkStatus.StartsWith("调休") ? "班" : info.WorkStatus.StartsWith("法定") ? "休" : important ? "★" : "";
        var preview = titles.Count > 0 ? titles[0] : count > 0 ? $"{count} 项" : info.Festivals.Split('·')[0].Trim();
        var tooltip = $"{date:yyyy-MM-dd dddd}\n{info.Lunar}\n{info.Festivals}\n{info.WorkStatus}" +
            (titles.Count > 0 ? "\n" + string.Join("\n", titles) : "") + (count > 0 ? $"\n{count} 项任务" : "");
        var presentation = new CalendarDayPresentation(today, important, info.IsOffDay, count, marker, preview, tooltip);
        if (presentation == day.Presentation) return;
        day.Presentation = presentation;
        day.Number.Foreground = today ? Brushes.White : info.IsOffDay ? Ui.Accent : Ui.Ink;
        day.Number.FontWeight = today ? FontWeights.SemiBold : FontWeights.Normal;
        day.Marker.Text = marker + (count > 0 ? count.ToString() : "");
        day.Marker.Foreground = today ? Brushes.White : important ? Ui.Amber : Ui.Accent;
        day.Preview.Text = preview; day.Preview.Foreground = today ? Ui.OnAccentMuted : Ui.Muted;
        day.Button.Background = today ? Ui.Accent : important ? Ui.ImportantBadge : count > 0 ? Ui.Selected : Brushes.Transparent;
        day.Button.ToolTip = tooltip;
    }

    private sealed class CalendarDayView(DateOnly date, Button button, TextBlock number, TextBlock marker, TextBlock preview)
    {
        public DateOnly Date { get; } = date;
        public Button Button { get; } = button;
        public TextBlock Number { get; } = number;
        public TextBlock Marker { get; } = marker;
        public TextBlock Preview { get; } = preview;
        public DayInformation? Information { get; set; }
        public CalendarDayPresentation? Presentation { get; set; }
    }
    private sealed record CalendarDayPresentation(bool Today, bool Important, bool OffDay, int Count, string Marker, string Preview, string Tooltip);

    private void ShowDate(DateOnly date)
    {
        var dialog = new Window
        {
            Owner = this, Title = date.ToString("yyyy 年 M 月 d 日"), Width = 780, Height = Math.Min(760, Height - 35),
            MinHeight = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var root = new StackPanel { Margin = new Thickness(26) };
        void Render()
        {
            root.Children.Clear(); var info = _holidays.ForDay(date);
            root.Children.Add(Ui.Text(date.ToString("M 月 d 日 · dddd", CultureInfo.GetCultureInfo("zh-CN")), 25, bold: true));
            var subtitle = Ui.Text($"{info.Lunar} · {info.WorkStatus}\n{info.Festivals}", 12, Ui.Muted);
            subtitle.Margin = new Thickness(0, 8, 0, 16); root.Children.Add(subtitle);
            var actions = Ui.Row(Ui.Button("＋ 安排事件", () => OpenEventEditor(date, Render)), Ui.Button("＋ 添加任务", () => OpenTaskEditor(date, Render), accent: true));
            actions.Margin = new Thickness(0, 0, 0, 18); root.Children.Add(actions);
            var tasks = _service.ForDate(date);
            if (tasks.Count == 0) root.Children.Add(GlassCardBorder.Create(Ui.Text("这一天还没有安排。", 13, Ui.Muted)));
            else foreach (var task in tasks) root.Children.Add(TaskRow(task, refresh: Render));
            var records = _service.ActivitiesFor(date);
            if (records.Count > 0)
            {
                root.Children.Add(Ui.Label("当日操作记录"));
                foreach (var record in records) root.Children.Add(ActivityRow(record));
            }
        }
        dialog.Content = new ScrollViewer { Content = root }; Render(); dialog.ShowDialog();
        if (_page == PageKind.Year) RefreshPage();
    }
}
