namespace DailyRecord.Core;

public sealed record CompletionEntry(TaskItem Task, DateOnly Day, string Title, bool WasImportant);
public sealed record CompletionDay(DateOnly Day, int Count, int ImportantCount);
public sealed record BacklogGroup(string Key, string Label, IReadOnlyList<TaskItem> Tasks);

public sealed class HabitAnalytics
{
    public EventDefinition Definition { get; }
    public IReadOnlyDictionary<DateOnly, IReadOnlyList<TaskItem>> CompletedDays { get; }
    public int CurrentStreak { get; }
    public int LongestStreak { get; }
    public bool Ended { get; }

    internal HabitAnalytics(EventDefinition definition, IEnumerable<CompletionEntry> completions, DateOnly today)
    {
        Definition = definition;
        CompletedDays = completions.GroupBy(c => c.Task.OccurrenceDate ?? c.Task.ScheduledDate)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<TaskItem>)g.Select(c => c.Task).ToArray());
        // Early completions remain visible in the calendar, but do not lengthen a streak into the future.
        var dates = CompletedDays.Keys.Where(d => d <= today).Order().ToArray();
        var run = 0; DateOnly? previous = null;
        foreach (var day in dates)
        {
            run = previous is { } prior && day.DayNumber == prior.DayNumber + 1 ? run + 1 : 1;
            LongestStreak = Math.Max(LongestStreak, run); previous = day;
        }
        Ended = definition.EndDate < today;
        if (!Ended && definition.StartDate <= today)
        {
            var anchor = CompletedDays.ContainsKey(today) ? today : today.AddDays(-1);
            while (CompletedDays.ContainsKey(anchor))
            {
                CurrentStreak++;
                if (anchor == DateOnly.MinValue) break;
                anchor = anchor.AddDays(-1);
            }
        }
    }

    public int CompletedInMonth(DateOnly month, DateOnly today) => CompletedDays.Keys.Count(d =>
        d.Year == month.Year && d.Month == month.Month && d <= today);
}

/// <summary>Read-only projections of saved records. Opening charts never generates occurrences or writes state.</summary>
public sealed class TaskAnalytics
{
    public DateOnly Today { get; }
    public IReadOnlyList<CompletionEntry> Completions { get; }
    public IReadOnlyList<BacklogGroup> Backlog { get; }
    public IReadOnlyList<HabitAnalytics> Habits { get; }
    public int OverdueCount => Backlog.Where(g => g.Key != "today").Sum(g => g.Tasks.Count);

    public TaskAnalytics(AppState state, DateOnly today)
    {
        Today = today;
        var snapshots = state.Activities.Where(a => a.Kind == ActivityKind.Completed && !a.Reverted)
            .ToLookup(a => a.TaskId);
        Completions = state.Tasks.Where(t => t.State == TaskState.Completed && t.CompletedOn is { } day && day <= today)
            .Select(task =>
            {
                var day = task.CompletedOn!.Value;
                var snapshot = snapshots[task.Id].Where(a => a.Day == day).MaxBy(a => a.At);
                return new CompletionEntry(task, day, snapshot?.Title ?? task.Title, snapshot?.IsImportant ?? task.IsImportant);
            }).OrderBy(c => c.Day).ThenBy(c => c.Task.Id).ToArray();
        var definitions = state.Events.ToDictionary(e => e.Id);
        var due = TaskService.Sort(state.Tasks.Where(t =>
        {
            if (t.State != TaskState.Open || t.ScheduledDate > today) return false;
            return t.EventId is null || !definitions.TryGetValue(t.EventId, out var ev) || ev.Repeat != RepeatRule.Daily ||
                t.ScheduledDate == today && ev.StartDate <= today && (ev.EndDate is null || ev.EndDate >= today);
        })).ToArray();
        Backlog = [Group("today", "今日到期", age => age == 0), Group("1-3", "逾期 1–3 天", age => age is >= 1 and <= 3),
            Group("4-7", "逾期 4–7 天", age => age is >= 4 and <= 7), Group("8+", "逾期超过 7 天", age => age >= 8)];
        BacklogGroup Group(string key, string label, Func<int, bool> matches) =>
            new(key, label, due.Where(t => matches(today.DayNumber - t.ScheduledDate.DayNumber)).ToArray());
        var dailyCompletions = Completions.Where(c => c.Task.EventId is not null).ToLookup(c => c.Task.EventId!);
        Habits = state.Events.Where(e => e.Repeat == RepeatRule.Daily)
            .Select(e => new HabitAnalytics(e, dailyCompletions[e.Id], today))
            .OrderBy(h => h.Ended).ThenBy(h => h.Definition.StartDate > today).ThenBy(h => h.Definition.Title).ToArray();
    }

    public IReadOnlyList<CompletionEntry> Between(DateOnly from, DateOnly to) =>
        Completions.Where(c => c.Day >= from && c.Day <= to).ToArray();

    public IReadOnlyList<CompletionDay> Daily(DateOnly from, DateOnly to)
    {
        var counts = Between(from, to).ToLookup(c => c.Day);
        var result = new List<CompletionDay>();
        for (var number = from.DayNumber; number <= to.DayNumber; number++)
        {
            var day = DateOnly.FromDayNumber(number); var entries = counts[day].ToArray();
            result.Add(new(day, entries.Length, entries.Count(c => c.WasImportant)));
        }
        return result;
    }
}
