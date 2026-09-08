using System.Text.Json;
using System.Text.RegularExpressions;
using Delegat.Services.Ai.Prompts;

namespace Delegat.Services.Ai.Agent;

/// <summary>
/// Shared success/failure for cabinet tool results. Nested HTTP uses { title, status, code }
/// without an <c>error</c> field — treat 4xx/5xx as failed so execute-plan cannot claim success.
/// </summary>
public static class AgentToolResult
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Regex HttpErrorSummary = new(
        @"HTTP\s+[45]\d\d",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
        if (result.TryGetProperty("error", out var err)
            && err.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            return true;
        if (result.TryGetProperty("status", out var st)
            && st.TryGetInt32(out var code)
            && code >= 400)
            return true;
        return false;
    }

    public static string FormatPlanOutcome(
        int succeeded,
        int total,
        IReadOnlyList<string> errors,
        string? locale)
    {
        var ru = AiPrompts.NormalizeLocale(locale) != "en";
        if (errors.Count == 0)
        {
            return ru
                ? $"Готово: выполнены все шаги ({succeeded} из {total})."
                : $"All steps finished ({succeeded} of {total}).";
        }

        var list = string.Join("\n", errors.Select(e => "• " + e));
        if (succeeded == 0)
        {
            return ru
                ? $"Не получилось ({succeeded} из {total}). В каталоге ничего не изменилось.\n{list}"
                : $"Nothing was changed ({succeeded} of {total} steps succeeded).\n{list}";
        }

        return ru
            ? $"Сделано частично ({succeeded} из {total}). Часть шагов уже применена — проверьте каталог и корзину.\n{list}"
            : $"Partly done ({succeeded} of {total}). Some steps already applied — check the catalog and trash.\n{list}";
    }
}
