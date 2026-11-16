using DailyRecord.Core;

internal static class LongTermPeriodTests
{
    internal static void Run(Action<string, Action> test, Action<bool, string> check, Func<Fixture> fixture, string runRoot)
    {
        test("阶段开始当天计入，七天与三十天覆盖闰日和跨年边界", () =>
        {
            using var f = fixture(); f.Clock.Today = new(2028, 2, 27);
            var task = f.Service.AddLongTermTask("七天", "", f.Clock.Today, durationDays: 7); var ev = f.Service.State.Events.Single();
            check(ev.EndDate == new DateOnly(2028, 3, 4) && Recurrence.Between(ev, f.Clock.Today, f.Clock.Today.AddDays(40)).Count() == 7, "七天结束边界错误");
            check(f.Service.CalendarForYear(2028).ContainsKey(new(2028, 2, 29)) && !f.Service.CalendarForYear(2028).ContainsKey(new(2028, 3, 5)), "日历阶段投影错误");
            check(LongTermSchedule.Create(new(2026, 12, 16), 30).End == new DateOnly(2027, 1, 14), "30 天被按一个月计算");
            check(task.Id == f.Service.VisibleOpen(f.Clock.Today).Single().Id, "创建阶段重复生成实例");
        });
        test("最后一天完成仍可续期，按期结束不取消当天也不新增完成", () =>
        {
            using var f = fixture(); var day = f.Clock.Today;
            var task = f.Service.AddLongTermTask("一天", "", day, durationDays: 1); var ev = f.Service.State.Events.Single();
            f.Service.ToggleCompleted(task); var history = StateJson.Serialize(new AppState { Activities = f.Service.State.Activities });
            check(f.Service.LongTermDecisions().Single() == ev && f.Service.IsLongTermActive(task), "勾选完成隐藏了到期决定");
            f.Service.ConfirmLongTermEnd(ev);
            check(!f.Service.LongTermDecisions().Any() && task.State == TaskState.Completed && history == StateJson.Serialize(new AppState { Activities = f.Service.State.Activities }), "按期结束改变了完成或历史");
            f.Service.ToggleCompleted(task); check(f.Service.VisibleOpen(day).Contains(task), "按期结束取消了当天");
            f.Clock.Today = day.AddDays(1); f.Service.RefreshDay(); check(!f.Service.VisibleOpen(f.Clock.Today).Any(), "到期次日继续生成");
        });
        test("当天续期从次日衔接同一长期任务，累计打卡与原色保留", () =>
        {
            using var f = fixture(); var day = f.Clock.Today;
            var task = f.Service.AddLongTermTask("每天学习", "备注", day, true, color: TaskColor.Purple, durationDays: 1); var ev = f.Service.State.Events.Single();
            f.Service.ToggleCompleted(task); f.Service.RenewLongTerm(ev, day.AddDays(1), 7);
            check(f.Service.State.Events.Count == 1 && ev.LongTermPeriods.Count == 2 && ev.EndDate == day.AddDays(7) && task.State == TaskState.Completed, "续期新建任务定义或重置完成");
            check(!f.Service.LongTermDecisions().Any() && new TaskAnalytics(f.Service.State, day).Habits.Single().CompletedDays.Count == 1, "续期丢失累计打卡");
            f.Clock.Today = day.AddDays(1); f.Service.RefreshDay(); var next = f.Service.VisibleOpen(f.Clock.Today).Single();
            check(next.EventId == task.EventId && next.Color == TaskColor.Purple && next.IsImportant && f.Service.PeriodProgress(ev).ElapsedDays == 1, "次阶段属性或天数错误");
        });
        test("错过到期后暂停待决定，续期空档不生成任务且日历也留空", () =>
        {
            using var f = fixture(); var day = f.Clock.Today;
            var task = f.Service.AddLongTermTask("阶段", "", day, durationDays: 7); var ev = f.Service.State.Events.Single();
            f.Clock.Today = day.AddDays(12); f.Service.RefreshDay();
            check(!f.Service.VisibleOpen(f.Clock.Today).Any() && f.Service.LongTermDecisions().Single() == ev && f.Service.State.Tasks.Count == 1, "过期自动续期或堆积漏做");
            f.Service.RenewLongTerm(ev, f.Clock.Today, 7); f.Service.RefreshDay();
            check(f.Service.VisibleOpen(f.Clock.Today).Count() == 1 && ev.LongTermPeriods.Count == 2, "过期续期没有生成今天或重复生成");
            check(!Recurrence.Between(ev, day.AddDays(7), day.AddDays(11)).Any() && !f.Service.CalendarForYear(day.Year).ContainsKey(day.AddDays(9)), "续期填满了中间空档");
            check(f.Service.State.Tasks.Single(t => t.Id == task.Id).State == TaskState.Open, "过去未完成历史被删除或归档");
        });
        test("阶段进度只计有效完成日，撤销与转换后的打卡归属正确", () =>
        {
            using var f = fixture(); var day = f.Clock.Today;
            var task = f.Service.AddLongTermTask("打卡", "", day, durationDays: 7); var ev = f.Service.State.Events.Single();
            f.Service.ToggleCompleted(task); f.Service.ConvertItem(task, ItemKind.Task, task.Title, task.Notes, day, false);
            f.Clock.Today = day.AddDays(4); f.Service.RefreshDay(); var current = f.Service.VisibleOpen(f.Clock.Today).Single(); f.Service.ToggleCompleted(current);
            var progress = f.Service.PeriodProgress(ev);
            check(progress.ElapsedDays == 5 && progress.TotalDays == 7 && progress.CompletedDays == 2, "转换归属或漏做进度错误");
            f.Service.ToggleCompleted(task); check(f.Service.PeriodProgress(ev).CompletedDays == 1, "撤销后阶段进度没更新");
            f.Clock.Today = day.AddDays(20); check(f.Service.PeriodProgress(ev).ElapsedDays == 7, "过期后的阶段天数超过总天数");
        });
        test("旧无限长期任务保持原安排，可从今天设置期限且保留过去记录", () =>
        {
            using var f = fixture(); var day = f.Clock.Today;
            var task = f.Service.AddLongTermTask("旧习惯", "", day.AddDays(-60)); var ev = f.Service.State.Events.Single();
            check(ev.LongTermPeriods.Count == 0 && ev.EndDate is null && f.Service.CalendarForYear(day.Year + 1).Count > 300, "旧习惯被自动改为有限任务");
            f.Service.SetLongTermDuration(ev, 7);
            check(ev.EndDate == day.AddDays(6) && ev.LongTermPeriods[^1].Start == day && ev.LongTermPeriods.Count == 2, "旧任务没有从今天开始新期限");
            check(Recurrence.Between(ev, day.AddDays(-10), day.AddDays(-1)).Count() == 10 && f.Service.State.Tasks.Contains(task), "编辑期限抹掉了原历史安排");
        });
        test("期限缩短归档未来未完成，保留已完成，并拒绝非法操作不写入", () =>
        {
            using var f = fixture(); var day = f.Clock.Today;
            var task = f.Service.AddLongTermTask("计划", "", day, durationDays: 30); var ev = f.Service.State.Events.Single();
            var pending = f.Service.EnsureOccurrence(ev, day.AddDays(10)); var done = f.Service.EnsureOccurrence(ev, day.AddDays(11)); f.Service.ToggleCompleted(done);
            f.Service.SetLongTermDuration(ev, 7); check(pending.State == TaskState.Cancelled && done.State == TaskState.Completed, "缩短覆盖了完成记录或留待办");
            var before = StateJson.Serialize(f.Service.State); var revision = f.Service.Revision;
            foreach (var action in new Action[] { () => f.Service.SetLongTermDuration(ev, 0), () => f.Service.RenewLongTerm(ev, day, 7), () => f.Service.AddLongTermTask("越界", "", new(2100, 12, 31), durationDays: 7) })
            { try { action(); throw new Exception("非法日期保存成功"); } catch (ArgumentException) { } }
            check(before == StateJson.Serialize(f.Service.State) && revision == f.Service.Revision, "失败前已部分写入");
        });
        test("含图备份和文件合并保留阶段、续期空档、偏好与历史", () =>
        {
            using var f = fixture(); var day = f.Clock.Today;
            var image = NoteImage.Create("阶段.png", Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aN1kAAAAASUVORK5CYII="));
            var task = f.Service.AddLongTermTask("含图阶段", "备注", day, true, [image], TaskColor.Blue, 1); var ev = f.Service.State.Events.Single(); f.Service.ToggleCompleted(task);
            f.Clock.Today = day.AddDays(3); f.Service.RenewLongTerm(ev, f.Clock.Today, 7); f.Service.SetCalendarView(CalendarViewMode.HideLongTerm); f.Service.SetCalendarView(CalendarViewMode.Plain);
            var path = Path.Combine(f.Store.DataDirectory, "phases.json"); f.Service.ExportBackup(path);
            f.Store.ExportPortableBackup(Path.Combine(runRoot, "period-sample.json"), f.Service.State);
            using var restored = fixture(); restored.Clock.Today = f.Clock.Today; restored.Service.RestoreBackup(path);
            var saved = restored.Service.State.Events.Single();
            check(saved.Id == ev.Id && saved.LongTermPeriods.SequenceEqual(ev.LongTermPeriods) && restored.Service.State.Settings.PreviousCalendarView == CalendarViewMode.HideLongTerm, "恢复丢失阶段或日历偏好");
            check(restored.Service.GetNoteImages([image.Id]).Single().Data.SequenceEqual(image.Data) && new TaskAnalytics(restored.Service.State, restored.Clock.Today).Completions.Count == 1, "恢复丢失图片或打卡");
            check(FileImport.Preview(restored.Service.State, f.Service.State).Conflicts.Count == 0, "一致备份产生冲突");
        });
        test("日历三视图只筛选投影，保留事件和完成，记住纯日历之前的视图", () =>
        {
            using var f = fixture(); var day = f.Clock.Today;
            f.Service.AddLongTermTask("习惯", "", day, durationDays: 7); var task = f.Service.AddTask("手动", "", day); f.Service.ToggleCompleted(task);
            f.Service.AddEvent("事件", "", day, EventImportance.Normal, RepeatRule.None);
            var before = StateJson.Serialize(f.Service.State); var revision = f.Service.Revision;
            check(f.Service.CalendarForYear(day.Year)[day].ScheduledCount == 3 && f.Service.CalendarForYear(day.Year, CalendarViewMode.HideLongTerm)[day].ScheduledCount == 2 &&
                f.Service.CalendarForYear(day.Year, CalendarViewMode.Plain).Count == 0, "日历筛选范围错误");
            check(before == StateJson.Serialize(f.Service.State) && revision == f.Service.Revision, "查看投影写入了记录");
            f.Service.SetCalendarView(CalendarViewMode.HideLongTerm); f.Service.SetCalendarView(CalendarViewMode.Plain);
            check(f.Store.Load().Settings.PreviousCalendarView == CalendarViewMode.HideLongTerm && f.Service.ForDate(day).Count == 3, "纯日历删除了任务或没有记住之前视图");
        });
        test("阶段非法重叠拒绝导入，续期结构和结束日期作为整体同步", () =>
        {
            using var f = fixture(); var day = f.Clock.Today;
            f.Service.AddLongTermTask("同步阶段", "", day, durationDays: 1); var ev = f.Service.State.Events.Single(); var baseline = SyncMerge.Clone(f.Service.State);
            f.Service.RenewLongTerm(ev, day.AddDays(1), 7);
            var phone = SyncMerge.Clone(baseline); phone.Events.Single().LongTermPeriods[0] = new(day, day.AddDays(2)); phone.Events.Single().EndDate = day.AddDays(2);
            var merged = SyncMerge.Merge(baseline, f.Service.State, phone);
            check(merged.Conflicts.Any(c => c.Field == "PeriodSchedule") && merged.State.Events.Single().LongTermPeriods.Count == 2, "阶段和截止日被独立混合");
            var invalid = SyncMerge.Clone(f.Service.State); invalid.Events.Single().LongTermPeriods[1] = new(day, day.AddDays(7));
            try {StateJson.Validate(invalid); throw new Exception("重叠阶段导入成功");} catch(InvalidDataException) { }
        });
    }
}
