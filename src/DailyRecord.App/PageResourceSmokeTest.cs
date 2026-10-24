using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DailyRecord.Core;

namespace DailyRecord.App;

internal static partial class SmokeTest
{
    private static async Task VerifyPageResourceLifecycle(string output)
    {
        static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }

        using var store = new SqliteStore(Path.Combine(output, "page-resources-fixture", Guid.NewGuid().ToString("N")));
        var clock = new PreviewClock(); var service = new TaskService(store, clock);
        var holidays = new HolidayService(Path.Combine(AppContext.BaseDirectory, "Data", "Holidays"), Path.Combine(store.DataDirectory, "Holidays"));
        var timings = new List<string>();
        var window = new MainWindow(service, store, holidays, true) { Width = 1400, Height = 760, ShowInTaskbar = false };
        var yearHost = (Border)window.FindName("CalendarHost"); var monthHost = (Border)window.FindName("MonthHost");
        async Task Settle() { await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle); await Task.Delay(35); }
        async Task Collect()
        {
            await Settle(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        }
        try
        {
            window.Show(); await Settle();
            foreach (var width in new[] { 1000d, 1400d })
            {
                window.Width = width; window.CalendarCacheLifetime = TimeSpan.FromSeconds(45); await Settle();
                var before = StateJson.Serialize(service.State); var revision = service.Revision;
                var recent = CaptureRecentCalendar(window, clock.Today);
                Check(yearHost.Child is not null && monthHost.Child is not null && window.HasCachedYearView && window.HasCachedMonthView,
                    "最近访问的年／月控件没有保留短时布局缓存。");
                var watch = Stopwatch.StartNew(); window.Navigate(PageKind.Year); await Settle();
                timings.Add($"{width}: recent return + real layout {watch.Elapsed.TotalMilliseconds:F2} ms");
                Check(ReferenceEquals(recent[0].Target, yearHost.Child), "短时间返回重建了全年控件。");
                Click(window, $"month:{clock.Today:yyyy-MM}"); await Settle();
                Check(ReferenceEquals(recent[1].Target, monthHost.Child) && !((FrameworkElement)window.FindName("CalendarScroller")).IsVisible, "短时间返回重建月历或隐藏全年仍连接。");
                window.CalendarCacheLifetime = TimeSpan.FromMilliseconds(100);
                window.Navigate(PageKind.Today);
                // Refreshes on other pages must not extend the original calendar deadline.
                for (var i = 0; i < 8; i++) { window.Navigate(i % 2 == 0 ? PageKind.Settings : PageKind.Today); await Task.Delay(30); }
                await Collect();
                Check(!window.HasCachedYearView && !window.HasCachedMonthView && !window.CalendarExpiryScheduled, "日历空闲到期未释放或计时器仍运行。");
                Check(recent.All(r => !r.IsAlive), "到期日历控件仍被引用。");
                Check(before == StateJson.Serialize(service.State) && revision == service.Revision, "资源回收修改了任务数据。");
                service.AddEvent("离页后新增的安排", "", clock.Today, EventImportance.Important, RepeatRule.None);
                watch.Restart(); window.Navigate(PageKind.Year); await Settle();
                timings.Add($"{width}: expired return + real layout {watch.Elapsed.TotalMilliseconds:F2} ms");
                Check(FindButton(window, "date:" + clock.Today.ToString("yyyy-MM-dd")).ToolTip.ToString()!.Contains("离页后新增的安排"), "到期重建未读取当前记录。");
                await Task.Delay(150); await Settle();
                Check(yearHost.Child is not null && window.HasCachedYearView && !window.CalendarExpiryScheduled, "活动日历被空闲计时器释放。");
                window.CalendarCacheLifetime = TimeSpan.FromMilliseconds(80);
                var oldPage = LeaveFocusedPage(window, PageKind.Today); await Task.Delay(220); await Collect();
                for (var attempt = 0; attempt < 5 && oldPage.IsAlive; attempt++) { await Task.Delay(60); await Collect(); }
                Check(!oldPage.IsAlive, "今日页输入控件仍连带保留已移除的页面内容。");
                oldPage = LeaveFocusedPage(window, PageKind.History); await Task.Delay(220); await Collect();
                for (var attempt = 0; attempt < 5 && oldPage.IsAlive; attempt++) { await Task.Delay(60); await Collect(); }
                Check(!oldPage.IsAlive, "历史页输入控件仍连带保留已移除的页面内容。");
            }
            // Expiry must not compete with a drag or a live smooth-scroll animation.
            for (var i = 0; i < 28; i++) service.State.Tasks.Add(new TaskItem { Title = "回收与滚动交互验证 " + i,
                ScheduledDate = clock.Today, OriginalScheduledDate = clock.Today, CreatedAt = clock.Now });
            service.Save();
            window.CalendarCacheLifetime = TimeSpan.FromSeconds(45); CaptureRecentCalendar(window, clock.Today);
            window.CalendarCacheLifetime = TimeSpan.FromMilliseconds(1); window.Navigate(PageKind.Today); window.UpdateLayout();
            var navigation = (StackPanel)window.FindName("NavigationPanel");
            Mouse.Capture((Button)navigation.Children[0]);
            try
            {
                await Task.Delay(100);
                Check(window.HasCachedYearView && window.HasCachedMonthView && window.CalendarExpiryScheduled, "拖动期间执行了日历回收。");
            }
            finally { Mouse.Capture(null); }
            if (SystemParameters.ClientAreaAnimation && SystemParameters.WheelScrollLines != 0)
            {
                var input = Descendants(window).OfType<TextBox>().First();
                input.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
                Check(window.PageScroll.IsAnimating, "资源验证没有启动平滑滚动。");
                await Task.Delay(20);
                Check(window.HasCachedYearView && window.HasCachedMonthView, "平滑滚动期间回收了日历。");
            }
            for (var attempt = 0; attempt < 30 && window.CalendarExpiryScheduled; attempt++) await Task.Delay(40);
            Check(!window.HasCachedYearView && !window.HasCachedMonthView, "交互结束后没有继续回收。");
            window.CalendarCacheLifetime = TimeSpan.FromSeconds(45);
            window.Navigate(PageKind.Year); Click(window, $"month:{clock.Today:yyyy-MM}"); window.Navigate(PageKind.Today);
            Check(window.CalendarExpiryScheduled, "测试关闭时没有待清理的日历计时器。");
            window.Close();
            Check(!window.ResourceExpiryScheduled && !window.HasCachedYearView && !window.HasCachedMonthView && yearHost.Child is null && monthHost.Child is null,
                "窗口关闭后仍保留日历树或空闲回调。");
            File.WriteAllLines(Path.Combine(output, "page-resources-performance.txt"), timings);
            File.WriteAllText(Path.Combine(output, "page-resources-result.txt"), "PASS: 1000/1400 real windows; short-lived year/month layout cache; recent control/template reuse; timer expiry and collectible old views; unrelated refreshes do not extend deadlines; active view protected; cleanup deferred during mouse capture/smooth scrolling and resumes afterward; latest records rebuilt after expiry; old focused Today/History content detached and collectible; no task/revision changes from cleanup; close cancels timer and releases trees. Explicit GC only in isolated verification.\n");
        }
        finally { if (window.IsVisible) window.Close(); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] CaptureRecentCalendar(MainWindow window, DateOnly today)
    {
        window.Navigate(PageKind.Year); window.UpdateLayout();
        var year = new WeakReference(((Border)window.FindName("CalendarHost")).Child!);
        var button = Descendants(window).OfType<Button>().Single(b => Equals(b.Tag, $"month:{today:yyyy-MM}"));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
        var month = new WeakReference(((Border)window.FindName("MonthHost")).Child!);
        window.Navigate(PageKind.Today); window.UpdateLayout(); return [year, month];
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LeaveFocusedPage(MainWindow window, PageKind page)
    {
        window.Navigate(page); window.UpdateLayout();
        var input = Descendants(window).OfType<TextBox>().First(); input.Focus(); Keyboard.Focus(input);
        var reference = new WeakReference(LogicalTreeHelper.GetParent(input)!);
        window.Navigate(PageKind.Settings); window.UpdateLayout();
        return reference;
    }
}
