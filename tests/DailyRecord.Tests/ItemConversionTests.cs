using DailyRecord.Core;

internal static class ItemConversionTests
{
    internal static void Run(Action<string, Action> test, Action<bool, string> check, Func<Fixture> fixture, string runRoot)
    {
        var image = NoteImage.Create("转换示例.png", Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aN1kAAAAASUVORK5CYII="));
        TaskItem Seed(Fixture f, ItemKind kind) => kind switch
        {
            ItemKind.Task => f.Service.AddTask("原事项", "文字备注", f.Clock.Today, true, [image]),
            ItemKind.LongTerm => f.Service.AddLongTermTask("原事项", "文字备注", f.Clock.Today, true, [image]),
            _ => f.Service.EnsureOccurrence(f.Service.AddEvent("原事项", "文字备注", f.Clock.Today, EventImportance.Important, RepeatRule.None, [image], true), f.Clock.Today)
        };
        test("任务、事件、长期任务六个方向互转保留身份、文字、图片和星标", () =>
        {
            foreach (var source in Enum.GetValues<ItemKind>()) foreach (var target in Enum.GetValues<ItemKind>().Where(k => k != source))
            {
                using var f = fixture(); var task = Seed(f, source); var id = task.Id; var created = task.CreatedAt; var original = task.OriginalScheduledDate;
                if (source == ItemKind.Event) f.Service.ToggleViewed(task);
                var history = StateJson.Serialize(new AppState { Activities = f.Service.State.Activities });
                f.Service.ConvertItem(task, target, task.Title, task.Notes, task.ScheduledDate, task.IsImportant, EventImportance.VeryImportant);
                check(task.Id == id && task.CreatedAt == created && task.OriginalScheduledDate == original && task.State == TaskState.Open, "原事项身份或状态改变");
                check(f.Service.KindOf(task) == target && task.Title == "原事项" && task.Notes == "文字备注" && task.IsImportant && task.ImageIds.SequenceEqual([image.Id]), "内容或目标类型错误");
                check(f.Service.GetNoteImages(task.ImageIds).Single().Data.SequenceEqual(image.Data), "原图丢失");
                check(f.Service.State.Tasks.Count(t => t.ScheduledDate == f.Clock.Today && t.State != TaskState.Cancelled) == 1, "本次被重复生成");
                check(history == StateJson.Serialize(new AppState { Activities = f.Service.State.Activities }), "转换修改了既有历史");
                if (target != ItemKind.Event) check(!f.Service.IsViewed(task, f.Clock.Today), "旧事件查看状态隐藏了普通或长期任务");
            }
        });
        test("仅转换月末这一次，原重复规则和闰日不漂移且不会重新生成", () =>
        {
            using var f = fixture(); f.Clock.Today = new(2028, 2, 29);
            var ev = f.Service.AddEvent("月底安排", "", new(2028, 1, 31), EventImportance.Normal, RepeatRule.Monthly);
            var task = f.Service.ForDate(f.Clock.Today).Single(t => t.EventId == ev.Id); var id = task.Id;
            f.Service.ConvertItem(task, ItemKind.Task, task.Title, "保留这一次", task.ScheduledDate.AddDays(1), false);
            f.Service.RefreshDay(); f.Service.ForDate(new(2028, 2, 29));
            check(f.Service.State.Tasks.Count(t => t.Id == id) == 1 && !f.Service.State.Tasks.Any(t => t.EventId == ev.Id && t.OccurrenceDate == new DateOnly(2028, 2, 29)), "原重复本次重新出现");
            check(f.Service.ScheduledCount(new(2028, 2, 29)) == 0 && f.Service.ScheduledCount(new(2028, 3, 1)) == 1, "改期后的日历重复计数");
            check(f.Service.CalendarForYear(2028)[new(2028, 3, 31)].ScheduledCount == 1 && f.Service.EventTitles(new(2028, 3, 31)).Contains("月底安排"), "后续月末重复发生漂移");
            check(ReferenceEquals(f.Service.EnsureOccurrence(ev, new(2028, 2, 29)), task), "显式取原本次生成了第二条身份");
        });
        test("转换并停止后续只归档未完成安排，已完成的未来实例和历史保留", () =>
        {
            using var f = fixture(); var day = f.Clock.Today;
            var ev = f.Service.AddEvent("每周复盘", "", day, EventImportance.Normal, RepeatRule.Weekly);
            var selected = f.Service.EnsureOccurrence(ev, day); var pending = f.Service.EnsureOccurrence(ev, day.AddDays(7));
            var completed = f.Service.EnsureOccurrence(ev, day.AddDays(14)); f.Service.ToggleCompleted(completed);
            var history = f.Service.State.Activities.Single(a => a.TaskId == completed.Id); var count = new TaskAnalytics(f.Service.State, day).Completions.Count;
            check(f.Service.ConversionAffectedCount(selected, EditScope.ThisAndFuture) == 1, "预览的影响数量错误");
            f.Service.ConvertItem(selected, ItemKind.LongTerm, selected.Title, selected.Notes, day, false, scope: EditScope.ThisAndFuture);
            check(ev.EndDate == day.AddDays(-1) && pending.State == TaskState.Cancelled && completed.State == TaskState.Completed, "停止范围改变完成状态");
            check(!history.Reverted && new TaskAnalytics(f.Service.State, day).Completions.Count == count && selected.State == TaskState.Open, "转换被当作完成或撤销历史");
            check(!Recurrence.Between(ev, day, day.AddDays(40)).Any(), "旧规则没有停止");
        });
        test("已完成长期任务多次转换保留原打卡归属，撤销后重新完成归入新类型", () =>
        {
            using var f = fixture(); var task = Seed(f, ItemKind.LongTerm); var old = task.EventId!; var id = task.Id;
            f.Service.ToggleCompleted(task);
            foreach (var target in new[] { ItemKind.Event, ItemKind.Task, ItemKind.LongTerm })
                f.Service.ConvertItem(task, target, task.Title, task.Notes, f.Clock.Today, task.IsImportant);
            var report = new TaskAnalytics(f.Service.State, f.Clock.Today);
            check(task.Id == id && task.State == TaskState.Completed && report.Completions.Count == 1 &&
                report.Habits.Single(h => h.Definition.Id == old).CompletedDays.ContainsKey(f.Clock.Today), "原打卡或完成数被转移或删除");
            check(report.Habits.Single(h => h.Definition.Id == task.EventId).CompletedDays.Count == 0, "同一次完成重复计入新长期任务");
            f.Service.ToggleCompleted(task); f.Service.ToggleCompleted(task);
            report = new TaskAnalytics(f.Service.State, f.Clock.Today);
            check(report.Habits.Single(h => h.Definition.Id == old).CompletedDays.Count == 0 &&
                report.Habits.Single(h => h.Definition.Id == task.EventId).CompletedDays.Count == 1 && report.Completions.Count == 1, "重新完成没有使用新归属");
        });
        test("旧格式已完成普通任务改为长期任务不会追认历史打卡", () =>
        {
            using var f = fixture(); var task = Seed(f, ItemKind.Task); f.Service.ToggleCompleted(task); task.CompletedOrigin = null; f.Service.Save();
            f.Service.ConvertToLongTerm(task, task.Title, task.Notes, task.ScheduledDate, task.IsImportant);
            var report = new TaskAnalytics(f.Service.State, f.Clock.Today);
            check(report.Completions.Count == 1 && report.Habits.Single().CompletedDays.Count == 0 && task.State == TaskState.Completed, "旧完成被追认为打卡或重新打开");
        });
        test("互转来源、完整图片、历史和打卡支持重载、备份与文件合并", () =>
        {
            using var f = fixture(); var task = Seed(f, ItemKind.LongTerm); f.Service.ToggleCompleted(task);
            var before = SyncMerge.Clone(f.Service.State); var old = task.EventId;
            f.Service.ConvertItem(task, ItemKind.Task, task.Title, task.Notes, task.ScheduledDate, task.IsImportant);
            var backup = Path.Combine(f.Store.DataDirectory, "converted.json"); f.Service.ExportBackup(backup);
            f.Store.ExportPortableBackup(Path.Combine(runRoot, "conversion-sample.json"), f.Service.State);
            using var other = fixture(); other.Service.RestoreBackup(backup);
            var reloaded = other.Store.Load(); var restored = reloaded.Tasks.Single(t => t.Id == task.Id);
            check(restored.ConversionSources.Single().EventId == old && restored.CompletedOrigin?.EventId == old && other.Service.GetNoteImages(restored.ImageIds).Single().Data.SequenceEqual(image.Data), "备份或重载丢失转换来源或图片");
            var legacy = SyncMerge.Clone(reloaded); legacy.Tasks.Single(t => t.Id == task.Id).ConversionSources.Clear(); other.Store.Save(legacy);
            var reopened = new TaskService(other.Store, other.Clock);
            check(reopened.State.Tasks.Count(t => t.Id == task.Id) == 1 && reopened.State.Tasks.Single(t => t.Id == task.Id).ConversionSources.Count == 1,
                "旧客户端遗漏来源后，重新打开生成了冲突身份");
            var plan = FileImport.Preview(before, f.Service.State); var merged = plan.Resolve(Enumerable.Range(0, plan.Conflicts.Count).ToDictionary(i => i, _ => "incoming"));
            check(merged.Tasks.Single(t => t.Id == task.Id).ConversionSources.Count == 1 && new TaskAnalytics(merged, f.Clock.Today).Habits.Single().CompletedDays.Count == 1, "文件交换丢失原打卡归属");
        });
        test("无效转换不写入，旧客户端重复生成冲突在导入前拒绝", () =>
        {
            using var f = fixture(); var task = Seed(f, ItemKind.LongTerm); var ev = f.Service.State.Events.Single();
            var before = StateJson.Serialize(f.Service.State); var revision = f.Service.Revision;
            foreach (var bad in new[] { (ItemKind)99, ItemKind.LongTerm })
            { try { f.Service.ConvertItem(task, bad, task.Title, task.Notes, task.ScheduledDate, false); throw new Exception("未拒绝错误类型"); } catch (ArgumentException) { } }
            check(before == StateJson.Serialize(f.Service.State) && revision == f.Service.Revision, "无效转换修改记录");
            f.Service.ConvertItem(task, ItemKind.Task, task.Title, task.Notes, task.ScheduledDate, true);
            var invalid = SyncMerge.Clone(f.Service.State);
            invalid.Tasks.Add(new TaskItem { Title = "旧客户端重新生成", EventId = ev.Id, OccurrenceDate = f.Clock.Today, ScheduledDate = f.Clock.Today, OriginalScheduledDate = f.Clock.Today });
            try { StateJson.Validate(invalid); throw new Exception("允许转换后重新生成同一次"); } catch (InvalidDataException) { }
        });
        test("仅转换长期任务今天不影响明天，已归档互转不会恢复待办", () =>
        {
            using var f = fixture(); var task = Seed(f, ItemKind.LongTerm); var old = task.EventId!; var day = f.Clock.Today;
            f.Service.ConvertItem(task, ItemKind.Task, task.Title, task.Notes, day, task.IsImportant);
            f.Clock.Today = day.AddDays(1); f.Service.RefreshDay();
            check(f.Service.State.Tasks.Count(t => t.EventId == old && t.ScheduledDate == f.Clock.Today) == 1, "只转换今天却停止了明天");
            f.Service.CancelTask(task); var count = f.Service.VisibleOpen(f.Clock.Today).Count();
            f.Service.ConvertItem(task, ItemKind.Event, task.Title, task.Notes, day, task.IsImportant, repeat: RepeatRule.Weekly);
            check(task.State == TaskState.Cancelled && f.Service.VisibleOpen(f.Clock.Today).Count() == count, "已归档转换恢复了提醒");
            f.Service.ConvertItem(task, ItemKind.LongTerm, task.Title, task.Notes, day.AddDays(10), task.IsImportant);
            check(!f.Service.IsLongTermActive(task) && new TaskAnalytics(f.Service.State, f.Clock.Today).Habits.Single(h => h.Definition.Id == task.EventId).Ended, "已归档长期任务被显示为活动");
        });
    }
}
