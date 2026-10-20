using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DailyRecord.App;

internal enum StartupResult { Completed, Skipped, Closed }

internal sealed class StartupWindow : Window
{
    internal const double DurationMilliseconds = 2200;
    private readonly TaskCompletionSource<StartupResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private readonly bool _reducedMotion;
    private bool _finished;
    internal StartupScene Scene { get; } = new();
    internal bool IsAnimating => _timer.IsEnabled;
    internal int FrameCount { get; private set; }
    internal Task<StartupResult> Completion => _completion.Task;

    internal StartupWindow(bool? reducedMotion = null)
    {
        _reducedMotion = reducedMotion ?? !SystemParameters.ClientAreaAnimation;
        Title = "E²-Day · 探索每一天";
        Width = 500; Height = 420; ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false; UseLayoutRounding = true;
        Content = new Border
        {
            CornerRadius = new CornerRadius(24), Background = Ui.Page,
            BorderBrush = new SolidColorBrush(Color.FromArgb(239, 255, 255, 255)), BorderThickness = new Thickness(1), Child = Scene
        };
        AutomationProperties.SetName(this, "E²-Day 开场动画");
        AutomationProperties.SetHelpText(this, "点击任意位置，或按 Esc、Enter、空格立即跳过。");
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += Tick;
        ContentRendered += Start;
        PreviewMouseDown += (_, e) => { e.Handled = true; Finish(StartupResult.Skipped); };
        PreviewTouchDown += (_, e) => { e.Handled = true; Finish(StartupResult.Skipped); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape or Key.Enter or Key.Space) { e.Handled = true; Finish(StartupResult.Skipped); }
        };
        Closed += (_, _) => Finish(StartupResult.Closed);
    }

    internal Task<StartupResult> PlayAsync()
    {
        Show();
        return Completion;
    }

    private void Start(object? sender, EventArgs e)
    {
        ContentRendered -= Start;
        if (_finished) return;
        Scene.SetFrame(_reducedMotion ? 1850 : 0);
        _clock.Start(); _timer.Start(); Focus();
    }

    private void Tick(object? sender, EventArgs e)
    {
        FrameCount++;
        var elapsed = _clock.Elapsed.TotalMilliseconds;
        if (elapsed >= (_reducedMotion ? 200 : DurationMilliseconds)) { Finish(StartupResult.Completed); return; }
        if (!_reducedMotion) Scene.SetFrame(elapsed);
    }

    private void Finish(StartupResult result)
    {
        if (_finished) return;
        _finished = true;
        _timer.Stop(); _timer.Tick -= Tick; _clock.Stop(); ContentRendered -= Start;
        // The opaque opening surface remains above the next window until it is ready;
        // the click that skipped the opening never reaches a task or login button.
        Scene.ShowLoading();
        _completion.TrySetResult(result);
    }
}

internal sealed class StartupScene : FrameworkElement
{
    private static readonly Brush Sky = Freeze(new LinearGradientBrush(Color.FromRgb(72, 170, 237), Color.FromRgb(65, 159, 234), 45));
    private static readonly Brush Ivory = Freeze(new SolidColorBrush(Color.FromRgb(251, 250, 246)));
    private static readonly Brush Sun = Freeze(new SolidColorBrush(Color.FromRgb(255, 206, 98)));
    private static readonly Geometry Road = Shape("M413,564 H849 C899,564 927,591 927,629 V695 Q927,719 903,719 H413 Z");
    private static readonly Geometry RoadGap = Shape("M413,719 C412,670 578,638 714,611 C786,597 828,587 827,578 C823,564 779,555 758,552 C813,552 869,559 875,576 C889,594 812,616 744,635 C645,663 564,690 549,719 Z");
    private static readonly Geometry SunHorizon = Shape("M700,200 H1100 V635 C1005,590 891,548 776,552 Z");
    private static readonly Geometry Power = Shape("M881,343 H939 C939,320 952,306 974,306 C994,306 1006,319 1006,338 C1006,352 998,363 984,376 L881,461 V505 H1068 V453 H967 L1032,397 C1058,374 1072,353 1072,329 C1072,280 1028,254 975,254 C921,254 881,289 881,331 Z");
    private static readonly ImageSource OriginalLogo = LoadLogo();
    private double _milliseconds;
    private bool _loading;

    internal void SetFrame(double milliseconds) { _milliseconds = Math.Clamp(milliseconds, 0, 2200); _loading = false; InvalidateVisual(); }
    internal void ShowLoading() { _loading = true; InvalidateVisual(); }
    private static T Freeze<T>(T value) where T : Freezable { value.Freeze(); return value; }
    private static Geometry Shape(string value) => Freeze(Geometry.Parse(value));
    private static ImageSource LoadLogo() => Freeze(BitmapFrame.Create(new Uri("pack://application:,,,/DailyRecord;component/Assets/e2-day-logo.png"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad));
    private double Phase(double start, double end)
    {
        var t = Math.Clamp((_milliseconds - start) / (end - start), 0, 1);
        return 1 - Math.Pow(1 - t, 3);
    }
    private void Text(DrawingContext context, string text, double size, double y, Brush brush, double alpha = 1, bool bold = false)
    {
        var formatted = new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        context.PushOpacity(alpha); context.DrawText(formatted, new Point((ActualWidth - formatted.Width) / 2, y)); context.Pop();
    }
    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        // Transparent paint supplies a hit target across the entire opening surface.
        context.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var size = Math.Min(220, Math.Min(ActualWidth - 60, ActualHeight * .55));
        if (size <= 0) return;
        var x = (ActualWidth - size) / 2;
        var y = 40.0;
        if (_loading)
        {
            context.DrawImage(OriginalLogo, new Rect(x, y, size, size));
            Text(context, "E²-Day", 27, 283, Ui.Ink, bold: true);
            Text(context, "正在打开…", 14, 330, Ui.Muted);
            return;
        }
        var fade = 1 - Phase(1950, 2200);
        var settle = Phase(1620, 1800);
        var scale = .94 + .06 * Phase(0, 220);
        context.PushOpacity(fade);
        context.PushTransform(new TranslateTransform(x + size * (1 - scale) / 2, y + size * (1 - scale) / 2));
        context.PushTransform(new ScaleTransform(size / 1254 * scale, size / 1254 * scale));
        context.PushOpacity(1 - settle);
        context.PushOpacity(Phase(0, 220));
        context.DrawRoundedRectangle(Sky, null, new Rect(60, 74, 1138, 1110), 210, 210); context.Pop();
        context.PushOpacity(Phase(640, 1220)); context.PushClip(SunHorizon);
        context.DrawEllipse(Sun, null, new Point(890, 510 + 75 * (1 - Phase(640, 1220))), 113, 113);
        context.Pop(); context.Pop();
        context.PushOpacity(Phase(220, 900));
        context.PushClip(new RectangleGeometry(new Rect(350, 490, 640 * Phase(220, 900), 290)));
        context.DrawGeometry(Ivory, null, Road); context.DrawGeometry(Sky, null, RoadGap); context.Pop(); context.Pop();
        context.PushOpacity(Phase(960, 1450));
        context.DrawRoundedRectangle(Ivory, null, new Rect(237, 279, 176, 735), 53, 53);
        context.DrawRoundedRectangle(Ivory, null, new Rect(237, 279, 561, 147), 23, 23);
        context.DrawRoundedRectangle(Ivory, null, new Rect(237, 855, 716, 159), 24, 24); context.Pop();
        context.PushOpacity(Phase(1320, 1620)); context.DrawGeometry(Ivory, null, Power); context.Pop();
        context.Pop();
        // Settle into the actual approved logo, retaining its precise contour and texture.
        context.PushOpacity(settle); context.DrawImage(OriginalLogo, new Rect(0, 0, 1254, 1254)); context.Pop();
        context.Pop(); context.Pop();
        Text(context, "E²-Day", 27, 283, Ui.Ink, Phase(1650, 1900), bold: true);
        Text(context, "探索每一天", 15, 327, Ui.Muted, Phase(1650, 1900));
        context.Pop();
        Text(context, "点击任意位置跳过", 12, ActualHeight - 35, Ui.Muted);
    }
}
