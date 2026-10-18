using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using DailyRecord.Core;

namespace DailyRecord.App;

internal abstract class TutorDialog : Window
{
    protected readonly StackPanel Fields = new();
    protected readonly TextBlock Error = Ui.Text("", 12, Ui.Amber);
    protected readonly bool Testing;
    protected readonly StackPanel Footer;
    public bool Saved { get; private set; }
    protected TutorDialog(string title, bool testing)
    {
        Testing = testing; Title = title; Width = 700; Height = Math.Min(850, SystemParameters.WorkArea.Height - 60); MinWidth = 560; MinHeight = 550; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var grid = new Grid { Background = Ui.Page }; grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Content = grid;
        grid.Margin = new Thickness(25);
        grid.Children.Add(new ScrollViewer { Content = Fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 12, 0) });
        Footer = Ui.Stack(Error); Footer.Margin = new Thickness(0, 15, 0, 0); Grid.SetRow(Footer, 1); grid.Children.Add(Footer);
        Fields.Children.Add(Ui.Text(title, 25, Ui.Accent, true));
    }
    protected void Buttons(Action save)
    {
        var button = Ui.Button("保存", () =>
        {
            try { save(); Saved = true; if (!Testing) DialogResult = true; }
            catch (Exception ex) { Error.Text = ex.Message; }
        }, true); button.Tag = "dialog-save";
        Footer.Children.Add(Ui.Row(button, Ui.Button("取消", () => { if (!Testing) DialogResult = false; })));
    }
    protected TextBox Field(string label, string value, string tag, bool multi = false)
    {
        Fields.Children.Add(Ui.Label(label)); var box = new TextBox { Text = value, Tag = tag, MaxLength = multi ? 20000 : 200, AcceptsReturn = multi, TextWrapping = multi ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multi ? 88 : 0, VerticalScrollBarVisibility = multi ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden };
        Fields.Children.Add(box); return box;
    }
    protected static decimal Number(string text, string label)
    {
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.GetCultureInfo("zh-CN"), out var value)) throw new ArgumentException(label + "请填写有效数字。"); return value;
    }
}

internal sealed class TutorStudentDialog : TutorDialog
{
    public TutorStudentDialog(TutorService service, TutorStudent? existing = null, bool testing = false) : base(existing is null ? "新增学生" : "编辑学生资料", testing)
    {
        var student = TutorRules.Copy(existing ?? new TutorStudent());
        var name = Field("姓名或昵称 *", student.Name, "student-name"); name.MaxLength = 80;
        var grade = Field("年级", student.Grade, "student-grade");
        var school = Field("就读学校（选填）", student.School, "student-school");
        var location = Field("授课地点（选填，如学生家、教室或线上）", student.TeachingLocation, "student-location");
        var subject = Field("默认科目", student.Subject, "student-subject");
        var rate = Field("默认时薪（元 / 小时）", student.HourlyRate.ToString("0.############################"), "student-rate");
        var archived = new CheckBox { Content = "已结课（历史课程与收入继续保留）", IsChecked = student.Archived, Margin = new Thickness(0, 18, 0, 12), Tag = "student-archived" }; Fields.Children.Add(archived);
        var learningStart = Fields.Children.Count;
        Fields.Children.Add(Ui.Text("可按当前情况选择，作为备课参考；也可以先不选，以后再调整。", 12, Ui.Muted));
        ComboBox Choice(string label, string tag, IReadOnlyList<string> options, string selected)
        {
            Fields.Children.Add(Ui.Label(label));
            var box = new ComboBox { ItemsSource = new[] { "暂不选择" }.Concat(options).ToArray(), SelectedIndex = selected.Length == 0 ? 0 : options.ToList().IndexOf(selected) + 1, Tag = tag };
            Fields.Children.Add(box); return box;
        }
        var level = Choice("基础水平（单选）", "student-level", TutorStudentProfile.Levels, student.LearningLevel);
        var pace = Choice("学习节奏（单选）", "student-pace", TutorStudentProfile.Paces, student.LearningPace);
        Fields.Children.Add(Ui.Label("辅导重点（可多选）"));
        var focus = new WrapPanel();
        foreach (var label in TutorStudentProfile.Focuses)
            focus.Children.Add(new CheckBox { Content = label, Tag = "student-focus:" + label, IsChecked = student.LearningFocus.Contains(label), Margin = new Thickness(0, 5, 20, 10) });
        Fields.Children.Add(focus);
        var notes = Field("学习特点与长期目标（自由补充）", student.Notes, "student-notes", true);
        notes.MinHeight = 130;
        var basicFields = new StackPanel(); var learningFields = new StackPanel();
        var children = Fields.Children.Cast<UIElement>().Skip(1).ToList();
        foreach (var child in children) Fields.Children.Remove(child);
        for (var i = 0; i < children.Count; i++) (i < learningStart - 1 ? basicFields : learningFields).Children.Add(children[i]);
        var tabs = new TabControl { Tag = "student-tabs", Background = System.Windows.Media.Brushes.Transparent, BorderBrush = Ui.Line, Margin = new Thickness(0, 18, 0, 0) };
        tabs.Items.Add(new TabItem { Header = "基本资料", Content = basicFields, Padding = new Thickness(16, 9, 16, 9) });
        tabs.Items.Add(new TabItem { Header = "学习特点", Content = learningFields, Padding = new Thickness(16, 9, 16, 9) });
        Fields.Children.Add(tabs);
        Buttons(() =>
        {
            student.Name = name.Text.Trim(); student.Grade = grade.Text.Trim(); student.Subject = subject.Text.Trim(); student.HourlyRate = Number(rate.Text, "时薪");
            student.School = school.Text.Trim(); student.TeachingLocation = location.Text.Trim();
            student.LearningLevel = level.SelectedIndex <= 0 ? "" : (string)level.SelectedItem;
            student.LearningPace = pace.SelectedIndex <= 0 ? "" : (string)pace.SelectedItem;
            student.LearningFocus = focus.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Content).ToList();
            student.Notes = notes.Text; student.Archived = archived.IsChecked == true; service.SaveStudent(student);
        });
    }
}

internal sealed class TutorLessonDialog : TutorDialog
{
    private readonly NoteImageEditor _images;
    internal NoteImageEditor ImagesEditor => _images;
    public TutorLessonDialog(TutorService service, TutorStore store, DateOnly date, TutorLesson? existing = null, bool complete = false, bool testing = false) : base(complete ? "确认授课与填写反馈" : existing is null ? "添加课程" : "课程与教学反馈", testing)
    {
        var lesson = TutorRules.Copy(existing ?? new TutorLesson { Date = date });
        var isNew = existing is null || existing.GroupId is null && !service.State.Lessons.Any(l => l.Id == existing.Id);
        if (complete) lesson.Status = LessonStatus.Taught;
        Fields.Children.Add(Ui.Text("时长以本节实际情况为准。待上课与已取消课程不计入收入。", 12, Ui.Muted));
        Fields.Children.Add(Ui.Label("学生 *"));
        var students = service.State.Students.Where(s => !s.Archived || s.Id == lesson.StudentId).OrderBy(s => s.Name).ToList();
        var student = new ComboBox { ItemsSource = students, DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValue = lesson.StudentId, Padding = new Thickness(10), Tag = "lesson-student" }; Fields.Children.Add(student);
        var subject = Field("科目 *", lesson.Subject, "lesson-subject");
        Fields.Children.Add(Ui.Label("实际 / 计划授课日期 *")); var day = new DatePicker { SelectedDate = lesson.Date.ToDateTime(TimeOnly.MinValue), DisplayDateStart = new DateTime(1901, 1, 1), DisplayDateEnd = new DateTime(2100, 12, 31), Tag = "lesson-date" }; Fields.Children.Add(day);
        var start = Field("开始时间（24 小时制，例如 18:30）", lesson.Start.ToString("HH:mm"), "lesson-start");
        var hours = Field("授课时长（小时，例如 1.5）", (lesson.Minutes / 60m).ToString("0.################"), "lesson-hours");
        var rate = Field("本节时薪（元 / 小时）", lesson.HourlyRate.ToString("0.############################"), "lesson-rate");
        var estimate = Ui.Text("", 13, Ui.Accent, true); estimate.Margin = new Thickness(0, 10, 0, 0); Fields.Children.Add(estimate);
        void Estimate() { if (decimal.TryParse(hours.Text, out var h) && decimal.TryParse(rate.Text, out var r) && h is >= 0 and <= 24 && r is >= 0 and <= 1000000) estimate.Text = $"本节金额：¥{decimal.Round(h * r, 2, MidpointRounding.AwayFromZero):N2}（已授课后计入收入）"; else estimate.Text = "请检查时长和时薪。"; }
        hours.TextChanged += (_, _) => Estimate(); rate.TextChanged += (_, _) => Estimate(); Estimate();
        Fields.Children.Add(Ui.Label("课程状态")); var status = new ComboBox { ItemsSource = new[] { "待上课", "已授课", "已取消" }, SelectedIndex = (int)lesson.Status, Padding = new Thickness(10), Tag = "lesson-status" }; Fields.Children.Add(status);
        var weekly = new CheckBox { Content = "每周同一天、同一时间重复排课", IsChecked = false, Visibility = isNew ? Visibility.Visible : Visibility.Collapsed, Margin = new Thickness(0, 16, 0, 8), Tag = "lesson-weekly" }; Fields.Children.Add(weekly);
        var scope = new ComboBox { ItemsSource = new[] { "仅修改这一次", "修改这一次及以后（保留已授课、已取消记录）" }, SelectedIndex = 0, Padding = new Thickness(8), Visibility = lesson.GroupId is not null && !complete && lesson.Status == LessonStatus.Planned ? Visibility.Visible : Visibility.Collapsed, Tag = "lesson-scope" }; Fields.Children.Add(scope);
        var feedbackIndex = Fields.Children.Count;
        Fields.Children.Add(Ui.Label("课程反馈 · 可先保存，稍后补写"));
        var content = Field("本节教学内容", lesson.Content, "lesson-content", true);
        var progress = Field("学生掌握情况", lesson.Progress, "lesson-progress", true);
        var problems = Field("存在的问题", lesson.Problems, "lesson-problems", true);
        var methods = Field("采用的方法及效果", lesson.Methods, "lesson-methods", true);
        var next = Field("作业与下次计划", lesson.NextPlan, "lesson-next", true);
        var privateNotes = Field("自己的内部备注（复制给家长时不包含）", lesson.PrivateNotes, "lesson-private", true);
        _images = new NoteImageEditor(store.Images(lesson.ImageIds), "课程"); Fields.Children.Add(Ui.Label("题目、作业与板书图片")); Fields.Children.Add(_images);
        var referenceIndex = Fields.Children.Count;
        Fields.Children.Add(Ui.Label("参考历史教学方法"));
        var keyword = new TextBox { Tag = "reference-keyword", ToolTip = "搜索历史课程内容、问题和教学方法" };
        var references = new ComboBox { DisplayMemberPath = "Label", SelectedValuePath = "Lesson", MaxDropDownHeight = 220, Padding = new Thickness(8), Tag = "reference-choice" };
        void FindReferences()
        {
            references.ItemsSource = service.State.Lessons.Where(l => l.Id != lesson.Id && l.Status == LessonStatus.Taught && l.HasFeedback &&
                (string.IsNullOrWhiteSpace(keyword.Text) || string.Join("\n", l.Content, l.Problems, l.Methods).Contains(keyword.Text.Trim(), StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(l => l.Date).Select(l => new Reference($"{l.Date:yyyy-MM-dd} · {l.Subject} · {Short(l.Content)}", l)).ToList(); references.SelectedIndex = 0;
        }
        Fields.Children.Add(Ui.Row(keyword, Ui.Button("查找", FindReferences))); keyword.Width = 350; Fields.Children.Add(references);
        Fields.Children.Add(Ui.Button("引用教学内容、方法与下次计划", () =>
        {
            if (references.SelectedItem is not Reference reference) return;
            if (!Testing && (!string.IsNullOrWhiteSpace(content.Text) || !string.IsNullOrWhiteSpace(methods.Text) || !string.IsNullOrWhiteSpace(next.Text)) && MessageBox.Show(this, "引用将替换当前教学内容、方法与下次计划，其他内容保留。是否继续？", "参考历史", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            content.Text = reference.Lesson.Content; methods.Text = reference.Lesson.Methods; next.Text = reference.Lesson.NextPlan;
        })); FindReferences();
        student.SelectionChanged += (_, _) => { if (isNew && student.SelectedItem is TutorStudent s) { subject.Text = s.Subject; rate.Text = s.HourlyRate.ToString("0.############################"); } };
        if (isNew && student.SelectedItem is null && students.Count > 0) student.SelectedIndex = 0;
        var originalFields = Fields.Children.Cast<UIElement>().ToArray(); Fields.Children.Clear(); Fields.Children.Add(originalFields[0]); Fields.Children.Add(originalFields[1]);
        var tabs = new TabControl { Background = Ui.Page, BorderBrush = Ui.Line, Margin = new Thickness(0, 18, 0, 0) };
        void Tab(string name, int from, int to)
        {
            var body = new StackPanel { Margin = new Thickness(12, 0, 12, 14) };
            for (var i = from; i < to; i++) body.Children.Add(originalFields[i]);
            tabs.Items.Add(new TabItem { Header = name, Padding = new Thickness(18, 10, 18, 10), Content = body });
        }
        Tab("课程信息", 2, feedbackIndex); Tab("课程反馈", feedbackIndex, referenceIndex); Tab("参考历史", referenceIndex, originalFields.Length);
        tabs.SelectedIndex = complete || existing?.Status == LessonStatus.Taught ? 1 : 0; Fields.Children.Add(tabs);
        Buttons(() =>
        {
            if (!_images.PendingImport.IsCompleted) throw new InvalidOperationException("图片正在处理，请稍后保存。");
            lesson.StudentId = student.SelectedValue as string ?? throw new ArgumentException("请选择学生；可以先新增在教学生。"); lesson.Subject = subject.Text.Trim();
            lesson.Date = DateOnly.FromDateTime(day.SelectedDate ?? throw new ArgumentException("请选择授课日期。"));
            if (!TimeOnly.TryParseExact(start.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) throw new ArgumentException("开始时间请按 HH:mm 填写，例如 18:30。"); lesson.Start = time;
            var minutes = Number(hours.Text, "时长") * 60;
            var roundedMinutes = decimal.Round(minutes, 0, MidpointRounding.AwayFromZero);
            if (Math.Abs(minutes - roundedMinutes) > 0.000001m || roundedMinutes is < 1 or > 1440) throw new ArgumentException("时长须为 1–1440 分钟，可输入 1.5 小时等数值。");
            minutes = roundedMinutes;
            lesson.Minutes = (int)minutes; lesson.HourlyRate = Number(rate.Text, "时薪"); lesson.Status = (LessonStatus)status.SelectedIndex;
            lesson.Content = content.Text; lesson.Progress = progress.Text; lesson.Problems = problems.Text; lesson.Methods = methods.Text; lesson.NextPlan = next.Text; lesson.PrivateNotes = privateNotes.Text; lesson.ImageIds = _images.Images.Select(i => i.Id).ToList();
            if (!TutorRules.ValidDate(lesson.Date) || lesson.HourlyRate is < 0 or > 1000000 || string.IsNullOrWhiteSpace(lesson.Subject)) throw new ArgumentException("请检查日期、科目和时薪。");
            if (weekly.IsChecked == true)
            {
                if (_images.Images.Count > 0) throw new ArgumentException("重复排课请先保存安排，再单独编辑本次课程添加图片。"); service.AddWeekly(lesson);
            }
            else if (scope.Visibility == Visibility.Visible && scope.SelectedIndex == 1)
            {
                if (lesson.Status == LessonStatus.Taught) throw new ArgumentException("批量修改只用于排课。确认授课请使用“仅修改这一次”。");
                if (lesson.Status == LessonStatus.Cancelled) service.ChangeFuture(existing!, null);
                else
                {
                    if (lesson.HasFeedback || !string.IsNullOrWhiteSpace(lesson.PrivateNotes) || lesson.ImageIds.Count > 0) throw new ArgumentException("后续安排仅修改排课信息；反馈和图片请使用“仅修改这一次”保存。");
                    service.ChangeFuture(existing!, lesson);
                }
            }
            else service.SaveLesson(lesson, _images.Images);
        });
    }
    private static string Short(string value) => value.Length > 24 ? value[..24] + "…" : value;
    private sealed record Reference(string Label, TutorLesson Lesson);
}

