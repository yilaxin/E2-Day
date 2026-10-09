using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DailyRecord.Core;

namespace DailyRecord.App;

public enum PageKind { Today, Year, History, Settings }

public partial class MainWindow : Window
{
    private readonly TaskService _service;
    private readonly SqliteStore _store;
    private readonly HolidayService _holidays;
    private readonly bool _smoke;
    private readonly DispatcherTimer _dayTimer;
    private readonly Dictionary<PageKind, Button> _nav = [];
    private readonly CancellationTokenSource _lifetime = new();
    private PageKind _page;
    private DateOnly _lastToday;
    private DateOnly _historyDate;
    private int _year;
    private bool _wide;
    private bool _fetchingNews;
    private string _newsStatus = "";
    private TextBox? _quickInput;
    private CheckBox? _quickImportant;
    private string _quickDraft = "";
    private bool _quickFlag;
    private string _search = "";
    private bool _includeArchived;

    public MainWindow(TaskService service, SqliteStore store, HolidayService holidays, bool smoke = false)
    {
        InitializeComponent();
        _service = service; _store = store; _holidays = holidays; _smoke = smoke;
        _lastToday = service.Today; _historyDate = service.Today; _year = service.Today.Year;
        if (!smoke)
        {
            Width = Math.Max(MinWidth, Math.Min(1360, SystemParameters.WorkArea.Width - 36));
            Height = Math.Max(MinHeight, Math.Min(940, SystemParameters.WorkArea.Height - 48));
        }
        _wide = Width >= 1240;
        foreach (var pair in new[] { (PageKind.Today, "◷  今日清单"), (PageKind.Year, "▦  全年日历"),
                     (PageKind.History, "≡  历史记录"), (PageKind.Settings, "⚙  偏好设置") })
        {
            var destination = pair.Item1;
            var button = Ui.Button(pair.Item2, () => Navigate(destination));
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Padding = new Thickness(14, 12, 10, 12); button.Margin = new Thickness(0, 0, 0, 9);
            button.FontSize = 13;
            NavigationPanel.Children.Add(button); _nav[destination] = button;
        }
        _service.Changed += UpdateSaveStatus;
        _dayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _dayTimer.Tick += (_, _) =>
        {
            if (_service.Today != _lastToday)
            {
                _lastToday = _service.Today;
                Run(_service.RefreshDay);
                _ = RefreshNewsAsync(false);
            }
        };
        _dayTimer.Start();
        Closed += (_, _) => { _dayTimer.Stop(); _lifetime.Cancel(); _service.Changed -= UpdateSaveStatus; };
        SizeChanged += (_, _) =>
        {
            var wide = ActualWidth >= 1240;
            if (wide == _wide || !IsLoaded) return;
            _wide = wide; RefreshPage();
        };
        Navigate(PageKind.Today);
        Loaded += async (_, _) => { if (!smoke) await RefreshNewsAsync(false); };
    }

    public void Navigate(PageKind page)
    {
        RememberDraft(); _page = page; _selectedMonth = null;
        if (page == PageKind.Year) CalendarScroller.ScrollToTop(); else PageScroller.ScrollToTop();
        RefreshPage();
    }
    private void RememberDraft()
    {
        if (_quickInput is not null) _quickDraft = _quickInput.Text;
        if (_quickImportant is not null) _quickFlag = _quickImportant.IsChecked == true;
    }
    private void RefreshPage()
    {
        RememberDraft(); _quickInput = null; _quickImportant = null;
        PageBody.Children.Clear(); HeaderActions.Children.Clear();
        PageScroller.Visibility = _page == PageKind.Year ? Visibility.Collapsed : Visibility.Visible;
        // Hidden keeps the year layout valid in its own viewport, without affecting other pages' scroll extent.
        CalendarScroller.Visibility = _page == PageKind.Year && _selectedMonth is null ? Visibility.Visible : Visibility.Hidden;
        MonthScroller.Visibility = _page == PageKind.Year && _selectedMonth is not null ? Visibility.Visible : Visibility.Hidden;
        foreach (var pair in _nav)
        {
            pair.Value.Background = pair.Key == _page ? Ui.Selected : Brushes.Transparent;
            pair.Value.Foreground = pair.Key == _page ? Ui.Accent : Ui.Muted;
            pair.Value.FontWeight = pair.Key == _page ? FontWeights.SemiBold : FontWeights.Normal;
        }
        UpdateSaveStatus();
        switch (_page)
        {
            case PageKind.Today: BuildToday(); break;
            case PageKind.Year: if (_selectedMonth is null) BuildYear(); else BuildMonth(); break;
            case PageKind.History: BuildHistory(); break;
            case PageKind.Settings: BuildSettings(); break;
        }
    }
    private void Header(string eyebrow, string title, string subtitle)
    { PageEyebrow.Text = eyebrow; PageTitle.Text = title; PageSubtitle.Text = subtitle; }
    private void UpdateSaveStatus()
    {
        SidebarDate.Text = _service.Today.ToString("yyyy 年 M 月 d 日");
        SaveStatusText.Text = _service.SaveStatus;
    }
    private void Run(Action action, Action? refresh = null)
    {
        try { action(); RefreshPage(); refresh?.Invoke(); }
        catch (Exception ex)
        {
            if (_smoke) throw;
            MessageBox.Show(this, ex.Message, "操作未完成", MessageBoxButton.OK, MessageBoxImage.Information);
            UpdateSaveStatus();
        }
    }

    private void BuildToday()
    {
        var today = _service.Today;
        var information = _holidays.ForDay(today);
        Header("每一天，都有自己的节奏", today.ToString("M 月 d 日 · dddd", CultureInfo.GetCultureInfo("zh-CN")),
            $"{today:yyyy 年}  ·  {information.Lunar}  ·  {information.WorkStatus}");
        Button? copyImage = null;
        copyImage = Ui.Button("复制为图片", async () => await CopyTodayTasksImageAsync(copyImage!));
        copyImage.Tag = "copy-today-image"; copyImage.ToolTip = "复制今日待办和已完成为图片，粘贴即可分享";
        HeaderActions.Children.Add(copyImage);
        HeaderActions.Children.Add(Ui.Button("＋ 安排事件", () => OpenEventEditor(today)));
        HeaderActions.Children.Add(Ui.Button("＋ 添加任务", () => OpenTaskEditor(today), accent: true));

        var open = _service.VisibleOpen(today).ToList();
        var done = _service.State.Tasks.Where(t => t.State == TaskState.Completed && t.CompletedOn == today).ToList();
        var viewed = open.Where(t => _service.IsViewed(t, today)).ToList();
        var pending = open.Where(t => !_service.IsViewed(t, today) && t.ScheduledDate <= today).ToList();
        var upcoming = open.Where(t => !_service.IsViewed(t, today) && t.ScheduledDate > today).ToList();

        var stats = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        for (var i = 0; i < 3; i++) stats.ColumnDefinitions.Add(new ColumnDefinition());
        var values = new[] { (open.Count.ToString(), "待处理事项", "看过提醒仍算未完成"),
            (done.Count.ToString(), "今天已完成", "保留每一份完成记录"), (open.Count(t => t.IsImportant).ToString(), "重要任务", "用星标突出今天的重点") };
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            var caption = Ui.Stack(Ui.Text(value.Item2, 12, Ui.Muted), Ui.Text(value.Item1, 31, i == 2 ? Ui.Amber : Ui.Accent, bold: true), Ui.Text(value.Item3, 10, Ui.Muted));
            var card = Ui.Card(caption, new Thickness(18, 15, 18, 14));
            card.Margin = new Thickness(0, 0, i < 2 ? 12 : 0, 0); Grid.SetColumn(card, i); stats.Children.Add(card);
        }
        PageBody.Children.Add(stats);

        var left = new StackPanel();
        var eventTitles = _service.EventTitles(today);
        var plan = Ui.Stack(Ui.Text("今天的安排", 12, Ui.Muted, bold: true));
        if (information.Festivals.Length > 0)
        {
            var festival = Ui.Text(information.Festivals, 15, Ui.Accent, bold: true);
            festival.Margin = new Thickness(0, 8, 0, 2); plan.Children.Add(festival);
        }
        var arrangement = Ui.Text(eventTitles.Count > 0 ? string.Join("  /  ", eventTitles) : "今天没有日历事件，可以留一点空间给自己。", 12, Ui.Muted);
        arrangement.Margin = new Thickness(0, 8, 0, 0); plan.Children.Add(arrangement);
        left.Children.Add(Ui.Card(plan));

        _quickInput = new TextBox { Text = _quickDraft, ToolTip = "写下任务标题，按 Enter 添加", Margin = new Thickness(0, 0, 8, 0) };
        _quickInput.KeyDown += (_, e) => { if (e.Key == Key.Enter) { AddQuickTask(); e.Handled = true; } };
        _quickImportant = new CheckBox { Content = "★ 重要", IsChecked = _quickFlag, Foreground = Ui.Amber, Margin = new Thickness(3, 0, 10, 0) };
        var quick = new Grid { Margin = new Thickness(0, 0, 0, 17) };
        quick.ColumnDefinitions.Add(new ColumnDefinition()); quick.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        quick.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        quick.Children.Add(_quickInput); Grid.SetColumn(_quickImportant, 1); quick.Children.Add(_quickImportant);
        var add = Ui.Button("添加", AddQuickTask, accent: true); Grid.SetColumn(add, 2); quick.Children.Add(add);
        var quickCaption = Ui.Text("写下今天要完成的任务，按 Enter 添加", 11, Ui.Muted);
        quickCaption.Margin = new Thickness(0, 0, 0, 8); left.Children.Add(quickCaption); left.Children.Add(quick);

        left.Children.Add(TaskGroup("今日待办", "到期与逾期任务都在这里", pending, "还没有待办，写下今天的一件事吧。"));
        if (upcoming.Count > 0) left.Children.Add(TaskGroup("提前提醒", "先看一眼，提前准备", upcoming, ""));
        if (viewed.Count > 0) left.Children.Add(TaskGroup("今日已查看", "明天会再次提醒，完成后才会停止", viewed, "", dim: true));
        left.Children.Add(TaskGroup("今日已完成", "做过的事，都会留在这里", done, "完成第一件任务后，会在这里留下记录。", dim: true));
        var right = BuildNewsPanel();
        if (_wide)
        {
            var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition());
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            left.Margin = new Thickness(0, 0, 20, 0); columns.Children.Add(left); Grid.SetColumn(right, 1); columns.Children.Add(right); PageBody.Children.Add(columns);
        }
        else { PageBody.Children.Add(left); PageBody.Children.Add(right); }
    }

    private void AddQuickTask()
    {
        var title = _quickInput?.Text ?? "";
        if (string.IsNullOrWhiteSpace(title)) { _quickInput?.Focus(); return; }
        var important = _quickImportant?.IsChecked == true;
        Run(() =>
        {
            _service.AddTask(title, "", _service.Today, important);
            _quickDraft = ""; _quickFlag = false;
            if (_quickInput is not null) _quickInput.Text = "";
            if (_quickImportant is not null) _quickImportant.IsChecked = false;
        });
        _quickInput?.Focus();
    }

    private Border TaskGroup(string title, string subtitle, IEnumerable<TaskItem> tasks, string empty, bool dim = false)
    {
        var list = tasks.ToList();
        var heading = Ui.Row(Ui.Text(title, 16, bold: true), Ui.Badge(list.Count.ToString()));
        ((FrameworkElement)heading.Children[0]).Margin = new Thickness(0, 0, 10, 0);
        var panel = Ui.Stack(heading);
        var caption = Ui.Text(subtitle, 10, Ui.Muted); caption.Margin = new Thickness(0, 5, 0, 12); panel.Children.Add(caption);
        if (list.Count == 0)
        {
            var placeholder = Ui.Text(empty, 12, Ui.Muted); placeholder.Margin = new Thickness(0, 12, 0, 12); panel.Children.Add(placeholder);
        }
        else foreach (var task in TaskService.Sort(list)) panel.Children.Add(TaskRow(task, dim: dim));
        return Ui.Card(panel, new Thickness(17));
    }

    private Border TaskRow(TaskItem task, bool dim = false, Action? refresh = null)
    {
        var content = new Grid(); content.ColumnDefinitions.Add(new ColumnDefinition());
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Ui.Text(task.Title, 13, bold: task.IsImportant);
        if (task.State == TaskState.Completed) title.TextDecorations = TextDecorations.Strikethrough;
        var left = Ui.Stack(title); left.Margin = new Thickness(0, 0, 8, 0);
        var badges = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
        if (task.IsImportant) badges.Children.Add(Ui.Badge("★ 重要任务", true));
        if (task.IsEvent) badges.Children.Add(Ui.Badge(Ui.Importance(task.EventImportance), task.EventImportance != EventImportance.Normal));
        var dateLabel = task.ScheduledDate.ToString("M 月 d 日");
        if (task.State == TaskState.Completed) dateLabel += $" · {task.CompletedOn:M/d} 已完成";
        else if (task.State == TaskState.Cancelled) dateLabel += " · " + task.ArchiveReason;
        else if (task.ScheduledDate < _service.Today) dateLabel += $" · 逾期 {_service.Today.DayNumber - task.ScheduledDate.DayNumber} 天";
        else if (task.ScheduledDate > _service.Today) dateLabel += $" · 还有 {task.ScheduledDate.DayNumber - _service.Today.DayNumber} 天";
        badges.Children.Add(Ui.Text(dateLabel, 10, task.ScheduledDate < _service.Today && task.State == TaskState.Open ? Ui.Amber : Ui.Muted));
        left.Children.Add(badges);
        if (!string.IsNullOrWhiteSpace(task.Notes))
        { var note = Ui.Text(task.Notes, 11, Ui.Muted); note.MaxHeight = 44; note.TextTrimming = TextTrimming.CharacterEllipsis; note.Margin = new Thickness(0, 6, 0, 0); left.Children.Add(note); }
        if (task.ImageIds.Count > 0) left.Children.Add(NoteImages.Gallery(_service.GetNoteImages(task.ImageIds), this));
        content.Children.Add(left);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        var star = Ui.Button(task.IsImportant ? "★" : "☆", () => Run(() => _service.ToggleImportant(task), refresh), quiet: true);
        star.Tag = "important:" + task.Id; star.Foreground = task.IsImportant ? Ui.Amber : Ui.Muted;
        star.FontSize = 19; star.Padding = new Thickness(5, 4, 5, 4); star.ToolTip = task.IsImportant ? "取消重要标记" : "标记为重要任务";
        System.Windows.Automation.AutomationProperties.SetName(star, task.IsImportant ? "取消重要标记" : "标记为重要任务");
        actions.Children.Add(star);
        if (task.State == TaskState.Open && task.IsEvent)
        {
            var seen = Ui.Button(_service.IsViewed(task, _service.Today) ? "撤销查看" : "看过", () => Run(() => _service.ToggleViewed(task), refresh), quiet: true);
            seen.FontSize = 11; seen.Padding = new Thickness(5, 7, 5, 7); seen.Tag = "viewed:" + task.Id; actions.Children.Add(seen);
        }
        if (task.State != TaskState.Cancelled)
        {
            var complete = Ui.Button(task.State == TaskState.Completed ? "撤销" : "完成", () => Run(() => _service.ToggleCompleted(task), refresh), quiet: task.State == TaskState.Completed);
            complete.FontSize = 11; complete.Padding = new Thickness(9, 7, 9, 7); complete.Tag = "complete:" + task.Id;
            actions.Children.Add(complete);
        }
        else actions.Children.Add(Ui.Button("恢复", () => Run(() => _service.RestoreTask(task), refresh)));
        var more = Ui.Button("⋯", () => { }, quiet: true); more.FontSize = 18; more.Padding = new Thickness(4, 2, 4, 2); more.ToolTip = "编辑、改期或取消";
        var menu = new ContextMenu();
        if (task.State != TaskState.Cancelled)
        {
            var edit = new MenuItem { Header = "编辑 / 改期" }; edit.Click += (_, _) => EditExisting(task, refresh); menu.Items.Add(edit);
            if (task.State == TaskState.Open)
            { var cancel = new MenuItem { Header = "取消并归档" }; cancel.Click += (_, _) => CancelExisting(task, refresh); menu.Items.Add(cancel); }
        }
        else { var restore = new MenuItem { Header = "恢复此任务" }; restore.Click += (_, _) => Run(() => _service.RestoreTask(task), refresh); menu.Items.Add(restore); }
        more.ContextMenu = menu; more.Click += (_, _) => { menu.PlacementTarget = more; menu.IsOpen = true; }; actions.Children.Add(more);
        Grid.SetColumn(actions, 1); content.Children.Add(actions);
        return new Border
        {
            Child = content, Padding = new Thickness(12, 11, 8, 11), Margin = new Thickness(0, 0, 0, 8),
            CornerRadius = new CornerRadius(9), Background = task.IsImportant ? Ui.ImportantFill : Ui.SubtleSurface,
            BorderBrush = task.IsImportant ? Ui.ImportantLine : Ui.Line,
            BorderThickness = task.IsImportant ? new Thickness(3, 1, 1, 1) : new Thickness(1),
            Opacity = dim || task.State != TaskState.Open ? 0.62 : 1
        };
    }

    private void OpenTaskEditor(DateOnly date, Action? refresh = null)
    {
        var dialog = new EditDialog(this, date, false);
        if (dialog.ShowDialog() == true) Run(() => _service.AddTask(dialog.ItemTitle, dialog.Notes, dialog.Date, dialog.ImportantTask, dialog.Images), refresh);
    }
    private void OpenEventEditor(DateOnly date, Action? refresh = null)
    {
        var dialog = new EditDialog(this, date, true);
        if (dialog.ShowDialog() == true) Run(() => _service.AddEvent(dialog.ItemTitle, dialog.Notes, dialog.Date, dialog.Importance, dialog.Repeat, dialog.Images), refresh);
    }
    private void EditExisting(TaskItem task, Action? refresh = null)
    {
        var ev = task.IsEvent ? _service.State.Events.FirstOrDefault(e => e.Id == task.EventId) : null;
        var dialog = new EditDialog(this, task.ScheduledDate, task.IsEvent, task, ev, _service.GetNoteImages(task.ImageIds));
        if (dialog.ShowDialog() != true) return;
        Run(() =>
        {
            if (task.IsEvent)
            {
                _service.EditEvent(task, dialog.ItemTitle, dialog.Notes, dialog.Date, dialog.Importance, dialog.Repeat, dialog.Scope, dialog.Images);
                if (task.IsImportant != dialog.ImportantTask) _service.ToggleImportant(task);
            }
            else _service.EditTask(task, dialog.ItemTitle, dialog.Notes, dialog.Date, dialog.ImportantTask, dialog.Images);
        }, refresh);
    }
    private void CancelExisting(TaskItem task, Action? refresh = null)
    {
        if (task.IsEvent)
        {
            var ev = _service.State.Events.Single(e => e.Id == task.EventId);
            var scope = EditScope.ThisOccurrence;
            if (ev.Repeat != RepeatRule.None)
            { var dialog = new ScopeDialog(this); if (dialog.ShowDialog() != true) return; scope = dialog.Scope; }
            Run(() => _service.CancelEvent(task, scope), refresh);
        }
        else Run(() => _service.CancelTask(task), refresh);
    }

    private FrameworkElement BuildNewsPanel()
    {
        var heading = Ui.Row(Ui.Text("AI 资讯", 16, bold: true));
        var refresh = Ui.Button(_fetchingNews ? "更新中…" : "刷新", () => _ = RefreshNewsAsync(true), quiet: true);
        refresh.IsEnabled = !_fetchingNews; refresh.FontSize = 11; refresh.Margin = new Thickness(8, 0, 0, 0); heading.Children.Add(refresh);
        var panel = Ui.Stack(heading);
        var updated = _service.State.News.FetchedAt is { } fetched ? $"更新于 {fetched.ToLocalTime():M/d HH:mm}" : "联网后获取官方来源资讯";
        var status = Ui.Text(string.IsNullOrEmpty(_newsStatus) ? updated : _newsStatus + "\n" + updated, 10, Ui.Muted);
        status.Margin = new Thickness(0, 7, 0, 14); panel.Children.Add(status);
        if (_service.State.News.Articles.Count == 0)
        {
            var empty = Ui.Text("暂无可展示的 AI 资讯。\n\n任务和日历可以继续离线使用。", 12, Ui.Muted);
            empty.Margin = new Thickness(0, 12, 0, 18); panel.Children.Add(empty);
        }
        foreach (var article in _service.State.News.Articles)
        {
            var item = Ui.Stack();
            var meta = Ui.Text($"{article.Source}  ·  {article.PublishedAt.ToLocalTime():M/d}  ·  {article.Language}", 9, Ui.Muted);
            item.Children.Add(meta);
            var title = Ui.Text(article.Title, 13, bold: true); title.Margin = new Thickness(0, 7, 0, 5); item.Children.Add(title);
            if (article.Summary.Length > 0)
            { var summary = Ui.Text(article.Summary, 11, Ui.Muted); summary.MaxHeight = 68; summary.TextTrimming = TextTrimming.CharacterEllipsis; item.Children.Add(summary); }
            var link = Ui.Button("阅读原文 ↗", () => OpenUrl(article.Url), quiet: true); link.FontSize = 10;
            link.HorizontalAlignment = HorizontalAlignment.Left; link.Padding = new Thickness(0, 7, 0, 4); item.Children.Add(link);
            panel.Children.Add(new Border { Child = item, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 10, 0, 12) });
        }
        panel.Children.Add(Ui.Text("最多 5 条 · 中文优先 · 保留来源", 9, Ui.Muted));
        return Ui.Card(new Expander { Header = "关注 AI 的新进展", IsExpanded = true, Content = panel, FontSize = 11, Foreground = Ui.Muted }, new Thickness(17));
    }

    private async Task RefreshNewsAsync(bool force)
    {
        if (_fetchingNews || _smoke || _lifetime.IsCancellationRequested) return;
        if (!force && _service.State.News.FetchedAt is { } fetched && DateOnly.FromDateTime(fetched.LocalDateTime) == _service.Today) return;
        _fetchingNews = true; _newsStatus = "正在更新资讯…";
        if (_page == PageKind.Today) RefreshPage();
        try
        {
            var result = await new NewsService().FetchAsync(DateTimeOffset.Now, _lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;
            _service.State.News = new NewsCache { FetchedAt = DateTimeOffset.Now, Articles = result.Articles };
            _service.Save();
            _newsStatus = result.SuccessfulSources == result.TotalSources ? "" : $"已更新 · {result.SuccessfulSources}/{result.TotalSources} 个来源可用";
        }
        catch (Exception ex) { if (!_lifetime.IsCancellationRequested) _newsStatus = ex is HttpRequestException or TaskCanceledException ? "联网更新失败，保留已有缓存" : "资讯更新失败，个人记录可正常使用"; }
        finally
        {
            _fetchingNews = false;
            if (!_lifetime.IsCancellationRequested && _page == PageKind.Today) RefreshPage();
        }
    }
    private void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { if (!_smoke) MessageBox.Show(this, ex.Message, "无法打开浏览器"); }
    }
}
