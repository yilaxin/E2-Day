using System.Windows;
using System.Windows.Controls;
using DailyRecord.Core;

namespace DailyRecord.App;

internal sealed class EditDialog : Window
{
    private readonly TextBox _title;
    private readonly TextBox _notes;
    private readonly DatePicker _date;
    private readonly ComboBox _importance;
    private readonly ComboBox _repeat;
    private readonly ComboBox _scope;
    private readonly CheckBox _importantTask;
    private readonly TextBlock _hint;
    private readonly bool _isEvent;
    private readonly bool _isRecurring;
    public string ItemTitle => _title.Text.Trim();
    public string Notes => _notes.Text.Trim();
    public DateOnly Date => DateOnly.FromDateTime(_date.SelectedDate!.Value);
    public EventImportance Importance => (EventImportance)_importance.SelectedIndex;
    public RepeatRule Repeat => (RepeatRule)_repeat.SelectedIndex;
    public EditScope Scope => _scope.SelectedIndex == 1 ? EditScope.ThisAndFuture : EditScope.ThisOccurrence;
    public bool ImportantTask => _importantTask.IsChecked == true;

    public EditDialog(Window owner, DateOnly date, bool isEvent, TaskItem? task = null, EventDefinition? ev = null)
    {
        if (owner.IsLoaded) Owner = owner;
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = task is null ? isEvent ? "安排一件事" : "添加任务" : isEvent ? "编辑日历事件" : "编辑任务";
        Width = 510; SizeToContent = SizeToContent.Height; MaxHeight = 870; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _isEvent = isEvent;
        _isRecurring = ev is { Repeat: not RepeatRule.None };
        _title = new TextBox { Text = task?.Title ?? "", MaxLength = 500 };
        _notes = new TextBox { Text = task?.Notes ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 82, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _date = new DatePicker { SelectedDate = date.ToDateTime(TimeOnly.MinValue), DisplayDateStart = new DateTime(1901, 1, 1), DisplayDateEnd = new DateTime(2100, 12, 31) };
        _importance = new ComboBox { ItemsSource = new[] { "普通 · 当天提醒", "重要 · 提前提醒", "非常重要 · 提前提醒" }, SelectedIndex = (int)(task?.EventImportance ?? EventImportance.Normal) };
        _repeat = new ComboBox { ItemsSource = Enum.GetValues<RepeatRule>().Select(Ui.Repeat).ToList(), SelectedIndex = (int)(ev?.Repeat ?? RepeatRule.None) };
        _scope = new ComboBox { ItemsSource = new[] { "仅这一次", "这一次及以后" }, SelectedIndex = 0 };
        _importantTask = new CheckBox { Content = "★ 标记为重要任务", IsChecked = task?.IsImportant ?? false, Foreground = Ui.Amber };
        _hint = Ui.Text("", 11, Ui.Muted);
        _hint.Margin = new Thickness(0, 8, 0, 0);

        var form = Ui.Stack(Ui.Text(Title, 23, bold: true), Ui.Text("保存后会自动记录在本地。", 12, Ui.Muted),
            Ui.Label("标题"), _title, Ui.Label("日期"), _date);
        if (isEvent)
        {
            form.Children.Add(Ui.Label("事件重要程度 · 决定提前提醒天数"));
            form.Children.Add(_importance);
            if (_isRecurring)
            {
                form.Children.Add(Ui.Label("修改范围")); form.Children.Add(_scope);
            }
            form.Children.Add(Ui.Label("重复规则")); form.Children.Add(_repeat); form.Children.Add(_hint);
        }
        form.Children.Add(Ui.Label("备注（可选）")); form.Children.Add(_notes);
        if (!isEvent || task is not null)
        {
            _importantTask.Margin = new Thickness(0, 18, 0, 0);
            form.Children.Add(_importantTask);
            form.Children.Add(Ui.Text("只突出显示此任务，不改变事件提醒日期。", 11, Ui.Muted));
        }
        var actions = Ui.Row(Ui.Button("取消", () => DialogResult = false), Ui.Button("保存", Accept, accent: true));
        actions.HorizontalAlignment = HorizontalAlignment.Right; actions.Margin = new Thickness(0, 24, 0, 0);
        form.Children.Add(actions);
        Content = new ScrollViewer { Content = form, Padding = new Thickness(28) };
        _scope.SelectionChanged += (_, _) => UpdateHint();
        _repeat.SelectionChanged += (_, _) => UpdateHint();
        _date.SelectedDateChanged += (_, _) => UpdateHint();
        Loaded += (_, _) => { _title.Focus(); UpdateHint(); };
    }

    private void UpdateHint()
    {
        _repeat.IsEnabled = !_isRecurring || Scope == EditScope.ThisAndFuture;
        if (_isRecurring && Scope == EditScope.ThisOccurrence)
        { _hint.Text = "只修改这一次的安排，其余重复日期保持原规则。"; return; }
        if (_isEvent && Repeat == RepeatRule.LunarYearly && _date.SelectedDate is { } date)
        {
            var day = DateOnly.FromDateTime(date);
            var lunar = LunarDates.FromSolar(day);
            _hint.Text = lunar is null ? "此日期超出农历支持范围。" :
                $"按农历 {lunar.Month} 月 {lunar.Day} 日逐年重复，不额外生成闰月。" +
                (lunar.IsLeapMonth ? $" 首次日期按普通月份调整为 {LunarDates.NormalizeRegularMonth(day):yyyy-MM-dd}。" : "");
        }
        else _hint.Text = Repeat == RepeatRule.Monthly ? "遇到不存在的日期，使用当月最后一天，后续仍按原始日期重复。" : "每次分别记录完成状态。";
    }
    private void Accept()
    {
        if (string.IsNullOrWhiteSpace(ItemTitle)) { MessageBox.Show(this, "请填写标题。", "还差一个标题"); _title.Focus(); return; }
        if (_date.SelectedDate is null) { MessageBox.Show(this, "请选择有效日期。", "检查日期"); return; }
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
