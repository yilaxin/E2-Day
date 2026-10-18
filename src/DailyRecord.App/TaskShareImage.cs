using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DailyRecord.Core;

namespace DailyRecord.App;

internal static class TaskShareImage
{
    internal const double LayoutWidth = 760;

    public static Border Build(TaskService service, HolidayService holidays)
    {
        var today = service.Today;
        var pending = service.VisibleOpen(today).Where(t => t.ScheduledDate <= today && !service.IsViewed(t, today)).ToList();
        var completed = TaskService.Sort(service.State.Tasks.Where(t => t.State == TaskState.Completed && t.CompletedOn == today)).ToList();
        var information = holidays.ForDay(today);
        var body = new StackPanel();
        var brand = Ui.Text("E²-Day  ·  每一天，都有自己的节奏", 12, Ui.Accent);
        brand.Margin = new Thickness(0, 0, 0, 14); body.Children.Add(brand);
        body.Children.Add(Ui.Text("今日任务", 34, bold: true));
        var date = Ui.Text(today.ToString("yyyy 年 M 月 d 日 · dddd", CultureInfo.GetCultureInfo("zh-CN")), 16, Ui.Muted);
        date.Margin = new Thickness(0, 9, 0, 0); body.Children.Add(date);
        var context = Ui.Text(string.Join(" · ", new[] { information.Lunar, information.Festivals }.Where(s => s.Length > 0)), 12, Ui.Muted);
        context.Margin = new Thickness(0, 5, 0, 20); body.Children.Add(context);
        var stats = Ui.Row(Ui.Badge($"待办 {pending.Count} 项"), Ui.Badge($"已完成 {completed.Count} 项"),
            Ui.Badge($"★ 重要 {pending.Concat(completed).Count(t => t.IsImportant)} 项", true));
        stats.Margin = new Thickness(0, 0, 0, 24); body.Children.Add(stats);
        AddGroup(body, "今日待办", pending, today, "今天没有待办任务。", false);
        AddGroup(body, "今日已完成", completed, today, "今天还没有已完成任务。", true);
        var footer = Ui.Text($"E²-Day · 探索每一天    {today:M 月 d 日}", 11, Ui.Muted); footer.Tag = "share-footer";
        footer.Margin = new Thickness(0, 20, 0, 0); body.Children.Add(footer);
        var card = new Border
        {
            Child = body, Background = Ui.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20), Padding = new Thickness(32)
        };
        var root = new Border { Width = LayoutWidth, Background = Ui.Page, Padding = new Thickness(20), Child = card };
        TextElement.SetFontFamily(root, new FontFamily("Microsoft YaHei UI, Segoe UI"));
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Display);
        root.UseLayoutRounding = true;
        return root;
    }

    private static void AddGroup(StackPanel body, string heading, IReadOnlyList<TaskItem> tasks, DateOnly today, string empty, bool completed)
    {
        var title = Ui.Text($"{heading}  ·  {tasks.Count} 项", 18, Ui.Accent, true);
        title.Margin = new Thickness(0, 0, 0, 12); body.Children.Add(title);
        if (tasks.Count == 0)
        {
            var placeholder = Ui.Text(empty, 14, Ui.Muted); placeholder.Margin = new Thickness(0, 6, 0, 22); body.Children.Add(placeholder);
        }
        foreach (var task in tasks)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            var marker = Ui.Text(completed ? "✓" : "○", 20, completed ? Ui.Accent : task.IsImportant ? Ui.Amber : Ui.Muted);
            marker.VerticalAlignment = VerticalAlignment.Top; row.Children.Add(marker);
            var caption = Ui.Text(task.Title, 16, bold: task.IsImportant); caption.Tag = "share-task:" + task.Id;
            if (completed) caption.TextDecorations = TextDecorations.Strikethrough;
            var text = Ui.Stack(caption);
            var details = new List<string>();
            if (task.IsImportant) details.Add("★ 重要任务");
            if (task.ScheduledDate < today && !completed) details.Add($"逾期 · 原定 {task.ScheduledDate:M 月 d 日}");
            else if (task.ScheduledDate != today) details.Add($"原定 {task.ScheduledDate:M 月 d 日}");
            if (details.Count > 0)
            {
                var detail = Ui.Text(string.Join("  ·  ", details), 11, task.IsImportant ? Ui.Amber : Ui.Muted);
                detail.Margin = new Thickness(0, 7, 0, 0); text.Children.Add(detail);
            }
            Grid.SetColumn(text, 1); row.Children.Add(text);
            var item = new Border
            {
                Child = row, Padding = new Thickness(14), CornerRadius = new CornerRadius(10),
                Background = task.IsImportant ? Ui.ImportantFill : Ui.SubtleSurface,
                BorderBrush = task.IsImportant ? Ui.ImportantLine : Ui.Line, BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 9), Opacity = completed ? 0.6 : 1
            };
            body.Children.Add(item);
        }
        body.Children.Add(new Border { Height = 10 });
    }

    public static BitmapSource Render(FrameworkElement root, double layoutWidth = LayoutWidth)
    {
        root.Measure(new Size(layoutWidth, double.PositiveInfinity));
        var height = Math.Ceiling(root.DesiredSize.Height);
        root.Arrange(new Rect(0, 0, layoutWidth, height)); root.UpdateLayout();
        // Bound bitmap memory while keeping the complete list, including its footer.
        var scale = Math.Min(1.5, Math.Min(16000 / height, Math.Sqrt(40_000_000 / (layoutWidth * height))));
        var widthPixels = Math.Max(1, (int)Math.Ceiling(layoutWidth * scale));
        var heightPixels = Math.Max(1, (int)Math.Ceiling(height * scale));
        var bitmap = new RenderTargetBitmap(widthPixels, heightPixels, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var context = background.RenderOpen()) context.DrawRectangle(Brushes.White, null, new Rect(0, 0, layoutWidth, height));
        bitmap.Render(background); bitmap.Render(root); bitmap.Freeze();
        return bitmap;
    }

    public static DataObject ClipboardData(BitmapSource bitmap)
    {
        var png = new MemoryStream();
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(png); png.Position = 0;
        var data = new DataObject(); data.SetImage(bitmap); data.SetData("PNG", png);
        return data;
    }
}

internal sealed class TaskImageClipboard(Action<DataObject>? write = null)
{
    private readonly Action<DataObject> _write = write ?? (data => Clipboard.SetDataObject(data, true));

    public async Task CopyAsync(BitmapSource bitmap, CancellationToken cancellationToken)
    {
        var data = TaskShareImage.ClipboardData(bitmap);
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { _write(data); return; }
            catch (ExternalException) when (attempt < 3) { await Task.Delay(100, cancellationToken); }
        }
    }
}

public partial class MainWindow
{
    internal TaskImageClipboard ImageClipboard { get; set; } = new();

    private Task CopyTodayTasksImageAsync(Button button) => CopyImageAsync(button, () => TaskShareImage.Render(TaskShareImage.Build(_service, _holidays)));

    private async Task CopyImageAsync(Button button, Func<BitmapSource> render)
    {
        var tooltip = button.ToolTip;
        button.IsEnabled = false;
        try
        {
            var bitmap = render();
            await ImageClipboard.CopyAsync(bitmap, _lifetime.Token);
            button.Content = "✓ 已复制图片"; button.ToolTip = "可在聊天窗口按 Ctrl+V 粘贴";
            if (_smoke) return;
            await Task.Delay(1800, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (_smoke) throw;
            MessageBox.Show(this, ex is ExternalException ? "剪贴板暂时被其它程序占用，请稍后再试。" : "图片复制未完成，请稍后重试。",
                "复制图片", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            if (!_smoke || !Equals(button.Content, "✓ 已复制图片")) { button.Content = "复制为图片"; button.ToolTip = tooltip; }
            button.IsEnabled = true;
        }
    }
}
