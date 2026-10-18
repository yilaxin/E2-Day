using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using DailyRecord.Core;

namespace DailyRecord.App;

internal static partial class TutorSmoke
{
    private static void VerifyExamples(TutorWindow window, TutorService service, TutorStore store, string output)
    {
        window.ShowInTaskbar = false; window.Show();
        var before = JsonSerializer.Serialize(service.State);
        var disk = JsonSerializer.Serialize(store.Load());
        var backups = Directory.GetFiles(store.BackupDirectory).Order().ToArray();
        string? temporary = null;
        var sessions = 0;
        window.PresentExamples = example =>
        {
            sessions++;
            Check(example.IsExample && example.Service.State.Students.Count == 6, "示例窗口没有独立数据");
            example.Width = 1380; example.Height = 1000; example.ShowInTaskbar = false; example.Show(); Prepare(example);
            Check(example.DashboardReport is { Taught: > 0, Minutes: > 0, MissingFeedback.Count: > 0, Pending.Count: > 0 }, "示例未覆盖教学概览与跟进状态");
            Check(Descendants(example).OfType<Border>().Any(b => Equals(b.Tag, "examples-banner")), "缺少醒目的虚构示例提示");
            if (sessions == 1)
            {
                Snapshot(example, Path.Combine(output, "tutor-examples.png"));
                example.PageScroller.ScrollToEnd(); Snapshot(example, Path.Combine(output, "tutor-examples-details.png"));
            }
            for (var page = 0; page < 7; page++)
            {
                example.Navigate(page); Prepare(example);
                Check(Descendants(example).OfType<Border>().Any(b => Equals(b.Tag, "examples-banner")), "切换页面丢失示例标识");
                Check(!Descendants(example).OfType<Button>().Any(b => Equals(b.Tag, "examples-open")), "示例可嵌套打开");
            }
            example.Navigate(5);
            Check(!Descendants(example).OfType<Button>().Any(b => b.Content?.ToString() == "从备份恢复" || b.Content?.ToString() == "切换 / 管理账号登录"), "临时示例可恢复个人备份或切换真实账号");
            // A demo edit must save to its own database, with no effect on either real state or real backups.
            Check(!example.Service.State.Todos[0].Done, "再次进入没有重新生成示例");
            example.Service.ToggleTodo(example.Service.State.Todos[0].Id);
            var pending = example.Service.State.Lessons.First(l => l.Status == LessonStatus.Taught && !l.HasFeedback);
            var editor = new TutorLessonDialog(example.Service, example.ExampleStore, pending.Date, pending, testing: true);
            Text(editor, "lesson-content", "示例练习：补写课堂反馈"); Click(editor, "dialog-save");
            Check(editor.Saved && example.Service.State.Lessons.Single(l => l.Id == pending.Id).HasFeedback, "示例无法实际编辑反馈");
            temporary = example.ExampleStore.DirectoryPath;
            example.Width = 1000; example.Height = 650; example.Navigate(6); Prepare(example);
            var close = Find<Button>(example, "examples-close");
            Check(close.IsVisible && close.ActualHeight > 0, "小窗口中无法返回真实记录");
            Click(example, "examples-close");
        };
        window.Navigate(5); Click(window, "examples-open");
        Check(temporary is not null && !Directory.Exists(temporary), "关闭示例后临时数据库未清理");
        window.OpenExamples();
        Check(sessions == 2 && temporary is not null && !Directory.Exists(temporary), "再次体验或临时清理失败");
        Check(before == JsonSerializer.Serialize(service.State) && disk == JsonSerializer.Serialize(store.Load()), "示例体验更改了个人数据");
        Check(backups.SequenceEqual(Directory.GetFiles(store.BackupDirectory).Order()), "示例体验更改了个人备份");
        window.PresentExamples = example => example.ShowDialog();
        File.WriteAllText(Path.Combine(output, "examples-result.txt"), "PASS: one-click examples, six fictional students, all seven pages labelled, real feedback editing, repeat entry resets, temporary cleanup, compact return button, real database and backups unchanged.\n");
    }
}
