namespace DailyRecord.Core;

public static class LongTermSchedule
{
    public static bool Contains(EventDefinition ev, DateOnly day) => ev.StartDate <= day && (ev.EndDate is null || day <= ev.EndDate) &&
        (ev.LongTermPeriods.Count == 0 || ev.LongTermPeriods.Any(p => p.Start <= day && (p.End is null || day <= p.End)));
    public static LongTermPeriod Create(DateOnly start, int? days)
    {
        if (start < new DateOnly(1901, 1, 1) || start > new DateOnly(2100, 12, 31) || days is < 1 or > 3650)
            throw new ArgumentException("请选择有效起始日期和 1～3650 天的持续时间。");
        if (days is { } count && start.DayNumber + count - 1 > new DateOnly(2100, 12, 31).DayNumber)
            throw new ArgumentException("结束日期不能超过 2100 年 12 月 31 日。");
        return new(start, days is { } length ? start.AddDays(length - 1) : null);
    }
}

public sealed record LongTermProgress(LongTermPeriod Period, int ElapsedDays, int? TotalDays, int CompletedDays);

public sealed partial class TaskService
{
    public IReadOnlyList<EventDefinition> LongTermDecisions() => State.Events.Where(e => e.Repeat == RepeatRule.Daily &&
        e.LongTermPeriods.LastOrDefault() is { End: { } end, EndConfirmed: false } && end <= Today && e.EndDate == end)
        .OrderBy(e => e.EndDate).ThenBy(e => e.Title).ToList();

    public LongTermProgress PeriodProgress(EventDefinition ev, DateOnly? occurrence = null)
    {
        var day = occurrence ?? Today;
        var period = ev.LongTermPeriods.LastOrDefault(p => p.Start <= day && (p.End is null || p.End >= day)) ??
            ev.LongTermPeriods.LastOrDefault() ?? new(ev.StartDate, ev.EndDate, ev.EndDate < Today);
        var until = period.End is { } end && end < Today ? end : Today;
        var completed = State.Tasks.Where(t => t.State == TaskState.Completed && t.CompletedOn <= Today &&
            (t.CompletedOrigin is { Repeat: RepeatRule.Daily } origin ? origin.EventId == ev.Id : t.CompletedOrigin is null && t.EventId == ev.Id))
            .Select(t => t.CompletedOrigin?.Day ?? t.OccurrenceDate ?? t.ScheduledDate)
            .Where(d => d >= period.Start && d <= until).Distinct().Count();
        var total = period.End is { } final ? Math.Max(0, final.DayNumber - period.Start.DayNumber + 1) : (int?)null;
        var elapsedUntil = ev.EndDate is { } stopped && stopped < until ? stopped : until;
        var elapsed = Math.Max(0, elapsedUntil.DayNumber - period.Start.DayNumber + 1);
        return new(period, elapsed, total, completed);
    }

    public void ConfirmLongTermEnd(EventDefinition ev)
    {
        if (!LongTermDecisions().Contains(ev)) throw new ArgumentException("此阶段尚未到期或已经处理。");
        ev.LongTermPeriods[^1] = ev.LongTermPeriods[^1] with { EndConfirmed = true }; Save();
    }

    public DateOnly DurationEditStart(EventDefinition ev) => ev.LongTermPeriods.LastOrDefault()?.Start ?? (ev.StartDate > Today ? ev.StartDate : Today);
    public void ValidateLongTermDuration(EventDefinition ev, int? days)
    {
        var period = LongTermSchedule.Create(DurationEditStart(ev), days);
        if (ev.EndDate < Today || period.End < Today) throw new ArgumentException("已到期的任务请使用续期；新结束日期不能早于今天。");
    }
    public void SetLongTermDuration(EventDefinition ev, int? days)
    {
        if (!State.Events.Contains(ev) || ev.Repeat != RepeatRule.Daily) throw new ArgumentException("长期任务已变化。");
        ValidateLongTermDuration(ev, days);
        var period = LongTermSchedule.Create(DurationEditStart(ev), days);
        if (ev.LongTermPeriods.Count == 0)
        {
            if (ev.StartDate < period.Start) ev.LongTermPeriods.Add(new(ev.StartDate, period.Start.AddDays(-1), true));
            ev.LongTermPeriods.Add(period);
        }
        else ev.LongTermPeriods[^1] = period;
        ev.EndDate = period.End;
        foreach (var task in State.Tasks.Where(t => t.EventId == ev.Id && t.State == TaskState.Open && t.OccurrenceDate > period.End))
            CancelWithoutSave(task, "阶段期限已调整");
        EnsureUpcoming(); Save();
    }

    public void RenewLongTerm(EventDefinition ev, DateOnly start, int? days)
    {
        if (!State.Events.Contains(ev) || ev.Repeat != RepeatRule.Daily || ev.EndDate is null || ev.EndDate > Today)
            throw new ArgumentException("此长期任务尚未到期，无需续期。");
        var earliest = ev.EndDate == Today ? Today.AddDays(1) : Today;
        if (start < earliest || start < ev.StartDate) throw new ArgumentException("续期从到期次日或今天起开始，不补出过去的任务。");
        var next = LongTermSchedule.Create(start, days);
        if (ev.LongTermPeriods.Count >= 2000) throw new ArgumentException("阶段数量已达上限。");
        if (ev.LongTermPeriods.Count == 0) ev.LongTermPeriods.Add(new(ev.StartDate, ev.EndDate, true));
        else
        {
            var old = ev.LongTermPeriods[^1];
            ev.LongTermPeriods[^1] = old with { End = ev.EndDate, EndConfirmed = true };
        }
        ev.LongTermPeriods.Add(next); ev.EndDate = next.End;
        foreach (var task in State.Tasks.Where(t => t.EventId == ev.Id && t.State == TaskState.Cancelled &&
                     t.ScheduledDate >= start && (next.End is null || t.ScheduledDate <= next.End) &&
                     t.ArchiveReason is "长期任务已结束" or "阶段期限已调整"))
        {
            task.State = TaskState.Open; task.ArchiveReason = "";
            foreach (var activity in State.Activities.Where(a => a.TaskId == task.Id && a.Kind == ActivityKind.Cancelled && !a.Reverted)) activity.Reverted = true;
        }
        EnsureOccurrence(ev, start); EnsureUpcoming(); Save();
    }

    public void SetCalendarView(CalendarViewMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentException("日历视图无效。");
        if (State.Settings.CalendarView == mode) return;
        State.Settings.CalendarView = mode;
        if (mode != CalendarViewMode.Plain) State.Settings.PreviousCalendarView = mode;
        Save();
    }
}
