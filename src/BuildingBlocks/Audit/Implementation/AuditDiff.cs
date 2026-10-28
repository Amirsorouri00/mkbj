using System.Text.Json;
using System.Text.Json.Nodes;

namespace EwaysVNext.Audit.Implementation;

/// <summary>
/// Reduces a before/after pair to the top-level properties that actually differ.
/// <para>
/// Recording whole objects would bury the field that moved and would copy personal data into the
/// audit table on edits that never touched it — a retention problem as much as a readability one.
/// </para>
/// </summary>
public static class AuditDiff
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static (JsonObject? Before, JsonObject? After) Changed(object? before, object? after)
    {
        var beforeJson = ToObject(before);
        var afterJson = ToObject(after);

        // A creation has nothing to compare against, so every field of the new state is the change.
        if (beforeJson is null) return (null, afterJson);
        if (afterJson is null) return (beforeJson, null);

        var changedBefore = new JsonObject();
        var changedAfter = new JsonObject();

        foreach (var name in beforeJson.Select(p => p.Key).Union(afterJson.Select(p => p.Key), StringComparer.Ordinal))
        {
            var hadBefore = beforeJson.TryGetPropertyValue(name, out var beforeValue);
            var hasAfter = afterJson.TryGetPropertyValue(name, out var afterValue);

            // A property present on only one side is a change: it was added or removed.
            var differs = hadBefore != hasAfter
                || !JsonNode.DeepEquals(beforeValue, afterValue);

            if (!differs) continue;

            if (hadBefore) changedBefore[name] = beforeValue?.DeepClone();
            if (hasAfter) changedAfter[name] = afterValue?.DeepClone();
        }

        return (changedBefore, changedAfter);
    }

    private static JsonObject? ToObject(object? value) =>
        value is null ? null : JsonSerializer.SerializeToNode(value, value.GetType(), Options) as JsonObject;
}
