using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DailyRecord.Core;

namespace DailyRecord.App;

internal static partial class SmokeTest
{
    private static void VerifyHeatmapRanges(MainWindow window, TaskService service, SqliteStore store, string output)
    {
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var original = service.State.Settings.CompletionHeatmapScale(); var presenter = window.HeatmapScalePresenter;
        var temporary = new List<TaskItem>(); Window? dialog = null;
        TextBox Input(int index) => Descendants(dialog!).OfType<TextBox>().Single(t => Equals(t.Tag, "heatmap-limit:" + index));
        try
        {
            // Isolated high-volume sample, spanning all four positive bands.
            foreach (var (age, total) in new[] { (0, 5), (1, 10), (2, 20), (3, 28) })
            {
                var day = service.Today.AddDays(-age);
                var existing = service.State.Tasks.Count(t => t.State == TaskState.Completed && t.CompletedOn == day);
                for (var i = existing; i < total; i++) temporary.Add(new TaskItem { Title = "热力图范围示例", ScheduledDate = day,
                    OriginalScheduledDate = day, CompletedOn = day, State = TaskState.Completed });
            }
            service.State.Tasks.AddRange(temporary); service.Save(); window.Navigate(PageKind.Analytics);
            window.HeatmapScalePresenter = value => { dialog = value; dialog.Show(); dialog.UpdateLayout(); };
            var before = StateJson.Serialize(service.State); var revision = service.Revision;
            Click(window, "analytics-heat-ranges"); Click(dialog!, "heatmap-preset-high");
            Check(before == StateJson.Serialize(service.State) && revision == service.Revision, "预设未保存就改变了范围。");
            Check(Descendants(dialog!).OfType<TextBlock>().Any(t => t.Text == "21+ 项"), "实时图例没有更新。");
            Render((FrameworkElement)dialog!.Content, 600, 600, Path.Combine(output, "heatmap-ranges-editor.png"));
            foreach (var text in new[] { "", "0", "1.5", "abc", "10000", "20" })
            {
                Input(0).Text = text;
                Check(!FindButton(dialog, "heatmap-range-save").IsEnabled, "非法范围可以点击保存。");
            }
            Click(dialog, "heatmap-range-cancel"); dialog = null;
            Check(before == StateJson.Serialize(service.State), "取消改动了设置。");
            Click(window, "analytics-heat-ranges"); Click(dialog!, "heatmap-preset-high"); Click(dialog!, "heatmap-range-save"); dialog = null;
            Check(store.Load().Settings.CompletionHeatmapScale() == new HeatmapScale(5, 10, 20), "界面保存未持久化。");
            var days = new TaskAnalytics(service.State, service.Today).Daily(new DateOnly(service.Today.Year, 1, 1), new(service.Today.Year, 12, 31));
            var colors = new[] { "#DED4F0", "#BBA5DC", "#9373C4", "#67459E" };
            var scale = service.State.Settings.CompletionHeatmapScale();
            foreach (var point in days.Where(d => d.Count > 0))
            {
                var cell = FindButton(window, "analytics-heat:" + point.Day.ToString("yyyy-MM-dd"));
                Check(((SolidColorBrush)cell.Background).Color == (Color)ColorConverter.ConvertFromString(colors[scale.Level(point.Count) - 1]), "页面色阶没有使用自定义边界。");
            }
            Check(Descendants(window).OfType<TextBlock>().Any(t => Equals(t.Tag, "analytics-heat-legend:21") && t.Text == "21+ 项"), "页面图例没有更新。");
            Render((FrameworkElement)window.Content, 1400, 1500, Path.Combine(output, "heatmap-ranges-overview.png"));
            var report = window.BuildAnalyticsShareImage();
            var texts = Descendants(report).OfType<TextBlock>().ToArray();
            Check(texts.Any(t => Equals(t.Tag, "analytics-share-heat:legend:21") && t.Text == "21+ 项"), "分享图图例没有使用自定义范围。");
            foreach (var age in new[] { 0, 1, 2, 3 })
            {
                var day = service.Today.AddDays(-age); var count = days.Single(d => d.Day == day).Count;
                var live = FindButton(window, "analytics-heat:" + day.ToString("yyyy-MM-dd"));
                var cell = Descendants(report).OfType<Border>().Single(b => Equals(b.Tag, "analytics-share-heat:" + day.ToString("yyyy-MM-dd")));
                Check(Equals(live.Background, cell.Background), "分享与页面色阶不一致。");
                var label = Descendants(cell).OfType<TextBlock>().First();
                Check(Equals(label.Foreground, scale.Level(count) >= 3 ? Brushes.White : Ui.Ink), "分享图文字对比没有随自定义档位变化。");
            }
            Check(texts.Any(t => Equals(t.Tag, "analytics-share-habit:legend:4") && t.Text == "已完成"), "范围设置错误影响长期打卡图例。");
            SaveBitmap(TaskShareImage.Render(report, MainWindow.AnalyticsShareWidth), Path.Combine(output, "heatmap-ranges-share.png"));
            Click(window, "analytics-heat-ranges"); Click(dialog!, "heatmap-preset-default");
            Check(Input(0).Text == "1" && Input(1).Text == "3" && Input(2).Text == "6", "恢复默认预设错误。");
            Click(dialog!, "heatmap-range-cancel"); dialog = null;
            Check(service.State.Settings.CompletionHeatmapScale() == scale, "取消恢复默认仍修改了设置。");
            File.WriteAllText(Path.Combine(output, "heatmap-ranges-result.txt"), "PASS: real WPF range editor; high-volume preset and live legend; empty/zero/fraction/text/oversize/unordered validation; cancel without writes; saved database reload; full-year colors and legend; share chart colors, legend and text contrast; independent habit legend; default preset and cancellation. Screenshots contain isolated sample data only.\n");
        }
        finally
        {
            dialog?.Close(); window.HeatmapScalePresenter = presenter;
            service.State.Tasks.RemoveAll(t => temporary.Contains(t)); service.UpdateHeatmapScale(original); window.Navigate(PageKind.Analytics);
        }
    }
}
