using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DailyRecord.Core;

namespace DailyRecord.App;

internal static partial class SmokeTest
{
    private static async Task VerifyAnalyticsShare(MainWindow window, TaskService service, PreviewClock clock, string output)
    {
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var clipboard = window.ImageClipboard; var today = clock.Today;
        var state = StateJson.Serialize(service.State); var revision = service.Revision;
        var pixelsBefore = ((FrameworkElement)window.Content).ActualWidth;
        try
        {
            Click(window, "analytics-year-next"); Click(window, "analytics-habit-next");
            var names = new[] { "7d", "30d", "month", "year" };
            for (var period = 0; period < 4; period++)
            {
                Click(window, "analytics-range:" + period);
                var from = period switch { 0 => today.AddDays(-6), 1 => today.AddDays(-29), 2 => new DateOnly(today.Year, today.Month, 1), _ => new DateOnly(today.Year, 1, 1) };
                var root = window.BuildAnalyticsShareImage(); var texts = Descendants(root).OfType<TextBlock>().ToArray();
                var range = texts.Single(t => Equals(t.Tag, "analytics-share-range")).Text;
                Check(range.Contains(from.ToString("yyyy/MM/dd")) && range.Contains(today.ToString("yyyy/MM/dd")), "分享时间范围错误。");
                var expected = service.State.Tasks.Count(t => t.State == TaskState.Completed && t.CompletedOn >= from && t.CompletedOn <= today);
                Check(texts.Single(t => Equals(t.Tag, "analytics-total")).Text == expected.ToString(), "分享统计没有使用所选范围。");
                var heat = Descendants(root).OfType<Border>().Where(b => b.Tag is string tag && tag.StartsWith("analytics-share-heat:")).ToArray();
                var habit = Descendants(root).OfType<Border>().Where(b => b.Tag is string tag && tag.StartsWith("analytics-share-habit:")).ToArray();
                Check(heat.Length == today.DayNumber - from.DayNumber + 1 && habit.Length == heat.Length, "分享热力图／打卡图未与所选范围对齐。");
                Check(heat.All(b => DateOnly.Parse(b.Tag.ToString()!["analytics-share-heat:".Length..]) >= from && DateOnly.Parse(b.Tag.ToString()!["analytics-share-heat:".Length..]) <= today), "分享图包含范围外的日期。");
                Check(texts.All(t => !t.Text.Contains("整理研究笔记") && !t.Text.Contains("回看并重新安排") && !t.Text.Contains("未来提醒不计入积压")), "统计分享泄露了任务清单。");
                Check(!Descendants(root).OfType<Image>().Any() && !Descendants(root).OfType<ComboBox>().Any(), "分享图包含备注图片或页面选择控件。");
                Check(texts.Any(t => t.Text.Contains($"截至 {today:yyyy/M/d}")), "当前积压没有独立标明日期。");
                var bitmap = TaskShareImage.Render(root, MainWindow.AnalyticsShareWidth);
                Check(bitmap.IsFrozen && bitmap.PixelWidth == 1560, "分享图尺寸或冻结状态错误。");
                var footer = texts.Single(t => Equals(t.Tag, "analytics-share-footer"));
                Check(footer.TransformToAncestor(root).TransformBounds(new Rect(footer.RenderSize)).Bottom <= bitmap.Height, "分享图底部被裁切。");
                var pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0); Check(pixel[3] == 255, "分享图片背景没有铺满。");
                SaveBitmap(bitmap, Path.Combine(output, "analytics-share-" + names[period] + ".png"));
            }
            Click(window, "analytics-range:1");
            var expectedBitmap = TaskShareImage.Render(window.BuildAnalyticsShareImage(), MainWindow.AnalyticsShareWidth);
            DataObject? recorded = null; var attempts = 0; byte[]? firstAttemptPng = null;
            window.ImageClipboard = new(data =>
            {
                firstAttemptPng ??= ((MemoryStream)data.GetData("PNG")!).ToArray();
                if (++attempts < 3) throw new COMException("Clipboard busy", unchecked((int)0x800401D0)); recorded = data;
            });
            var copy = FindButton(window, "copy-analytics-image"); Click(window, "copy-analytics-image");
            Check(!copy.IsEnabled, "复制等待期间按钮没有禁用。");
            Click(window, "analytics-range:0");
            var otherRangeBitmap = TaskShareImage.Render(window.BuildAnalyticsShareImage(), MainWindow.AnalyticsShareWidth);
            for (var i = 0; i < 60 && !copy.IsEnabled; i++) await Task.Delay(25);
            Check(attempts == 3 && copy.IsEnabled && Equals(copy.Content, "✓ 已复制图片"), "复制重试或成功反馈错误。");
            Check(recorded is not null && recorded.ContainsImage() && recorded.GetDataPresent("PNG"), "复制没有同时提供原生图片和PNG。");
            var png = recorded!.GetData("PNG") as MemoryStream ?? throw new InvalidOperationException("剪贴板PNG缺失。");
            using var stream = new MemoryStream(png.ToArray());
            var decoded = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            Check(decoded.PixelWidth == expectedBitmap.PixelWidth && decoded.PixelHeight == expectedBitmap.PixelHeight, "剪贴板图像尺寸改变。");
            Check(decoded.PixelHeight != otherRangeBitmap.PixelHeight && firstAttemptPng!.SequenceEqual(png.ToArray()), "等待复制时切换范围改动了已生成的报告。");
            clock.Today = new DateOnly(2028, 3, 1); window.Navigate(PageKind.Analytics); Click(window, "analytics-range:1");
            var leap = window.BuildAnalyticsShareImage();
            Check(Descendants(leap).OfType<Border>().Any(b => Equals(b.Tag, "analytics-share-heat:2028-02-29")), "分享遗漏闰日。");
            clock.Today = new DateOnly(2027, 1, 5); window.Navigate(PageKind.Analytics); Click(window, "analytics-range:1");
            var crossYear = window.BuildAnalyticsShareImage();
            Check(Descendants(crossYear).OfType<Border>().Any(b => Equals(b.Tag, "analytics-share-heat:2026-12-31")) &&
                Descendants(crossYear).OfType<Border>().Any(b => Equals(b.Tag, "analytics-share-heat:2027-01-01")), "分享遗漏跨年日期。");
            Check(state == StateJson.Serialize(service.State) && revision == service.Revision, "分享改变了任务数据或服务修订号。");
            Check(((FrameworkElement)window.Content).ActualWidth == pixelsBefore, "分享改变了页面布局宽度。");
            File.WriteAllText(Path.Combine(output, "analytics-share-result.txt"), "PASS: four ranges and date-aligned heat/habit charts; current backlog date; selected-habit summary; no task lists, note images or picker controls; opaque complete PNG; native bitmap + PNG clipboard; busy retry and feedback; range change during copy preserves the captured report; leap/cross-year dates; read-only data and live layout. Tests use a recording clipboard and do not replace the system clipboard.\n");
        }
        finally
        {
            window.ImageClipboard = clipboard; clock.Today = today; window.Navigate(PageKind.Analytics);
            Click(window, "analytics-range:1"); Click(window, "analytics-year-previous"); Click(window, "analytics-habit-previous");
        }
    }
}
