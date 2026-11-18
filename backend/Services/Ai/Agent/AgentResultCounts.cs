using System.Text.Json;
using System.Text.Json.Serialization;

namespace Delegat.Services.Ai.Agent;

public sealed record AgentCountEvidence(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Value,
    string Accuracy,
    string Unit)
{
    public static AgentCountEvidence Unknown(string unit = "records") => new(null, "unknown", unit);
    public static AgentCountEvidence Exact(long value, string unit) => new(value, "exact", unit);
}

/// <summary>
/// Per-invocation evidence, not a count of unique entities across a plan.
/// Read means returned entries, never physical database rows scanned.
/// </summary>
public sealed record AgentResultCounts(
    int Version, string Source,
    AgentCountEvidence Read, AgentCountEvidence Matched, AgentCountEvidence Written)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const long MaxSafeInteger = 9_007_199_254_740_991;

    public static AgentResultCounts Unknown { get; } = new(
        1, "unknown", AgentCountEvidence.Unknown(),
        AgentCountEvidence.Unknown(), AgentCountEvidence.Unknown());

    public static AgentResultCounts Observe(string tool, string input, object? result)
    {
        if (tool.StartsWith("functions.", StringComparison.OrdinalIgnoreCase))
            tool = tool["functions.".Length..];
        if (tool == GeneratedFileContract.ImportToolName)
        {
            // Only the native completed receipt type proves a write or replay.
            // JSON carrying similarly named fields is not import evidence.
            return result is GeneratedFileImportResult file
                && file.FileId > 0 && file.Length >= 0
                && file.Destination is "store" or "product"
                && file.WrittenCount is 0 or 1
                && file.Idempotent == (file.WrittenCount == 0)
                ? new(1, tool, AgentCountEvidence.Unknown(), AgentCountEvidence.Unknown(),
                    AgentCountEvidence.Exact(file.WrittenCount, "files"))
                : Unknown;
        }
        if (tool is not ("sql_readonly" or "search_catalog" or "platform_request") || result == null)
            return Unknown;

        try
        {
            var root = result is JsonElement element
                ? element : JsonSerializer.SerializeToElement(result, Json);
            if (root.ValueKind != JsonValueKind.Object || !HasSuccessfulVerdict(root))
                return Unknown;
            var noWrites = AgentCountEvidence.Exact(0, "records");
            if (tool == "sql_readonly")
            {
                return ArrayLength(root, "rows") is { } rows
                    ? new(1, tool, AgentCountEvidence.Exact(rows, "rows"),
                        AgentCountEvidence.Unknown(), noWrites)
                    : Unknown;
            }
            if (tool == "search_catalog")
            {
                if (ArrayLength(root, "products") is not { } products
                    || ArrayLength(root, "files") is not { } files
                    || !TryBoolean(root, "productsHasMore", out var productsMore)
                    || !TryBoolean(root, "filesHasMore", out var filesMore))
                    return Unknown;
                var returned = products + files;
                return new(1, tool, AgentCountEvidence.Exact(returned, "results"),
                    productsMore || filesMore ? AgentCountEvidence.Unknown("results")
                        : AgentCountEvidence.Exact(returned, "results"), noWrites);
            }

            using var arguments = JsonDocument.Parse(input);
            var args = arguments.RootElement;
            if (args.ValueKind != JsonValueKind.Object
                || !TryString(args, "method", out var method)
                || !TryString(args, "path", out var path))
                return Unknown;
            path = path.Split('?', 2)[0];
            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                path = path[4..];
            // These adapters have explicit native endpoint semantics. Request
            // IDs, generic response counts and queued jobs never prove writes.
            if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
                && path.Equals("/products", StringComparison.OrdinalIgnoreCase)
                && ArrayLength(root, "items") is { } items)
            {
                var matched = NonNegativeInteger(root, "totalCount");
                return new(1, tool, AgentCountEvidence.Exact(items, "products"),
                    matched.HasValue && matched.Value >= items ? AgentCountEvidence.Exact(matched.Value, "products")
                        : AgentCountEvidence.Unknown("products"), noWrites);
            }
            return Unknown;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
        {
            return Unknown;
        }
    }

    private static bool HasSuccessfulVerdict(JsonElement root)
    {
        foreach (var name in new[] { "error", "errorClass" })
            if (root.TryGetProperty(name, out var error)
                && error.ValueKind != JsonValueKind.Null
                && !(error.ValueKind == JsonValueKind.String && error.GetString() == ""))
                return false;
        foreach (var name in new[] { "outcomeUnknown", "isError" })
            if (root.TryGetProperty(name, out var flag)
                && flag.ValueKind != JsonValueKind.False)
                return false;
        if (root.TryGetProperty("exitCode", out var exit)
            && (exit.ValueKind != JsonValueKind.Number || !exit.TryGetInt32(out var code) || code != 0))
            return false;
        if (root.TryGetProperty("operationId", out var operation)
            && operation.ValueKind != JsonValueKind.Null)
            return false;
        if (!root.TryGetProperty("status", out var status))
            return true;
        if (status.ValueKind == JsonValueKind.Number)
            return status.TryGetInt32(out var http) && http is >= 200 and < 300 && http != 202;
        return status.ValueKind == JsonValueKind.String
            && status.GetString()?.ToLowerInvariant() is "completed" or "success" or "succeeded" or "done" or "ok";
    }

    private static int? ArrayLength(JsonElement root, string key) =>
        root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.GetArrayLength() : null;

    private static long? NonNegativeInteger(JsonElement root, string key) =>
        root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var number) && number is >= 0 and <= MaxSafeInteger ? number : null;

    private static bool TryBoolean(JsonElement root, string key, out bool value)
    {
        value = false;
        if (!root.TryGetProperty(key, out var node)
            || node.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return false;
        value = node.GetBoolean();
        return true;
    }

    private static bool TryString(JsonElement root, string key, out string value)
    {
        value = "";
        if (!root.TryGetProperty(key, out var node) || node.ValueKind != JsonValueKind.String)
            return false;
        value = node.GetString() ?? "";
        return true;
    }
}
