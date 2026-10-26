namespace DailyRecord.Core;

/// <summary>Inclusive teaching-hour thresholds. Zero always has its own colour.</summary>
public sealed record TutorHeatmapScale(decimal LightHours = 1, decimal MediumHours = 2, decimal DarkHours = 4)
{
    public const string ValidationMessage = "请填写 0.25–24 小时内、以 0.25 小时递增的数值，三个上限须从小到大且不能相同。";
    public bool IsValid => LightHours >= .25m && LightHours < MediumHours && MediumHours < DarkHours && DarkHours <= 24 &&
        new[] { LightHours, MediumHours, DarkHours }.All(n => n * 4 == decimal.Truncate(n * 4));
    public int Level(int minutes) => minutes <= 0 ? 0 : minutes <= LightHours * 60 ? 1 : minutes <= MediumHours * 60 ? 2 : minutes <= DarkHours * 60 ? 3 : 4;
    public IReadOnlyList<string> Labels() => ["无授课", $"≤{LightHours:0.##}小时", $"≤{MediumHours:0.##}小时", $"≤{DarkHours:0.##}小时", $">{DarkHours:0.##}小时"];
}
