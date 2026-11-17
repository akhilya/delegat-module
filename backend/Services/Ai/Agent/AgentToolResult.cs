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
        var previewLength = Math.Min(json.Length, MaxModelResultChars);
        while (true)
        {
            var preview = json[..SafePrefixLength(json, previewLength)];
            var result = JsonSerializer.Serialize(new
            {
                truncated = true,
                originalLength = json.Length,
                important,
                preview,
            }, JsonOpts);
            if (result.Length <= MaxModelResultChars)
                return result;

            var excess = result.Length - MaxModelResultChars;
            var nextLength = previewLength - Math.Max(256, excess);
            previewLength = nextLength >= previewLength ? previewLength / 2 : Math.Max(0, nextLength);
            if (previewLength == 0)
            {
                return JsonSerializer.Serialize(new
                {
                    truncated = true,
                    originalLength = json.Length,
                    important,
                    preview = "",
                }, JsonOpts);
            }
        }
    }

    private static Dictionary<string, object?> ExtractImportantFields(string json)
    {
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return fields;

            string[] names = ["error", "errorClass", "status", "code", "exitCode", "title", "message", "id", "fileName", "operationId", "totalCount", "totalRows", "totalCharacters", "nextStartRow", "nextStartColumn", "nextStartOffset", "hasMore", "truncated", "isError"];
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
                ? $"Готово: выполнены все шаги ({succeeded} из {total})."
                : $"All steps finished ({succeeded} of {total}).";
        }

        var list = string.Join("\n", errors.Select(e => "• " + e));
        var queuedNote = queued > 0
            ? (ru ? $" Фоновых операций поставлено в очередь: {queued}." : $" Background operations queued: {queued}.")
            : "";
        var applied = Math.Max(0, succeeded - queued);
        if (succeeded == 0 && queued == 0)
        {
            return ru
                ? $"Не удалось завершить шаги ({succeeded} из {total}). Ошибка не доказывает, что изменений не было; проверьте результат перед повторным запуском.{queuedNote}\n{list}"
                : $"No steps completed successfully ({succeeded} of {total}). A failure does not prove that nothing changed; check the outcome before retrying.{queuedNote}\n{list}";
        }

        if (queued > 0)
        {
            var appliedNote = applied > 0
                ? (ru ? " Часть синхронных шагов уже применена." : " Some synchronous steps were applied.")
                : "";
            return ru
                ? $"Не все шаги выполнены ({succeeded} из {total} выполнено или поставлено в очередь).{appliedNote}{queuedNote}\n{list}"
                : $"Not all steps completed ({succeeded} of {total} completed or queued).{appliedNote}{queuedNote}\n{list}";
        }

        return ru
            ? $"Сделано частично ({succeeded} из {total}). Часть шагов уже применена — проверьте каталог и корзину.{queuedNote}\n{list}"
            : $"Partly done ({succeeded} of {total}). Some steps already applied — check the catalog and trash.{queuedNote}\n{list}";
    }
}
