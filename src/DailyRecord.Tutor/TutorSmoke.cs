using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DailyRecord.Core;

namespace DailyRecord.App;

internal static partial class TutorSmoke
{
    private static int _checks;
    private static void Check(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); _checks++; }
    internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root; foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) foreach (var next in Descendants(child)) yield return next;
    }
    private static T Find<T>(Window window, string tag) where T : FrameworkElement => Descendants(window).OfType<T>().FirstOrDefault(x => Equals(x.Tag, tag)) ?? throw new InvalidOperationException("找不到控件：" + tag);
    private static void Click(Window window, string tag) { Prepare(window); Find<Button>(window, tag).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
    private static void Text(Window window, string tag, string text) => Find<TextBox>(window, tag).Text = text;
    private static void Prepare(Window window)
    {
        foreach (var element in Descendants(window).OfType<UIElement>()) element.InvalidateVisual();
        window.ApplyTemplate(); var root = (FrameworkElement)window.Content;
        root.Measure(new Size(window.Width, window.Height)); root.Arrange(new Rect(0, 0, window.Width, window.Height)); root.UpdateLayout();
    }
    private static void Snapshot(Window window, string path)
    {
        Prepare(window); var root = (FrameworkElement)window.Content;
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.Width, (int)window.Height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual(); using (var dc = background.RenderOpen()) dc.DrawRectangle(Ui.Page, null, new Rect(0, 0, window.Width, window.Height)); bitmap.Render(background); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
    public static async Task Run(TutorWindow window, TutorService service, TutorStore store, string output)
    {
        Directory.CreateDirectory(output); window.Width = 1380; window.Height = 1000;
        var vault = new AccountVault(Path.Combine(store.DirectoryPath, "account-test"));
        var accountWindow = new AccountWindow(vault, true); Prepare(accountWindow); Text(accountWindow, "account-name", "示例老师");
        Find<PasswordBox>(accountWindow, "account-password").Password = "Tutor-Example-2026"; Find<PasswordBox>(accountWindow, "account-confirm").Password = "Tutor-Example-2026"; Click(accountWindow, "account-next"); Check(accountWindow.Session is not null, "家教版创建账号失败");
        var login = new AccountWindow(vault, true); Find<ComboBox>(login, "account-mode").SelectedIndex = 0; Text(login, "account-name", "示例老师"); Find<PasswordBox>(login, "account-password").Password = "Tutor-Example-2026"; Click(login, "account-next"); Check(login.Session?.AccountId == accountWindow.Session!.AccountId, "家教版登录失败");
        var studentDialog = new TutorStudentDialog(service, testing: true); Text(studentDialog, "student-name", "小林（示例）"); Text(studentDialog, "student-grade", "初二"); Text(studentDialog, "student-subject", "数学"); Text(studentDialog, "student-rate", "150"); Text(studentDialog, "student-notes", "擅长直观理解，应用题需要多画图。目标：建立清晰的解题步骤。"); Click(studentDialog, "dialog-save"); Check(studentDialog.Saved, "学生编辑窗口保存失败");
        var first = service.State.Students.Single();
        var invalid = new TutorStudentDialog(service, testing: true); Text(invalid, "student-name", "无效示例"); Text(invalid, "student-rate", "-1"); Click(invalid, "dialog-save"); Check(!invalid.Saved && service.State.Students.Count == 1, "无效时薪未被界面拒绝");
        var second = new TutorStudent { Name = "小周（示例）", Grade = "高一", Subject = "英语", HourlyRate = 160, Notes = "通过短文语境理解词汇，逐步提升阅读速度。" }; service.SaveStudent(second);
        var third = new TutorStudent { Name = "小陈（示例）", Grade = "小学五年级", Subject = "数学", HourlyRate = 120 }; service.SaveStudent(third);
        var newLesson = new TutorLessonDialog(service, store, window.Today, testing: true); Find<ComboBox>(newLesson, "lesson-student").SelectedValue = first.Id; Text(newLesson, "lesson-start", "16:00"); Text(newLesson, "lesson-hours", "1.5"); Find<ComboBox>(newLesson, "lesson-status").SelectedIndex = 1;
        Text(newLesson, "lesson-content", "一元一次方程应用题：从文字条件中找出等量关系。"); Text(newLesson, "lesson-progress", "能独立列出基础题的方程，复杂条件仍需提示。"); Text(newLesson, "lesson-problems", "容易遗漏单位换算，审题时跳过关键信息。"); Text(newLesson, "lesson-methods", "先用线段图整理数量关系，再用不同颜色标记已知量和未知量。"); Text(newLesson, "lesson-next", "完成 4 道变式题，下节课复盘列式过程。"); Text(newLesson, "lesson-private", "内部教学笔记：下一节增加口头复述环节。");
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) { dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 440, 160)); dc.DrawLine(new Pen(Brushes.SteelBlue, 4), new Point(35, 65), new Point(390, 65)); dc.DrawLine(new Pen(Brushes.Goldenrod, 4), new Point(35, 110), new Point(240, 110)); }
        var image = new RenderTargetBitmap(440, 160, 96, 96, PixelFormats.Pbgra32); image.Render(visual); newLesson.ImagesEditor.ReadClipboardImage = () => image; Click(newLesson, "paste-note-image");
        Click(newLesson, "dialog-save"); Check(newLesson.Saved && service.State.Lessons.Single().ImageIds.Count == 1, "录课和粘贴图片保存失败");
        var taught = service.State.Lessons.Single();
        using (var precisionStore = new TutorStore(Path.Combine(store.DirectoryPath, "precision-test")))
        {
            var precisionService = new TutorService(precisionStore); precisionService.SaveStudent(first);
            var oneMinute = new TutorLesson { StudentId = first.Id, Subject = "精度示例", Date = window.Today.AddDays(-1), Minutes = 1, HourlyRate = 150.125m };
            precisionService.SaveLesson(oneMinute); var precise = new TutorLessonDialog(precisionService, precisionStore, oneMinute.Date, oneMinute, testing: true); Click(precise, "dialog-save"); Check(precise.Saved && precisionService.State.Lessons.Single().HourlyRate == 150.125m && precisionService.State.Lessons.Single().Minutes == 1, "分钟与时薪精度在编辑时丢失");
        }
        var planned = new TutorLesson { StudentId = second.Id, Subject = second.Subject, HourlyRate = second.HourlyRate, Date = window.Today, Start = new(19, 0), Minutes = 90 }; service.SaveLesson(planned);
        var complete = new TutorLessonDialog(service, store, window.Today, planned, true, true); Click(complete, "dialog-save"); Check(complete.Saved && service.Statistics(window.Today, window.Today).Income == 465, "确认授课统计错误");
        var cancelDialog = new TutorLessonDialog(service, store, window.Today, service.State.Lessons.Single(l => l.Id == planned.Id), testing: true); Find<ComboBox>(cancelDialog, "lesson-status").SelectedIndex = 2; Click(cancelDialog, "dialog-save"); Check(cancelDialog.Saved && service.Statistics(window.Today, window.Today).Income == 225, "取消课程未更新收入");
        var undone = TutorRules.Copy(planned); undone.Status = LessonStatus.Planned; service.SaveLesson(undone);
        var weekly = new TutorLessonDialog(service, store, window.Today.AddDays(2), testing: true); Find<ComboBox>(weekly, "lesson-student").SelectedValue = third.Id; Find<CheckBox>(weekly, "lesson-weekly").IsChecked = true; Click(weekly, "dialog-save"); Check(weekly.Saved && service.State.Series.Count == 1, "重复排课窗口失败");
        var occurrence = service.Lessons(window.Today.AddDays(9), window.Today.AddDays(9)).Single();
        var future = new TutorLessonDialog(service, store, occurrence.Date, occurrence, testing: true); Find<ComboBox>(future, "lesson-scope").SelectedIndex = 1; Text(future, "lesson-rate", "130"); Click(future, "dialog-save"); Check(future.Saved && service.Lessons(window.Today.AddDays(16), window.Today.AddDays(16)).Single().HourlyRate == 130, "修改后续课程界面失败");
        var old = new TutorLesson { StudentId = third.Id, Subject = third.Subject, HourlyRate = third.HourlyRate, Date = window.Today.AddDays(-2), Start = new(17, 0), Minutes = 60 }; service.SaveLesson(old);
        for (var i = 1; i <= 5; i++) { service.SaveLesson(new TutorLesson { StudentId = i % 2 == 0 ? first.Id : second.Id, Subject = i % 2 == 0 ? "数学" : "英语", HourlyRate = i % 2 == 0 ? 150 : 160, Date = window.Today.AddDays(-i * 2), Status = LessonStatus.Taught, Minutes = 90, Content = i % 2 == 0 ? "函数图像与数量关系" : "阅读理解：段落主旨与词义推断", Progress = "基础掌握良好，继续巩固。", Methods = "结合例题，引导学生口述解题思路。" }); }
        service.AddTodo("准备周六的应用题练习"); service.AddTodo("整理本周课程反馈");
        string copied = ""; window.WriteClipboard = text => copied = text;
        window.Navigate(0); Prepare(window); Click(window, "copy:" + taught.Id); Check(copied.Contains("等量关系") && !copied.Contains("内部教学笔记"), "反馈复制泄露内部备注或丢失内容"); Snapshot(window, Path.Combine(output, "tutor-today.png"));
        window.WriteClipboard = _ => throw new UnauthorizedAccessException("测试剪贴板拒绝访问"); Click(window, "copy:" + taught.Id); Check(Descendants(window).OfType<TextBlock>().Any(t => t.Text.Contains("暂时无法访问剪贴板")), "剪贴板失败没有反馈"); window.WriteClipboard = text => copied = text;
        window.SelectDate(window.Today); Check(Descendants(window).OfType<Button>().Any(b => Equals(b.Tag, "day:2026-10-15")), "月历控件缺失"); Snapshot(window, Path.Combine(output, "tutor-calendar.png"));
        window.SelectDate(window.Today.AddDays(14)); Prepare(window);
        Descendants(window).OfType<ScrollViewer>().First().ScrollToEnd();
        Snapshot(window, Path.Combine(output, "tutor-calendar-action.png"));
        window.SelectStudent(first.Id); Snapshot(window, Path.Combine(output, "tutor-student.png"));
        window.Navigate(3); Snapshot(window, Path.Combine(output, "tutor-income.png"));
        window.Navigate(4); Snapshot(window, Path.Combine(output, "tutor-records.png")); window.Navigate(5); Snapshot(window, Path.Combine(output, "tutor-settings.png"));
        var edit = new TutorLessonDialog(service, store, taught.Date, taught, testing: true); Snapshot(edit, Path.Combine(output, "tutor-editor.png"));
        var reused = new TutorLessonDialog(service, store, window.Today, planned, testing: true); Find<TextBox>(reused, "reference-keyword").Text = "线段图";
        var reference = Descendants(reused).OfType<Button>().Single(b => Equals(b.Content, "查找")); reference.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var use = Descendants(reused).OfType<Button>().Single(b => Equals(b.Content, "引用教学内容、方法与下次计划")); use.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(Find<TextBox>(reused, "lesson-methods").Text.Contains("线段图") && string.IsNullOrEmpty(Find<TextBox>(reused, "lesson-progress").Text), "引用历史覆盖学生评价");
        var preview = NoteImages.CreatePreview(null, store.Images(taught.ImageIds).Single()); Prepare(preview); Check(preview.Content is not null, "图片预览失败");
        var before = service.State.Lessons.Count; var backup = Path.Combine(store.DirectoryPath, "ui-roundtrip.json"); store.Export(backup, service.State); service.Restore(backup); Check(service.State.Lessons.Count == before && store.Images(taught.ImageIds).Count == 1, "界面图文备份往返失败");
        window.Width = 1000; window.Height = 650; window.Navigate(0); Snapshot(window, Path.Combine(output, "tutor-compact.png")); Check(window.ActualWidth >= 0, "小窗口布局失败");
        await VerifyAppearanceAndScrolling(window, service, output);
        var result = $"PASS Tutor WPF: {_checks} checks\nVersion: {Assembly.GetExecutingAssembly().GetName().Version}\nArchitecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}\nIsolatedData: {store.DirectoryPath}\nStudents: {service.State.Students.Count}\nLessons: {service.State.Lessons.Count}\nScreenshots: 9\n"; File.WriteAllText(Path.Combine(output, "result.txt"), result);
        await Task.CompletedTask;
    }
}
