using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DailyRecord.Core;

namespace DailyRecord.App;

internal static partial class SmokeTest
{
    private static void VerifyItemConversions(MainWindow window, TaskService service, PreviewClock clock, string output)
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var baseline = Path.Combine(output, "conversion-baseline.json"); service.ExportBackup(baseline);
        var presenter = window.ItemEditorPresenter; var image = MemoryImage(90, 65, 55);
        ComboBox Choice(EditDialog dialog, string tag) => Descendants(dialog).OfType<ComboBox>().Single(c => Equals(c.Tag, tag));
        void ConvertFromMenu(TaskItem task, ItemKind kind, Action<EditDialog> configure, bool save = true)
        {
            window.ItemEditorPresenter = dialog =>
            {
                Exception? failure = null;
                dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        Check(dialog.Kind == kind && dialog.Images.Single().Id == image.Id, "快捷转换未选目标类型或丢失图片。");
                        configure(dialog);
                        FindButton(dialog, save ? "item-save" : "item-cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    catch (Exception ex) { failure = ex; dialog.Close(); }
                }, DispatcherPriority.ContextIdle);
                var result = dialog.ShowDialog(); if (failure is not null) throw failure; return result;
            };
            var menu = FindButton(window, "more:" + task.Id).ContextMenu!;
            var action = menu.Items.OfType<MenuItem>().Single(i => Equals(i.Tag, $"convert:{task.Id}:{kind}"));
            action.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Prepare(window);
        }
        try
        {
            var task = service.AddTask("准备下周分享的材料", "需要保留的图文备注", clock.Today, true, [image]); var id = task.Id;
            window.Navigate(PageKind.Today); Prepare(window);
            var before = StateJson.Serialize(service.State); var revision = service.Revision;
            ConvertFromMenu(task, ItemKind.Event, dialog =>
            {
                var title = Descendants(dialog).OfType<TextBox>().Single(t => Equals(t.Tag, "task-title")); title.Text = "尚未保存的标题";
                Choice(dialog, "item-kind").SelectedIndex = (int)ItemKind.LongTerm;
                Choice(dialog, "item-kind").SelectedIndex = (int)ItemKind.Task;
                Choice(dialog, "item-kind").SelectedIndex = (int)ItemKind.Event;
                Check(title.Text == "尚未保存的标题" && dialog.Images.Single().Id == image.Id && dialog.Notes == task.Notes, "未保存切换清空了草稿。");
            }, save: false);
            Check(before == StateJson.Serialize(service.State) && revision == service.Revision, "取消转换修改了数据。");
            ConvertFromMenu(task, ItemKind.Event, dialog =>
            {
                Descendants(dialog).OfType<DatePicker>().Single(d => Equals(d.Tag, "item-date")).SelectedDate = clock.Today.AddDays(3).ToDateTime(TimeOnly.MinValue);
                Choice(dialog, "item-reminder").SelectedIndex = (int)EventImportance.Important;
                Choice(dialog, "item-color").SelectedIndex = (int)TaskColor.Blue;
                Check(Descendants(dialog).OfType<TextBlock>().Single(t => Equals(t.Tag, "item-preview")).Text.Contains("提前 3 天"), "提醒预览未跟随当前设置。");
                Render((FrameworkElement)dialog.Content, 580, 960, Path.Combine(output, "item-conversion-editor.png"));
            });
            Check(task.Id == id && service.KindOf(task) == ItemKind.Event && service.VisibleOpen(clock.Today).Contains(task) && task.IsImportant && task.Color == TaskColor.Blue &&
                service.State.Events.Single(e => e.Id == task.EventId).Color == TaskColor.Blue, "任务转事件失败、提醒未生效或新分类未用于新安排。");
            Check(FindButton(window, "viewed:" + id) is not null, "事件查看入口没有更新。");
            service.ToggleCompleted(task); window.Navigate(PageKind.Today); Prepare(window);
            var completed = task.CompletedOn; var history = StateJson.Serialize(new AppState { Activities = service.State.Activities });
            ConvertFromMenu(task, ItemKind.LongTerm, _ => { });
            Check(task.State == TaskState.Completed && task.CompletedOn == completed && new TaskAnalytics(service.State, clock.Today).Habits.Single(h => h.Definition.Id == task.EventId).CompletedDays.Count == 0,
                "已完成事件变长期后重复计入打卡。");
            ConvertFromMenu(task, ItemKind.Task, dialog =>
            {
                var date = Descendants(dialog).OfType<DatePicker>().Single(d => Equals(d.Tag, "item-date"));
                var draft = clock.Today.AddDays(8).ToDateTime(TimeOnly.MinValue); date.SelectedDate = draft;
                Choice(dialog, "item-kind").SelectedIndex = (int)ItemKind.LongTerm;
                Check(date.SelectedDate == task.ScheduledDate.ToDateTime(TimeOnly.MinValue) && !date.IsEnabled, "返回原长期任务时显示了未生效的日期草稿。");
                Choice(dialog, "item-kind").SelectedIndex = (int)ItemKind.Task;
                Check(date.SelectedDate == draft && date.IsEnabled, "转换日期草稿丢失。");
                date.SelectedDate = task.ScheduledDate.ToDateTime(TimeOnly.MinValue);
                Choice(dialog, "item-scope").SelectedIndex = 1;
                Check(Descendants(dialog).OfType<TextBlock>().Single(t => Equals(t.Tag, "item-preview")).Text.Contains("停止旧规则"), "停止范围预览不明确。");
            });
            Check(task.Id == id && service.KindOf(task) == ItemKind.Task && task.State == TaskState.Completed && task.CompletedOn == completed &&
                task.ImageIds.Single() == image.Id && service.GetNoteImages(task.ImageIds).Single().Data.SequenceEqual(image.Data), "长期任务转普通任务丢失状态或原图。");
            Check(history == StateJson.Serialize(new AppState { Activities = service.State.Activities }), "仅转换本次改变了历史快照。");
            Render((FrameworkElement)window.Content, 1400, 940, Path.Combine(output, "item-conversion-today.png"));
            File.WriteAllText(Path.Combine(output, "item-conversion-result.txt"), "PASS: real modal editor and quick conversion menus; unsaved three-way switching retains title/notes/images; cancel without writes; reminder preview and actual reminder entry; task -> event -> long-term -> task; original identity, star, completed day, history and full image retained; completed event not retroactively counted as habit; explicit stop-scope preview. Isolated sample data only.\n");
        }
        finally { window.ItemEditorPresenter = presenter; service.RestoreBackup(baseline); File.Delete(baseline); window.Navigate(PageKind.Today); }
    }
}
