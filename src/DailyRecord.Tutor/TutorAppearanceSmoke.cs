using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DailyRecord.Core;

namespace DailyRecord.App;

internal static partial class TutorSmoke
{
    private static IEnumerable<DependencyObject> VisualChildren(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in VisualChildren(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static async Task VerifyAppearanceAndScrolling(TutorWindow window, TutorService service, string output)
    {
        Check(Ui.Page is LinearGradientBrush && Application.Current.TryFindResource("GlassCard") is Style,
            "家教版没有加载渐变背景与玻璃卡片样式");
        window.ShowInTaskbar = false; window.Show();
        var viewer = window.PageScroller; var motion = window.PageScroll;
        var before = System.Text.Json.JsonSerializer.Serialize(service.State); var reports = new List<string>();
        async Task Settle()
        {
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            var watch = Stopwatch.StartNew();
            while (motion.IsAnimating && watch.ElapsedMilliseconds < 1800) await Task.Delay(16);
            Check(!motion.IsAnimating, "页面滚动动画没有停止");
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
        }
        void Wheel(UIElement source, int delta)
        {
            var e = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
            source.RaiseEvent(e);
            if (!e.Handled) { e.RoutedEvent = Mouse.MouseWheelEvent; source.RaiseEvent(e); }
        }
        for (var page = 0; page < 7; page++)
        {
            window.Navigate(page); await Settle();
            if (page == 3)
            {
                var picker = Descendants(window).OfType<ComboBox>().Single();
                Check(VisualChildren(picker).OfType<TextBlock>().Any(t => t.Text == "全部学生"), "学生下拉框没有显示姓名");
            }
            if (viewer.ScrollableHeight <= 60) { reports.Add($"Page {page}: no overflow, theme and navigation checked."); continue; }
            viewer.ScrollToVerticalOffset(30); await Settle(); var start = viewer.VerticalOffset;
            var source = Descendants(window).OfType<Button>().FirstOrDefault(b => b.IsVisible && b.Tag?.ToString()?.StartsWith("day:") == true)
                ?? (UIElement)viewer.Content;
            var samples = new List<double>();
            ScrollChangedEventHandler sample = (_, e) => { if (ReferenceEquals(e.OriginalSource, viewer) && e.VerticalChange != 0) samples.Add(viewer.VerticalOffset); };
            viewer.ScrollChanged += sample;
            try
            {
                Wheel(source, -120); var target = motion.TargetOffset;
                if (SystemParameters.ClientAreaAnimation && SystemParameters.WheelScrollLines != 0)
                {
                    Check(motion.IsAnimating && viewer.VerticalOffset == start, "页面没有启动平滑过渡"); await Settle();
                    Check(samples.Count >= 2 && samples.Any(v => v > start && v < target), "缺少滚动中间帧");
                    Check(Math.Abs(viewer.VerticalOffset - target) < .5 && samples.Zip(samples.Skip(1)).All(p => p.First <= p.Second), "滚动终点错误或跳动");
                    viewer.ScrollToVerticalOffset(start); await Settle();
                    for (var i = 0; i < 4; i++) Wheel(source, -30);
                    Check(Math.Abs(motion.TargetOffset - target) < .5, "连续细小滚轮输入没有合并"); await Settle();
                    viewer.ScrollToVerticalOffset(start); await Settle(); Wheel(source, -120); await Task.Delay(30); window.UpdateLayout();
                    var reverse = viewer.VerticalOffset; Wheel(source, 120);
                    Check(motion.TargetOffset < reverse, "反向滚动仍追赶旧目标"); await Settle();
                }
                else await Settle();
                viewer.ScrollToBottom(); await Settle(); var bottom = viewer.VerticalOffset;
                Wheel(source, -120); await Settle(); Check(viewer.VerticalOffset == bottom, "底部滚动越界");
                Wheel(source, 120); await Settle(); Check(SystemParameters.WheelScrollLines == 0 || viewer.VerticalOffset < bottom, "到底后不能反向滚动");
                Wheel(source, -120); window.Navigate((page + 1) % 7); await Settle();
                Check(!motion.IsAnimating && viewer.VerticalOffset == 0, "切页后旧动画没有停止");
                reports.Add($"Page {page}: smooth intermediate frames, fine input, immediate reversal, bounds and navigation passed.");
            }
            finally { viewer.ScrollChanged -= sample; }
        }
        window.SelectDate(window.Today.AddDays(14)); await Settle();
        var add = Find<Button>(window, "add-selected-date-lesson");
        Check(add.MinHeight >= 52 && ReferenceEquals(add.Style, Application.Current.FindResource("AccentButton")), "指定日期添加课程按钮不醒目");
        window.Navigate(0); await Settle();
        var draft = Find<TextBox>(window, "todo-title"); draft.Text = "滚动中保留的草稿"; Wheel(draft, -120); await Settle();
        Check(draft.Text == "滚动中保留的草稿" && before == System.Text.Json.JsonSerializer.Serialize(service.State), "滚动改变草稿或教学记录");
        draft.Text = "";
        Wheel(draft, -120); window.Close(); Check(!motion.IsAnimating, "关闭后动画事件没有释放");
        File.WriteAllLines(Path.Combine(output, "appearance-scroll-result.txt"), reports);
    }
}
