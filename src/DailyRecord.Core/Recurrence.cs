using System.Globalization;

namespace DailyRecord.Core;

public record LunarDate(int Year, int Month, int Day, bool IsLeapMonth);

public static class LunarDates
{
    private static ChineseLunisolarCalendar Calendar => new();
    public static LunarDate? FromSolar(DateOnly date)
    {
        var cal = Calendar;
        var dt = date.ToDateTime(TimeOnly.MinValue);
        if (dt < cal.MinSupportedDateTime || dt > cal.MaxSupportedDateTime) return null;
        var year = cal.GetYear(dt);
        var ordinalMonth = cal.GetMonth(dt);
        var leap = cal.GetLeapMonth(year);
        var month = leap > 0 && ordinalMonth >= leap ? ordinalMonth - 1 : ordinalMonth;
        return new(year, month, cal.GetDayOfMonth(dt), leap == ordinalMonth);
    }

    public static DateOnly? ToSolar(int lunarYear, int month, int day)
    {
        if (lunarYear is < 1901 or > 2100 || month is < 1 or > 12 || day is < 1 or > 30) return null;
        var cal = Calendar;
        var leap = cal.GetLeapMonth(lunarYear);
        var ordinalMonth = leap > 0 && month >= leap ? month + 1 : month;
        var clampedDay = Math.Min(day, cal.GetDaysInMonth(lunarYear, ordinalMonth));
        return DateOnly.FromDateTime(cal.ToDateTime(lunarYear, ordinalMonth, clampedDay, 0, 0, 0, 0));
    }

    public static DateOnly NormalizeRegularMonth(DateOnly date)
    {
        var lunar = FromSolar(date) ?? throw new ArgumentException("农历年度事件支持 1901 至 2100 年。");
        return ToSolar(lunar.Year, lunar.Month, lunar.Day)!.Value;
    }

    public static string Display(DateOnly date)
    {
        var lunar = FromSolar(date);
        if (lunar is null) return "";
        string[] months = ["正", "二", "三", "四", "五", "六", "七", "八", "九", "十", "冬", "腊"];
        string[] digits = ["一", "二", "三", "四", "五", "六", "七", "八", "九", "十"];
        string day = lunar.Day switch
        {
            <= 10 => "初" + digits[lunar.Day - 1],
            < 20 => "十" + digits[lunar.Day - 11],
            20 => "二十",
            < 30 => "廿" + digits[lunar.Day - 21],
            _ => "三十"
        };
        return $"农历{(lunar.IsLeapMonth ? "闰" : "")}{months[lunar.Month - 1]}月{day}";
    }
}

public static class Recurrence
{
    public static IEnumerable<DateOnly> Between(EventDefinition definition, DateOnly from, DateOnly to)
    {
        if (to < from || definition.EndDate < from || definition.StartDate > to) yield break;
        from = from > definition.StartDate ? from : definition.StartDate;
        if (definition.EndDate is { } end && end < to) to = end;
        var start = definition.StartDate;
        switch (definition.Repeat)
        {
            case RepeatRule.None:
                if (start >= from && start <= to) yield return start;
                break;
            case RepeatRule.Daily:
                for (var dayNumber = from.DayNumber; dayNumber <= to.DayNumber; dayNumber++)
                    yield return DateOnly.FromDayNumber(dayNumber);
                break;
            case RepeatRule.Weekly:
                var weeks = Math.Max(0, (from.DayNumber - start.DayNumber + 6) / 7);
                for (var dayNumber = start.DayNumber + weeks * 7; dayNumber <= to.DayNumber; dayNumber += 7)
                    yield return DateOnly.FromDayNumber(dayNumber);
                break;
            case RepeatRule.Monthly:
                var firstMonth = Math.Max(0, (from.Year - start.Year) * 12 + from.Month - start.Month);
                for (var offset = firstMonth; ; offset++)
                {
                    var total = start.Year * 12 + start.Month - 1 + offset;
                    var year = total / 12;
                    var month = total % 12 + 1;
                    if (year > to.Year || year == to.Year && month > to.Month) break;
                    var occurrence = new DateOnly(year, month, Math.Min(start.Day, DateTime.DaysInMonth(year, month)));
                    if (occurrence >= from && occurrence <= to) yield return occurrence;
                }
                break;
            case RepeatRule.Yearly:
                for (var year = from.Year; year <= to.Year; year++)
                {
                    var occurrence = new DateOnly(year, start.Month, Math.Min(start.Day, DateTime.DaysInMonth(year, start.Month)));
                    if (occurrence >= from && occurrence <= to) yield return occurrence;
                }
                break;
            case RepeatRule.LunarYearly:
                for (var year = Math.Max(1901, from.Year - 1); year <= Math.Min(2100, to.Year); year++)
                {
                    var occurrence = LunarDates.ToSolar(year, definition.LunarMonth, definition.LunarDay);
                    if (occurrence >= from && occurrence <= to) yield return occurrence.Value;
                }
                break;
        }
    }
}
