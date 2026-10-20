using System.IO;
using System.Windows;
using System.Windows.Controls;
using DailyRecord.Core;

namespace DailyRecord.App;

internal static partial class SmokeTest
{
    private static async Task VerifyAnalytics(MainWindow window, TaskService service, SqliteStore store, HolidayService holidays, PreviewClock clock, string output)
    {
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        string Value(string tag) => Descendants(window).OfType<TextBlock>().Single(t => Equals(t.Tag, tag)).Text;
        var baseline = Path.Combine(output, "analytics-baseline.json"); service.ExportBackup(baseline);
        var today = clock.Today; var presenter = window.AnalyticsDetailsPresenter;
        Window? details = null;
        try
        {
            service.State.Tasks.Clear(); service.State.Events.Clear(); service.State.Activities.Clear(); service.Save();
            window.Navigate(PageKind.Analytics); Prepare(window);
            Check(Value("analytics-total") == "0" && Value("analytics-overdue") == "0", "空账号的统计不是零。");
            Render((FrameworkElement)window.Content, 1400, 940, Path.Combine(output, "analytics-empty.png"));
            var emptyShare = window.BuildAnalyticsShareImage();
            Check(Descendants(emptyShare).OfType<TextBlock>().Single(t => Equals(t.Tag, "analytics-total")).Text == "0", "空账号分享不是零值。");
            SaveBitmap(TaskShareImage.Render(emptyShare, MainWindow.AnalyticsShareWidth), Path.Combine(output, "analytics-share-empty.png"));
            var habit = new EventDefinition { Title = "每天阅读 20 分钟", StartDate = today.AddDays(-28), Repeat = RepeatRule.Daily };
            service.State.Events.Add(habit);
            var names = new[] { "整理研究笔记", "完成学习清单", "散步与运动", "推进本周计划", "阅读并摘录", "整理文件", "完成一次练习" };
            for (var offset = -89; offset <= 0; offset++)
            {
                var day = today.AddDays(offset); var count = (offset + 91) % 8;
                for (var i = 0; i < count; i++)
                    service.State.Tasks.Add(new TaskItem { Title = names[i % names.Length], ScheduledDate = day == today ? today : day.AddDays(-1), OriginalScheduledDate = day == today ? today : day.AddDays(-1),
                        CompletedOn = day, State = TaskState.Completed, CreatedAt = clock.Now, IsImportant = i == 0 });
                if (offset >= -28 && offset % 7 != -4)
                    service.State.Tasks.Add(new TaskItem { Title = habit.Title, EventId = habit.Id, OccurrenceDate = day, ScheduledDate = day, OriginalScheduledDate = day,
                        CompletedOn = day, State = TaskState.Completed, CreatedAt = clock.Now });
            }
            foreach (var age in new[] { 0, 1, 3, 4, 7, 8 })
                service.State.Tasks.Add(new TaskItem { Title = age == 0 ? "完成今天的整理" : $"回看并重新安排 {age} 天前的事项", ScheduledDate = today.AddDays(-age),
                    OriginalScheduledDate = today.AddDays(-age), IsImportant = age == 8, CreatedAt = clock.Now });
            service.State.Tasks.Add(new TaskItem { Title = "未来提醒不计入积压", ScheduledDate = today.AddDays(4), OriginalScheduledDate = today.AddDays(4), CreatedAt = clock.Now });
            service.Save();
            VerifyHeatmapRanges(window, service, store, output);
            var before = StateJson.Serialize(service.State); var revision = service.Revision;
            window.Navigate(PageKind.Analytics); Prepare(window);
            Check(before == StateJson.Serialize(service.State) && revision == service.Revision, "进入统计页面改动了记录。");
            var expected = service.State.Tasks.Count(t => t.State == TaskState.Completed && t.CompletedOn >= today.AddDays(-29) && t.CompletedOn <= today);
            Check(Value("analytics-total") == expected.ToString() && Value("analytics-overdue") == "5", "统计数字卡片与记录不符。");
            Check(Descendants(window).OfType<Button>().Count(b => b.Tag is string tag && tag.StartsWith("analytics-heat:")) == 365, "热力图没有完整显示全年。");
            Check(!FindButton(window, "analytics-heat:" + today.AddDays(1).ToString("yyyy-MM-dd")).IsEnabled, "未来热力格可误当作已有记录。");
            Render((FrameworkElement)window.Content, 1400, 940, Path.Combine(output, "analytics.png"));
            Render((FrameworkElement)window.Content, 1400, 1500, Path.Combine(output, "analytics-full.png"));
            Prepare(window);
            await VerifyAnalyticsShare(window, service, clock, output);
            window.AnalyticsDetailsPresenter = dialog => details = dialog;
            Click(window, "analytics-heat:" + today.ToString("yyyy-MM-dd"));
            Check(details is not null && Descendants(details).OfType<Button>().Any(b => b.Tag is string tag && tag.StartsWith("complete:")), "热力图没有打开对应的完成事项。");
            var completed = service.State.Tasks.First(t => t.State == TaskState.Completed && t.CompletedOn == today);
            Click(details!, "complete:" + completed.Id);
            Check(Value("analytics-total") == (expected - 1).ToString(), "撤销完成后统计没有更新。"); details!.Close(); details = null;
            Click(window, "analytics-backlog:1-3");
            Check(details is not null && Descendants(details).OfType<Button>().Count(b => b.Tag is string tag && tag.StartsWith("complete:")) == 2, "积压分组详情筛选错误。");
            var overdue = service.State.Tasks.Single(t => t.State == TaskState.Open && t.ScheduledDate == today.AddDays(-1));
            Click(details!, "complete:" + overdue.Id);
            Check(Value("analytics-overdue") == "4" && !Descendants(details!).OfType<Button>().Any(b => Equals(b.Tag, "complete:" + overdue.Id)), "详情中完成后未从积压移除。");
            Render((FrameworkElement)details!.Content, 770, 560, Path.Combine(output, "analytics-detail.png")); details.Close(); details = null;
            Click(window, "analytics-range:0");
            Check(Descendants(window).OfType<Button>().Count(b => b.Tag is string tag && tag.StartsWith("analytics-trend:")) == 7, "近7天筛选没有更新图表。");
            Click(window, "analytics-trend:" + today.ToString("yyyy-MM-dd"));
            Check(details is not null && Descendants(details).OfType<Button>().Count(b => b.Tag is string tag && tag.StartsWith("complete:")) ==
                service.State.Tasks.Count(t => t.State == TaskState.Completed && t.CompletedOn == today), "趋势柱形没有筛选实际完成日。");
            details!.Close(); details = null;
            Click(window, "analytics-range:3");
            Check(FindButton(window, $"analytics-trend:{today.Year}-01-01") is not null, "今年筛选没有按月汇总。");
            Click(window, $"analytics-trend:{today.Year}-01-01");
            Check(details is not null && Descendants(details).OfType<TextBlock>().Any(t => t.Text == "这里暂时没有对应的事项。"), "空月份详情没有空状态。");
            details!.Close(); details = null;
            Click(window, "analytics-year-next"); Click(window, "analytics-year-next");
            Check(FindButton(window, "analytics-heat:2028-02-29") is not null, "闰年热力图遗漏2月29日。");
            Click(window, "analytics-year-previous"); Click(window, "analytics-year-previous");
            Click(window, "analytics-habit:" + today.ToString("yyyy-MM-dd"));
            Check(details is not null && details.Title.Contains(habit.Title), "长期打卡未打开对应事项。");
            var habitToday = service.State.Tasks.Single(t => t.EventId == habit.Id && t.OccurrenceDate == today);
            Click(details!, "complete:" + habitToday.Id);
            Check(habitToday.State == TaskState.Open && FindButton(window, "analytics-habit:" + today.ToString("yyyy-MM-dd")).IsEnabled, "撤销打卡后无法在今天格重新完成。");
            details!.Close(); details = null;
            Click(window, "analytics-habit:" + today.ToString("yyyy-MM-dd"));
            Click(details!, "complete:" + habitToday.Id);
            Check(habitToday.State == TaskState.Completed, "今天的打卡格无法完成长期任务。"); details!.Close(); details = null;
            Click(window, "analytics-habit-next"); Click(window, "analytics-habit-previous");
            Check(Descendants(window).OfType<ComboBox>().Any(c => Equals(c.Tag, "analytics-habit-picker")), "打卡切换月份后丢失任务选择器。");
            using var emptyStore = new SqliteStore(Path.Combine(output, "analytics-other-account"));
            var otherService = new TaskService(emptyStore, clock); var other = new MainWindow(otherService, emptyStore, holidays, smoke: true);
            other.Navigate(PageKind.Analytics); Prepare(other);
            Check(Descendants(other).OfType<TextBlock>().Single(t => Equals(t.Tag, "analytics-total")).Text == "0", "另一个账号显示了之前账号的统计。"); other.Close();
            for (var i = 0; i < 20; i++) service.State.Events.Add(new EventDefinition { Title = $"滚动验证示例 {i + 1:00}", StartDate = today, Repeat = RepeatRule.Daily });
            var narrow = new MainWindow(service, store, holidays, smoke: true) { Width = 1100, Height = 760, ShowInTaskbar = false };
            narrow.Show(); narrow.Width = 1000; narrow.UpdateLayout(); narrow.Navigate(PageKind.Analytics);
            try
            {
                await VerifyAnalyticsScrolling(narrow, output, "narrow");
                Render((FrameworkElement)narrow.Content, 984, 720, Path.Combine(output, "analytics-narrow.png"));
                narrow.Width = 1400; narrow.Height = 940; narrow.UpdateLayout();
                await VerifyAnalyticsScrolling(narrow, output, "wide");
            }
            finally { narrow.Close(); }
            File.WriteAllText(Path.Combine(output, "analytics-result.txt"), "PASS: empty/current-account-only data; four statistics; read-only navigation; 365/366 heat cells and future state; heat/trend/backlog/habit drill-downs; undo and completion update totals and lists; 7-day/monthly filters; year/month navigation; narrow real window layout.\n");
        }
        finally
        {
            details?.Close(); window.AnalyticsDetailsPresenter = presenter; clock.Today = today;
            service.RestoreBackup(baseline); window.Navigate(PageKind.Today); if (File.Exists(baseline)) File.Delete(baseline);
        }
    }
}
