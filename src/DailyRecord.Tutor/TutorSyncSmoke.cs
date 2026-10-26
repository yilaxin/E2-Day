using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DailyRecord.Core;

namespace DailyRecord.App;

internal static partial class TutorSmoke
{
    private static async Task VerifySyncedFeatures(TutorWindow window, TutorService service, TutorStore store, string output)
    {
        window.Width = 1380; window.Height = 1000; window.Navigate(6); Prepare(window);
        var before = JsonSerializer.Serialize(service.State);
        window.HeatmapScalePresenter = dialog =>
        {
            dialog.ShowInTaskbar = false; dialog.Show(); Prepare(dialog);
            Text(dialog, "heatmap-limit:0", "2"); Text(dialog, "heatmap-limit:1", "2");
            Check(!Find<Button>(dialog, "heatmap-range-save").IsEnabled, "重复热力上限没有被禁用");
            Text(dialog, "heatmap-limit:1", "4"); Text(dialog, "heatmap-limit:2", "8");
            Check(Find<Button>(dialog, "heatmap-range-save").IsEnabled, "有效热力上限被拒绝");
            Snapshot(dialog, Path.Combine(output, "tutor-heat-ranges.png")); Click(dialog, "heatmap-range-cancel");
        };
        Click(window, "dashboard-heat-ranges"); Check(before == JsonSerializer.Serialize(service.State), "取消颜色设置改变了数据");
        window.HeatmapScalePresenter = dialog =>
        { dialog.ShowInTaskbar = false; dialog.Show(); Click(dialog, "heatmap-preset-high"); Click(dialog, "heatmap-range-save"); };
        Click(window, "dashboard-heat-ranges"); Check(service.State.HeatmapScale == new TutorHeatmapScale(2, 4, 8), "热力档位没有保存");
        var counts = (window.DashboardReport!.Taught, window.DashboardReport.Minutes);
        var live = Find<Button>(window, "dashboard-day:2026-10-03");
        var applied = service.State.HeatmapScale;
        foreach (var day in window.DashboardReport.Days)
        {
            var cell = Find<Button>(window, "dashboard-day:" + day.Date.ToString("yyyy-MM-dd"));
            Check(((SolidColorBrush)cell.Background).Color == ((SolidColorBrush)TutorWindow.HeatColorForTest(applied.Level(day.Minutes))).Color, "页面颜色没有使用当前档位");
        }
        before = JsonSerializer.Serialize(service.State); var persisted = JsonSerializer.Serialize(store.Load());
        var reportRoot = window.BuildDashboardShareImage();
        var labels = Descendants(reportRoot).OfType<TextBlock>().Select(t => t.Text).ToArray();
        var text = string.Join("\n", labels);
        Check(text.Contains("2026/10/01") && text.Contains("2026/10/15") && text.Contains("匿名"), "分享日期或匿名说明丢失");
        Check(service.State.Students.All(s => !text.Contains(s.Name)) && !text.Contains(store.DirectoryPath), "分享泄露学生姓名或本机路径");
        Check(service.State.Lessons.Where(l => l.PrivateNotes.Length > 0).All(l => !text.Contains(l.PrivateNotes)), "分享泄露内部反馈");
        Check(Descendants(reportRoot).OfType<Border>().Count(b => b.Tag?.ToString()?.StartsWith("share-heat:") == true) == 15, "分享没有按所选日期生成完整热力格");
        foreach (var day in window.DashboardReport!.Days)
        {
            var cell = Descendants(reportRoot).OfType<Border>().Single(b => Equals(b.Tag, "share-heat:" + day.Date.ToString("yyyy-MM-dd")));
            Check(((SolidColorBrush)cell.Background).Color == ((SolidColorBrush)TutorWindow.HeatColorForTest(applied.Level(day.Minutes))).Color, "分享图与页面色阶不一致");
        }
        var bitmap = TutorImage.Render(reportRoot); TutorWindow.RetireTree(reportRoot);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(Path.Combine(output, "tutor-dashboard-share.png"))) encoder.Save(file);
        Check(bitmap.PixelWidth == 1560 && bitmap.PixelHeight > 1000 && bitmap.IsFrozen, "分享图片未完整渲染");
        var attempts = 0; var pngValid = false;
        window.WriteImageClipboard = data =>
        {
            attempts++; if (attempts < 3) throw new ExternalException("isolated clipboard contention");
            var stream = (MemoryStream)data.GetData("PNG")!; pngValid = stream.ToArray().Take(4).SequenceEqual(new byte[] { 137, 80, 78, 71 });
            Check(data.GetDataPresent(DataFormats.Bitmap), "剪贴板缺少位图格式");
        };
        await window.CopyDashboardImageAsync(Find<Button>(window, "dashboard-share"));
        Check(attempts == 3 && pngValid, "图片复制没有重试或 PNG 数据损坏：attempts=" + attempts + ", png=" + pngValid + ", status=" + string.Join("|", Descendants(window).OfType<TextBlock>().Select(t => t.Text).Where(t => t.Contains("复制"))));
        Check(ReferenceEquals(live, Find<Button>(window, "dashboard-day:2026-10-03")) && counts == (window.DashboardReport!.Taught, window.DashboardReport.Minutes), "分享重建页面或改变统计");
        Check(before == JsonSerializer.Serialize(service.State) && persisted == JsonSerializer.Serialize(store.Load()), "分享改变了数据库");
        window.WriteImageClipboard = _ => throw new ExternalException("isolated unavailable clipboard");
        var copyButton = Find<Button>(window, "dashboard-share"); await window.CopyDashboardImageAsync(copyButton); Check(copyButton.IsEnabled, "复制失败后按钮不可重试");
        using (var cancelled = new CancellationTokenSource())
        { cancelled.Cancel(); var writes = 0; try { await TutorImage.CopyAsync(bitmap, _ => writes++, cancelled.Token); } catch (OperationCanceledException) { } Check(writes == 0, "取消后仍写入剪贴板"); }
        Find<DatePicker>(window, "dashboard-from").SelectedDate = window.Today.AddDays(-365).ToDateTime(TimeOnly.MinValue);
        Find<DatePicker>(window, "dashboard-to").SelectedDate = window.Today.ToDateTime(TimeOnly.MinValue); Click(window, "dashboard-apply");
        var yearShare = window.BuildDashboardShareImage();
        Check(Descendants(yearShare).OfType<Border>().Count(b => b.Tag?.ToString()?.StartsWith("share-heat:") == true) == 366, "全年分享丢失日期");
        Check(TutorImage.Render(yearShare).PixelWidth == 1560, "全年分享渲染失败"); TutorWindow.RetireTree(yearShare);
        Click(window, "dashboard-month"); var selectedStudent = service.State.Students[0];
        Find<ComboBox>(window, "dashboard-student").SelectedValue = selectedStudent.Id; Click(window, "dashboard-apply");
        var studentShare = window.BuildDashboardShareImage(); var anonymous = string.Join("|", Descendants(studentShare).OfType<TextBlock>().Select(t => t.Text));
        Check(anonymous.Contains("所选学生（已匿名）") && !anonymous.Contains(selectedStudent.Name), "筛选学生分享未匿名"); TutorWindow.RetireTree(studentShare);
        Find<ComboBox>(window, "dashboard-student").SelectedValue = ""; Click(window, "dashboard-apply");
        window.HeatmapScalePresenter = dialog => { dialog.ShowInTaskbar = false; dialog.Show(); Click(dialog, "heatmap-preset-default"); Click(dialog, "heatmap-range-save"); };
        Click(window, "dashboard-heat-ranges"); window.HeatmapScalePresenter = null;
        window.WriteImageClipboard = data => Clipboard.SetDataObject(data, true);
        window.Navigate(0); Prepare(window); var image = Descendants(window).OfType<Button>().First(b => b.Tag?.ToString()?.StartsWith("note-image:") == true);
        var id = image.Tag.ToString()![11..]; var source = ((Image)image.Content).Source;
        Check(source is BitmapSource pixels && pixels.PixelWidth <= 200 && pixels.PixelHeight <= 200 && pixels is not BitmapImage, "缩略图没有隔离原图像素");
        var oldPreview = NoteImages.OpenPreview; var previewed = false;
        try { NoteImages.OpenPreview = (_, original) => previewed = original.Id == id && original.Data.Length > 0; image.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        finally { NoteImages.OpenPreview = oldPreview; }
        Check(previewed, "点击缩略图没有读取原图");
        Text(window, "todo-title", "尚未提交的备课草稿"); var staleCard = (Border)Descendants(window).OfType<Border>().First(b => b.Tag?.ToString()?.StartsWith("lesson:") == true);
        window.Navigate(5); Check(staleCard.Child is null && image.Content is null, "切页没有断开旧页面内容");
        window.Navigate(0); Check(Find<TextBox>(window, "todo-title").Text == "尚未提交的备课草稿", "资源回收丢失待办草稿");
        Text(window, "todo-title", "");
        Check(ReferenceEquals(source, ((Image)Descendants(window).OfType<Button>().First(b => Equals(b.Tag, "note-image:" + id)).Content).Source), "跨页面未复用缩略图");
        var todo = service.State.Todos[0].Id; service.ToggleTodo(todo); service.ToggleTodo(todo); window.Navigate(0); Prepare(window);
        Check(!ReferenceEquals(source, ((Image)Descendants(window).OfType<Button>().First(b => Equals(b.Tag, "note-image:" + id)).Content).Source), "数据变化后缓存未失效");
        Check(Ui.Page.IsFrozen && Ui.Surface.IsFrozen, "共享主题资源没有冻结");
        Check(Descendants(window).OfType<GlassCardBorder>().Any() && Descendants(window).OfType<GlassCardBorder>().All(b => b.Effect is null), "卡片仍使用整卡阴影缓冲");
        var cache = new NoteThumbnailCache(); var originalImage = store.Images([id]).Single();
        for (var i = 0; i < 90; i++) { var key = i.ToString(); cache.Get(key, _ => new NoteImage { Id = key, FileName = "example.png", Data = originalImage.Data }); }
        Check(cache.Count <= 64 && cache.PixelBytes <= 4 * 1024 * 1024, "缩略图缓存超出上限");
        var raw = DetachedPayload(cache, originalImage); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(!raw.IsAlive, "小缩略图仍持有原始 PNG 数据");
        cache.Clear(); Check(cache.Count == 0 && cache.PixelBytes == 0, "缓存清理不完整");
        File.WriteAllText(Path.Combine(output, "sync-result.txt"), "PASS: colour validation/save/cancel and 15 exact day levels; full anonymous range share; native Bitmap and PNG; clipboard retries/failure/cancellation; live page and persisted records unchanged; lazy original preview; detached old trees; todo draft preservation; vector cards; <=64 thumbnails and <=4 MiB pixel cache.\n");
        await StartupSmokeTest.Run(output);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference DetachedPayload(NoteThumbnailCache cache, NoteImage source)
    {
        var image = new NoteImage { Id = "detached-payload", FileName = source.FileName, Data = source.Data.ToArray() };
        var weak = new WeakReference(image.Data); cache.Get(image.Id, _ => image); return weak;
    }
}
