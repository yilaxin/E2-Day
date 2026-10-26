using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace DailyRecord.Core;

public enum LessonStatus { Planned, Taught, Cancelled }
public sealed class TutorStudent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Grade { get; set; } = "";
    public string School { get; set; } = "";
    public string TeachingLocation { get; set; } = "";
    public string LearningLevel { get; set; } = "";
    public string LearningPace { get; set; } = "";
    public List<string> LearningFocus { get; set; } = [];
    public string Subject { get; set; } = "";
    public decimal HourlyRate { get; set; }
    public string Notes { get; set; } = "";
    public bool Archived { get; set; }
}
public static class TutorStudentProfile
{
    public static IReadOnlyList<string> Levels { get; } = Array.AsReadOnly(new[] { "需巩固", "中等", "良好", "优秀" });
    public static IReadOnlyList<string> Paces { get; } = Array.AsReadOnly(new[] { "较慢 · 需要多练", "适中 · 稳步推进", "较快 · 理解较快" });
    public static IReadOnlyList<string> Focuses { get; } = Array.AsReadOnly(new[] { "基础巩固", "查漏补缺", "同步提高", "拔高拓展", "考试冲刺", "习惯培养" });
    public static IEnumerable<string> Labels(TutorStudent student)
    {
        if (student.LearningLevel.Length > 0) yield return "基础 · " + student.LearningLevel;
        if (student.LearningPace.Length > 0) yield return "节奏 · " + student.LearningPace;
        foreach (var focus in student.LearningFocus) yield return focus;
    }
    public static bool Matches(TutorStudent student, string query) => string.Join("\n",
        new[] { student.Name, student.Grade, student.Subject, student.School, student.TeachingLocation }
            .Concat(Labels(student))).Contains(query, StringComparison.OrdinalIgnoreCase);
}
public sealed class TutorLesson
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string StudentId { get; set; } = "";
    public string Subject { get; set; } = "";
    public DateOnly Date { get; set; }
    public TimeOnly Start { get; set; } = new(18, 30);
    public int Minutes { get; set; } = 90;
    public decimal HourlyRate { get; set; }
    public LessonStatus Status { get; set; }
    public string? GroupId { get; set; }
    public DateOnly? OriginalDate { get; set; }
    public string Content { get; set; } = "";
    public string Progress { get; set; } = "";
    public string Problems { get; set; } = "";
    public string Methods { get; set; } = "";
    public string NextPlan { get; set; } = "";
    public string PrivateNotes { get; set; } = "";
    public List<string> ImageIds { get; set; } = [];
    public decimal Income => Status == LessonStatus.Taught ? decimal.Round(Minutes * HourlyRate / 60m, 2, MidpointRounding.AwayFromZero) : 0;
    public bool HasFeedback => new[] { Content, Progress, Problems, Methods, NextPlan }.Any(s => !string.IsNullOrWhiteSpace(s));
    public string ParentFeedback(string student) => $"{student} · {Subject} · {Date:yyyy-MM-dd}\n" + string.Join("\n\n",
        new[] { ("教学内容", Content), ("掌握情况", Progress), ("存在问题", Problems), ("教学方法与效果", Methods), ("作业与下次计划", NextPlan) }
            .Where(p => !string.IsNullOrWhiteSpace(p.Item2)).Select(p => p.Item1 + "：\n" + p.Item2));
}
public sealed class TutorSeries
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string GroupId { get; set; } = Guid.NewGuid().ToString("N");
    public string StudentId { get; set; } = "";
    public string Subject { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public TimeOnly Start { get; set; }
    public int Minutes { get; set; }
    public decimal HourlyRate { get; set; }
}
public sealed class TutorTodo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}
public sealed class TutorState
{
    public int Version { get; set; } = 1;
    public List<TutorStudent> Students { get; set; } = [];
    public List<TutorLesson> Lessons { get; set; } = [];
    public List<TutorSeries> Series { get; set; } = [];
    public List<TutorTodo> Todos { get; set; } = [];
    public TutorHeatmapScale HeatmapScale { get; set; } = new();
}
public sealed class TutorBackup
{
    public string Format { get; set; } = "E2DayTutorBackup";
    public int Version { get; set; } = 1;
    public string Payload { get; set; } = "";
    public List<NoteImage> Images { get; set; } = [];
    public string Sha256 { get; set; } = "";
}
public sealed record TutorStatistics(int Count, int Minutes, decimal Income);

public static class TutorRules
{
    public static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, StateJson.Options), StateJson.Options)!;
    public static IEnumerable<string> ImageIds(TutorState state) => state.Lessons.SelectMany(l => l.ImageIds).Distinct();
    public static bool ValidDate(DateOnly date) => date >= new DateOnly(1901, 1, 1) && date <= new DateOnly(2100, 12, 31);
    public static void Validate(TutorState state)
    {
        if (state.HeatmapScale is null || !state.HeatmapScale.IsValid) throw new InvalidDataException(TutorHeatmapScale.ValidationMessage);
        if (state.Version != 1 || state.Students is null || state.Lessons is null || state.Series is null || state.Todos is null)
            throw new InvalidDataException("家教版数据版本或结构无效。");
        static void Ids(IEnumerable<string?> ids)
        {
            var list = ids.ToList();
            if (list.Any(string.IsNullOrWhiteSpace) || list.Distinct().Count() != list.Count) throw new InvalidDataException("记录标识为空或重复。");
        }
        if (state.Students.Any(s => s is null) || state.Lessons.Any(l => l is null) || state.Series.Any(s => s is null) || state.Todos.Any(t => t is null))
            throw new InvalidDataException("数据包含空记录。");
        Ids(state.Students.Select(s => s.Id)); Ids(state.Lessons.Select(l => l.Id)); Ids(state.Series.Select(s => s.Id)); Ids(state.Todos.Select(t => t.Id));
        var students = state.Students.Select(s => s.Id).ToHashSet();
        static bool ValidRate(decimal rate) => rate is >= 0 and <= 1_000_000;
        foreach (var s in state.Students)
            if (string.IsNullOrWhiteSpace(s.Name) || s.Name.Length > 80 || s.Grade is null || s.Subject is null || s.Notes is null ||
                s.School is null || s.School.Length > 200 || s.TeachingLocation is null || s.TeachingLocation.Length > 200 ||
                s.LearningLevel is null || s.LearningLevel != "" && !TutorStudentProfile.Levels.Contains(s.LearningLevel) ||
                s.LearningPace is null || s.LearningPace != "" && !TutorStudentProfile.Paces.Contains(s.LearningPace) ||
                s.LearningFocus is null || s.LearningFocus.Count > TutorStudentProfile.Focuses.Count ||
                s.LearningFocus.Any(f => !TutorStudentProfile.Focuses.Contains(f)) || s.LearningFocus.Distinct().Count() != s.LearningFocus.Count || !ValidRate(s.HourlyRate))
                throw new InvalidDataException("学生资料不完整、学习选项无效，或学校／授课地点超过 200 字、时薪超出范围。");
        foreach (var l in state.Lessons)
        {
            if (!students.Contains(l.StudentId) || string.IsNullOrWhiteSpace(l.Subject) || !ValidDate(l.Date) || l.Minutes is < 1 or > 1440 || !ValidRate(l.HourlyRate) || !Enum.IsDefined(l.Status) ||
                new[] { l.Content, l.Progress, l.Problems, l.Methods, l.NextPlan, l.PrivateNotes }.Any(s => s is null) ||
                (l.GroupId is null) != (l.OriginalDate is null) || l.GroupId is not null && (string.IsNullOrWhiteSpace(l.GroupId) || !ValidDate(l.OriginalDate!.Value) || l.Id != OccurrenceId(l.GroupId, l.OriginalDate.Value)))
                throw new InvalidDataException("课程数据不完整；时长须为 1–1440 分钟，时薪须为 0–1000000 元。");
            NoteImage.ValidateIds(l.ImageIds);
        }
        foreach (var s in state.Series)
            if (!students.Contains(s.StudentId) || string.IsNullOrWhiteSpace(s.GroupId) || string.IsNullOrWhiteSpace(s.Subject) || !ValidDate(s.StartDate) || s.EndDate is { } end && (!ValidDate(end) || end < s.StartDate) ||
                s.Minutes is < 1 or > 1440 || !ValidRate(s.HourlyRate)) throw new InvalidDataException("重复课程规则无效。");
        foreach (var group in state.Series.GroupBy(s => s.GroupId))
        {
            var segments = group.OrderBy(s => s.StartDate).ToArray();
            for (var i = 1; i < segments.Length; i++)
                if (segments[i - 1].EndDate is null || segments[i - 1].EndDate >= segments[i].StartDate) throw new InvalidDataException("重复课程区间发生重叠。");
        }
        if (state.Todos.Any(t => string.IsNullOrWhiteSpace(t.Title))) throw new InvalidDataException("待办标题不能为空。");
    }
    public static string OccurrenceId(string group, DateOnly day) => group + ":" + day.ToString("yyyyMMdd");
}

// Independent database and backup format: regular E²-Day cannot silently discard tutor records.
public sealed class TutorStore : IDisposable
{
    private readonly SqliteConnection _connection;
    public string DirectoryPath { get; }
    public string BackupDirectory => Path.Combine(DirectoryPath, "Backups");
    public TutorStore(string directory)
    {
        DirectoryPath = Path.GetFullPath(directory); Directory.CreateDirectory(DirectoryPath);
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(DirectoryPath, "tutor.db"), Pooling = false }.ToString());
        _connection.Open();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000; CREATE TABLE IF NOT EXISTS tutor_state(id INTEGER PRIMARY KEY CHECK(id=1),payload TEXT NOT NULL); CREATE TABLE IF NOT EXISTS tutor_images(id TEXT PRIMARY KEY,name TEXT NOT NULL,data BLOB NOT NULL);"; cmd.ExecuteNonQuery();
    }
    public TutorState Load()
    {
        using var cmd = _connection.CreateCommand(); cmd.CommandText = "SELECT payload FROM tutor_state WHERE id=1";
        var state = cmd.ExecuteScalar() is string json ? JsonSerializer.Deserialize<TutorState>(json, StateJson.Options) ?? throw new InvalidDataException("课程数据库无效。") : new TutorState();
        TutorRules.Validate(state); return state;
    }
    public List<NoteImage> Images(IEnumerable<string> ids)
    {
        var images = new List<NoteImage>();
        foreach (var id in ids.Distinct())
        {
            using var cmd = _connection.CreateCommand(); cmd.CommandText = "SELECT name,data FROM tutor_images WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) throw new InvalidDataException("课程图片缺失。");
            images.Add(new NoteImage { Id = id, FileName = reader.GetString(0), Data = (byte[])reader.GetValue(1) });
        }
        return images;
    }
    public void Save(TutorState state, IEnumerable<NoteImage>? additions = null)
    {
        TutorRules.Validate(state);
        var added = (additions ?? []).DistinctBy(i => i.Id).ToList(); foreach (var image in added) image.Validate();
        var required = TutorRules.ImageIds(state).ToHashSet();
        Images(required.Except(added.Select(i => i.Id))); // Reject missing images before writing.
        using var tx = _connection.BeginTransaction();
        foreach (var image in added.Where(i => required.Contains(i.Id)))
        {
            using var cmd = _connection.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "INSERT OR IGNORE INTO tutor_images(id,name,data) VALUES($id,$name,$data)";
            cmd.Parameters.AddWithValue("$id", image.Id); cmd.Parameters.AddWithValue("$name", image.FileName); cmd.Parameters.AddWithValue("$data", image.Data); cmd.ExecuteNonQuery();
        }
        using (var cmd = _connection.CreateCommand())
        {
            cmd.Transaction = tx; cmd.CommandText = "INSERT INTO tutor_state(id,payload) VALUES(1,$payload) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload";
            cmd.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(state, StateJson.Options)); cmd.ExecuteNonQuery();
        }
        // Keep only images referenced by the committed state; pre-restore backups retain the old images.
        var existing = new List<string>();
        using (var cmd = _connection.CreateCommand())
        { cmd.Transaction = tx; cmd.CommandText = "SELECT id FROM tutor_images"; using var reader = cmd.ExecuteReader(); while (reader.Read()) existing.Add(reader.GetString(0)); }
        foreach (var id in existing.Except(required))
        { using var cmd = _connection.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "DELETE FROM tutor_images WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id); cmd.ExecuteNonQuery(); }
        tx.Commit();
    }
    private static string Hash(TutorBackup backup) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(backup.Payload + "\n" + JsonSerializer.Serialize(backup.Images, StateJson.Options))));
    public void Export(string path, TutorState state)
    {
        TutorRules.Validate(state);
        var backup = new TutorBackup { Payload = JsonSerializer.Serialize(state, StateJson.Options), Images = Images(TutorRules.ImageIds(state)) }; backup.Sha256 = Hash(backup);
        var text = JsonSerializer.Serialize(backup, StateJson.Options);
        if (Encoding.UTF8.GetByteCount(text) > 512_000_000) throw new InvalidDataException("备份超过 512 MB，请减少图片。");
        var full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); var temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, text, new UTF8Encoding(false)); File.Move(temp, full, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static (TutorState State, List<NoteImage> Images) ReadBackup(string path)
    {
        var file = new FileInfo(path); if (!file.Exists || file.Length > 512_000_000) throw new InvalidDataException("备份不存在或超过 512 MB。");
        var backup = JsonSerializer.Deserialize<TutorBackup>(File.ReadAllText(path), StateJson.Options) ?? throw new InvalidDataException("备份无效。");
        if (backup.Format != "E2DayTutorBackup" || backup.Version != 1 || backup.Payload is null || backup.Images is null || backup.Images.Any(i => i is null) ||
            !string.Equals(backup.Sha256, Hash(backup), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("请选择家教版备份；文件版本或完整性校验失败，现有记录未更改。");
        var state = JsonSerializer.Deserialize<TutorState>(backup.Payload, StateJson.Options) ?? throw new InvalidDataException("备份无效。"); TutorRules.Validate(state);
        foreach (var image in backup.Images) image.Validate();
        if (backup.Images.Select(i => i.Id).Distinct().Count() != backup.Images.Count || TutorRules.ImageIds(state).Except(backup.Images.Select(i => i.Id)).Any()) throw new InvalidDataException("备份图片重复或缺失。");
        return (state, backup.Images);
    }
    public void DailyBackup(TutorState state, DateOnly today)
    {
        Export(Path.Combine(BackupDirectory, $"{today:yyyy-MM-dd}.json"), state);
        foreach (var path in Directory.GetFiles(BackupDirectory, "????-??-??.json").Where(p => DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(p), "yyyy-MM-dd", out _)).OrderByDescending(Path.GetFileName).Skip(30)) File.Delete(path);
    }
    public void Dispose() => _connection.Dispose();
}

public sealed class TutorService
{
    private readonly TutorStore _store;
    public TutorState State { get; private set; }
    public string SaveStatus { get; private set; } = "已保存在本机";
    public TutorService(TutorStore store) { _store = store; State = store.Load(); }
    private void Change(Action<TutorState> change, IEnumerable<NoteImage>? images = null)
    {
        var next = TutorRules.Copy(State); change(next); _store.Save(next, images); State = next;
        SaveStatus = "已保存 · " + DateTime.Now.ToString("HH:mm");
        try { _store.DailyBackup(State, DateOnly.FromDateTime(DateTime.Now)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { SaveStatus += " · 自动备份未完成，请手动备份"; }
    }
    public void UpdateHeatmapScale(TutorHeatmapScale scale)
    {
        if (scale is null || !scale.IsValid) throw new ArgumentException(TutorHeatmapScale.ValidationMessage);
        Change(s => s.HeatmapScale = scale);
    }
    public void SaveStudent(TutorStudent student) => Change(s => { s.Students.RemoveAll(x => x.Id == student.Id); s.Students.Add(TutorRules.Copy(student)); });
    public void SaveLesson(TutorLesson lesson, IEnumerable<NoteImage>? images = null) => Change(s => { s.Lessons.RemoveAll(l => l.Id == lesson.Id); s.Lessons.Add(TutorRules.Copy(lesson)); }, images);
    public void AddWeekly(TutorLesson template)
    {
        if (template.Status != LessonStatus.Planned) throw new ArgumentException("重复排课请使用待上课状态；已上课程请逐节补录。");
        var series = new TutorSeries { StudentId = template.StudentId, Subject = template.Subject, StartDate = template.Date, Start = template.Start, Minutes = template.Minutes, HourlyRate = template.HourlyRate };
        var first = TutorRules.Copy(template); first.GroupId = series.GroupId; first.OriginalDate = template.Date; first.Id = TutorRules.OccurrenceId(series.GroupId, template.Date);
        Change(s => { s.Series.Add(series); s.Lessons.Add(first); });
    }
    public IReadOnlyList<TutorLesson> Lessons(DateOnly from, DateOnly to)
    {
        if (!TutorRules.ValidDate(from) || !TutorRules.ValidDate(to) || to < from) throw new ArgumentException("请选择 1901–2100 年内的有效日期范围。");
        var saved = State.Lessons.ToDictionary(l => l.Id); var result = State.Lessons.Where(l => l.Date >= from && l.Date <= to).Select(TutorRules.Copy).ToList();
        foreach (var series in State.Series)
        {
            var start = from > series.StartDate ? from : series.StartDate; var end = series.EndDate is { } e && e < to ? e : to;
            var offset = (7 - (start.DayNumber - series.StartDate.DayNumber) % 7) % 7;
            for (var dayNumber = start.DayNumber + offset; dayNumber <= end.DayNumber; dayNumber += 7)
            {
                var day = DateOnly.FromDayNumber(dayNumber); var id = TutorRules.OccurrenceId(series.GroupId, day); if (saved.ContainsKey(id)) continue;
                result.Add(new TutorLesson { Id = id, GroupId = series.GroupId, OriginalDate = day, StudentId = series.StudentId, Subject = series.Subject, Date = day, Start = series.Start, Minutes = series.Minutes, HourlyRate = series.HourlyRate });
            }
        }
        return result.OrderBy(l => l.Date).ThenBy(l => l.Start).ThenBy(l => l.Id).ToList();
    }
    public void ChangeFuture(TutorLesson occurrence, TutorLesson? replacement)
    {
        if (occurrence.GroupId is null || occurrence.OriginalDate is null) throw new ArgumentException("这不是重复课程。");
        var boundary = occurrence.OriginalDate.Value;
        Change(state =>
        {
            foreach (var series in state.Series.Where(s => s.GroupId == occurrence.GroupId && (s.EndDate is null || s.EndDate >= boundary)).ToList())
            {
                if (series.StartDate >= boundary) state.Series.Remove(series); else series.EndDate = boundary.AddDays(-1);
            }
            state.Lessons.RemoveAll(l => l.GroupId == occurrence.GroupId && l.OriginalDate >= boundary && l.Status == LessonStatus.Planned);
            if (replacement is not null)
            {
                if (replacement.Date < boundary) throw new ArgumentException("修改后续安排的起始日期不能早于本次原定日期。");
                state.Series.Add(new TutorSeries { GroupId = occurrence.GroupId, StudentId = replacement.StudentId, Subject = replacement.Subject, StartDate = replacement.Date, Start = replacement.Start, Minutes = replacement.Minutes, HourlyRate = replacement.HourlyRate });
            }
        });
    }
    public TutorStatistics Statistics(DateOnly from, DateOnly to, string? studentId = null)
    {
        if (!TutorRules.ValidDate(from) || !TutorRules.ValidDate(to) || to < from) throw new ArgumentException("请选择 1901–2100 年内的有效日期范围。");
        var lessons = State.Lessons.Where(l => l.Date >= from && l.Date <= to && l.Status == LessonStatus.Taught && (studentId is null || l.StudentId == studentId)).ToList();
        return new(lessons.Count, lessons.Sum(l => l.Minutes), lessons.Sum(l => l.Income));
    }
    public void AddTodo(string title) { if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("请填写待办。"); Change(s => s.Todos.Add(new TutorTodo { Title = title.Trim() })); }
    public void ToggleTodo(string id) => Change(s => { var todo = s.Todos.Single(t => t.Id == id); todo.Done = !todo.Done; });
    public void RemoveTodo(string id) => Change(s => s.Todos.RemoveAll(t => t.Id == id));
    public void Restore(string path)
    {
        var backup = TutorStore.ReadBackup(path);
        _store.Export(Path.Combine(_store.BackupDirectory, $"恢复前-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json"), State);
        _store.Save(backup.State, backup.Images); State = backup.State; SaveStatus = "已恢复家教版备份";
    }
}
