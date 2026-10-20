using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DailyRecord.App;

internal static class StartupSmokeTest
{
    private static StartupWindow Offscreen(bool reduced = false) => new(reduced)
    {
        WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000,
        ShowActivated = false, ShowInTaskbar = false
    };
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException("开场动画：" + message); }
    private static async Task<StartupResult> Result(StartupWindow window) => await window.Completion.WaitAsync(TimeSpan.FromSeconds(6));
    internal static async Task Run(string outputDirectory)
    {
        foreach (var point in new[] { new Point(4, 210), new Point(250, 100), new Point(250, 210), new Point(250, 390) })
        {
            var window = Offscreen();
            try
            {
                _ = window.PlayAsync();
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
                window.UpdateLayout();
                var hit = window.InputHitTest(point) as UIElement;
                Check(hit is not null, "动画边缘、图形或底部留白缺少点击目标。");
                var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent };
                hit!.RaiseEvent(click);
                Check(click.Handled && await Result(window) == StartupResult.Skipped, "点击没有立即跳过并拦截事件。");
                Check(!window.IsAnimating, "跳过后计时器仍在运行。");
                var count = window.FrameCount; await Task.Delay(70);
                Check(window.FrameCount == count, "结束后仍在绘制动画帧。");
            }
            finally { window.Close(); }
        }
        foreach (var key in new[] { Key.Escape, Key.Enter, Key.Space })
        {
            var window = Offscreen();
            try
            {
                _ = window.PlayAsync();
                var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                window.RaiseEvent(input);
                Check(input.Handled && await Result(window) == StartupResult.Skipped && !window.IsAnimating, "键盘跳过未结束动画。");
            }
            finally { window.Close(); }
        }
        var natural = Offscreen();
        try
        {
            var watch = Stopwatch.StartNew(); _ = natural.PlayAsync();
            Check(await Result(natural) == StartupResult.Completed, "动画未自动结束。");
            Check(watch.Elapsed.TotalMilliseconds >= 2100 && watch.Elapsed.TotalSeconds < 6 && !natural.IsAnimating, "自动结束时长或清理异常。");
        }
        finally { natural.Close(); }
        var reduced = Offscreen(true);
        try
        {
            var watch = Stopwatch.StartNew(); _ = reduced.PlayAsync();
            Check(await Result(reduced) == StartupResult.Completed && watch.Elapsed.TotalMilliseconds < 1500 && !reduced.IsAnimating, "减少动态效果没有快速结束静态品牌画面。");
        }
        finally { reduced.Close(); }
        var closed = Offscreen(); _ = closed.PlayAsync(); closed.Close();
        Check(await Result(closed) == StartupResult.Closed && !closed.IsAnimating, "关闭窗口没有释放动画。");
        var scene = new StartupScene();
        foreach (var milliseconds in new[] { 220, 700, 1100, 1450, 1850 })
        {
            scene.SetFrame(milliseconds); scene.Measure(new Size(500, 420)); scene.Arrange(new Rect(0, 0, 500, 420)); scene.UpdateLayout();
            var bitmap = new RenderTargetBitmap(500, 420, 96, 96, PixelFormats.Pbgra32);
            var background = new DrawingVisual(); using (var drawing = background.RenderOpen()) drawing.DrawRectangle(Ui.Page, null, new Rect(0, 0, 500, 420));
            bitmap.Render(background); bitmap.Render(scene);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(outputDirectory, $"startup-{milliseconds}.png")); encoder.Save(stream);
        }
        File.WriteAllText(Path.Combine(outputDirectory, "startup-result.txt"),
            "PASS: 2.2-second automatic opening; first-frame edge/logo/blank-area click skip with consumed input; Esc/Enter/Space; repeated launches; reduced-motion static opening; close/skip/completion stop timer and frames; five isolated storyboard renders. No personal data used.\n");
    }
}
