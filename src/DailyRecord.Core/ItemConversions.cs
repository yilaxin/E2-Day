namespace DailyRecord.Core;

public sealed partial class TaskService
{
    public ItemKind KindOf(TaskItem task) => IsLongTerm(task) ? ItemKind.LongTerm : task.IsEvent ? ItemKind.Event : ItemKind.Task;

    private IEnumerable<DateOnly> Occurrences(EventDefinition ev, DateOnly from, DateOnly to, ILookup<string, DateOnly>? excluded = null)
    {
        var removed = (excluded is null ? State.Tasks.SelectMany(t => t.ConversionSources).Where(s => s.EventId == ev.Id).Select(s => s.Day) : excluded[ev.Id]).ToHashSet();
        return Recurrence.Between(ev, from, to).Where(day => !removed.Contains(day));
    }

    private CompletionOrigin CaptureCompletionOrigin(TaskItem task)
    {
        var ev = Definition(task);
        return new(ev?.Id, task.OccurrenceDate, ev?.Repeat ?? RepeatRule.None);
    }

    public int ConversionAffectedCount(TaskItem selected, EditScope scope) => scope == EditScope.ThisAndFuture && selected.EventId is { } id
        ? State.Tasks.Count(t => t.Id != selected.Id && t.EventId == id && t.State == TaskState.Open && t.OccurrenceDate >= selected.OccurrenceDate) : 0;

    public void ConvertItem(TaskItem selected, ItemKind target, string title, string notes, DateOnly date, bool important,
        EventImportance importance = EventImportance.Normal, RepeatRule repeat = RepeatRule.None,
        EditScope scope = EditScope.ThisOccurrence, IReadOnlyList<NoteImage>? images = null, TaskColor? color = null)
    {
        if (!State.Tasks.Contains(selected)) throw new ArgumentException("事项已变化，请重新打开编辑。");
        ArgumentNullException.ThrowIfNull(notes);
        if (color is { } chosen && !Enum.IsDefined(chosen)) throw new ArgumentException("分类颜色无效。");
        if (!Enum.IsDefined(target) || !Enum.IsDefined(importance) || !Enum.IsDefined(repeat) || !Enum.IsDefined(scope))
            throw new ArgumentException("请选择有效的事项类型和规则。");
        if (date < new DateOnly(1901, 1, 1) || date > new DateOnly(2100, 12, 31)) throw new ArgumentException("请选择 1901 至 2100 年的日期。");
        if (KindOf(selected) == target) throw new ArgumentException("事项类型未改变，请使用普通编辑。");
        ValidateTitle(title);
        var replacement = target == ItemKind.Task ? null : CreateDefinition(title, notes, date,
            target == ItemKind.LongTerm ? EventImportance.Normal : importance, target == ItemKind.LongTerm ? RepeatRule.Daily : repeat);
        if (target == ItemKind.Event && repeat == RepeatRule.Daily) throw new ArgumentException("每天重复请选长期任务。");
        var ids = images is null ? [.. selected.ImageIds] : PrepareImages(images);
        var original = Definition(selected); var anchor = selected.OccurrenceDate;
        if (selected.State == TaskState.Completed) selected.CompletedOrigin ??= CaptureCompletionOrigin(selected);
        if (original is not null && anchor is { } sourceDay)
        {
            selected.ConversionSources.Add(new(original.Id, sourceDay));
            if (original.Repeat == RepeatRule.None || scope == EditScope.ThisAndFuture)
            {
                var end = sourceDay.AddDays(-1);
                if (original.EndDate is null || end < original.EndDate) original.EndDate = end;
                foreach (var task in State.Tasks.Where(t => t.Id != selected.Id && t.EventId == original.Id &&
                             t.OccurrenceDate >= sourceDay && t.State == TaskState.Open))
                    CancelWithoutSave(task, original.Repeat == RepeatRule.Daily ? "长期任务已结束" : "后续安排因事项类型转换而停止");
            }
        }
        selected.Title = title.Trim(); selected.Notes = notes.Trim(); selected.ImageIds = [.. ids]; selected.IsImportant = important;
        selected.Color = color ?? selected.Color;
        selected.EventId = replacement?.Id; selected.OccurrenceDate = replacement?.StartDate;
        selected.ScheduledDate = replacement?.StartDate ?? date; selected.EventImportance = replacement?.Importance ?? EventImportance.Normal;
        selected.HasOverride = false;
        if (replacement is not null)
        {
            replacement.ImageIds = [.. ids]; replacement.IsImportant = important; replacement.Color = selected.Color;
            if (selected.State == TaskState.Cancelled) replacement.EndDate = replacement.StartDate.AddDays(-1);
            State.Events.Add(replacement);
        }
        selected.ColorOverride = false;
        EnsureUpcoming(); Save(images);
    }
}
