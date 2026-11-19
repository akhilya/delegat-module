using System.Text.Json;

namespace Delegat.Services.Ai.Agent;

/// <summary>
/// Reads propose_plan arguments. A stringified steps array must not discard the summary.
/// </summary>
internal static class ProposePlanReader
{
    internal sealed record Plan(string Summary, IReadOnlyList<JsonElement> Steps, int? AffectedCount, bool IsDestructive)
    {
        public string AffectedCountAccuracy => AffectedCount.HasValue ? "estimated" : "unknown";

        public bool HasContent =>
            !string.IsNullOrWhiteSpace(Summary)
            && Steps.Count > 0
            && Steps.All(IsWellFormedStep)
            && Steps.All(step => HasConsistentNamedExport(step, Steps.Count == 1 ? AffectedCount : null));
    }

    private static bool HasConsistentNamedExport(JsonElement step, int? affectedCount)
    {
        if (!string.Equals(step.GetProperty("tool").GetString(), "platform_request", StringComparison.OrdinalIgnoreCase))
            return true;
        var args = step.GetProperty("args");
        if (!HasText(args, "method") || !string.Equals(args.GetProperty("method").GetString(), "POST", StringComparison.OrdinalIgnoreCase)
            || !HasText(args, "path")
            || !AssistantWriteContract.PathHas(args.GetProperty("path").GetString()!, "products/export-batch")
            || !args.TryGetProperty("body", out var body) || body.ValueKind != JsonValueKind.Object
            || !body.TryGetProperty("productIds", out var ids))
            return true;

        if (ids.ValueKind != JsonValueKind.Array
            || (body.TryGetProperty("allMatching", out var allMatching) && allMatching.ValueKind == JsonValueKind.True)
            || (body.TryGetProperty("where", out var where) && where.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(where.GetString())))
            return false;

        var values = ids.EnumerateArray().ToArray();
        return values.Length > 0
            && values.All(id => id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out var n) && n > 0)
            && values.Select(id => id.GetInt32()).Distinct().Count() == values.Length
            && (!affectedCount.HasValue || affectedCount == values.Length);
    }

    private static bool IsWellFormedStep(JsonElement step) =>
        step.ValueKind == JsonValueKind.Object
        && step.EnumerateObject().All(property => property.Name is "tool" or "description" or "args")
        && HasText(step, "tool")
        && !string.Equals(step.GetProperty("tool").GetString(), "propose_plan", StringComparison.OrdinalIgnoreCase)
        && HasText(step, "description")
        && step.TryGetProperty("args", out var args)
        && args.ValueKind == JsonValueKind.Object;

    private static bool HasText(JsonElement step, string name) =>
        step.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString());

    public static Plan Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Empty;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Empty;

            var summary = FirstString(root, "summary", "title");
            int? affected = root.TryGetProperty("affectedCount", out var count)
                && count.ValueKind == JsonValueKind.Number
                && count.TryGetInt32(out var n) && n >= 0 ? n : null;
            var destructive = root.TryGetProperty("isDestructive", out var flag) && flag.ValueKind == JsonValueKind.True;
            var steps = root.TryGetProperty("steps", out var raw) ? CoerceSteps(raw) : [];
            return new Plan(summary, steps, affected, destructive);
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    private static readonly Plan Empty = new("", [], null, false);

    private static string FirstString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString()?.Trim() ?? "";
                if (text.Length > 0)
                    return text;
            }
        }
        return "";
    }

    private static List<JsonElement> CoerceSteps(JsonElement steps)
    {
        switch (steps.ValueKind)
        {
            case JsonValueKind.Array:
                var list = new List<JsonElement>();
                foreach (var item in steps.EnumerateArray())
                {
                    var step = NormalizeStep(item);
                    if (step.HasValue)
                        list.Add(step.Value);
                }
                return list;
            case JsonValueKind.Object:
                var one = NormalizeStep(steps);
                return one.HasValue ? [one.Value] : [];
            case JsonValueKind.String:
                var text = steps.GetString()?.Trim();
                if (string.IsNullOrEmpty(text))
                    return [];
                try
                {
                    using var inner = JsonDocument.Parse(text);
                    return CoerceSteps(inner.RootElement);
                }
                catch (JsonException)
                {
                    return [DescriptionStep(text)];
                }
            default:
                return [];
        }
    }

    private static JsonElement? NormalizeStep(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.String)
        {
            var text = item.GetString()?.Trim();
            return string.IsNullOrEmpty(text) ? null : DescriptionStep(text);
        }
        if (item.ValueKind != JsonValueKind.Object)
            return null;
        return item.Clone();
    }

    private static JsonElement DescriptionStep(string text)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new { description = text }));
        return doc.RootElement.Clone();
    }
}
