using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Delegat.Services.Ai.Agent;

/// <summary>
/// Deterministic <c>$step:N.id</c> references inside signed propose_plan args. A
/// reference is only the exact string <c>$step:N.id</c> (zero-based, earlier steps
/// only) as a complete slash-delimited path segment or as an entire body value
/// whose property name is <c>id</c> or ends in <c>Id</c>, inside a
/// <c>platform_request</c> step that points back at an earlier native POST step.
/// Every other <c>$step:</c> occurrence fails closed: the whole graph is checked
/// before any side effects, and after execution the host substitutes the referenced
/// result's positive integer <c>id</c>, then re-runs the normal dispatch guards.
/// </summary>
internal static class AgentPlanReferences
{
    private const string MarkerPrefix = "$step:";

    private static readonly Regex Marker = new(
        @"^\$step:(0|[1-9][0-9]*)\.id$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Whole-graph check, evaluated before any side effects. Returns a rejection
    /// for the first step whose args carry a marker outside the two exact forms,
    /// a malformed marker, a non-backward index, or a reference to a step that is
    /// not a native platform_request POST.
    /// </summary>
    internal static string? RejectionReason(IReadOnlyList<JsonElement> steps)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            if (!TryReadStep(steps[i], out var tool, out var args))
                continue;
            if ((tool ?? "").Contains(MarkerPrefix, StringComparison.Ordinal)
                || (steps[i].TryGetProperty("description", out var descriptionEl)
                    && descriptionEl.ValueKind == JsonValueKind.String
                    && (descriptionEl.GetString() ?? "").Contains(MarkerPrefix, StringComparison.Ordinal)))
                return Reject(i,
                    "carries a $step reference outside platform_request args. References resolve only inside native request ID positions");
            if (args.ValueKind != JsonValueKind.Object
                || !ContainsMarker(args))
                continue;
            if (!IsPlatformRequest(tool))
                return Reject(i,
                    "carries a $step reference outside platform_request args. References resolve only inside native request ID positions");
            if (args.TryGetProperty("path", out var pathEl)
                && pathEl.ValueKind == JsonValueKind.String)
            {
                var raw = pathEl.GetString() ?? "";
                var queryAt = raw.IndexOf('?');
                var apiPart = queryAt >= 0 ? raw[..queryAt] : raw;
                if (queryAt >= 0 && raw[(queryAt + 1)..].Contains(MarkerPrefix, StringComparison.Ordinal))
                    return Reject(i, "carries a $step reference inside the query string");
                foreach (var segment in apiPart.Split('/'))
                {
                    if (!segment.Contains(MarkerPrefix, StringComparison.Ordinal))
                        continue;
                    var error = ValidateMarker(steps, i, segment);
                    if (error != null)
                        return Reject(i, error);
                }
            }
            if (args.TryGetProperty("body", out var body))
            {
                var error = ValidateBody(steps, i, body);
                if (error != null)
                    return Reject(i, error);
            }
            foreach (var property in args.EnumerateObject())
            {
                if (property.NameEquals("path") || property.NameEquals("body"))
                    continue;
                if (ContainsMarker(property.Value))
                    return Reject(i,
                        $"carries a $step reference in {property.Name}; references are allowed only as a complete path segment or an id/…Id body value");
            }
        }
        return null;
    }

    /// <summary>
    /// Clones the plan for structural capability checks: valid markers become the
    /// literal path segment <c>1</c> or the JSON integer <c>1</c> so route checks
    /// (including <c>:int</c> constraints) see the same shape execution produces.
    /// Signed steps are never rewritten; this copy leaves the plan unchanged.
    /// </summary>
    internal static IReadOnlyList<JsonElement> ForValidation(IReadOnlyList<JsonElement> steps)
    {
        var result = new List<JsonElement>(steps.Count);
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (!TryReadStep(step, out var tool, out var args)
                || !IsPlatformRequest(tool)
                || args.ValueKind != JsonValueKind.Object
                || !ContainsMarker(args))
            {
                result.Add(step);
                continue;
            }
            var rewritten = Rewrite(args, marker =>
                TryMarkerIndex(steps, i, marker, out var reference)
                    ? 1
                    : (int?)null);
            var node = new JsonObject
            {
                ["tool"] = tool,
                ["args"] = rewritten,
            };
            if (step.TryGetProperty("description", out var description))
                node["description"] = JsonNode.Parse(description.GetRawText());
            result.Add(JsonSerializer.SerializeToElement(node));
        }
        return result;
    }

    /// <summary>
    /// Substitutes the referenced earlier results' positive integer ids. Throws a
    /// safe <see cref="InvalidOperationException"/> when the marker is malformed,
    /// points forward, or its result is missing or carries no positive integer
    /// root <c>id</c> (string, float, absent, queued, or error results refuse).
    /// The caller must re-run the normal dispatch validation on the returned args.
    /// </summary>
    internal static string ResolveArguments(
        string tool, string argsJson, int stepIndex, IReadOnlyList<JsonElement> successfulResults)
    {
        if (string.IsNullOrWhiteSpace(argsJson))
            return argsJson;
        using var probe = JsonDocument.Parse(argsJson);
        if (!ContainsMarker(probe.RootElement))
            return argsJson;
        if (!IsPlatformRequest(tool))
            throw new InvalidOperationException(
                "Plan step references are only valid inside platform_request args.");
        var args = JsonNode.Parse(argsJson)!.AsObject();
        Rewrite(args, marker =>
        {
            if (!Marker.IsMatch(marker)
                || !int.TryParse(marker[PrefixLength..^IdSuffixLength],
                    NumberStyles.None, CultureInfo.InvariantCulture, out var reference))
                throw new InvalidOperationException(
                    $"Plan step {stepIndex + 1} carries a malformed step reference.");
            if (reference >= stepIndex || reference >= successfulResults.Count)
                throw new InvalidOperationException(
                    $"Plan step {stepIndex + 1} references step {reference + 1}, which has no completed result.");
            var result = successfulResults[reference];
            if (result.ValueKind != JsonValueKind.Object
                || AgentToolResult.IsFailure(result)
                || AgentToolResult.IsQueuedJob(result)
                || !result.TryGetProperty("id", out var id)
                || id.ValueKind != JsonValueKind.Number
                || !id.TryGetInt32(out var value)
                || value <= 0)
                throw new InvalidOperationException(
                    $"Plan step {stepIndex + 1} references step {reference + 1}, whose result has no positive integer id.");
            return value;
        });
        var resolved = args.ToJsonString();
        if (resolved.Contains(MarkerPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Plan step {stepIndex + 1} carries a step reference outside a native request ID position.");
        return resolved;
    }

    private const int PrefixLength = 6; // "$step:"
    private const int IdSuffixLength = 3; // ".id"

    private static string Reject(int stepIndex, string reason) =>
        $"Plan not presented: step {stepIndex + 1} {reason}. "
        + "Use $step:N.id (zero-based earlier step) only as a complete path segment or an "
        + "id/…Id body value of a platform_request step that points back at an earlier native POST step.";

    private static string? ValidateBody(IReadOnlyList<JsonElement> steps, int index, JsonElement node)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in node.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        var text = property.Value.GetString() ?? "";
                        if (!text.Contains(MarkerPrefix, StringComparison.Ordinal))
                            continue;
                        if (!IsIdProperty(property.Name))
                            return $"carries a $step reference in body property {property.Name}; references are allowed only in id/…Id body values";
                        var error = ValidateMarker(steps, index, text);
                        if (error != null)
                            return error;
                    }
                    else
                    {
                        var error = ValidateBody(steps, index, property.Value);
                        if (error != null)
                            return error;
                    }
                }
                return null;
            case JsonValueKind.Array:
                foreach (var item in node.EnumerateArray())
                {
                    var error = ValidateBody(steps, index, item);
                    if (error != null)
                        return error;
                }
                return null;
            case JsonValueKind.String:
                return (node.GetString() ?? "").Contains(MarkerPrefix, StringComparison.Ordinal)
                    ? "carries a $step reference in a non-id body value"
                    : null;
            default:
                return null;
        }
    }

    /// <summary>Backward index plus a native POST source; the marker text itself.</summary>
    private static string? ValidateMarker(IReadOnlyList<JsonElement> steps, int index, string text)
    {
        if (!TryMarkerIndex(steps, index, text, out var reference))
            return Marker.IsMatch(text)
                ? $"references step {text}, which is not an earlier native platform_request POST step"
                : $"carries the malformed step reference {text}";
        return null;
    }

    private static bool TryMarkerIndex(
        IReadOnlyList<JsonElement> steps, int index, string text, out int reference)
    {
        reference = -1;
        return Marker.IsMatch(text)
            && int.TryParse(text[PrefixLength..^IdSuffixLength],
                NumberStyles.None, CultureInfo.InvariantCulture, out reference)
            && reference >= 0
            && reference < index
            && IsNativePost(steps[reference]);
    }

    private static bool IsNativePost(JsonElement step)
    {
        if (!TryReadStep(step, out var tool, out var args) || !IsPlatformRequest(tool))
            return false;
        return args.ValueKind == JsonValueKind.Object
            && args.TryGetProperty("method", out var method)
            && method.ValueKind == JsonValueKind.String
            && string.Equals(method.GetString(), "POST", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIdProperty(string name) =>
        string.Equals(name, "id", StringComparison.Ordinal)
        || name.EndsWith("Id", StringComparison.Ordinal);

    private static bool IsPlatformRequest(string? tool) =>
        string.Equals(NormalizeTool(tool), "platform_request", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeTool(string? tool) =>
        tool?.StartsWith("functions.", StringComparison.OrdinalIgnoreCase) == true
            ? tool["functions.".Length..]
            : tool;

    private static bool TryReadStep(JsonElement step, out string? tool, out JsonElement args)
    {
        tool = null;
        args = default;
        if (step.ValueKind != JsonValueKind.Object)
            return false;
        if (step.TryGetProperty("tool", out var toolEl) && toolEl.ValueKind == JsonValueKind.String)
            tool = toolEl.GetString();
        step.TryGetProperty("args", out args);
        return true;
    }

    private static bool ContainsMarker(JsonElement node)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.String:
                return (node.GetString() ?? "").Contains(MarkerPrefix, StringComparison.Ordinal);
            case JsonValueKind.Object:
                foreach (var property in node.EnumerateObject())
                    if (ContainsMarker(property.Value))
                        return true;
                return false;
            case JsonValueKind.Array:
                foreach (var item in node.EnumerateArray())
                    if (ContainsMarker(item))
                        return true;
                return false;
            default:
                return false;
        }
    }

    /// <summary>
    /// Rewrites markers in a parsed args object. Path segments and id/…Id body
    /// values that match the marker shape are replaced through
    /// <paramref name="resolve"/>; a null return leaves the value unchanged.
    /// </summary>
    private static JsonNode Rewrite(JsonElement args, Func<string, int?> resolve)
    {
        var node = JsonNode.Parse(args.GetRawText())!;
        Rewrite(node, resolve);
        return node;
    }

    private static void Rewrite(JsonNode args, Func<string, int?> resolve)
    {
        var root = args.AsObject();
        if (root["path"] is JsonValue pathValue && pathValue.TryGetValue<string>(out var path))
        {
            var queryAt = path.IndexOf('?');
            var apiPart = queryAt >= 0 ? path[..queryAt] : path;
            var segments = apiPart.Split('/');
            var changed = false;
            for (var i = 0; i < segments.Length; i++)
            {
                if (!segments[i].Contains(MarkerPrefix, StringComparison.Ordinal))
                    continue;
                var id = resolve(segments[i]);
                if (id == null)
                    continue;
                segments[i] = id.Value.ToString(CultureInfo.InvariantCulture);
                changed = true;
            }
            if (changed)
                root["path"] = string.Join('/', segments) + (queryAt >= 0 ? path[queryAt..] : "");
        }
        if (root["body"] is JsonObject body)
            RewriteBody(body, resolve);
    }

    private static void RewriteBody(JsonObject node, Func<string, int?> resolve)
    {
        foreach (var property in node.ToList())
        {
            if (property.Value is JsonValue value && value.TryGetValue<string>(out var text))
            {
                if (text == null
                    || !text.Contains(MarkerPrefix, StringComparison.Ordinal)
                    || !IsIdProperty(property.Key))
                    continue;
                var id = resolve(text);
                if (id != null)
                    node[property.Key] = id.Value;
            }
            else if (property.Value is JsonObject or JsonArray)
            {
                RewriteChild(property.Value, resolve);
            }
        }
    }

    /// <summary>Descends nested objects/arrays; ids still resolve only on named object properties.</summary>
    private static void RewriteChild(JsonNode node, Func<string, int?> resolve)
    {
        switch (node)
        {
            case JsonObject child:
                RewriteBody(child, resolve);
                break;
            case JsonArray array:
                foreach (var item in array)
                    if (item is JsonObject or JsonArray)
                        RewriteChild(item, resolve);
                break;
        }
    }
}
