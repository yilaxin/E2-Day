namespace DailyRecord.Core;

public sealed record CalendarDaySummary(int ScheduledCount, int RecordedCount, bool HasImportantTask, IReadOnlyList<string> EventTitles)
{
    public IReadOnlyList<CalendarTaskPreview> Tasks { get; init; } = Array.Empty<CalendarTaskPreview>();
    public static CalendarDaySummary Empty { get; } = new(0, 0, false, Array.Empty<string>());
}

public sealed record CalendarTaskPreview(string Title, bool IsImportant, TaskState State, bool IsEvent, EventImportance EventImportance);
