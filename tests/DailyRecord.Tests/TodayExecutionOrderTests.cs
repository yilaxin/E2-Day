using DailyRecord.Core;

internal static class TodayExecutionOrderTests
{
    public static void Run(Action<string, Action> test, Action<bool, string> check, Func<Fixture> fixture, string runRoot)
    {
        static IEnumerable<string> Titles(TaskService service, TaskColor? color = null) => service.TodayExecutionOrder(color).Select(t => t.Title);

        test("首次移动从自动分组进入我的顺序，切换保留手动排列及任务状态", () =>
        {
            using var f = fixture();
            var service = f.Service;
            var first = service.AddTask("普通", "", f.Clock.Today);
            var upcoming = service.AddEvent("提前", "", f.Clock.Today.AddDays(2), EventImportance.Important, RepeatRule.None);
            var viewed = service.AddEvent("查看", "", f.Clock.Today, EventImportance.Normal, RepeatRule.None);
            var seen = service.State.Tasks.Single(t => t.EventId == viewed.Id);
            service.ToggleViewed(seen);
            check(Titles(service).SequenceEqual(["普通", "提前", "查看"]), "自动分组顺序错误");
            check(service.MoveTodayTask(seen.Id, 0, null), "第一次移动没有生效");
            check(service.UsesManualExecutionOrder && Titles(service).SequenceEqual(["查看", "普通", "提前"]), "未切换手动顺序");
            service.SetManualExecutionOrder(false);
            check(Titles(service).SequenceEqual(["普通", "提前", "查看"]), "自动顺序被手动顺序覆盖");
            service.ToggleImportant(first);
            service.SetManualExecutionOrder(true);
            check(Titles(service).SequenceEqual(["查看", "普通", "提前"]) && service.IsViewed(seen, f.Clock.Today) &&
                service.State.Events.Contains(upcoming) && first.IsImportant, "模式切换、星标或提醒影响了手动顺序");
        });

        test("筛选只替换可见位置，撤销恢复原顺序并防止覆盖后续排序", () =>
        {
            using var f = fixture();
            var service = f.Service;
            var a = service.AddTask("青甲", "", f.Clock.Today, color: TaskColor.Teal);
            service.AddTask("粉", "", f.Clock.Today, color: TaskColor.Pink);
            var c = service.AddTask("青乙", "", f.Clock.Today, color: TaskColor.Teal);
            service.AddTask("蓝", "", f.Clock.Today, color: TaskColor.Blue);
            service.SetManualExecutionOrder(true);
            var before = service.State.Settings.ExecutionPlan;
            check(!service.MoveTodayTask(a.Id, 9, TaskColor.Teal), "越界移动被接受");
            check(service.MoveTodayTask(c.Id, 0, TaskColor.Teal), "筛选移动失败");
            var moved = service.State.Settings.ExecutionPlan!;
            check(Titles(service).SequenceEqual(["青乙", "粉", "青甲", "蓝"]), "隐藏项位置发生变化");
            check(service.UndoExecutionMove(moved, before) && Titles(service).SequenceEqual(["青甲", "粉", "青乙", "蓝"]), "撤销未恢复");
            check(!service.UndoExecutionMove(moved, before), "过期撤销覆盖当前位置");
        });

        test("完成保留原位置，新增追加，重启和含记录备份恢复后顺序一致", () =>
        {
            using var f = fixture();
            var service = f.Service;
            var a = service.AddTask("甲", "", f.Clock.Today);
            var b = service.AddTask("乙", "已保存备注", f.Clock.Today);
            var c = service.AddTask("丙", "", f.Clock.Today);
            service.MoveTodayTask(c.Id, 0, null);
            service.ToggleCompleted(b);
            service.AddTask("丁", "", f.Clock.Today, true);
            service.SetTaskColors([a], TaskColor.Blue);
            check(Titles(service).SequenceEqual(["丙", "甲", "丁"]), "完成或追加改变了原顺序");
            var reloaded = new TaskService(f.Store, f.Clock);
            var completed = reloaded.State.Tasks.Single(t => t.Id == b.Id);
            check(completed.Notes == "已保存备注" && completed.CompletedOn == f.Clock.Today, "重启丢失原记录");
            reloaded.ToggleCompleted(completed);
            check(Titles(reloaded).SequenceEqual(["丙", "甲", "乙", "丁"]), "撤销完成未回到原位");
            var backup = Path.Combine(runRoot, "execution-order-sample.json");
            f.Store.ExportPortableBackup(backup, reloaded.State);
            using var restored = fixture();
            restored.Clock.Today = f.Clock.Today;
            restored.Service.RestoreBackup(backup);
            check(Titles(restored.Service).SequenceEqual(Titles(reloaded)), "恢复备份丢失手动顺序");
        });

        test("跨天留下未完成的相对顺序及模式，清理旧打卡并追加新提醒", () =>
        {
            using var f = fixture();
            var service = f.Service;
            var a = service.AddTask("甲", "", f.Clock.Today);
            var b = service.AddTask("乙", "", f.Clock.Today);
            var oldHabit = service.AddLongTermTask("每日", "", f.Clock.Today);
            var ev = service.AddEvent("新增提醒", "", f.Clock.Today.AddDays(4), EventImportance.Important, RepeatRule.None);
            service.MoveTodayTask(b.Id, 0, null);
            var oldPlan = service.State.Settings.ExecutionPlan!;
            f.Clock.Today = f.Clock.Today.AddDays(1);
            service.RefreshDay();
            var ordered = service.TodayExecutionOrder();
            check(ordered.Take(2).Select(t => t.Id).SequenceEqual([b.Id, a.Id]) && ordered.Skip(2).Count() == 2 &&
                ordered.Any(t => t.EventId == ev.Id) && ordered.Any(t => t.EventId == oldHabit.EventId && t.Id != oldHabit.Id), "跨天未保留相对顺序或漏了新增提醒");
            check(service.State.Settings.ExecutionPlan!.Day == f.Clock.Today &&
                !service.State.Settings.ExecutionPlan.TaskIds.Contains(oldHabit.Id) &&
                !service.UndoExecutionMove(oldPlan, null), "旧打卡位置或旧撤销延续到今天");
        });
    }
}
