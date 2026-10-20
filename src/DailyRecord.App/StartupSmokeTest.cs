using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DailyRecord.Core;

namespace DailyRecord.App;

internal static class StartupSmokeTest
{
    private static MainWindow Offscreen() => new()
    {
        WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000,
        ShowActivated = false, ShowInTaskbar = false
    };
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException("开场动画：" + message); }
    private static async Task<StartupResult> Result(StartupWindow window) => await window.Completion.WaitAsync(TimeSpan.FromSeconds(6));
    internal static async Task Run(string outputDirectory)
    {
        foreach (var point in new[] { new Point(.01, .5), new Point(.5, .25), new Point(.5, .5), new Point(.99, .99) })
        {
            var host = Offscreen(); using var window = new StartupWindow(host, false);
            try
            {
                _ = window.PlayAsync();
                await host.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
                host.UpdateLayout();
                Check(host.ActualWidth >= 1000 && host.ActualHeight >= 650, "第一帧未使用正常大小的主窗口。");
                var hit = window.Scene.InputHitTest(new Point(window.Scene.ActualWidth * point.X, window.Scene.ActualHeight * point.Y)) as UIElement;
                Check(hit is not null, "动画边缘、图形或底部留白缺少点击目标。");
                var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent };
                hit!.RaiseEvent(click);
                Check(click.Handled && await Result(window) == StartupResult.Skipped, "点击没有立即跳过并拦截事件。");
                Check(!window.IsAnimating, "跳过后计时器仍在运行。");
                var count = window.FrameCount; await Task.Delay(70);
                Check(window.FrameCount == count, "结束后仍在绘制动画帧。");
            }
            finally { host.Close(); }
        }
        foreach (var key in new[] { Key.Escape, Key.Enter, Key.Space })
        {
            var host = Offscreen(); using var window = new StartupWindow(host, false);
            try
            {
                _ = window.PlayAsync();
                var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(host), Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                host.RaiseEvent(input);
                Check(input.Handled && await Result(window) == StartupResult.Skipped && !window.IsAnimating, "键盘跳过未结束动画。");
            }
            finally { host.Close(); }
        }
        var naturalHost = Offscreen(); using var natural = new StartupWindow(naturalHost, false);
        try
        {
            var watch = Stopwatch.StartNew(); _ = natural.PlayAsync();
            var handle = new WindowInteropHelper(naturalHost).Handle;
            naturalHost.Width = 1120; naturalHost.Height = 740;
            Check(await Result(natural) == StartupResult.Completed, "动画未自动结束。");
            Check(watch.Elapsed.TotalMilliseconds >= 3100 && watch.Elapsed.TotalSeconds < 6 && !natural.IsAnimating, "3.2 秒自动结束时长或清理异常。");
            natural.RestoreContent(); naturalHost.UpdateLayout();
            Check(new WindowInteropHelper(naturalHost).Handle == handle && naturalHost.Width == 1120 && naturalHost.Height == 740 && naturalHost.Content is Grid,
                "开场结束替换了窗口或重置了用户调整的尺寸。");
        }
        finally { naturalHost.Close(); }
        var reducedHost = Offscreen(); using var reduced = new StartupWindow(reducedHost, true);
        try
        {
            var watch = Stopwatch.StartNew(); _ = reduced.PlayAsync();
            Check(await Result(reduced) == StartupResult.Completed && watch.Elapsed.TotalMilliseconds < 1500 && !reduced.IsAnimating, "减少动态效果没有快速结束静态品牌画面。");
            reducedHost.WindowState = WindowState.Maximized;
            reduced.RestoreContent();
            Check(reducedHost.WindowState == WindowState.Maximized, "切换内容丢失最大化状态。");
        }
        finally { reducedHost.Close(); }
        var closedHost = Offscreen(); using var closed = new StartupWindow(closedHost, false); _ = closed.PlayAsync(); closedHost.Close();
        Check(await Result(closed) == StartupResult.Closed && !closed.IsAnimating, "关闭窗口没有释放动画。");
        await EmbeddedLogin(outputDirectory);
        var scene = new StartupScene();
        foreach (var milliseconds in new[] { 320, 1020, 1600, 2100, 2690 })
        {
            scene.SetFrame(milliseconds); scene.Measure(new Size(1360, 900)); scene.Arrange(new Rect(0, 0, 1360, 900)); scene.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1360, 900, 96, 96, PixelFormats.Pbgra32);
            var background = new DrawingVisual(); using (var drawing = background.RenderOpen()) drawing.DrawRectangle(Ui.Page, null, new Rect(0, 0, 1360, 900));
            bitmap.Render(background); bitmap.Render(scene);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(outputDirectory, $"startup-{milliseconds}.png")); encoder.Save(stream);
        }
        File.WriteAllText(Path.Combine(outputDirectory, "startup-result.txt"),
            "PASS: 3.2-second automatic opening in full-size main HWND; first-frame edge/logo/blank-area click skip with consumed input; Esc/Enter/Space; resize/maximize retained; embedded login success/close; repeated launches; reduced-motion static opening; close/skip/completion stop timer and frames; five isolated large-window storyboard renders. No personal data used.\n");
    }

    private static async Task EmbeddedLogin(string outputDirectory)
    {
        var vault = new AccountVault(Path.Combine(outputDirectory, "startup-login-fixture", Guid.NewGuid().ToString("N")));
        vault.Create("开场示例账号", "example-password");
        var host = Offscreen(); using var opening = new StartupWindow(host, true);
        try
        {
            _ = opening.PlayAsync(); await Result(opening);
            var handle = new WindowInteropHelper(host).Handle;
            var login = new AccountWindow(vault);
            var authenticated = login.ShowInHostAsync(host); host.UpdateLayout();
            T Field<T>(string tag) where T : FrameworkElement => Descendants((DependencyObject)host.Content).OfType<T>().Single(control => Equals(control.Tag, tag));
            Field<PasswordBox>("account-password").Password = "wrong-password";
            Field<Button>("account-next").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!authenticated.IsCompleted, "密码错误却离开登录页面。");
            Field<PasswordBox>("account-password").Password = "example-password";
            Field<Button>("account-next").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(await authenticated.WaitAsync(TimeSpan.FromSeconds(5)) && login.Session is not null, "主窗口内登录未完成。");
            opening.RestoreContent();
            Check(host.IsVisible && new WindowInteropHelper(host).Handle == handle, "登录后主窗口被替换或关闭。");
            var closingLogin = new AccountWindow(vault);
            var cancelled = closingLogin.ShowInHostAsync(host); host.Close();
            Check(!await cancelled.WaitAsync(TimeSpan.FromSeconds(5)), "关闭登录页没有结束启动流程。");
        }
        finally { host.Close(); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
