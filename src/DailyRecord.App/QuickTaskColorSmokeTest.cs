using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ellipse = System.Windows.Shapes.Ellipse;
using System.Windows.Threading;
using DailyRecord.Core;

namespace DailyRecord.App;

internal static partial class SmokeTest
{
    private static async Task VerifyQuickTaskColors(HolidayService holidays, string output)
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        var clock = new PreviewClock();
        using var store = new SqliteStore(Path.Combine(output, "quick-color-fixture", Guid.NewGuid().ToString("N")));
        var service = new TaskService(store, clock);
        for(var i = 0; i < 28; i++) service.State.Tasks.Add(new TaskItem { Title = $"当前安排 {i + 1:00} · 分步完成今天的计划", Notes = "隔离示例，保留图文备注与历史。",
            ScheduledDate = clock.Today, OriginalScheduledDate = clock.Today, CreatedAt = clock.Now.AddSeconds(i), Color = i % 3 == 0 ? TaskColor.Purple : TaskColor.None });
        service.Save();
        var task = service.State.Tasks[7]; var previous = task.Color;
        var window = new MainWindow(service, store, holidays, smoke: true) { Width = 1400, Height = 940, ShowInTaskbar = false };
        async Task Layout() => await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
        void ClickTag(string tag) => FindButton(window, tag).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            window.Show(); await Layout();
            var scroller = (ScrollViewer)window.FindName("PageScroller");
            var quick = FindButton(window, "quick-color:" + task.Id);
            var star = FindButton(window, "important:" + task.Id);
            Check(quick.Parent == star.Parent && ((Panel)quick.Parent).Children.IndexOf(quick) + 1 == ((Panel)star.Parent).Children.IndexOf(star), "快捷圆点不在星标前面。");
            Check(quick.Content is Ellipse && quick.ContextMenu!.Items.OfType<MenuItem>().Count() == 6, "圆点或右键直达六色缺失。");
            var state = StateJson.Serialize(service.State); var revision = service.Revision;
            quick.ContextMenu.PlacementTarget = quick; quick.ContextMenu.IsOpen = true; await Layout();
            Check(state == StateJson.Serialize(service.State) && revision == service.Revision, "打开选色菜单就修改了数据。");
            quick.ContextMenu.IsOpen = false;
            scroller.ScrollToVerticalOffset(780); await Layout(); var offset = scroller.VerticalOffset;
            Check(offset > 100, "没有建立真实滚动位置。");
            ClickTag("quick-color:" + task.Id); await Layout();
            Check(task.Color == TaskColor.Blue && task.QuickColorRestore?.PreviousColor == previous && Math.Abs(scroller.VerticalOffset - offset) < 1 && !window.PageScroll.IsAnimating, "一键着色改变滚动位置或丢失恢复。");
            ClickTag("quick-color:" + task.Id); await Layout();
            Check(task.Color == previous && Math.Abs(scroller.VerticalOffset - offset) < 1, "再次点击没有恢复原颜色或滚动位置。");
            var originalPurple = service.State.Tasks[6];
            ClickTag("quick-color:" + originalPurple.Id); await Layout();
            ClickTag("quick-color:" + originalPurple.Id); await Layout();
            Check(originalPurple.Color == TaskColor.Purple, "紫色原分类恢复失败。");
            var right = FindButton(window, "quick-color:" + task.Id);
            right.ContextMenu!.Items.OfType<MenuItem>().Single(m => Equals(m.Tag, $"quick-palette:{task.Id}:Pink"))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Layout();
            Check(task.Color == TaskColor.Pink && task.QuickColorRestore is null && Math.Abs(scroller.VerticalOffset - offset) < 1, "右键颜色未生效或跳回顶部。");
            ClickTag("quick-color-undo"); await Layout(); Check(task.Color == previous, "快捷撤销未恢复颜色。");

            var setting = FindButton(window, "quick-color-setting");
            setting.ContextMenu!.Items.OfType<MenuItem>().Single(m => Equals(m.Tag, "quick-setting:Teal"))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Layout();
            Check(service.State.Settings.QuickTaskColor == TaskColor.Teal && store.Load().Settings.QuickTaskColor == TaskColor.Teal && Math.Abs(scroller.VerticalOffset - offset) < 1, "偏好保存或设置后视口保持失败。");
            ClickTag("quick-color:" + task.Id); await Layout(); Check(task.Color == TaskColor.Teal, "任务行仍使用旧快捷色。");
            ClickTag("color-filter:Teal"); await Layout();
            ClickTag("quick-color:" + task.Id); await Layout();
            Check(!Descendants(window).OfType<Button>().Any(b => Equals(b.Tag, "quick-color:" + task.Id)), "移出当前分类后任务仍显示。");
            Check(Descendants(window).OfType<TextBlock>().Any(t => Equals(t.Tag, "quick-color-notice-text")), "移出筛选后没有操作提示。");
            ClickTag("quick-color-undo"); await Layout(); Check(task.Color == TaskColor.Teal && FindButton(window, "quick-color:" + task.Id) is not null, "筛选移出后撤销没有找回任务。");
            ClickTag("color-filter:All"); await Layout();
            scroller.ScrollToVerticalOffset(600); await Layout(); offset = scroller.VerticalOffset;
            // Continuous wheel input is stopped at its current viewport by the color operation.
            var source = FindButton(window, "quick-color:" + task.Id);
            source.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
            var current = scroller.VerticalOffset; ClickTag("quick-color:" + task.Id); await Layout();
            Check(!window.PageScroll.IsAnimating && Math.Abs(scroller.VerticalOffset - current) < 1, "着色后旧滚动动画继续拖动视口。");
            window.Navigate(PageKind.Today); scroller.ScrollToVerticalOffset(580); await Layout();
            Render((FrameworkElement)window.Content, 1400, 940, Path.Combine(output, "quick-colors-task-row.png"));
            scroller.ScrollToTop(); await Layout();
            Render((FrameworkElement)window.Content, 1400, 940, Path.Combine(output, "quick-colors-settings.png"));
            window.Navigate(PageKind.History); await Layout();
            var historyTask = service.State.Tasks[5]; scroller.ScrollToVerticalOffset(400); await Layout(); offset = scroller.VerticalOffset;
            ClickTag("quick-color:" + historyTask.Id); await Layout();
            Check(Math.Abs(scroller.VerticalOffset - offset) < 1, "历史页着色跳回顶部。");
            var daily = service.AddLongTermTask("每天阅读", "", clock.Today, color: TaskColor.Purple);
            window.Navigate(PageKind.Today); await Layout(); ClickTag("quick-color:" + daily.Id); await Layout();
            Check(service.State.Events.Single(e => e.Id == daily.EventId).Color == TaskColor.Purple && daily.Color == TaskColor.Teal, "长期快捷着色改变后续默认。");
            var palette = FindButton(window, "quick-color:" + daily.Id).ContextMenu!;
            Check(palette.Items.OfType<MenuItem>().Count(m => m.Tag is string tag && tag.StartsWith("quick-palette:")) == 6, "重复任务右键仍需多层选择本次颜色。");
            ClickTag("quick-color:" + daily.Id); await Layout(); Check(daily.Color == TaskColor.Purple && !daily.ColorOverride, "长期任务原色与继承标记未恢复。");
            File.WriteAllText(Path.Combine(output, "quick-colors-result.txt"), "PASS: real row dot before star; context menu opens without writes and six colors directly selectable; quick apply/restore, explicit palette, undo including filter removal; saved quick-color preference; actual scrolled today/history viewport retained; in-flight wheel stopped; recurring color only this occurrence; original-color inheritance restored; notice cleanup on navigation/close. Isolated sample records only.");
        }
        finally { window.Close(); }
    }
}
