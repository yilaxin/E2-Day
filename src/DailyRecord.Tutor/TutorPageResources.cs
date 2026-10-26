using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DailyRecord.App;

public sealed partial class TutorWindow
{
    private readonly NoteThumbnailCache _noteThumbnails = new();
    private object? _thumbnailState;
    private string _todoDraft = "";
    private readonly CancellationTokenSource _lifetime = new();
    internal NoteThumbnailCache Thumbnails => _noteThumbnails;

    private UIElement LessonGallery(IEnumerable<string> ids)
    {
        if (!ReferenceEquals(_thumbnailState, _service.State)) { _noteThumbnails.Clear(); _thumbnailState = _service.State; }
        var panel = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) };
        foreach (var id in ids.Distinct())
        {
            var imageId = id;
            var button = Ui.Button("", () => NoteImages.OpenPreview(this, _store.Images([imageId]).Single()));
            button.Tag = "note-image:" + id; button.Width = 100; button.Height = 68; button.Padding = new Thickness(3); button.Margin = new Thickness(0, 0, 7, 7);
            try
            {
                var entry = _noteThumbnails.Get(id, key => _store.Images([key]).FirstOrDefault());
                if (entry is null) continue;
                button.Content = new Image { Source = entry.Pixels, Stretch = Stretch.Uniform };
                button.ToolTip = entry.FileName + " · 点击放大";
                System.Windows.Automation.AutomationProperties.SetName(button, "查看备注图片：" + entry.FileName);
            }
            catch (Exception ex) when (ex is IOException or FileFormatException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException)
            { button.Content = Ui.Text("图片无法显示", 10, Ui.Muted); }
            panel.Children.Add(button);
        }
        return panel;
    }

    private void ClearPageResources()
    {
        bool InPage(DependencyObject? node)
        {
            for (; node is not null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
                if (ReferenceEquals(node, _body) || ReferenceEquals(node, _actions)) return true;
            return false;
        }
        if (InPage(Keyboard.FocusedElement as DependencyObject))
        { if (_nav.TryGetValue(_page, out var navigation)) Keyboard.Focus(navigation); else Keyboard.ClearFocus(); }
        if (InPage(FocusManager.GetFocusedElement(this) as DependencyObject)) FocusManager.SetFocusedElement(this, null);
        RetireChildren(_body); RetireChildren(_actions); DashboardReport = null;
    }

    private static void RetireChildren(Panel panel)
    {
        var children = panel.Children.OfType<UIElement>().ToArray(); panel.Children.Clear();
        foreach (var child in children) RetireTree(child);
    }
    internal static void RetireTree(UIElement element)
    {
        element.CacheMode = null;
        switch (element)
        {
            case Panel panel: RetireChildren(panel); break;
            case Decorator decorator:
                var decorated = decorator.Child; decorator.Child = null; if (decorated is not null) RetireTree(decorated); break;
            case ContentControl control when control.Content is UIElement content:
                control.Content = null; RetireTree(content); break;
            case Image image: image.Source = null; break;
        }
    }
}
