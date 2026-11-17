using System.Text.Json;
using System.Text.RegularExpressions;
using Delegat.Services.Ai.Prompts;

namespace Delegat.Services.Ai.Agent;

/// <summary>
/// Shared success/failure for cabinet tool results. Nested HTTP uses { title, status, code }
/// without an <c>error</c> field — treat 4xx/5xx as failed so execute-plan cannot claim success.
/// Catalog Connect jobs use a string <c>status</c> (pending/running/completed) and a domain
/// <c>error</c> (lastError); HTTP 200 enqueue is not a failed write.
/// </summary>
public static class AgentToolResult
{
    public const int MaxModelResultChars = 24_000;
    public const string ProviderErrorMessage = "Error requesting the AI API. Please try again.";
    public const string TurnInterruptedMessage =
        "Could not finish this reply. If a catalog action already started, check Operations.";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Regex HttpErrorSummary = new(
        @"HTTP\s+[45]\d\d",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string TruncateForModel(string json)
    {
        if (string.IsNullOrEmpty(json) || json.Length <= MaxModelResultChars)
            return json;

        var important = ExtractImportantFields(json);
        // Verified generated-artifact handles survive truncation verbatim beside
        // the root execution verdict; no other structural fields are promoted.
        var artifacts = ExtractArtifactHandles(json);
        var previewLength = Math.Min(json.Length, MaxModelResultChars);
        while (true)
        {
            var preview = json[..SafePrefixLength(json, previewLength)];
            var result = SerializeEnvelope(json.Length, important, artifacts, preview);
            if (result.Length <= MaxModelResultChars)
                return result;

            var excess = result.Length - MaxModelResultChars;
            var nextLength = previewLength - Math.Max(256, excess);
            previewLength = nextLength >= previewLength ? previewLength / 2 : Math.Max(0, nextLength);
            if (previewLength == 0)
                return SerializeEnvelope(json.Length, important, artifacts, "");
        }
    }

    private static string SerializeEnvelope(
        int originalLength,
        Dictionary<string, object?> important,
        IReadOnlyList<GeneratedArtifactMetadata> artifacts,
        string preview)
    {
        // The artifacts field is only emitted when validated handles exist, so
        // non-artifact truncation keeps its historical shape byte for byte.
        if (artifacts.Count == 0)
            return JsonSerializer.Serialize(new
            {
                truncated = true,
                originalLength,
                important,
                preview,
            }, JsonOpts);
        var envelope = new Dictionary<string, object?>
        {
            ["truncated"] = true,
            ["originalLength"] = originalLength,
            ["important"] = important,
            ["artifacts"] = artifacts,
        };
        // Keep the root execution verdict beside promoted handles for SSE/history;
        // never infer a success from artifacts or from a nested text preview.
        string[] verdictFields =
            ["exitCode", "error", "errorClass", "outcomeUnknown", "isError", "status"];
        foreach (var field in verdictFields)
            if (important.TryGetValue(field, out var value))
                envelope[field] = value;
        envelope["preview"] = preview;
        return JsonSerializer.Serialize(envelope, JsonOpts);
    }

    /// <summary>
    /// Collects up to <see cref="GeneratedFileContract.MaxOutputs"/> verified
    /// handles from the app-owned envelopes only: root <c>artifacts</c>, a prior
    /// envelope's <c>important.artifacts</c>, and each <c>result</c> element of a
    /// plan-outcomes array (which may itself be a truncated envelope). Raw text
    /// fields are never parsed and no other object fields are promoted.
    /// </summary>
    private static List<GeneratedArtifactMetadata> ExtractArtifactHandles(string json)
    {
        var handles = new List<GeneratedArtifactMetadata>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(json);
            CollectArtifactHandles(doc.RootElement, handles, seen);
        }
        catch (JsonException)
        {
            // Invalid JSON still truncates to the plain envelope.
        }
        return handles;
    }

    private static void CollectArtifactHandles(
        JsonElement node, List<GeneratedArtifactMetadata> handles, HashSet<string> seen)
    {
        if (handles.Count >= GeneratedFileContract.MaxOutputs)
            return;
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in node.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Object
                    && element.TryGetProperty("result", out var result))
                    CollectArtifactHandles(result, handles, seen);
            }
            return;
        }
        if (node.ValueKind != JsonValueKind.Object)
            return;
        if (node.TryGetProperty("artifacts", out var artifacts)
            && artifacts.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in artifacts.EnumerateArray())
            {
                if (handles.Count >= GeneratedFileContract.MaxOutputs)
                    break;
                var meta = TryReadArtifactMetadata(item);
                if (meta != null && seen.Add(meta.ArtifactId))
                    handles.Add(meta);
            }
        }
        if (node.TryGetProperty("important", out var important)
            && important.ValueKind == JsonValueKind.Object)
            CollectArtifactHandles(important, handles, seen);
        // Serialized AgentToolResult wraps its payload in a `data` object.
        if (node.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object)
            CollectArtifactHandles(data, handles, seen);
    }

    private static GeneratedArtifactMetadata? TryReadArtifactMetadata(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("artifactId", out var idElement)
            || idElement.ValueKind != JsonValueKind.String
            || !GeneratedFileContract.IsValidArtifactId(idElement.GetString())
            || !element.TryGetProperty("fileName", out var nameElement)
            || nameElement.ValueKind != JsonValueKind.String
            || !GeneratedFileContract.IsValidOutputFileName(nameElement.GetString())
            || !element.TryGetProperty("contentType", out var typeElement)
            || typeElement.ValueKind != JsonValueKind.String
            || !GeneratedFileContract.TryNormalizeContentType(typeElement.GetString(), out var contentType)
            || !element.TryGetProperty("length", out var lengthElement)
            || lengthElement.ValueKind != JsonValueKind.Number
            || !lengthElement.TryGetInt64(out var length)
            || length < 0 || length > GeneratedFileContract.MaxFileBytes
            || !element.TryGetProperty("sha256", out var shaElement)
            || shaElement.ValueKind != JsonValueKind.String
            || !GeneratedFileContract.IsValidSha256(shaElement.GetString())
            || !element.TryGetProperty("expiresAtUtc", out var expiryElement)
            || expiryElement.ValueKind != JsonValueKind.String
            || !GeneratedFileContract.TryParseInstant(expiryElement.GetString(), out var expiry))
            return null;
        // Optional canonical download URL: when present it must be the exact
        // server route for this artifact id - a foreign or malformed URL
        // rejects the whole handle rather than passing a substituted link.
        string? downloadUrl = null;
        if (element.TryGetProperty("downloadUrl", out var urlElement)
            && urlElement.ValueKind != JsonValueKind.Null)
        {
            if (urlElement.ValueKind != JsonValueKind.String
                || urlElement.GetString()
                    != GeneratedFileContract.TemporaryDownloadUrl(idElement.GetString()!))
                return null;
            downloadUrl = urlElement.GetString();
        }
        return new GeneratedArtifactMetadata(
            idElement.GetString()!, nameElement.GetString()!, contentType, length,
            shaElement.GetString()!, GeneratedFileContract.TruncateToMicroseconds(expiry),
            downloadUrl);
    }

    private static Dictionary<string, object?> ExtractImportantFields(string json)
    {
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return fields;

            string[] names = ["error", "errorClass", "status", "code", "exitCode", "title", "message", "id", "fileName", "operationId", "totalCount", "totalRows", "totalCharacters", "nextStartRow", "nextStartColumn", "nextStartOffset", "hasMore", "truncated", "isError", "outcomeUnknown", "retryable"];
            foreach (var name in names)
            {
                var pascalName = char.ToUpperInvariant(name[0]) + name[1..];
                if (!doc.RootElement.TryGetProperty(name, out var value)
                    && !doc.RootElement.TryGetProperty(pascalName, out value))
                    continue;

                fields[name] = value.ValueKind switch
                {
                    JsonValueKind.String => ClipDiagnostic(value.GetString() ?? ""),
                    JsonValueKind.Object or JsonValueKind.Array => ClipDiagnostic(value.GetRawText()),
                    JsonValueKind.Undefined => null,
                    _ => value.Clone(),
                };
            }
        }
        catch (JsonException)
        {
            // The envelope still returns a valid JSON result with a text preview.
        }

        return fields;
    }

    private static string ClipDiagnostic(string value)
    {
        const int maxLength = 512;
        if (value.Length <= maxLength)
            return value;
        return value[..SafePrefixLength(value, maxLength)] + "…(truncated)";
    }

    private static int SafePrefixLength(string value, int length)
    {
        if (length > 0 && length < value.Length
            && char.IsHighSurrogate(value[length - 1])
            && char.IsLowSurrogate(value[length]))
            return length - 1;
        return length;
    }

    public static bool IsFailure(object? result, string? summary)
    {
        if (!string.IsNullOrEmpty(summary) && HttpErrorSummary.IsMatch(summary))
            return true;
        if (result == null)
            return false;
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result, JsonOpts));
            return IsFailure(doc.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool IsFailure(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object)
            return false;
        if (result.TryGetProperty("exitCode", out var exitCode)
            && exitCode.ValueKind == JsonValueKind.Number
            && exitCode.TryGetInt32(out var processExitCode)
            && processExitCode != 0)
            return true;
        if (result.TryGetProperty("status", out var st))
        {
            if (st.ValueKind == JsonValueKind.String)
            {
                var job = st.GetString();
                return string.Equals(job, "failed", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(job, "cancelled", StringComparison.OrdinalIgnoreCase);
            }
            if (st.TryGetInt32(out var code) && code >= 400)
                return true;
        }
        if (result.TryGetProperty("error", out var err)
            && err.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
            && !(err.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(err.GetString())))
            return true;
        return false;
    }

    public static bool IsQueuedJob(object? result)
    {
        if (result == null) return false;
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result, JsonOpts));
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("status", out var status)
                || status.ValueKind != JsonValueKind.String)
                return false;

            var value = status.GetString();
            return value is not null
                && (value.Equals("pending", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("running", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("queued", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("in_progress", StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string FormatPlanOutcome(
        int succeeded,
        int total,
        IReadOnlyList<string> errors,
        string? locale,
        int queued = 0)
    {
        var ru = AiPrompts.NormalizeLocale(locale) != "en";
        if (errors.Count == 0)
        {
            if (queued > 0)
                return ru
                    ? $"Шаги плана запущены: {succeeded} из {total}; фоновых операций поставлено в очередь: {queued}. Фактическое завершение отслеживается отдельно."
                    : $"Plan steps started: {succeeded} of {total}; background operations queued: {queued}. Their completion is tracked separately.";
            return ru
                ? $"Шаги плана выполнены: {succeeded} из {total}. Результат задачи ещё требует проверки."
                : $"Plan steps completed: {succeeded} of {total}. The task result still needs verification.";
        }

        // Human status text never embeds raw tool diagnostics - those stay in the
        // structured tool results and the review payload.
        var queuedNote = queued > 0
            ? (ru ? $" Фоновых операций поставлено в очередь: {queued}." : $" Background operations queued: {queued}.")
            : "";
        var applied = Math.Max(0, succeeded - queued);
        if (succeeded == 0 && queued == 0)
        {
            return ru
                ? $"Не удалось завершить шаги ({succeeded} из {total}). Ошибка не доказывает, что изменений не было; проверьте результат перед повторным запуском.{queuedNote}"
                : $"No steps completed successfully ({succeeded} of {total}). A failure does not prove that nothing changed; check the outcome before retrying.{queuedNote}";
        }

        if (queued > 0)
        {
            var appliedNote = applied > 0
                ? (ru ? " Часть синхронных шагов уже применена." : " Some synchronous steps were applied.")
                : "";
            return ru
                ? $"Не все шаги выполнены ({succeeded} из {total} выполнено или поставлено в очередь).{appliedNote}{queuedNote}"
                : $"Not all steps completed ({succeeded} of {total} completed or queued).{appliedNote}{queuedNote}";
        }

        return ru
            ? $"Сделано частично ({succeeded} из {total}). Часть шагов уже применена — проверьте каталог и корзину.{queuedNote}"
            : $"Partly done ({succeeded} of {total}). Some steps already applied — check the catalog and trash.{queuedNote}";
    }
}
