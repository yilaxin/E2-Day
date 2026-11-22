using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DailyRecord.Core;

namespace DailyRecord.App;

public partial class MainWindow
{
    private readonly List<Border> _executionRows = [];
    private Point _executionDragOrigin;
    private Border? _executionHandle;
    private string? _executionDragId;
    private int _executionDropSlot = -1;
    private DispatcherTimer? _executionScrollTimer;
    private ExecutionDropLine? _executionDropLine;
    private AdornerLayer? _executionAdornerLayer;

    private FrameworkElement ExecutionOrderControls()
    {
        var manual = _service.UsesManualExecutionOrder;
        var auto = Ui.Button("自动排序", () => ChangeExecutionMode(false), accent: !manual);
        auto.Tag = "execution-mode-auto";
        var custom = Ui.Button("我的顺序", () => ChangeExecutionMode(true), accent: manual);
        custom.Tag = "execution-mode-manual";
        var controls = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        controls.Children.Add(auto);
        controls.Children.Add(custom);
        controls.Children.Add(Ui.Text("按住任务左侧手柄调整执行顺序", 11, Ui.Muted));
        return controls;
    }

    private void ChangeExecutionMode(bool manual)
    {
        if (_service.UsesManualExecutionOrder == manual) return;
        HideColorNotice();
        RunColorChange(() => _service.SetManualExecutionOrder(manual));
    }

    private void MoveExecutionTask(string id, int destination)
    {
        var before = _service.State.Settings.ExecutionPlan;
        var moved = false;
        RunColorChange(() => moved = _service.MoveTodayTask(id, destination, _taskColorFilter));
        if (!moved || _service.State.Settings.ExecutionPlan is not { } expected) return;
        ShowColorNotice("顺序已调整", () =>
        {
            var undone = false;
            RunColorChange(() => undone = _service.UndoExecutionMove(expected, before),
                feedback: () => undone ? "已撤销顺序调整" : "顺序已有更新，这次调整无法撤销");
        });
    }

    private MenuItem ExecutionMoveMenu(TaskItem task)
    {
        var menu = new MenuItem { Header = "调整执行顺序", Tag = "execution-move:" + task.Id };
        menu.SubmenuOpened += (_, _) =>
        {
            var order = _service.TodayExecutionOrder(_taskColorFilter).Select(t => t.Id).ToList();
            var index = order.IndexOf(task.Id);
            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                var target = MenuDestination((string)item.Tag, index, order.Count);
                item.IsEnabled = index >= 0 && target >= 0 && target < order.Count && target != index;
            }
        };
        foreach (var (key, label) in new[] { ("first", "移到最前"), ("up", "上移"), ("down", "下移"), ("last", "移到最后") })
        {
            var item = new MenuItem { Header = label, Tag = key };
            item.Click += (_, _) =>
            {
                var order = _service.TodayExecutionOrder(_taskColorFilter).Select(t => t.Id).ToList();
                var index = order.IndexOf(task.Id);
                if (index >= 0) MoveExecutionTask(task.Id, MenuDestination(key, index, order.Count));
            };
            menu.Items.Add(item);
        }
        return menu;
    }

    private static int MenuDestination(string key, int index, int count) => key switch
    {
        "first" => 0,
        "up" => index - 1,
        "down" => index + 1,
        _ => count - 1
    };

    private void AddExecutionHandle(Grid grid, TaskItem task, int? number)
    {
        var glyph = Ui.Text("⠿", 17, Ui.Accent);
        glyph.FontFamily = new FontFamily("Segoe UI Symbol");
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        var handle = new Border
        {
            Child = glyph, Background = Brushes.Transparent, Width = 25, Height = 29,
            Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Top,
            Cursor = Cursors.SizeNS, ToolTip = "按住拖动。也可在更多菜单调整执行顺序。",
            Tag = "execution-handle:" + task.Id
        };
        System.Windows.Automation.AutomationProperties.SetName(handle, "拖动调整：" + task.Title);
        handle.MouseLeftButtonDown += (_, e) =>
        {
            CancelExecutionDrag();
            if (!handle.CaptureMouse()) return;
            _executionHandle = handle;
            _executionDragOrigin = e.GetPosition(PageScroller);
            _pageScroll.Stop();
            e.Handled = true;
        };
        handle.MouseMove += (_, e) =>
        {
            if (!ReferenceEquals(_executionHandle, handle) || e.LeftButton != MouseButtonState.Pressed) return;
            var point = e.GetPosition(PageScroller);
            if (_executionDragId is null)
            {
                if (Math.Abs(point.Y - _executionDragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                _executionDragId = task.Id;
                _executionScrollTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(30) };
                _executionScrollTimer.Tick += ScrollWhileDragging;
                _executionScrollTimer.Start();
            }
            UpdateExecutionDrop(point);
            e.Handled = true;
        };
        handle.MouseLeftButtonUp += (_, e) =>
        {
            if (!ReferenceEquals(_executionHandle, handle)) return;
            var dragged = _executionDragId is not null;
            if (dragged) DropExecutionTask(task.Id, e.GetPosition(PageScroller));
            else CancelExecutionDrag();
            e.Handled = true;
        };
        handle.LostMouseCapture += (_, _) => { if (ReferenceEquals(_executionHandle, handle)) CancelExecutionDrag(); };

        grid.ColumnDefinitions.Insert(0, new ColumnDefinition { Width = GridLength.Auto });
        foreach (UIElement child in grid.Children) Grid.SetColumn(child, Grid.GetColumn(child) + 1);
        if (number is { } rank)
        {
            var label = Ui.Text(rank.ToString(), 11, Ui.Accent, bold: true);
            label.Tag = "execution-number:" + task.Id;
            label.HorizontalAlignment = HorizontalAlignment.Center;
            grid.Children.Add(Ui.Stack(handle, label));
        }
        else grid.Children.Add(handle);
    }

    internal void DropExecutionTask(string id, Point point)
    {
        UpdateExecutionDrop(point);
        var destination = ExecutionDropDestination(id);
        CancelExecutionDrag();
        if (destination >= 0) MoveExecutionTask(id, destination);
    }

    private void ScrollWhileDragging(object? sender, EventArgs e)
    {
        var point = Mouse.GetPosition(PageScroller);
        if (point.X >= 0 && point.X <= PageScroller.ViewportWidth && point.Y >= -50 && point.Y <= PageScroller.ViewportHeight + 50)
        {
            const double edge = 48;
            var nearTop = Math.Clamp(edge - point.Y, 0, edge);
            var nearBottom = Math.Clamp(point.Y - (PageScroller.ViewportHeight - edge), 0, edge);
            if (nearTop != nearBottom)
            {
                var offset = PageScroller.VerticalOffset + (nearBottom - nearTop) * 0.55;
                PageScroller.ScrollToVerticalOffset(Math.Clamp(offset, 0, PageScroller.ScrollableHeight));
                PageScroller.UpdateLayout();
            }
        }
        UpdateExecutionDrop(point);
    }

    private void UpdateExecutionDrop(Point point)
    {
        if (_executionRows.Count == 0) return;
        var first = _executionRows[0];
        var origin = first.TranslatePoint(new Point(), PageScroller);
        if (point.X < origin.X - 15 || point.X > origin.X + first.ActualWidth + 15 ||
            point.Y < 0 || point.Y > PageScroller.ViewportHeight)
        {
            ShowExecutionDrop(-1);
            return;
        }

        var low = 0;
        var high = _executionRows.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            var row = _executionRows[middle];
            var center = row.TranslatePoint(new Point(0, row.ActualHeight / 2), PageScroller).Y;
            if (point.Y < center) high = middle;
            else low = middle + 1;
        }
        ShowExecutionDrop(low);
    }

    private int ExecutionDropDestination(string id)
    {
        if (_executionDropSlot < 0) return -1;
        var from = _executionRows.FindIndex(row => Equals(row.Tag, id));
        if (from < 0) return -1;
        return _executionDropSlot > from ? _executionDropSlot - 1 : _executionDropSlot;
    }

    private void ShowExecutionDrop(int slot)
    {
        if (slot == _executionDropSlot) return;
        if (_executionDropLine is not null) _executionAdornerLayer?.Remove(_executionDropLine);
        _executionDropLine = null;
        _executionAdornerLayer = null;
        _executionDropSlot = slot;
        if (slot < 0) return;
        var last = slot == _executionRows.Count;
        var target = _executionRows[last ? slot - 1 : slot];
        _executionAdornerLayer = AdornerLayer.GetAdornerLayer(target);
        if (_executionAdornerLayer is null) return;
        _executionDropLine = new ExecutionDropLine(target, PageScroller, last);
        _executionAdornerLayer.Add(_executionDropLine);
    }

    private void CancelExecutionDrag()
    {
        var handle = _executionHandle;
        _executionHandle = null;
        _executionDragId = null;
        if (_executionScrollTimer is not null)
        {
            _executionScrollTimer.Stop();
            _executionScrollTimer.Tick -= ScrollWhileDragging;
            _executionScrollTimer = null;
        }
        ShowExecutionDrop(-1);
        if (handle?.IsMouseCaptured == true) handle.ReleaseMouseCapture();
    }

    private sealed class ExecutionDropLine(FrameworkElement target, ScrollViewer viewport, bool after) : Adorner(target)
    {
        protected override void OnRender(DrawingContext drawingContext)
        {
            var y = after ? target.ActualHeight : 0;
            var viewportY = target.TranslatePoint(new Point(0, y), viewport).Y;
            if (viewportY < 1 || viewportY > viewport.ViewportHeight - 1) return;
            drawingContext.DrawLine(new Pen(Ui.Accent, 3), new Point(4, y), new Point(target.ActualWidth - 4, y));
        }

        protected override HitTestResult? HitTestCore(PointHitTestParameters parameters) => null;
    }
}
