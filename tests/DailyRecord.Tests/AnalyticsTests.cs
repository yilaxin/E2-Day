using DailyRecord.Core;

internal static class AnalyticsTests
{
    internal static void Run(Action<string, Action> test, Action<bool, string> check, Func<Fixture> fixture)
    {
        var today = new DateOnly(2026, 10, 7);
        TaskItem Item(DateOnly scheduled, DateOnly? completed = null) => new()
        {
            Title = "统计示例", ScheduledDate = scheduled, OriginalScheduledDate = scheduled,
            State = completed is null ? TaskState.Open : TaskState.Completed, CompletedOn = completed
        };
        test("完成统计按实际日期，排除查看、撤销、取消和未来完成日期", () =>
        {
            var done = Item(today.AddDays(-8), today); var undone = Item(today); var cancelled = Item(today); cancelled.State = TaskState.Cancelled;
            var state = new AppState { Tasks = [done, undone, cancelled, Item(today, today.AddDays(1))] };
            state.Activities.AddRange([
                new() { TaskId = done.Id, Kind = ActivityKind.Completed, Day = today },
                new() { TaskId = done.Id, Kind = ActivityKind.Completed, Day = today },
                new() { TaskId = undone.Id, Kind = ActivityKind.Completed, Day = today, Reverted = true },
                new() { TaskId = undone.Id, Kind = ActivityKind.Viewed, Day = today },
                new() { TaskId = cancelled.Id, Kind = ActivityKind.Cancelled, Day = today },
                new() { TaskId = done.Id, Kind = ActivityKind.Ended, Day = today }]);
            var report = new TaskAnalytics(state, today);
            check(report.Completions.Count == 1 && report.Daily(today, today).Single().Count == 1, "同一任务被操作历史重复计数");
            check(report.Daily(today.AddDays(-8), today.AddDays(-8)).Single().Count == 0, "按计划日统计了完成");
        });
        test("重要完成采用当时快照，旧数据无快照仍计入", () =>
        {
            using var f = fixture(); var task = f.Service.AddTask("完成时重要", "", f.Clock.Today, true);
            f.Service.ToggleCompleted(task); f.Service.ToggleImportant(task);
            f.Service.EditTask(task, "后来修改标题", "", task.ScheduledDate, false);
            var old = Item(today, today); old.IsImportant = true; f.Service.State.Tasks.Add(old);
            var entries = new TaskAnalytics(f.Service.State, today).Between(today, today);
            check(entries.Count == 2 && entries.All(c => c.WasImportant), "当前星标覆盖了完成快照或旧数据被漏掉");
            check(entries.Single(c => c.Task.Id == task.Id).Title == "完成时重要", "标题没有使用完成时的快照");
        });
        test("积压按到期年龄分组，未来提醒与历史长期实例不累积", () =>
        {
            var ev = new EventDefinition { Title = "每天", StartDate = today.AddDays(-20), Repeat = RepeatRule.Daily };
            var historical = Item(today.AddDays(-10)); historical.EventId = ev.Id; historical.OccurrenceDate = historical.ScheduledDate;
            var daily = Item(today); daily.EventId = ev.Id; daily.OccurrenceDate = today;
            var state = new AppState { Events = [ev], Tasks = [Item(today), Item(today.AddDays(-1)), Item(today.AddDays(-3)),
                Item(today.AddDays(-4)), Item(today.AddDays(-7)), Item(today.AddDays(-8)), Item(today.AddDays(1)), historical, daily] };
            state.Activities.Add(new() { TaskId = state.Tasks[1].Id, Kind = ActivityKind.Viewed, Day = today });
            var report = new TaskAnalytics(state, today);
            check(report.Backlog.Select(g => g.Tasks.Count).SequenceEqual(new[] { 2, 2, 2, 1 }), "积压边界或看过提醒的未完成状态错误");
            check(report.OverdueCount == 5, "今日任务或历史长期任务被算作逾期");
            ev.EndDate = today.AddDays(-1);
            check(new TaskAnalytics(state, today).Backlog[0].Tasks.Count == 1, "已结束长期任务仍计入当前到期");
        });
        test("长期打卡按原安排日去重，补做不改变实际完成趋势", () =>
        {
            var ev = new EventDefinition { Title = "阅读", StartDate = today.AddDays(-10), Repeat = RepeatRule.Daily };
            var state = new AppState { Events = [ev] };
            foreach (var offset in new[] { -7, -4, -3, -2, -1 })
            { var task = Item(today.AddDays(offset), today); task.EventId = ev.Id; task.OccurrenceDate = task.ScheduledDate; state.Tasks.Add(task); }
            // A rescheduled instance still belongs to its original daily occurrence.
            state.Tasks[0].ScheduledDate = today;
            var report = new TaskAnalytics(state, today); var habit = report.Habits.Single();
            check(report.Daily(today, today).Single().Count == 5, "补做没有按实际完成日计数");
            check(habit.CompletedDays.Count == 5 && habit.CurrentStreak == 4 && habit.LongestStreak == 4, "补做按点击当天打卡或连续天数错误");
            check(habit.CompletedInMonth(new(2026, 9, 1), today) == 1 && habit.CompletedInMonth(today, today) == 4, "跨月补做被计入了完成月份的打卡");
            state.Tasks[2].State = TaskState.Open; state.Tasks[2].CompletedOn = null;
            habit = new TaskAnalytics(state, today).Habits.Single();
            check(habit.CurrentStreak == 2 && habit.LongestStreak == 2, "撤销没有打断连续记录");
        });
        test("长期任务结束保留当天完成，重新开始与提前完成不虚增长连续天数", () =>
        {
            using var f = fixture(); var first = f.Service.AddLongTermTask("每天完成", "", today, false);
            f.Service.ToggleCompleted(first); f.Service.EndLongTerm(first);
            var habit = new TaskAnalytics(f.Service.State, today).Habits.Single();
            check(habit.Ended && habit.CurrentStreak == 0 && habit.CompletedInMonth(today, today) == 1 && habit.LongestStreak == 1, "结束删除了完成记录或仍显示当前连续");
            f.Service.ResumeLongTerm(first);
            var tomorrow = f.Service.EnsureOccurrence(f.Service.State.Events.Single(), today.AddDays(1)); f.Service.ToggleCompleted(tomorrow);
            habit = new TaskAnalytics(f.Service.State, today).Habits.Single();
            check(!habit.Ended && habit.CurrentStreak == 1 && habit.LongestStreak == 1 && habit.CompletedInMonth(today, today) == 1,
                "提前完成未来任务增长了截至今天的连续或月度天数");
            check(habit.CompletedDays.ContainsKey(today.AddDays(1)), "已提前完成的打卡信息被丢掉");
        });
        test("每日统计包含零值、闰日及跨年边界", () =>
        {
            var state = new AppState { Tasks = [Item(new(2028, 2, 28), new(2028, 2, 29)), Item(new(2027, 12, 31), new(2028, 1, 1))] };
            var report = new TaskAnalytics(state, new(2028, 12, 31)); var year = report.Daily(new(2028, 1, 1), new(2028, 12, 31));
            check(year.Count == 366 && year.Sum(d => d.Count) == 2 && year.Single(d => d.Day == new DateOnly(2028, 2, 29)).Count == 1, "闰年日期或跨年统计遗漏");
            check(report.Daily(new(2027, 12, 31), new(2028, 1, 1)).Select(d => d.Count).SequenceEqual(new[] { 0, 1 }), "跨年按原计划日期计数");
            check(report.Between(new(2028, 1, 2), new(2028, 1, 1)).Count == 0, "空范围不应包含记录");
        });
        test("查看统计不生成长期实例、不改记录及服务修订号", () =>
        {
            using var f = fixture(); f.Service.AddLongTermTask("很早开始", "", new(1901, 1, 1));
            var before = StateJson.Serialize(f.Service.State); var revision = f.Service.Revision;
            var report = new TaskAnalytics(f.Service.State, today); _ = report.Daily(new(2028, 1, 1), new(2028, 12, 31));
            check(StateJson.Serialize(f.Service.State) == before && f.Service.Revision == revision, "查看图表写入了数据");
            check(new TaskAnalytics(new AppState(), today).Habits.Count == 0, "空账号出现虚构打卡");
        });
        test("完成撤销与备份恢复后统计重新读取当前状态", () =>
        {
            using var f = fixture(); var task = f.Service.AddTask("可恢复的完成", "", today); f.Service.ToggleCompleted(task);
            var backup = Path.Combine(f.Store.DataDirectory, "analytics-backup.json"); f.Service.ExportBackup(backup);
            f.Service.ToggleCompleted(task);
            check(new TaskAnalytics(f.Service.State, today).Completions.Count == 0, "撤销后还计入完成");
            f.Service.RestoreBackup(backup);
            check(new TaskAnalytics(f.Service.State, today).Completions.Count == 1, "恢复后仍使用旧统计");
        });
    }
}
