using System.Text.Json;
using DailyRecord.Core;

internal static class FileImportTests
{
    public static void Run(Action<string, Action> test, string root)
    {
        void Check(bool condition, string text) { if (!condition) throw new Exception(text); }
        AppState Seed() => new() { Tasks = [new TaskItem { Id = "shared", Title = "已有记录", ScheduledDate = new(2026, 10, 14), OriginalScheduledDate = new(2026, 10, 14), CreatedAt = DateTimeOffset.Parse("2026-10-14T08:00:00.1234567+08:00") }] };
        var fixtureDirectory = Path.Combine(root, "file-exchange"); Directory.CreateDirectory(fixtureDirectory);
        test("文件合并保留双方不同任务，重复导入不新增，不按缺失记录删除", () =>
        {
            var current = Seed(); var incoming = Seed(); incoming.Tasks[0].Id = "mobile-only"; incoming.Tasks[0].Title = "手机独立添加";
            var plan = FileImport.Preview(current, incoming); var merged = plan.Resolve(new Dictionary<int, string>());
            Check(plan.Added == 1 && merged.Tasks.Count == 2, "未保留双方任务");
            Check(FileImport.Preview(merged, incoming).Added == 0, "重复导入增加记录");
            Check(current.Tasks.Count == 1 && incoming.Tasks.Count == 1, "预览改变输入");
        });
        test("同一记录差异需要明确选择，时间格式差异不产生假冲突", () =>
        {
            var current = Seed(); var incoming = Seed(); incoming.Tasks[0].CreatedAt = DateTimeOffset.Parse("2026-10-14T00:00:00.123Z");
            Check(FileImport.Preview(current, incoming).Conflicts.Count == 0, "等价时间格式产生冲突");
            incoming.Tasks[0].Title = "来自文件的新标题"; var plan = FileImport.Preview(current, incoming);
            try { plan.Resolve(new Dictionary<int, string>()); throw new Exception("未选择版本也完成合并"); } catch (InvalidDataException) { }
            Check(plan.Resolve(new Dictionary<int, string> { [0] = "current" }).Tasks[0].Title == current.Tasks[0].Title, "本机选择失效");
            Check(plan.Resolve(new Dictionary<int, string> { [0] = "incoming" }).Tasks[0].Title == incoming.Tasks[0].Title, "文件选择失效");
            File.WriteAllText(Path.Combine(fixtureDirectory, "difference.json"), JsonSerializer.Serialize(new { Current = current, Incoming = incoming, ExpectedAdded = plan.Added, ExpectedConflicts = plan.Conflicts.Count }, StateJson.Options));
        });
        test("重复事件跨设备生成不同身份时合并为同一任务并修正历史引用", () =>
        {
            var current = Seed(); current.Events.Add(new EventDefinition { Id = "event", Title = "每周事项", StartDate = new(2026, 10, 14), Repeat = RepeatRule.Weekly });
            current.Tasks[0].EventId = "event"; current.Tasks[0].OccurrenceDate = new(2026, 10, 14);
            var incoming = SyncMerge.Clone(current); incoming.Tasks[0].Id = "legacy-random";
            incoming.Activities.Add(new ActivityRecord { Id = "history", TaskId = "legacy-random", Title = "看过", ScheduledDate = new(2026, 10, 14), Day = new(2026, 10, 14), Kind = ActivityKind.Viewed, At = DateTimeOffset.UtcNow });
            var plan = FileImport.Preview(current, incoming); var result = plan.Resolve(new Dictionary<int, string>());
            Check(result.Tasks.Count == 1 && result.Activities.Single().TaskId == "shared", "实例或历史引用重复");
            File.WriteAllText(Path.Combine(fixtureDirectory, "occurrence.json"), JsonSerializer.Serialize(new { Current = current, Incoming = incoming, ExpectedAdded = plan.Added, ExpectedConflicts = plan.Conflicts.Count }, StateJson.Options));
        });
        test("完成版本选择保留双方历史快照，只计入选定完成日期", () =>
        {
            var current = Seed(); current.Tasks[0].State = TaskState.Completed; current.Tasks[0].CompletedOn = new(2026, 10, 14);
            current.Activities.Add(new ActivityRecord { Id = "pc-complete", TaskId = "shared", Title = "原标题", Kind = ActivityKind.Completed, Day = new(2026, 10, 14), ScheduledDate = new(2026, 10, 14), At = DateTimeOffset.UtcNow });
            var incoming = SyncMerge.Clone(current); incoming.Activities.Clear(); incoming.Tasks[0].CompletedOn = new(2026, 10, 15);
            incoming.Activities.Add(new ActivityRecord { Id = "phone-complete", TaskId = "shared", Title = "新标题", Kind = ActivityKind.Completed, Day = new(2026, 10, 15), ScheduledDate = new(2026, 10, 14), At = DateTimeOffset.UtcNow.AddDays(1) });
            var plan = FileImport.Preview(current, incoming); var result = plan.Resolve(new Dictionary<int, string> { [0] = "incoming" });
            Check(result.Activities.Count == 2 && result.Activities.Count(history => !history.Reverted) == 1, "历史丢失或完成次数重复");
        });
        test("导入前保留含图副本，原图片和导入图片可跨数据库恢复", () =>
        {
            using var fixture = new Fixture(Path.Combine(root, "file-import-store"), new(2026, 10, 14));
            var task = fixture.Service.AddTask("本机任务", "", fixture.Clock.Today);
            var localImage = NoteImage.Create("本机图片.png", Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aN1kAAAAASUVORK5CYII="));
            fixture.Service.EditTask(task, task.Title, "本机备注", task.ScheduledDate, false, [localImage]);
            var incomingImage = NoteImage.Create("文件图片.png", Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg=="));
            var incoming = Seed(); incoming.Tasks[0].ImageIds = [incomingImage.Id];
            var path = fixture.Service.ImportRecords(FileImport.Preview(fixture.Service.State, incoming), new Dictionary<int, string>(), [incomingImage]);
            var before = SqliteStore.ReadBackupData(path);
            Check(File.Exists(path) && before.State.Tasks.Single().Id == task.Id && before.Images.Single().Id == localImage.Id, "导入前副本不正确");
            Check(fixture.Service.State.Tasks.Count == 2, "文件任务未合并");
            fixture.Store.ExportPortableBackup(Path.Combine(fixtureDirectory, "exchange.json"), fixture.Service.State);
            var exported = SqliteStore.ReadBackupData(Path.Combine(fixtureDirectory, "exchange.json"));
            using var receiving = new SqliteStore(Path.Combine(root, "another-device")); receiving.Save(exported.State, exported.Images);
            Check(exported.State.Tasks.Count == 2 && receiving.GetNoteImages([localImage.Id, incomingImage.Id]).Count == 2, "交换文件或图片不兼容");
        });
        test("预览后本机新增记录或图片校验失败时不会覆盖当前数据", () =>
        {
            using var fixture = new Fixture(Path.Combine(root, "stale-file-import"), new(2026, 10, 14));
            fixture.Service.AddTask("本机", "", fixture.Clock.Today); var incoming = Seed(); var stale = FileImport.Preview(fixture.Service.State, incoming);
            fixture.Service.AddTask("预览后的修改", "", fixture.Clock.Today);
            try { fixture.Service.ImportRecords(stale, new Dictionary<int, string>(), []); throw new Exception("旧预览覆盖新记录"); } catch (InvalidDataException) { }
            var before = FileImport.Fingerprint(fixture.Service.State);
            incoming.Tasks[0].ImageIds = [new string('A', 64)]; var plan = FileImport.Preview(fixture.Service.State, incoming);
            try { fixture.Service.ImportRecords(plan, new Dictionary<int, string>(), []); throw new Exception("缺图导入没有失败"); } catch (InvalidDataException) { }
            Check(FileImport.Fingerprint(fixture.Service.State) == before && FileImport.Fingerprint(fixture.Store.Load()) == before, "失败导入改变原记录");
        });
    }
}
