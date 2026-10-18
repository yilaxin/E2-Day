using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DailyRecord.Core;

namespace DailyRecord.App;

internal static partial class TutorSmoke
{
    private static void VerifyDashboard(TutorWindow window, TutorService service, TutorStore store, string output)
    {
        using (var emptyStore = new TutorStore(Path.Combine(output, "empty-dashboard-fixture")))
        {
            var empty = new TutorWindow(new TutorService(emptyStore), emptyStore, window.Today) { Width = 1000, Height = 650, ShowInTaskbar = false };
            empty.Show(); empty.Navigate(6); Prepare(empty);
            Check(empty.DashboardReport is { Taught: 0, Minutes: 0, ActiveStudents: 0 } && !Find<Button>(empty, "dashboard-metric:2:待补反馈").IsEnabled, "空数据概览不正确");
            Snapshot(empty, Path.Combine(output, "tutor-dashboard-empty.png")); empty.Close();
        }
        var first = service.State.Students[0]; var third = service.State.Students.Single(s => s.Name.StartsWith("小陈"));
        third.LearningLevel = "需巩固"; third.LearningPace = TutorStudentProfile.Paces[1]; third.LearningFocus = ["基础巩固", "习惯培养"]; service.SaveStudent(third);
        service.SaveStudent(new TutorStudent { Name = "小许（示例）", Grade = "初一", Subject = "数学", HourlyRate = 130 });
        void Add(string student, int offset, int minutes, LessonStatus status)
            => service.SaveLesson(new TutorLesson { StudentId = student, Subject = "数学（示例）", Date = window.Today.AddDays(offset), Minutes = minutes, HourlyRate = 150, Status = status });
        Add(first.Id, -12, 120, LessonStatus.Taught); Add(third.Id, -5, 60, LessonStatus.Taught); Add(third.Id, -9, 60, LessonStatus.Cancelled);
        Add(third.Id, -window.Today.Day, 60, LessonStatus.Taught);
        var before = JsonSerializer.Serialize(service.State); var disk = JsonSerializer.Serialize(store.Load());
        window.Width = 1380; window.Height = 1000; window.ShowInTaskbar = false; window.Show(); Click(window, "nav:6");
        Check(window.DashboardReport is { Taught: 8, Minutes: 720, MissingFeedback.Count: 2, Pending.Count: 1, ActiveStudents: 4 }, "概览汇总与示例实际记录不符");
        Snapshot(window, Path.Combine(output, "tutor-dashboard.png"));
        window.PageScroller.ScrollToEnd(); Snapshot(window, Path.Combine(output, "tutor-dashboard-details.png"));
        Find<DatePicker>(window, "dashboard-from").SelectedDate = window.Today.ToDateTime(TimeOnly.MinValue);
        Find<DatePicker>(window, "dashboard-to").SelectedDate = window.Today.AddDays(-1).ToDateTime(TimeOnly.MinValue); Click(window, "dashboard-apply");
        Check(Find<TextBlock>(window, "dashboard-error").Text.Length > 0 && window.DashboardReport!.Taught == 8, "无效日期没有提示或破坏当前概览");
        Find<DatePicker>(window, "dashboard-from").SelectedDate = new DateTime(window.Today.Year, window.Today.Month, 1);
        Find<DatePicker>(window, "dashboard-to").SelectedDate = new DateTime(window.Today.Year, window.Today.Month, 1).AddMonths(1).AddDays(-1);
        Click(window, "dashboard-apply"); Check(window.DashboardReport!.Planned == 4, "未来重复排课未进入概览");
        Find<ComboBox>(window, "dashboard-student").SelectedValue = first.Id; Click(window, "dashboard-apply");
        Check(window.DashboardReport!.Students.Count == 1 && window.DashboardReport.Students[0].StudentId == first.Id && window.DashboardReport.Taught == 4, "概览学生筛选错误");
        Click(window, "dashboard-student:" + first.Id);
        Check(Descendants(window).OfType<TextBlock>().Any(t => t.Text == first.Name), "排行点击未进入学生档案");
        Click(window, "nav:6"); Find<ComboBox>(window, "dashboard-student").SelectedValue = ""; Click(window, "dashboard-apply"); Click(window, "dashboard-month");
        var targetDay = window.Today.AddDays(-12); Click(window, "dashboard-day:" + targetDay.ToString("yyyy-MM-dd"));
        Check(Descendants(window).OfType<Button>().Any(b => Equals(b.Tag, "day:" + targetDay.ToString("yyyy-MM-dd"))), "热力格点击未进入对应月历");
        Click(window, "nav:6"); Click(window, "dashboard-feedback-all");
        Check(Descendants(window).OfType<Border>().Count(b => b.Tag?.ToString()?.StartsWith("lesson:") == true) == 2 && Find<Button>(window, "records-clear-range").Content.ToString()!.Contains("2026-10-01"), "反馈跳转没有保留日期范围");
        Click(window, "records-clear-range"); Check(Descendants(window).OfType<Border>().Count(b => b.Tag?.ToString()?.StartsWith("lesson:") == true) == 3, "清除日期筛选没有恢复全部待补反馈");
        Click(window, "nav:6"); Click(window, "dashboard-metric:1:待确认课程");
        Check(Descendants(window).OfType<Border>().Count(b => b.Tag?.ToString()?.StartsWith("lesson:") == true) == 1, "待确认课程跳转口径错误");
        Click(window, "nav:6"); var missing = window.DashboardReport!.MissingFeedback[0]; var opened = false;
        window.Dispatcher.BeginInvoke(() =>
        {
            var dialog = window.OwnedWindows.OfType<TutorLessonDialog>().Single();
            opened = (string)Find<ComboBox>(dialog, "lesson-student").SelectedValue == missing.StudentId && Find<DatePicker>(dialog, "lesson-date").SelectedDate == missing.Date.ToDateTime(TimeOnly.MinValue);
            dialog.Close();
        }, DispatcherPriority.ApplicationIdle);
        Click(window, "dashboard-feedback:" + missing.Id); Check(opened, "补写反馈未打开对应课程编辑器");
        var period = window.DashboardReport!.Periods[0]; Click(window, "dashboard-period:" + period.From.ToString("yyyy-MM-dd"));
        Check(window.DashboardReport!.From == period.From && window.DashboardReport.To == period.To, "柱形点击未切换到对应时段");
        Click(window, "dashboard-year"); Check(window.DashboardReport!.From == new DateOnly(window.Today.Year, 1, 1), "本年快捷筛选错误");
        Snapshot(window, Path.Combine(output, "tutor-dashboard-year.png"));
        window.Width = 1000; window.Height = 650; Click(window, "dashboard-month"); Snapshot(window, Path.Combine(output, "tutor-dashboard-compact.png"));
        Prepare(window);
        var nav = Find<Button>(window, "nav:5"); var root = (FrameworkElement)window.Content; var bottom = nav.TransformToAncestor(root).Transform(new Point(0, nav.ActualHeight));
        Check(nav.IsVisible && bottom.Y < root.ActualHeight - 40, "小窗口侧边栏设置入口被页脚遮挡");
        var draft = Find<DatePicker>(window, "dashboard-from"); draft.SelectedDate = window.Today.AddDays(-1).ToDateTime(TimeOnly.MinValue);
        window.Width = 1380; Prepare(window); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Check(Find<Grid>(window, "dashboard-grid:2").ColumnDefinitions.Count == 2 && ReferenceEquals(draft, Find<DatePicker>(window, "dashboard-from")) && draft.SelectedDate == window.Today.AddDays(-1).ToDateTime(TimeOnly.MinValue), "放大窗口后图表未重排或丢失筛选草稿");
        window.Width = 1000; Prepare(window); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Check(Find<Grid>(window, "dashboard-grid:2").RowDefinitions.Count == 2 && draft.SelectedDate == window.Today.AddDays(-1).ToDateTime(TimeOnly.MinValue), "缩小窗口后图表未纵向排列或丢失筛选草稿");
        Check(before == JsonSerializer.Serialize(service.State) && disk == JsonSerializer.Serialize(store.Load()), "筛选、查看图表或关闭反馈窗口改变了教学数据");
        File.WriteAllText(Path.Combine(output, "dashboard-result.txt"), "PASS: empty state, known totals, future recurring plans, student/date filters, invalid dates, heatmap/student/feedback links, date-scoped records, actual editor opening, period drilldown, yearly heatmap, compact sidebar and read-only database checks.\n");
    }
}
