using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DailyRecord.Core;

namespace DailyRecord.App;

public partial class MainWindow
{
    private Border? _colorNotice;
    private DispatcherTimer? _colorNoticeTimer;
    private Action? _colorUndo;
    private long _colorScrollVersion;
    private sealed record ColorState(TaskColor Color, bool Override, QuickColorRestore? Restore)
    {
        internal static ColorState Of(TaskItem task) => new(task.Color, task.ColorOverride, task.QuickColorRestore);
    }

    private void HideColorNotice()
    {
        _colorNoticeTimer?.Stop(); _colorUndo = null;
        if (_colorNotice is not null)
        {
            if (Content is Grid root) root.Children.Remove(_colorNotice);
            RetirePageTree(_colorNotice); _colorNotice = null;
        }
    }
    private void DisposeColorNotice()
    {
        HideColorNotice(); _colorScrollVersion++;
        if (_colorNoticeTimer is not null) { _colorNoticeTimer.Tick -= ColorNoticeExpired; _colorNoticeTimer = null; }
    }
    private void ColorNoticeExpired(object? sender, EventArgs e) => HideColorNotice();

    private void ShowColorNotice(string text, Action? undo)
    {
        HideColorNotice(); _colorUndo = undo;
        var caption = Ui.Text(text, 12); caption.MaxWidth = 440; caption.Tag = "quick-color-notice-text";
        var row = Ui.Row(caption);
        if (undo is not null)
        {
            var button = Ui.Button("撤销", () => { var action = _colorUndo; HideColorNotice(); action?.Invoke(); }, quiet: true);
            button.Tag = "quick-color-undo"; button.Margin = new Thickness(12, 0, 0, 0); row.Children.Add(button);
        }
        _colorNotice = new Border { Child = row, Background = Ui.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(15, 10, 15, 10), Margin = new Thickness(20, 0, 20, 30),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetColumnSpan(_colorNotice, 2); Panel.SetZIndex(_colorNotice, 20);
        if (Content is Grid root) root.Children.Add(_colorNotice);
        if (_colorNoticeTimer is null)
        {
            _colorNoticeTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(6) };
            _colorNoticeTimer.Tick += ColorNoticeExpired;
        }
        _colorNoticeTimer.Start();
    }

    // Capture the current viewport, stop remaining wheel motion, then restore only after the new tree has laid out.
    private void RunColorChange(Action action, Action? refresh = null, Func<string>? feedback = null, Action? undo = null)
    {
        var version = ++_colorScrollVersion; var page = _page;
        var views = new[] { PageScroller, CalendarScroller, MonthScroller };
        var offsets = views.Select(v => (Viewer: v, X: v.HorizontalOffset, Y: v.VerticalOffset)).ToArray();
        Run(action, () =>
        {
            refresh?.Invoke();
            void Restore()
            {
                if (version != _colorScrollVersion || page != _page || _lifetime.IsCancellationRequested) return;
                UpdateLayout();
                foreach (var item in offsets)
                {
                    item.Viewer.ScrollToHorizontalOffset(Math.Min(item.X, item.Viewer.ScrollableWidth));
                    item.Viewer.ScrollToVerticalOffset(Math.Min(item.Y, item.Viewer.ScrollableHeight));
                }
                UpdateLayout();
            }
            Restore();
            Dispatcher.BeginInvoke(Restore, DispatcherPriority.ContextIdle);
            if (feedback is not null) ShowColorNotice(feedback(), undo);
        });
    }

    private void ChangeTaskColor(TaskItem task, Action change, Action? refresh, bool allowUndo = true)
    {
        var before = ColorState.Of(task); var id = task.Id; var expected = before;
        RunColorChange(() => { change(); expected = ColorState.Of(task); }, refresh,
            () => "已设为 " + ColorStyle.Label(_service.State.Settings, task.Color),
            !allowUndo ? null : () => RunColorChange(() =>
            {
                var current = _service.State.Tasks.SingleOrDefault(t => t.Id == id);
                if (current is null || ColorState.Of(current) != expected) throw new InvalidOperationException("这条任务的颜色已有变化，未覆盖后续修改。");
                current.Color = before.Color; current.ColorOverride = before.Override; current.QuickColorRestore = before.Restore; _service.Save();
            }, refresh, () => "已撤销着色"));
    }

    private Button QuickTaskColorButton(TaskItem task, Action? refresh)
    {
        var quick = _service.State.Settings.QuickTaskColor;
        var active = task.Color == quick;
        var destination = _service.QuickColorDestination(task);
        var name = TaskColors.Name(_service.State.Settings, destination);
        var hint = active ? $"恢复为{name}" : $"加入{name}";
        var button = Ui.Button("", () => ChangeTaskColor(task, () => _service.ToggleQuickTaskColor(task), refresh), quiet: true);
        button.Content = new Ellipse { Width = 13, Height = 13, Stroke = ColorStyle.Line(quick), StrokeThickness = 1.8,
            Fill = active ? ColorStyle.Line(quick) : Brushes.Transparent, IsHitTestVisible = false };
        button.Padding = new Thickness(7, 8, 7, 8); button.MinWidth = 28; button.Tag = "quick-color:" + task.Id;
        button.ToolTip = hint + " · 右键选择其他颜色（仅本次）";
        System.Windows.Automation.AutomationProperties.SetName(button, hint);
        var menu = new ContextMenu();
        foreach (var color in Enum.GetValues<TaskColor>())
        {
            var captured = color;
            var item = new MenuItem { Header = ColorStyle.Label(_service.State.Settings, color), IsCheckable = true, IsChecked = task.Color == color,
                Tag = $"quick-palette:{task.Id}:{color}", Icon = new Ellipse { Width = 11, Height = 11, Fill = ColorStyle.Line(color) } };
            item.Click += (_, e) => { e.Handled = true; ChangeTaskColor(task, () => _service.SetTaskColors([task], captured), refresh); };
            menu.Items.Add(item);
        }
        if (task.IsEvent && _service.State.Events.Any(ev => ev.Id == task.EventId && ev.Repeat != RepeatRule.None))
        {
            menu.Items.Add(new Separator());
            var future = ColorMenu((color, scope) => ChangeTaskColor(task, () => _service.SetTaskColors([task], color, scope), refresh, scope == EditScope.ThisOccurrence), task.Id, true);
            future.Header = "修改后续安排的颜色…"; menu.Items.Add(future);
        }
        button.ContextMenu = menu; return button;
    }

    private Button QuickColorSetting(string tag)
    {
        var settings = _service.State.Settings;
        var button = Ui.Button("快捷着色：" + ColorStyle.Label(settings, settings.QuickTaskColor) + " ▾", () => { }, quiet: true);
        button.Tag = tag; button.Foreground = ColorStyle.Line(settings.QuickTaskColor); button.ToolTip = "选择任务行圆点一键使用的颜色，按账号保存";
        var menu = new ContextMenu();
        foreach (var color in Enum.GetValues<TaskColor>().Where(c => c != TaskColor.None))
        {
            var captured = color;
            var item = new MenuItem { Header = ColorStyle.Label(settings, color), IsCheckable = true, IsChecked = settings.QuickTaskColor == color,
                Tag = "quick-setting:" + color, Icon = new Ellipse { Width = 11, Height = 11, Fill = ColorStyle.Line(color) } };
            item.Click += (_, e) => { e.Handled = true; RunColorChange(() => _service.SetQuickTaskColor(captured), feedback: () => "快捷着色已设为 " + ColorStyle.Label(settings, captured)); };
            menu.Items.Add(item);
        }
        button.ContextMenu = menu; button.Click += (_, _) => { menu.PlacementTarget = button; menu.IsOpen = true; }; return button;
    }
}
