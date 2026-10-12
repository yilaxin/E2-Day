namespace DailyRecord.Core;

public sealed partial class TaskService
{
    private readonly SqliteStore _store;
    private readonly IClock _clock;
    public AppState State { get; private set; }
    public string SaveStatus { get; private set; } = "已保存到本地";
    public long Revision { get; private set; }
    public event Action? Changed;
    public DateOnly Today => _clock.Today;

    public TaskService(SqliteStore store, IClock clock)
    {
        _store = store;
        _clock = clock;
        State = store.Load();
        EnsureUpcoming();
        Save();
    }

    public void Save(IEnumerable<NoteImage>? images = null)
    {
        // Also invalidate readers if validation fails and State is reloaded.
        Revision++;
        try { _store.Save(State, images); }
        catch { State = _store.Load(); throw; }
        try
        {
            _store.DailyBackup(State, Today);
            SaveStatus = $"已保存 · {_clock.Now:HH:mm}";
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        { SaveStatus = "记录已保存，自动备份失败，请检查存储空间或权限"; }
        Changed?.Invoke();
    }

    public void RefreshDay()
    {
        EnsureUpcoming();
        Save();
    }

    private void EnsureUpcoming()
    {
        foreach (var ev in State.Events)
            foreach (var day in Recurrence.Between(ev, ev.Repeat == RepeatRule.Daily ? Today : ev.StartDate,
                         ev.Repeat == RepeatRule.Daily ? Today : Today.AddDays(State.Settings.LeadDays(ev.Importance))))
                EnsureOccurrence(ev, day);
    }

    public TaskItem EnsureOccurrence(EventDefinition ev, DateOnly occurrence)
    {
        var existing = State.Tasks.FirstOrDefault(t => t.EventId == ev.Id &&
            (ev.Repeat == RepeatRule.None || t.OccurrenceDate == occurrence));
        if (existing is not null) return existing;
        var task = new TaskItem
        {
            EventId = ev.Id, OccurrenceDate = occurrence, ScheduledDate = occurrence,
            OriginalScheduledDate = occurrence, Title = ev.Title, Notes = ev.Notes,
            ImageIds = [.. ev.ImageIds], IsImportant = ev.IsImportant,
            EventImportance = ev.Importance, CreatedAt = _clock.Now
        };
        State.Tasks.Add(task);
        Revision++;
        return task;
    }

    public TaskItem AddTask(string title, string notes, DateOnly date, bool important = false, IReadOnlyList<NoteImage>? images = null)
    {
        ValidateTitle(title);
        var ids = PrepareImages(images);
        var task = new TaskItem
        {
            Title = title.Trim(), Notes = notes.Trim(), ScheduledDate = date,
            OriginalScheduledDate = date, IsImportant = important, CreatedAt = _clock.Now, ImageIds = ids
        };
        State.Tasks.Add(task);
        Save(images);
        return task;
    }

    public EventDefinition AddEvent(string title, string notes, DateOnly date, EventImportance importance, RepeatRule repeat, IReadOnlyList<NoteImage>? images = null)
    {
        var ev = CreateDefinition(title, notes, date, importance, repeat);
        ev.ImageIds = PrepareImages(images);
        State.Events.Add(ev);
        EnsureUpcoming();
        EnsureOccurrence(ev, ev.StartDate);
        Save(images);
        return ev;
    }

    private static EventDefinition CreateDefinition(string title, string notes, DateOnly date, EventImportance importance, RepeatRule repeat)
    {
        ValidateTitle(title);
        var lunar = repeat == RepeatRule.LunarYearly
            ? LunarDates.FromSolar(date) ?? throw new ArgumentException("农历年度事件支持 1901 至 2100 年。") : null;
        return new EventDefinition
        {
            Title = title.Trim(), Notes = notes.Trim(), Importance = importance, Repeat = repeat,
            StartDate = lunar is null ? date : LunarDates.NormalizeRegularMonth(date),
            LunarMonth = lunar?.Month ?? 0, LunarDay = lunar?.Day ?? 0
        };
    }

    public void ToggleImportant(TaskItem task)
    {
        task.IsImportant = !task.IsImportant;
        if (IsLongTerm(task) && task.ScheduledDate == Today) SetLongTermImportance(task);
        Save();
    }

    public bool IsViewed(TaskItem task, DateOnly day) => State.Activities.Any(a =>
        a.TaskId == task.Id && a.Kind == ActivityKind.Viewed && a.Day == day && !a.Reverted);

    public void ToggleViewed(TaskItem task)
    {
        if (!task.IsEvent || task.State != TaskState.Open) return;
        var existing = State.Activities.LastOrDefault(a => a.TaskId == task.Id &&
            a.Kind == ActivityKind.Viewed && a.Day == Today && !a.Reverted);
        if (existing is not null) existing.Reverted = true;
        else State.Activities.Add(Snapshot(task, ActivityKind.Viewed));
        Save();
    }

    public void ToggleCompleted(TaskItem task)
    {
        if (task.State == TaskState.Cancelled) return;
        if (task.State == TaskState.Completed)
        {
            task.State = TaskState.Open;
            task.CompletedOn = null;
            foreach (var activity in State.Activities.Where(a => a.TaskId == task.Id && a.Kind == ActivityKind.Completed && !a.Reverted))
                activity.Reverted = true;
        }
        else
        {
            task.State = TaskState.Completed;
            task.CompletedOn = Today;
            State.Activities.Add(Snapshot(task, ActivityKind.Completed));
        }
        Save();
    }

    public void CancelTask(TaskItem task)
    {
        if (task.State != TaskState.Open) return;
        CancelWithoutSave(task, "已取消");
        Save();
    }

    private void CancelWithoutSave(TaskItem task, string reason)
    {
        task.State = TaskState.Cancelled;
        task.ArchiveReason = reason;
        State.Activities.Add(Snapshot(task, ActivityKind.Cancelled));
    }

    public void RestoreTask(TaskItem task)
    {
        if (task.State != TaskState.Cancelled) return;
        task.State = TaskState.Open;
        task.ArchiveReason = "";
        task.HasOverride = true;
        foreach (var activity in State.Activities.Where(a => a.TaskId == task.Id && a.Kind == ActivityKind.Cancelled && !a.Reverted))
            activity.Reverted = true;
        Save();
    }

    public void EditTask(TaskItem task, string title, string notes, DateOnly date, bool important, IReadOnlyList<NoteImage>? images = null)
    {
        ValidateTitle(title);
        var ids = images is null ? task.ImageIds : PrepareImages(images);
        task.Title = title.Trim();
        task.Notes = notes.Trim();
        task.ImageIds = [.. ids];
        task.ScheduledDate = date;
        task.IsImportant = important;
        task.HasOverride = task.IsEvent;
        Save(images);
    }

    public void EditEvent(TaskItem selected, string title, string notes, DateOnly date,
        EventImportance importance, RepeatRule repeat, EditScope scope, IReadOnlyList<NoteImage>? images = null)
    {
        var old = State.Events.Single(e => e.Id == selected.EventId);
        var replacement = CreateDefinition(title, notes, date, importance,
            old.Repeat != RepeatRule.None && scope == EditScope.ThisOccurrence ? RepeatRule.None : repeat);
        replacement.ImageIds = images is null ? [.. selected.ImageIds] : PrepareImages(images);
        if (replacement.Repeat == RepeatRule.Daily) replacement.IsImportant = selected.IsImportant;
        if (old.Repeat == RepeatRule.None)
        {
            old.Title = replacement.Title;
            old.Notes = replacement.Notes;
            old.ImageIds = [.. replacement.ImageIds];
            old.StartDate = replacement.StartDate;
            old.Importance = replacement.Importance;
            old.Repeat = replacement.Repeat;
            old.IsImportant = replacement.IsImportant;
            old.LunarMonth = replacement.LunarMonth;
            old.LunarDay = replacement.LunarDay;
            old.EndDate = null;
            selected.OccurrenceDate = replacement.StartDate;
            ApplyDefinition(selected, old, replacement.StartDate);
        }
        else if (scope == EditScope.ThisOccurrence)
        {
            if (selected.State == TaskState.Open)
            {
                ApplyDefinition(selected, replacement, date);
                selected.HasOverride = true;
            }
        }
        else
        {
            var anchor = selected.OccurrenceDate!.Value;
            old.EndDate = anchor.AddDays(-1);
            State.Events.Add(replacement);
            foreach (var task in State.Tasks.Where(t => t.EventId == old.Id && t.Id != selected.Id &&
                t.OccurrenceDate >= anchor && t.State == TaskState.Open))
                CancelWithoutSave(task, "重复安排已更新");
            selected.EventId = replacement.Id;
            selected.OccurrenceDate = replacement.StartDate;
            selected.HasOverride = false;
            ApplyDefinition(selected, replacement, replacement.StartDate);
        }
        if (selected.State != TaskState.Open && images is not null)
        {
            // The task's current notes can change; activity snapshots keep the original images.
            selected.Notes = notes.Trim(); selected.ImageIds = [.. replacement.ImageIds];
        }
        EnsureUpcoming();
        Save(images);
    }

    private static void ApplyDefinition(TaskItem task, EventDefinition ev, DateOnly date)
    {
        if (task.State != TaskState.Open) return;
        task.Title = ev.Title;
        task.Notes = ev.Notes;
        task.ImageIds = [.. ev.ImageIds];
        task.ScheduledDate = date;
        task.EventImportance = ev.Importance;
    }

    public void CancelEvent(TaskItem selected, EditScope scope)
    {
        var ev = State.Events.Single(e => e.Id == selected.EventId);
        if (ev.Repeat == RepeatRule.None || scope == EditScope.ThisAndFuture)
        {
            var anchor = selected.OccurrenceDate!.Value;
            ev.EndDate = anchor.AddDays(-1);
            foreach (var task in State.Tasks.Where(t => t.EventId == ev.Id && t.OccurrenceDate >= anchor && t.State == TaskState.Open))
                CancelWithoutSave(task, "事件已取消");
        }
        else if (selected.State == TaskState.Open) CancelWithoutSave(selected, "本次事件已取消");
        Save();
    }

    public DateOnly ReminderStart(TaskItem task) => task.IsEvent && !IsLongTerm(task)
        ? task.ScheduledDate.AddDays(-State.Settings.LeadDays(task.EventImportance)) : task.ScheduledDate;

    public IEnumerable<TaskItem> VisibleOpen(DateOnly day) => Sort(State.Tasks.Where(t =>
        t.State == TaskState.Open && (IsLongTerm(t) ? t.ScheduledDate == day && IsLongTermActive(t, day) : ReminderStart(t) <= day)));

    public static IEnumerable<TaskItem> Sort(IEnumerable<TaskItem> tasks) => tasks
        .OrderByDescending(t => t.IsImportant).ThenByDescending(t => t.EventImportance)
        .ThenBy(t => t.ScheduledDate).ThenBy(t => t.CreatedAt);

    public List<TaskItem> ForDate(DateOnly day)
    {
        foreach (var ev in State.Events)
            foreach (var occurrence in Recurrence.Between(ev, day, day)) EnsureOccurrence(ev, occurrence);
        return Sort(State.Tasks.Where(t => t.ScheduledDate == day || t.OriginalScheduledDate == day ||
            t.CompletedOn == day || State.Activities.Any(a => a.TaskId == t.Id && a.Day == day && !a.Reverted))).ToList();
    }

    public int ScheduledCount(DateOnly day)
    {
        var actual = State.Tasks.Count(t => t.ScheduledDate == day && t.State != TaskState.Cancelled);
        foreach (var ev in State.Events)
            foreach (var occurrence in Recurrence.Between(ev, day, day))
                if (!State.Tasks.Any(t => t.EventId == ev.Id && (ev.Repeat == RepeatRule.None || t.OccurrenceDate == occurrence))) actual++;
        return actual;
    }

    public IReadOnlyDictionary<DateOnly, CalendarDaySummary> CalendarForYear(int year)
    {
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);
        var days = new Dictionary<DateOnly, CalendarDayBuilder>();
        var existingEvents = new HashSet<string>();
        var existingOccurrences = new HashSet<(string, DateOnly?)>();
        CalendarDayBuilder Day(DateOnly date)
        {
            if (!days.TryGetValue(date, out var summary)) days[date] = summary = new();
            return summary;
        }
        foreach (var task in State.Tasks)
        {
            // Cancelled and moved occurrences still suppress the original projection.
            if (task.EventId is { } eventId)
            {
                existingEvents.Add(eventId);
                existingOccurrences.Add((eventId, task.OccurrenceDate));
            }
            if (task.ScheduledDate.Year != year || task.State == TaskState.Cancelled) continue;
            var day = Day(task.ScheduledDate);
            day.Count++; day.RecordedCount++; day.Important |= task.IsImportant;
            day.Tasks.Add(new(task.Title, task.IsImportant, task.State, task.IsEvent, task.EventImportance));
            if (task.IsEvent) day.Titles.Add((task.State == TaskState.Completed ? "✓ " + task.Title : task.Title, task.EventImportance));
        }
        foreach (var ev in State.Events)
            foreach (var occurrence in Recurrence.Between(ev, from, to))
            {
                if (ev.Repeat == RepeatRule.None ? existingEvents.Contains(ev.Id) : existingOccurrences.Contains((ev.Id, occurrence))) continue;
                var day = Day(occurrence);
                day.Count++; day.Titles.Add((ev.Title, ev.Importance));
                day.Important |= ev.IsImportant;
                day.Tasks.Add(new(ev.Title, ev.IsImportant, TaskState.Open, true, ev.Importance));
            }
        return days.ToDictionary(p => p.Key, p => new CalendarDaySummary(p.Value.Count, p.Value.RecordedCount,
            p.Value.Important, p.Value.Titles.OrderByDescending(t => t.Importance).Select(t => t.Title).ToArray())
        {
            Tasks = p.Value.Tasks.OrderByDescending(t => t.IsImportant).ThenBy(t => t.State == TaskState.Completed)
                .ThenByDescending(t => t.EventImportance).ToArray()
        });
    }

    private sealed class CalendarDayBuilder
    {
        public int Count;
        public int RecordedCount;
        public bool Important;
        public List<(string Title, EventImportance Importance)> Titles { get; } = [];
        public List<CalendarTaskPreview> Tasks { get; } = [];
    }

    public List<string> EventTitles(DateOnly day)
    {
        var titles = State.Tasks.Where(t => t.IsEvent && t.ScheduledDate == day && t.State != TaskState.Cancelled)
            .Select(t => (Title: t.State == TaskState.Completed ? "✓ " + t.Title : t.Title, t.EventImportance)).ToList();
        foreach (var ev in State.Events)
            foreach (var occurrence in Recurrence.Between(ev, day, day))
                if (!State.Tasks.Any(t => t.EventId == ev.Id && (ev.Repeat == RepeatRule.None || t.OccurrenceDate == occurrence)))
                    titles.Add((ev.Title, ev.Importance));
        return titles.OrderByDescending(t => t.Item2).Select(t => t.Title).ToList();
    }

    public List<ActivityRecord> ActivitiesFor(DateOnly day) => State.Activities
        .Where(a => a.Day == day && !a.Reverted).OrderByDescending(a => a.At).ToList();

    public void UpdateSettings(int normal, int important, int veryImportant)
    {
        if (new[] { normal, important, veryImportant }.Any(d => d is < 0 or > 365))
            throw new ArgumentException("提醒天数必须是 0 至 365 的整数。");
        State.Settings.NormalDays = normal;
        State.Settings.ImportantDays = important;
        State.Settings.VeryImportantDays = veryImportant;
        EnsureUpcoming();
        Save();
    }

    public void RestoreBackup(string path)
    {
        var restored = SqliteStore.ReadBackupData(path);
        _store.BeforeRestoreBackup(State);
        _store.Save(restored.State, restored.Images);
        State = restored.State;
        EnsureUpcoming();
        Save();
    }

    private ActivityRecord Snapshot(TaskItem task, ActivityKind kind) => new()
    {
        TaskId = task.Id, Kind = kind, Day = Today, At = _clock.Now,
        Title = task.Title, Notes = task.Notes, ImageIds = [.. task.ImageIds], ScheduledDate = task.ScheduledDate, IsImportant = task.IsImportant
    };
    public IReadOnlyList<NoteImage> GetNoteImages(IEnumerable<string> ids) => _store.GetNoteImages(ids);
    public void ExportBackup(string path) => _store.ExportBackup(path, State);

    private static List<string> PrepareImages(IReadOnlyList<NoteImage>? images)
    {
        var list = images ?? [];
        foreach (var image in list) image.Validate();
        var ids = list.Select(i => i.Id).ToList(); NoteImage.ValidateIds(ids); return ids;
    }
    private static void ValidateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("请填写标题。");
        if (title.Trim().Length > 500) throw new ArgumentException("标题最多 500 个字符。");
    }
}
