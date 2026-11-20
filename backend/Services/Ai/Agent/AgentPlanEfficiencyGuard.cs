using System.Text.Json;

namespace Delegat.Services.Ai.Agent;

/// <summary>Pre-proposal advice only; never rewrites or executes approved steps.</summary>
internal static class AgentPlanEfficiencyGuard
{
    // Deliberately conservative: attribute replacement, unique values, price
    // transformations, mixed operations and per-product values are not equivalent.
    private static readonly HashSet<string> CommonFields =
        ["name", "description", "storageQuantity", "currency", "barcode"];

    internal static string? RejectionReason(IReadOnlyList<JsonElement> steps)
    {
        // Built-in read-only lookups run directly in the turn; they never need
        // confirmation. Only explicitly named reads and platform GET/HEAD count —
        // Python or connected MCP steps are never assumed read-only.
        if (steps.Count > 0 && steps.All(IsBuiltInReadStep))
            return "Plan not presented: these are built-in read-only lookups. Run the required "
                + "built-in reads directly in this turn, then answer or propose one plan for the "
                + "actual changes. Do not wrap built-in reads in Python or ask the user to "
                + "approve information gathering.";
        if (steps.Count < 2) return null;
        string? commonBody = null;
        var ids = new HashSet<int>();
        foreach (var step in steps)
        {
            var tool = step.GetProperty("tool").GetString() ?? "";
            if (tool.StartsWith("functions.", StringComparison.OrdinalIgnoreCase)) tool = tool[10..];
            if (!tool.Equals("platform_request", StringComparison.OrdinalIgnoreCase)) return null;
            var args = step.GetProperty("args");
            if (!args.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String
                || !string.Equals(method.GetString(), "PATCH", StringComparison.OrdinalIgnoreCase)
                || !args.TryGetProperty("path", out var path) || path.ValueKind != JsonValueKind.String)
                return null;
            var (apiPath, query) = PlatformRequestPolicy.SplitPathAndQuery(path.GetString()!);
            var parts = apiPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (query.Count != 0 || parts.Length != 3 || parts[0] != "api" || parts[1] != "products"
                || !int.TryParse(parts[2], out var id) || id <= 0 || !ids.Add(id)) return null;
            if (!args.TryGetProperty("body", out var body) || body.ValueKind != JsonValueKind.Object) return null;
            var fields = body.EnumerateObject().ToList();
            if (fields.Count == 0 || fields.Select(f => f.Name).Distinct().Count() != fields.Count
                || fields.Any(f => !CommonFields.Contains(f.Name)
                    || f.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Object or JsonValueKind.Array))
                return null;
            if (body.TryGetProperty("barcode", out var barcode)
                && (barcode.ValueKind != JsonValueKind.String || barcode.GetString() != "")) return null;
            var normalized = JsonSerializer.Serialize(fields.OrderBy(f => f.Name, StringComparer.Ordinal)
                .ToDictionary(f => f.Name, f => f.Value));
            commonBody ??= normalized;
            if (normalized != commonBody) return null;
        }
        return "Plan not presented: these individual product PATCH steps set the same bulk-supported fields. "
            + "Propose one PATCH /products/bulk with exactly the same productIds and updates, preserving the original task. "
            + "Do not use allMatching, broaden targets, execute anything, or alter an already approved plan. "
            + AssistantWriteContract.ProductsBulk;
    }

    private static bool IsBuiltInReadStep(JsonElement step)
    {
        if (!step.TryGetProperty("tool", out var toolEl) || toolEl.ValueKind != JsonValueKind.String)
            return false;
        var tool = toolEl.GetString() ?? "";
        if (tool.StartsWith("functions.", StringComparison.OrdinalIgnoreCase)) tool = tool[10..];
        if (tool is "search_actions" or "search_knowledge" or "get_doc"
            or "search_catalog" or "sql_readonly" or "memory_search")
            return true;
        if (!tool.Equals("platform_request", StringComparison.OrdinalIgnoreCase))
            return false;
        return step.TryGetProperty("args", out var args)
            && args.ValueKind == JsonValueKind.Object
            && args.TryGetProperty("method", out var method)
            && method.ValueKind == JsonValueKind.String
            && method.GetString()?.ToUpperInvariant() is "GET" or "HEAD";
    }
}
