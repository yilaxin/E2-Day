using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DailyRecord.Core;

namespace DailyRecord.App;

internal static class MobileAssets
{
    public static void Generate(string directory)
    {
        Directory.CreateDirectory(directory);
        var logo = new BitmapImage(new Uri("pack://application:,,,/Assets/e2-day-logo.png"));
        foreach (var size in new[] { 192, 512 })
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(new SolidColorBrush(Color.FromRgb(33, 111, 166)), null, new Rect(0, 0, size, size));
                var scale = Math.Min(size * .78 / logo.PixelWidth, size * .78 / logo.PixelHeight);
                var width = logo.PixelWidth * scale; var height = logo.PixelHeight * scale;
                context.DrawImage(logo, new Rect((size - width) / 2, (size - height) / 2, width, height));
            }
            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(directory, $"icon-{size}.png")); encoder.Save(file);
        }
        var lunar = new Dictionary<string, List<string>>();
        for (var month = 1; month <= 12; month++) for (var day = 1; day <= 30; day++)
        {
            var dates = new List<string>(); for (var year = 1901; year <= 2100; year++)
                if (LunarDates.ToSolar(year, month, day) is { } solar) dates.Add(solar.ToString("yyyy-MM-dd"));
            lunar[$"{month}-{day}"] = dates;
        }
        File.WriteAllText(Path.Combine(directory, "lunar-dates.json"), JsonSerializer.Serialize(lunar));
    }
}
