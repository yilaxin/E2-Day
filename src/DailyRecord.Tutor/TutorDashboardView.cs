using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DailyRecord.Core;

namespace DailyRecord.App;

public sealed partial class TutorWindow
{
    private DateOnly? _dashboardFrom;
    private DateOnly? _dashboardTo;
    private string? _dashboardStudent;
    private bool _dashboardAllStudents;
    private DateOnly? _recordFrom;
    private DateOnly? _recordTo;
    internal TutorTeachingReport? DashboardReport { get; private set; }
    private static readonly Brush ChartGreen = new SolidColorBrush(Color.FromRgb(65, 147, 139));
    private static readonly Brush ChartAmber = new SolidColorBrush(Color.FromRgb(184, 133, 52));
    private static readonly Brush ChartBlue = new SolidColorBrush(Color.FromRgb(82, 130, 196));
    private static readonly Brush ChartGray = new SolidColorBrush(Color.FromRgb(153, 145, 163));

    private void DashboardPage()
    {
        Header("教学概览", "看见授课节奏、学生投入与课后跟进，把每一次记录用起来。");
        if (!IsExample && _service.State.Students.Count == 0) _body.Children.Add(ExampleEntry());
        var from = _dashboardFrom ?? new DateOnly(Today.Year, Today.Month, 1);
        var to = _dashboardTo ?? Today;
        if (_dashboardStudent is not null && !_service.State.Students.Any(s => s.Id == _dashboardStudent)) _dashboardStudent = null;
        var start = new DatePicker { SelectedDate = from.ToDateTime(TimeOnly.MinValue), Width = 138, Tag = "dashboard-from", DisplayDateStart = new DateTime(1901, 1, 1), DisplayDateEnd = new DateTime(2100, 12, 31) };
        var end = new DatePicker { SelectedDate = to.ToDateTime(TimeOnly.MinValue), Width = 138, Tag = "dashboard-to", DisplayDateStart = start.DisplayDateStart, DisplayDateEnd = start.DisplayDateEnd };
        var students = new List<TutorStudent> { new() { Id = "", Name = "全部学生" } }; students.AddRange(_service.State.Students.OrderBy(s => s.Archived).ThenBy(s => s.Name));
        var picker = new ComboBox { ItemsSource = students, DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValue = _dashboardStudent ?? "", Width = 160, Margin = new Thickness(8, 0, 8, 0), Tag = "dashboard-student" };
        var error = Ui.Text("", 12, Ui.Amber); error.Tag = "dashboard-error";
        var apply = Ui.Button("查看", () =>
        {
            try
            {
                if (start.SelectedDate is null || end.SelectedDate is null) throw new ArgumentException("请选择开始和结束日期。");
                var a = DateOnly.FromDateTime(start.SelectedDate.Value); var b = DateOnly.FromDateTime(end.SelectedDate.Value);
                TutorTeachingAnalytics.ValidateRange(a, b);
                _dashboardFrom = a; _dashboardTo = b; _dashboardStudent = string.IsNullOrEmpty(picker.SelectedValue as string) ? null : (string)picker.SelectedValue;
                _dashboardAllStudents = false; Render(); _scroller.ScrollToTop();
            }
            catch (ArgumentException ex) { error.Text = ex.Message; }
        }, true); apply.Tag = "dashboard-apply";
        var filters = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var child in new UIElement[] { start, Ui.Text(" 至 ", 12), end, picker, apply }) filters.Children.Add(child);
        _body.Children.Add(filters);
        var shortcuts = new WrapPanel();
        void Quick(string label, DateOnly a, DateOnly b, string tag)
        {
            var button = Ui.Button(label, () => { _dashboardFrom = a; _dashboardTo = b; _dashboardAllStudents = false; Render(); _scroller.ScrollToTop(); });
            button.Tag = tag; shortcuts.Children.Add(button);
        }
        Quick("今天", Today, Today, "dashboard-today");
        Quick("本周", DateOnly.FromDayNumber(Math.Max(new DateOnly(1901, 1, 1).DayNumber, Today.DayNumber - (((int)Today.DayOfWeek + 6) % 7))), Today, "dashboard-week");
        Quick("本月", new(Today.Year, Today.Month, 1), Today, "dashboard-month");
        Quick("本年", new(Today.Year, 1, 1), Today, "dashboard-year");
        _body.Children.Add(shortcuts); _body.Children.Add(error);
        var report = DashboardReport = TutorTeachingAnalytics.Build(_service, from, to, Today, _dashboardStudent);
        var scope = Ui.Text($"{from:yyyy-MM-dd} 至 {to:yyyy-MM-dd}  ·  单次最多 366 天  ·  实际课时统计截至今天的已授课记录", 11, Ui.Muted);
        scope.Margin = new Thickness(0, 8, 0, 12); _body.Children.Add(scope);
        var metrics = new UniformGrid { Columns = 4 };
        Metric("已授课", report.Taught + " 节", 3, report.Taught);
        Metric("实际课时", Hours(report.Minutes) + " 小时", 3, report.Taught);
        Metric("待确认课程", report.Pending.Count + " 节", 1, report.Pending.Count);
        Metric("待补反馈", report.MissingFeedback.Count + " 节", 2, report.MissingFeedback.Count);
        void Metric(string label, string value, int status, int count)
        {
            var button = Ui.Button("", () => DashboardRecords(status)); button.Content = Ui.Stack(Ui.Text(label, 12, Ui.Muted), Ui.Text(value, 24, Ui.Accent, true));
            button.Tag = "dashboard-metric:" + status + ":" + label; button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0); button.Padding = new Thickness(0); button.IsEnabled = count > 0;
            var card = Ui.Card(button, new Thickness(15)); card.Margin = new Thickness(0, 0, 8, 14); metrics.Children.Add(card);
        }
        _body.Children.Add(metrics);
        if (report.FutureTaught > 0) _body.Children.Add(Ui.Text($"另有 {report.FutureTaught} 节“已授课”记录的日期晚于今天，未计入实际课时；可在授课日历核对日期。", 12, Ui.Amber));
        _body.Children.Add(DashboardHeatmap(report));
        _body.Children.Add(DashboardPair(DashboardExecution(report), DashboardFeedback(report)));
        _body.Children.Add(DashboardRanking(report));
        _body.Children.Add(Ui.Text($"当前学习需求 · {_service.State.Students.Count(s => !s.Archived && (_dashboardStudent is null || s.Id == _dashboardStudent))} 位在教学生", 19, Ui.Accent, true));
        var note = Ui.Text("使用当前学生档案标签，与日期范围无关。辅导重点可多选，各项人数不要求相加等于总人数。", 11, Ui.Muted); note.Margin = new Thickness(0, 6, 0, 12); _body.Children.Add(note);
        _body.Children.Add(DashboardColumns(new UIElement[] { DemandCard("基础水平", report.Levels), DemandCard("学习节奏", report.Paces), DemandCard("辅导重点", report.Focuses) }));
        Border DemandCard(string title, IReadOnlyList<TutorDemandCount> counts)
        {
            var panel = Ui.Stack(Ui.Text(title, 16, bold: true));
            if (report.ActiveStudents == 0) panel.Children.Add(Ui.Text("没有符合筛选的在教学生。", 12, Ui.Muted));
            else foreach (var count in counts) panel.Children.Add(DashboardBar(count.Label.Split('·')[0].Trim(), count.Count, report.ActiveStudents, count.Count + " 人", Ui.Accent));
            return Ui.Card(panel, new Thickness(16));
        }
    }

    private UIElement DashboardPair(UIElement first, UIElement second) => DashboardColumns(new[] { first, second });
    private Grid DashboardColumns(IReadOnlyList<UIElement> children)
    {
        var grid = new Grid { Tag = "dashboard-grid:" + children.Count }; var mode = -1;
        foreach (var child in children) grid.Children.Add(child);
        void Arrange(bool horizontal)
        {
            var next = horizontal ? 1 : 0; if (mode == next) return; mode = next;
            grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
            for (var i = 0; i < children.Count; i++)
            {
                if (horizontal) grid.ColumnDefinitions.Add(new ColumnDefinition()); else grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetColumn(children[i], horizontal ? i : 0); Grid.SetRow(children[i], horizontal ? 0 : i);
                ((FrameworkElement)children[i]).Margin = new Thickness(0, 0, horizontal && i < children.Count - 1 ? 12 : 0, 14);
            }
        }
        Arrange(Width >= 1220); grid.SizeChanged += (_, _) => Arrange(grid.ActualWidth >= 890); return grid;
    }

    private Border DashboardHeatmap(TutorTeachingReport report)
    {
        var panel = Ui.Stack(Ui.Text("授课热力日历", 18, bold: true), Ui.Text("颜色越深，实际授课时长越多；点击日期查看课程。蓝色边框表示今天或未来有待上课安排。", 11, Ui.Muted));
        var summary = Ui.Text($"授课 {report.TeachingDays} 天    单日最多 {Hours(report.PeakMinutes)} 小时    今天及未来待上课 {report.Planned} 节", 12, Ui.Accent);
        summary.Margin = new Thickness(0, 12, 0, 12); panel.Children.Add(summary);
        var monday = report.From.DayNumber - (((int)report.From.DayOfWeek + 6) % 7);
        var weeks = (report.To.DayNumber - monday) / 7 + 1;
        var compact = report.Days.Count <= 42;
        var size = Math.Min(18, Math.Max(8, (Width - 360d) / weeks));
        var grid = new Grid { Width = 25 + size * weeks, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
        if (compact)
        {
            grid.Width = double.NaN; grid.HorizontalAlignment = HorizontalAlignment.Stretch;
            for (var i = 0; i < 7; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (var i = 0; i < 7; i++) { var label = Ui.Text(new[] { "一", "二", "三", "四", "五", "六", "日" }[i], 10, Ui.Muted); label.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetColumn(label, i); grid.Children.Add(label); }
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
            for (var i = 0; i < weeks; i++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
        }
        else
        {
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) });
        for (var i = 0; i < weeks; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
        for (var i = 0; i < 7; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Math.Min(size, 24)) });
            var text = Ui.Text(new[] { "一", "二", "三", "四", "五", "六", "日" }[i], 9, Ui.Muted); Grid.SetRow(text, i + 1); grid.Children.Add(text);
        }
        panel.SizeChanged += (_, _) =>
        {
            grid.Width = Math.Max(1, Math.Min(25 + weeks * 18, panel.ActualWidth));
            foreach (var row in grid.RowDefinitions.Skip(1)) row.Height = new GridLength(Math.Max(5, Math.Min(18, (grid.Width - 25) / weeks)));
        };
        }
        var colors = new[] { "#EEEAF4", "#E0D5F3", "#BAA5E3", "#9674CD", "#6352A6" }.Select(c => (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString(c))).ToArray();
        var lastLabel = -4;
        foreach (var day in report.Days)
        {
            var column = (day.Date.DayNumber - monday) / 7 + 1; var row = ((int)day.Date.DayOfWeek + 6) % 7 + 1;
            if (!compact && (day.Date == report.From || day.Date.Day == 1) && column >= lastLabel + (size < 20 ? 3 : 2))
            {
                var label = Ui.Text(day.Date.ToString("M月"), 10, Ui.Muted); Grid.SetColumn(label, column); Grid.SetColumnSpan(label, Math.Min(size < 20 ? 3 : 2, weeks - column + 1)); grid.Children.Add(label); lastLabel = column;
            }
            var level = day.Minutes == 0 ? 0 : day.Minutes <= 60 ? 1 : day.Minutes <= 120 ? 2 : day.Minutes <= 240 ? 3 : 4;
            var button = Ui.Button("", () => SelectDate(day.Date)); button.Tag = "dashboard-day:" + day.Date.ToString("yyyy-MM-dd");
            button.Style = (Style)Application.Current.FindResource("TeachingHeatCell");
            button.Background = colors[level]; button.Padding = new Thickness(0); button.Margin = new Thickness(1);
            button.BorderBrush = day.Planned > 0 ? ChartBlue : day.Date == Today ? Ui.Accent : Brushes.Transparent;
            button.BorderThickness = new Thickness(day.Planned > 0 || day.Date == Today ? 1.5 : 0);
            if (compact)
            {
                var color = level >= 3 ? Brushes.White : Ui.Ink;
                var detail = day.Minutes > 0 ? Hours(day.Minutes) + " 小时" : day.Planned > 0 ? "待上 " + day.Planned + " 节" : day.Pending > 0 ? "待确认 " + day.Pending + " 节" : day.Cancelled > 0 ? "已取消" : "—";
                button.Content = Ui.Stack(Ui.Text(day.Date.ToString("M/d"), 11, color, true), Ui.Text(detail, 10, color));
            }
            button.ToolTip = $"{day.Date:yyyy-MM-dd} · 已授 {day.Count} 节 / {Hours(day.Minutes)} 小时\n待确认 {day.Pending} 节 · 待上课 {day.Planned} 节 · 已取消 {day.Cancelled} 节";
            Grid.SetColumn(button, compact ? row - 1 : column); Grid.SetRow(button, compact ? column : row); grid.Children.Add(button);
        }
        panel.Children.Add(grid);
        var legend = new WrapPanel();
        var labels = new[] { "无授课", "≤1小时", "≤2小时", "≤4小时", ">4小时" };
        for (var i = 0; i < colors.Length; i++) legend.Children.Add(Ui.Row(new Border { Width = 12, Height = 12, Background = colors[i], CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 5, 0) }, LegendText(labels[i])));
        panel.Children.Add(legend); return Ui.Card(panel);
    }
    private static TextBlock LegendText(string text) { var label = Ui.Text(text, 10, Ui.Muted); label.Margin = new Thickness(0, 0, 15, 0); return label; }

    private Border DashboardExecution(TutorTeachingReport report)
    {
        var panel = Ui.Stack(Ui.Text("课程安排与执行", 18, bold: true), Ui.Text("待确认：今天之前尚未确认；待上课：今天及未来安排。", 11, Ui.Muted));
        var legend = new WrapPanel { Margin = new Thickness(0, 12, 0, 8) };
        foreach (var item in new[] { ("已授课", ChartGreen), ("已取消", ChartGray), ("待确认", ChartAmber), ("待上课", ChartBlue) })
            legend.Children.Add(Ui.Row(new Border { Width = 9, Height = 9, Background = item.Item2, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 5, 0) }, LegendText(item.Item1)));
        panel.Children.Add(legend);
        if (!report.Periods.Any(p => p.Total > 0)) { panel.Children.Add(Ui.Text("所选范围暂无课程安排。", 13, Ui.Muted)); return Ui.Card(panel); }
        var chart = new UniformGrid { Columns = report.Periods.Count, Margin = new Thickness(0, 8, 0, 0) };
        var max = Math.Max(1, report.Periods.Max(p => p.Total));
        foreach (var period in report.Periods)
        {
            var bars = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            var number = Ui.Text(period.Total.ToString(), 10, Ui.Muted); number.HorizontalAlignment = HorizontalAlignment.Center; bars.Children.Add(number);
            foreach (var item in new[] { (period.Planned, ChartBlue), (period.Pending, ChartAmber), (period.Cancelled, ChartGray), (period.Taught, ChartGreen) })
                if (item.Item1 > 0) bars.Children.Add(new Border { Height = 125d * item.Item1 / max, Background = item.Item2, Margin = new Thickness(2, 0, 2, 1), CornerRadius = new CornerRadius(3) });
            var area = new Grid { Height = 150 }; area.Children.Add(bars);
            var label = Ui.Text(period.From.ToString("M/d"), 10, Ui.Muted); label.HorizontalAlignment = HorizontalAlignment.Center; label.Margin = new Thickness(0, 7, 0, 0);
            var button = Ui.Button("", () => { _dashboardFrom = period.From; _dashboardTo = period.To; Render(); _scroller.ScrollToTop(); });
            button.Content = Ui.Stack(area, label); button.Padding = new Thickness(1); button.Margin = new Thickness(1); button.Background = Brushes.Transparent; button.BorderBrush = Brushes.Transparent;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Tag = "dashboard-period:" + period.From.ToString("yyyy-MM-dd");
            button.ToolTip = $"{period.From:yyyy-MM-dd} 至 {period.To:yyyy-MM-dd}\n已授课 {period.Taught} · 已取消 {period.Cancelled}\n待确认 {period.Pending} · 待上课 {period.Planned}\n点击查看该时段概览"; chart.Children.Add(button);
        }
        panel.Children.Add(chart); panel.Children.Add(Ui.Text("按范围自动汇总为日、周或月；点击柱形缩小查看范围。", 10, Ui.Muted)); return Ui.Card(panel);
    }

    private Border DashboardFeedback(TutorTeachingReport report)
    {
        var panel = Ui.Stack(Ui.Text("课后反馈跟进", 18, bold: true), Ui.Text("统计所选范围已授课程；填写任一课程反馈项即视为已记录。", 11, Ui.Muted));
        var ring = new Grid { Width = 150, Height = 145, Margin = new Thickness(0, 8, 0, 6), HorizontalAlignment = HorizontalAlignment.Left };
        ring.Children.Add(new TutorFeedbackRing(report.WithFeedback, report.Taught));
        var text = Ui.Stack(Ui.Text(report.Taught == 0 ? "—" : (100d * report.WithFeedback / report.Taught).ToString("0") + "%", 25, Ui.Accent, true), Ui.Text("已记录反馈", 10, Ui.Muted));
        text.HorizontalAlignment = HorizontalAlignment.Center; text.VerticalAlignment = VerticalAlignment.Center; ring.Children.Add(text);
        panel.Children.Add(Ui.Row(ring, Ui.Stack(Ui.Text($"已记录 {report.WithFeedback} 节", 13, Ui.Accent), Ui.Text($"待补充 {report.MissingFeedback.Count} 节", 13, ChartAmber))));
        if (report.Taught == 0) panel.Children.Add(Ui.Text("所选范围暂无已授课记录。", 12, Ui.Muted));
        else if (report.MissingFeedback.Count == 0) panel.Children.Add(Ui.Text("这段时间的授课都已有反馈记录。", 12, Ui.Muted));
        foreach (var lesson in report.MissingFeedback.Take(3))
        {
            var button = Ui.Button($"{lesson.Date:M/d}  {StudentName(lesson.StudentId)} · 补写反馈", () => AddLesson(lesson.Date, lesson), quiet: true);
            button.Tag = "dashboard-feedback:" + lesson.Id; button.HorizontalContentAlignment = HorizontalAlignment.Left; button.ToolTip = lesson.Subject + " · " + lesson.Start.ToString("HH:mm"); panel.Children.Add(button);
        }
        if (report.MissingFeedback.Count > 0)
        {
            var all = Ui.Button($"查看这段时间全部 {report.MissingFeedback.Count} 节待补反馈", () => DashboardRecords(2)); all.Tag = "dashboard-feedback-all"; panel.Children.Add(all);
        }
        return Ui.Card(panel);
    }

    private Border DashboardRanking(TutorTeachingReport report)
    {
        var panel = Ui.Stack(Ui.Text("学生课时分布", 18, bold: true), Ui.Text("按所选范围实际授课时长排列，包含已结课学生的历史记录；点击打开档案。", 11, Ui.Muted));
        if (report.Students.Count == 0) panel.Children.Add(Ui.Text("所选范围暂无已授课记录。", 12, Ui.Muted));
        foreach (var student in report.Students.Take(_dashboardAllStudents ? int.MaxValue : 10))
        {
            var button = Ui.Button("", () => SelectStudent(student.StudentId)); button.Content = DashboardBar(student.Name + (student.Archived ? "（已结课）" : ""), student.Minutes, Math.Max(1, report.Students[0].Minutes), $"{student.Count} 节 · {Hours(student.Minutes)} 小时", Ui.Accent, true);
            button.Tag = "dashboard-student:" + student.StudentId; button.Padding = new Thickness(0); button.Margin = new Thickness(0, 8, 0, 0); button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Background = Brushes.Transparent; button.BorderBrush = Brushes.Transparent; panel.Children.Add(button);
        }
        if (report.Students.Count > 10)
        {
            var more = Ui.Button(_dashboardAllStudents ? "收起排行" : $"查看全部 {report.Students.Count} 位学生", () => { _dashboardAllStudents = !_dashboardAllStudents; Render(); }); more.Tag = "dashboard-ranking-more"; panel.Children.Add(more);
        }
        var idle = Ui.Text($"所选范围内没有已授课记录的在教学生 · {report.NoTeaching.Count} 人", 12, Ui.Muted); idle.Margin = new Thickness(0, 15, 0, 4); panel.Children.Add(idle);
        var row = new WrapPanel();
        foreach (var student in report.NoTeaching.Take(8)) { var button = Ui.Button(student.Name, () => SelectStudent(student.Id), quiet: true); button.Tag = "dashboard-idle:" + student.Id; row.Children.Add(button); }
        panel.Children.Add(row);
        if (report.NoTeaching.Count > 8) panel.Children.Add(Ui.Text($"另有 {report.NoTeaching.Count - 8} 位，可到学生档案查看。", 11, Ui.Muted));
        return Ui.Card(panel);
    }

    private static UIElement DashboardBar(string name, int value, int maximum, string detail, Brush color, bool wide = false)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var heading = new Grid(); heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Ui.Text(name, wide ? 13 : 11); title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis; title.Margin = new Thickness(0, 0, 8, 0); heading.Children.Add(title);
        var number = Ui.Text(detail, wide ? 12 : 11, Ui.Muted); Grid.SetColumn(number, 1); heading.Children.Add(number); stack.Children.Add(heading);
        var bar = new Grid { Height = wide ? 9 : 6, Margin = new Thickness(0, 5, 0, 0), Background = Ui.Selected };
        var fill = Math.Clamp(value / (double)Math.Max(1, maximum), 0, 1); bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fill, GridUnitType.Star) }); bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - fill, GridUnitType.Star) });
        bar.Children.Add(new Border { Background = color, CornerRadius = new CornerRadius(3) }); stack.Children.Add(bar); stack.ToolTip = name + " · " + detail; return stack;
    }

    private void DashboardRecords(int status)
    {
        var from = DashboardReport!.From; var to = DashboardReport.To;
        if (status is 2 or 3 && to > Today) to = Today;
        if (to < from) return;
        _recordFrom = from; _recordTo = to; _recordStatus = status; _filterStudent = _dashboardStudent; _search = ""; Navigate(4, true);
    }
}

internal sealed class TutorFeedbackRing(int filled, int total) : FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); var center = new Point(75, 72); const double radius = 53;
        dc.DrawEllipse(null, new Pen(total > 0 ? new SolidColorBrush(Color.FromRgb(238, 217, 181)) : Ui.Selected, 14), center, radius, radius);
        if (total <= 0 || filled <= 0) return;
        var pen = new Pen(Ui.Accent, 14) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (filled >= total) { dc.DrawEllipse(null, pen, center, radius, radius); return; }
        var angle = 2 * Math.PI * filled / total;
        var geometry = new StreamGeometry(); using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(75, 72 - radius), false, false);
            context.ArcTo(new Point(75 + radius * Math.Sin(angle), 72 - radius * Math.Cos(angle)), new Size(radius, radius), 0, angle > Math.PI, SweepDirection.Clockwise, true, false);
        }
        dc.DrawGeometry(null, pen, geometry);
    }
}
