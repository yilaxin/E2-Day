using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace DailyRecord.Core;

public sealed class BackupEnvelope
{
    public string Format { get; set; } = "DailyRecordBackup";
    public int Version { get; set; } = 1;
    public DateTimeOffset ExportedAt { get; set; }
    public string Sha256 { get; set; } = "";
    public string Payload { get; set; } = "";
    public List<NoteImage> Images { get; set; } = [];
}

public sealed class SqliteStore : IDisposable
{
    private const long MaxBackupBytes = 512_000_000;
    private readonly SqliteConnection _connection;
    public string DataDirectory { get; }
    public string BackupDirectory => Path.Combine(DataDirectory, "Backups");
    public SqliteStore(string dataDirectory)
    {
        DataDirectory = Path.GetFullPath(dataDirectory);
        Directory.CreateDirectory(DataDirectory);
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(DataDirectory, "daily-record.db"), Pooling = false
        }.ToString());
        _connection.Open();
        using var command = _connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000; CREATE TABLE IF NOT EXISTS app_state (id INTEGER PRIMARY KEY CHECK(id=1), schema_version INTEGER NOT NULL, payload TEXT NOT NULL, updated_at TEXT NOT NULL); CREATE TABLE IF NOT EXISTS note_images (id TEXT PRIMARY KEY, file_name TEXT NOT NULL, data BLOB NOT NULL);";
        command.ExecuteNonQuery();
        using var sync = _connection.CreateCommand();
        sync.CommandText = "CREATE TABLE IF NOT EXISTS sync_clock(id INTEGER PRIMARY KEY CHECK(id=1),payload TEXT NOT NULL); CREATE TABLE IF NOT EXISTS sync_receipts(device TEXT NOT NULL,request TEXT NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(device,request)); INSERT OR IGNORE INTO sync_clock(id,payload) VALUES(1,$clock)";
        sync.Parameters.AddWithValue("$clock", JsonSerializer.Serialize(new SyncMetadata(), StateJson.Options)); sync.ExecuteNonQuery();
    }

    public AppState Load()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT payload FROM app_state WHERE id=1";
        return command.ExecuteScalar() is string json ? StateJson.Deserialize(json) : new AppState();
    }

    public void Save(AppState state, IEnumerable<NoteImage>? images = null, SyncCommit? syncCommit = null)
    {
        StateJson.Validate(state);
        var added = (images ?? []).DistinctBy(i => i.Id).ToList();
        foreach (var image in added) image.Validate();
        var available = new HashSet<string>(added.Select(i => i.Id));
        using (var list = _connection.CreateCommand())
        {
            list.CommandText = "SELECT id FROM note_images";
            using var reader = list.ExecuteReader();
            while (reader.Read()) available.Add(reader.GetString(0));
        }
        if (ImageIds(state).Any(id => !available.Contains(id))) throw new InvalidDataException("备注图片缺失，记录未保存。");
        var json = StateJson.Serialize(state);
        var clock = GetSyncMetadata(); clock.Observe(Load(), state);
        using var transaction = _connection.BeginTransaction();
        foreach (var image in added)
        {
            using var insert = _connection.CreateCommand(); insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO note_images(id,file_name,data) VALUES($id,$name,$data) ON CONFLICT(id) DO NOTHING";
            insert.Parameters.AddWithValue("$id", image.Id); insert.Parameters.AddWithValue("$name", image.FileName); insert.Parameters.AddWithValue("$data", image.Data);
            insert.ExecuteNonQuery();
        }
        var referenced = ImageIds(state).ToHashSet();
        foreach (var unused in available.Except(referenced))
        {
            using var delete = _connection.CreateCommand(); delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM note_images WHERE id=$id"; delete.Parameters.AddWithValue("$id", unused); delete.ExecuteNonQuery();
        }
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO app_state(id,schema_version,payload,updated_at) VALUES(1,$schema,$payload,$updated) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload, schema_version=excluded.schema_version, updated_at=excluded.updated_at";
        command.Parameters.AddWithValue("$schema", state.SchemaVersion);
        command.Parameters.AddWithValue("$payload", json);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
        using var sync = _connection.CreateCommand(); sync.Transaction = transaction;
        sync.CommandText = "UPDATE sync_clock SET payload=$clock WHERE id=1";
        sync.Parameters.AddWithValue("$clock", JsonSerializer.Serialize(clock, StateJson.Options)); sync.ExecuteNonQuery();
        if (syncCommit is not null)
        {
            syncCommit.Epoch = clock.Epoch; syncCommit.Version = clock.Version;
            using var receipt = _connection.CreateCommand(); receipt.Transaction = transaction;
            receipt.CommandText = "INSERT INTO sync_receipts(device,request,payload) VALUES($device,$request,$payload)";
            receipt.Parameters.AddWithValue("$device", syncCommit.DeviceHash); receipt.Parameters.AddWithValue("$request", syncCommit.RequestId);
            receipt.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(syncCommit, StateJson.Options)); receipt.ExecuteNonQuery();
        }
        transaction.Commit();
    }
    public SyncMetadata GetSyncMetadata()
    {
        using var command = _connection.CreateCommand(); command.CommandText = "SELECT payload FROM sync_clock WHERE id=1";
        return JsonSerializer.Deserialize<SyncMetadata>((string)command.ExecuteScalar()!, StateJson.Options)!;
    }
    public SyncCommit? GetSyncCommit(string device, string request)
    {
        using var command = _connection.CreateCommand(); command.CommandText = "SELECT payload FROM sync_receipts WHERE device=$device AND request=$request";
        command.Parameters.AddWithValue("$device", device); command.Parameters.AddWithValue("$request", request);
        return command.ExecuteScalar() is string json ? JsonSerializer.Deserialize<SyncCommit>(json, StateJson.Options) : null;
    }

    public IReadOnlyList<NoteImage> GetNoteImages(IEnumerable<string> ids)
    {
        var images = new List<NoteImage>();
        foreach (var id in ids.Distinct())
        {
            using var command = _connection.CreateCommand(); command.CommandText = "SELECT file_name,data FROM note_images WHERE id=$id";
            command.Parameters.AddWithValue("$id", id); using var reader = command.ExecuteReader();
            if (!reader.Read()) throw new InvalidDataException("备注图片缺失。");
            images.Add(new NoteImage { Id = id, FileName = reader.GetString(0), Data = (byte[])reader.GetValue(1) });
        }
        return images;
    }

    private static IEnumerable<string> ImageIds(AppState state) => state.Tasks.SelectMany(t => t.ImageIds)
        .Concat(state.Events.SelectMany(e => e.ImageIds)).Concat(state.Activities.SelectMany(a => a.ImageIds)).Distinct();

    public void ExportBackup(string path, AppState state) => WriteBackup(path, state, GetNoteImages(ImageIds(state)));
    public void ExportPortableBackup(string path, AppState state)
    {
        var images = GetNoteImages(ImageIds(state)).ToList(); ValidateImages(state, images);
        var envelope = new BackupEnvelope { Format = "E2DayExchange", Version = 1, ExportedAt = DateTimeOffset.UtcNow, Payload = StateJson.Serialize(state), Images = images };
        envelope.Sha256 = PortableHash(envelope);
        var text = JsonSerializer.Serialize(envelope, StateJson.Options);
        if (Encoding.UTF8.GetByteCount(text) > MaxBackupBytes) throw new InvalidDataException("备份超过 512 MB。");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = Path.GetFullPath(path) + ".tmp";
        try { File.WriteAllText(temporary, text, new UTF8Encoding(false)); File.Move(temporary, Path.GetFullPath(path), true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static string PortableHash(BackupEnvelope envelope) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envelope.Payload + "\n" + string.Join("\n", envelope.Images.Select(i => i.Id).Order(StringComparer.Ordinal)))));

    private static string BackupHash(BackupEnvelope envelope) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        envelope.Version == 1 ? envelope.Payload : envelope.Payload + "\n" + JsonSerializer.Serialize(envelope.Images, StateJson.Options))));

    public static string ExportText(AppState state, IEnumerable<NoteImage>? images = null)
    {
        StateJson.Validate(state);
        var payload = StateJson.Serialize(state);
        var envelope = new BackupEnvelope
        {
            Version = 2, ExportedAt = DateTimeOffset.UtcNow, Payload = payload, Images = (images ?? []).DistinctBy(i => i.Id).ToList()
        };
        ValidateImages(state, envelope.Images);
        envelope.Sha256 = BackupHash(envelope);
        return JsonSerializer.Serialize(envelope, StateJson.Options);
    }

    public static AppState ReadBackup(string path) => ReadBackupData(path).State;

    public static BackupData ReadBackupData(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > MaxBackupBytes) throw new InvalidDataException("备份不存在或超过 512 MB。");
        var envelope = JsonSerializer.Deserialize<BackupEnvelope>(File.ReadAllText(path, Encoding.UTF8), StateJson.Options)
            ?? throw new InvalidDataException("备份文件无效。");
        var portable = (envelope.Format is "E2DayMobileBackup" or "E2DayExchange") && envelope.Version == 1;
        if ((!portable && (envelope.Format != "DailyRecordBackup" || envelope.Version is not (1 or 2))) || envelope.Payload is null || envelope.Images is null || envelope.Images.Any(i => i is null) ||
            !string.Equals(envelope.Sha256, portable ? PortableHash(envelope) : BackupHash(envelope), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("备份版本或完整性校验失败，现有数据未更改。");
        var state = StateJson.Deserialize(envelope.Payload);
        if (!portable && envelope.Version == 1 && envelope.Images.Count != 0) throw new InvalidDataException("旧版备份不能包含图片。");
        ValidateImages(state, envelope.Images);
        return new(state, envelope.Images);
    }

    private static void ValidateImages(AppState state, List<NoteImage> images)
    {
        if (images.Any(i => i is null) || images.Select(i => i.Id).Distinct().Count() != images.Count)
            throw new InvalidDataException("备份图片数据无效。");
        foreach (var image in images) image.Validate();
        var available = images.Select(i => i.Id).ToHashSet();
        if (ImageIds(state).Any(id => !available.Contains(id))) throw new InvalidDataException("备份缺少备注图片，现有数据未更改。");
    }

    public static void WriteBackup(string path, AppState state, IEnumerable<NoteImage>? images = null)
    {
        var text = ExportText(state, images);
        if (Encoding.UTF8.GetByteCount(text) > MaxBackupBytes) throw new InvalidDataException("备份超过 512 MB，请减少备注图片或分开保留旧备份。");
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, text, new UTF8Encoding(false));
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void DailyBackup(AppState state, DateOnly day)
    {
        Directory.CreateDirectory(BackupDirectory);
        ExportBackup(Path.Combine(BackupDirectory, $"{day:yyyy-MM-dd}.json"), state);
        var automatic = Directory.GetFiles(BackupDirectory, "????-??-??.json")
            .Where(p => DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(p), "yyyy-MM-dd", out _))
            .OrderByDescending(Path.GetFileName).ToList();
        foreach (var old in automatic.Skip(30)) File.Delete(old);
    }

    public string BeforeRestoreBackup(AppState state)
    {
        var path = Path.Combine(BackupDirectory, $"恢复前-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        ExportBackup(path, state);
        return path;
    }
    public void Dispose() => _connection.Dispose();
}
