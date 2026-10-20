using System.Text.Json;
using System.Text.Json.Serialization;

namespace DailyRecord.Core;

public enum EventImportance { Normal, Important, VeryImportant }
public enum RepeatRule { None, Weekly, Monthly, Yearly, LunarYearly, Daily }
public enum TaskState { Open, Completed, Cancelled }
public enum EditScope { ThisOccurrence, ThisAndFuture }
public enum ActivityKind { Viewed, Completed, Cancelled, Ended }

public interface IClock
{
    DateOnly Today { get; }
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
    public DateTimeOffset Now => DateTimeOffset.Now;
}

public sealed class EventDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Notes { get; set; } = "";
    public List<string> ImageIds { get; set; } = [];
    public DateOnly StartDate { get; set; }
    public bool IsImportant { get; set; }
    public DateOnly? EndDate { get; set; }
    public EventImportance Importance { get; set; }
    public RepeatRule Repeat { get; set; }
    public int LunarMonth { get; set; }
    public int LunarDay { get; set; }
}

public sealed class TaskItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? EventId { get; set; }
    public DateOnly? OccurrenceDate { get; set; }
    public DateOnly ScheduledDate { get; set; }
    public DateOnly OriginalScheduledDate { get; set; }
    public string Title { get; set; } = "";
    public string Notes { get; set; } = "";
    public List<string> ImageIds { get; set; } = [];
    public EventImportance EventImportance { get; set; }
    public bool IsImportant { get; set; }
    public bool HasOverride { get; set; }
    public TaskState State { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateOnly? CompletedOn { get; set; }
    public string ArchiveReason { get; set; } = "";
    [JsonIgnore] public bool IsEvent => EventId is not null;
}

public sealed class ActivityRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TaskId { get; set; } = "";
    public ActivityKind Kind { get; set; }
    public DateOnly Day { get; set; }
    public DateTimeOffset At { get; set; }
    public string Title { get; set; } = "";
    public string Notes { get; set; } = "";
    public List<string> ImageIds { get; set; } = [];
    public DateOnly ScheduledDate { get; set; }
    public bool IsImportant { get; set; }
    public bool Reverted { get; set; }
}

public sealed class UserSettings
{
    public int NormalDays { get; set; }
    public int ImportantDays { get; set; } = 3;
    public int VeryImportantDays { get; set; } = 7;
    public int HeatmapLightMax { get; set; } = 1;
    public int HeatmapMediumMax { get; set; } = 3;
    public int HeatmapDarkMax { get; set; } = 6;
    public HeatmapScale CompletionHeatmapScale() => new(HeatmapLightMax, HeatmapMediumMax, HeatmapDarkMax);
    public int LeadDays(EventImportance importance) => importance switch
    {
        EventImportance.Important => ImportantDays,
        EventImportance.VeryImportant => VeryImportantDays,
        _ => NormalDays
    };
    public int MaxLeadDays => Math.Max(NormalDays, Math.Max(ImportantDays, VeryImportantDays));
}

public sealed class NewsArticle
{
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Url { get; set; } = "";
    public string Source { get; set; } = "";
    public string Language { get; set; } = "";
    public DateTimeOffset PublishedAt { get; set; }
}

public sealed class NewsCache
{
    public DateTimeOffset? FetchedAt { get; set; }
    public List<NewsArticle> Articles { get; set; } = [];
}

public sealed class AppState
{
    public int SchemaVersion { get; set; } = 2;
    public List<EventDefinition> Events { get; set; } = [];
    public List<TaskItem> Tasks { get; set; } = [];
    public List<ActivityRecord> Activities { get; set; } = [];
    public UserSettings Settings { get; set; } = new();
    public NewsCache News { get; set; } = new();
}

public static class StateJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public static string Serialize(AppState state) => JsonSerializer.Serialize(state, Options);
    public static AppState Deserialize(string json)
    {
        var state = JsonSerializer.Deserialize<AppState>(json, Options)
            ?? throw new InvalidDataException("文件没有有效的数据。");
        Validate(state);
        state.SchemaVersion = 2;
        return state;
    }
    public static void Validate(AppState state)
    {
        if (state.SchemaVersion is not (1 or 2)) throw new InvalidDataException("此数据版本不受支持。");
        if (state.Events is null || state.Tasks is null || state.Activities is null ||
            state.Settings is null || state.News is null || state.News.Articles is null)
            throw new InvalidDataException("数据缺少必要字段。");
        if (state.Events.Any(e => e is null) || state.Tasks.Any(t => t is null) ||
            state.Activities.Any(a => a is null) || state.News.Articles.Any(a => a is null))
            throw new InvalidDataException("数据含有空记录。");
        static bool ValidDate(DateOnly date) => date >= new DateOnly(1901, 1, 1) && date <= new DateOnly(2100, 12, 31);
        if (state.Tasks.Select(t => t.Id).Distinct().Count() != state.Tasks.Count ||
            state.Events.Select(e => e.Id).Distinct().Count() != state.Events.Count ||
            state.Activities.Select(a => a.Id).Distinct().Count() != state.Activities.Count)
            throw new InvalidDataException("数据含有重复记录。");
        var events = state.Events.Select(e => e.Id).ToHashSet();
        var tasks = state.Tasks.Select(t => t.Id).ToHashSet();
        foreach (var task in state.Tasks)
        {
            NoteImage.ValidateIds(task.ImageIds);
            if (string.IsNullOrWhiteSpace(task.Id) || string.IsNullOrWhiteSpace(task.Title) || task.Notes is null ||
                !ValidDate(task.ScheduledDate) || !ValidDate(task.OriginalScheduledDate) ||
                task.OccurrenceDate is { } occurrence && !ValidDate(occurrence) ||
                task.CompletedOn is { } completed && !ValidDate(completed) ||
                !Enum.IsDefined(task.State) || !Enum.IsDefined(task.EventImportance) ||
                task.EventId is not null && (!events.Contains(task.EventId) || task.OccurrenceDate is null) ||
                task.State == TaskState.Completed && task.CompletedOn is null)
                throw new InvalidDataException("任务数据不完整。");
        }
        foreach (var ev in state.Events)
        {
            NoteImage.ValidateIds(ev.ImageIds);
            if (string.IsNullOrWhiteSpace(ev.Id) || string.IsNullOrWhiteSpace(ev.Title) || ev.Notes is null || !ValidDate(ev.StartDate) ||
                !Enum.IsDefined(ev.Repeat) || !Enum.IsDefined(ev.Importance) ||
                ev.Repeat == RepeatRule.LunarYearly && (LunarDates.FromSolar(ev.StartDate) is null || ev.LunarMonth is < 1 or > 12 || ev.LunarDay is < 1 or > 30))
                throw new InvalidDataException("事件数据不完整。");
        }
        if (state.Tasks.Where(t => t.IsEvent).GroupBy(t => (t.EventId, t.OccurrenceDate)).Any(g => g.Count() > 1))
            throw new InvalidDataException("同一事件出现了重复任务。");
        if (state.Activities.Any(a => !tasks.Contains(a.TaskId) || !Enum.IsDefined(a.Kind) ||
            string.IsNullOrWhiteSpace(a.Title) || a.Notes is null || !ValidDate(a.Day) || !ValidDate(a.ScheduledDate)))
            throw new InvalidDataException("历史记录缺少对应任务。");
        foreach (var activity in state.Activities) NoteImage.ValidateIds(activity.ImageIds);
        if (state.News.Articles.Any(a => string.IsNullOrWhiteSpace(a.Title) || a.Summary is null ||
            a.Url is null || a.Source is null || a.Language is null))
            throw new InvalidDataException("资讯缓存数据无效。");
        if (new[] { state.Settings.NormalDays, state.Settings.ImportantDays, state.Settings.VeryImportantDays }.Any(d => d is < 0 or > 365))
            throw new InvalidDataException("提醒天数必须在 0 到 365 之间。");
        if (!state.Settings.CompletionHeatmapScale().IsValid)
            throw new InvalidDataException(HeatmapScale.ValidationMessage);
    }
}
