using DailyRecord.Core;

internal static class AccountAndSyncTests
{
    public static void Run(Action<string, Action> test, string root)
    {
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        AppState Seed() => new() { Tasks = [new TaskItem { Id = "sample", Title = "记录", ScheduledDate = new(2026, 10, 12), OriginalScheduledDate = new(2026, 10, 12), CreatedAt = DateTimeOffset.UtcNow }] };
        test("账号重启后校验密码、拒绝重复账号名与跨账号设备令牌", () =>
        {
            var path = Path.Combine(root, "accounts"); var vault = new AccountVault(path);
            var first = vault.Create("示例甲", "example-password-a"); var second = vault.Create("示例乙", "example-password-b");
            var session = vault.Bind(first.Account.Id, "测试手机");
            Check(vault.DataDirectory(first.Account.Id) != vault.DataDirectory(second.Account.Id), "账号目录未隔离");
            try { vault.Login("示例甲", "wrong-password"); throw new Exception("错误密码未拒绝"); } catch (UnauthorizedAccessException) { }
            try { vault.Create("示例甲", "example-password-c"); throw new Exception("重复账号未拒绝"); } catch (ArgumentException) { }
            try { vault.Authenticate(session.Token, second.Account.Id); throw new Exception("跨账号令牌未拒绝"); } catch (UnauthorizedAccessException) { }
            vault = new AccountVault(path); Check(vault.Login("示例甲", "example-password-a").Id == first.Account.Id, "重启登录失效");
            Check(!File.ReadAllText(Path.Combine(path, "accounts.json")).Contains("example-password"), "账号文件含明文密码");
        });
        test("恢复码只能重置对应账号、旧恢复码和旧设备授权失效", () =>
        {
            var vault = new AccountVault(Path.Combine(root, "recovery")); var created = vault.Create("恢复示例", "old-password"); var session = vault.Bind(created.Account.Id, "旧设备");
            try { vault.ResetPassword(created.Account.Id, "new-password", "invalid"); throw new Exception("错误恢复码未拒绝"); } catch (UnauthorizedAccessException) { }
            var nextCode = vault.ResetPassword(created.Account.Id, "new-password", created.RecoveryCode);
            Check(nextCode != created.RecoveryCode, "恢复码未轮换");
            try { vault.Authenticate(session.Token); throw new Exception("恢复后旧设备令牌仍可用"); } catch (UnauthorizedAccessException) { }
            try { vault.ResetPassword(created.Account.Id, "third-password", created.RecoveryCode); throw new Exception("旧恢复码仍可用"); } catch (UnauthorizedAccessException) { }
            Check(vault.Login("恢复示例", "new-password").Id == created.Account.Id, "新密码无法登录");
        });
        test("账号准备数据失败时不会注册空账号", () =>
        {
            var vault = new AccountVault(Path.Combine(root, "migration-failure"));
            try { vault.Create("迁移示例", "example-password", _ => throw new IOException("sample")); } catch (IOException) { }
            Check(vault.Accounts.Count == 0, "失败迁移留下账号");
        });
        test("两台设备新增不同任务以及编辑不同字段自动合并", () =>
        {
            var b = Seed(); var c = SyncMerge.Clone(b); var p = SyncMerge.Clone(b);
            c.Tasks[0].Title = "电脑标题"; p.Tasks[0].Notes = "手机备注";
            c.Tasks.Add(new TaskItem { Id = "computer", Title = "电脑新增", ScheduledDate = b.Tasks[0].ScheduledDate, OriginalScheduledDate = b.Tasks[0].ScheduledDate });
            p.Tasks.Add(new TaskItem { Id = "phone", Title = "手机新增", ScheduledDate = b.Tasks[0].ScheduledDate, OriginalScheduledDate = b.Tasks[0].ScheduledDate });
            var merged = SyncMerge.Merge(b, c, p);
            Check(merged.Conflicts.Count == 0 && merged.State.Tasks.Count == 3, "独立修改未合并");
            Check(merged.State.Tasks[0].Title == "电脑标题" && merged.State.Tasks[0].Notes == "手机备注", "字段修改丢失");
            Check(b.Tasks[0].Title == "记录" && c.Tasks[0].Notes == "", "合并修改了输入");
        });
        test("同一标题冲突保留两边，备注仍能合并", () =>
        {
            var b = Seed(); var c = SyncMerge.Clone(b); var p = SyncMerge.Clone(b); c.Tasks[0].Title = "电脑"; p.Tasks[0].Title = "手机"; p.Tasks[0].Notes = "保留备注";
            var result = SyncMerge.Merge(b, c, p); Check(result.Conflicts.Count == 1, "未识别标题冲突");
            Check(result.Conflicts[0].Computer!.GetValue<string>() == "电脑" && result.Conflicts[0].Phone!.GetValue<string>() == "手机", "冲突内容未保留");
            Check(result.State.Tasks[0].Notes == "保留备注", "非冲突备注丢失");
        });
        test("完成与取消作为整体状态冲突，不出现无完成日期的完成任务", () =>
        {
            var b = Seed(); var c = SyncMerge.Clone(b); var p = SyncMerge.Clone(b); c.Tasks[0].State = TaskState.Completed; c.Tasks[0].CompletedOn = new(2026, 10, 12); p.Tasks[0].State = TaskState.Cancelled; p.Tasks[0].ArchiveReason = "手机取消";
            var result = SyncMerge.Merge(b, c, p); Check(result.Conflicts.Single().Field == "Status", "状态未整体比较"); StateJson.Validate(result.State);
        });
        test("取消账号同步不重复叠加相同修改，重试具备幂等性", () =>
        {
            var b = Seed(); var p = SyncMerge.Clone(b); p.Tasks[0].Title = "手机更新";
            var first = SyncMerge.Merge(b, b, p); var retry = SyncMerge.Merge(b, first.State, p);
            Check(retry.Conflicts.Count == 0 && retry.State.Tasks.Count == 1 && retry.State.Tasks[0].Title == "手机更新", "重试产生冲突或重复任务");
        });
        test("事件实例稳定身份与旧版随机身份合并，历史引用跟随规范身份", () =>
        {
            var b = new AppState { Events = [new EventDefinition { Id = "event", Title = "每周", StartDate = new(2026, 10, 12), Repeat = RepeatRule.Weekly }] };
            var c = SyncMerge.Clone(b); var p = SyncMerge.Clone(b);
            TaskItem Occurrence(string id, DateTimeOffset at) => new() { Id = id, EventId = "event", OccurrenceDate = new(2026, 10, 12), ScheduledDate = new(2026, 10, 12), OriginalScheduledDate = new(2026, 10, 12), Title = "每周", CreatedAt = at };
            c.Tasks.Add(Occurrence("legacy-id", DateTimeOffset.UtcNow)); p.Tasks.Add(Occurrence(TaskService.OccurrenceId("event", new(2026, 10, 12)), DateTimeOffset.UtcNow.AddSeconds(3)));
            var result = SyncMerge.Merge(b, c, p); Check(result.Conflicts.Count == 0 && result.State.Tasks.Count == 1 && result.State.Tasks[0].Id == "legacy-id", "同一事件生成重复任务");
        });
        test("事件删除与手机编辑发生图关系冲突时保留完整的双方快照", () =>
        {
            var b = new AppState { Events = [new EventDefinition { Id = "event", Title = "事件", StartDate = new(2026, 10, 12) }] };
            b.Tasks.Add(new TaskItem { Id = "task", EventId = "event", OccurrenceDate = new(2026, 10, 12), ScheduledDate = new(2026, 10, 12), OriginalScheduledDate = new(2026, 10, 12), Title = "事件" });
            var c = new AppState(); var p = SyncMerge.Clone(b); p.Tasks[0].Notes = "手机改动";
            var result = SyncMerge.Merge(b, c, p); Check(result.Conflicts.Single().Collection == "Account", "关系冲突未升级为整份比较");
            Check(result.Conflicts[0].Phone!["Tasks"]!.AsArray().Count == 1, "手机记录丢失");
        });
        test("同时完成不会重复统计，两个操作快照仍保留", () =>
        {
            var b = Seed(); var c = SyncMerge.Clone(b); var p = SyncMerge.Clone(b);
            foreach (var state in new[] { c, p }) { state.Tasks[0].State = TaskState.Completed; state.Tasks[0].CompletedOn = new(2026, 10, 12); state.Activities.Add(new ActivityRecord { TaskId = "sample", Title = "记录", Kind = ActivityKind.Completed, Day = new(2026, 10, 12), ScheduledDate = new(2026, 10, 12), At = DateTimeOffset.UtcNow }); }
            var result = SyncMerge.Merge(b, c, p); Check(result.State.Activities.Count == 2 && result.State.Activities.Count(a => !a.Reverted) == 1, "并行完成重复计数或丢失快照");
        });
        test("缺图片的同步提交失败时保留电脑原有状态", () =>
        {
            using var store = new SqliteStore(Path.Combine(root, "sync-rollback")); var service = new TaskService(store, new TestClock()); service.AddTask("原有记录", "", service.Today);
            var incoming = SyncMerge.Clone(service.State); incoming.Tasks[0].Title = "尚未完整的记录"; incoming.Tasks[0].ImageIds = [new string('A', 64)];
            try { service.ApplySync(incoming); throw new Exception("缺图片仍提交"); } catch (InvalidDataException) { }
            Check(service.State.Tasks[0].Title == "原有记录" && store.Load().Tasks[0].Title == "原有记录", "失败提交替换了原数据");
        });
        test("改回原文仍保留字段修改版本，其他字段继续自动合并", () =>
        {
            using var store = new SqliteStore(Path.Combine(root, "sync-version")); var service = new TaskService(store, new TestClock());
            service.AddTask("原文", "", service.Today); var baseline = SyncMerge.Clone(service.State); var version = service.SyncMetadata;
            var current = service.State.Tasks[0]; service.EditTask(current, "临时", "", service.Today, false); service.EditTask(current, "原文", "", service.Today, false);
            var phone = SyncMerge.Clone(baseline); phone.Tasks[0].Title = "手机修改"; phone.Tasks[0].Notes = "独立备注";
            var result = SyncMerge.Merge(baseline, service.State, phone, service.SyncMetadata, version.Version);
            Check(result.Conflicts.Single().Field == "Title" && result.State.Tasks[0].Notes == "独立备注", "修改版本未防止覆盖或阻止无关字段合并");
        });
        test("同步记录与确认回执同事务，失败时两者都不提交", () =>
        {
            var path = Path.Combine(root, "sync-transaction"); using var store = new SqliteStore(path); var state = Seed(); store.Save(state);
            var metadata = store.GetSyncMetadata(); var next = SyncMerge.Clone(state); next.Tasks[0].ImageIds = [new string('A', 64)];
            var receipt = new SyncCommit { DeviceHash = "sample-device", RequestId = "sample-request", RequestHash = "sample-hash" };
            try { store.Save(next, syncCommit: receipt); throw new Exception("不完整提交仍保存"); } catch (InvalidDataException) { }
            Check(store.GetSyncMetadata().Version == metadata.Version && store.GetSyncCommit(receipt.DeviceHash, receipt.RequestId) is null, "失败提交推进版本或写入回执");
            next.Tasks[0].ImageIds = []; next.Tasks[0].Notes = "完整提交"; store.Save(next, syncCommit: receipt);
            using var reopened = new SqliteStore(path); Check(reopened.Load().Tasks[0].Notes == "完整提交" && reopened.GetSyncCommit(receipt.DeviceHash, receipt.RequestId)!.Version == reopened.GetSyncMetadata().Version, "重开数据库丢失记录或回执");
        });
        test("离线手机生成已停止事件的实例时，双方快照保留供选择", () =>
        {
            var baseline = new AppState { Events = [new EventDefinition { Id = "event", Title = "每周", StartDate = new(2026, 10, 12), Repeat = RepeatRule.Weekly }] };
            var computer = SyncMerge.Clone(baseline); computer.Events[0].EndDate = new(2026, 10, 18);
            var phone = SyncMerge.Clone(baseline); phone.Tasks.Add(new TaskItem { EventId = "event", OccurrenceDate = new(2026, 10, 19), Title = "每周", ScheduledDate = new(2026, 10, 19), OriginalScheduledDate = new(2026, 10, 19) });
            var result = SyncMerge.Merge(baseline, computer, phone); Check(result.Conflicts.Single().Collection == "Account" && result.State.Tasks.Count == 0, "离线生成静默重启了已停止事件");
        });
        test("手机和电脑通用备份包含完整图片，恢复与旧格式共存", () =>
        {
            using var store = new SqliteStore(Path.Combine(root, "portable")); var service = new TaskService(store, new TestClock());
            var image = NoteImage.Create("中文备注.png", Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aN1kAAAAASUVORK5CYII="));
            service.AddTask("通用备份示例", "电脑与手机都可恢复", service.Today, images: [image]);
            var path = Path.Combine(root, "portable-sample.json"); store.ExportPortableBackup(path, service.State);
            var restored = SqliteStore.ReadBackupData(path); Check(restored.Images.Single().Id == image.Id && restored.State.Tasks[0].Title == "通用备份示例", "通用备份内容丢失");
            using var destination = new SqliteStore(Path.Combine(root, "portable-restore")); var second = new TaskService(destination, new TestClock()); second.RestoreBackup(path);
            Check(second.State.Tasks.Count == 1 && second.GetNoteImages([image.Id]).Single().Data.SequenceEqual(image.Data), "通用备份无法独立恢复");
        });
    }
    private sealed class TestClock : IClock { public DateOnly Today => new(2026, 10, 12); public DateTimeOffset Now => DateTimeOffset.UtcNow; }
}
