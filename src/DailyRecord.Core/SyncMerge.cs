using System.Text.Json;
using System.Text.Json.Nodes;

namespace DailyRecord.Core;

public sealed record SyncConflict(string Collection, string Id, string Field, JsonNode? Base, JsonNode? Computer, JsonNode? Phone);
public sealed record MergeResult(AppState State, List<SyncConflict> Conflicts);

public static class SyncMerge
{
    public static AppState Clone(AppState state) => StateJson.Deserialize(StateJson.Serialize(state));
    public static IEnumerable<string> ImageIds(AppState state) => state.Tasks.SelectMany(t => t.ImageIds)
        .Concat(state.Events.SelectMany(e => e.ImageIds)).Concat(state.Activities.SelectMany(a => a.ImageIds)).Distinct();
    public static MergeResult Merge(AppState baseline, AppState computer, AppState phone, SyncMetadata? metadata = null, long baseVersion = 0)
    {
        // Canonicalise independently materialised recurring occurrences before comparing records.
        baseline = Clone(baseline); computer = Clone(computer); phone = Clone(phone);
        var canonical = computer.Tasks.Where(t => t.IsEvent).ToDictionary(t => (t.EventId!, t.OccurrenceDate!.Value), t => t.Id);
        foreach (var state in new[] { baseline, phone })
            foreach (var task in state.Tasks.Where(t => t.IsEvent))
                if (canonical.TryGetValue((task.EventId!, task.OccurrenceDate!.Value), out var id) && id != task.Id)
                {
                    foreach (var activity in state.Activities.Where(a => a.TaskId == task.Id)) activity.TaskId = id;
                    task.Id = id;
                }
        foreach (var task in phone.Tasks.Where(t => t.IsEvent && baseline.Tasks.All(b => b.Id != t.Id)))
        {
            var other = computer.Tasks.FirstOrDefault(t => t.Id == task.Id);
            if (other is null) continue;
            var pc = JsonSerializer.SerializeToNode(other, StateJson.Options)!.AsObject();
            var mobile = JsonSerializer.SerializeToNode(task, StateJson.Options)!.AsObject();
            pc.Remove("CreatedAt"); mobile.Remove("CreatedAt");
            if (JsonNode.DeepEquals(pc, mobile)) task.CreatedAt = other.CreatedAt;
        }
        var b = JsonSerializer.SerializeToNode(baseline, StateJson.Options)!.AsObject();
        var c = JsonSerializer.SerializeToNode(computer, StateJson.Options)!.AsObject();
        var p = JsonSerializer.SerializeToNode(phone, StateJson.Options)!.AsObject();
        // An offline device can materialise occurrences of a series stopped on the computer.
        // Keep both complete snapshots for an explicit choice instead of silently reviving the series.
        if (phone.Tasks.Any(t => t.IsEvent && t.State == TaskState.Open && baseline.Tasks.All(old => old.Id != t.Id)
            && computer.Events.Any(e => e.Id == t.EventId && e.EndDate is { } end && t.OccurrenceDate > end)))
            return new(computer, [new("Account", "state", "Record", b, c, p)]);
        var result = c.DeepClone().AsObject(); var conflicts = new List<SyncConflict>();
        foreach (var collection in new[] { "Events", "Tasks", "Activities" })
        {
            Dictionary<string, JsonNode> Map(JsonObject state) => state[collection]!.AsArray().ToDictionary(n => n!["Id"]!.GetValue<string>(), n => n!);
            var bm = Map(b); var cm = Map(c); var pm = Map(p); var output = new JsonArray();
            foreach (var id in cm.Keys.Concat(pm.Keys).Concat(bm.Keys).Distinct())
            {
                bm.TryGetValue(id, out var bv); cm.TryGetValue(id, out var cv); pm.TryGetValue(id, out var pv);
                JsonNode? value;
                if (Equal(pv, bv)) value = cv?.DeepClone();
                else if (Equal(cv, bv) && !(metadata?.RecordTouched(collection, id, baseVersion) ?? false) || Equal(cv, pv)) value = pv?.DeepClone();
                else if (bv is JsonObject bo && cv is JsonObject co && pv is JsonObject po)
                {
                    var merged = co.DeepClone().AsObject();
                    // These fields describe one state transition and must never be merged independently.
                    var grouped = collection == "Tasks" ? new[] { "State", "CompletedOn", "ArchiveReason" } : [];
                    foreach (var key in co.Select(k => k.Key).Concat(po.Select(k => k.Key)).Distinct().Where(k => !grouped.Contains(k)))
                        merged[key] = MergeValue(collection, id, key, bo[key], co[key], po[key], conflicts, metadata?.Touched(collection, id, key, baseVersion) ?? false);
                    if (grouped.Length > 0)
                    {
                        JsonObject Group(JsonObject o) { var x = new JsonObject(); foreach (var key in grouped) x[key] = o[key]?.DeepClone(); return x; }
                        var choice = MergeValue(collection, id, "Status", Group(bo), Group(co), Group(po), conflicts, metadata?.Touched(collection, id, "Status", baseVersion) ?? false)!.AsObject();
                        foreach (var key in grouped) merged[key] = choice[key]?.DeepClone();
                    }
                    value = merged;
                }
                else
                {
                    conflicts.Add(new(collection, id, "Record", bv?.DeepClone(), cv?.DeepClone(), pv?.DeepClone()));
                    value = (cv ?? pv)?.DeepClone();
                }
                if (value is not null) output.Add(value);
            }
            result[collection] = output;
        }
        result["Settings"] = MergeValue("Settings", "settings", "Record", b["Settings"], c["Settings"], p["Settings"], conflicts, metadata?.Touched("Settings", "settings", "Record", baseVersion) ?? false);
        try
        {
            var state = StateJson.Deserialize(result.ToJsonString(StateJson.Options));
            if (conflicts.Count == 0) ReconcileActivities(state);
            return new(state, conflicts);
        }
        catch (InvalidDataException)
        {
            // Cross-record conflicts (for example an event split vs a task edit) are reviewed as a complete graph.
            return new(computer, [new("Account", "state", "Record", b, c, p)]);
        }
    }
    private static JsonNode? MergeValue(string collection, string id, string field, JsonNode? b, JsonNode? c, JsonNode? p, List<SyncConflict> conflicts, bool computerTouched = false)
    {
        if (Equal(p, b)) return c?.DeepClone();
        if (Equal(c, b) && !computerTouched || Equal(c, p)) return p?.DeepClone();
        conflicts.Add(new(collection, id, field, b?.DeepClone(), c?.DeepClone(), p?.DeepClone()));
        return c?.DeepClone();
    }
    private static bool Equal(JsonNode? a, JsonNode? b) => JsonNode.DeepEquals(a, b);
    public static void ReconcileActivities(AppState state)
    {
        foreach (var task in state.Tasks)
        {
            foreach (var activity in state.Activities.Where(a => a.TaskId == task.Id && !a.Reverted))
                if (activity.Kind == ActivityKind.Completed && task.State != TaskState.Completed || activity.Kind == ActivityKind.Cancelled && task.State != TaskState.Cancelled) activity.Reverted = true;
            foreach (var group in state.Activities.Where(a => a.TaskId == task.Id && !a.Reverted).GroupBy(a => (a.Kind, a.Day)))
                foreach (var redundant in group.OrderBy(a => a.At).ThenBy(a => a.Id).Skip(1)) redundant.Reverted = true;
        }
    }
}
