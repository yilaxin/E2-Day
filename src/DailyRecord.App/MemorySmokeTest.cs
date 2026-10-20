using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DailyRecord.Core;

namespace DailyRecord.App;

internal static partial class SmokeTest
{
    internal static async Task MeasureMemory(MainWindow window, TaskService service, PreviewClock clock, string output)
    {
        Directory.CreateDirectory(output);
        for (var i = 0; i < 24; i++) service.State.Tasks.Add(new TaskItem
        {
            Title = $"内存测试事项 {i + 1:00}", ScheduledDate = clock.Today, OriginalScheduledDate = clock.Today,
            CreatedAt = clock.Now, State = TaskState.Completed, CompletedOn = clock.Today
        });
        service.State.Events.Add(new EventDefinition { Title = "隔离的每日任务", StartDate = clock.Today, Repeat = RepeatRule.Daily });
        service.Save();
        window.Width = 1400; window.Height = 940; window.ShowInTaskbar = false; window.Show();
        var rows = new List<object>();
        async Task Record(string phase, PageKind page)
        {
            var watch = Stopwatch.StartNew(); window.Navigate(page);
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            var navigation = watch.Elapsed.TotalMilliseconds;
            // Collections are only for this isolated diagnostic, never normal app use.
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            await Task.Delay(140);
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            using var process = Process.GetCurrentProcess(); process.Refresh();
            rows.Add(new { phase, page = page.ToString(), managedMiB = GC.GetTotalMemory(false) / 1048576d,
                privateMiB = process.PrivateMemorySize64 / 1048576d, workingMiB = process.WorkingSet64 / 1048576d,
                navigationMs = navigation, thumbnails = Descendants(window).OfType<Button>().Count(b => b.Tag is string s && s.StartsWith("note-image:")) });
        }
        foreach (var page in new[] { PageKind.Today, PageKind.Year, PageKind.Analytics, PageKind.History }) await Record("plain", page);
        var bytes = SeedMemoryImages(service, clock);
        foreach (var page in new[] { PageKind.Today, PageKind.History, PageKind.Analytics }) await Record("images", page);
        for (var round = 0; round < 3; round++)
            foreach (var page in new[] { PageKind.Today, PageKind.History, PageKind.Analytics, PageKind.Year }) await Record("cycle-" + round, page);
        var report = new { version = typeof(MainWindow).Assembly.GetName().Version?.ToString(),
            architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            fixture = "24 completed tasks and 24 activity snapshots reuse one deterministic 1536x1024 PNG; separate fresh data directory; no system clipboard", imageBytes = bytes, samples = rows };
        File.WriteAllText(Path.Combine(output, "memory-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        window.Close();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int SeedMemoryImages(TaskService service, PreviewClock clock)
    {
        var pixels = new byte[1536 * 1024 * 4]; new Random(314159).NextBytes(pixels);
        for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        var bitmap = BitmapSource.Create(1536, 1024, 96, 96, PixelFormats.Bgra32, null, pixels, 1536 * 4);
        var image = NoteImages.Normalize(bitmap, "隔离内存样例.png");
        foreach (var task in service.State.Tasks.Where(t => !t.IsEvent))
        {
            task.ImageIds.Add(image.Id);
            service.State.Activities.Add(new ActivityRecord { TaskId = task.Id, Title = task.Title,
                Kind = DailyRecord.Core.ActivityKind.Completed, Day = clock.Today, At = clock.Now, ScheduledDate = clock.Today, ImageIds = [image.Id] });
        }
        service.Save([image]); return image.Data.Length;
    }
}
