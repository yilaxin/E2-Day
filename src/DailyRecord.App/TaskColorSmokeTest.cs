using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DailyRecord.Core;

namespace DailyRecord.App;

internal static partial class SmokeTest
{
    private static async Task VerifyTaskColors(MainWindow window, TaskService service, HolidayService holidays, PreviewClock clock, string output)
    {
        static void Check(bool okay, string message) { if (!okay) throw new InvalidOperationException(message); }
        var baseline = Path.Combine(output, "colors-baseline.json"); service.ExportBackup(baseline);
        var oldClipboard = window.ImageClipboard; var namesPresenter = window.ColorNamesPresenter; var itemPresenter = window.ItemEditorPresenter;
        MenuItem Menu(TaskItem task, TaskColor color) => FindButton(window, "more:" + task.Id).ContextMenu!.Items.OfType<MenuItem>()
            .Single(m => Equals(m.Tag, "task-color-menu:" + task.Id)).Items.OfType<MenuItem>()
            .Single(m => Equals(m.Tag, $"color:{task.Id}:{color}:{EditScope.ThisOccurrence}"));
        void Select(string id, bool value) => Descendants(window).OfType<CheckBox>().Single(c => Equals(c.Tag, "color-select:" + id)).IsChecked = value;
        string[] RowIds() => Descendants(window).OfType<Button>().Where(b => b.Tag is string tag && tag.StartsWith("more:")).Select(b => ((string)b.Tag)[5..]).ToArray();
        try
        {
            var image = MemoryImage(55, 75, 95);
            var a = service.AddTask("整理分享提纲", "备注不进入分享图", clock.Today, true, [image]);
            var b = service.AddTask("阅读两节学习材料", "", clock.Today);
            var pink = service.AddTask("等待讨论结果", "", clock.Today, color: TaskColor.Pink);
            var done = service.AddTask("整理昨日笔记", "", clock.Today, color: TaskColor.Blue); service.ToggleCompleted(done);
            var upcoming = service.AddEvent("准备后天的材料", "", clock.Today.AddDays(2), EventImportance.Important, RepeatRule.None, important: true, color: TaskColor.Blue);
            var seen = service.EnsureOccurrence(upcoming, upcoming.StartDate); service.ToggleViewed(seen);
            window.Navigate(PageKind.Today); Prepare(window);
            Menu(a, TaskColor.Blue).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Prepare(window);
            Check(a.Color == TaskColor.Blue && a.IsImportant && a.ImageIds.Single() == image.Id, "单项菜单着色丢失内容或星标。");
            foreach (var save in new[] { false, true })
            {
                var prior = StateJson.Serialize(service.State);
                window.ItemEditorPresenter = dialog =>
                {
                    Exception? failure = null;
                    dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(() =>
                    {
                        try
                        {
                            Descendants(dialog).OfType<ComboBox>().Single(c => Equals(c.Tag, "item-color")).SelectedIndex = (int)TaskColor.Gray;
                            if (save) Render((FrameworkElement)dialog.Content, 580, 920, Path.Combine(output, "task-colors-editor.png"));
                            FindButton(dialog, save ? "item-save" : "item-cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        }
                        catch (Exception ex) { failure = ex; dialog.Close(); }
                    }, DispatcherPriority.ContextIdle);
                    var result = dialog.ShowDialog(); if (failure is not null) throw failure; return result;
                };
                FindButton(window, "more:" + pink.Id).ContextMenu!.Items.OfType<MenuItem>().First().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Prepare(window);
                Check(save ? pink.Color == TaskColor.Gray : prior == StateJson.Serialize(service.State), "编辑器分类保存或取消不正确。");
            }
            Menu(pink, TaskColor.Pink).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Prepare(window);
            Click(window, "color-batch-toggle"); Prepare(window);
            Select(a.Id, true); Select(b.Id, true);
            Check(Descendants(window).OfType<TextBlock>().Single(t => Equals(t.Tag, "color-selection-count")).Text.Contains("2 项"), "复选未即时更新数量。");
            var apply = FindButton(window, "color-batch-apply"); Check(apply.IsEnabled, "选择后批量着色按钮未启用。");
            apply.ContextMenu!.Items.OfType<MenuItem>().Single(m => Equals(m.Tag, $"color:batch:{TaskColor.Blue}:{EditScope.ThisOccurrence}"))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Prepare(window);
            Check(a.Color == TaskColor.Blue && b.Color == TaskColor.Blue && pink.Color == TaskColor.Pink, "批量着色影响了未选事项。");
            Click(window, "color-filter:Blue"); Prepare(window);
            var ids = RowIds(); Check(new[] { a.Id, b.Id, done.Id, seen.Id }.All(ids.Contains) && !ids.Contains(pink.Id), "颜色筛选遗漏已完成或已查看，或混入其他分类。");
            var filterButton = FindButton(window, "color-filter:Blue"); Check(((string)filterButton.Content).EndsWith("3"), "分类未完成数包含了已完成或漏掉已查看。");
            var before = StateJson.Serialize(service.State); var revision = service.Revision;
            var root = TaskShareImage.Build(service, holidays, TaskColor.Blue);
            var shared = Descendants(root).OfType<TextBlock>().Where(t => t.Tag is string tag && tag.StartsWith("share-task:")).Select(t => ((string)t.Tag)[11..]).ToArray();
            Check(ids.Order().SequenceEqual(shared.Order()), "分类图片与当前清单范围不同。");
            Check(Descendants(root).OfType<TextBlock>().Any(t => Equals(t.Tag, "share-color-label") && t.Text.Contains("本时段要做")), "分享图未标明分类。");
            Check(Descendants(root).OfType<TextBlock>().Any(t => t.Text == "★ 重要 2 项"), "分类分享的重要数量遗漏了提前或已查看提醒。");
            var bitmap = TaskShareImage.Render(root); SaveBitmap(bitmap, Path.Combine(output, "task-colors-share.png"));
            DataObject? copied = null; window.ImageClipboard = new(data => copied = data); Click(window, "copy-today-image");
            Check(copied is not null && copied.ContainsImage() && copied.GetDataPresent("PNG"), "当前分类复制入口没有位图和PNG。");
            var png = copied!.GetData("PNG") as MemoryStream ?? throw new InvalidOperationException("分类PNG数据缺失。");
            using (var stream = new MemoryStream(png.ToArray()))
            {
                var decoded = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                Check(decoded.PixelWidth == bitmap.PixelWidth && decoded.PixelHeight == bitmap.PixelHeight, "复制按钮没有使用当前分类。");
            }
            Check(before == StateJson.Serialize(service.State) && revision == service.Revision, "筛选或分享写入任务状态。");

            void Names(bool save, bool invalid = false)
            {
                window.ColorNamesPresenter = dialog =>
                {
                    Exception? failure = null;
                    dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(() =>
                    {
                        try
                        {
                            var field = Descendants(dialog).OfType<TextBox>().Single(t => Equals(t.Tag, "color-name:Blue")); field.Text = invalid ? "专注处理" : "上午计划";
                            if (invalid)
                            {
                                FindButton(dialog, "color-names-save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                Check(dialog.IsVisible && Descendants(dialog).OfType<TextBlock>().Single(t => Equals(t.Tag, "color-name-error")).Text.Length > 0, "重复名称没有提示或关闭了窗口。");
                            }
                            FindButton(dialog, save ? "color-names-save" : "color-names-cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        }
                        catch (Exception ex) { failure = ex; dialog.Close(); }
                    }, DispatcherPriority.ContextIdle);
                    var result = dialog.ShowDialog(); if (failure is not null) throw failure; return result;
                };
                Click(window, "color-names"); Prepare(window);
            }
            Names(false, true); Check(before == StateJson.Serialize(service.State), "取消分类名称修改了记录。");
            Names(true); Check(TaskColors.Name(service.State.Settings, TaskColor.Blue) == "上午计划" && ((string)FindButton(window, "color-filter:Blue").Content).Contains("上午计划"), "改名没有更新筛选条。");
            Check(Descendants(TaskShareImage.Build(service, holidays, TaskColor.Blue)).OfType<TextBlock>().Any(t => Equals(t.Tag, "share-color-label") && t.Text.Contains("上午计划")), "改名未用于分享图。");
            Click(window, "complete:" + a.Id); Click(window, "complete:" + b.Id); Click(window, "complete:" + seen.Id); Prepare(window);
            Check(((string)FindButton(window, "color-filter:Blue").Content).EndsWith("0") && RowIds().Length == 4, "分类完成后清空了已完成记录或计数未归零。");
            Click(window, "complete:" + a.Id); Prepare(window);
            Check(((string)FindButton(window, "color-filter:Blue").Content).EndsWith("1"), "撤销完成未恢复分类未完成数。");
            Click(window, "color-batch-toggle"); Select(a.Id, true); Click(window, "color-filter:Pink"); Click(window, "color-batch-toggle"); Prepare(window);
            Check(!FindButton(window, "color-batch-apply").IsEnabled, "切换分类后仍保留隐藏任务选择。");
            Click(window, "color-batch-toggle"); Click(window, "color-filter:All"); Prepare(window);
            window.Width = 1400; window.Height = 940; Prepare(window);
            Render((FrameworkElement)window.Content, 1400, 940, Path.Combine(output, "task-colors-today.png"));
            window.Width = 1000;
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            window.Navigate(PageKind.Today);
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            Render((FrameworkElement)window.Content, 1000, 940, Path.Combine(output, "task-colors-narrow.png"));
            window.Width = 1400;
            await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
            File.WriteAllText(Path.Combine(output, "task-colors-result.txt"), "PASS: single and batch menu coloring, independent important star and images; checkbox count without page rebuild; category counts include viewed reminders and exclude completed; filtered image exactly matches current category with native bitmap and PNG; modal rename validation/cancel/save; completed visibility and undo; filter change clears hidden selection; wide/narrow glass layout. Isolated data and recording clipboard only.");
        }
        finally
        {
            window.ImageClipboard = oldClipboard; window.ColorNamesPresenter = namesPresenter; window.ItemEditorPresenter = itemPresenter;
            service.RestoreBackup(baseline); window.Navigate(PageKind.Today); Prepare(window); Click(window, "color-filter:All");
            File.Delete(baseline);
        }
    }
}
