using DailyRecord.Core;

internal static class HeatmapScaleTests
{
    internal static void Run(Action<string, Action> test, Action<bool, string> check, Func<Fixture> fixture)
    {
        test("热力图自定义档位连续覆盖、包含上限且零项独立", () =>
        {
            var scale = new HeatmapScale(5, 10, 20);
            var points = new[] { -1, 0, 1, 5, 6, 10, 11, 20, 21, int.MaxValue };
            check(points.Select(scale.Level).SequenceEqual(new[] { 0, 0, 1, 1, 2, 2, 3, 3, 4, 4 }), "档位边界错误");
            check(scale.Bands().Select(b => b.Label).SequenceEqual(new[] { "0 项", "1–5 项", "6–10 项", "11–20 项", "21+ 项" }), "图例与档位不一致");
            check(new HeatmapScale(1, 2, 3).Bands()[2].Label == "2 项", "单数量档位没有简化");
        });
        test("热力图非法范围拒绝保存及导入，旧备份保留原有颜色范围", () =>
        {
            using var f = fixture(); var before = StateJson.Serialize(f.Service.State); var revision = f.Service.Revision;
            foreach (var scale in new[] { new HeatmapScale(0, 3, 6), new(5, 5, 20), new(5, 4, 20), new(1, 3, 10000) })
            {
                try { f.Service.UpdateHeatmapScale(scale); throw new Exception("接受非法范围"); } catch (ArgumentException) { }
                check(before == StateJson.Serialize(f.Service.State) && revision == f.Service.Revision, "非法范围改动了状态");
                var invalid = new AppState(); invalid.Settings.HeatmapLightMax = scale.LightMax;
                invalid.Settings.HeatmapMediumMax = scale.MediumMax; invalid.Settings.HeatmapDarkMax = scale.DarkMax;
                try { StateJson.Deserialize(StateJson.Serialize(invalid)); throw new Exception("导入接受非法范围"); } catch (InvalidDataException) { }
            }
            var old = StateJson.Deserialize("{\"SchemaVersion\":2,\"Settings\":{\"NormalDays\":0,\"ImportantDays\":3,\"VeryImportantDays\":7}}");
            check(old.Settings.CompletionHeatmapScale() == new HeatmapScale(), "旧备份颜色变化");
        });
        test("热力图范围持久保存、备份往返并与其他账号隔离", () =>
        {
            using var f = fixture(); using var other = fixture();
            var custom = new HeatmapScale(5, 10, 20); f.Service.UpdateHeatmapScale(custom);
            f.Service.UpdateSettings(0, 4, 9);
            check(f.Store.Load().Settings.CompletionHeatmapScale() == custom, "重新加载丢失范围");
            var backup = Path.Combine(f.Store.DataDirectory, "heatmap-backup.json"); f.Service.ExportBackup(backup);
            check(SqliteStore.ReadBackupData(backup).State.Settings.CompletionHeatmapScale() == custom, "备份丢失范围");
            f.Service.UpdateHeatmapScale(new()); f.Service.RestoreBackup(backup);
            check(f.Service.State.Settings.CompletionHeatmapScale() == custom, "恢复备份丢失范围");
            check(other.Service.State.Settings.CompletionHeatmapScale() == new HeatmapScale(), "其他账号受到影响");
        });
    }
}
