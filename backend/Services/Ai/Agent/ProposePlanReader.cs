using System.Text.Json;

namespace Delegat.Services.Ai.Agent;

/// <summary>
/// Reads propose_plan arguments. A stringified steps array must not discard the summary.
/// </summary>
internal static class ProposePlanReader
{
    internal sealed record Plan(string Summary, IReadOnlyList<JsonElement> Steps, int AffectedCount, bool IsDestructive)
    {
        public bool HasContent =>
            !string.IsNullOrWhiteSpace(Summary)
            && Steps.Count > 0
            && Steps.All(IsWellFormedStep);
    }

    private static bool IsWellFormedStep(JsonElement step) =>
        step.ValueKind == JsonValueKind.Object
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
            var affected = root.TryGetProperty("affectedCount", out var count) && count.TryGetInt32(out var n) ? n : 0;
            var destructive = root.TryGetProperty("isDestructive", out var flag) && flag.ValueKind == JsonValueKind.True;
            var steps = root.TryGetProperty("steps", out var raw) ? CoerceSteps(raw) : [];
            return new Plan(summary, steps, affected, destructive);
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    private static readonly Plan Empty = new("", [], 0, false);

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
