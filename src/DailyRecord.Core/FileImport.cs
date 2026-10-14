using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DailyRecord.Core;

public sealed record ImportConflict(string Collection, string Id, JsonNode Current, JsonNode Incoming);

public sealed class FileImportPlan
{
    public string CurrentFingerprint { get; }
    public AppState Proposed { get; }
    public IReadOnlyList<ImportConflict> Conflicts { get; }
    public int Added { get; }
    public int Unchanged { get; }
    internal FileImportPlan(string fingerprint, AppState proposed, List<ImportConflict> conflicts, int added, int unchanged)
    { CurrentFingerprint = fingerprint; Proposed = proposed; Conflicts = conflicts; Added = added; Unchanged = unchanged; }
    public AppState Resolve(IReadOnlyDictionary<int, string> choices)
    {
        if (choices.Count != Conflicts.Count || Enumerable.Range(0, Conflicts.Count).Any(i => !choices.TryGetValue(i, out var choice) || choice is not ("current" or "incoming")))
            throw new InvalidDataException("请为每条不同的记录选择要保留的版本。");
        var result = JsonSerializer.SerializeToNode(Proposed, StateJson.Options)!.AsObject();
        for (var i = 0; i < Conflicts.Count; i++)
        {
            var conflict = Conflicts[i]; var rows = result[conflict.Collection]!.AsArray();
            var index = Enumerable.Range(0, rows.Count).Single(index => rows[index]!["Id"]!.GetValue<string>() == conflict.Id);
            rows[index] = (choices[i] == "incoming" ? conflict.Incoming : conflict.Current).DeepClone();
        }
        var state = StateJson.Deserialize(result.ToJsonString(StateJson.Options));
        SyncMerge.ReconcileActivities(state);
        foreach (var task in state.Tasks.Where(task => task.State == TaskState.Completed))
            foreach (var history in state.Activities.Where(history => history.TaskId == task.Id && history.Kind == ActivityKind.Completed && history.Day != task.CompletedOn)) history.Reverted = true;
        return state;
    }
}

public static class FileImport
{
    private static bool SameRecord(JsonNode current, JsonNode incoming, bool ignoreCreatedAt = false)
    {
        JsonObject Normalize(JsonNode value)
        {
            var normalized = value.DeepClone().AsObject();
            foreach (var key in new[] { "CreatedAt", "At" })
                if (normalized[key] is JsonValue field && field.TryGetValue<string>(out var text) && DateTimeOffset.TryParse(text, out var at))
                    normalized[key] = at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
            if (ignoreCreatedAt) normalized.Remove("CreatedAt");
            return normalized;
        }
        return JsonNode.DeepEquals(Normalize(current), Normalize(incoming));
    }
    public static string Fingerprint(AppState state)
    {
        var records = JsonSerializer.SerializeToNode(state, StateJson.Options)!.AsObject(); records.Remove("News");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(records.ToJsonString(StateJson.Options))));
    }
    public static FileImportPlan Preview(AppState current, AppState incoming)
    {
        StateJson.Validate(current); StateJson.Validate(incoming);
        var fingerprint = Fingerprint(current); current = SyncMerge.Clone(current); incoming = SyncMerge.Clone(incoming);
        var occurrences = current.Tasks.Where(task => task.IsEvent).ToDictionary(task => (task.EventId!, task.OccurrenceDate!.Value), task => task.Id);
        foreach (var task in incoming.Tasks.Where(task => task.IsEvent))
        {
            if (!occurrences.TryGetValue((task.EventId!, task.OccurrenceDate!.Value), out var canonicalId) || canonicalId == task.Id) continue;
            foreach (var history in incoming.Activities.Where(history => history.TaskId == task.Id)) history.TaskId = canonicalId;
            task.Id = canonicalId;
        }
        StateJson.Validate(incoming);
        var local = JsonSerializer.SerializeToNode(current, StateJson.Options)!.AsObject();
        var foreign = JsonSerializer.SerializeToNode(incoming, StateJson.Options)!.AsObject();
        var conflicts = new List<ImportConflict>(); var added = 0; var unchanged = 0;
        foreach (var collection in new[] { "Events", "Tasks", "Activities" })
        {
            var rows = local[collection]!.AsArray();
            var byId = rows.ToDictionary(row => row!["Id"]!.GetValue<string>(), row => row!);
            foreach (var incomingRow in foreign[collection]!.AsArray())
            {
                var row = incomingRow!; var id = row["Id"]!.GetValue<string>();
                if (!byId.TryGetValue(id, out var old)) { rows.Add(row.DeepClone()); added++; continue; }
                if (collection == "Tasks" && row["EventId"] is not null)
                {
                    if (SameRecord(old, row, ignoreCreatedAt: true)) row["CreatedAt"] = old["CreatedAt"]?.DeepClone();
                }
                if (SameRecord(old, row)) unchanged++;
                else conflicts.Add(new(collection, id, old.DeepClone(), row.DeepClone()));
            }
        }
        // Imports add records; an absent row is never interpreted as a deletion.
        // Reminder preferences and downloaded news stay local to the receiving device.
        var proposed = StateJson.Deserialize(local.ToJsonString(StateJson.Options));
        return new(fingerprint, proposed, conflicts, added, unchanged);
    }
}
