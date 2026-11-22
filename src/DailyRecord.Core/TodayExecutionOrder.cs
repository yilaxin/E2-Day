namespace DailyRecord.Core;

public sealed record TodayExecutionPlan(DateOnly Day, bool Manual, IReadOnlyList<string> TaskIds);

public sealed partial class TaskService
{
    public bool UsesManualExecutionOrder => State.Settings.ExecutionPlan?.Manual == true;

    public IReadOnlyList<TaskItem> AutomaticExecutionOrder()
    {
        var open = VisibleOpen(Today).ToArray();
        return open.OrderBy(t => IsViewed(t, Today) ? 2 : t.ScheduledDate > Today ? 1 : 0).ToArray();
    }

    private List<string> ReconciledExecutionIds()
    {
        var plan = State.Settings.ExecutionPlan;
        var open = AutomaticExecutionOrder();
        var retained = open.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        retained.UnionWith(State.Tasks.Where(t => t.State == TaskState.Completed && t.CompletedOn == Today).Select(t => t.Id));
        var ids = plan?.TaskIds.Where(retained.Contains).ToList() ?? [];
        var known = ids.ToHashSet(StringComparer.Ordinal);
        ids.AddRange(open.Select(t => t.Id).Where(known.Add));
        return ids;
    }

    private void ReconcileExecutionPlan()
    {
        if (State.Settings.ExecutionPlan is not { } plan) return;
        var ids = ReconciledExecutionIds();
        if (plan.Day != Today || !plan.TaskIds.SequenceEqual(ids))
            State.Settings.ExecutionPlan = new(Today, plan.Manual, ids.AsReadOnly());
    }

    public IReadOnlyList<TaskItem> TodayExecutionOrder(TaskColor? color = null)
    {
        var open = AutomaticExecutionOrder();
        if (!UsesManualExecutionOrder) return open.Where(t => color is null || t.Color == color).ToArray();

        var byId = open.ToDictionary(t => t.Id);
        return ReconciledExecutionIds().Where(byId.ContainsKey).Select(id => byId[id])
            .Where(t => color is null || t.Color == color).ToArray();
    }

    public void SetManualExecutionOrder(bool manual)
    {
        if (UsesManualExecutionOrder == manual) return;
        State.Settings.ExecutionPlan = new(Today, manual, ReconciledExecutionIds().AsReadOnly());
        Save();
    }

    // A filtered move replaces only the slots occupied by the displayed tasks.
    public bool MoveTodayTask(string taskId, int index, TaskColor? color)
    {
        var order = TodayExecutionOrder();
        var visible = order.Where(t => color is null || t.Color == color).Select(t => t.Id).ToList();
        var from = visible.IndexOf(taskId);
        if (from < 0 || index < 0 || index >= visible.Count || index == from) return false;
        var visibleSlots = visible.ToHashSet(StringComparer.Ordinal);
        visible.RemoveAt(from);
        visible.Insert(index, taskId);

        var next = 0;
        var openOrder = order.Select(t => visibleSlots.Contains(t.Id) ? visible[next++] : t.Id).ToArray();
        var openIds = openOrder.ToHashSet(StringComparer.Ordinal);
        next = 0;
        var ids = ReconciledExecutionIds().Select(id => openIds.Contains(id) ? openOrder[next++] : id).ToList();
        State.Settings.ExecutionPlan = new(Today, true, ids.AsReadOnly());
        Save();
        return true;
    }

    public bool UndoExecutionMove(TodayExecutionPlan expected, TodayExecutionPlan? previous)
    {
        if (expected.Day != Today || !ReferenceEquals(State.Settings.ExecutionPlan, expected)) return false;
        State.Settings.ExecutionPlan = previous;
        Save();
        return true;
    }
}
