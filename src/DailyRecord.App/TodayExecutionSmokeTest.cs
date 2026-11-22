using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DailyRecord.Core;

namespace DailyRecord.App;

internal static partial class SmokeTest
{
    private static async Task VerifyExecutionOrder(HolidayService holidays, string output)
    {
        static void Check(bool okay, string message) { if (!okay) throw new InvalidOperationException(message); }
        var clock = new PreviewClock();
        using var store = new SqliteStore(Path.Combine(output, "execution-ui", Guid.NewGuid().ToString("N")));
        var service = new TaskService(store, clock);
        var a = service.AddTask("整理本周笔记", "", clock.Today, color: TaskColor.Blue);
        var hidden = service.AddTask("回复组内消息", "", clock.Today, color: TaskColor.Pink);
        var c = service.AddTask("先看会议材料", "", clock.Today, color: TaskColor.Blue);
        for (var i = 1; i <= 18; i++)
            service.AddTask($"待办示例 {i:00}", "", clock.Today, color: TaskColor.Blue);
        var window = new MainWindow(service, store, holidays, true) { Width = 1400, Height = 920, ShowInTaskbar = false };
        async Task Layout() => await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
        void ClickTag(string tag) => FindButton(window, tag).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        string[] VisibleIds() => Descendants(window).OfType<Border>().Where(b => b.Tag is string tag && tag.StartsWith("execution-handle:"))
            .Select(b => ((string)b.Tag)[17..]).ToArray();
        try
        {
            window.Show();
            await Layout();
            Check(VisibleIds().Take(3).SequenceEqual([a.Id, hidden.Id, c.Id]), "自动模式的手柄顺序错误。");
            var scroll = (ScrollViewer)window.FindName("PageScroller");
            var firstRow = Descendants(window).OfType<Border>().Single(b => Equals(b.Tag, a.Id));
            scroll.ScrollToVerticalOffset(firstRow.TranslatePoint(new Point(), scroll).Y + scroll.VerticalOffset - 110);
            await Layout();
            var oldOffset = scroll.VerticalOffset;
            window.DropExecutionTask(c.Id, firstRow.TranslatePoint(new Point(10, 2), scroll));
            await Layout();
            Check(service.UsesManualExecutionOrder && VisibleIds().Take(3).SequenceEqual([c.Id, a.Id, hidden.Id]), "实际落点没有将任务移到第一位。");
            Check(oldOffset > 0 && Math.Abs(scroll.VerticalOffset - oldOffset) < 1, "移动后滚动位置丢失。");
            ClickTag("quick-color-undo");
            await Layout();
            Check(!service.UsesManualExecutionOrder && VisibleIds()[0] == a.Id, "首拖撤销未还原自动模式。");
            ClickTag("color-filter:Blue");
            await Layout();
            var menu = FindButton(window, "more:" + c.Id).ContextMenu!.Items.OfType<MenuItem>()
                .Single(m => Equals(m.Tag, "execution-move:" + c.Id));
            menu.Items.OfType<MenuItem>().Single(m => Equals(m.Tag, "first")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Layout();
            Check(VisibleIds().Take(2).SequenceEqual([c.Id, a.Id]) && service.TodayExecutionOrder()[1].Id == hidden.Id, "筛选菜单移动覆盖隐藏位置。");
            var numbers = Descendants(window).OfType<TextBlock>().Where(t => t.Tag is string tag && tag.StartsWith("execution-number:"));
            Check(numbers.Take(2).Select(t => t.Text).SequenceEqual(["1", "2"]), "可见序号未刷新。");
            var share = TaskShareImage.Build(service, holidays, TaskColor.Blue);
            var shared = Descendants(share).OfType<TextBlock>().Where(t => t.Tag is string tag && tag.StartsWith("share-task:"))
                .Select(t => ((string)t.Tag)[11..]);
            Check(shared.SequenceEqual(VisibleIds()), "图片与筛选后的手动顺序不一致。");
            SaveBitmap(TaskShareImage.Render(share), Path.Combine(output, "execution-share.png"));
            ClickTag("execution-mode-auto");
            await Layout();
            Check(VisibleIds()[0] == a.Id, "自动排序按钮未还原分组。");
            ClickTag("execution-mode-manual");
            await Layout();
            Check(VisibleIds()[0] == c.Id, "手动模式未恢复。");
            scroll.ScrollToVerticalOffset(Math.Max(0, scroll.VerticalOffset + FindButton(window, "execution-mode-manual").TranslatePoint(new Point(), scroll).Y - 24));
            await Layout();
            Render((FrameworkElement)window.Content, 1400, 920, Path.Combine(output, "execution-today.png"));
            File.WriteAllText(Path.Combine(output, "execution-result.txt"),
                "PASS: actual WPF list and viewport drop coordinates; auto-to-manual transition, undo, filtered menu move, numbered rows, shared image order, and both mode toggles. Isolated sample data.");
        }
        finally { window.Close(); }
    }
}
