using System.Text.Json;

namespace DailyRecord.Core;

public sealed class HolidayDay
{
    public string Name { get; set; } = "";
    public DateOnly Date { get; set; }
    public bool IsOffDay { get; set; }
}
public sealed class HolidayYear
{
    public int Year { get; set; }
    public List<string> Papers { get; set; } = [];
    public List<HolidayDay> Days { get; set; } = [];
}
public record DayInformation(string Lunar, string Festivals, string WorkStatus, bool HasAnnualData, bool IsOffDay);

public sealed class HolidayService
{
    private readonly Dictionary<int, HolidayYear> _years = [];
    private readonly string _cacheDirectory;
    public long Revision { get; private set; }
    public HolidayService(string bundledDirectory, string cacheDirectory)
    {
        _cacheDirectory = cacheDirectory;
        foreach (var directory in new[] { bundledDirectory, cacheDirectory })
            if (Directory.Exists(directory))
                foreach (var path in Directory.GetFiles(directory, "*.json"))
                    try
                    {
                        var data = Parse(File.ReadAllText(path));
                        _years[data.Year] = data;
                    }
                    catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException) { }
    }

    private static HolidayYear Parse(string json)
    {
        var result = JsonSerializer.Deserialize<HolidayYear>(json, StateJson.Options)
            ?? throw new InvalidDataException("节假日数据为空。");
        if (result.Year is < 1901 or > 2100 || result.Days is null || result.Days.Any(d => d.Date.Year != result.Year || string.IsNullOrWhiteSpace(d.Name)))
            throw new InvalidDataException("节假日年度数据无效。");
        return result;
    }

    public DayInformation ForDay(DateOnly day)
    {
        var festivals = new List<string>();
        var solar = (day.Month, day.Day) switch
        {
            (1, 1) => "元旦", (3, 8) => "妇女节", (3, 12) => "植树节", (5, 1) => "劳动节",
            (5, 4) => "青年节", (6, 1) => "儿童节", (7, 1) => "建党节", (8, 1) => "建军节",
            (9, 10) => "教师节", (10, 1) => "国庆节", _ => ""
        };
        if (solar.Length > 0) festivals.Add(solar);
        var lunar = LunarDates.FromSolar(day);
        if (lunar is { IsLeapMonth: false })
        {
            var festival = (lunar.Month, lunar.Day) switch
            {
                (1, 1) => "春节", (1, 15) => "元宵节", (5, 5) => "端午节", (7, 7) => "七夕",
                (8, 15) => "中秋节", (9, 9) => "重阳节", (12, 8) => "腊八节",
                (12, 23) => "小年（北方）", (12, 24) => "小年（南方）", _ => ""
            };
            if (festival.Length > 0) festivals.Add(festival);
            if (LunarDates.FromSolar(day.AddDays(1)) is { Month: 1, Day: 1, IsLeapMonth: false }) festivals.Add("除夕");
        }
        var annual = _years.GetValueOrDefault(day.Year);
        var official = annual?.Days.FirstOrDefault(d => d.Date == day);
        if (official is not null && official.IsOffDay && !festivals.Contains(official.Name)) festivals.Add(official.Name + "假期");
        var weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        var status = official is not null ? official.IsOffDay ? "法定休息日" : "调休上班日"
            : weekend ? "周末" : "工作日";
        if (annual is null) status += " · 放假调休数据未更新";
        return new(LunarDates.Display(day), string.Join(" · ", festivals.Distinct()), status, annual is not null,
            official?.IsOffDay ?? weekend);
    }

    public bool HasYear(int year) => _years.ContainsKey(year);
    public async Task UpdateYearAsync(int year, CancellationToken cancellationToken = default)
    {
        if (year is < 1901 or > 2100) throw new ArgumentException("请选择 1901 至 2100 年。");
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DailyRecord/1.0 (personal-calendar)");
        var json = await client.GetStringAsync($"https://raw.githubusercontent.com/NateScarlet/holiday-cn/master/{year}.json", cancellationToken);
        var data = Parse(json);
        if (data.Year != year) throw new InvalidDataException("收到的年度数据不匹配。");
        Directory.CreateDirectory(_cacheDirectory);
        var path = Path.Combine(_cacheDirectory, $"{year}.json");
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, json, cancellationToken);
        File.Move(temp, path, overwrite: true);
        _years[year] = data;
        Revision++;
    }
}
