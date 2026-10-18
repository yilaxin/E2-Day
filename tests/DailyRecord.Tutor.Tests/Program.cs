using System.Text.Json;
using DailyRecord.Core;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".build/tutor-tests"); Directory.CreateDirectory(root);
var passed = 0; var failed = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); }
void Reject(Action action) { try { action(); } catch (Exception ex) when (ex is InvalidDataException or ArgumentException or InvalidOperationException) { return; } throw new Exception("应拒绝无效操作"); }
void Test(string name, Action<TutorService, TutorStore, TutorStudent> body)
{
    try
    {
        using var store = new TutorStore(Path.Combine(root, Guid.NewGuid().ToString("N"))); var service = new TutorService(store);
        var student = new TutorStudent { Name = "示例学生", Subject = "数学", HourlyRate = 150 }; service.SaveStudent(student);
        body(service, store, student); Console.WriteLine("PASS " + name); passed++;
    }
    catch (Exception ex) { Console.WriteLine("FAIL " + name + ": " + ex); failed++; }
}
TutorLesson Lesson(TutorStudent student, DateOnly? date = null) => new() { StudentId = student.Id, Subject = student.Subject, HourlyRate = student.HourlyRate, Date = date ?? new(2026, 10, 15), Minutes = 90, Status = LessonStatus.Taught };
Test("同日多学生、分钟与金额四舍五入", (s, store, a) =>
{
    var b = new TutorStudent { Name = "学生乙", Subject = "英语", HourlyRate = 160 }; s.SaveStudent(b); s.SaveLesson(Lesson(a)); s.SaveLesson(Lesson(b));
    var stats = s.Statistics(new(2026, 10, 15), new(2026, 10, 15)); Check(stats.Count == 2 && stats.Minutes == 180 && stats.Income == 465, "每日汇总错误");
    var odd = Lesson(a); odd.Minutes = 1; odd.HourlyRate = 100; s.SaveLesson(odd); Check(odd.Income == 1.67m, "金额精度错误");
});
Test("补录按授课日归属、按学生筛选", (s, store, a) => { s.SaveLesson(Lesson(a, new(2026, 9, 30))); Check(s.Statistics(new(2026, 9, 1), new(2026, 9, 30), a.Id).Income == 225 && s.Statistics(new(2026, 10, 1), new(2026, 10, 31)).Count == 0, "收入日期错误"); });
Test("时薪快照与重复保存不重复计费", (s, store, a) => { var l = Lesson(a); s.SaveLesson(l); a.HourlyRate = 180; s.SaveStudent(a); s.SaveLesson(l); Check(s.State.Lessons.Single().Income == 225, "历史时薪被覆盖"); });
Test("取消、撤销授课与恢复统计", (s, store, a) => { var l = Lesson(a); s.SaveLesson(l); l.Status = LessonStatus.Cancelled; s.SaveLesson(l); Check(s.Statistics(l.Date, l.Date).Count == 0, "取消仍计费"); l.Status = LessonStatus.Planned; s.SaveLesson(l); Check(s.Statistics(l.Date, l.Date).Income == 0, "待上课仍计费"); l.Status = LessonStatus.Taught; s.SaveLesson(l); Check(s.Statistics(l.Date, l.Date).Income == 225, "恢复错误"); });
Test("每周预测稳定、无结束日期与单次完成", (s, store, a) => { var l = Lesson(a); l.Status = LessonStatus.Planned; s.AddWeekly(l); var forecast = s.Lessons(l.Date, l.Date.AddDays(28)); Check(forecast.Count == 5 && s.Statistics(l.Date, l.Date.AddDays(28)).Count == 0, "重复生成或误计费"); var second = forecast[1]; second.Status = LessonStatus.Taught; s.SaveLesson(second); Check(s.Lessons(l.Date, l.Date.AddDays(28)).Count == 5 && s.Statistics(l.Date, l.Date.AddDays(28)).Count == 1, "完成产生重复"); Check(s.Lessons(new(2027, 10, 1), new(2027, 10, 31)).Count > 0, "后续年份排课缺失"); });
Test("重复课程改期保留实例身份", (s, store, a) => { var l = Lesson(a); l.Status = LessonStatus.Planned; s.AddWeekly(l); var second = s.Lessons(l.Date, l.Date.AddDays(14))[1]; var original = second.Date; second.Date = original.AddDays(1); s.SaveLesson(second); Check(s.Lessons(original, original).Count == 0 && s.Lessons(second.Date, second.Date).Single().Id == second.Id, "改期产生原日残留"); });
Test("修改本次及以后保留已授课快照", (s, store, a) => { var l = Lesson(a); l.Status = LessonStatus.Planned; s.AddWeekly(l); var lessons = s.Lessons(l.Date, l.Date.AddDays(28)); var done = lessons[2]; done.Status = LessonStatus.Taught; s.SaveLesson(done); var changed = TutorRules.Copy(lessons[1]); changed.HourlyRate = 200; changed.Minutes = 120; s.ChangeFuture(lessons[1], changed); var result = s.Lessons(l.Date, l.Date.AddDays(28)); Check(result.Count == 5 && result.Single(x => x.Id == done.Id).Income == 225 && result.Last().HourlyRate == 200, "后续修改丢失历史或重复"); });
Test("停止后续课程保留已授和已取消", (s, store, a) => { var l = Lesson(a); l.Status = LessonStatus.Planned; s.AddWeekly(l); var lessons = s.Lessons(l.Date, l.Date.AddDays(28)); var done = lessons[2]; done.Status = LessonStatus.Taught; s.SaveLesson(done); var cancelled = lessons[3]; cancelled.Status = LessonStatus.Cancelled; s.SaveLesson(cancelled); s.ChangeFuture(lessons[1], null); Check(s.Lessons(l.Date, l.Date.AddDays(28)).Count == 3, "停止后续未保留记录"); });
Test("无效时长、时薪、关联学生和日期不改变数据库", (s, store, a) => { foreach (var bad in new[] { 0, -1, 1441 }) { var l = Lesson(a); l.Minutes = bad; Reject(() => s.SaveLesson(l)); } var negative = Lesson(a); negative.HourlyRate = -1; Reject(() => s.SaveLesson(negative)); var orphan = Lesson(a); orphan.StudentId = "missing"; Reject(() => s.SaveLesson(orphan)); Check(store.Load().Lessons.Count == 0 && s.State.Lessons.Count == 0, "失败操作改变状态"); Reject(() => s.Statistics(new(2026, 10, 20), new(2026, 10, 1))); });
Test("学生结课仍可查历史", (s, store, a) => { s.SaveLesson(Lesson(a)); a.Archived = true; s.SaveStudent(a); Check(s.Statistics(new(2026, 10, 1), new(2026, 10, 31), a.Id).Income == 225, "结课隐藏收入"); });
Test("给家长反馈排除内部备注、收入与其他学生", (s, store, a) => { var l = Lesson(a); l.Content = "分数应用题"; l.Methods = "画线段图"; l.PrivateNotes = "内部不分享"; var text = l.ParentFeedback(a.Name); Check(text.Contains("分数应用题") && text.Contains("画线段图") && !text.Contains("内部不分享") && !text.Contains("225"), "反馈内容错误"); });
Test("图文反馈备份恢复、断开源数据库", (s, store, a) =>
{
    var image = NoteImage.Create("示例.png", Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aWQ0AAAAASUVORK5CYII=")); var l = Lesson(a); l.Content = "示例反馈"; l.ImageIds.Add(image.Id); s.SaveLesson(l, [image]);
    var path = Path.Combine(store.DirectoryPath, "export.json"); store.Export(path, s.State);
    using var destination = new TutorStore(Path.Combine(store.DirectoryPath, "other")); var target = new TutorService(destination); target.Restore(path);
    Check(target.State.Lessons.Single().Content == "示例反馈" && destination.Images([image.Id]).Single().Data.SequenceEqual(image.Data), "图文恢复失败");
    Check(Directory.GetFiles(destination.BackupDirectory, "恢复前-*.json").Length == 1, "恢复前没有备份");
});
Test("丢失图片整次保存失败", (s, store, a) => { var l = Lesson(a); l.ImageIds.Add(new string('a', 64)); Reject(() => s.SaveLesson(l)); Check(s.State.Lessons.Count == 0 && store.Load().Lessons.Count == 0, "图片失败保存了课程"); });
Test("备份篡改与日常版格式拒绝，现有数据保留", (s, store, a) => { var path = Path.Combine(store.DirectoryPath, "bad.json"); File.WriteAllText(path, "{\"Format\":\"DailyRecordBackup\",\"Version\":1,\"Payload\":\"{}\"}"); Reject(() => s.Restore(path)); store.Export(path, s.State); var b = JsonSerializer.Deserialize<TutorBackup>(File.ReadAllText(path), StateJson.Options)!; b.Payload += " "; File.WriteAllText(path, JsonSerializer.Serialize(b)); Reject(() => s.Restore(path)); Check(s.State.Students.Single().Name == a.Name, "失败恢复改变数据"); });
Test("重启读取课程、反馈与待办", (s, store, a) => { s.SaveLesson(Lesson(a)); s.AddTodo("备课"); var id = s.State.Todos.Single().Id; s.ToggleTodo(id); using var reopened = new TutorStore(store.DirectoryPath); var next = new TutorService(reopened); Check(next.State.Lessons.Count == 1 && next.State.Todos.Single().Done, "重启丢失数据"); });
Test("自动备份保留30份", (s, store, a) => { for (var i = 0; i < 35; i++) store.DailyBackup(s.State, new DateOnly(2025, 1, 1).AddDays(i)); Check(Directory.GetFiles(store.BackupDirectory, "????-??-??.json").Length == 30, "备份保留数错误"); });
Test("日常版与家教版同目录也使用不同数据文件", (s, store, a) => { using var regular = new SqliteStore(store.DirectoryPath); Check(regular.Load().Tasks.Count == 0 && s.State.Students.Count == 1, "隔离错误"); });
Test("学生学校地点和标签重启、备份与副本隔离", (s, store, a) =>
{
    a.School = "向阳中学（示例）"; a.TeachingLocation = "线上（示例）"; a.LearningLevel = "中等";
    a.LearningPace = TutorStudentProfile.Paces[0]; a.LearningFocus = ["基础巩固", "查漏补缺"]; s.SaveStudent(a);
    a.LearningFocus.Clear();
    using var reopened = new TutorStore(store.DirectoryPath); var saved = reopened.Load().Students.Single();
    Check(saved.School.Contains("向阳") && saved.TeachingLocation.Contains("线上") && saved.LearningFocus.Count == 2 && s.State.Students.Single().LearningFocus.Count == 2, "资料重启丢失或引用未隔离");
    var path = Path.Combine(store.DirectoryPath, "profile.json"); store.Export(path, s.State);
    using var targetStore = new TutorStore(Path.Combine(store.DirectoryPath, "profile-target")); var target = new TutorService(targetStore); target.Restore(path);
    Check(JsonSerializer.Serialize(target.State.Students.Single()) == JsonSerializer.Serialize(saved), "新资料备份恢复不一致");
});
Test("旧版数据库及旧备份缺少新字段仍可打开", (s, store, a) =>
{
    a.Notes = "旧版长期备注"; s.SaveStudent(a);
    var legacy = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(s.State))!;
    foreach (var name in new[] { "School", "TeachingLocation", "LearningLevel", "LearningPace", "LearningFocus" })
        legacy["Students"]![0]!.AsObject().Remove(name);
    var payload = legacy.ToJsonString();
    using (var db = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + Path.Combine(store.DirectoryPath, "tutor.db")))
    {
        db.Open(); using var command = db.CreateCommand(); command.CommandText = "UPDATE tutor_state SET payload=$payload WHERE id=1";
        command.Parameters.AddWithValue("$payload", payload); command.ExecuteNonQuery();
    }
    var next = new TutorService(store); var old = next.State.Students.Single();
    Check(old.School == "" && old.TeachingLocation == "" && old.LearningLevel == "" && old.LearningPace == "" && old.LearningFocus.Count == 0 && old.Notes == a.Notes, "旧库缺省值或备注有误");
    var backup = new TutorBackup { Payload = payload };
    backup.Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload + "\n" + JsonSerializer.Serialize(backup.Images, StateJson.Options))));
    var path = Path.Combine(store.DirectoryPath, "legacy.json"); File.WriteAllText(path, JsonSerializer.Serialize(backup)); next.Restore(path);
    Check(next.State.Students.Single().Notes == "旧版长期备注" && next.State.Students.Single().LearningFocus.Count == 0, "旧备份恢复失败");
});
Test("无效学校地点或标签不改写学生资料", (s, store, a) =>
{
    var before = JsonSerializer.Serialize(s.State);
    var changes = new Action<TutorStudent>[] { x => x.School = new string('学', 201), x => x.TeachingLocation = new string('地', 201), x => x.School = null!, x => x.LearningLevel = "不存在", x => x.LearningPace = "不存在", x => x.LearningFocus = ["不存在"], x => x.LearningFocus = ["基础巩固", "基础巩固"], x => x.LearningFocus = null!, x => x.LearningFocus = [null!] };
    foreach (var change in changes) { var invalid = TutorRules.Copy(a); change(invalid); Reject(() => s.SaveStudent(invalid)); }
    Check(JsonSerializer.Serialize(s.State) == before && JsonSerializer.Serialize(store.Load()) == before, "无效资料污染了内存或数据库");
    a.School = new string('学', 200); a.TeachingLocation = new string('地', 200); s.SaveStudent(a);
    Check(store.Load().Students.Single().School.Length == 200, "有效边界值被拒绝");
});
Test("清空学习选项保留原备注和历史课程", (s, store, a) =>
{
    s.SaveLesson(Lesson(a)); a.Notes = "保留原来的教学观察"; a.School = "示例学校"; a.LearningFocus = ["拔高拓展"]; a.LearningLevel = "优秀"; s.SaveStudent(a);
    a.School = ""; a.TeachingLocation = ""; a.LearningFocus = []; a.LearningLevel = ""; a.LearningPace = ""; s.SaveStudent(a);
    var saved = store.Load().Students.Single(); Check(saved.Notes == a.Notes && saved.School == "" && saved.LearningFocus.Count == 0 && s.Statistics(new(2026, 10, 15), new(2026, 10, 15)).Income == 225, "清空选项误删备注或改写历史收入");
});
Test("按学校地点或学习标签查找学生", (s, store, a) =>
{
    a.School = "向阳中学"; a.TeachingLocation = "Online Classroom"; a.LearningLevel = "中等"; a.LearningPace = TutorStudentProfile.Paces[0]; a.LearningFocus = ["拔高拓展"];
    Check(new[] { "向阳", "online", "中等", "较慢", "拔高" }.All(q => TutorStudentProfile.Matches(a, q)) && !TutorStudentProfile.Matches(a, "无关搜索"), "资料搜索未覆盖新字段");
});
Test("家长反馈不附带学校地点与内部学习标签", (s, store, a) =>
{
    a.School = "内部学校标记"; a.TeachingLocation = "内部地址标记"; a.LearningLevel = "需巩固"; a.LearningFocus = ["习惯培养"]; s.SaveStudent(a);
    var lesson = Lesson(a); lesson.Content = "本节练习内容"; var feedback = lesson.ParentFeedback(s.State.Students.Single().Name);
    Check(feedback.Contains(lesson.Content) && !new[] { a.School, a.TeachingLocation, a.LearningLevel, a.LearningFocus[0] }.Any(feedback.Contains), "复制反馈带入了内部学生资料");
});
Console.WriteLine($"Tutor core: {passed} passed, {failed} failed"); return failed == 0 ? 0 : 1;

