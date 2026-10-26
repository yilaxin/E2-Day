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

