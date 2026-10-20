using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DailyRecord.Core;

namespace DailyRecord.App;

// Window/account-local cache. It retains small, detached pixels, never PNG payloads.
internal sealed class NoteThumbnailCache(long maximumBytes = 4 * 1024 * 1024, int maximumEntries = 64)
{
    internal sealed record Entry(string Id, string FileName, BitmapSource Pixels)
    {
        internal long PixelBytes => (long)Pixels.PixelWidth * Pixels.PixelHeight * 4;
    }
    private readonly Dictionary<string, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _recent = new();
    internal long PixelBytes { get; private set; }
    internal int Count => _entries.Count;
    internal Entry? Get(string id, Func<string, NoteImage?> load)
    {
        if (_entries.TryGetValue(id, out var cached))
        { _recent.Remove(cached); _recent.AddFirst(cached); return cached.Value; }
        var image = load(id); if (image is null) return null;
        var entry = new Entry(id, image.FileName, DecodeSmall(image));
        if (entry.PixelBytes > maximumBytes || maximumEntries <= 0) return entry;
        while (_entries.Count >= maximumEntries || PixelBytes + entry.PixelBytes > maximumBytes)
        {
            var oldest = _recent.Last!; _recent.RemoveLast(); _entries.Remove(oldest.Value.Id); PixelBytes -= oldest.Value.PixelBytes;
        }
        _entries.Add(id, _recent.AddFirst(entry)); PixelBytes += entry.PixelBytes; return entry;
    }
    internal void Clear() { _entries.Clear(); _recent.Clear(); PixelBytes = 0; }

    private static BitmapSource DecodeSmall(NoteImage image)
    {
        using var stream = new MemoryStream(image.Data, writable: false);
        var header = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None).Frames[0];
        var width = header.PixelWidth; var height = header.PixelHeight; stream.Position = 0;
        var decoded = new BitmapImage(); decoded.BeginInit(); decoded.CacheOption = BitmapCacheOption.OnLoad;
        if (width >= height) decoded.DecodePixelWidth = Math.Min(200, width);
        else decoded.DecodePixelHeight = Math.Min(200, height);
        decoded.StreamSource = stream; decoded.EndInit();
        var converted = new FormatConvertedBitmap(decoded, PixelFormats.Pbgra32, null, 0);
        var stride = converted.PixelWidth * 4; var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        // BitmapImage.StreamSource otherwise retains the encoded image even after the
        // stream is disposed. A pixel copy severs that reference and bounds dimensions.
        var small = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        small.Freeze(); return small;
    }
}

public partial class MainWindow
{
    private readonly NoteThumbnailCache _noteThumbnails = new();
    private long _thumbnailRevision = -1;

    private FrameworkElement NoteGallery(IEnumerable<string> ids)
    {
        if (_thumbnailRevision != _service.Revision)
        { _noteThumbnails.Clear(); _thumbnailRevision = _service.Revision; }
        var panel = new WrapPanel { Margin = new Thickness(0, 9, 0, 0), Tag = "note-gallery" };
        foreach (var id in ids.Distinct())
        {
            var imageId = id;
            var button = Ui.Button("", () =>
            {
                var original = _service.GetNoteImages([imageId]).FirstOrDefault();
                if (original is not null) NoteImages.OpenPreview(this, original);
            });
            button.Padding = new Thickness(3); button.Margin = new Thickness(0, 0, 7, 7);
            button.Width = 100; button.Height = 68; button.Background = Ui.Surface; button.BorderBrush = Ui.Line;
            button.Tag = "note-image:" + imageId;
            try
            {
                var entry = _noteThumbnails.Get(imageId, key => _service.GetNoteImages([key]).FirstOrDefault());
                if (entry is null) continue;
                button.Content = new Image { Source = entry.Pixels, Stretch = Stretch.Uniform };
                button.ToolTip = entry.FileName + " · 点击放大";
                System.Windows.Automation.AutomationProperties.SetName(button, "查看备注图片：" + entry.FileName);
            }
            catch (Exception ex) when (ex is IOException or FileFormatException or NotSupportedException or ArgumentException or COMException)
            { button.Content = Ui.Text("图片无法显示", 10, Ui.Muted); }
            panel.Children.Add(button);
        }
        return panel;
    }
}
