using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DailyRecord.Core;

namespace DailyRecord.App;

internal static partial class SmokeTest
{
    private static async Task VerifyMemoryResources(MainWindow window, TaskService service, string output)
    {
        static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        var cache = new NoteThumbnailCache(320000, 2);
        var originals = new[] { MemoryImage(200, 200, 10), MemoryImage(200, 200, 20), MemoryImage(200, 200, 30) };
        var reads = 0;
        NoteImage? Load(string key) { reads++; return originals.Single(i => i.Id == key); }
        var first = cache.Get(originals[0].Id, Load)!; var second = cache.Get(originals[1].Id, Load)!;
        Check(ReferenceEquals(first.Pixels, cache.Get(first.Id, Load)!.Pixels) && reads == 2, "同一备注图重复读取或解码。");
        cache.Get(originals[2].Id, Load); Check(cache.Count == 2 && cache.PixelBytes <= 320000, "缩略图缓存超过上限。");
        Check(ReferenceEquals(first.Pixels, cache.Get(first.Id, Load)!.Pixels) && reads == 3, "最近使用的缩略图被错误淘汰。");
        cache.Get(second.Id, Load); Check(reads == 4, "最旧缩略图没有被淘汰。");
        var pixels = new byte[first.Pixels.PixelWidth * first.Pixels.PixelHeight * 4]; first.Pixels.CopyPixels(pixels, first.Pixels.PixelWidth * 4, 0);
        Check(first.Pixels.IsFrozen && pixels[0] == 10 && pixels[3] == 255, "缩略图像素或冻结状态不正确。");
        var encoded = LoadDetachedThumbnail(cache);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(!encoded.IsAlive, "缩略图仍保留了整张 PNG 数据。");
        var portrait = MemoryImage(32, 2048, 40);
        var small = cache.Get(portrait.Id, _ => portrait)!;
        Check(small.Pixels.PixelHeight <= 200 && small.Pixels.PixelWidth <= 200, "长图缩略图超出尺寸上限。");
        cache.Clear(); Check(cache.Count == 0 && cache.PixelBytes == 0, "缓存未清空。");
        var backup = Path.Combine(output, "memory-ui-baseline.json"); service.ExportBackup(backup);
        var originalPreview = NoteImages.OpenPreview;
        try
        {
            var image = originals[0];
            var task = service.AddTask("缩略图按需读取验证", "", service.Today, images: [image]);
            window.Navigate(PageKind.Today); Prepare(window);
            var firstSource = (BitmapSource)((Image)FindButton(window, "note-image:" + image.Id).Content).Source;
            window.Navigate(PageKind.History); Prepare(window);
            var historySource = (BitmapSource)((Image)FindButton(window, "note-image:" + image.Id).Content).Source;
            Check(ReferenceEquals(firstSource, historySource), "跨页重复图片未复用缩略图。");
            var previewed = false;
            NoteImages.OpenPreview = (_, selected) => previewed = selected.Id == image.Id && selected.Data.SequenceEqual(image.Data) && NoteImages.Decode(selected).PixelWidth == 200;
            FindButton(window, "note-image:" + image.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(previewed, "点击缩略图未按需读取完整原图。");
            service.ToggleImportant(task); window.Navigate(PageKind.Today); Prepare(window);
            var updated = (BitmapSource)((Image)FindButton(window, "note-image:" + image.Id).Content).Source;
            Check(!ReferenceEquals(firstSource, updated), "数据变动后缓存未失效。");
            service.AddEvent("选择器释放验证", "", service.Today, EventImportance.Normal, RepeatRule.Daily);
            var stale = LeaveAnalytics(window);
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Check(!stale.IsAlive, "离开数据概览后仍保留旧选择器。");
            Check(Descendants(window).OfType<GlassCardBorder>().Any() && Descendants(window).OfType<GlassCardBorder>().All(c => c.Effect is null), "玻璃卡片仍使用整卡阴影缓冲。");
            File.WriteAllText(Path.Combine(output, "memory-resources-result.txt"), "PASS: bounded LRU pixel cache; shared thumbnail identity; source PNG collectible; portrait dimension cap; pixel fidelity; full original read on click; data revision invalidation; inactive analytics picker collectible; vector glass cards; original image bytes and backup preserved. Explicit GC only in isolated diagnostics.");
        }
        finally
        {
            NoteImages.OpenPreview = originalPreview; service.RestoreBackup(backup); File.Delete(backup); window.Navigate(PageKind.Today);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LeaveAnalytics(MainWindow window)
    {
        window.Navigate(PageKind.Analytics); Prepare(window);
        var picker = Descendants(window).OfType<ComboBox>().Single();
        var reference = new WeakReference(picker);
        window.Navigate(PageKind.Today); Prepare(window); return reference;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LoadDetachedThumbnail(NoteThumbnailCache cache)
    {
        var image = MemoryImage(400, 300, 60); var reference = new WeakReference(image.Data);
        cache.Get(image.Id, _ => image); return reference;
    }

    private static NoteImage MemoryImage(int width, int height, byte value)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = value; pixels[i + 1] = 80; pixels[i + 2] = 120; pixels[i + 3] = 255; }
        return NoteImages.Normalize(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4), "隔离图片.png");
    }
}
