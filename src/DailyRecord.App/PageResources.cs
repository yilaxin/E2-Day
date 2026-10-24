using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DailyRecord.App;

public partial class MainWindow
{
    // A short return still reuses templates and controls. Long-inactive calendars no longer live for the entire session.
    internal TimeSpan CalendarCacheLifetime { get; set; } = TimeSpan.FromSeconds(45);
    private long? _yearHiddenAt;
    private long? _monthHiddenAt;
    private DispatcherTimer? _calendarExpiry;
    internal bool HasCachedYearView => _calendarBody is not null;
    internal bool HasCachedMonthView => MonthHost.Child is not null;
    internal bool CalendarExpiryScheduled => (_yearHiddenAt.HasValue || _monthHiddenAt.HasValue) && _calendarExpiry?.IsEnabled == true;
    internal bool ResourceExpiryScheduled => _calendarExpiry?.IsEnabled == true;

    private void ReleasePageFocus()
    {
        // Logical focus can keep a removed input and its entire page alive after keyboard focus moves away.
        bool IsPageElement(DependencyObject? element)
        {
            for (var node = element; node is not null;)
            {
                if (ReferenceEquals(node, PageBody) || ReferenceEquals(node, CalendarHost) ||
                    ReferenceEquals(node, MonthHost) || ReferenceEquals(node, HeaderActions)) return true;
                node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
            }
            return false;
        }
        if (IsPageElement(Keyboard.FocusedElement as DependencyObject))
        {
            // Give the input subsystem a live target instead of leaving its last text editor detached.
            if (_nav.TryGetValue(_page, out var navigation) && navigation.IsVisible) Keyboard.Focus(navigation);
            else Keyboard.ClearFocus();
        }
        if (IsPageElement(FocusManager.GetFocusedElement(this) as DependencyObject)) FocusManager.SetFocusedElement(this, null);
    }

    private void UpdateCalendarHosts()
    {
        var yearVisible = _page == PageKind.Year && _selectedMonth is null;
        var monthVisible = _page == PageKind.Year && _selectedMonth is not null;
        // A recent calendar retains its layout for fast return; idle expiry disconnects and retires it.
        CalendarScroller.Visibility = yearVisible ? Visibility.Visible : Visibility.Hidden;
        MonthScroller.Visibility = monthVisible ? Visibility.Visible : Visibility.Hidden;
        if (yearVisible) _yearHiddenAt = null;
        else if (_calendarBody is not null)
        {
            _yearHiddenAt ??= Stopwatch.GetTimestamp();
        }
        if (monthVisible) _monthHiddenAt = null;
        else if (MonthHost.Child is not null)
        {
            _monthHiddenAt ??= Stopwatch.GetTimestamp();
        }
    }

    private void ScheduleCalendarExpiry()
    {
        _calendarExpiry?.Stop();
        var remaining = new[] { _yearHiddenAt, _monthHiddenAt }.Where(t => t.HasValue)
            .Select(t => CalendarCacheLifetime - Stopwatch.GetElapsedTime(t!.Value)).ToArray();
        if (remaining.Length == 0) return;
        if (_calendarExpiry is null)
        {
            _calendarExpiry = new DispatcherTimer(DispatcherPriority.ContextIdle, Dispatcher);
            _calendarExpiry.Tick += ExpireCalendarViews;
        }
        _calendarExpiry.Interval = TimeSpan.FromMilliseconds(Math.Max(1, remaining.Min().TotalMilliseconds));
        _calendarExpiry.Start();
    }

    private void ExpireCalendarViews(object? sender, EventArgs e)
    {
        _calendarExpiry?.Stop();
        if (_pageScroll.IsAnimating || _calendarScroll.IsAnimating || _monthScroll.IsAnimating || Mouse.Captured is not null)
        {
            // Retiring a large tree must wait until scrolling/dragging has settled.
            if (_calendarExpiry is not null) { _calendarExpiry.Interval = TimeSpan.FromMilliseconds(250); _calendarExpiry.Start(); }
            return;
        }
        if (_yearHiddenAt is { } year && Stopwatch.GetElapsedTime(year) >= CalendarCacheLifetime && (_page != PageKind.Year || _selectedMonth is not null))
            ReleaseYearView();
        if (_monthHiddenAt is { } month && Stopwatch.GetElapsedTime(month) >= CalendarCacheLifetime && (_page != PageKind.Year || _selectedMonth is null))
            ReleaseMonthView();
        ScheduleCalendarExpiry();
    }

    private void ReleaseYearView()
    {
        if (_calendarBody is not null) RetirePageTree(_calendarBody);
        CalendarHost.Child = null; _calendarBody = null; _calendarMonths = null; _calendarNotice = null;
        _calendarDays.Clear(); _calendarMonthCounts.Clear(); _yearHiddenAt = null;
        _calendarDataRevision = -1; _calendarHolidayRevision = -1;
    }

    private void ReleaseMonthView()
    {
        if (MonthHost.Child is { } body) RetirePageTree(body);
        MonthHost.Child = null; _renderedMonth = null; _monthCount = null; _monthNotice = null;
        _monthDays.Clear(); _monthHiddenAt = null; _monthDataRevision = -1; _monthHolidayRevision = -1;
    }

    private void ReleasePageResources()
    {
        ReleasePageFocus();
        if (_calendarExpiry is not null)
        {
            _calendarExpiry.Stop(); _calendarExpiry.Tick -= ExpireCalendarViews; _calendarExpiry = null;
        }
        ReleaseYearView(); ReleaseMonthView();
        _analyticsHabitPicker = null; _quickInput = null; _quickImportant = null;
        ClearPageContent();
    }

    private void ClearPageContent()
    {
        RetirePanelChildren(PageBody); RetirePanelChildren(HeaderActions);
    }

    private static void RetirePanelChildren(Panel panel)
    {
        var children = panel.Children.OfType<UIElement>().ToArray(); panel.Children.Clear();
        foreach (var child in children) RetirePageTree(child);
    }

    // Only retire trees that will never be reused. Do not walk shared styles or control templates.
    // WPF may temporarily retain an input/automation peer; severing owned content links prevents
    // that one control from keeping every sibling, chart and image on the old page alive.
    private static void RetirePageTree(UIElement element)
    {
        element.CacheMode = null;
        switch (element)
        {
            case Panel panel:
                RetirePanelChildren(panel);
                break;
            case Decorator decorator:
                var decorated = decorator.Child; decorator.Child = null;
                if (decorated is not null) RetirePageTree(decorated);
                break;
            case ContentControl control when control.Content is UIElement content:
                control.Content = null; RetirePageTree(content);
                break;
            case Image image:
                image.Source = null;
                break;
        }
    }
}
