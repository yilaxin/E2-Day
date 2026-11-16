using System.Windows;
using System.Windows.Controls;
using DailyRecord.Core;

namespace DailyRecord.App;

internal sealed class DurationEditor : StackPanel
{
    internal ComboBox Preset { get; }
    private readonly TextBox _days;
    private readonly DatePicker _end;
    private readonly TextBlock _preview;
    private readonly Func<DateOnly> _start;
    internal int? Days
    {
        get
        {
            if (Preset.SelectedIndex == 4) return null;
            if (Preset.SelectedIndex == 0) return 7;
            if (Preset.SelectedIndex == 1) return 30;
            if (Preset.SelectedIndex == 3)
            {
                if (_end.SelectedDate is null) throw new ArgumentException("请选择结束日期。");
                return DateOnly.FromDateTime(_end.SelectedDate.Value).DayNumber - _start().DayNumber + 1;
            }
            if (!int.TryParse(_days.Text, out var days)) throw new ArgumentException("请填写有效的持续天数。");
            return days;
        }
    }
    internal LongTermPeriod Period => LongTermSchedule.Create(_start(), Days);
    internal DurationEditor(Func<DateOnly> start, int? days = 30)
    {
        _start = start;
        Preset = new ComboBox { ItemsSource = new[] { "7 天", "30 天", "自定义天数", "指定结束日期", "持续进行，手动结束" },
            SelectedIndex = days is null ? 4 : days == 7 ? 0 : days == 30 ? 1 : 2, Tag = "period-duration" };
        _days = new TextBox { Text = (days ?? 30).ToString(), MaxLength = 4, Tag = "period-days" };
        _end = new DatePicker { SelectedDate = days is { } length ? start().AddDays(Math.Clamp(length - 1, 0, 3649)).ToDateTime(TimeOnly.MinValue) : null,
            DisplayDateStart = new(1901, 1, 1), DisplayDateEnd = new(2100, 12, 31), Tag = "period-end" };
        _preview = Ui.Text("", 11, Ui.Muted); _preview.Tag = "period-preview"; _preview.Margin = new Thickness(0, 8, 0, 0);
        Children.Add(Ui.Label("持续时间 · 开始当天计入")); Children.Add(Preset); Children.Add(_days); Children.Add(_end); Children.Add(_preview);
        Preset.SelectionChanged += (_, _) => UpdatePreview(); _days.TextChanged += (_, _) => UpdatePreview(); _end.SelectedDateChanged += (_, _) => UpdatePreview(); UpdatePreview();
    }
    internal void UpdatePreview()
    {
        _days.Visibility = Preset.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        _end.Visibility = Preset.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        try
        {
            var period = Period;
            _preview.Text = period.End is { } end ? $"{period.Start:yyyy/M/d} 至 {end:yyyy/M/d}，共 {Days} 天。最后一天再决定是否续期。" : $"{period.Start:yyyy/M/d} 起持续进行，直到手动结束。";
            _preview.Foreground = Ui.Muted;
        }
        catch (ArgumentException ex) { _preview.Text = ex.Message; _preview.Foreground = Ui.Amber; }
    }
}

internal sealed class RenewPeriodDialog : Window
{
    private readonly DatePicker _start;
    private readonly DurationEditor _duration;
    public DateOnly Start => DateOnly.FromDateTime(_start.SelectedDate!.Value);
    public int? Days => _duration.Days;
    internal RenewPeriodDialog(Window owner, TaskService service, EventDefinition ev)
    {
        if (owner.IsLoaded) Owner = owner;
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "继续一个阶段"; Width = 520; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var earliest = ev.EndDate == service.Today ? service.Today.AddDays(1) : service.Today;
        if (earliest < ev.StartDate) earliest = ev.StartDate;
        _start = new DatePicker { SelectedDate = earliest.ToDateTime(TimeOnly.MinValue), DisplayDateStart = earliest.ToDateTime(TimeOnly.MinValue), DisplayDateEnd = new(2100, 12, 31), Tag = "period-start" };
        var previous = ev.LongTermPeriods.LastOrDefault();
        var length = previous?.End is { } end ? Math.Clamp(end.DayNumber - previous.Start.DayNumber + 1, 1, 3650) : 30;
        _duration = new DurationEditor(() => _start.SelectedDate is { } selected ? DateOnly.FromDateTime(selected) : earliest, length);
        _start.SelectedDateChanged += (_, _) => _duration.UpdatePreview();
        var error = Ui.Text("", 11, Ui.Amber); error.Tag = "renew-error";
        var save = Ui.Button("确认续期", () =>
        {
            try
            {
                if (_start.SelectedDate is null || Start < earliest) throw new ArgumentException("请选择今天或到期次日以后的开始日期。");
                _ = _duration.Period; DialogResult = true;
            }
            catch (ArgumentException ex) { error.Text = ex.Message; }
        }, accent: true); save.Tag = "renew-save";
        var cancel = Ui.Button("取消", () => DialogResult = false); cancel.IsCancel = true; cancel.Tag = "renew-cancel";
        var form = Ui.Stack(Ui.Text(ev.Title, 22, bold: true), Ui.Text("沿用原任务、图文备注、分类与累计打卡；中间空档不补出任务。", 12, Ui.Muted),
            Ui.Label("下一阶段开始日期"), _start, _duration, error, Ui.Row(cancel, save));
        form.Margin = new Thickness(26); Content = form;
    }
}

public partial class MainWindow
{
    internal Func<RenewPeriodDialog, bool?>? RenewalPresenter { get; set; }
    private readonly HashSet<string> _periodSnoozed = [];
    private DateOnly? _periodSnoozedDay;
    private void OpenRenewal(EventDefinition ev, Action? refresh = null)
    {
        var dialog = new RenewPeriodDialog(this, _service, ev);
        if ((RenewalPresenter is { } show ? show(dialog) : dialog.ShowDialog()) == true)
            RunColorChange(() => _service.RenewLongTerm(ev, dialog.Start, dialog.Days), refresh);
    }
    private void ResumeLongTermItem(TaskItem task, Action? refresh)
    {
        var ev = _service.State.Events.Single(e => e.Id == task.EventId);
        if (ev.LongTermPeriods.Count > 0 && task.State == TaskState.Cancelled && LongTermSchedule.Contains(ev, task.ScheduledDate))
            RunColorChange(() => _service.RestoreTask(task), refresh);
        else if (ev.LongTermPeriods.Count > 0) OpenRenewal(ev, refresh);
        else Run(() => _service.ResumeLongTerm(task), refresh);
    }
    private string ProgressText(EventDefinition ev, DateOnly? occurrence = null)
    {
        var progress = _service.PeriodProgress(ev, occurrence);
        return progress.ElapsedDays == 0 ? $"{progress.Period.Start:M/d} 开始 · 本阶段已完成 {progress.CompletedDays} 天" :
            $"本阶段第 {progress.ElapsedDays}{(progress.TotalDays is { } total ? "/" + total : "")} 天 · 已完成 {progress.CompletedDays} 天";
    }
    private void BuildPeriodDecisions(StackPanel parent)
    {
        if (_periodSnoozedDay != _service.Today) { _periodSnoozed.Clear(); _periodSnoozedDay = _service.Today; }
        var decisions = _service.LongTermDecisions().Where(e => !_periodSnoozed.Contains(e.Id)).ToList();
        if (decisions.Count == 0) return;
        var body = Ui.Stack(Ui.Text("阶段到期 · 由你决定下一步", 16, bold: true), Ui.Text("到期不自动续期。今日完成与续期分别处理，过去打卡继续保留。", 11, Ui.Muted));
        foreach (var ev in decisions)
        {
            var overdue = ev.EndDate < _service.Today;
            var title = Ui.Text(ev.Title + (overdue ? " · 已到期待决定" : " · 本阶段最后一天"), 13, bold: true); title.Tag = "period-decision:" + ev.Id;
            var period = ev.LongTermPeriods[^1];
            var details = Ui.Text($"{period.Start:yyyy/M/d}—{period.End:yyyy/M/d} · {ProgressText(ev)}", 11, Ui.Muted);
            var renew = Ui.Button("继续一个阶段", () => OpenRenewal(ev)); renew.Tag = "period-renew:" + ev.Id;
            var finish = Ui.Button(overdue ? "结束并保留记录" : "按期结束", () => RunColorChange(() => _service.ConfirmLongTermEnd(ev)));
            finish.Tag = "period-confirm:" + ev.Id; finish.ToolTip = "今天仍可完成，到期次日停止出现";
            var later = Ui.Button("稍后决定", () => RunColorChange(() => _periodSnoozed.Add(ev.Id)), quiet: true); later.Tag = "period-later:" + ev.Id;
            var actions = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) }; actions.Children.Add(renew); actions.Children.Add(finish); actions.Children.Add(later);
            var row = Ui.Stack(title, details, actions); row.Margin = new Thickness(0, 14, 0, 0); body.Children.Add(row);
        }
        parent.Children.Add(GlassCardBorder.Create(body, new Thickness(17)));
    }
}
