using System.IO;
using System.Windows;
using System.Windows.Controls;
using DailyRecord.Core;

namespace DailyRecord.App;

internal static partial class SmokeTest
{
    private static void VerifyLongTermTasks(MainWindow window, TaskService service, PreviewClock clock, string output)
    {
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var baseline = Path.Combine(output, "long-term-baseline.json"); service.ExportBackup(baseline); var today = clock.Today;
        try
        {
            var editor = new EditDialog(window, today, false);
            Descendants(editor).OfType<TextBox>().Single(t => Equals(t.Tag, "task-title")).Text = "每天推进毕业论文，直到完成整个项目";
            var checkbox = Descendants(editor).OfType<CheckBox>().Single(c => Equals(c.Tag, "long-term-task")); checkbox.IsChecked = true;
            Check(editor.LongTermTask, "长期任务选项没有生效。");
            Render((FrameworkElement)editor.Content, 560, 820, Path.Combine(output, "long-term-editor.png"));
            var first = service.AddLongTermTask(editor.ItemTitle, "每天记录一点进度，项目结束时手动结束。", editor.Date, true);
            window.Navigate(PageKind.Today); Prepare(window);
            Check(Equals(FindButton(window, "complete:" + first.Id).Content, "今日完成"), "长期任务未显示今日完成。");
            Check(!Descendants(window).OfType<Button>().Any(b => Equals(b.Tag, "viewed:" + first.Id)), "长期任务错误显示了提前提醒查看操作。");
            Click(window, "complete:" + first.Id);
            Check(first.State == TaskState.Completed && service.IsLongTermActive(first), "今日完成结束了整个任务。");
            clock.Today = today.AddDays(1); service.RefreshDay(); window.Navigate(PageKind.Today); Prepare(window);
            var next = service.VisibleOpen(clock.Today).Single(t => t.EventId == first.EventId);
            Check(next.Id != first.Id && next.IsImportant, "跨天未出现新任务或星标丢失。");
            Render((FrameworkElement)window.Content, 1400, 940, Path.Combine(output, "long-term-today.png"));
            Click(window, "end-long-term:" + next.Id);
            Check(!service.VisibleOpen(clock.Today).Any(t => t.EventId == first.EventId), "结束未立即停止今日出现。");
            clock.Today = today.AddDays(2); service.RefreshDay(); window.Navigate(PageKind.Today); Prepare(window);
            Check(!service.VisibleOpen(clock.Today).Any(t => t.EventId == first.EventId), "结束后次日重新出现。");
            Check(service.State.Tasks.Single(t => t.Id == first.Id).State == TaskState.Completed, "结束丢失完成历史。");
            editor.Close();
            File.WriteAllText(Path.Combine(output, "long-term-result.txt"), "PASS: long-term checkbox; today's completion; new day without duplicate overdue tasks; inherited important flag; actual end button; stopped next day; completion history preserved.\n");
        }
        finally { clock.Today = today; service.RestoreBackup(baseline); window.Navigate(PageKind.Today); if (File.Exists(baseline)) File.Delete(baseline); }
    }
}
