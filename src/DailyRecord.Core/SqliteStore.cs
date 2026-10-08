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
}

public sealed class SqliteStore : IDisposable
{
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
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000; CREATE TABLE IF NOT EXISTS app_state (id INTEGER PRIMARY KEY CHECK(id=1), schema_version INTEGER NOT NULL, payload TEXT NOT NULL, updated_at TEXT NOT NULL);";
        command.ExecuteNonQuery();
    }

    public AppState Load()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT payload FROM app_state WHERE id=1";
        return command.ExecuteScalar() is string json ? StateJson.Deserialize(json) : new AppState();
    }

    public void Save(AppState state)
    {
        StateJson.Validate(state);
        var json = StateJson.Serialize(state);
        using var transaction = _connection.BeginTransaction();
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO app_state(id,schema_version,payload,updated_at) VALUES(1,1,$payload,$updated) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload, schema_version=excluded.schema_version, updated_at=excluded.updated_at";
        command.Parameters.AddWithValue("$payload", json);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public static string ExportText(AppState state)
    {
        StateJson.Validate(state);
        var payload = StateJson.Serialize(state);
        var envelope = new BackupEnvelope
        {
            ExportedAt = DateTimeOffset.UtcNow, Payload = payload,
            Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
        };
        return JsonSerializer.Serialize(envelope, StateJson.Options);
    }

    public static AppState ReadBackup(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > 100_000_000) throw new InvalidDataException("备份不存在或过大。");
        var envelope = JsonSerializer.Deserialize<BackupEnvelope>(File.ReadAllText(path, Encoding.UTF8), StateJson.Options)
            ?? throw new InvalidDataException("备份文件无效。");
        if (envelope.Format != "DailyRecordBackup" || envelope.Version != 1 || envelope.Payload is null ||
            !string.Equals(envelope.Sha256, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envelope.Payload))), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("备份版本或完整性校验失败，现有数据未更改。");
        return StateJson.Deserialize(envelope.Payload);
    }

    public static void WriteBackup(string path, AppState state)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, ExportText(state), new UTF8Encoding(false));
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void DailyBackup(AppState state, DateOnly day)
    {
        Directory.CreateDirectory(BackupDirectory);
        WriteBackup(Path.Combine(BackupDirectory, $"{day:yyyy-MM-dd}.json"), state);
        var automatic = Directory.GetFiles(BackupDirectory, "????-??-??.json")
            .Where(p => DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(p), "yyyy-MM-dd", out _))
            .OrderByDescending(Path.GetFileName).ToList();
        foreach (var old in automatic.Skip(30)) File.Delete(old);
    }

    public string BeforeRestoreBackup(AppState state)
    {
        var path = Path.Combine(BackupDirectory, $"恢复前-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        WriteBackup(path, state);
        return path;
    }
    public void Dispose() => _connection.Dispose();
}
