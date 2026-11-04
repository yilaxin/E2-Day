namespace DailyRecord.Core;

public static class TaskColors
{
    public static IReadOnlyList<string> DefaultNames { get; } = Array.AsReadOnly(new[] { "本时段要做", "专注处理", "零碎处理", "待沟通", "稍后安排" });
    public static string Name(UserSettings settings, TaskColor color) => color == TaskColor.None ? "未分类" : settings.TaskColorNames[(int)color - 1];
    public static void ValidateNames(IReadOnlyList<string>? names)
    {
        if (names is null || names.Count != 5 || names.Any(n => string.IsNullOrWhiteSpace(n) || n != n.Trim() || n.Length > 12 || n.Any(char.IsControl)) ||
            names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 5 || names.Any(n => n is "全部" or "未分类"))
            throw new InvalidDataException("请填写 5 个不重复的分类名称，每个 1～12 字，不能使用“全部”或“未分类”。");
    }
}

public sealed partial class TaskService
{
    // The same source set drives the on-screen counts, filter and classified share image.
    public IReadOnlyList<TaskItem> TodayColorTasks() => Sort(VisibleOpen(Today).Concat(State.Tasks.Where(t =>
        t.State == TaskState.Completed && t.CompletedOn == Today))).ToList();
    public IReadOnlyList<TaskItem> FilterToday(TaskColor? color) => TodayColorTasks().Where(t => color is null || t.Color == color).ToList();

    public void RenameTaskColors(IEnumerable<string> names)
    {
        var prepared = names.Select(n => n?.Trim() ?? "").ToList(); TaskColors.ValidateNames(prepared);
        if (State.Settings.TaskColorNames.SequenceEqual(prepared)) return;
        State.Settings.TaskColorNames = prepared; Save();
    }

    public void SetTaskColors(IEnumerable<TaskItem> tasks, TaskColor color, EditScope scope = EditScope.ThisOccurrence)
    {
        if (!Enum.IsDefined(color) || !Enum.IsDefined(scope)) throw new ArgumentException("请选择有效的分类和范围。");
        var selected = tasks.DistinctBy(t => t.Id).ToList();
        if (selected.Any(t => !State.Tasks.Any(existing => ReferenceEquals(existing, t)))) throw new ArgumentException("事项已有变化，请重新选择。");
        var changed = false;
        void Set(TaskItem task, bool overridden)
        {
            if (task.Color == color && task.ColorOverride == overridden) return;
            task.Color = color; task.ColorOverride = overridden; changed = true;
        }
        foreach (var task in selected)
        {
            if (scope == EditScope.ThisAndFuture && Definition(task) is { } ev && task.OccurrenceDate is { } anchor)
            {
                if (ev.Color != color) { ev.Color = color; changed = true; }
                foreach (var future in State.Tasks.Where(t => t.EventId == ev.Id && t.OccurrenceDate >= anchor &&
                             t.State == TaskState.Open && !t.ColorOverride)) Set(future, false);
                Set(task, false);
            }
            else Set(task, task.IsEvent);
        }
        if (changed) Save();
    }
}
