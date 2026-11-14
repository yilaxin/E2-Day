using DailyRecord.Core;

internal static class QuickTaskColorTests
{
    internal static void Run(Action<string, Action> test, Action<bool, string> check, Func<Fixture> fixture, string runRoot)
    {
        test("快捷着色双击往返无色和已有颜色，保留状态备注星标和历史", () =>
        {
            using var f = fixture();
            foreach (var color in Enum.GetValues<TaskColor>())
            {
                var task = f.Service.AddTask("快捷示例", "原备注", f.Clock.Today, true, color: color);
                f.Service.ToggleCompleted(task); var history = StateJson.Serialize(new AppState { Activities = f.Service.State.Activities });
                f.Service.ToggleQuickTaskColor(task);
                check(task.Color == (color == TaskColor.Blue ? TaskColor.None : TaskColor.Blue), "首次点击目标错误");
                if (color != TaskColor.Blue)
                {
                    f.Service.ToggleQuickTaskColor(task); check(task.Color == color && task.QuickColorRestore is null, "再次点击没有恢复原颜色");
                }
                check(task.State == TaskState.Completed && task.IsImportant && task.Notes == "原备注" &&
                    history == StateJson.Serialize(new AppState { Activities = f.Service.State.Activities }), "颜色操作改变了完成快照或内容");
            }
        });
        test("快捷颜色按账号保存，切换快捷色按当前实际颜色建立恢复链", () =>
        {
            using var f = fixture(); using var other = fixture();
            var task = f.Service.AddTask("分类", "", f.Clock.Today, color: TaskColor.Purple);
            f.Service.ToggleQuickTaskColor(task); f.Service.SetQuickTaskColor(TaskColor.Teal);
            f.Service.ToggleQuickTaskColor(task); check(task.Color == TaskColor.Teal, "新快捷颜色未生效");
            f.Service.ToggleQuickTaskColor(task); check(task.Color == TaskColor.Blue && task.QuickColorRestore is null, "切换快捷色后没有恢复最近实际颜色");
            check(other.Service.State.Settings.QuickTaskColor == TaskColor.Blue && f.Store.Load().Settings.QuickTaskColor == TaskColor.Teal, "账号偏好没有隔离或持久化");
            var revision = f.Service.Revision;
            foreach(var invalid in new[] { TaskColor.None, (TaskColor)30 })
            { try { f.Service.SetQuickTaskColor(invalid); throw new Exception("非法快捷色已保存"); } catch(ArgumentException) { } }
            check(revision == f.Service.Revision, "无效设置导致写入");
        });
        test("重启及含图备份保留快捷色和可恢复原颜色，旧备份默认蓝色", () =>
        {
            using var f = fixture(); var image = NoteImage.Create("样例.png", Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aN1kAAAAASUVORK5CYII="));
            var task = f.Service.AddTask("可恢复分类样例", "原图备注", f.Clock.Today, true, [image], TaskColor.Purple);
            f.Service.ToggleQuickTaskColor(task);
            var backup = Path.Combine(f.Store.DataDirectory, "quick-backup.json"); f.Service.ExportBackup(backup);
            f.Store.ExportPortableBackup(Path.Combine(runRoot, "quick-color-sample.json"), f.Service.State);
            var restarted = new TaskService(f.Store, f.Clock); var loaded = restarted.State.Tasks.Single();
            check(loaded.Color == TaskColor.Blue && loaded.QuickColorRestore?.PreviousColor == TaskColor.Purple, "重启丢失恢复信息");
            restarted.ToggleQuickTaskColor(loaded); check(loaded.Color == TaskColor.Purple, "重启后恢复失败");
            restarted.RestoreBackup(backup); loaded = restarted.State.Tasks.Single(); restarted.ToggleQuickTaskColor(loaded);
            check(loaded.Color == TaskColor.Purple && restarted.GetNoteImages(loaded.ImageIds).Single().Data.SequenceEqual(image.Data), "备份恢复后原色或原图丢失");
            check(StateJson.Deserialize("{\"SchemaVersion\":2}").Settings.QuickTaskColor == TaskColor.Blue, "旧备份默认快捷色错误");
        });
        test("手动选色、批量和互转清除旧快捷恢复，取消归档状态不变", () =>
        {
            using var f = fixture(); var task = f.Service.AddTask("待处理", "", f.Clock.Today, color: TaskColor.Pink);
            f.Service.ToggleQuickTaskColor(task); f.Service.SetTaskColors([task], TaskColor.Blue);
            check(task.QuickColorRestore is null, "显式选择同颜色仍保留旧恢复");
            f.Service.ToggleQuickTaskColor(task); check(task.Color == TaskColor.None, "手动选色后误恢复旧分类");
            f.Service.ToggleQuickTaskColor(task); f.Service.CancelTask(task); f.Service.ToggleQuickTaskColor(task);
            check(task.State == TaskState.Cancelled && task.Color == TaskColor.None, "归档快捷操作恢复了待办");
            f.Service.ToggleQuickTaskColor(task); f.Service.ConvertItem(task, ItemKind.LongTerm, task.Title, task.Notes, task.ScheduledDate, false);
            check(task.QuickColorRestore is null && task.Color == TaskColor.Blue && task.State == TaskState.Cancelled, "互转留下旧恢复信息或改变状态");
        });
        test("重复和长期快捷仅本次，恢复单次标记且保护期间变更的后续默认", () =>
        {
            using var f = fixture(); var task = f.Service.AddLongTermTask("每天", "", f.Clock.Today, color: TaskColor.Purple);
            var ev = f.Service.State.Events.Single(); f.Service.ToggleQuickTaskColor(task);
            var next = f.Service.EnsureOccurrence(ev, f.Clock.Today.AddDays(1));
            check(task.ColorOverride && task.Color == TaskColor.Blue && next.Color == TaskColor.Purple && ev.Color == TaskColor.Purple, "快捷污染了后续默认");
            f.Service.ToggleQuickTaskColor(task); check(!task.ColorOverride && task.Color == TaskColor.Purple, "未恢复原继承标记");
            f.Service.ToggleQuickTaskColor(task); f.Service.SetTaskColors([next], TaskColor.Teal, EditScope.ThisAndFuture);
            f.Service.ToggleQuickTaskColor(task);
            check(task.Color == TaskColor.Purple && task.ColorOverride && ev.Color == TaskColor.Teal, "恢复时改变了新默认或丢失单次保护");
        });
        test("颜色与恢复信息整体同步，拒绝不合法恢复数据", () =>
        {
            using var f = fixture(); var task = f.Service.AddTask("同步", "", f.Clock.Today, color: TaskColor.Purple);
            var baseline = SyncMerge.Clone(f.Service.State); f.Service.ToggleQuickTaskColor(task);
            var phone = SyncMerge.Clone(baseline); phone.Tasks.Single().Notes = "手机备注";
            var merged = SyncMerge.Merge(baseline, f.Service.State, phone);
            check(merged.Conflicts.Count == 0 && merged.State.Tasks.Single().QuickColorRestore?.PreviousColor == TaskColor.Purple && merged.State.Tasks.Single().Notes == "手机备注", "独立备注同步丢失恢复信息");
            phone.Tasks.Single().Color = TaskColor.Gray; phone.Tasks.Single().ColorOverride = true;
            merged = SyncMerge.Merge(baseline, f.Service.State, phone);
            check(merged.Conflicts.Any(c => c.Field == "ColorClassification") && merged.State.Tasks.Single().Color == TaskColor.Blue && merged.State.Tasks.Single().QuickColorRestore?.PreviousColor == TaskColor.Purple, "冲突拆开了颜色与恢复信息");
            var invalid = SyncMerge.Clone(f.Service.State); invalid.Tasks.Single().QuickColorRestore = new(TaskColor.None, TaskColor.Purple, false);
            try { StateJson.Validate(invalid); throw new Exception("无效恢复数据被接受"); } catch(InvalidDataException) { }
        });
    }
}
