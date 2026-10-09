using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DailyRecord.Core;
using Microsoft.Win32;

namespace DailyRecord.App;

internal static class NoteImages
{
    private const int MaxDimension = 2048;
    internal static Action<Window?, NoteImage> OpenPreview { get; set; } = (owner, image) => CreatePreview(owner, image).ShowDialog();

    public static NoteImage ImportFile(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > 20_000_000) throw new InvalidDataException("请选择不超过 20 MB 的图片。");
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        if ((long)frame.PixelWidth * frame.PixelHeight > 50_000_000) throw new InvalidDataException("图片尺寸过大，请先缩小图片。");
        stream.Position = 0;
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        if (Math.Max(frame.PixelWidth, frame.PixelHeight) > MaxDimension)
        {
            if (frame.PixelWidth >= frame.PixelHeight) bitmap.DecodePixelWidth = MaxDimension;
            else bitmap.DecodePixelHeight = MaxDimension;
        }
        bitmap.EndInit(); bitmap.Freeze();
        BitmapSource oriented = bitmap;
        try
        {
            var metadata = frame.Metadata as BitmapMetadata;
            var orientation = metadata?.GetQuery("/app1/ifd/{ushort=274}") is ushort value ? value : 1;
            var transform = new TransformGroup();
            if (orientation is 2 or 4 or 5 or 7) transform.Children.Add(new ScaleTransform(-1, 1));
            var degrees = orientation switch { 3 or 4 => 180, 6 or 7 => 90, 5 or 8 => 270, _ => 0 };
            if (degrees != 0) transform.Children.Add(new RotateTransform(degrees));
            if (transform.Children.Count > 0) { oriented = new TransformedBitmap(bitmap, transform); oriented.Freeze(); }
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException or COMException) { }
        return Normalize(oriented, Path.GetFileNameWithoutExtension(file.Name) + ".png");
    }

    public static NoteImage Normalize(BitmapSource source, string name)
    {
        BitmapSource bitmap = source;
        var scale = Math.Min(1, (double)MaxDimension / Math.Max(source.PixelWidth, source.PixelHeight));
        if (scale < 1) { bitmap = new TransformedBitmap(source, new ScaleTransform(scale, scale)); bitmap.Freeze(); }
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream);
        if (stream.Length > NoteImage.MaxBytes) throw new InvalidDataException("图片压缩后仍过大，请选择更小的图片。");
        if (name.Length > 240) name = name[..230] + ".png";
        return NoteImage.Create(name, stream.ToArray());
    }

    public static BitmapSource Decode(NoteImage image, int width = 0)
    {
        using var stream = new MemoryStream(image.Data, writable: false);
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        if (width > 0) bitmap.DecodePixelWidth = width;
        bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); return bitmap;
    }

    public static Button Thumbnail(NoteImage image, Window? owner, double width = 100, double height = 68)
    {
        var button = Ui.Button("", () => { });
        button.Click += (_, _) => OpenPreview(owner ?? Window.GetWindow(button), image);
        button.Padding = new Thickness(3); button.Margin = new Thickness(0, 0, 7, 7);
        button.Width = width; button.Height = height; button.Background = Ui.Surface; button.BorderBrush = Ui.Line;
        button.ToolTip = image.FileName + " · 点击放大"; button.Tag = "note-image:" + image.Id;
        try { button.Content = new Image { Source = Decode(image, (int)(width * 2)), Stretch = Stretch.Uniform }; }
        catch (Exception ex) when (ex is IOException or FileFormatException or NotSupportedException or ArgumentException or COMException)
        { button.Content = Ui.Text("图片无法显示", 10, Ui.Muted); }
        System.Windows.Automation.AutomationProperties.SetName(button, "查看备注图片：" + image.FileName);
        return button;
    }

    public static FrameworkElement Gallery(IReadOnlyList<NoteImage> images, Window? owner)
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 9, 0, 0), Tag = "note-gallery" };
        foreach (var image in images) panel.Children.Add(Thumbnail(image, owner));
        return panel;
    }

    internal static Window CreatePreview(Window? owner, NoteImage image)
    {
        var source = Decode(image);
        var window = new Window { Title = image.FileName, Width = 940, Height = 740, MinWidth = 440, MinHeight = 350,
            WindowStartupLocation = owner?.IsLoaded == true ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen };
        if (owner?.IsLoaded == true) window.Owner = owner;
        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var picture = new Image { Source = source, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var scroller = new ScrollViewer { Content = picture, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(scroller);
        var zoom = new Slider { Minimum = 0.1, Maximum = 3, Value = Math.Min(1, Math.Min(860d / source.PixelWidth, 600d / source.PixelHeight)), Width = 170, Margin = new Thickness(12, 0, 12, 0) };
        var percentage = Ui.Text("", 11, Ui.Muted); percentage.Width = 55;
        void UpdateZoom() { picture.Width = source.PixelWidth * zoom.Value; picture.Height = source.PixelHeight * zoom.Value; percentage.Text = $"{zoom.Value:P0}"; }
        zoom.ValueChanged += (_, _) => UpdateZoom(); UpdateZoom();
        var controls = Ui.Row(Ui.Text("缩放", 12, Ui.Muted), zoom, percentage, Ui.Button("适应窗口", () =>
            zoom.Value = Math.Clamp(Math.Min(scroller.ViewportWidth / source.PixelWidth, scroller.ViewportHeight / source.PixelHeight), 0.1, 3)), Ui.Button("关闭", window.Close));
        controls.Margin = new Thickness(0, 16, 0, 0); Grid.SetRow(controls, 1); root.Children.Add(controls);
        window.Content = root; return window;
    }
}

internal sealed class NoteImageEditor : Border
{
    private readonly List<NoteImage> _images;
    private readonly WrapPanel _gallery = new();
    private readonly TextBlock _message = Ui.Text("", 10, Ui.Muted);
    private readonly Button _upload;
    private readonly Button _paste;
    public IReadOnlyList<NoteImage> Images => _images.ToArray();
    internal Func<IReadOnlyList<string>> SelectFiles { get; set; }
    internal Func<BitmapSource?> ReadClipboardImage { get; set; } = Clipboard.GetImage;
    internal Task PendingImport { get; private set; } = Task.CompletedTask;

    public NoteImageEditor(IEnumerable<NoteImage>? images = null)
    {
        _images = (images ?? []).ToList();
        SelectFiles = () =>
        {
            var picker = new OpenFileDialog { Title = "选择备注图片（可多选）", Multiselect = true,
                Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|所有文件|*.*" };
            var owner = Window.GetWindow(this);
            var accepted = owner?.IsLoaded == true ? picker.ShowDialog(owner) : picker.ShowDialog();
            return accepted == true ? picker.FileNames : [];
        };
        _upload = Ui.Button("＋ 上传图片", async () => { PendingImport = AddFilesAsync(SelectFiles()); await PendingImport; }); _upload.Tag = "upload-note-images";
        _paste = Ui.Button("粘贴图片", Paste); _paste.Tag = "paste-note-image";
        var hint = Ui.Text("最多 8 张 · 单张原图不超过 20 MB · 点击缩略图放大", 10, Ui.Muted);
        hint.Margin = new Thickness(0, 8, 0, 8); _message.Margin = new Thickness(0, 5, 0, 0);
        Child = Ui.Stack(Ui.Row(_upload, _paste), hint, _gallery, _message);
        Margin = new Thickness(0, 12, 0, 0); Render();
    }

    internal async Task AddFilesAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        _upload.IsEnabled = _paste.IsEnabled = false; _message.Text = "正在处理图片…";
        try
        {
            if (paths.Count > NoteImage.MaxPerNote) throw new InvalidDataException("一次最多选择 8 张图片。");
            var imported = await Task.Run(() => paths.Select(NoteImages.ImportFile).ToList());
            Append(imported); _message.Text = "图片已添加，保存任务后生效。";
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or FileFormatException or UnauthorizedAccessException or NotSupportedException or ArgumentException or COMException)
        { _message.Text = ex is InvalidDataException ? ex.Message : "无法读取图片，请选择有效的 PNG、JPG、BMP、GIF 或 TIFF 图片。"; }
        finally { _upload.IsEnabled = _paste.IsEnabled = true; }
    }

    private void Paste()
    {
        try
        {
            var bitmap = ReadClipboardImage();
            if (bitmap is null) { _message.Text = "剪贴板中没有图片，请先复制图片。"; return; }
            Append([NoteImages.Normalize(bitmap, "粘贴图片.png")]); _message.Text = "图片已添加，保存任务后生效。";
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or FileFormatException or NotSupportedException or ArgumentException or ExternalException)
        { _message.Text = ex is InvalidDataException ? ex.Message : "暂时无法粘贴图片，请重试或使用上传图片。"; }
    }

    private void Append(IEnumerable<NoteImage> images)
    {
        var combined = _images.Concat(images).DistinctBy(i => i.Id).ToList();
        if (combined.Count > NoteImage.MaxPerNote) throw new InvalidDataException("每则备注最多 8 张图片，请先移除一张。");
        _images.Clear(); _images.AddRange(combined); Render();
    }

    private void Render()
    {
        _gallery.Children.Clear();
        foreach (var image in _images)
        {
            var thumbnail = NoteImages.Thumbnail(image, Window.GetWindow(this), 118, 80);
            var name = Ui.Text(image.FileName, 10, Ui.Muted); name.Width = 118; name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis;
            var remove = Ui.Button("移除", () => { _images.Remove(image); Render(); _message.Text = "已从备注移除，保存任务后生效。"; }, quiet: true);
            remove.Tag = "remove-note-image:" + image.Id; remove.FontSize = 10; remove.Padding = new Thickness(4);
            var item = Ui.Stack(thumbnail, name, remove); item.Width = 125; item.Margin = new Thickness(0, 0, 5, 8); _gallery.Children.Add(item);
        }
    }
}
