using System.Globalization;
using System.Text.Json;
using DailyRecord.Core;

var rootArgument = Array.IndexOf(args, "--output");
var root = rootArgument >= 0 && rootArgument + 1 < args.Length ? Path.GetFullPath(args[rootArgument + 1]) : Path.Combine(Environment.CurrentDirectory, ".build", "tests");
Directory.CreateDirectory(root);
var runRoot = Path.Combine(root, Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(runRoot);
var passed = 0;
var failed = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
void Equal<T>(T expected, T actual, string message) { Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected {expected}, actual {actual}"); }
void Test(string name, Action body)
{
    try { body(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + " — " + ex.Message); }
}
Fixture NewFixture(DateOnly? date = null) => new(Path.Combine(runRoot, Guid.NewGuid().ToString("N")), date ?? new DateOnly(2026, 10, 7));

Test("提前提醒边界、跨天查看和同一任务身份", () =>
{
    using var f = NewFixture(new(2026, 10, 12));
    var ev = f.Service.AddEvent("交材料", "附件齐全", new(2026, 10, 20), EventImportance.VeryImportant, RepeatRule.None);
    var task = f.Service.State.Tasks.Single(); var id = task.Id;
    Equal(0, f.Service.VisibleOpen(f.Clock.Today).Count(), "13日之前不可提前出现");
    f.Clock.Today = new(2026, 10, 13); f.Service.RefreshDay();
    Equal(1, f.Service.VisibleOpen(f.Clock.Today).Count(), "13日应出现一次");
    f.Service.ToggleViewed(task);
    Check(f.Service.IsViewed(task, f.Clock.Today), "当天查看记录缺失");
    Equal(TaskState.Open, task.State, "查看不能变成完成");
    Equal(0, f.Service.State.Tasks.Count(t => t.State == TaskState.Completed), "查看不能增加完成数");
    f.Clock.Today = new(2026, 10, 14); f.Service.RefreshDay();
    Check(!f.Service.IsViewed(task, f.Clock.Today), "次日应该重新提醒");
    Equal(id, f.Service.VisibleOpen(f.Clock.Today).Single().Id, "不能复制每日提醒任务");
    Equal(1, f.Service.State.Tasks.Count, "跨天生成了重复任务");
});
Test("提前完成停止提醒，实际完成日与事件日分别保留", () =>
{
    using var f = NewFixture(new(2026, 10, 13));
    f.Service.AddEvent("交材料", "", new(2026, 10, 20), EventImportance.VeryImportant, RepeatRule.None);
    var task = f.Service.State.Tasks.Single();
    f.Clock.Today = new(2026, 10, 16); f.Service.ToggleCompleted(task);
    Equal(new DateOnly(2026, 10, 16), task.CompletedOn!.Value, "完成日期错误");
    f.Clock.Today = new(2026, 10, 20); f.Service.RefreshDay();
    Equal(0, f.Service.VisibleOpen(f.Clock.Today).Count(), "完成后还在提醒");
    Check(f.Service.ForDate(new(2026, 10, 16)).Any(t => t.Id == task.Id), "完成日不能回看任务");
    Check(f.Service.ForDate(new(2026, 10, 20)).Any(t => t.Id == task.Id), "事件日记录丢失");
});
Test("撤销完成与反复完成不重复统计", () =>
{
    using var f = NewFixture(); var task = f.Service.AddTask("阅读", "", f.Clock.Today);
    f.Service.ToggleCompleted(task); f.Service.ToggleCompleted(task); f.Service.ToggleCompleted(task);
    Equal(1, f.Service.State.Activities.Count(a => a.Kind == ActivityKind.Completed && !a.Reverted), "重复统计完成");
    f.Service.ToggleCompleted(task);
    Equal(TaskState.Open, task.State, "撤销后未恢复");
    Equal(0, f.Service.State.Activities.Count(a => a.Kind == ActivityKind.Completed && !a.Reverted), "撤销后仍统计完成");
});
Test("手动任务跨天延续并保留原定日期", () =>
{
    using var f = NewFixture(new(2026, 10, 5)); var task = f.Service.AddTask("整理笔记", "", f.Clock.Today);
    f.Clock.Today = new(2026, 10, 7); f.Service.RefreshDay();
    Equal(task.Id, f.Service.VisibleOpen(f.Clock.Today).Single().Id, "跨天任务身份改变");
    Equal(new DateOnly(2026, 10, 5), task.ScheduledDate, "跨天覆盖原定日期");
    f.Service.ToggleCompleted(task);
    Equal(new DateOnly(2026, 10, 7), task.CompletedOn!.Value, "实际完成日期错误");
    Check(f.Service.ForDate(new(2026, 10, 5)).Any(t => t.Id == task.Id), "原定日不可回看");
});
Test("重要标记独立于事件档位，排序、跨天与重启后保留", () =>
{
    var path = Path.Combine(runRoot, "importance"); var clock = new FakeClock(new(2026, 10, 7)); string id;
    using (var store = new SqliteStore(path))
    {
        var service = new TaskService(store, clock);
        service.AddTask("普通任务", "", clock.Today);
        var ev = service.AddEvent("将来的事件", "", new(2026, 10, 10), EventImportance.Important, RepeatRule.None);
        var task = service.State.Tasks.Single(t => t.EventId == ev.Id); id = task.Id;
        var before = service.ReminderStart(task); service.ToggleImportant(task);
        Equal(before, service.ReminderStart(task), "重要标记改变了提醒日期");
        Equal(EventImportance.Important, task.EventImportance, "重要标记改变了事件档位");
        Equal(id, service.VisibleOpen(clock.Today).First().Id, "重要任务未优先排列");
        clock.Today = clock.Today.AddDays(1); service.RefreshDay(); Check(task.IsImportant, "跨天丢失重要标记");
    }
    using (var store = new SqliteStore(path))
    {
        var service = new TaskService(store, clock);
        var task = service.State.Tasks.Single(t => t.Id == id); Check(task.IsImportant, "重启丢失重要标记");
        service.ToggleCompleted(task); Check(task.IsImportant, "完成后丢失重要标记");
        service.ToggleImportant(task); Check(!task.IsImportant, "不能取消重要标记");
    }
});
Test("单次事件改期同步任务并保留查看快照", () =>
{
    using var f = NewFixture(); f.Service.AddEvent("原始标题", "原始备注", new(2026, 10, 10), EventImportance.VeryImportant, RepeatRule.None);
    var task = f.Service.State.Tasks.Single(); var id = task.Id; f.Service.ToggleViewed(task);
    f.Service.EditEvent(task, "修改标题", "修改备注", new(2026, 10, 15), EventImportance.Important, RepeatRule.None, EditScope.ThisOccurrence);
    Equal(id, f.Service.State.Tasks.Single().Id, "改期生成新任务");
    Equal(new DateOnly(2026, 10, 15), task.ScheduledDate, "任务未改期");
    Equal(new DateOnly(2026, 10, 12), f.Service.ReminderStart(task), "提醒未重算");
    var activity = f.Service.State.Activities.Single(); Equal("原始标题", activity.Title, "历史标题被覆盖");
    Equal(new DateOnly(2026, 10, 10), activity.ScheduledDate, "历史日期被覆盖");
});
Test("修改已完成事件不重新打开", () =>
{
    using var f = NewFixture(); f.Service.AddEvent("原始", "", f.Clock.Today, EventImportance.Normal, RepeatRule.None);
    var task = f.Service.State.Tasks.Single(); f.Service.ToggleCompleted(task);
    f.Service.EditEvent(task, "改后", "", f.Clock.Today.AddDays(4), EventImportance.VeryImportant, RepeatRule.None, EditScope.ThisOccurrence);
    Equal(TaskState.Completed, task.State, "修改重新打开已完成任务"); Equal("原始", task.Title, "完成记录的标题改变");
});
Test("每月31日采用月底且不漂移", () =>
{
    var ev = new EventDefinition { Title = "月底", StartDate = new(2026, 1, 31), Repeat = RepeatRule.Monthly };
    var dates = Recurrence.Between(ev, new(2026, 1, 1), new(2026, 4, 30)).Select(d => d.ToString("yyyy-MM-dd"));
    Equal("2026-01-31,2026-02-28,2026-03-31,2026-04-30", string.Join(',', dates), "月份边界错误");
});
Test("每年闰日平年夹到28日，闰年回到29日", () =>
{
    var ev = new EventDefinition { Title = "闰日", StartDate = new(2024, 2, 29), Repeat = RepeatRule.Yearly };
    var dates = Recurrence.Between(ev, new(2024, 1, 1), new(2028, 12, 31)).Select(d => d.ToString("yyyy-MM-dd"));
    Equal("2024-02-29,2025-02-28,2026-02-28,2027-02-28,2028-02-29", string.Join(',', dates), "闰日规则错误");
});
Test("农历2026春节与2023闰二月不额外重复", () =>
{
    Equal(new DateOnly(2026, 2, 17), LunarDates.ToSolar(2026, 1, 1)!.Value, "2026春节日期错误");
    var leap = LunarDates.FromSolar(new(2023, 3, 22))!;
    Check(leap.IsLeapMonth && leap.Month == 2 && leap.Day == 1, "闰二月识别错误");
    var ev = new EventDefinition { Title = "农历生日", StartDate = new(2023, 2, 20), Repeat = RepeatRule.LunarYearly, LunarMonth = 2, LunarDay = 1 };
    var dates = Recurrence.Between(ev, new(2023, 1, 1), new(2023, 12, 31)).ToList();
    Equal(1, dates.Count, "闰月生成了额外实例"); Equal(new DateOnly(2023, 2, 20), dates.Single(), "普通二月日期错误");
});
Test("重复事件旧任务逾期与新任务分别存在", () =>
{
    using var f = NewFixture(new(2026, 10, 7)); f.Service.AddEvent("周整理", "", f.Clock.Today, EventImportance.Normal, RepeatRule.Weekly);
    var first = f.Service.State.Tasks.Single(); f.Clock.Today = new(2026, 10, 14); f.Service.RefreshDay();
    Equal(2, f.Service.VisibleOpen(f.Clock.Today).Count(), "重复事件没有分别保留");
    var second = f.Service.State.Tasks.Single(t => t.Id != first.Id); f.Service.ToggleCompleted(second);
    Equal(TaskState.Open, first.State, "完成下一期影响上一期");
});
Test("重复事件仅本次修改不影响下次", () =>
{
    using var f = NewFixture(); f.Service.AddEvent("周整理", "", f.Clock.Today, EventImportance.Normal, RepeatRule.Weekly);
    var first = f.Service.State.Tasks.Single();
    f.Service.EditEvent(first, "仅本次", "", f.Clock.Today.AddDays(1), EventImportance.Important, RepeatRule.Weekly, EditScope.ThisOccurrence);
    f.Clock.Today = new(2026, 10, 14); f.Service.RefreshDay();
    var second = f.Service.State.Tasks.Single(t => t.Id != first.Id);
    Equal("周整理", second.Title, "下次标题被修改"); Equal(new DateOnly(2026, 10, 14), second.ScheduledDate, "下次日期改变");
});
Test("农历重复的单次改期使用选定公历日期", () =>
{
    using var f = NewFixture(new(2023, 2, 20));
    f.Service.AddEvent("农历生日", "", f.Clock.Today, EventImportance.Normal, RepeatRule.LunarYearly);
    var task = f.Service.State.Tasks.Single();
    f.Service.EditEvent(task, "本次改期", "", new(2023, 3, 22), EventImportance.Normal, RepeatRule.LunarYearly, EditScope.ThisOccurrence);
    Equal(new DateOnly(2023, 3, 22), task.ScheduledDate, "单次公历改期被农历普通月规则覆盖");
    f.Clock.Today = new(2024, 12, 31); f.Service.RefreshDay();
    Check(f.Service.State.Tasks.Any(t => t.Id != task.Id && t.Title == "农历生日"), "单次改期影响次年规则");
});
Test("重复事件本次及以后拆分保留过去记录", () =>
{
    using var f = NewFixture(); var ev = f.Service.AddEvent("周整理", "", f.Clock.Today, EventImportance.Normal, RepeatRule.Weekly);
    var first = f.Service.State.Tasks.Single(); f.Service.ToggleCompleted(first);
    f.Clock.Today = new(2026, 10, 14); f.Service.RefreshDay(); var second = f.Service.State.Tasks.Single(t => t.Id != first.Id);
    f.Service.ToggleViewed(second);
    f.Service.EditEvent(second, "新安排", "", new(2026, 10, 15), EventImportance.Important, RepeatRule.Weekly, EditScope.ThisAndFuture);
    Equal(TaskState.Completed, first.State, "过去完成状态被覆盖"); Equal("周整理", first.Title, "过去标题被覆盖");
    Equal("周整理", f.Service.State.Activities.Single(a => a.TaskId == second.Id && a.Kind == ActivityKind.Viewed).Title, "查看快照改变");
    f.Clock.Today = new(2026, 10, 22); f.Service.RefreshDay();
    Check(f.Service.State.Tasks.Any(t => t.Title == "新安排" && t.ScheduledDate == new DateOnly(2026, 10, 22)), "未来规则未改");
    Check(!f.Service.State.Tasks.Any(t => t.EventId == ev.Id && t.State == TaskState.Open && t.ScheduledDate >= new DateOnly(2026, 10, 14)), "旧未来仍提醒");
});
Test("取消单次重复实例、停止后续与恢复本次", () =>
{
    using var f = NewFixture(); f.Service.AddEvent("重复", "", f.Clock.Today, EventImportance.Normal, RepeatRule.Weekly);
    var first = f.Service.State.Tasks.Single(); f.Service.CancelEvent(first, EditScope.ThisOccurrence);
    f.Clock.Today = new(2026, 10, 14); f.Service.RefreshDay(); Equal(1, f.Service.VisibleOpen(f.Clock.Today).Count(), "取消本次停止了下次");
    var second = f.Service.VisibleOpen(f.Clock.Today).Single(); f.Service.CancelEvent(second, EditScope.ThisAndFuture);
    f.Clock.Today = new(2026, 10, 28); f.Service.RefreshDay(); Equal(0, f.Service.VisibleOpen(f.Clock.Today).Count(), "后续仍生成提醒");
    f.Service.RestoreTask(second); Equal(1, f.Service.VisibleOpen(f.Clock.Today).Count(), "不能恢复某一次");
    Equal(0, f.Service.State.Tasks.Count(t => t.State == TaskState.Completed), "取消被计为完成");
});
Test("提醒天数修改重算，越界设置不写入", () =>
{
    using var f = NewFixture(); f.Service.AddEvent("事件", "", new(2026, 10, 12), EventImportance.Important, RepeatRule.None);
    Equal(0, f.Service.VisibleOpen(f.Clock.Today).Count(), "默认提醒提前太早");
    f.Service.UpdateSettings(0, 6, 7); Equal(1, f.Service.VisibleOpen(f.Clock.Today).Count(), "天数修改未生效");
    try { f.Service.UpdateSettings(-1, 3, 7); throw new Exception("未拒绝非法设置"); } catch (ArgumentException) { }
    Equal(6, f.Service.State.Settings.ImportantDays, "非法设置破坏原设置");
});
Test("备份恢复还原星标、状态和历史并保留恢复前备份", () =>
{
    using var f = NewFixture(); var task = f.Service.AddTask("重要任务", "备注", f.Clock.Today, true); f.Service.ToggleCompleted(task);
    f.Service.AddEvent("事件", "", f.Clock.Today.AddDays(1), EventImportance.Important, RepeatRule.Monthly);
    f.Service.ToggleViewed(f.Service.State.Tasks.First(t => t.IsEvent));
    var path = Path.Combine(f.Store.DataDirectory, "export.json"); SqliteStore.WriteBackup(path, f.Service.State);
    f.Service.AddTask("后加的任务", "", f.Clock.Today);
    f.Service.RestoreBackup(path);
    Equal(2, f.Service.State.Tasks.Count, "恢复任务数量错误");
    var restored = f.Service.State.Tasks.Single(t => t.Id == task.Id); Check(restored.IsImportant, "备份丢失星标");
    Equal(TaskState.Completed, restored.State, "备份丢失完成状态");
    Equal(2, f.Service.State.Activities.Count(a => !a.Reverted), "历史丢失");
    Check(Directory.GetFiles(f.Store.BackupDirectory, "恢复前-*.json").Length == 1, "没有恢复前备份");
});
Test("损坏备份拒绝恢复且现有数据不变", () =>
{
    using var f = NewFixture(); f.Service.AddTask("安全保留", "", f.Clock.Today);
    var path = Path.Combine(f.Store.DataDirectory, "bad.json");
    var envelope = JsonSerializer.Deserialize<BackupEnvelope>(SqliteStore.ExportText(f.Service.State), StateJson.Options)!;
    var altered = StateJson.Deserialize(envelope.Payload);
    altered.Tasks.Single().Title = "损坏内容";
    envelope.Payload = StateJson.Serialize(altered);
    File.WriteAllText(path, JsonSerializer.Serialize(envelope, StateJson.Options));
    try { f.Service.RestoreBackup(path); throw new Exception("损坏备份被接受"); } catch (InvalidDataException) { }
    Equal("安全保留", f.Service.State.Tasks.Single().Title, "现有内存数据被破坏");
    Equal("安全保留", f.Store.Load().Tasks.Single().Title, "现有数据库被破坏");
});
Test("有效校验但缺少日期的备份在替换数据库前拒绝", () =>
{
    using var f = NewFixture(); f.Service.AddTask("当前记录", "", f.Clock.Today);
    var malformed = StateJson.Deserialize(StateJson.Serialize(f.Service.State));
    malformed.Tasks.Single().ScheduledDate = default;
    var payload = StateJson.Serialize(malformed);
    var envelope = new BackupEnvelope { Payload = payload, Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload))) };
    var path = Path.Combine(f.Store.DataDirectory, "malformed.json");
    File.WriteAllText(path, JsonSerializer.Serialize(envelope, StateJson.Options));
    try { f.Service.RestoreBackup(path); throw new Exception("缺失日期被接受"); } catch (InvalidDataException) { }
    Equal(new DateOnly(2026, 10, 7), f.Store.Load().Tasks.Single().ScheduledDate, "验证失败后现有数据库被替换");
});
Test("自动备份只保留30份且不删除手动恢复前备份", () =>
{
    using var f = NewFixture(); f.Store.BeforeRestoreBackup(f.Service.State);
    for (var index = 0; index < 40; index++) f.Store.DailyBackup(f.Service.State, new DateOnly(2026, 8, 1).AddDays(index));
    Equal(30, Directory.GetFiles(f.Store.BackupDirectory, "????-??-??.json").Length, "自动备份保留数量错误");
    Equal(1, Directory.GetFiles(f.Store.BackupDirectory, "恢复前-*.json").Length, "清理误删恢复前备份");
});
Test("RSS筛选日期、AI主题、原文链接和清理摘要", () =>
{
    var xml = """
        <rss><channel>
        <item><title>New AI model</title><link>https://example.test/ai</link><pubDate>Wed, 07 Oct 2026 01:00:00 GMT</pubDate><description><![CDATA[<p>Research &amp; products</p>]]></description></item>
        <item><title>New phone case</title><link>https://example.test/case</link><pubDate>Wed, 07 Oct 2026 01:00:00 GMT</pubDate><description>Accessories</description></item>
        <item><title>Old GPT announcement</title><link>https://example.test/old</link><pubDate>Tue, 01 Sep 2026 01:00:00 GMT</pubDate></item>
        <item><title>AI missing date</title><link>https://example.test/unknown</link></item>
        <item><title>AI invalid link</title><link>javascript:alert(1)</link><pubDate>Wed, 07 Oct 2026 01:00:00 GMT</pubDate></item>
        </channel></rss>
        """;
    var result = NewsService.Parse(xml, new("Test", "https://example.test/feed", "英文"), new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.FromHours(8)));
    Equal(1, result.Count, "资讯筛选错误"); Equal("Research & products", result.Single().Summary, "摘要HTML未清理");
});
Test("中国2026国庆休息与10月10日调休区分", () =>
{
    var directory = Path.Combine(Environment.CurrentDirectory, "data", "holidays");
    var service = new HolidayService(directory, Path.Combine(runRoot, "holiday-cache"));
    Check(service.HasYear(2026), "年度数据未加载");
    var off = service.ForDay(new(2026, 10, 7)); Check(off.IsOffDay && off.WorkStatus == "法定休息日", "国庆假期判定错误");
    var work = service.ForDay(new(2026, 10, 10)); Check(!work.IsOffDay && work.WorkStatus == "调休上班日", "调休上班错误");
    Check(!service.ForDay(new(2030, 10, 1)).HasAnnualData, "未知年份伪装已更新");
});
Test("全年批量汇总保持改期、取消、完成、重复和闰年语义", () =>
{
    using var f = NewFixture(new(2024, 1, 31));
    foreach (var rule in Enum.GetValues<RepeatRule>())
        f.Service.AddEvent("事件 " + rule, "", f.Clock.Today, EventImportance.Important, rule);
    var monthly = f.Service.State.Tasks.First(t => f.Service.State.Events.Single(e => e.Id == t.EventId).Repeat == RepeatRule.Monthly);
    f.Service.EditEvent(monthly, "本次改期", "", new(2024, 2, 1), EventImportance.VeryImportant, RepeatRule.Monthly, EditScope.ThisOccurrence);
    var weekly = f.Service.State.Tasks.First(t => f.Service.State.Events.Single(e => e.Id == t.EventId).Repeat == RepeatRule.Weekly);
    f.Service.CancelTask(weekly);
    var single = f.Service.State.Tasks.First(t => f.Service.State.Events.Single(e => e.Id == t.EventId).Repeat == RepeatRule.None);
    f.Service.EditEvent(single, "单次改期", "", new(2025, 3, 3), EventImportance.Normal, RepeatRule.None, EditScope.ThisOccurrence);
    f.Service.ToggleCompleted(single);
    for (var i = 0; i < 5000; i++)
        f.Service.State.Tasks.Add(new TaskItem { Title = "历史任务 " + i, ScheduledDate = new DateOnly(2023, 1, 1).AddDays(i % 1461),
            IsImportant = i % 13 == 0, State = i % 11 == 0 ? TaskState.Cancelled : TaskState.Open });
    var taskCount = f.Service.State.Tasks.Count;
    foreach (var year in new[] { 2024, 2025, 2026 })
    {
        var summary = f.Service.CalendarForYear(year);
        for (var day = new DateOnly(year, 1, 1); day.Year == year; day = day.AddDays(1))
        {
            var actual = summary.GetValueOrDefault(day, CalendarDaySummary.Empty);
            Equal(f.Service.ScheduledCount(day), actual.ScheduledCount, "全年汇总数量错误 " + day);
            Equal(actual.ScheduledCount, actual.Tasks.Count, "月历任务摘要漏项 " + day);
            Equal(string.Join('|', f.Service.EventTitles(day)), string.Join('|', actual.EventTitles), "全年汇总标题或顺序错误 " + day);
            Equal(f.Service.State.Tasks.Any(t => t.ScheduledDate == day && t.IsImportant && t.State != TaskState.Cancelled), actual.HasImportantTask, "重要标记错误");
            Equal(f.Service.State.Tasks.Count(t => t.ScheduledDate == day && t.State != TaskState.Cancelled), actual.RecordedCount, "已记录数量错误");
        }
    }
    Equal(taskCount, f.Service.State.Tasks.Count, "浏览全年不应生成任务实例");
});
Test("月历摘要包含手动任务、星标、完成状态及未来重复任务", () =>
{
    using var f = NewFixture();
    var ordinary = f.Service.AddTask("普通任务", "", f.Clock.Today);
    var important = f.Service.AddTask("重要关键词", "", f.Clock.Today, true);
    var completed = f.Service.AddTask("已完成关键词", "", f.Clock.Today, true); f.Service.ToggleCompleted(completed);
    var ev = f.Service.AddEvent("每周关键词", "", f.Clock.Today, EventImportance.Normal, RepeatRule.Weekly);
    f.Service.CancelTask(ordinary);
    var summary = f.Service.CalendarForYear(2026)[f.Clock.Today];
    Equal("重要关键词", summary.Tasks[0].Title, "重要未完成任务未排首位");
    Equal("已完成关键词", summary.Tasks[1].Title, "重要已完成任务摘要缺失");
    Equal(TaskState.Completed, summary.Tasks[1].State, "完成状态缺失");
    Check(summary.Tasks.All(t => t.Title != "普通任务"), "摘要展示已取消任务");
    var future = f.Clock.Today.AddDays(7);
    Equal("每周关键词", f.Service.CalendarForYear(2026)[future].Tasks.Single().Title, "未来重复任务缺失");
    Equal(4, f.Service.State.Tasks.Count, "只看月历不应生成未来实例");
    var futureTask = f.Service.EnsureOccurrence(ev, future); f.Service.ToggleImportant(futureTask);
    f.Service.EditEvent(futureTask, "改期关键词", "", future.AddDays(1), EventImportance.Normal, RepeatRule.Weekly, EditScope.ThisOccurrence);
    var moved = f.Service.CalendarForYear(2026);
    Check(!moved.ContainsKey(future), "改期后原日期仍有重复摘要");
    Equal("改期关键词", moved[future.AddDays(1)].Tasks.Single().Title, "改期后的标题缺失");
    Check(moved[future.AddDays(1)].Tasks.Single().IsImportant, "改期丢失重要标记");
});
Test("懒生成实例与保存失败均使日历缓存失效", () =>
{
    using var f = NewFixture();
    var ev = f.Service.AddEvent("每周", "", f.Clock.Today, EventImportance.Normal, RepeatRule.Weekly);
    var before = f.Service.Revision;
    f.Service.ForDate(f.Clock.Today.AddDays(7));
    Check(f.Service.Revision > before, "懒生成未更新版本");
    before = f.Service.Revision;
    f.Service.ForDate(f.Clock.Today.AddDays(7));
    Equal(before, f.Service.Revision, "已有实例的读取不应使缓存失效");
    f.Service.State.Tasks.Add(f.Service.State.Tasks.First());
    try { f.Service.Save(); throw new Exception("无效数据未拒绝"); } catch (InvalidDataException) { }
    Check(f.Service.Revision > before, "保存失败回滚未更新版本");
    Equal(1, f.Service.State.Tasks.Count, "保存失败未恢复数据库版本");
});
Test("重复ID验证失败不改变SQLite已保存记录", () =>
{
    using var f = NewFixture(); var task = f.Service.AddTask("保留", "", f.Clock.Today);
    f.Service.State.Tasks.Add(task);
    try { f.Service.Save(); throw new Exception("重复ID未拒绝"); } catch (InvalidDataException) { }
    Equal(1, f.Store.Load().Tasks.Count, "验证失败破坏数据库");
    Equal(1, f.Service.State.Tasks.Count, "内存未回到已保存状态");
});

Test("备注图片随任务持久化，原始文件删除不影响重启读取", () =>
{
    var directory = Path.Combine(runRoot, "image-restart"); var clock = new FakeClock(new(2026, 10, 9));
    var original = Path.Combine(directory, "original.png"); Directory.CreateDirectory(directory);
    File.WriteAllBytes(original, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aN1kAAAAASUVORK5CYII="));
    var image = NoteImage.Create("示例.png", File.ReadAllBytes(original)); string id;
    using (var store = new SqliteStore(directory))
    {
        var service = new TaskService(store, clock);
        var task = service.AddTask("带图任务", "文字备注", clock.Today, images: [image]); id = task.Id;
        service.EditTask(task, task.Title, "修改文字", clock.Today, false);
        Equal(image.Id, task.ImageIds.Single(), "只改文字应保留图片");
        Check(!StateJson.Serialize(service.State).Contains(Convert.ToBase64String(image.Data)), "任务状态不应重复存储图片字节");
    }
    File.Delete(original);
    using var reopened = new SqliteStore(directory); var loaded = new TaskService(reopened, clock);
    Equal("修改文字", loaded.State.Tasks.Single(t => t.Id == id).Notes, "重启丢失文字");
    Check(loaded.GetNoteImages(loaded.State.Tasks.Single().ImageIds).Single().Data.SequenceEqual(image.Data), "原图删除后图片丢失");
});
Test("备注图片移除、取消和完成快照相互独立", () =>
{
    using var f = NewFixture(); var image = NoteImage.Create("记录.png", Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aN1kAAAAASUVORK5CYII="));
    var task = f.Service.AddTask("记录", "原始备注", f.Clock.Today, images: [image]); f.Service.ToggleCompleted(task);
    f.Service.EditTask(task, "记录", "新的备注", f.Clock.Today, false, []);
    Equal(0, task.ImageIds.Count, "移除未生效");
    Equal(image.Id, f.Service.State.Activities.Single().ImageIds.Single(), "修改覆盖了完成快照");
    Check(f.Service.GetNoteImages(f.Service.State.Activities.Single().ImageIds).Single().Data.SequenceEqual(image.Data), "历史图片被删除");
    f.Service.ToggleCompleted(task); f.Service.EditTask(task, task.Title, task.Notes, f.Clock.Today, false, [image]);
    f.Service.CancelTask(task); f.Service.RestoreTask(task);
    Equal(image.Id, task.ImageIds.Single(), "取消/恢复丢失图片");
    var disposable = f.Service.AddTask("只用一次的图片", "", f.Clock.Today, images: [image]);
    f.Service.EditTask(disposable, disposable.Title, "", f.Clock.Today, false, []);
    Equal(image.Id, f.Service.State.Activities.First().ImageIds.Single(), "移除另一个任务影响历史图片");
});
Test("重复事件图片遵循仅本次和本次及以后范围", () =>
{
    using var f = NewFixture();
    var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aN1kAAAAASUVORK5CYII=");
    var first = NoteImage.Create("原图.png", bytes);
    var secondBytes = bytes.Concat(new byte[] { 0 }).ToArray(); var second = NoteImage.Create("新图.png", secondBytes);
    var ev = f.Service.AddEvent("每周安排", "", f.Clock.Today, EventImportance.Normal, RepeatRule.Weekly, [first]);
    var task = f.Service.State.Tasks.Single();
    f.Service.EditEvent(task, "本次备注", "", f.Clock.Today, EventImportance.Normal, RepeatRule.Weekly, EditScope.ThisOccurrence, [second]);
    Equal(second.Id, task.ImageIds.Single(), "本次未使用新图片");
    var next = f.Service.EnsureOccurrence(ev, f.Clock.Today.AddDays(7)); Equal(first.Id, next.ImageIds.Single(), "仅本次影响了下次");
    f.Service.EditEvent(next, "以后的备注", "", next.ScheduledDate, EventImportance.Normal, RepeatRule.Weekly, EditScope.ThisAndFuture, [second]);
    var replacement = f.Service.State.Events.Single(e => e.Id == next.EventId);
    var future = f.Service.EnsureOccurrence(replacement, next.ScheduledDate.AddDays(7)); Equal(second.Id, future.ImageIds.Single(), "未来实例没有继承图片");
    f.Service.ToggleCompleted(future);
    f.Service.EditEvent(future, "新标题", "完成后的备注", future.ScheduledDate, EventImportance.Normal, RepeatRule.Weekly, EditScope.ThisOccurrence, []);
    Equal(0, future.ImageIds.Count, "已完成任务不能修改备注图片");
    Equal("以后的备注", future.Title, "修改图片改变已完成标题");
    Equal(second.Id, f.Service.State.Activities.Last(a => a.Kind == ActivityKind.Completed).ImageIds.Single(), "已完成图片快照丢失");
});
Test("图片备份可在另一数据库独立恢复，并保留恢复前图片", () =>
{
    using var source = NewFixture(); using var target = NewFixture();
    var image = NoteImage.Create("参考.png", Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aN1kAAAAASUVORK5CYII="));
    var task = source.Service.AddTask("附图", "文字", source.Clock.Today, images: [image]); source.Service.ToggleCompleted(task);
    var path = Path.Combine(source.Store.DataDirectory, "with-images.json"); source.Service.ExportBackup(path);
    Equal(1, SqliteStore.ReadBackupData(path).Images.Count, "共享引用重复导出图片");
    target.Service.AddTask("当前图片", "", target.Clock.Today, images: [image]); target.Service.RestoreBackup(path);
    Check(target.Service.GetNoteImages(target.Service.State.Tasks.Single().ImageIds).Single().Data.SequenceEqual(image.Data), "异地恢复丢失图片");
    var before = Directory.GetFiles(target.Store.BackupDirectory, "恢复前-*.json").Single();
    Equal(image.Id, SqliteStore.ReadBackupData(before).Images.Single().Id, "恢复前备份丢失图片");
    var automatic = Path.Combine(target.Store.BackupDirectory, $"{target.Clock.Today:yyyy-MM-dd}.json");
    Equal(image.Id, SqliteStore.ReadBackupData(automatic).Images.Single().Id, "自动备份漏图片");
});
Test("旧版无图备份和数据库兼容迁移", () =>
{
    using var f = NewFixture(); f.Service.AddTask("旧任务", "旧备注", f.Clock.Today);
    var legacy = System.Text.Json.Nodes.JsonNode.Parse(StateJson.Serialize(f.Service.State))!; legacy["SchemaVersion"] = 1;
    foreach (var item in legacy["Tasks"]!.AsArray()) item!.AsObject().Remove("ImageIds");
    var payload = legacy.ToJsonString();
    var envelope = new BackupEnvelope { Payload = payload, Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload))) };
    var path = Path.Combine(f.Store.DataDirectory, "legacy.json"); File.WriteAllText(path, JsonSerializer.Serialize(envelope, StateJson.Options));
    f.Service.RestoreBackup(path); Equal(2, f.Service.State.SchemaVersion, "旧备份未升级"); Equal(0, f.Service.State.Tasks.Single().ImageIds.Count, "旧任务图片应为空");
    var legacyState = StateJson.Deserialize(payload); legacyState.SchemaVersion = 1; f.Store.Save(legacyState);
    Equal(2, f.Store.Load().SchemaVersion, "旧数据库无法读取"); Equal("旧备注", f.Store.Load().Tasks.Single().Notes, "迁移丢失文字");
});
Test("图片缺失和数据损坏拒绝恢复，现有记录及图片不变", () =>
{
    using var f = NewFixture(); var image = NoteImage.Create("图片.png", Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aN1kAAAAASUVORK5CYII="));
    f.Service.AddTask("保留记录", "", f.Clock.Today, images: [image]);
    var path = Path.Combine(f.Store.DataDirectory, "broken-image.json");
    var envelope = JsonSerializer.Deserialize<BackupEnvelope>(SqliteStore.ExportText(f.Service.State, [image]), StateJson.Options)!;
    envelope.Images.Clear();
    envelope.Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(envelope.Payload + "\n" + JsonSerializer.Serialize(envelope.Images, StateJson.Options))));
    File.WriteAllText(path, JsonSerializer.Serialize(envelope, StateJson.Options));
    try { f.Service.RestoreBackup(path); throw new Exception("缺图片备份未拒绝"); } catch (InvalidDataException) { }
    Equal("保留记录", f.Store.Load().Tasks.Single().Title, "失败恢复覆盖记录"); Check(f.Service.GetNoteImages([image.Id]).Single().Data.SequenceEqual(image.Data), "失败恢复破坏图片");
    envelope = JsonSerializer.Deserialize<BackupEnvelope>(SqliteStore.ExportText(f.Service.State, [image]), StateJson.Options)!;
    envelope.Images[0].Data[8] ^= 1;
    File.WriteAllText(path, JsonSerializer.Serialize(envelope, StateJson.Options));
    try { f.Service.RestoreBackup(path); throw new Exception("图片篡改未拒绝"); } catch (InvalidDataException) { }
    Equal(1, f.Store.Load().Tasks.Count, "图片校验失败破坏记录");
});
Test("无效图片引用保存回滚、数量限制及不再使用图片清理", () =>
{
    using var f = NewFixture(); var image = NoteImage.Create("图片.png", Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aN1kAAAAASUVORK5CYII="));
    var task = f.Service.AddTask("任务", "", f.Clock.Today, images: [image]);
    task.ImageIds = [new string('A', 64)];
    try { f.Service.Save(); throw new Exception("缺图片引用未拒绝"); } catch (InvalidDataException) { }
    Equal(image.Id, f.Service.State.Tasks.Single().ImageIds.Single(), "失败保存未回滚图片关联");
    try { f.Service.AddTask("无效", "", f.Clock.Today, images: Enumerable.Repeat(image, 9).ToList()); throw new Exception("数量限制未生效"); } catch (InvalidDataException) { }
    Equal(1, f.Service.State.Tasks.Count, "无效添加留下任务");
    task = f.Service.State.Tasks.Single(); f.Service.EditTask(task, task.Title, "", f.Clock.Today, false, []);
    try { f.Service.GetNoteImages([image.Id]); throw new Exception("无引用图片未清理"); } catch (InvalidDataException) { }
});

LongTermTaskTests.Run(Test, Check, () => NewFixture());

if (args.Contains("--network"))
{
    try
    {
        var result = await new NewsService().FetchAsync(DateTimeOffset.Now);
        Check(result.SuccessfulSources > 0, "没有可连接的资讯源");
        Check(result.Articles.Count <= 5, "超过5条");
        passed++; Console.WriteLine($"PASS Live RSS: {result.SuccessfulSources}/{result.TotalSources} sources, {result.Articles.Count} AI articles");
    }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL Live RSS — " + ex); }
}
Console.WriteLine($"RESULT: {passed} passed, {failed} failed");
File.WriteAllText(Path.Combine(root, "latest-result.txt"), $"{passed} passed, {failed} failed\nData: {runRoot}\n");
return failed == 0 ? 0 : 1;

sealed class FakeClock(DateOnly today) : IClock
{
    public DateOnly Today { get; set; } = today;
    public DateTimeOffset Now => new(Today.ToDateTime(new TimeOnly(9, 30)), TimeSpan.FromHours(8));
}
sealed class Fixture : IDisposable
{
    public SqliteStore Store { get; }
    public FakeClock Clock { get; }
    public TaskService Service { get; }
    public Fixture(string directory, DateOnly day) { Store = new(directory); Clock = new(day); Service = new(Store, Clock); }
    public void Dispose() => Store.Dispose();
}
