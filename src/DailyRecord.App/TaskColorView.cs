using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DailyRecord.Core;

namespace DailyRecord.App;

internal static class ColorStyle
{
    private static Brush Frozen(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!; b.Freeze(); return b; }
    private static readonly Brush[] Lines = [Ui.Muted, Frozen("#476CAB"), Frozen("#8056AC"), Frozen("#267A7D"), Frozen("#AA527B"), Frozen("#677386")];
    private static readonly Brush[] Fills = [Ui.SubtleSurface, Frozen("#B9E8EFFC"), Frozen("#B9EFE5FA"), Frozen("#B9DEF3EF"), Frozen("#B9FBE3ED"), Frozen("#B9E8EBF1")];
    internal static Brush Line(TaskColor color) => Lines[(int)color];
    internal static Brush Fill(TaskColor color) => Fills[(int)color];
    internal static string Hue(TaskColor color) => new[] { "无色", "蓝色", "紫色", "青色", "粉色", "灰色" }[(int)color];
    internal static string Label(UserSettings settings, TaskColor color) => $"{Hue(color)} · {TaskColors.Name(settings, color)}";
    internal static Border Badge(UserSettings settings, TaskColor color)
    {
        var badge = Ui.Badge(TaskColors.Name(settings, color)); badge.Background = Fill(color);
        ((TextBlock)badge.Child).Foreground = Line(color); return badge;
    }
    internal static ComboBox Picker(UserSettings settings, TaskColor color, string tag) => new()
    { ItemsSource = Enum.GetValues<TaskColor>().Select(c => Label(settings, c)).ToArray(), SelectedIndex = (int)color, Tag = tag };
}

internal sealed class ColorNamesDialog : Window
{
    private readonly List<TextBox> _names = [];
    internal IReadOnlyList<string> Names => _names.Select(n => n.Text.Trim()).ToList();
    internal ColorNamesDialog(Window owner, UserSettings settings)
    {
        if (owner.IsLoaded) Owner = owner;
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "分类名称"; Width = 470; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var form = Ui.Stack(Ui.Text("让颜色适合你的安排", 22, bold: true), Ui.Text("每种颜色一个名称，最多 12 字；改名会用于所有页面和分享，原任务颜色保持对应。", 12, Ui.Muted));
        foreach (var color in Enum.GetValues<TaskColor>().Where(c => c != TaskColor.None))
        {
            var input = new TextBox { Text = TaskColors.Name(settings, color), MaxLength = 12, Tag = "color-name:" + color };
            _names.Add(input); form.Children.Add(Ui.Label(ColorStyle.Hue(color))); form.Children.Add(input);
        }
        var error = Ui.Text("", 11, Ui.Amber); error.Tag = "color-name-error"; form.Children.Add(error);
        var reset = Ui.Button("恢复默认名称", () => { for (var i = 0; i < 5; i++) _names[i].Text = TaskColors.DefaultNames[i]; }); reset.Tag = "color-names-reset";
        var save = Ui.Button("保存", () =>
        {
            try { TaskColors.ValidateNames(Names); DialogResult = true; }
            catch (System.IO.InvalidDataException ex) { error.Text = ex.Message; }
        }, accent: true); save.Tag = "color-names-save";
        var cancel = Ui.Button("取消", () => DialogResult = false); cancel.IsCancel = true; cancel.Tag = "color-names-cancel";
        var actions = new WrapPanel { Margin = new Thickness(0, 20, 0, 0) }; actions.Children.Add(reset); actions.Children.Add(cancel); actions.Children.Add(save);
        form.Children.Add(actions); form.Margin = new Thickness(26); Content = form;
    }
}

public partial class MainWindow
{
    private TaskColor? _taskColorFilter;
    private bool _colorSelecting;
    private readonly HashSet<string> _colorSelectedIds = [];
    private TextBlock? _colorSelectionCaption;
    internal Func<ColorNamesDialog, bool?>? ColorNamesPresenter { get; set; }

    private void EditColorNames()
    {
        var dialog = new ColorNamesDialog(this, _service.State.Settings);
        if ((ColorNamesPresenter is { } show ? show(dialog) : dialog.ShowDialog()) == true)
            Run(() => _service.RenameTaskColors(dialog.Names));
    }
    private void ClearColorSelection() { _colorSelectedIds.Clear(); _colorSelecting = false; _colorSelectionCaption = null; }
    private void UpdateColorSelectionCaption()
    { if (_colorSelectionCaption is not null) _colorSelectionCaption.Text = $"已选 {_colorSelectedIds.Count} 项 · 批量仅修改本次"; }

    private MenuItem ColorMenu(Action<TaskColor, EditScope> apply, string id, bool recurring = false)
    {
        var root = new MenuItem { Header = "设置分类颜色", Tag = "task-color-menu:" + id };
        foreach (var color in Enum.GetValues<TaskColor>())
        {
            var captured = color;
            var item = new MenuItem { Header = ColorStyle.Label(_service.State.Settings, color),
                Icon = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = ColorStyle.Line(color) } };
            void AddChoice(MenuItem parent, EditScope scope, string label)
            {
                var choice = new MenuItem { Header = label, Tag = $"color:{id}:{captured}:{scope}" };
                choice.Click += (_, e) => { e.Handled = true; apply(captured, scope); }; parent.Items.Add(choice);
            }
            if (recurring) { AddChoice(item, EditScope.ThisOccurrence, "仅这一次"); AddChoice(item, EditScope.ThisAndFuture, "这次及后续未完成安排（保留单次分类）"); }
            else { item.Tag = $"color:{id}:{captured}:{EditScope.ThisOccurrence}"; item.Click += (_, e) => { e.Handled = true; apply(captured, EditScope.ThisOccurrence); }; }
            root.Items.Add(item);
        }
        return root;
    }

    private FrameworkElement BuildTaskColorControls(IReadOnlyList<TaskItem> today)
    {
        var panel = Ui.Stack(Ui.Text("颜色分类", 15, bold: true));
        var counts = today.Where(t => t.State == TaskState.Open).GroupBy(t => t.Color).ToDictionary(g => g.Key, g => g.Count());
        var filters = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        void FilterButton(TaskColor? color)
        {
            var count = color is { } c ? counts.GetValueOrDefault(c) : counts.Values.Sum();
            var name = color is { } chosen ? TaskColors.Name(_service.State.Settings, chosen) : "全部";
            var button = Ui.Button($"{name}  {count}", () => { _taskColorFilter = color; ClearColorSelection(); RefreshPage(); });
            button.Tag = "color-filter:" + (color?.ToString() ?? "All"); button.Margin = new Thickness(0, 0, 8, 8); button.FontSize = 12;
            button.ToolTip = $"{name} · 今日可见未完成 {count} 项（看过提醒仍计入）";
            if (color is { } value) { button.Background = ColorStyle.Fill(value); button.Foreground = value == TaskColor.None ? Ui.Ink : ColorStyle.Line(value); }
            button.BorderBrush = _taskColorFilter == color ? Ui.Accent : Ui.Line;
            button.BorderThickness = new Thickness(_taskColorFilter == color ? 2 : 1);
            filters.Children.Add(button);
        }
        FilterButton(null); foreach (var color in Enum.GetValues<TaskColor>()) FilterButton(color);
        panel.Children.Add(filters);
        var current = Ui.Text($"当前分类：{(_taskColorFilter is { } selected ? ColorStyle.Label(_service.State.Settings, selected) : "全部")} · 数量为未完成，列表也保留今日已完成", 11, Ui.Muted);
        current.Tag = "color-filter-caption"; panel.Children.Add(current);
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var batch = Ui.Button(_colorSelecting ? "退出批量" : "批量着色", () => { _colorSelecting = !_colorSelecting; _colorSelectedIds.Clear(); RefreshPage(); });
        batch.Tag = "color-batch-toggle"; actions.Children.Add(batch);
        var names = Ui.Button("分类名称", EditColorNames, quiet: true); names.Tag = "color-names"; actions.Children.Add(names);
        if (_colorSelecting)
        {
            var visible = today.Where(t => _taskColorFilter is null || t.Color == _taskColorFilter).ToList();
            _colorSelectedIds.IntersectWith(visible.Select(t => t.Id));
            var all = Ui.Button("选择本分类未完成", () => { _colorSelectedIds.UnionWith(visible.Where(t => t.State == TaskState.Open).Select(t => t.Id)); RefreshPage(); });
            all.Tag = "color-select-open"; actions.Children.Add(all);
            var clear = Ui.Button("清空选择", () => { _colorSelectedIds.Clear(); RefreshPage(); }); clear.Tag = "color-selection-clear"; actions.Children.Add(clear);
            var apply = Ui.Button("为所选着色 ▾", () => { }); apply.Tag = "color-batch-apply";
            apply.IsEnabled = _colorSelectedIds.Count > 0;
            // Checkbox changes update this button without rebuilding the page or interrupting scrolling.
            var menu = new ContextMenu(); var options = ColorMenu((color, _) => Run(() =>
            {
                _service.SetTaskColors(_service.State.Tasks.Where(t => _colorSelectedIds.Contains(t.Id)), color);
                _colorSelectedIds.Clear();
            }), "batch");
            foreach (var item in options.Items.Cast<MenuItem>().ToArray()) { options.Items.Remove(item); menu.Items.Add(item); }
            apply.ContextMenu = menu; apply.Click += (_, _) => { menu.PlacementTarget = apply; menu.IsOpen = true; }; actions.Children.Add(apply);
            _colorBatchButton = apply;
            _colorSelectionCaption = Ui.Text("", 11, Ui.Muted); _colorSelectionCaption.Tag = "color-selection-count";
            UpdateColorSelectionCaption(); panel.Children.Add(_colorSelectionCaption);
        }
        panel.Children.Add(actions); return GlassCardBorder.Create(panel, new Thickness(17));
    }
    private Button? _colorBatchButton;
    private void AddColorSelection(Grid content, TaskItem task)
    {
        if (!_colorSelecting || _page != PageKind.Today) return;
        content.ColumnDefinitions.Insert(0, new ColumnDefinition { Width = GridLength.Auto });
        foreach (UIElement child in content.Children) Grid.SetColumn(child, Grid.GetColumn(child) + 1);
        var check = new CheckBox { IsChecked = _colorSelectedIds.Contains(task.Id), VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 10, 0), ToolTip = "选择任务以批量着色", Tag = "color-select:" + task.Id };
        System.Windows.Automation.AutomationProperties.SetName(check, "选择任务：" + task.Title);
        void Changed(bool selected)
        {
            if (selected) _colorSelectedIds.Add(task.Id); else _colorSelectedIds.Remove(task.Id);
            UpdateColorSelectionCaption(); if (_colorBatchButton is not null) _colorBatchButton.IsEnabled = _colorSelectedIds.Count > 0;
        }
        check.Checked += (_, _) => Changed(true); check.Unchecked += (_, _) => Changed(false); content.Children.Add(check);
    }
    private void BuildTaskColorSettings()
    {
        var panel = Ui.Stack(Ui.Text("任务颜色分类", 18, bold: true), Ui.Text("5 种颜色可改名，按账号保存。分类与重要星标独立，备份保留颜色及名称。", 12, Ui.Muted));
        var chips = new WrapPanel { Margin = new Thickness(0, 12, 0, 12) };
        foreach (var color in Enum.GetValues<TaskColor>().Where(c => c != TaskColor.None)) chips.Children.Add(ColorStyle.Badge(_service.State.Settings, color));
        panel.Children.Add(chips); var edit = Ui.Button("修改分类名称", EditColorNames); edit.Tag = "settings-color-names"; panel.Children.Add(edit);
        PageBody.Children.Add(GlassCardBorder.Create(panel));
    }
}
