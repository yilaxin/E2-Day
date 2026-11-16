using System.Windows;
using System.Windows.Controls;
using DailyRecord.Core;

namespace DailyRecord.App;

internal sealed class EditDialog : Window
{
    private readonly TextBox _title;
    private readonly TextBox _notes;
    private readonly DatePicker _date;
    private readonly ComboBox _kind;
    private readonly ComboBox _importance;
    private readonly ComboBox _repeat;
    private readonly ComboBox _scope;
    private readonly CheckBox _importantTask;
    private readonly ComboBox _color;
    private readonly ComboBox _colorScope;
    private readonly StackPanel _colorScopeFields;
    public TaskColor Color => (TaskColor)_color.SelectedIndex;
    public EditScope ColorScope => _colorScopeFields.Visibility == Visibility.Visible && _colorScope.SelectedIndex == 1 ? EditScope.ThisAndFuture : EditScope.ThisOccurrence;
    private readonly TextBlock _hint;
    private readonly TextBlock _heading;
    private readonly TextBlock _dateLabel;
    private readonly StackPanel _eventFields;
    private readonly StackPanel _scopeFields;
    private readonly NoteImageEditor _images;
    private readonly DurationEditor _duration;
    public int? DurationDays => _duration.Days;
    public bool DurationChanged => Kind == ItemKind.LongTerm && _duration.IsEnabled &&
        (_definition is null || _originalKind != ItemKind.LongTerm || DurationDays != OriginalDuration);
    private int? OriginalDuration => _definition?.LongTermPeriods.LastOrDefault() is { End: { } end } period ? Math.Max(1, end.DayNumber - period.Start.DayNumber + 1) : null;
    private readonly TaskItem? _task;
    private readonly EventDefinition? _definition;
    private readonly TaskService? _service;
    private readonly ItemKind _originalKind;
    private readonly DateTime _originalDate;
    private DateTime? _conversionDateDraft;
    private bool _dateLocked;
    public string ItemTitle => _title.Text.Trim();
    public string Notes => _notes.Text.Trim();
    public IReadOnlyList<NoteImage> Images => _images.Images;
    internal NoteImageEditor ImageEditor => _images;
    public DateOnly Date => DateOnly.FromDateTime(_date.SelectedDate!.Value);
    public ItemKind Kind => (ItemKind)_kind.SelectedIndex;
    public EventImportance Importance => Kind == ItemKind.Event ? (EventImportance)_importance.SelectedIndex : EventImportance.Normal;
    public RepeatRule Repeat => Kind == ItemKind.LongTerm ? RepeatRule.Daily : Kind == ItemKind.Task ? RepeatRule.None : (RepeatRule)_repeat.SelectedIndex;
    public EditScope Scope => _scope.SelectedIndex == 1 ? EditScope.ThisAndFuture : EditScope.ThisOccurrence;
    public bool ImportantTask => _importantTask.IsChecked == true;
    public bool LongTermTask => Kind == ItemKind.LongTerm;

    public EditDialog(Window owner, DateOnly date, bool isEvent, TaskItem? task = null, EventDefinition? ev = null,
        IEnumerable<NoteImage>? images = null, TaskService? service = null, ItemKind? initialKind = null, TaskColor? initialColor = null)
    {
        if (owner.IsLoaded) Owner = owner;
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Width = 580; SizeToContent = SizeToContent.Height; MaxHeight = Math.Min(920, SystemParameters.WorkArea.Height - 50);
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _task = task; _definition = ev; _service = service;
        _originalDate = date.ToDateTime(TimeOnly.MinValue);
        _originalKind = ev?.Repeat == RepeatRule.Daily ? ItemKind.LongTerm : task?.IsEvent == true || isEvent ? ItemKind.Event : ItemKind.Task;
        _kind = new ComboBox { ItemsSource = new[] { "普通任务", "日历事件", "长期任务" }, SelectedIndex = (int)(initialKind ?? _originalKind), Tag = "item-kind" };
        _heading = Ui.Text("", 23, bold: true);
        _title = new TextBox { Text = task?.Title ?? "", MaxLength = 500, Tag = "task-title" };
        _notes = new TextBox { Text = task?.Notes ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 82,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Tag = "item-notes" };
        _images = new NoteImageEditor(images);
        _date = new DatePicker { SelectedDate = date.ToDateTime(TimeOnly.MinValue), DisplayDateStart = new DateTime(1901, 1, 1),
            DisplayDateEnd = new DateTime(2100, 12, 31), Tag = "item-date" };
        _dateLabel = Ui.Label("日期");
        _importance = new ComboBox { ItemsSource = new[] { "普通", "重要", "非常重要" },
            SelectedIndex = (int)(task?.EventImportance ?? EventImportance.Normal), Tag = "item-reminder" };
        _repeat = new ComboBox { ItemsSource = Enum.GetValues<RepeatRule>().Where(r => r != RepeatRule.Daily).Select(Ui.Repeat).ToArray(),
            SelectedIndex = ev?.Repeat == RepeatRule.Daily ? 0 : (int)(ev?.Repeat ?? RepeatRule.None), Tag = "item-repeat" };
        _scope = new ComboBox { SelectedIndex = 0, Tag = "item-scope" };
        _importantTask = new CheckBox { Content = "★ 标记为重要任务", IsChecked = task?.IsImportant ?? false,
            Foreground = Ui.Amber, Margin = new Thickness(0, 16, 0, 0), Tag = "item-important" };
        _hint = Ui.Text("", 11, Ui.Muted); _hint.Tag = "item-preview"; _hint.Margin = new Thickness(0, 12, 0, 0);
        _color = ColorStyle.Picker(service?.State.Settings ?? new UserSettings(), task?.Color ?? initialColor ?? TaskColor.None, "item-color");
        _colorScope = new ComboBox { ItemsSource = new[] { "仅这一次", "这次及后续未完成安排（保留单次分类）" }, SelectedIndex = 0, Tag = "item-color-scope" };
        _colorScopeFields = Ui.Stack(Ui.Label("颜色影响范围"), _colorScope);
        var originalPeriod = ev?.LongTermPeriods.LastOrDefault();
        var days = ev?.Repeat == RepeatRule.Daily ? originalPeriod?.End is { } final ? Math.Max(1, final.DayNumber - originalPeriod.Start.DayNumber + 1) : (int?)null : 30;
        _duration = new DurationEditor(() => Kind == ItemKind.LongTerm && _originalKind == ItemKind.LongTerm && _definition is not null ?
            _service?.DurationEditStart(_definition) ?? _definition.StartDate : _date.SelectedDate is { } selected ? DateOnly.FromDateTime(selected) : date, days);
        _scopeFields = Ui.Stack(Ui.Label("影响范围"), _scope);
        _eventFields = Ui.Stack(Ui.Label("事件重要程度 · 决定提前提醒天数"), _importance, Ui.Label("重复规则"), _repeat);
        var form = Ui.Stack(_heading, Ui.Text("保存前可调整类型，原有内容和历史继续保留。", 12, Ui.Muted),
            Ui.Label("事项类型"), _kind, Ui.Label("标题"), _title, _dateLabel, _date, _scopeFields, _eventFields, _duration, _hint,
            Ui.Label("颜色分类"), _color, _colorScopeFields, Ui.Label("备注 · 文字与图片（可选）"), _notes, _images, _importantTask,
            Ui.Text("星标只突出显示，不改变提前提醒日期。", 11, Ui.Muted));
        var cancel = Ui.Button("取消", () => DialogResult = false); cancel.Tag = "item-cancel"; cancel.IsCancel = true;
        var save = Ui.Button("保存", Accept, accent: true); save.Tag = "item-save";
        var actions = Ui.Row(cancel, save); actions.HorizontalAlignment = HorizontalAlignment.Right; actions.Margin = new Thickness(28, 12, 28, 20);
        var layout = new Grid(); layout.RowDefinitions.Add(new RowDefinition()); layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(new ScrollViewer { Content = form, Padding = new Thickness(28, 28, 28, 10), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Grid.SetRow(actions, 1); layout.Children.Add(actions); Content = layout;
        _kind.SelectionChanged += (_, _) => UpdateHint(); _importance.SelectionChanged += (_, _) => UpdateHint();
        _scope.SelectionChanged += (_, _) => UpdateHint(); _repeat.SelectionChanged += (_, _) => UpdateHint(); _date.SelectedDateChanged += (_, _) => UpdateHint();
        Loaded += (_, _) => { _title.Focus(); UpdateHint(); }; UpdateHint();
    }

    private void UpdateHint()
    {
        var converting = _task is not null && Kind != _originalKind;
        Title = _task is null ? Kind == ItemKind.Task ? "添加任务" : Kind == ItemKind.Event ? "安排事件" : "添加长期任务" :
            converting ? "转换事项类型" : Kind == ItemKind.Task ? "编辑任务" : Kind == ItemKind.Event ? "编辑日历事件" : "编辑长期任务";
        _heading.Text = Title;
        _dateLabel.Text = Kind == ItemKind.LongTerm ? _originalKind == ItemKind.LongTerm && !converting ? "本次日期" : "长期任务开始日期" : "安排日期";
        var lockDate = _task is not null && _originalKind == ItemKind.LongTerm && !converting;
        if (lockDate && !_dateLocked)
        { _conversionDateDraft = _date.SelectedDate; _dateLocked = true; _date.SelectedDate = _originalDate; }
        else if (!lockDate && _dateLocked)
        { _dateLocked = false; _date.SelectedDate = _conversionDateDraft ?? _originalDate; }
        _date.IsEnabled = !lockDate;
        _eventFields.Visibility = Kind == ItemKind.Event ? Visibility.Visible : Visibility.Collapsed;
        _duration.Visibility = Kind == ItemKind.LongTerm ? Visibility.Visible : Visibility.Collapsed;
        _duration.IsEnabled = Kind != _originalKind || _task?.State != TaskState.Cancelled &&
            (_definition?.Repeat != RepeatRule.Daily || _definition.EndDate is null || _definition.EndDate >= (_service?.Today ?? DateOnly.FromDateTime(DateTime.Today)));
        _duration.UpdatePreview();
        var recurring = _definition is { Repeat: not RepeatRule.None };
        _colorScopeFields.Visibility = recurring && !converting ? Visibility.Visible : Visibility.Collapsed;
        var archivedEdit = _task?.State == TaskState.Cancelled && !converting;
        var showScope = recurring && (converting || Kind == ItemKind.Event) && !archivedEdit;
        _scopeFields.Visibility = showScope ? Visibility.Visible : Visibility.Collapsed;
        var labels = converting ? new[] { "仅转换这一次，原后续安排继续", "转换这次，并停止原后续安排" } : new[] { "仅这一次", "这一次及以后" };
        if (_scope.ItemsSource is not string[] previous || !previous.SequenceEqual(labels))
        { var index = Math.Max(0, _scope.SelectedIndex); _scope.ItemsSource = labels; _scope.SelectedIndex = index; }
        _repeat.IsEnabled = !archivedEdit && (!recurring || converting || Scope == EditScope.ThisAndFuture);
        _importance.IsEnabled = !archivedEdit;
        var hints = new List<string>();
        if (archivedEdit) hints.Add("归档事项仅修改本条内容，不恢复待办或改动旧重复规则；也可切换成其他类型并保留归档状态。");
        if (converting)
        {
            hints.Add("保留当前事项的完成状态、星标、备注图片和历史；转换本身不计入完成统计。");
            if (_task!.State == TaskState.Cancelled) hints.Add("归档状态保留，转换后不会自动重新开始；需要待办时再恢复。");
            if (recurring)
                hints.Add(Scope == EditScope.ThisOccurrence ? "原规则的其他日期继续，本次不会重新生成。" :
                    $"从原本次日期 {_task!.OccurrenceDate:yyyy/M/d} 起停止旧规则；已生成的其他未完成安排 {_service?.ConversionAffectedCount(_task, Scope) ?? 0} 项将归档，已完成历史保留。");
        }
        if (_date.SelectedDate is { })
        {
            if (Kind == ItemKind.Event)
            {
                var lead = (_service?.State.Settings ?? new UserSettings()).LeadDays(Importance);
                hints.Add($"当前设置：提前 {lead} 天，预计从 {Date.AddDays(-lead):yyyy/M/d} 开始提醒。");
                if (Repeat == RepeatRule.LunarYearly)
                {
                    var lunar = LunarDates.FromSolar(Date);
                    hints.Add(lunar is null ? "此日期超出农历支持范围。" : $"按农历 {lunar.Month} 月 {lunar.Day} 日逐年重复，不额外生成闰月。" +
                        (lunar.IsLeapMonth ? $" 首次日期调整为 {LunarDates.NormalizeRegularMonth(Date):yyyy/M/d}。" : ""));
                }
                else if (Repeat == RepeatRule.Monthly) hints.Add("遇到不存在的日期使用当月最后一天，后续仍按原始日期重复。");
            }
            else if (Kind == ItemKind.LongTerm)
                hints.Add(_originalKind == ItemKind.LongTerm && !converting ? "修改用于今天未完成及后续安排，过去的记录保留。结束请使用清单上的‘结束’。" :
                    $"从 {Date:yyyy/M/d} 开始每天出现，不提前提醒；按上方持续时间执行，到期再决定是否继续。");
            else hints.Add($"按普通任务在 {Date:yyyy/M/d} 进入今日清单，不再使用事件的提前提醒，未完成会继续保留。");
        }
        _hint.Text = string.Join("\n", hints);
    }

    private void Accept()
    {
        if (!_images.PendingImport.IsCompleted) { MessageBox.Show(this, "图片还在处理中，请稍等。", "正在添加图片"); return; }
        if (string.IsNullOrWhiteSpace(ItemTitle)) { MessageBox.Show(this, "请填写标题。", "还差一个标题"); _title.Focus(); return; }
        if (_date.SelectedDate is null) { MessageBox.Show(this, "请选择有效日期。", "检查日期"); return; }
        if (Kind == ItemKind.LongTerm && _duration.IsEnabled)
        {
            try { _ = _duration.Period; if (_definition is not null && _originalKind == Kind && DurationChanged) _service?.ValidateLongTermDuration(_definition, DurationDays); }
            catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, "检查持续时间"); return; }
        }
        DialogResult = true;
    }
}


internal sealed class ScopeDialog : Window
{
    public EditScope Scope { get; private set; }
    public ScopeDialog(Window owner)
    {
        Owner = owner; Title = "取消重复事件"; Width = 430; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = Ui.Stack(Ui.Text("取消哪些安排？", 22, bold: true),
            Ui.Text("记录会保留，可在历史页恢复某一次。", 12, Ui.Muted),
            Ui.Button("仅这一次", () => { Scope = EditScope.ThisOccurrence; DialogResult = true; }),
            Ui.Button("这一次及以后", () => { Scope = EditScope.ThisAndFuture; DialogResult = true; }),
            Ui.Button("返回", () => DialogResult = false));
        ((StackPanel)Content).Margin = new Thickness(26);
        foreach (FrameworkElement child in ((StackPanel)Content).Children) child.Margin = new Thickness(0, 0, 0, 12);
    }
}
