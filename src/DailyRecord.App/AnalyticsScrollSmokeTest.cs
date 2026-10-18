using System.IO;
using System.Diagnostics;
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
        async Task Settle()
        {
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            var watch = Stopwatch.StartNew();
            while (window.AnalyticsScroll.IsAnimating && watch.ElapsedMilliseconds < 1800) await Task.Delay(16);
            Check(!window.AnalyticsScroll.IsAnimating, "滚动动画未及时停止。");
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
        }
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var page = (ScrollViewer)window.FindName("PageScroller");
        await Settle();
        if (page.ScrollableHeight <= 100) throw new InvalidOperationException("滚动验证缺少可滚动的真实窗口。");
        var heat = Descendants(window).OfType<Button>().First(b => b.IsEnabled && b.Tag is string tag && tag.StartsWith("analytics-heat:"));
        var picker = Descendants(window).OfType<ComboBox>().Single(c => Equals(c.Tag, "analytics-habit-picker"));
        var report = new List<string>();
        VerifyScrollMotion();
        if (layout == "narrow") await VerifySmoothScrollPreferences(output);
        if (SystemParameters.ClientAreaAnimation && SystemParameters.WheelScrollLines > 0)
        {
            var offsets = new List<double>();
            ScrollChangedEventHandler sample = (_, e) => { if (ReferenceEquals(e.OriginalSource, page) && e.VerticalChange != 0) offsets.Add(page.VerticalOffset); };
            page.ScrollToVerticalOffset(150); await Settle(); page.ScrollChanged += sample;
            try
            {
                Wheel(heat, -120);
                Check(window.AnalyticsScroll.IsAnimating && page.VerticalOffset == 150, "滚轮仍然直接跳到终点。");
                var destination = window.AnalyticsScroll.TargetOffset;
                await Settle();
                Check(offsets.Count >= 2 && offsets.Any(n => n > 150 && n < destination), "没有观察到中间滚动位置。");
                Check(offsets.Zip(offsets.Skip(1)).All(p => p.First <= p.Second), "向下滚动出现回跳。");
                Check(Math.Abs(page.VerticalOffset - destination) < .5, "滚动终点不正确。");
                report.Add($"Smooth wheel: {offsets.Count} changed positions; 150 -> {destination:F1}; intermediate positions observed; no backwards jump.");
            }
            finally { page.ScrollChanged -= sample; }
            page.ScrollToVerticalOffset(150); await Settle();
            for (var i = 0; i < 4; i++) { Wheel(heat, -120); await Task.Delay(12); }
            var accumulated = Math.Min(page.ScrollableHeight, 150 + 4 * ScrollMotion.WheelDistance(-120, SystemParameters.WheelScrollLines, page.ViewportHeight));
            Check(Math.Abs(window.AnalyticsScroll.TargetOffset - accumulated) < .5, "连续输入丢失或重复累计。");
            await Settle(); Check(Math.Abs(page.VerticalOffset - accumulated) < .5, "连续滚动没有到达合并目标。");
            page.ScrollToVerticalOffset(150); await Settle(); Wheel(heat, -120); await Task.Delay(35); window.UpdateLayout();
            var reversal = page.VerticalOffset; Wheel(heat, 120);
            Check(window.AnalyticsScroll.TargetOffset < reversal, "反向滚动仍追赶旧方向的目标。");
            await Settle(); Check(page.VerticalOffset < reversal, "反向滚动没有立即改变方向。");
            Wheel(heat, -120); await Task.Delay(20); page.ScrollToVerticalOffset(80); await Settle();
            Check(Math.Abs(page.VerticalOffset - 80) < .5, "程序定位后动画把页面拉回旧位置。");
            Wheel(heat, -120);
            page.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
            Check(!window.AnalyticsScroll.IsAnimating, "鼠标操作没有停止剩余滚动。");
            report.Add("PASS: merged continuous wheel input; immediate reversal; external offset and pointer input cancel animation.");
        }
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
        for (var i = 0; i < 4; i++) Wheel(trend, -30);
        await Settle();
        var normal = page.VerticalOffset;
        page.ScrollToVerticalOffset(150); await Settle();
        for (var i = 0; i < 4; i++) Wheel(heat, -30);
        await Settle();
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
        if (SystemParameters.ClientAreaAnimation && SystemParameters.WheelScrollLines > 0)
        {
            Wheel(heat, -120); window.Navigate(PageKind.Today); await Settle(); await Task.Delay(100);
            Check(!window.AnalyticsScroll.IsAnimating && page.VerticalOffset == 0, "离开数据概览后动画继续改变其它页面位置。");
            window.Navigate(PageKind.Analytics); await Settle();
            if (layout == "wide")
            {
                heat = Descendants(window).OfType<Button>().First(b => b.IsEnabled && b.Tag is string tag && tag.StartsWith("analytics-heat:"));
                Wheel(heat, -120); window.Close(); Check(!window.AnalyticsScroll.IsAnimating, "关闭窗口后仍保留动画订阅。");
            }
        }
    }

    private static void VerifyScrollMotion()
    {
        static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        var one = new ScrollMotion(); one.Reset(100); one.Add(48, 1000);
        var fine = new ScrollMotion(); fine.Reset(100);
        for (var i = 0; i < 4; i++) fine.Add(ScrollMotion.WheelDistance(-30, 3, 500), 1000);
        Check(fine.Target == one.Target && ScrollMotion.WheelDistance(-120, -1, 500) == 500 && ScrollMotion.WheelDistance(-120, 0, 500) == 0,
            "细小增量、整页或零行系统设置计算错误。");
        for (var i = 0; i < 6; i++) fine.Advance(.01);
        one.Advance(.06); Check(Math.Abs(one.Position - fine.Position) < .0001, "平滑滚动速度取决于帧率。");
        Check(one.Position > 100 && one.Position < one.Target, "平滑滚动缺少中间位置。");
        one.Add(-48, 1000); Check(one.Target < one.Position, "反向输入没有取消旧方向的剩余距离。");
        one.Add(5000, 1000); Check(one.Target == 1000, "连续滚动越过底部。");
        one.Advance(1); Check(one.Position == 1000 && !one.IsMoving, "停止输入后没有及时收住。");
        one.Add(-5000, 1000); one.Advance(1); Check(one.Position == 0 && !one.IsMoving, "连续滚动越过顶部。");
    }

    private static async Task VerifySmoothScrollPreferences(string output)
    {
        static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        static void Wheel(UIElement source) => source.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
        var viewer = new ScrollViewer { Content = new Border { Height = 1800, Background = Brushes.White } };
        var host = new Window { Content = viewer, Width = 420, Height = 320, ShowInTaskbar = false };
        var animate = false; var enabled = true;
        using var scrolling = new SmoothWheelScroller(viewer, () => enabled, () => animate);
        async Task Layout() { await host.Dispatcher.InvokeAsync(() => host.UpdateLayout(), DispatcherPriority.ContextIdle); }
        try
        {
            host.Show(); await Layout();
            var direct = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
            viewer.RaiseEvent(direct); Check(!direct.Handled && !scrolling.IsAnimating, "关闭系统动画时仍拦截原生滚动。");
            direct.RoutedEvent = Mouse.MouseWheelEvent; viewer.RaiseEvent(direct); await Layout();
            Check(SystemParameters.WheelScrollLines == 0 || viewer.VerticalOffset > 0, "关闭系统动画时无法直接滚动。");
            animate = true; Wheel(viewer);
            if (SystemParameters.WheelScrollLines != 0)
            {
                Check(scrolling.IsAnimating, "恢复动画后不能平滑滚动。");
                viewer.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(host)!, Environment.TickCount, Key.End) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                Check(!scrolling.IsAnimating, "键盘滚动没有停止动画。");
                Wheel(viewer); enabled = false; await Task.Delay(100); await Layout();
                Check(!scrolling.IsAnimating, "停用页面后仍保留帧订阅。");
                enabled = true; Wheel(viewer); animate = false; await Task.Delay(100); await Layout();
                Check(!scrolling.IsAnimating, "动画偏好关闭后仍在运行。");
                animate = true; Wheel(viewer); host.Close(); await Layout();
                Check(!scrolling.IsAnimating, "卸载后仍保留帧订阅。");
            }
            File.WriteAllText(Path.Combine(output, "analytics-smooth-preferences-result.txt"), "PASS: deterministic time-based easing, delta scaling, merged targets, immediate reversal and bounded settling; reduced-motion native fallback; keyboard interruption; disable, preference change and unload release render subscriptions.");
        }
        finally { host.Close(); }
    }
}
