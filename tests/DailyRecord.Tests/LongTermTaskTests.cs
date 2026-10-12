using DailyRecord.Core;

internal static class LongTermTaskTests
{
    internal static void Run(Action<string, Action> test, Action<bool, string> check, Func<Fixture> fixture)
    {
        test("长期任务每天出现，今日完成不会结束，跨天不累积逾期", () =>
        {
            using var f = fixture(); var day = f.Clock.Today;
            var first = f.Service.AddLongTermTask("每天阅读", "记录阅读进度", day, true);
            check(f.Service.VisibleOpen(day).Single().Id == first.Id, "当天任务缺失");
            f.Service.ToggleCompleted(first); check(f.Service.IsLongTermActive(first), "今日完成结束了整个任务");
            f.Clock.Today = day.AddDays(1); f.Service.RefreshDay();
            var second = f.Service.VisibleOpen(f.Clock.Today).Single();
            check(second.Id != first.Id && second.IsImportant && second.ScheduledDate == f.Clock.Today, "新一天未生成独立任务或丢失星标");
            f.Service.RefreshDay(); check(f.Service.State.Tasks.Count == 2, "同日重复生成任务");
            f.Clock.Today = day.AddDays(4); f.Service.RefreshDay();
            check(f.Service.VisibleOpen(f.Clock.Today).Count() == 1, "以前未完成的任务堆积到今天");
            check(f.Service.ForDate(day).Single().State == TaskState.Completed, "完成日记录丢失");
            check(f.Service.State.Activities.Single(a => a.Kind == ActivityKind.Completed).TaskId == first.Id, "完成快照指向了另一天");
        });
        test("长期任务结束取消今天和未来提醒，保留完成记录并可重新开始", () =>
        {
            using var f = fixture(); var day = f.Clock.Today;
            var first = f.Service.AddLongTermTask("整理资料", "", day); var ev = f.Service.State.Events.Single();
            var future = f.Service.EnsureOccurrence(ev, day.AddDays(5));
            f.Service.ToggleCompleted(first); f.Service.EndLongTerm(first);
            check(first.State == TaskState.Completed && future.State == TaskState.Cancelled, "结束覆盖了完成记录或留下未来提醒");
            check(f.Service.State.Activities.Count(a => a.Kind == ActivityKind.Ended) == 1, "结束记录缺失");
            f.Service.EndLongTerm(first); check(f.Service.State.Activities.Count(a => a.Kind == ActivityKind.Ended) == 1, "重复结束重复记录");
            f.Clock.Today = day.AddDays(1); f.Service.RefreshDay();
            check(!f.Service.VisibleOpen(f.Clock.Today).Any(), "结束后仍每天出现");
            check(!f.Service.CalendarForYear(day.Year).ContainsKey(day.AddDays(2)), "日历仍预测结束后的任务");
            f.Service.ResumeLongTerm(first);
            check(f.Service.VisibleOpen(f.Clock.Today).Count() == 1 && future.State == TaskState.Open, "重新开始未恢复今天或未来安排");
            f.Service.EndLongTerm(f.Service.VisibleOpen(f.Clock.Today).Single());
            check(!f.Service.VisibleOpen(f.Clock.Today).Any(), "结束未立即移出今日待办");
        });
        test("未来开始与很早开始的长期任务只生成当前需要的实例", () =>
        {
            using var f = fixture(); var today = f.Clock.Today;
            var future = f.Service.AddLongTermTask("将来开始", "", today.AddDays(3));
            check(!f.Service.VisibleOpen(today).Any() && f.Service.IsLongTermActive(future), "开始前出现或无法管理未来任务");
            f.Service.EndLongTerm(future); f.Clock.Today = today.AddDays(3); f.Service.RefreshDay();
            check(!f.Service.VisibleOpen(f.Clock.Today).Any(), "未来开始的任务不能提前结束");
            var ancient = f.Service.AddLongTermTask("持续记录", "", new DateOnly(1901, 1, 1));
            check(f.Service.State.Tasks.Count <= 3, "打开任务时生成了所有历史日期");
            var count = f.Service.State.Tasks.Count; var summaries = f.Service.CalendarForYear(2028);
            check(summaries[new DateOnly(2028, 2, 29)].Tasks.Single().Title == ancient.Title, "每天预测漏掉闰日");
            check(f.Service.State.Tasks.Count == count, "查看全年生成了全部每日实例");
        });
        test("普通任务可转换为长期任务，编辑和星标延续到未来，历史保持快照", () =>
        {
            using var f = fixture(); var day = f.Clock.Today;
            var original = f.Service.AddTask("原任务", "旧备注", day); var id = original.Id;
            f.Service.ConvertToLongTerm(original, "长期目标", "第一版", day, false);
            check(original.Id == id && f.Service.IsLongTerm(original), "转换丢失原任务身份");
            f.Service.ToggleImportant(original); f.Service.ToggleCompleted(original);
            f.Clock.Today = day.AddDays(1); f.Service.RefreshDay(); var current = f.Service.VisibleOpen(f.Clock.Today).Single();
            check(current.IsImportant, "星标未延续到明天");
            f.Service.EditLongTerm(current, "更新目标", "第二版", false);
            check(original.Title == "长期目标" && original.Notes == "第一版", "编辑覆盖了过去的任务");
            check(f.Service.State.Activities.Single(a => a.Kind == ActivityKind.Completed).Title == "长期目标", "编辑覆盖了过去的完成快照");
            f.Clock.Today = day.AddDays(2); f.Service.RefreshDay(); current = f.Service.VisibleOpen(f.Clock.Today).Single();
            check(current.Title == "更新目标" && current.Notes == "第二版" && !current.IsImportant, "新设置未延续到未来");
        });
        test("长期任务备份恢复保留每日完成、结束状态、图片与重新开始", () =>
        {
            using var f = fixture();
            var image = NoteImage.Create("长期备注.png", Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aN1kAAAAASUVORK5CYII="));
            var task = f.Service.AddLongTermTask("长期附图", "备注", f.Clock.Today, true, [image]);
            f.Service.ToggleCompleted(task); f.Service.EndLongTerm(task);
            var file = Path.Combine(f.Store.DataDirectory, "long-term.json"); f.Service.ExportBackup(file);
            using var restored = fixture(); restored.Clock.Today = f.Clock.Today.AddDays(1); restored.Service.RestoreBackup(file);
            var saved = restored.Service.State.Tasks.Single();
            check(saved.State == TaskState.Completed && !restored.Service.IsLongTermActive(saved), "恢复丢失完成或结束状态");
            check(!restored.Service.VisibleOpen(restored.Clock.Today).Any(), "恢复重新打开了已结束任务");
            check(restored.Service.GetNoteImages(saved.ImageIds).Single().Data.SequenceEqual(image.Data), "恢复丢失长期备注图片");
            restored.Service.ResumeLongTerm(saved); var next = restored.Service.VisibleOpen(restored.Clock.Today).Single();
            check(next.IsImportant && next.ImageIds.Single() == image.Id, "恢复后的下一天丢失星标或图片");
            restored.Service.RefreshDay(); check(restored.Service.VisibleOpen(restored.Clock.Today).Count() == 1, "恢复重复生成每日任务");
        });
        test("每天规则跨月、闰日与结束边界正确，旧规则保持原值", () =>
        {
            var ev = new EventDefinition { Title = "每天", StartDate = new DateOnly(2028, 2, 28), EndDate = new DateOnly(2028, 3, 1), Repeat = RepeatRule.Daily };
            check(Recurrence.Between(ev, new(2028, 2, 27), new(2028, 3, 3)).Count() == 3, "每天规则边界错误");
            check((int)RepeatRule.LunarYearly == 4 && (int)RepeatRule.Daily == 5, "改变了旧重复规则的数值");
        });
    }
}
