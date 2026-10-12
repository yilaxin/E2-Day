using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DailyRecord.Core;

namespace DailyRecord.App;

internal static partial class SmokeTest
{
    public static async Task Run(MainWindow window, TaskService service, HolidayService holidays, PreviewClock clock, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        if (service.State.Tasks.Count == 0)
        {
            service.AddTask("完成项目的第一版需求整理", "把想法变成可以执行的下一步。", service.Today, true);
            service.AddTask("阅读一篇 AI 研究文章", "记录三个值得继续思考的问题。", service.Today);
            service.AddTask("散步 30 分钟", "给自己留一点休息时间。", service.Today);
            var finished = service.AddTask("整理昨日的笔记", "", service.Today.AddDays(-1));
            service.ToggleCompleted(finished);
            var upcoming = service.AddEvent("提交材料", "先检查附件是否齐全。", service.Today.AddDays(3), EventImportance.VeryImportant, RepeatRule.None);
            service.ToggleImportant(service.State.Tasks.Single(t => t.EventId == upcoming.Id));
            var viewed = service.AddEvent("预约年度体检", "确认时间后提前安排出行。", service.Today.AddDays(2), EventImportance.Important, RepeatRule.None);
            service.ToggleViewed(service.State.Tasks.Single(t => t.EventId == viewed.Id));
            service.AddEvent("每周整理资料", "", service.Today, EventImportance.Normal, RepeatRule.Weekly);
            service.State.News = new NewsCache
            {
                FetchedAt = new PreviewClock().Now,
                Articles = [new NewsArticle
                {
                    Title = "示例：关注模型、产品与研究的新进展", Summary = "联网后将使用官方 RSS 的真实标题和简介，标示来源、语言与发布时间。",
                    Source = "资讯展示示例", Language = "中文", PublishedAt = new PreviewClock().Now,
                    Url = "https://deepmind.google/blog/"
                }]
            };
            service.Save();
        }
        window.Width = 1400; window.Height = 980;
        window.Navigate(PageKind.Today);
        Prepare(window);
        var testTask = service.State.Tasks.First(t => !t.IsEvent && t.State == TaskState.Open && !t.IsImportant);
        Click(window, "important:" + testTask.Id);
        if (!testTask.IsImportant) throw new InvalidOperationException("重要按钮没有更新任务。");
        Click(window, "important:" + testTask.Id);
        if (testTask.IsImportant) throw new InvalidOperationException("取消重要按钮没有更新任务。");
        Click(window, "complete:" + testTask.Id);
        if (testTask.State != TaskState.Completed || testTask.CompletedOn != service.Today)
            throw new InvalidOperationException("完成按钮没有正确记录日期。");
        Click(window, "complete:" + testTask.Id);
        if (testTask.State != TaskState.Open) throw new InvalidOperationException("撤销完成按钮未恢复任务。");
        var reminder = service.State.Tasks.First(t => t.IsEvent && t.State == TaskState.Open && !service.IsViewed(t, service.Today));
        Click(window, "viewed:" + reminder.Id);
        if (!service.IsViewed(reminder, service.Today) || reminder.State != TaskState.Open)
            throw new InvalidOperationException("看过按钮混淆了查看与完成。");
        Click(window, "viewed:" + reminder.Id);
        if (service.IsViewed(reminder, service.Today)) throw new InvalidOperationException("撤销查看未生效。");

        MeasureCalendarNavigation(window, outputDirectory);
        VerifyCalendarCache(window, service, clock, outputDirectory);
        VerifyMonthView(window, service, outputDirectory);
        await VerifyTaskShare(window, service, holidays, clock, outputDirectory);
        await VerifyNoteImages(window, service, holidays, outputDirectory);
        VerifyLongTermTasks(window, service, clock, outputDirectory);

        foreach (var page in Enum.GetValues<PageKind>())
        {
            window.Navigate(page); Prepare(window);
            Render((FrameworkElement)window.Content, 1400, 940, Path.Combine(outputDirectory, page.ToString().ToLowerInvariant() + ".png"));
        }
        var editor = new EditDialog(window, service.Today, true);
        var editorRoot = (FrameworkElement)editor.Content;
        Render(editorRoot, 560, 850, Path.Combine(outputDirectory, "event-editor.png"));
        File.WriteAllText(Path.Combine(outputDirectory, "result.txt"),
            "PASS: WPF root layout; 4 page renders; event editor render; mark/unmark important; complete/undo; viewed/undo; persisted state; calendar control reuse, task changes, completion, rescheduling, cancellation, backup restore, day rollover and leap-year switching; month zoom, task previews, completion fading, overflow, month/year navigation and return to overview; task share image, empty/long lists, filtering, native bitmap + PNG data, copy button and clipboard contention retries; note image upload callback, PNG/JPEG import, orientation, scaling, thumbnails, preview, draft isolation, removal, invalid files, size/count limits and history images.\nScreenshots use explicit preview data in an isolated data directory. Clipboard tests use a recording backend and leave the system clipboard untouched. Image picker tests inject file paths and do not open the system dialog.\nRuntime: " + System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory());
    }

    private static async Task VerifyNoteImages(MainWindow window, TaskService service, HolidayService holidays, string outputDirectory)
    {
        void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
        Button EditorButton(DependencyObject editor, string tag) => Descendants(editor).OfType<Button>().Single(b => Equals(b.Tag, tag));
        var baseline = Path.Combine(outputDirectory, "note-images-baseline.json"); service.ExportBackup(baseline);
        var sourcePath = Path.Combine(outputDirectory, "备注上传示例.png");
        var invalidPath = Path.Combine(outputDirectory, "invalid-image.txt");
        var largePath = Path.Combine(outputDirectory, "oversized-image.png");
        var jpegPath = Path.Combine(outputDirectory, "orientation.jpg");
        var previewAction = NoteImages.OpenPreview;
        try
        {
            var reference = Ui.Card(Ui.Stack(Ui.Text("项目参考笔记", 30, Ui.Accent, true),
                Ui.Text("示例图片 · 任务备注", 15, Ui.Muted), Ui.Text("\n1. 需求整理\n\n2. 界面验证\n\n3. 发布检查", 21)), new Thickness(32));
            reference.Margin = new Thickness(16); Render(reference, 680, 370, sourcePath);
            var task = service.AddTask("将参考截图附在任务备注里", "文字和图片可以一起记录。", service.Today, true);
            var editor = new EditDialog(window, service.Today, false, task);
            var beforeDraft = StateJson.Serialize(service.State);
            editor.ImageEditor.SelectFiles = () => [sourcePath];
            EditorButton(editor, "upload-note-images").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await editor.ImageEditor.PendingImport;
            Check(editor.Images.Count == 1, "上传按钮未添加图片。");
            Check(StateJson.Serialize(service.State) == beforeDraft, "编辑草稿改动了保存的数据。");
            var image = editor.Images.Single();
            Check(NoteImages.Decode(image).IsFrozen, "图片未完成加载并释放文件。");
            await editor.ImageEditor.AddFilesAsync([sourcePath]); Check(editor.Images.Count == 1, "重复上传了同一图片。");
            Render((FrameworkElement)editor.Content, 560, 800, Path.Combine(outputDirectory, "note-image-editor.png"));
            File.WriteAllText(invalidPath, "这不是一张图片"); await editor.ImageEditor.AddFilesAsync([invalidPath]);
            Check(editor.Images.Count == 1, "损坏图片改变了草稿。");
            using (var oversized = File.Create(largePath)) oversized.SetLength(20_000_001);
            await editor.ImageEditor.AddFilesAsync([largePath]); Check(editor.Images.Count == 1, "超大文件改变了草稿。");
            await editor.ImageEditor.AddFilesAsync(Enumerable.Repeat(sourcePath, 9).ToArray()); Check(editor.Images.Count == 1, "过多文件改变了草稿。");
            service.EditTask(task, task.Title, task.Notes, task.ScheduledDate, task.IsImportant, editor.Images);
            File.Delete(sourcePath);
            Check(service.GetNoteImages(task.ImageIds).Single().Data.SequenceEqual(image.Data), "删除原文件后备注图片丢失。");
            window.Navigate(PageKind.Today); Prepare(window);
            var previewed = false; NoteImages.OpenPreview = (_, selected) => previewed = selected.Id == image.Id;
            Click(window, "note-image:" + image.Id); Check(previewed, "缩略图没有打开对应的图片。");
            Render((FrameworkElement)window.Content, 1400, 940, Path.Combine(outputDirectory, "note-image-task.png"));
            var preview = NoteImages.CreatePreview(window, image);
            Render((FrameworkElement)preview.Content, 904, 660, Path.Combine(outputDirectory, "note-image-preview.png"));
            Check(Descendants(preview).OfType<Image>().Single().Source.Width > 0, "放大窗口未显示图片。"); preview.Close();
            EditorButton(editor, "remove-note-image:" + image.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(editor.Images.Count == 0 && task.ImageIds.Count == 1, "移除草稿图片立即修改了任务。");
            service.ToggleCompleted(task); service.EditTask(task, task.Title, "已更新备注", task.ScheduledDate, true, editor.Images);
            window.Navigate(PageKind.History); Prepare(window);
            Check(Descendants(window).OfType<Button>().Any(b => Equals(b.Tag, "note-image:" + image.Id)), "历史快照丢失图片。");
            Check(!Descendants(TaskShareImage.Build(service, holidays)).OfType<Button>().Any(b => Equals(b.Tag, "note-image:" + image.Id)), "备注图片意外进入分享清单。");
            editor.ImageEditor.ReadClipboardImage = () => NoteImages.Decode(image);
            EditorButton(editor, "paste-note-image").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(editor.Images.Count == 1 && task.ImageIds.Count == 0, "粘贴图片未添加到独立草稿。");
            editor.Close();

            var pixels = new byte[4096 * 64 * 4];
            for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            var wide = BitmapSource.Create(4096, 64, 96, 96, PixelFormats.Bgra32, null, pixels, 4096 * 4);
            var scaled = NoteImages.Decode(NoteImages.Normalize(wide, "大图.png")); Check(scaled.PixelWidth == 2048, "大图未缩小。");
            var metadata = new BitmapMetadata("jpg"); metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)6);
            var encoder = new JpegBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(wide, null, metadata, null));
            using (var file = File.Create(jpegPath)) encoder.Save(file);
            var rotated = NoteImages.Decode(NoteImages.ImportFile(jpegPath));
            Check(rotated.PixelHeight == 2048 && rotated.PixelWidth < 64, "手机图片方向未修正。");
        }
        finally
        {
            NoteImages.OpenPreview = previewAction; service.RestoreBackup(baseline); window.Navigate(PageKind.Today);
            foreach (var path in new[] { baseline, sourcePath, invalidPath, largePath, jpegPath }) if (File.Exists(path)) File.Delete(path);
        }
    }

    private static async Task VerifyTaskShare(MainWindow window, TaskService service, HolidayService holidays, PreviewClock clock, string outputDirectory)
    {
        var baseline = Path.Combine(outputDirectory, "share-baseline.json"); service.ExportBackup(baseline);
        void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
        var originalToday = clock.Today;
        var originalClipboard = window.ImageClipboard;
        try
        {
            var overdue = service.AddTask("继续完成前一天留下的任务", "备注不进入分享图", originalToday.AddDays(-1), true);
            var viewed = service.AddEvent("今日已查看的提醒", "", originalToday, EventImportance.Normal, RepeatRule.None);
            service.ToggleViewed(service.State.Tasks.Single(t => t.EventId == viewed.Id));
            var cancelled = service.AddTask("已取消的安排", "", originalToday); service.CancelTask(cancelled);
            var yesterday = service.AddTask("昨天已经完成的安排", "", originalToday.AddDays(-1));
            clock.Today = originalToday.AddDays(-1); service.ToggleCompleted(yesterday); clock.Today = originalToday;
            var root = TaskShareImage.Build(service, holidays);
            var labels = Descendants(root).OfType<TextBlock>().Where(t => t.Tag is string tag && tag.StartsWith("share-task:")).ToList();
            Check(labels.Any(t => Equals(t.Tag, "share-task:" + overdue.Id)), "分享图遗漏逾期任务。");
            Check(labels.Any(t => t.TextDecorations.Count > 0), "分享图未保留今日完成状态。");
            Check(labels.All(t => !t.Text.Contains("已取消") && !t.Text.Contains("今日已查看") && !t.Text.Contains("昨天已经完成") && !t.Text.Contains("预约年度体检") && !t.Text.Contains("提交材料")), "分享图包含范围之外的任务。");
            var bitmap = TaskShareImage.Render(root); SaveBitmap(bitmap, Path.Combine(outputDirectory, "task-share.png"));
            Check(bitmap.IsFrozen && bitmap.PixelWidth == 1140, "分享图清晰度或冻结状态错误。");
            var pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
            Check(pixel[3] == 255, "分享图片背景不透明度错误。");
            DataObject? recorded = null; var writes = 0;
            window.ImageClipboard = new(data => { recorded = data; writes++; });
            window.Navigate(PageKind.Today); Prepare(window);
            var beforeCopy = StateJson.Serialize(service.State); Click(window, "copy-today-image");
            Check(writes == 1 && recorded is not null && recorded.ContainsImage() && recorded.GetDataPresent("PNG"), "复制按钮未提供图片和PNG格式。");
            Check(Equals(FindButton(window, "copy-today-image").Content, "✓ 已复制图片"), "复制成功没有反馈。");
            Check(beforeCopy == StateJson.Serialize(service.State), "分享修改了任务数据。");
            var png = recorded!.GetData("PNG") as MemoryStream ?? throw new InvalidOperationException("剪贴板PNG数据缺失。");
            using (var stream = new MemoryStream(png.ToArray()))
            {
                var decoded = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                Check(decoded.PixelWidth == bitmap.PixelWidth && decoded.PixelHeight == bitmap.PixelHeight, "PNG数据与图片尺寸不一致。");
            }
            var attempts = 0;
            await new TaskImageClipboard(_ => { if (++attempts < 3) throw new COMException("Clipboard busy", unchecked((int)0x800401D0)); }).CopyAsync(bitmap, CancellationToken.None);
            Check(attempts == 3, "剪贴板忙碌重试没有成功。");
            attempts = 0;
            try
            {
                await new TaskImageClipboard(_ => { attempts++; throw new COMException("Clipboard busy", unchecked((int)0x800401D0)); }).CopyAsync(bitmap, CancellationToken.None);
                throw new InvalidOperationException("剪贴板持续失败被当成成功。");
            }
            catch (COMException) { Check(attempts == 4, "剪贴板失败重试次数不正确。"); }

            service.State.Tasks.Clear(); service.State.Events.Clear(); service.State.Activities.Clear();
            var empty = TaskShareImage.Build(service, holidays);
            Check(Descendants(empty).OfType<TextBlock>().Any(t => t.Text == "今天没有待办任务。"), "空清单缺少说明。");
            SaveBitmap(TaskShareImage.Render(empty), Path.Combine(outputDirectory, "task-share-empty.png"));
            TaskItem? last = null;
            for (var i = 1; i <= 80; i++) last = service.AddTask($"待办 {i:00}：检查这条较长任务的全部内容是否清楚显示，并且在图片中保留末尾标识。", "", originalToday, i % 10 == 0);
            var longRoot = TaskShareImage.Build(service, holidays); var longBitmap = TaskShareImage.Render(longRoot);
            Check(Descendants(longRoot).OfType<TextBlock>().Count(t => t.Tag is string tag && tag.StartsWith("share-task:")) == 80, "长清单遗漏任务。");
            var lastLabel = Descendants(longRoot).OfType<TextBlock>().Single(t => Equals(t.Tag, "share-task:" + last!.Id));
            var lastBounds = lastLabel.TransformToAncestor(longRoot).TransformBounds(new Rect(lastLabel.RenderSize));
            var footer = Descendants(longRoot).OfType<TextBlock>().Single(t => Equals(t.Tag, "share-footer"));
            var footerBounds = footer.TransformToAncestor(longRoot).TransformBounds(new Rect(footer.RenderSize));
            Check(lastBounds.Bottom <= longBitmap.Height && footerBounds.Bottom <= longBitmap.Height && longBitmap.PixelHeight > 6000, "长清单底部被裁切。");
            SaveBitmap(longBitmap, Path.Combine(outputDirectory, "task-share-long.png"));
        }
        finally
        {
            clock.Today = originalToday; window.ImageClipboard = originalClipboard;
            service.RestoreBackup(baseline); File.Delete(baseline); window.Navigate(PageKind.Today);
        }
    }

    private static void SaveBitmap(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    private static void VerifyMonthView(MainWindow window, TaskService service, string outputDirectory)
    {
        var baseline = Path.Combine(outputDirectory, "month-baseline.json"); service.ExportBackup(baseline);
        void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
        Button Day(DateOnly day) => FindButton(window, "month-date:" + day.ToString("yyyy-MM-dd"));
        try
        {
            var date = new DateOnly(2026, 10, 8);
            var important = service.AddTask("准备项目评审需要的材料与演示内容", "", date, true);
            var done = service.AddTask("整理会议资料", "", date, true); service.ToggleCompleted(done);
            foreach (var title in new[] { "阅读研究文章", "完成代码检查", "整理本周笔记", "预约下次讨论" }) service.AddTask(title, "", date);
            window.Navigate(PageKind.Year); Prepare(window);
            var cachedYearDay = FindButton(window, "date:2026-10-08");
            Click(window, "month:2026-10"); Prepare(window);
            Check(((FrameworkElement)window.FindName("MonthScroller")).Visibility == Visibility.Visible, "点击月份未进入放大月历。");
            Check(((FrameworkElement)window.FindName("CalendarScroller")).Visibility == Visibility.Hidden, "放大月历覆盖时全年仍显示。");
            var day = Day(date);
            var labels = Descendants(day).OfType<TextBlock>().Where(t => Equals(t.Tag, "month-task")).ToList();
            Check(labels.Count == 3 && labels[0].Text.Contains(important.Title), "月历未优先显示重要任务关键词。");
            Check(labels.Any(t => t.Text.Contains(done.Title) && t.Opacity < 1 && t.TextDecorations.Count > 0), "月历已完成任务未淡化保留。");
            Check(Descendants(day).OfType<TextBlock>().Any(t => Equals(t.Tag, "month-more") && t.Text.Contains("3 项")), "更多任务数量错误。");
            Check(day.ToolTip.ToString()!.Contains("预约下次讨论"), "完整任务标题在月历中不可查看。");
            Check(Day(new(2026, 10, 14)).ToolTip.ToString()!.Contains("每周整理资料"), "月历缺少未来重复安排。");
            Render((FrameworkElement)window.Content, 1400, 940, Path.Combine(outputDirectory, "month.png"));
            Render((FrameworkElement)window.Content, 1000, 700, Path.Combine(outputDirectory, "month-narrow.png"));
            Prepare(window);
            service.EditTask(important, "更新后的关键词", "", date, true);
            Click(window, "current-month"); Prepare(window);
            Check(ReferenceEquals(day, Day(date)) && day.ToolTip.ToString()!.Contains("更新后的关键词"), "月历修改任务后未更新或重建日期控件。");
            Click(window, "next-month"); Prepare(window); Check(Day(new(2026, 11, 30)) is not null, "下月切换错误。");
            Click(window, "next-month"); Click(window, "next-month"); Prepare(window);
            Check(Day(new(2027, 1, 1)) is not null, "跨年切换月份错误。");
            Click(window, "previous-month"); Prepare(window); Check(Day(new(2026, 12, 31)) is not null, "跨年返回错误。");
            Click(window, "current-month"); Click(window, "back-year"); Prepare(window);
            Check(ReferenceEquals(cachedYearDay, FindButton(window, "date:2026-10-08")), "月历返回重建了全年控件。");
            Check(((FrameworkElement)window.FindName("CalendarScroller")).Visibility == Visibility.Visible, "返回全年未恢复总览。");
            Click(window, "month:2026-10"); window.Navigate(PageKind.Settings); window.Navigate(PageKind.Year); Prepare(window);
            Check(((FrameworkElement)window.FindName("MonthScroller")).Visibility == Visibility.Hidden, "重新进入全年日历默认仍是放大月历。");
            Click(window, "next-year"); Click(window, "next-year"); Click(window, "month:2028-02"); Prepare(window);
            Check(Day(new(2028, 2, 29)) is not null, "放大闰年月历遗漏2月29日。");
            Click(window, "back-year"); Click(window, "current-year");
        }
        finally { service.RestoreBackup(baseline); File.Delete(baseline); window.Navigate(PageKind.Year); }
    }

    private static void VerifyCalendarCache(MainWindow window, TaskService service, PreviewClock clock, string outputDirectory)
    {
        var baseline = Path.Combine(outputDirectory, "calendar-cache-baseline.json");
        service.ExportBackup(baseline);
        var originalToday = clock.Today;
        var date = new DateOnly(2026, 3, 19);
        var updateMeasurements = new List<string>();
        Button Day(DateOnly day) => FindButton(window, "date:" + day.ToString("yyyy-MM-dd"));
        void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
        void Refresh()
        {
            window.Navigate(PageKind.Today); Prepare(window);
            var watch = Stopwatch.StartNew(); window.Navigate(PageKind.Year); Prepare(window);
            updateMeasurements.Add($"Data change -> Year: build + layout {watch.Elapsed.TotalMilliseconds:F2} ms");
        }
        try
        {
            var cachedDay = Day(date);
            foreach (var source in new[] { PageKind.Today, PageKind.History, PageKind.Settings })
            {
                window.Navigate(source); Prepare(window); window.Navigate(PageKind.Year); Prepare(window);
                Check(ReferenceEquals(cachedDay, Day(date)), "切换页面重建了日期控件。");
            }
            var task = service.AddTask("缓存验证任务", "", date, true); Refresh();
            Check(ReferenceEquals(cachedDay, Day(date)) && cachedDay.ToolTip.ToString()!.Contains("1 项任务"), "新增任务后日历未更新数量或重建控件。");
            Check(cachedDay.Background == Ui.ImportantBadge, "日历重要背景未更新。");
            service.ToggleImportant(task); Refresh();
            Check(cachedDay.Background == Ui.Selected, "取消重要后日历背景未恢复。");
            var ev = service.AddEvent("缓存验证事件", "", date, EventImportance.Important, RepeatRule.None); Refresh();
            Check(cachedDay.ToolTip.ToString()!.Contains("2 项任务") && cachedDay.ToolTip.ToString()!.Contains(ev.Title), "事件标题或数量没有更新。");
            var occurrence = service.State.Tasks.Single(t => t.EventId == ev.Id);
            service.ToggleCompleted(occurrence); Refresh();
            Check(cachedDay.ToolTip.ToString()!.Contains("✓ 缓存验证事件"), "完成事件未更新勾选标记。");
            service.ToggleCompleted(occurrence);
            service.EditEvent(occurrence, "改期事件", "", date.AddDays(1), EventImportance.Normal, RepeatRule.None, EditScope.ThisOccurrence); Refresh();
            Check(!cachedDay.ToolTip.ToString()!.Contains("事件") && Day(date.AddDays(1)).ToolTip.ToString()!.Contains("改期事件"), "改期后日历仍显示旧事件。");
            service.CancelTask(task); Refresh();
            Check(!cachedDay.ToolTip.ToString()!.Contains("项任务"), "取消任务后日历数量未更新。");
            service.RestoreTask(task); Refresh();
            Check(cachedDay.ToolTip.ToString()!.Contains("1 项任务"), "恢复任务后日历数量未更新。");
            clock.Today = originalToday.AddDays(1); window.Navigate(PageKind.Year); Prepare(window);
            Check(((SolidColorBrush)Day(clock.Today).Background).Color == ((SolidColorBrush)Ui.Accent).Color &&
                ((SolidColorBrush)Day(originalToday).Background).Color != ((SolidColorBrush)Ui.Accent).Color, "跨天未移动今日高亮。");
            Click(window, "next-year"); Click(window, "next-year"); Prepare(window);
            Check(Day(new(2028, 2, 29)) is not null, "闰年缺少2月29日。");
            Click(window, "current-year"); Prepare(window);
            Check(Day(date).ToolTip.ToString()!.Contains("1 项任务"), "年份切换后任务数据丢失。");
            service.RestoreBackup(baseline); Refresh();
            Check(!Day(date).ToolTip.ToString()!.Contains("项任务"), "恢复备份后日历仍使用旧缓存。");
            File.WriteAllLines(Path.Combine(outputDirectory, "calendar-updates-performance.txt"), updateMeasurements);
        }
        finally
        {
            clock.Today = originalToday;
            service.RestoreBackup(baseline);
            File.Delete(baseline);
        }
    }

    private static void MeasureCalendarNavigation(MainWindow window, string outputDirectory)
    {
        var measurements = new List<string>();
        foreach (var source in new[] { PageKind.Today, PageKind.History, PageKind.Settings, PageKind.Today, PageKind.History, PageKind.Settings })
        {
            window.Navigate(source); Prepare(window);
            var watch = Stopwatch.StartNew();
            window.Navigate(PageKind.Year);
            var build = watch.Elapsed.TotalMilliseconds;
            Prepare(window);
            measurements.Add($"{source} -> Year: build {build:F2} ms; build + layout {watch.Elapsed.TotalMilliseconds:F2} ms");
        }
        File.WriteAllLines(Path.Combine(outputDirectory, "calendar-performance.txt"), measurements);
    }

    private static void Click(MainWindow window, string tag)
    {
        Prepare(window);
        var button = FindButton(window, tag);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private static Button FindButton(MainWindow window, string tag) => Descendants(window).OfType<Button>()
        .FirstOrDefault(b => Equals(b.Tag, tag)) ?? throw new InvalidOperationException("没有找到 UI 按钮：" + tag);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Descendants(child)) yield return descendant;
    }
    private static void Prepare(MainWindow window)
    {
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(1400, 940)); root.Arrange(new Rect(0, 0, 1400, 940)); root.UpdateLayout();
    }
    private static void Render(FrameworkElement element, int width, int height, string path)
    {
        element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var context = background.RenderOpen()) context.DrawRectangle(Ui.Page, null, new Rect(0, 0, width, height));
        bitmap.Render(background); bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
