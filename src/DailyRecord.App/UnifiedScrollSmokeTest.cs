using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DailyRecord.Core;

namespace DailyRecord.App;

internal static partial class SmokeTest
{
    private static async Task VerifyUnifiedScrolling(HolidayService holidays, PreviewClock clock, string output)
    {
        static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        static void Wheel(UIElement source, int delta = -120)
        {
            var e = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
            source.RaiseEvent(e);
            if (!e.Handled) { e.RoutedEvent = Mouse.MouseWheelEvent; source.RaiseEvent(e); }
        }
        static IEnumerable<DependencyObject> Visuals(DependencyObject root)
        {
            yield return root;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Visuals(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
        using var store = new SqliteStore(Path.Combine(output, "unified-scroll-fixture", Guid.NewGuid().ToString("N")));
        var service = new TaskService(store, clock);
        for (var i = 0; i < 28; i++) service.State.Tasks.Add(new TaskItem
        {
            Title = $"统一滚动示例 {i + 1:00}", Notes = "隔离验证数据", ScheduledDate = clock.Today,
            OriginalScheduledDate = clock.Today, CreatedAt = clock.Now,
            State = i >= 18 ? TaskState.Completed : TaskState.Open, CompletedOn = i >= 18 ? clock.Today : null
        });
        service.Save();
        var window = new MainWindow(service, store, holidays, smoke: true) { Width = 1100, Height = 760, ShowInTaskbar = false };
        var reports = new List<string>();
        bool Animating() => window.PageScroll.IsAnimating || window.CalendarScroll.IsAnimating || window.MonthScroll.IsAnimating;
        async Task Layout() { await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle); }
        async Task Settle()
        {
            await Layout(); var watch = Stopwatch.StartNew();
            while (Animating() && watch.ElapsedMilliseconds < 1800) await Task.Delay(16);
            Check(!Animating(), "统一滚动未及时停止。"); await Layout();
        }
        ScrollViewer Viewer(string name) => (ScrollViewer)window.FindName(name);
        void ClickTag(string tag) => FindButton(window, tag).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        async Task Motion(string label, ScrollViewer viewer, SmoothWheelScroller scroll, UIElement source)
        {
            await Settle(); Check(viewer.ScrollableHeight > 60, label + "缺少可滚动内容。");
            var start = Math.Min(40, viewer.ScrollableHeight / 5);
            viewer.ScrollToVerticalOffset(start); await Settle();
            start = viewer.VerticalOffset;
            var before = StateJson.Serialize(service.State); var revision = service.Revision;
            var offsets = new List<double>();
            ScrollChangedEventHandler sample = (_, e) => { if (ReferenceEquals(e.OriginalSource, viewer) && e.VerticalChange != 0) offsets.Add(viewer.VerticalOffset); };
            viewer.ScrollChanged += sample;
            try
            {
                Wheel(source);
                if (SystemParameters.ClientAreaAnimation && SystemParameters.WheelScrollLines != 0)
                {
                    Check(scroll.IsAnimating && viewer.VerticalOffset == start, label + "没有启动平滑过渡。");
                    var target = scroll.TargetOffset; await Settle();
                    Check(offsets.Count >= 2 && offsets.Any(v => v > start && v < target), label + "缺少中间位置。");
                    Check(Math.Abs(viewer.VerticalOffset - target) < .5 && offsets.Zip(offsets.Skip(1)).All(p => p.First <= p.Second), label + "终点错误或向后跳动。");
                    reports.Add($"{label}: {offsets.Count} intermediate changes, {start:F1} -> {target:F1}; monotonic; target reached.");
                    viewer.ScrollToVerticalOffset(start); await Settle();
                    for (var i = 0; i < 4; i++) Wheel(source, -30);
                    Check(Math.Abs(scroll.TargetOffset - target) < .5, label + "细小连续输入未合并到同一目标。"); await Settle();
                    viewer.ScrollToVerticalOffset(start); await Settle(); Wheel(source); await Task.Delay(30); window.UpdateLayout();
                    var reverse = viewer.VerticalOffset; Wheel(source, 120);
                    Check(scroll.TargetOffset < reverse, label + "反向仍追赶原目标。"); await Settle();
                }
                else await Settle();
            }
            finally { viewer.ScrollChanged -= sample; }
            Check(before == StateJson.Serialize(service.State) && revision == service.Revision, label + "滚动改动了记录。");
            viewer.ScrollToBottom(); await Settle(); var bottom = viewer.VerticalOffset;
            Wheel(source); await Settle(); Check(viewer.VerticalOffset == bottom, label + "底部继续滚动发生跳动。");
            Wheel(source, 120); await Settle(); Check(SystemParameters.WheelScrollLines == 0 || viewer.VerticalOffset < bottom, label + "到底后不能反向。");
            viewer.ScrollToTop(); await Settle(); Wheel(source, 120); await Settle(); Check(viewer.VerticalOffset == 0, label + "顶部越界。");
        }
        try
        {
            window.Show();
            foreach (var width in new[] { 1000, 1400 })
            {
                window.Width = width; await Settle();
                var page = Viewer("PageScroller"); var year = Viewer("CalendarScroller"); var month = Viewer("MonthScroller");
                window.Navigate(PageKind.Today); await Settle();
                var task = service.State.Tasks.First(t => t.State == TaskState.Open);
                var complete = FindButton(window, "complete:" + task.Id);
                await Motion($"{width}/today", page, window.PageScroll, complete);
                Check(ReferenceEquals(complete, FindButton(window, "complete:" + task.Id)), "今日滚动重建了任务行。");
                var quick = Descendants(window).OfType<TextBox>().Single(); quick.Text = "保留输入中的草稿";
                await Motion($"{width}/today-input", page, window.PageScroll, quick);
                Check(quick.Text == "保留输入中的草稿", "滚动丢失了今日草稿。");
                Wheel(complete); complete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle();
                Check(!window.PageScroll.IsAnimating && task.State == TaskState.Completed, "今日完成操作未停止动画或未生效。");
                FindButton(window, "complete:" + task.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle();

                window.Navigate(PageKind.Year); await Settle();
                var yearHost = (Border)window.FindName("CalendarHost"); var yearBody = yearHost.Child;
                var day = FindButton(window, "date:" + clock.Today.ToString("yyyy-MM-dd"));
                await Motion($"{width}/year", year, window.CalendarScroll, day);
                Check(ReferenceEquals(yearBody, yearHost.Child) && ReferenceEquals(day, FindButton(window, "date:" + clock.Today.ToString("yyyy-MM-dd"))), "全年滚动重建了缓存。");
                Wheel(day); ClickTag($"month:{clock.Today:yyyy-MM}"); await Settle();
                Check(!window.CalendarScroll.IsAnimating && month.IsVisible && !year.IsVisible, "全年切换月历后动画仍运行。");
                var monthHost = (Border)window.FindName("MonthHost"); var monthBody = monthHost.Child;
                var monthDay = FindButton(window, "month-date:" + clock.Today.ToString("yyyy-MM-dd"));
                await Motion($"{width}/month", month, window.MonthScroll, monthDay);
                Check(ReferenceEquals(monthBody, monthHost.Child), "月历滚动重建了缓存。");
                Wheel(monthDay); ClickTag("back-year"); await Settle();
                Check(!window.MonthScroll.IsAnimating && ReferenceEquals(yearBody, yearHost.Child), "返回全年未停止动画或丢失缓存。");
                ClickTag($"month:{clock.Today:yyyy-MM}"); await Settle();
                Check(ReferenceEquals(monthBody, monthHost.Child), "重开同月没有复用原控件。");
                Wheel(FindButton(window, "month-date:" + clock.Today.ToString("yyyy-MM-dd"))); ClickTag("next-month"); await Settle();
                Check(!window.MonthScroll.IsAnimating && !ReferenceEquals(monthBody, monthHost.Child), "月份切换未停止旧动画或未更新内容。");

                window.Navigate(PageKind.History); await Settle();
                var historyTask = FindButton(window, "complete:" + task.Id);
                await Motion($"{width}/history", page, window.PageScroll, historyTask);
                Check(ReferenceEquals(historyTask, FindButton(window, "complete:" + task.Id)), "历史滚动重建了事项。");
                var search = Descendants(window).OfType<TextBox>().Single();
                await Motion($"{width}/history-search", page, window.PageScroll, search);
                Wheel(historyTask); search.Text = "统一滚动"; await Settle();
                Check(!window.PageScroll.IsAnimating && Descendants(window).OfType<TextBox>().Single().Text == "统一滚动", "历史筛选未停止动画或丢失输入。");
                search.Text = ""; await Settle();
                var date = Descendants(window).OfType<DatePicker>().Single(); var selected = date.SelectedDate;
                date.IsDropDownOpen = true; await Settle();
                var popup = (Popup)date.Template.FindName("PART_Popup", date);
                var popupDay = Visuals(popup.Child).OfType<CalendarDayButton>().First(b => b.IsEnabled);
                var offset = page.VerticalOffset; Wheel(popupDay); await Settle();
                Check(!window.PageScroll.IsAnimating && page.VerticalOffset == offset && date.SelectedDate == selected,
                    $"日期弹窗滚轮带动了背景或改变选择：offset {offset:F1}->{page.VerticalOffset:F1}, animating={window.PageScroll.IsAnimating}, date {selected}->{date.SelectedDate}。");
                Visuals(popup.Child).OfType<Calendar>().Single().SelectedDate = clock.Today.AddDays(-1).ToDateTime(TimeOnly.MinValue); await Settle();
                Check(date.SelectedDate == clock.Today.AddDays(-1).ToDateTime(TimeOnly.MinValue) && page.ScrollableHeight == 0, "日期弹窗选择未更新历史日期及内容。");
                date.SelectedDate = selected; await Settle();
                date.IsDropDownOpen = false; await Settle();
                Wheel(FindButton(window, "complete:" + task.Id)); window.Navigate(PageKind.Today); await Settle();
                Check(!Animating() && page.VerticalOffset == 0, "页面切换后仍有旧动画。");
                window.Navigate(PageKind.History); await Settle();
                Descendants(window).OfType<DatePicker>().Single().SelectedDate = clock.Today.AddDays(-1).ToDateTime(TimeOnly.MinValue); await Settle();
                Check(page.ScrollableHeight == 0, "空历史日期没有收起滚动范围。"); Wheel((UIElement)page.Content); await Settle();
                Check(!Animating() && page.VerticalOffset == 0, "无溢出页面仍启动动画。");
                Descendants(window).OfType<DatePicker>().Single().SelectedDate = clock.Today.ToDateTime(TimeOnly.MinValue); await Settle();
            }
            window.Navigate(PageKind.Settings); await Settle(); Wheel((UIElement)Viewer("PageScroller").Content); await Settle();
            Check(!Animating(), "设置页意外启用平滑滚动。");
            window.Navigate(PageKind.Year); await Settle(); Wheel(FindButton(window, "date:" + clock.Today.ToString("yyyy-MM-dd")));
            window.Close(); Check(!Animating(), "关闭窗口后未释放三种滚动动画。");
            reports.Add("PASS: today, year, zoomed month and history share easing; input fields and drafts; continuous/fine/reverse input; bounds; no-overflow page; actions/search interrupt; date popup isolation; year/month cache reuse; month navigation; page changes; settings scope and close cleanup. Isolated fixture only.");
            File.WriteAllLines(Path.Combine(output, "unified-scroll-result.txt"), reports);
        }
        finally { window.Close(); }
    }
}
