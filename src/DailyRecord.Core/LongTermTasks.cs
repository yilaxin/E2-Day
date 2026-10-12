namespace DailyRecord.Core;

public sealed partial class TaskService
{
    public bool IsLongTerm(TaskItem task) => Definition(task)?.Repeat == RepeatRule.Daily;
    public bool IsLongTermActive(TaskItem task) => Definition(task) is { Repeat: RepeatRule.Daily } ev &&
        (ev.EndDate is null || ev.EndDate >= Today);
    private bool IsLongTermActive(TaskItem task, DateOnly day) => Definition(task) is { Repeat: RepeatRule.Daily } ev &&
        ev.StartDate <= day && (ev.EndDate is null || ev.EndDate >= day);
    private EventDefinition? Definition(TaskItem task) => State.Events.FirstOrDefault(e => e.Id == task.EventId);
    private EventDefinition LongTermDefinition(TaskItem task) => Definition(task) is { Repeat: RepeatRule.Daily } ev ? ev :
        throw new ArgumentException("此任务不是长期任务。");

    public TaskItem AddLongTermTask(string title, string notes, DateOnly start, bool important = false, IReadOnlyList<NoteImage>? images = null)
    {
        var ev = CreateDefinition(title, notes, start, EventImportance.Normal, RepeatRule.Daily);
        ev.IsImportant = important; ev.ImageIds = PrepareImages(images);
        State.Events.Add(ev); var task = EnsureOccurrence(ev, start); EnsureUpcoming(); Save(images); return task;
    }

    public void ConvertToLongTerm(TaskItem task, string title, string notes, DateOnly start, bool important, IReadOnlyList<NoteImage>? images = null)
    {
        if (task.IsEvent) throw new ArgumentException("日历事件请使用重复规则设置长期任务。");
        var ev = CreateDefinition(title, notes, start, EventImportance.Normal, RepeatRule.Daily);
        ev.IsImportant = important; ev.ImageIds = images is null ? [.. task.ImageIds] : PrepareImages(images);
        State.Events.Add(ev);
        task.EventId = ev.Id; task.OccurrenceDate = start; task.ScheduledDate = start; task.Title = ev.Title;
        task.Notes = ev.Notes; task.ImageIds = [.. ev.ImageIds]; task.IsImportant = important;
        task.EventImportance = EventImportance.Normal; task.HasOverride = false;
        EnsureUpcoming(); Save(images);
    }

    public void EditLongTerm(TaskItem selected, string title, string notes, bool important, IReadOnlyList<NoteImage>? images = null)
    {
        var ev = LongTermDefinition(selected); ValidateTitle(title);
        var ids = images is null ? selected.ImageIds : PrepareImages(images);
        ev.Title = title.Trim(); ev.Notes = notes.Trim(); ev.ImageIds = [.. ids]; ev.IsImportant = important;
        foreach (var task in State.Tasks.Where(t => t.EventId == ev.Id && t.ScheduledDate >= Today && t.State == TaskState.Open))
        {
            task.Title = ev.Title; task.Notes = ev.Notes; task.ImageIds = [.. ids]; task.IsImportant = important;
        }
        if (selected.ScheduledDate == Today && selected.State == TaskState.Completed)
        { selected.Notes = ev.Notes; selected.ImageIds = [.. ids]; }
        EnsureUpcoming(); Save(images);
    }

    private void SetLongTermImportance(TaskItem selected)
    {
        var ev = LongTermDefinition(selected); ev.IsImportant = selected.IsImportant;
        foreach (var task in State.Tasks.Where(t => t.EventId == ev.Id && t.ScheduledDate > Today && t.State == TaskState.Open))
            task.IsImportant = ev.IsImportant;
    }

    public void EndLongTerm(TaskItem selected)
    {
        var ev = LongTermDefinition(selected);
        if (ev.EndDate < Today) return;
        ev.EndDate = Today.AddDays(-1);
        foreach (var task in State.Tasks.Where(t => t.EventId == ev.Id && t.State == TaskState.Open && t.ScheduledDate >= Today))
            CancelWithoutSave(task, "长期任务已结束");
        State.Activities.Add(Snapshot(selected, ActivityKind.Ended)); Save();
    }

    public void ResumeLongTerm(TaskItem selected)
    {
        var ev = LongTermDefinition(selected); ev.EndDate = null;
        foreach (var task in State.Tasks.Where(t => t.EventId == ev.Id && t.ScheduledDate >= Today &&
                     t.State == TaskState.Cancelled && (t.ArchiveReason == "长期任务已结束" || t.ScheduledDate == Today || t.Id == selected.Id)))
        {
            task.State = TaskState.Open; task.ArchiveReason = "";
            foreach (var activity in State.Activities.Where(a => a.TaskId == task.Id && a.Kind == ActivityKind.Cancelled && a.Day == Today))
                activity.Reverted = true;
        }
        EnsureUpcoming(); Save();
    }
}
