using System.Text.Json;
using System.Text.Json.Nodes;

namespace DailyRecord.Core;

public sealed class SyncMetadata
{
    public string Epoch { get; set; } = Guid.NewGuid().ToString("N");
    public long Version { get; set; }
    public Dictionary<string, long> Fields { get; set; } = [];
    public bool Touched(string collection, string id, string field, long baseline) =>
        Fields.GetValueOrDefault(Key(collection, id, field)) > baseline || Fields.GetValueOrDefault(Key(collection, id, "Record")) > baseline;
    public bool RecordTouched(string collection, string id, long baseline) => Fields.Any(pair => pair.Key.StartsWith(collection + "/" + id + "/", StringComparison.Ordinal) && pair.Value > baseline);
    private static string Key(string collection, string id, string field) => collection + "/" + id + "/" + field;
    public void Observe(AppState before, AppState after)
    {
        var changes = new HashSet<string>();
        var b = JsonSerializer.SerializeToNode(before, StateJson.Options)!.AsObject(); var a = JsonSerializer.SerializeToNode(after, StateJson.Options)!.AsObject();
        foreach (var collection in new[] { "Events", "Tasks", "Activities" })
        {
            Dictionary<string, JsonObject> Map(JsonObject state) => state[collection]!.AsArray().ToDictionary(n => n!["Id"]!.GetValue<string>(), n => n!.AsObject());
            var bm = Map(b); var am = Map(a);
            foreach (var id in bm.Keys.Concat(am.Keys).Distinct())
            {
                bm.TryGetValue(id, out var old); am.TryGetValue(id, out var current);
                if (old is null || current is null) { changes.Add(Key(collection, id, "Record")); continue; }
                foreach (var field in old.Select(p => p.Key).Concat(current.Select(p => p.Key)).Distinct())
                    if (!JsonNode.DeepEquals(old[field], current[field]))
                        changes.Add(Key(collection, id, collection == "Tasks" && field is "State" or "CompletedOn" or "ArchiveReason" ? "Status" : field));
            }
        }
        if (!JsonNode.DeepEquals(b["Settings"], a["Settings"])) changes.Add(Key("Settings", "settings", "Record"));
        if (changes.Count == 0) return;
        Version++; foreach (var key in changes) Fields[key] = Version;
    }
}
public sealed class SyncCommit
{
    public string DeviceHash { get; set; } = "";
    public string RequestId { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string Epoch { get; set; } = "";
    public long Version { get; set; }
}
