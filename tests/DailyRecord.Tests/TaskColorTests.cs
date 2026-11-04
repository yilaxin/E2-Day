using DailyRecord.Core;

internal static class TaskColorTests
{
    internal static void Run(Action<string, Action> test, Action<bool, string> check, Func<Fixture> fixture, string runRoot)
    {
        test("旧记录默认无色，五个分类名称按账号独立并拒绝非法名称", () =>
        {
            var old = StateJson.Deserialize("{\"SchemaVersion\":2,\"Tasks\":[],\"Events\":[],\"Activities\":[],\"Settings\":{},\"News\":{}}");
            check(old.Settings.TaskColorNames.SequenceEqual(TaskColors.DefaultNames), "旧格式没有默认分类");
            using var f = fixture(); using var other = fixture();
            f.Service.RenameTaskColors(["上午", "下午", "晚上", "等回复", "周末"]);
            check(other.Service.State.Settings.TaskColorNames.SequenceEqual(TaskColors.DefaultNames), "分类名称串到其他账号");
            var before = StateJson.Serialize(f.Service.State); var revision = f.Service.Revision;
            foreach (var invalid in new string[][] { ["a"], ["a", "a", "b", "c", "d"], ["全部", "b", "c", "d", "e"], ["", "b", "c", "d", "e"], [new('长', 13), "b", "c", "d", "e"], ["a\nb", "b", "c", "d", "e"] })
            { try { f.Service.RenameTaskColors(invalid); throw new Exception("非法名称被保存"); } catch (InvalidDataException) { } }
            check(before == StateJson.Serialize(f.Service.State) && revision == f.Service.Revision, "无效名称修改了记录");
        });
        test("单项批量着色一次保存，去重并保留星标、备注与操作历史", () =>
        {
            using var f = fixture(); var day = f.Clock.Today;
            var a = f.Service.AddTask("第一项", "备注", day, true); var b = f.Service.AddTask("第二项", "", day);
            f.Service.ToggleCompleted(b); var history = StateJson.Serialize(new AppState { Activities = f.Service.State.Activities });
            var revision = f.Service.Revision; f.Service.SetTaskColors([a, b, a], TaskColor.Blue);
            check(f.Service.Revision == revision + 1 && a.Color == TaskColor.Blue && b.Color == TaskColor.Blue && a.IsImportant && a.Notes == "备注", "批量保存或内容错误");
            check(b.State == TaskState.Completed && history == StateJson.Serialize(new AppState { Activities = f.Service.State.Activities }), "着色重写了完成历史");
            f.Service.SetTaskColors([a], TaskColor.None); check(a.Color == TaskColor.None && b.Color == TaskColor.Blue, "移除颜色影响别的任务");
            var before = StateJson.Serialize(f.Service.State); revision = f.Service.Revision;
            foreach (var action in new Action[] { () => f.Service.SetTaskColors([a, new TaskItem()], TaskColor.Pink), () => f.Service.SetTaskColors([a], (TaskColor)8) })
            { try { action(); throw new Exception("无效批量被保存"); } catch (ArgumentException) { } }
            check(before == StateJson.Serialize(f.Service.State) && revision == f.Service.Revision, "批量失败前部分任务已被修改");
        });
        test("颜色筛选和未完成数统一包含到期、提前、已查看与今日完成", () =>
        {
            using var f = fixture(); var day = f.Clock.Today;
            f.Service.AddTask("到期", "", day, color: TaskColor.Blue);
            var completed = f.Service.AddTask("完成", "", day, color: TaskColor.Blue); f.Service.ToggleCompleted(completed);
            var future = f.Service.AddEvent("提前", "", day.AddDays(2), EventImportance.Important, RepeatRule.None, color: TaskColor.Blue);
            var seen = f.Service.EnsureOccurrence(future, future.StartDate); f.Service.ToggleViewed(seen);
            f.Service.AddTask("未来不显示", "", day.AddDays(20), color: TaskColor.Blue);
            var archived = f.Service.AddTask("归档不显示", "", day, color: TaskColor.Blue); f.Service.CancelTask(archived);
            f.Service.AddTask("另一分类", "", day, color: TaskColor.Pink);
            var revision = f.Service.Revision; var result = f.Service.FilterToday(TaskColor.Blue);
            check(result.Count == 3 && result.Count(t => t.State == TaskState.Open) == 2 && result.Contains(seen), "颜色范围或已查看计数错误");
            check(f.Service.FilterToday(TaskColor.None).Count == 0 && f.Service.FilterToday(null).Count == 4 && revision == f.Service.Revision, "筛选写入或混入其他任务");
        });
        test("重复安排默认继承颜色，单次分类保留，后续修改不改完成历史", () =>
        {
            using var f = fixture(); var day = f.Clock.Today;
            var ev = f.Service.AddEvent("每周", "", day, EventImportance.Normal, RepeatRule.Weekly, color: TaskColor.Purple);
            var today = f.Service.EnsureOccurrence(ev, day); var next = f.Service.EnsureOccurrence(ev, day.AddDays(7));
            var done = f.Service.EnsureOccurrence(ev, day.AddDays(14)); f.Service.ToggleCompleted(done);
            f.Service.SetTaskColors([next], TaskColor.Teal);
            f.Service.SetTaskColors([today], TaskColor.Blue, EditScope.ThisAndFuture);
            check(today.Color == TaskColor.Blue && ev.Color == TaskColor.Blue && next.Color == TaskColor.Teal && next.ColorOverride && done.Color == TaskColor.Purple,
                "后续颜色覆盖了单次分类或已完成事项");
            check(f.Service.EnsureOccurrence(ev, day.AddDays(21)).Color == TaskColor.Blue, "新实例没有继承分类");
            check(f.Service.State.Activities.Single().Color == TaskColor.Purple, "历史没有保存当时颜色");
        });
        test("长期任务跨天继承及类型互转保留分类，结束记录保留颜色", () =>
        {
            using var f = fixture(); var task = f.Service.AddLongTermTask("每天", "", f.Clock.Today, color: TaskColor.Teal);
            f.Service.SetTaskColors([task], TaskColor.Pink, EditScope.ThisAndFuture);
            f.Clock.Today = f.Clock.Today.AddDays(1); f.Service.RefreshDay();
            var next = f.Service.VisibleOpen(f.Clock.Today).Single(); check(next.Color == TaskColor.Pink, "长期次日未继承");
            f.Service.EndLongTerm(next); check(f.Service.State.Activities.All(a => a.Color == TaskColor.Pink), "结束历史丢失颜色");
            f.Service.ConvertItem(task, ItemKind.Task, task.Title, task.Notes, task.ScheduledDate, false);
            f.Service.ConvertItem(task, ItemKind.Event, task.Title, task.Notes, task.ScheduledDate, false);
            check(task.Color == TaskColor.Pink && f.Service.State.Events.Single(e => e.Id == task.EventId).Color == TaskColor.Pink, "互转丢失颜色");
            f.Service.ConvertItem(task, ItemKind.LongTerm, task.Title, task.Notes, f.Clock.Today, false, color: TaskColor.Blue);
            check(task.Color == TaskColor.Blue && f.Service.State.Events.Single(e => e.Id == task.EventId).Color == TaskColor.Blue &&
                f.Service.EnsureOccurrence(f.Service.State.Events.Single(e => e.Id == task.EventId), f.Clock.Today.AddDays(1)).Color == TaskColor.Blue, "转换时选的新颜色没有成为新安排的默认色");
        });
        test("SQLite与含图备份往返保留颜色、名称和历史分类", () =>
        {
            using var f = fixture();
            var image = NoteImage.Create("分类样例.png", Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aN1kAAAAASUVORK5CYII="));
            f.Service.RenameTaskColors(["上午", "下午", "零碎", "沟通", "以后"]);
            var task = f.Service.AddTask("颜色备份样例", "原图与备注", f.Clock.Today, true, [image], TaskColor.Blue);
            f.Service.ToggleCompleted(task); f.Service.SetTaskColors([task], TaskColor.Gray);
            var loaded = f.Store.Load(); check(loaded.Tasks.Single().Color == TaskColor.Gray && loaded.Activities.Single().Color == TaskColor.Blue && loaded.Settings.TaskColorNames[0] == "上午", "数据库丢失分类");
            var path = Path.Combine(f.Store.DataDirectory, "color-backup.json"); f.Service.ExportBackup(path);
            f.Store.ExportPortableBackup(Path.Combine(runRoot, "color-sample.json"), f.Service.State);
            var backup = SqliteStore.ReadBackupData(path);
            check(backup.State.Tasks.Single().Color == TaskColor.Gray && backup.State.Activities.Single().Color == TaskColor.Blue && backup.Images.Single().Data.SequenceEqual(image.Data), "备份丢失历史分类或原图");
            f.Service.RenameTaskColors(TaskColors.DefaultNames); f.Service.RestoreBackup(path);
            check(f.Service.State.Settings.TaskColorNames[0] == "上午" && f.Service.State.Tasks.Single().Color == TaskColor.Gray, "恢复备份丢失名称");
        });
        test("文件合并和同步保留颜色，非法枚举在导入前拒绝", () =>
        {
            using var f = fixture(); var task = f.Service.AddTask("合并", "", f.Clock.Today);
            var original = SyncMerge.Clone(f.Service.State); f.Service.SetTaskColors([task], TaskColor.Purple);
            var plan = FileImport.Preview(original, f.Service.State); var merged = plan.Resolve(new Dictionary<int, string> { [0] = "incoming" });
            check(merged.Tasks.Single().Color == TaskColor.Purple, "文件合并丢失分类");
            var phone = SyncMerge.Clone(original); phone.Tasks.Single().Notes = "手机备注";
            var sync = SyncMerge.Merge(original, f.Service.State, phone);
            check(sync.Conflicts.Count == 0 && sync.State.Tasks.Single().Color == TaskColor.Purple && sync.State.Tasks.Single().Notes == "手机备注", "独立字段同步互相覆盖");
            var invalid = StateJson.Serialize(f.Service.State).Replace("\"Purple\"", "\"UnknownColor\"");
            try { StateJson.Deserialize(invalid); throw new Exception("未知颜色导入成功"); } catch (System.Text.Json.JsonException) { }
        });
    }
}
