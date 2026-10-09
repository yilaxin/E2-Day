using System.Security.Cryptography;

namespace DailyRecord.Core;

public sealed record NoteImage
{
    public const int MaxPerNote = 8;
    public const int MaxBytes = 10_000_000;
    public string Id { get; init; } = "";
    public string FileName { get; init; } = "";
    public byte[] Data { get; init; } = [];

    public static NoteImage Create(string name, byte[] png)
    {
        var image = new NoteImage { Id = Convert.ToHexString(SHA256.HashData(png)), FileName = Path.GetFileName(name), Data = png.ToArray() };
        image.Validate();
        return image;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(FileName) || FileName.Length > 240 || Data is null || Data.Length is < 8 or > MaxBytes ||
            !Data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            !string.Equals(Id, Convert.ToHexString(SHA256.HashData(Data)), StringComparison.Ordinal))
            throw new InvalidDataException("备注图片无效或过大，请重新添加图片。");
    }

    internal static void ValidateIds(List<string>? ids)
    {
        if (ids is null || ids.Count > MaxPerNote || ids.Distinct().Count() != ids.Count ||
            ids.Any(id => id is null || id.Length != 64 || id.Any(c => !char.IsAsciiHexDigit(c))))
            throw new InvalidDataException("备注图片列表无效，每则备注最多 8 张图片。");
    }
}

public sealed record BackupData(AppState State, IReadOnlyList<NoteImage> Images);
