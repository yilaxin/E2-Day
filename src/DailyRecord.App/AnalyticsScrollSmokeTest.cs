using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DailyRecord.App;

internal static partial class SmokeTest
{
    private static async Task VerifyAnalyticsScrolling(MainWindow window, string output, string layout)
    {
        static IEnumerable<DependencyObject> Visuals(DependencyObject root)
        {
            yield return root;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Visuals(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
        static void Wheel(UIElement source, int delta)
        {
            var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
            source.RaiseEvent(args);
            if (!args.Handled) { args.RoutedEvent = Mouse.MouseWheelEvent; source.RaiseEvent(args); }
        }
        async Task Settle() { await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle); }
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var page = (ScrollViewer)window.FindName("PageScroller");
        await Settle();
        if (page.ScrollableHeight <= 100) throw new InvalidOperationException("滚动验证缺少可滚动的真实窗口。");
        var heat = Descendants(window).OfType<Button>().First(b => b.IsEnabled && b.Tag is string tag && tag.StartsWith("analytics-heat:"));
        var picker = Descendants(window).OfType<ComboBox>().Single(c => Equals(c.Tag, "analytics-habit-picker"));
        var report = new List<string>();
        page.ScrollToVerticalOffset(150); await Settle();
        var before = page.VerticalOffset; Wheel(heat, -120); await Settle();
        report.Add($"Heat wheel: {before:F1} -> {page.VerticalOffset:F1}; extent={page.ExtentHeight:F1}; viewport={page.ViewportHeight:F1}");
        var heatMoved = page.VerticalOffset > before;
        picker.Focus(); await Settle(); page.ScrollToVerticalOffset(150); await Settle();
        var selected = picker.SelectedItem; before = page.VerticalOffset;
        Wheel(picker, -120); await Settle();
        report.Add($"Focused closed picker wheel: {before:F1} -> {page.VerticalOffset:F1}; selection preserved={Equals(selected, picker.SelectedItem)}");
        var reportPath = Path.Combine(output, $"analytics-scroll-{layout}-result.txt");
        File.WriteAllLines(reportPath, report);
        if (!heatMoved) throw new InvalidOperationException("热力图截住了整页的向下滚轮。");
        if (page.VerticalOffset <= before || !Equals(selected, picker.SelectedItem)) throw new InvalidOperationException("关闭的打卡选择框截住滚轮或改变了选择。");
        before = page.VerticalOffset; Wheel(heat, 120); await Settle();
        Check(page.VerticalOffset < before, "热力图向上滚轮没有传给页面。");
        Keyboard.ClearFocus(); page.ScrollToVerticalOffset(150); await Settle(); before = page.VerticalOffset;
        Wheel(picker, -120); await Settle();
        Check(page.VerticalOffset > before && Equals(selected, picker.SelectedItem), "未聚焦的选择框截住滚轮或改变选择。");
        var trend = Descendants(window).OfType<Button>().First(b => b.Tag is string tag && tag.StartsWith("analytics-trend:"));
        before = page.VerticalOffset; Wheel(trend, -120); await Settle();
        Check(page.VerticalOffset > before, "普通图表区域不能滚动。");

        page.ScrollToVerticalOffset(150); await Settle();
        for (var i = 0; i < 4; i++) { Wheel(trend, -30); await Settle(); }
        var normal = page.VerticalOffset;
        page.ScrollToVerticalOffset(150); await Settle();
        for (var i = 0; i < 4; i++) { Wheel(heat, -30); await Settle(); }
        Check(normal > 150 && Math.Abs(page.VerticalOffset - normal) < .1, "热力图小步滚轮与页面其它区域的滚动距离不符。");
        Check(ReferenceEquals(heat, Descendants(window).OfType<Button>().First(b => b.IsEnabled && b.Tag is string tag && tag.StartsWith("analytics-heat:"))), "滚轮触发了图表重建。");

        var horizontal = Visuals(window).OfType<ScrollViewer>().Single(s => ReferenceEquals(s.Content, (heat.Parent as Grid)));
        var originalWidth = horizontal.Width;
        horizontal.Width = 400; await Settle(); horizontal.LineRight(); await Settle();
        Check(horizontal.ScrollableWidth > 0 && horizontal.HorizontalOffset > 0, "热力图横向滚动失效。");
        var left = horizontal.HorizontalOffset; before = page.VerticalOffset;
        Wheel(heat, -120); await Settle();
        Check(page.VerticalOffset > before && horizontal.HorizontalOffset == left, "热力图横向溢出时不能滚动整页。");
        horizontal.Width = originalWidth; await Settle();

        picker.Focus(); picker.IsDropDownOpen = true; await Settle();
        var popup = (Popup)picker.Template.FindName("PART_Popup", picker);
        var menu = Visuals(popup.Child).OfType<ScrollViewer>().Single();
        var item = Visuals(popup.Child).OfType<ComboBoxItem>().First();
        Check(menu.ScrollableHeight > 0, "下拉菜单没有足够选项验证滚动。");
        menu.ScrollToTop(); await Settle(); before = page.VerticalOffset;
        Wheel(item, -120); await Settle();
        Check(menu.VerticalOffset > 0 && page.VerticalOffset == before && picker.IsDropDownOpen, "展开菜单的滚轮错误地滚动了背景页面。");
        picker.IsDropDownOpen = false; await Settle();
        picker.SelectedIndex = 2; await Settle();
        var changed = Descendants(window).OfType<ComboBox>().Single(c => Equals(c.Tag, "analytics-habit-picker"));
        Check(changed.SelectedIndex == 2, "正常选择长期任务失效。");

        heat = Descendants(window).OfType<Button>().First(b => b.IsEnabled && b.Tag is string tag && tag.StartsWith("analytics-heat:"));
        page.ScrollToBottom(); await Settle(); before = page.VerticalOffset; Wheel(heat, -120); await Settle();
        Check(page.VerticalOffset == before, "底部继续向下滚动发生跳动。");
        Wheel(heat, 120); await Settle(); Check(page.VerticalOffset < before, "到达底部后不能反向滚动。");
        page.ScrollToTop(); await Settle(); Wheel(heat, 120); await Settle();
        Check(page.VerticalOffset == 0, "顶部继续向上滚动发生跳动。");
        Wheel(heat, -120); await Settle(); Check(page.VerticalOffset > 0, "到达顶部后不能反向滚动。");
        report.Add("PASS: heat cells and ordinary chart wheel routing; focused/unfocused closed picker preserves selection; open popup scrolls its own list; normal habit selection; horizontal overflow remains usable; fractional deltas; top/bottom reversal; chart controls reused.");
        File.WriteAllLines(reportPath, report);
        page.ScrollToTop(); await Settle();
    }
}
