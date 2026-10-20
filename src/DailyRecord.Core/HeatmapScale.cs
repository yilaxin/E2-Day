namespace DailyRecord.Core;

/// <summary>Inclusive upper bounds for the first three positive completion bands; zero is always separate.</summary>
public sealed record HeatmapScale(int LightMax = 1, int MediumMax = 3, int DarkMax = 6)
{
    public const string ValidationMessage = "颜色范围请填写 1 至 9999 的整数，三个上限必须从小到大且不能相同。";
    public bool IsValid => LightMax >= 1 && LightMax < MediumMax && MediumMax < DarkMax && DarkMax <= 9999;
    public int Level(int count) => count <= 0 ? 0 : count <= LightMax ? 1 : count <= MediumMax ? 2 : count <= DarkMax ? 3 : 4;
    public IReadOnlyList<(int Minimum, string Label)> Bands()
    {
        static string Range(int min, int max) => min == max ? $"{min} 项" : $"{min}–{max} 项";
        return [(0, "0 项"), (1, Range(1, LightMax)), (LightMax + 1, Range(LightMax + 1, MediumMax)),
            (MediumMax + 1, Range(MediumMax + 1, DarkMax)), (DarkMax + 1, $"{DarkMax + 1}+ 项")];
    }
}
