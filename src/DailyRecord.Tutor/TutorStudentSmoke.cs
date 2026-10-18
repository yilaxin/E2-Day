using System.IO;
using System.Windows;
using System.Windows.Controls;
using DailyRecord.Core;

namespace DailyRecord.App;

internal static partial class TutorSmoke
{
    private static void VerifyStudentProfile(TutorWindow window, TutorService service, TutorStudent first, string output)
    {
        Check(first.School == "向阳中学（示例）" && first.TeachingLocation == "线上授课（示例）" && first.LearningLevel == "中等" && first.LearningFocus.SequenceEqual(new[] { "基础巩固", "查漏补缺" }), "新增学生没有保存学校、地点和学习选择");
        var edit = new TutorStudentDialog(service, first, testing: true);
        Check(Find<TextBox>(edit, "student-school").Text == first.School && (string)Find<ComboBox>(edit, "student-pace").SelectedItem == first.LearningPace && Find<CheckBox>(edit, "student-focus:查漏补缺").IsChecked == true, "重开编辑器丢失学习选项");
        edit.ShowInTaskbar = false; edit.Show();
        Snapshot(edit, Path.Combine(output, "tutor-student-edit.png"));
        Find<TabControl>(edit, "student-tabs").SelectedIndex = 1;
        Snapshot(edit, Path.Combine(output, "tutor-student-learning.png"));
        edit.Width = 560; edit.Height = 550;
        foreach (var tab in new[] { 0, 1 })
        {
            Find<TabControl>(edit, "student-tabs").SelectedIndex = tab; Prepare(edit);
            var save = Find<Button>(edit, "dialog-save"); var root = (FrameworkElement)edit.Content;
            var position = save.TransformToAncestor(root).Transform(new Point());
            Check(save.IsVisible && save.ActualHeight > 0 && position.Y >= 0 && position.Y + save.ActualHeight <= root.ActualHeight, "最小学生窗口的保存按钮不可见");
        }
        Find<ComboBox>(edit, "student-level").SelectedItem = "良好";
        Find<ComboBox>(edit, "student-pace").SelectedIndex = 0;
        Find<CheckBox>(edit, "student-focus:基础巩固").IsChecked = false;
        Find<CheckBox>(edit, "student-focus:拔高拓展").IsChecked = true;
        Click(edit, "dialog-save"); var changed = service.State.Students.Single();
        Check(edit.Saved && changed.LearningLevel == "良好" && changed.LearningPace == "" && changed.LearningFocus.SequenceEqual(new[] { "查漏补缺", "拔高拓展" }) && changed.Notes == first.Notes, "修改或取消选项没有生效、原备注丢失");
        edit.Close();
        var cleared = new TutorStudentDialog(service, changed, testing: true);
        Text(cleared, "student-school", ""); Text(cleared, "student-location", ""); Find<ComboBox>(cleared, "student-level").SelectedIndex = 0;
        foreach (var label in TutorStudentProfile.Focuses) Find<CheckBox>(cleared, "student-focus:" + label).IsChecked = false;
        Click(cleared, "dialog-save");
        Check(service.State.Students.Single().School == "" && service.State.Students.Single().LearningFocus.Count == 0 && service.State.Students.Single().Notes == first.Notes, "清空学生选项失败");
        var cancelled = new TutorStudentDialog(service, service.State.Students.Single(), testing: true);
        Text(cancelled, "student-school", "未保存的学校");
        Check(service.State.Students.Single().School == "", "未保存编辑污染资料");
        var legacy = new TutorStudentDialog(service, new TutorStudent { Name = "旧学生", Notes = "旧备注" }, testing: true);
        Check(Find<ComboBox>(legacy, "student-level").SelectedIndex == 0 && Find<ComboBox>(legacy, "student-pace").SelectedIndex == 0 && Find<TextBox>(legacy, "student-notes").Text == "旧备注", "旧资料默认选择或备注有误");
        service.SaveStudent(first); window.SelectStudent(first.Id); Prepare(window);
        Check(Descendants(window).OfType<TextBlock>().Any(t => t.Text.Contains(first.School)) && Descendants(window).OfType<TextBlock>().Any(t => t.Text == "基础巩固"), "学生档案未显示学校或标签");
        Descendants(window).OfType<Button>().Single(b => Equals(b.Content, "‹ 全部学生")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Text(window, "student-search", "向阳"); Descendants(window).OfType<Button>().Single(b => Equals(b.Content, "搜索学生")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(Descendants(window).OfType<TextBlock>().Any(t => t.Text == first.Name), "学校搜索找不到学生");
        Text(window, "student-search", "无关学校"); Descendants(window).OfType<Button>().Single(b => Equals(b.Content, "搜索学生")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(!Descendants(window).OfType<Button>().Any(b => Equals(b.Content, "查看档案")), "学生搜索没有排除不匹配记录");
        Text(window, "student-search", ""); Descendants(window).OfType<Button>().Single(b => Equals(b.Content, "搜索学生")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
