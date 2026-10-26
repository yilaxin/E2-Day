using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using DailyRecord.Core;
using Microsoft.Win32;

namespace DailyRecord.App;

public sealed partial class TutorWindow : Window
{
    private TutorService _service = null!;
    private TutorStore _store = null!;
    private HolidayService _holidays = null!;
    private readonly DateOnly? _fixedToday;
    private readonly StackPanel _body = new();
    private readonly TextBlock _title = Ui.Text("", 30, bold: true);
    private readonly TextBlock _subtitle = Ui.Text("", 12, Ui.Muted);
    private readonly TextBlock _saved = Ui.Text("", 10, Ui.Muted);
    private readonly StackPanel _actions = Ui.Row();
    private readonly Dictionary<int, Button> _nav = [];
    private readonly ScrollViewer _scroller;
    private readonly DispatcherTimer _timer;
    internal SmoothWheelScroller PageScroll { get; }
    internal ScrollViewer PageScroller => _scroller;
    private int _page;
    private DateOnly _month;
    private DateOnly? _selectedDate;
    private bool _year;
    private string? _student;
    private string _studentSearch = "";
    private string _search = "";
    private string? _filterStudent;
    private DateOnly _from;
    private DateOnly _to;
    public DateOnly Today => _fixedToday ?? DateOnly.FromDateTime(DateTime.Now);
    public string AccountName { get; set; } = "示例账号";
    public string BackupWarning { get; set; } = "";
    public Action? SwitchAccount { get; set; }
    internal TutorService Service => _service;
    internal Action<string> WriteClipboard { get; set; } = Clipboard.SetText;

    public TutorWindow() : this(null, false) { }
    public TutorWindow(TutorService service, TutorStore store, DateOnly? today = null, bool isExample = false) : this(today, isExample)
    { InitializeData(service, store); }
    internal void InitializeData(TutorService service, TutorStore store)
    {
        _service = service; _store = store;
        _holidays = new HolidayService(Path.Combine(AppContext.BaseDirectory, "Data", "Holidays"), Path.Combine(store.DirectoryPath, "Holidays"));
        Title = "E²-Day 家教版"; Navigate(0); _timer.Start();
    }
    private TutorWindow(DateOnly? today, bool isExample)
    {
        _fixedToday = today; IsExample = isExample; _month = new(Today.Year, Today.Month, 1); _from = _month; _to = Today;
        Title = "E²-Day 家教版"; Width = Math.Min(1380, SystemParameters.WorkArea.Width - 32); Height = Math.Min(940, SystemParameters.WorkArea.Height - 32); MinWidth = 1000; MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var layout = new Grid { Background = Ui.Page, ClipToBounds = true }; layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) }); layout.ColumnDefinitions.Add(new ColumnDefinition()); Content = layout;
        var backdrop = TutorAppearance.Backdrop(); Grid.SetColumnSpan(backdrop, 2); layout.Children.Add(backdrop);
        var sidebarGrid = new Grid { Margin = new Thickness(16, 22, 16, 18) }; sidebarGrid.RowDefinitions.Add(new RowDefinition()); sidebarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var sidebar = Ui.Stack(new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/e2-day-logo.png")), Width = 56, Height = 56, HorizontalAlignment = HorizontalAlignment.Left },
            Ui.Text("E²-Day", 26, Ui.Accent, true), Ui.Text("家教版  /  TUTOR", 12, Ui.Amber, true), Ui.Text("记录每一堂课\n看见每一步成长", 12, Ui.Muted));
        sidebar.Children[0].SetValue(MarginProperty, new Thickness(0, 0, 0, 12)); sidebar.Children[3].SetValue(MarginProperty, new Thickness(0, 10, 0, 20));
        var labels = new[] { ("◷  今日授课", 0), ("▦  授课日历", 1), ("♧  学生档案", 2), ("▥  收入统计", 3), ("◈  教学概览", 6), ("≡  教学记录", 4), ("⚙  设置与备份", 5) };
        for (var i = 0; i < labels.Length; i++) { var destination = labels[i].Item2; var b = Ui.Button(labels[i].Item1, () => Navigate(destination)); b.Tag = "nav:" + destination; b.HorizontalContentAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 0, 0, 8); b.Padding = new Thickness(10); sidebar.Children.Add(b); _nav[destination] = b; }
        sidebarGrid.Children.Add(sidebar);
        var footer = Ui.Stack(Ui.Text("●  独立本地记录", 11, Ui.Accent), _saved); Grid.SetRow(footer, 1); sidebarGrid.Children.Add(footer);
        var sidebarBorder = new Border { Background = (Brush)Application.Current.FindResource("SidebarBrush"), BorderBrush = (Brush)Application.Current.FindResource("SidebarLineBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(24), Margin = new Thickness(14, 14, 0, 14), Child = sidebarGrid }; layout.Children.Add(sidebarBorder);
        var main = new Grid { Margin = new Thickness(24, 28, 22, 18) }; Grid.SetColumn(main, 1); layout.Children.Add(main);
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); main.RowDefinitions.Add(new RowDefinition());
        var header = new Grid { Margin = new Thickness(0, 0, 0, 20) }; header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _subtitle.Margin = new Thickness(0, 9, 0, 0); header.Children.Add(Ui.Stack(_title, _subtitle)); Grid.SetColumn(_actions, 1); _actions.Margin = new Thickness(12, 0, 0, 0); header.Children.Add(_actions); main.Children.Add(header);
        _scroller = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(_scroller, 1); main.Children.Add(_scroller);
        PageScroll = new SmoothWheelScroller(_scroller, () => true);
        var last = Today; _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += (_, _) => { if (last != Today) { last = Today; if (!OwnedWindows.Cast<Window>().Any()) Render(); } }; Closed += (_, _) => { _timer.Stop(); PageScroll.Dispose(); _lifetime.Cancel(); _noteThumbnails.Clear(); _thumbnailState = null; ClearPageResources(); SwitchAccount = null; };
    }
    internal void Navigate(int page, bool keepRecordRange = false) { if (page == 4 && !keepRecordRange) { _recordFrom = null; _recordTo = null; } PageScroll.Stop(); _page = page; _scroller.ScrollToTop(); Render(); }
    internal void SelectDate(DateOnly date) { _month = new(date.Year, date.Month, 1); _selectedDate = date; _year = false; Navigate(1); }
    internal void SelectStudent(string id) { _student = id; Navigate(2); }
    internal void Run(Action action)
    {
        try { action(); Render(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "操作未完成", MessageBoxButton.OK, MessageBoxImage.Information); }
    }
    private void Header(string title, string subtitle) { _title.Text = IsExample ? title + " · 示例体验" : title; _subtitle.Text = subtitle; }
    private string StudentName(string id) => _service.State.Students.FirstOrDefault(s => s.Id == id)?.Name ?? "未知学生";
    private static string Hours(int minutes) => (minutes / 60m).ToString("0.##");
    private static string Status(TutorLesson l) => l.Status switch { LessonStatus.Taught => "已授课", LessonStatus.Cancelled => "已取消", _ => "待上课" };
    private void Render()
    {
        PageScroll.Stop();
        ClearPageResources(); _saved.Text = _service.SaveStatus;
        foreach (var pair in _nav) { pair.Value.Background = pair.Key == _page ? Ui.Selected : Brushes.Transparent; pair.Value.Foreground = pair.Key == _page ? Ui.Accent : Ui.Muted; }
        if (IsExample) _body.Children.Add(ExampleBanner());
        switch (_page) { case 0: TodayPage(); break; case 1: CalendarPage(); break; case 2: StudentPage(); break; case 3: StatisticsPage(); break; case 4: RecordsPage(); break; case 6: DashboardPage(); break; default: SettingsPage(); break; }
    }
    private void AddLesson(DateOnly date, TutorLesson? lesson = null, bool complete = false)
    {
        if (_service.State.Students.Count == 0) { EditStudent(); if (_service.State.Students.Count == 0) return; }
        var dialog = new TutorLessonDialog(_service, _store, date, lesson, complete) { Owner = this }; dialog.ShowDialog(); Render();
    }
    private void EditStudent(TutorStudent? student = null)
    { var dialog = new TutorStudentDialog(_service, student) { Owner = this }; dialog.ShowDialog(); Render(); }
    private void Summary(TutorStatistics stats, int? feedback = null)
    {
        var grid = new UniformGrid { Columns = feedback is null ? 3 : 4, Margin = new Thickness(0, 0, 0, 10) };
        var metrics = new List<(string, string)> { ("已授课", stats.Count + " 节"), ("授课时长", Hours(stats.Minutes) + " 小时"), ("授课收入", "¥" + stats.Income.ToString("N2")) };
        if (feedback is { } n) metrics.Add(("待填写反馈", n + " 节"));
        foreach (var metric in metrics) { var card = Ui.Card(Ui.Stack(Ui.Text(metric.Item1, 12, Ui.Muted), Ui.Text(metric.Item2, 26, Ui.Accent, true)), new Thickness(17)); card.Margin = new Thickness(0, 0, 10, 8); grid.Children.Add(card); }
        _body.Children.Add(grid);
    }
    private void Empty(string text) => _body.Children.Add(Ui.Card(Ui.Text(text, 14, Ui.Muted)));
    private void TodayPage()
    {
        Header("今日授课", $"{Today:yyyy 年 M 月 d 日 · dddd}  ·  { _holidays.ForDay(Today).Lunar}  ·  课后记录，随时回顾");
        _actions.Children.Add(Ui.Button("＋ 新增学生", () => EditStudent())); _actions.Children.Add(Ui.Button("＋ 添加课程", () => AddLesson(Today), true));
        if (!IsExample && _service.State.Students.Count == 0) _body.Children.Add(ExampleEntry());
        var feedback = _service.State.Lessons.Where(l => l.Status == LessonStatus.Taught && !l.HasFeedback).OrderByDescending(l => l.Date).ToList();
        Summary(_service.Statistics(Today, Today), feedback.Count);
        _body.Children.Add(Ui.Label("今天的课程")); var lessons = _service.Lessons(Today, Today);
        if (lessons.Count == 0) Empty("今天还没有课程。点击“添加课程”安排授课，也可以补录已经上过的课。");
        foreach (var l in lessons) _body.Children.Add(LessonCard(l));
        var pending = _service.Lessons(Today.AddDays(-Math.Min(365, Today.DayNumber - new DateOnly(1901, 1, 1).DayNumber)), Today.AddDays(-1)).Where(l => l.Status == LessonStatus.Planned).ToList();
        _body.Children.Add(Ui.Label($"待确认课程 · 最近一年 {pending.Count} 节"));
        foreach (var l in pending.TakeLast(3)) _body.Children.Add(LessonCard(l));
        if (pending.Count > 3) _body.Children.Add(Ui.Button("在教学记录中查看全部待确认课程", () => { _search = ""; _filterStudent = null; _recordStatus = 1; Navigate(4); }));
        if (pending.Count == 0) _body.Children.Add(Ui.Text("过去的课程已确认。", 12, Ui.Muted));
        _body.Children.Add(Ui.Label("待补充反馈")); foreach (var l in feedback.Take(3)) _body.Children.Add(LessonCard(l));
        if (feedback.Count == 0) _body.Children.Add(Ui.Text("授课后填写反馈，教学经验会保存在这里。", 12, Ui.Muted));
        _body.Children.Add(Ui.Label("接下来七天"));
        foreach (var l in _service.Lessons(Today.AddDays(1), Today.AddDays(7)).Where(l => l.Status == LessonStatus.Planned).Take(5)) _body.Children.Add(LessonCard(l));
        _body.Children.Add(Ui.Label("备课与其他待办"));
        var input = new TextBox { Text = _todoDraft, MinWidth = 220, MaxLength = 500, Tag = "todo-title" }; input.TextChanged += (_, _) => _todoDraft = input.Text; var add = Ui.Button("添加", () => Run(() => { _service.AddTodo(input.Text); _todoDraft = ""; }), true); add.Tag = "todo-add";
        _body.Children.Add(Ui.Row(input, add));
        foreach (var todo in _service.State.Todos.OrderBy(t => t.Done))
        {
            var check = new CheckBox { Content = todo.Title, IsChecked = todo.Done, Margin = new Thickness(0, 12, 10, 6) }; check.Click += (_, _) => Run(() => _service.ToggleTodo(todo.Id));
            _body.Children.Add(Ui.Row(check, Ui.Button("移除", () => Run(() => _service.RemoveTodo(todo.Id)), quiet: true)));
        }
    }
    private Border LessonCard(TutorLesson lesson)
    {
        var panel = new StackPanel();
        var heading = new Grid(); heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.Children.Add(Ui.Text($"{lesson.Start:HH:mm}  {StudentName(lesson.StudentId)} · {lesson.Subject}", 17, bold: true));
        var badge = Ui.Badge(Status(lesson), lesson.Status == LessonStatus.Taught); Grid.SetColumn(badge, 1); heading.Children.Add(badge); panel.Children.Add(heading);
        panel.Children.Add(Ui.Text($"{lesson.Date:yyyy-MM-dd}  ·  {Hours(lesson.Minutes)} 小时  ·  ¥{lesson.HourlyRate:0.##}/小时  ·  " + (lesson.Status == LessonStatus.Taught ? $"收入 ¥{lesson.Income:N2}" : $"预计 ¥{decimal.Round(lesson.Minutes * lesson.HourlyRate / 60m, 2):N2}"), 12, Ui.Muted));
        if (!string.IsNullOrWhiteSpace(lesson.Content)) { var content = Ui.Text(lesson.Content, 13); content.MaxHeight = 52; content.TextTrimming = TextTrimming.CharacterEllipsis; content.Margin = new Thickness(0, 12, 0, 0); panel.Children.Add(content); }
        if (lesson.ImageIds.Count > 0) panel.Children.Add(LessonGallery(lesson.ImageIds));
        var buttons = new WrapPanel { Margin = new Thickness(0, 13, 0, 0) };
        var edit = Ui.Button(lesson.Status == LessonStatus.Taught ? "查看 / 编辑反馈" : "编辑 / 改期", () => AddLesson(lesson.Date, lesson)); edit.Tag = "edit:" + lesson.Id; buttons.Children.Add(edit);
        if (lesson.Status == LessonStatus.Planned) { var complete = Ui.Button("完成授课", () => AddLesson(lesson.Date, lesson, true), true); complete.Tag = "complete:" + lesson.Id; buttons.Children.Add(complete); }
        if (lesson.Status == LessonStatus.Taught && lesson.HasFeedback) { var copy = Ui.Button("复制给家长", () => CopyFeedback(lesson)); copy.Tag = "copy:" + lesson.Id; buttons.Children.Add(copy); }
        if (lesson.Status != LessonStatus.Cancelled) buttons.Children.Add(Ui.Button("取消课程", () => Run(() => { var changed = TutorRules.Copy(lesson); changed.Status = LessonStatus.Cancelled; _service.SaveLesson(changed); }), quiet: true));
        panel.Children.Add(buttons); var card = Ui.Card(panel); card.Tag = "lesson:" + lesson.Id; return card;
    }
    internal void CopyFeedback(TutorLesson lesson) { try { WriteClipboard(lesson.ParentFeedback(StudentName(lesson.StudentId))); _saved.Text = "反馈已复制，可粘贴给家长"; } catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or UnauthorizedAccessException) { _saved.Text = "暂时无法访问剪贴板，请重试"; } }
    private void CalendarPage()
    {
        Header("授课日历", "每周课程、临时改期和课后记录，都在同一张日历里。");
        _actions.Children.Add(Ui.Button("＋ 添加课程", () => AddLesson(_selectedDate ?? Today), true));
        var bar = Ui.Row(Ui.Button("‹", () => MoveCalendar(-1)), Ui.Text(_year ? $"{_month:yyyy 年}" : $"{_month:yyyy 年 M 月}", 21, Ui.Accent, true),
            Ui.Button("›", () => MoveCalendar(1)), Ui.Button("本月", () => { _month = new(Today.Year, Today.Month, 1); _year = false; Render(); }), Ui.Button(_year ? "月历" : "全年总览", () => { _year = !_year; Render(); })); bar.Margin = new Thickness(0, 0, 0, 18); _body.Children.Add(bar);
        if (_year)
        {
            var months = new UniformGrid { Columns = 3 };
            for (var month = 1; month <= 12; month++)
            {
                var date = new DateOnly(_month.Year, month, 1); var stats = _service.Statistics(date, date.AddMonths(1).AddDays(-1));
                var button = Ui.Button("", () => { _month = date; _year = false; Render(); }); button.Content = Ui.Stack(Ui.Text($"{month} 月", 22, Ui.Accent, true), Ui.Text($"已上 {stats.Count} 节 · {Hours(stats.Minutes)} 小时", 12, Ui.Muted), Ui.Text($"¥{stats.Income:N2}", 17)); button.Height = 115; button.Margin = new Thickness(0, 0, 12, 12); months.Children.Add(button);
            }
            _body.Children.Add(months); return;
        }
        var weekdays = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 8) }; foreach (var day in new[] { "一", "二", "三", "四", "五", "六", "日" }) { var t = Ui.Text(day, 12, Ui.Muted); t.HorizontalAlignment = HorizontalAlignment.Center; weekdays.Children.Add(t); } _body.Children.Add(weekdays);
        var grid = new UniformGrid { Columns = 7 }; var offset = ((int)_month.DayOfWeek + 6) % 7;
        var all = _service.Lessons(_month, _month.AddMonths(1).AddDays(-1));
        for (var i = 0; i < offset; i++) grid.Children.Add(new Border());
        for (var d = 1; d <= DateTime.DaysInMonth(_month.Year, _month.Month); d++)
        {
            var day = new DateOnly(_month.Year, _month.Month, d); var lessons = all.Where(l => l.Date == day).ToList(); var taught = lessons.Where(l => l.Status == LessonStatus.Taught).ToList();
            var info = _holidays.ForDay(day); var panel = Ui.Stack(Ui.Text(d + (day == Today ? " · 今天" : ""), 15, day == Today ? Ui.Accent : Ui.Ink, true), Ui.Text(info.Lunar, 9, Ui.Muted));
            foreach (var l in lessons.Take(2)) { var text = Ui.Text($"{l.Start:HH:mm} {StudentName(l.StudentId)}", 10, l.Status == LessonStatus.Cancelled ? Ui.Muted : Ui.Accent); text.TextWrapping = TextWrapping.NoWrap; text.TextTrimming = TextTrimming.CharacterEllipsis; panel.Children.Add(text); }
            if (lessons.Count > 2) panel.Children.Add(Ui.Text($"另有 {lessons.Count - 2} 节", 9, Ui.Muted));
            if (taught.Count > 0) panel.Children.Add(Ui.Text($"{Hours(taught.Sum(l => l.Minutes))}h · ¥{taught.Sum(l => l.Income):0.##}", 10, Ui.Amber));
            var button = Ui.Button("", () => { _selectedDate = day; Render(); }); button.Content = panel; button.Height = 128; button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.VerticalContentAlignment = VerticalAlignment.Top; button.Padding = new Thickness(8); button.Margin = new Thickness(0, 0, 5, 5); button.Background = _selectedDate == day ? Ui.Selected : Ui.Surface; button.Tag = "day:" + day.ToString("yyyy-MM-dd"); grid.Children.Add(button);
        }
        _body.Children.Add(grid);
        var selected = _selectedDate ?? Today; _body.Children.Add(Ui.Label($"{selected:yyyy 年 M 月 d 日} · 课程详情"));
        var addSelectedDate = Ui.Button("＋  在这一天添加课程", () => AddLesson(selected), accent: true);
        addSelectedDate.FontSize = 16;
        addSelectedDate.FontWeight = FontWeights.SemiBold;
        addSelectedDate.MinHeight = 52;
        addSelectedDate.Padding = new Thickness(20, 14, 20, 14);
        addSelectedDate.Margin = new Thickness(0, 4, 6, 14);
        addSelectedDate.Tag = "add-selected-date-lesson";
        addSelectedDate.ToolTip = $"添加 {selected:yyyy 年 M 月 d 日} 的课程";
        _body.Children.Add(addSelectedDate);
        foreach (var l in _service.Lessons(selected, selected)) _body.Children.Add(LessonCard(l));
    }
    private void MoveCalendar(int direction)
    {
        var candidate = _year ? _month.AddYears(direction) : _month.AddMonths(direction);
        if (candidate.Year is < 1901 or > 2100) return;
        _month = candidate; _selectedDate = null; Render();
    }
    private void StudentPage()
    {
        Header("学生档案", "把每个学生的学习特点、课程反馈和成长过程连起来。"); _actions.Children.Add(Ui.Button("＋ 新增学生", () => EditStudent(), true));
        if (_student is { } id && _service.State.Students.FirstOrDefault(s => s.Id == id) is { } student)
        {
            _body.Children.Add(Ui.Row(Ui.Button("‹ 全部学生", () => { _student = null; Render(); }), Ui.Button("编辑资料", () => EditStudent(student)), Ui.Button("添加课程", () => AddLesson(Today, new TutorLesson { StudentId = student.Id, Date = Today, Subject = student.Subject, HourlyRate = student.HourlyRate }))));
            _body.Children.Add(Ui.Card(Ui.Stack(Ui.Text(student.Name, 26, Ui.Accent, true), Ui.Text($"{student.Grade} · {student.Subject} · 默认 ¥{student.HourlyRate:0.##}/小时 · {(student.Archived ? "已结课" : "在教")}", 13, Ui.Muted), StudentProfileDetails(student), Ui.Label("学习特点与长期目标"), Ui.Text(string.IsNullOrWhiteSpace(student.Notes) ? "尚未填写" : student.Notes))));
            Summary(_service.Statistics(new(1901, 1, 1), new(2100, 12, 31), student.Id));
            _body.Children.Add(Ui.Label("未来 30 天课程"));
            foreach (var l in _service.Lessons(Today, Today.AddDays(30)).Where(l => l.StudentId == student.Id && l.Status == LessonStatus.Planned)) _body.Children.Add(LessonCard(l));
            _body.Children.Add(Ui.Label("历史课程与反馈"));
            foreach (var l in _service.State.Lessons.Where(l => l.StudentId == student.Id && (l.Date < Today || l.Status != LessonStatus.Planned)).OrderByDescending(l => l.Date)) _body.Children.Add(LessonCard(l));
            return;
        }
        var search = new TextBox { Text = _studentSearch, Width = 260, Tag = "student-search", ToolTip = "搜索姓名、年级、科目、学校、授课地点或学习标签" }; _body.Children.Add(Ui.Row(search, Ui.Button("搜索学生", () => { _studentSearch = search.Text.Trim(); Render(); })));
        if (_service.State.Students.Count == 0) Empty("先添加第一位学生，设置默认科目与时薪，以后录课会自动填入。");
        foreach (var s in _service.State.Students.Where(s => string.IsNullOrEmpty(_studentSearch) || TutorStudentProfile.Matches(s, _studentSearch)).OrderBy(s => s.Archived).ThenBy(s => s.Name))
        {
            var panel = Ui.Stack(Ui.Text(s.Name, 20, Ui.Accent, true), Ui.Text($"{s.Grade} · {s.Subject} · ¥{s.HourlyRate:0.##}/小时 · {(s.Archived ? "已结课" : "在教")}", 12, Ui.Muted)); panel.Children.Add(StudentProfileDetails(s)); panel.Children.Add(Ui.Row(Ui.Button("查看档案", () => SelectStudent(s.Id)), Ui.Button("编辑", () => EditStudent(s)))); _body.Children.Add(Ui.Card(panel));
        }
    }
    private static StackPanel StudentProfileDetails(TutorStudent student)
    {
        var details = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        if (!string.IsNullOrWhiteSpace(student.School)) details.Children.Add(Ui.Text("就读学校：" + student.School, 12, Ui.Muted));
        if (!string.IsNullOrWhiteSpace(student.TeachingLocation)) details.Children.Add(Ui.Text("授课地点：" + student.TeachingLocation, 12, Ui.Muted));
        var tags = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
        foreach (var label in TutorStudentProfile.Labels(student)) { var badge = Ui.Badge(label); ((TextBlock)badge.Child).FontSize = 12; badge.Padding = new Thickness(9, 4, 9, 4); badge.Margin = new Thickness(0, 0, 7, 7); tags.Children.Add(badge); }
        if (tags.Children.Count > 0) details.Children.Add(tags);
        return details;
    }
    private ComboBox StudentFilter()
    {
        var choices = new List<TutorStudent> { new() { Id = "", Name = "全部学生" } }; choices.AddRange(_service.State.Students.OrderBy(s => s.Name));
        var combo = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValue = _filterStudent ?? "", Width = 165, Margin = new Thickness(8, 0, 8, 0), Padding = new Thickness(8) }; return combo;
    }
    private void StatisticsPage()
    {
        Header("收入统计", "只统计已确认授课的课程；收入归属实际授课日，按每节课的时薪计算。");
        var from = new DatePicker { SelectedDate = _from.ToDateTime(TimeOnly.MinValue), Width = 140 }; var to = new DatePicker { SelectedDate = _to.ToDateTime(TimeOnly.MinValue), Width = 140 }; var student = StudentFilter();
        _body.Children.Add(Ui.Row(from, Ui.Text(" 至 ", 12), to, student, Ui.Button("查询", () => Run(() => { _from = DateOnly.FromDateTime(from.SelectedDate ?? throw new ArgumentException("请选择开始日期。")); _to = DateOnly.FromDateTime(to.SelectedDate ?? throw new ArgumentException("请选择结束日期。")); _filterStudent = string.IsNullOrEmpty(student.SelectedValue as string) ? null : student.SelectedValue as string; _service.Statistics(_from, _to, _filterStudent); }))));
        _body.Children.Add(Ui.Row(Ui.Button("今天", () => { _from = _to = Today; Render(); }), Ui.Button("本周", () => { _from = Today.AddDays(-(((int)Today.DayOfWeek + 6) % 7)); _to = Today; Render(); }), Ui.Button("本月", () => { _from = new(Today.Year, Today.Month, 1); _to = Today; Render(); }), Ui.Button("今年", () => { _from = new(Today.Year, 1, 1); _to = Today; Render(); })));
        if (_to < _from || !TutorRules.ValidDate(_from) || !TutorRules.ValidDate(_to)) { Empty("请选择 1901–2100 年内的有效日期范围，结束日期不能早于开始日期。"); return; }
        Summary(_service.Statistics(_from, _to, _filterStudent));
        var lessons = _service.Lessons(_from, _to).Where(l => l.Status == LessonStatus.Taught && (_filterStudent is null || l.StudentId == _filterStudent)).ToList();
        if (lessons.Count == 0) { Empty("所选范围内还没有已授课程。安排中的课程不会计入授课收入。"); return; }
        _body.Children.Add(Ui.Label("学生收入明细"));
        foreach (var group in lessons.GroupBy(l => l.StudentId).OrderByDescending(g => g.Sum(l => l.Income))) _body.Children.Add(Ui.Card(Ui.Text($"{StudentName(group.Key)}    {group.Count()} 节 · {Hours(group.Sum(l => l.Minutes))} 小时    ¥{group.Sum(l => l.Income):N2}")));
        _body.Children.Add(Ui.Label(_to.DayNumber - _from.DayNumber > 62 ? "每月收入趋势" : "每日收入趋势"));
        var bars = lessons.GroupBy(l => _to.DayNumber - _from.DayNumber > 62 ? l.Date.ToString("yyyy-MM") : l.Date.ToString("yyyy-MM-dd")).OrderBy(g => g.Key).Select(g => (Label: g.Key, Amount: g.Sum(l => l.Income))).ToList(); var max = Math.Max(1, bars.Max(b => b.Amount));
        foreach (var bar in bars) { var label = Ui.Text(bar.Label, 12, Ui.Muted); label.Width = 95; var fill = new Border { Background = Ui.Accent, Width = Math.Max(2, 420 * (double)(bar.Amount / max)), Height = 15, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 8, 12, 8) }; _body.Children.Add(Ui.Row(label, fill, Ui.Text($"¥{bar.Amount:N2}", 12))); }
        _body.Children.Add(Ui.Label("课程明细")); foreach (var l in lessons.OrderByDescending(l => l.Date)) _body.Children.Add(LessonCard(l));
    }
    private void RecordsPage()
    {
        Header("教学记录", "搜索教学内容、学习问题与方法，积累自己的教学经验。");
        var search = new TextBox { Text = _search, Width = 290 }; var student = StudentFilter();
        var status = new ComboBox { ItemsSource = new[] { "全部记录", "待确认课程", "待填写反馈", "已授课", "已取消" }, SelectedIndex = _recordStatus, Padding = new Thickness(8), Width = 140 };
        _body.Children.Add(Ui.Row(search, student, status, Ui.Button("搜索", () => { _search = search.Text.Trim(); _filterStudent = string.IsNullOrEmpty(student.SelectedValue as string) ? null : student.SelectedValue as string; _recordStatus = status.SelectedIndex; Render(); }, true)));
        if (_recordFrom is { } from && _recordTo is { } to)
        {
            var clear = Ui.Button($"{from:yyyy-MM-dd} 至 {to:yyyy-MM-dd} · 清除日期筛选", () => { _recordFrom = null; _recordTo = null; Render(); }, quiet: true); clear.Tag = "records-clear-range"; _body.Children.Add(clear);
        }
        var first = _recordFrom ?? new DateOnly(1901, 1, 1); var last = _recordTo ?? new DateOnly(2100, 12, 31);
        var pendingEnd = DateOnly.FromDayNumber(Math.Min(last.DayNumber, Today.DayNumber - 1));
        IEnumerable<TutorLesson> records = _recordStatus == 1
            ? pendingEnd < first ? [] : _service.Lessons(first, pendingEnd).Where(l => l.Status == LessonStatus.Planned)
            : _service.State.Lessons.Where(l => l.Date >= first && l.Date <= last);
        records = records.Where(l => (_filterStudent is null || l.StudentId == _filterStudent) && (_recordStatus switch { 2 => l.Status == LessonStatus.Taught && !l.HasFeedback, 3 => l.Status == LessonStatus.Taught, 4 => l.Status == LessonStatus.Cancelled, _ => true }) &&
            (string.IsNullOrEmpty(_search) || string.Join("\n", StudentName(l.StudentId), l.Subject, l.Content, l.Progress, l.Problems, l.Methods, l.NextPlan, l.PrivateNotes).Contains(_search, StringComparison.OrdinalIgnoreCase)));
        var list = records.OrderByDescending(l => l.Date).ToList(); _body.Children.Add(Ui.Label($"找到 {list.Count} 条记录"));
        foreach (var l in list) _body.Children.Add(LessonCard(l)); if (list.Count == 0) Empty("还没有匹配记录。可以换一个学生、状态或关键词。");
    }
    private int _recordStatus;
    private void SettingsPage()
    {
        Header("设置与备份", $"E²-Day 家教版 {typeof(TutorWindow).Assembly.GetName().Version!.ToString(3)} · 记录保存在这台电脑上。");
        if (IsExample)
        {
            _body.Children.Add(Ui.Card(Ui.Text("当前为临时示例体验，关闭后丢弃本次修改。请返回自己的记录进行账号管理、导出或恢复备份。", 14, Ui.Muted)));
            return;
        }
        _body.Children.Add(ExampleEntry());
        _body.Children.Add(Ui.Card(Ui.Stack(Ui.Text("本机账号 · " + AccountName, 18, Ui.Accent, true), Ui.Text("每个账号拥有独立的学生与课程记录。", 13, Ui.Muted), Ui.Button("切换 / 管理账号登录", () => SwitchAccount?.Invoke()))));
        var panel = Ui.Stack(Ui.Text("保护你的教学记录", 20, Ui.Accent, true), Ui.Text("运行当天自动备份，保留最近 30 天；导出的家教版备份包含学生、课程、反馈、图片和待办。恢复会替换当前账号记录，恢复前会另存一份备份。", 13, Ui.Muted));
        panel.Children.Add(Ui.Row(Ui.Button("导出家教版备份", ExportBackup, true), Ui.Button("从备份恢复", RestoreBackup))); panel.Children.Add(Ui.Label("当前账号数据目录")); panel.Children.Add(Ui.Text(_store.DirectoryPath, 12, Ui.Muted));
        if (!string.IsNullOrEmpty(BackupWarning)) panel.Children.Add(Ui.Text(BackupWarning, 12, Ui.Amber));
        _body.Children.Add(Ui.Card(panel));
        _body.Children.Add(Ui.Card(Ui.Stack(Ui.Text("关于 E²-Day 家教版", 20, Ui.Accent, true), Ui.Text("记录每一堂课，看见每一步成长。\n\n授课收入表示已上课程产生的收入；当前不记录实际到账、课包或欠费。家教版使用独立的数据与备份格式，当前仅支持 Windows 电脑。\n\n本机账号用于区分访问，数据库和导出备份没有加密。请把备份保存到自己合适的位置。\n\n保留版权。允许下载并运行正式发布程序。", 13, Ui.Muted))));
    }
    private void ExportBackup()
    { var picker = new SaveFileDialog { FileName = $"E2-Day-Tutor-{Today:yyyyMMdd}.json", Filter = "家教版备份 (*.json)|*.json" }; if (picker.ShowDialog(this) == true) Run(() => { _store.Export(picker.FileName, _service.State); MessageBox.Show(this, "家教版备份已导出。", "备份完成"); }); }
    private void RestoreBackup()
    {
        var picker = new OpenFileDialog { Filter = "家教版备份 (*.json)|*.json" }; if (picker.ShowDialog(this) != true) return;
        Run(() => { var preview = TutorStore.ReadBackup(picker.FileName); if (MessageBox.Show(this, $"备份包含 {preview.State.Students.Count} 位学生、{preview.State.Lessons.Count} 条课程记录。\n恢复将替换当前账号记录，先保留一份当前备份。是否继续？", "恢复家教版备份", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) { _service.Restore(picker.FileName); _student = null; _dashboardStudent = null; } });
    }
}

