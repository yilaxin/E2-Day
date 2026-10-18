namespace DailyRecord.Core;

/// <summary>Fictional records anchored to the day of the demonstration, never merged into a real account.</summary>
public static class TutorExamples
{
    public static TutorState Create(DateOnly today)
    {
        if (!TutorRules.ValidDate(today)) throw new ArgumentException("请选择 1901–2100 年内的示例日期。");
        var state = new TutorState();
        void Student(string name, string grade, string subject, decimal rate, string school, string location,
            string level, string pace, string[] focus, string notes, bool archived = false)
            => state.Students.Add(new TutorStudent
            {
                Id = "example-student-" + state.Students.Count, Name = name + "（示例）", Grade = grade,
                Subject = subject, HourlyRate = rate, School = school + "（虚构）", TeachingLocation = location + "（示例）",
                LearningLevel = level, LearningPace = pace, LearningFocus = [.. focus], Notes = notes, Archived = archived
            });
        Student("小林", "初二", "数学", 150, "向阳中学", "线上授课", "中等", TutorStudentProfile.Paces[0],
            ["基础巩固", "查漏补缺"], "虚构档案：先画图梳理数量关系，再列式；每次安排少量变式练习。");
        Student("小周", "高一", "英语", 180, "星河中学", "示例学习室 A", "良好", TutorStudentProfile.Paces[1],
            ["同步提高", "习惯培养"], "虚构档案：词汇基础较好，阅读时需要练习定位证据与控制时间。");
        Student("小陈", "五年级", "数学", 120, "晨光小学", "线上授课", "需巩固", TutorStudentProfile.Paces[0],
            ["基础巩固", "习惯培养"], "虚构档案：通过实物图理解分数，鼓励口头讲解每一步。");
        Student("小许", "初三", "物理", 200, "远帆中学", "示例学习室 B", "优秀", TutorStudentProfile.Paces[2],
            ["拔高拓展", "考试冲刺"], "虚构档案：理解较快，适合用开放题讨论模型适用条件。");
        Student("小唐", "初一", "英语", 140, "青禾中学", "待商定", "", "", [],
            "虚构档案：新加入的学生，尚未授课及选择学习特点，可观察未设置项的统计。");
        Student("小陆", "初三", "数学", 160, "向阳中学", "线上授课", "良好", TutorStudentProfile.Paces[1],
            ["考试冲刺"], "虚构档案：已结课，保留历史课程与反馈，便于回顾。", true);

        var topics = new[] { "一元一次方程应用题：寻找等量关系", "阅读理解：主旨与证据定位", "分数应用题：画图理解单位量", "力与运动：建立受力模型", "词汇与短文阅读", "二次函数：图像与最值" };
        var methods = new[] { "用线段图标记已知量和未知量，再用变式题检验理解。", "先读问题再定位原文证据，让学生说明排除其他选项的原因。", "先画实物分割图，再口述单位量和数量关系。", "先画受力图，讨论边界条件，再对比两种解法。", "用短文语境复习词汇。", "对比图像与解析式，通过例题总结步骤。" };
        void Lesson(int studentIndex, int offset, LessonStatus status, bool feedback, int minutes = 90)
        {
            var number = today.DayNumber + offset;
            if (number < new DateOnly(1901, 1, 1).DayNumber || number > new DateOnly(2100, 12, 31).DayNumber) return;
            var student = state.Students[studentIndex];
            state.Lessons.Add(new TutorLesson
            {
                Id = $"example-lesson-{studentIndex}-{offset}", StudentId = student.Id, Subject = student.Subject,
                Date = DateOnly.FromDayNumber(number), Start = new TimeOnly(14 + studentIndex, 0), Minutes = minutes,
                HourlyRate = student.HourlyRate, Status = status,
                Content = feedback ? topics[studentIndex] : "",
                Progress = feedback ? "能够独立完成基础题，综合题需要少量提示；已在课堂中完成一次口头复盘。" : "",
                Problems = feedback ? "审题时偶尔遗漏限制条件，需要继续练习检查步骤。" : "",
                Methods = feedback ? methods[studentIndex] : "",
                NextPlan = feedback ? "完成 4 道变式题，下次先复盘错题，再增加一道拓展题。" : "",
                PrivateNotes = feedback ? "虚构内部教学笔记：下节课观察能否自主检查；此项不会出现在复制给家长的文字中。" : ""
            });
        }
        for (var week = 0; week < 12; week++)
            for (var student = 0; student < 4; student++)
            {
                var offset = -(week * 7 + new[] { 1, 3, 5, 6 }[student]);
                var status = week == 0 && student == 0 ? LessonStatus.Planned :
                    week % 4 == 1 && student == 2 ? LessonStatus.Cancelled : LessonStatus.Taught;
                Lesson(student, offset, status, status == LessonStatus.Taught && !(week % 3 == 0 && student == 1), student == 2 ? 60 : student == 3 ? 120 : 90);
            }
        for (var week = 5; week < 10; week++) Lesson(5, -week * 7, LessonStatus.Taught, true);
        Lesson(0, 0, LessonStatus.Taught, true);
        Lesson(1, 0, LessonStatus.Planned, false);
        for (var i = 0; i < 4; i++)
        {
            var start = today.DayNumber + i + 1;
            var maximum = new DateOnly(2100, 12, 31).DayNumber;
            if (start > maximum) continue;
            var student = state.Students[i];
            state.Series.Add(new TutorSeries
            {
                Id = "example-series-" + i, GroupId = "example-group-" + i, StudentId = student.Id,
                Subject = student.Subject, StartDate = DateOnly.FromDayNumber(start),
                EndDate = DateOnly.FromDayNumber(Math.Min(maximum, start + 28)), Start = new TimeOnly(14 + i, 0),
                Minutes = i == 2 ? 60 : i == 3 ? 120 : 90, HourlyRate = student.HourlyRate
            });
        }
        state.Todos =
        [
            new() { Id = "example-todo-1", Title = "（示例）准备小林的应用题变式练习" },
            new() { Id = "example-todo-2", Title = "（示例）补写小周最近一节课的反馈" },
            new() { Id = "example-todo-3", Title = "（示例）整理本周物理拓展题", Done = true }
        ];
        TutorRules.Validate(state);
        return state;
    }
}
