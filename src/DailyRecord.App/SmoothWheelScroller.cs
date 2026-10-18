using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DailyRecord.App;

// One target, advanced on render frames. Input never queues a chain of animations.
internal sealed class ScrollMotion
{
    internal double Position { get; private set; }
    internal double Target { get; private set; }
    internal bool IsMoving => Math.Abs(Target - Position) > .01;
    internal void Reset(double offset) => Position = Target = offset;
    internal void Add(double distance, double maximum)
    {
        if (distance == 0) return;
        var basis = (Target - Position) * distance < 0 ? Position : Target;
        Target = Math.Clamp(basis + distance, 0, maximum);
    }
    internal double Advance(double elapsedSeconds)
    {
        Position = Target + (Position - Target) * Math.Exp(-Math.Max(0, elapsedSeconds) / .055);
        if (Math.Abs(Target - Position) < .25) Position = Target;
        return Position;
    }
    internal static double WheelDistance(int delta, int lines, double viewport) =>
        -(double)delta / Mouse.MouseWheelDeltaForOneLine * (lines < 0 ? viewport : 16d * lines);
}

internal sealed class SmoothWheelScroller : IDisposable
{
    private readonly ScrollViewer _viewer;
    private readonly Func<bool> _enabled;
    private readonly Func<bool> _animationsEnabled;
    private readonly ScrollMotion _motion = new();
    private long _lastFrame;
    private TimeSpan _renderTime;
    private double _requested;
    private bool _disposed;
    internal bool IsAnimating { get; private set; }
    internal double TargetOffset => _motion.Target;

    internal SmoothWheelScroller(ScrollViewer viewer, Func<bool> enabled, Func<bool>? animationsEnabled = null)
    {
        _viewer = viewer; _enabled = enabled;
        _animationsEnabled = animationsEnabled ?? (() => SystemParameters.ClientAreaAnimation);
        viewer.PreviewMouseWheel += OnWheel;
        viewer.PreviewMouseDown += OnMouseDown;
        viewer.PreviewKeyDown += OnKeyDown;
        viewer.ScrollChanged += OnScrollChanged;
        viewer.Unloaded += OnUnloaded;
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || e.Delta == 0 || !_enabled() || !_viewer.IsVisible) return;
        if (!_animationsEnabled()) { Stop(); return; }
        e.Handled = true;
        if (!IsAnimating) _motion.Reset(_viewer.VerticalOffset);
        _motion.Add(ScrollMotion.WheelDistance(e.Delta, SystemParameters.WheelScrollLines, _viewer.ViewportHeight), _viewer.ScrollableHeight);
        if (!_motion.IsMoving) { Stop(); return; }
        if (IsAnimating) return;
        _requested = _viewer.VerticalOffset;
        _lastFrame = Stopwatch.GetTimestamp();
        _renderTime = TimeSpan.MinValue;
        IsAnimating = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!_enabled() || !_viewer.IsVisible) { Stop(); return; }
        if (e is RenderingEventArgs frame)
        {
            if (frame.RenderingTime == _renderTime) return;
            _renderTime = frame.RenderingTime;
        }
        var now = Stopwatch.GetTimestamp();
        var elapsed = (now - _lastFrame) / (double)Stopwatch.Frequency; _lastFrame = now;
        var animate = _animationsEnabled();
        _requested = animate ? _motion.Advance(elapsed) : _motion.Target;
        _viewer.ScrollToVerticalOffset(_requested);
        if (!_motion.IsMoving || !animate) Stop();
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Ignore nested horizontal viewers and stop if a scrollbar, keyboard action,
        // BringIntoView or layout change takes control of the page's position.
        if (!IsAnimating || !ReferenceEquals(e.OriginalSource, _viewer)) return;
        if (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0 ||
            e.VerticalChange != 0 && Math.Abs(_viewer.VerticalOffset - _requested) > .5) Stop();
    }
    private void OnMouseDown(object sender, MouseButtonEventArgs e) => Stop();
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End or Key.Tab or Key.Space) Stop();
    }
    private void OnUnloaded(object sender, RoutedEventArgs e) => Stop();
    internal void Stop()
    {
        if (!IsAnimating) return;
        CompositionTarget.Rendering -= OnRendering;
        IsAnimating = false;
        _motion.Reset(_viewer.VerticalOffset);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Stop();
        _viewer.PreviewMouseWheel -= OnWheel;
        _viewer.PreviewMouseDown -= OnMouseDown;
        _viewer.PreviewKeyDown -= OnKeyDown;
        _viewer.ScrollChanged -= OnScrollChanged;
        _viewer.Unloaded -= OnUnloaded;
    }
}
