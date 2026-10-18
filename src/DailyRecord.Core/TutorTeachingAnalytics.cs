namespace DailyRecord.Core;

public sealed record TutorTeachingDay(DateOnly Date, int Count, int Minutes, int Planned, int Pending, int Cancelled);
public sealed record TutorExecutionPeriod(DateOnly From, DateOnly To, int Taught, int Cancelled, int Pending, int Planned)
{
    public int Total => Taught + Cancelled + Pending + Planned;
}
public sealed record TutorStudentHours(string StudentId, string Name, bool Archived, int Count, int Minutes);
public sealed record TutorDemandCount(string Label, int Count);

public sealed class TutorTeachingReport
{
    public required DateOnly From { get; init; }
    public required DateOnly To { get; init; }
    public required IReadOnlyList<TutorTeachingDay> Days { get; init; }
    public required IReadOnlyList<TutorExecutionPeriod> Periods { get; init; }
    public required IReadOnlyList<TutorStudentHours> Students { get; init; }
    public required IReadOnlyList<TutorLesson> MissingFeedback { get; init; }
    public required IReadOnlyList<TutorLesson> Pending { get; init; }
    public required IReadOnlyList<TutorStudent> NoTeaching { get; init; }
    public required IReadOnlyList<TutorDemandCount> Levels { get; init; }
    public required IReadOnlyList<TutorDemandCount> Paces { get; init; }
    public required IReadOnlyList<TutorDemandCount> Focuses { get; init; }
    public int ActiveStudents { get; init; }
    public int Taught => Days.Sum(d => d.Count);
    public int Minutes => Days.Sum(d => d.Minutes);
    public int Planned => Days.Sum(d => d.Planned);
    public int Cancelled => Days.Sum(d => d.Cancelled);
    public int TeachingDays => Days.Count(d => d.Count > 0);
    public int PeakMinutes => Days.Select(d => d.Minutes).DefaultIfEmpty().Max();
    public int WithFeedback => Taught - MissingFeedback.Count;
    public int FutureTaught { get; init; }
}

// Read-only aggregation. Recurring projections come from the same service as the calendar.
public static class TutorTeachingAnalytics
{
    public static void ValidateRange(DateOnly from, DateOnly to)
    {
        if (!TutorRules.ValidDate(from) || !TutorRules.ValidDate(to) || to < from)
            throw new ArgumentException("请选择 1901–2100 年内的日期，结束日期不能早于开始日期。");
        if (to.DayNumber - from.DayNumber >= 366)
            throw new ArgumentException("教学概览单次最多查看 366 天，请缩短日期范围。");
    }

    public static TutorTeachingReport Build(TutorService service, DateOnly from, DateOnly to, DateOnly today, string? studentId = null)
    {
        ValidateRange(from, to);
        if (!TutorRules.ValidDate(today)) throw new ArgumentException("当前日期超出支持范围。");
        if (studentId is not null && !service.State.Students.Any(s => s.Id == studentId)) throw new ArgumentException("找不到所选学生。");
        var students = service.State.Students.ToDictionary(s => s.Id);
        var lessons = service.Lessons(from, to).Where(l => studentId is null || l.StudentId == studentId).ToList();
        var taught = lessons.Where(l => l.Status == LessonStatus.Taught && l.Date <= today).ToList();
        var pending = lessons.Where(l => l.Status == LessonStatus.Planned && l.Date < today).OrderBy(l => l.Date).ThenBy(l => l.Start).ToList();
        var byDay = lessons.GroupBy(l => l.Date).ToDictionary(g => g.Key, g => g.ToList());
        var days = Enumerable.Range(from.DayNumber, to.DayNumber - from.DayNumber + 1).Select(n =>
        {
            var day = DateOnly.FromDayNumber(n); var rows = byDay.GetValueOrDefault(day) ?? [];
            var done = rows.Where(l => l.Status == LessonStatus.Taught && l.Date <= today).ToList();
            return new TutorTeachingDay(day, done.Count, done.Sum(l => l.Minutes),
                rows.Count(l => l.Status == LessonStatus.Planned && l.Date >= today),
                rows.Count(l => l.Status == LessonStatus.Planned && l.Date < today),
                rows.Count(l => l.Status == LessonStatus.Cancelled));
        }).ToList();
        DateOnly Key(DateOnly date) => days.Count <= 14 ? date : days.Count <= 93
            ? date.AddDays(-(((int)date.DayOfWeek + 6) % 7)) : new DateOnly(date.Year, date.Month, 1);
        var periods = days.GroupBy(d => Key(d.Date)).Select(g => new TutorExecutionPeriod(g.First().Date, g.Last().Date,
            g.Sum(d => d.Count), g.Sum(d => d.Cancelled), g.Sum(d => d.Pending), g.Sum(d => d.Planned))).ToList();
        var ranking = taught.GroupBy(l => l.StudentId).Select(g => new TutorStudentHours(g.Key, students[g.Key].Name, students[g.Key].Archived, g.Count(), g.Sum(l => l.Minutes)))
            .OrderByDescending(s => s.Minutes).ThenBy(s => s.Name, StringComparer.Ordinal).ThenBy(s => s.StudentId, StringComparer.Ordinal).ToList();
        var active = students.Values.Where(s => !s.Archived && (studentId is null || s.Id == studentId)).ToList();
        List<TutorDemandCount> Choices(IReadOnlyList<string> options, Func<TutorStudent, string> select) =>
            options.Select(o => new TutorDemandCount(o, active.Count(s => select(s) == o))).Append(new("未选择", active.Count(s => select(s) == ""))).ToList();
        return new TutorTeachingReport
        {
            From = from, To = to, Days = days, Periods = periods, Students = ranking,
            MissingFeedback = taught.Where(l => !l.HasFeedback).OrderBy(l => l.Date).ThenBy(l => l.Start).ToList(), Pending = pending,
            NoTeaching = active.Where(s => !ranking.Any(r => r.StudentId == s.Id)).OrderBy(s => s.Name, StringComparer.Ordinal).Select(TutorRules.Copy).ToList(),
            ActiveStudents = active.Count, Levels = Choices(TutorStudentProfile.Levels, s => s.LearningLevel), Paces = Choices(TutorStudentProfile.Paces, s => s.LearningPace),
            Focuses = TutorStudentProfile.Focuses.Select(f => new TutorDemandCount(f, active.Count(s => s.LearningFocus.Contains(f))))
                .Append(new("未选择", active.Count(s => s.LearningFocus.Count == 0))).ToList(),
            FutureTaught = lessons.Count(l => l.Status == LessonStatus.Taught && l.Date > today)
        };
    }
}
