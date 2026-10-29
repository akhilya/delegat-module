using System.Text.Json;

namespace Delegat.Services.Ai.Agent;

/// <summary>
/// Built-in read-only lookups may precede a plan. Writes and external capabilities are
/// deferred until the user confirms; confirmed steps run in <c>execute-plan</c>, not in
/// the model loop.
/// </summary>
public static class AgentChatTurnGate
{
    public const string ProposePlanTool = "propose_plan";
    public const string PlatformRequestTool = "platform_request";

    public const string DeferredForPlan =
        "Not run. This tool batch includes propose_plan. Present the plan and wait for confirmation; do not run other tools from this batch. Required read-only lookups belong in an earlier model step.";

    public const string AdditionalPlanDeferred =
        "Not run. This batch already proposed a plan. Present only the first valid plan and wait for confirmation before proposing another.";

    public const string WriteNeedsPlan =
        "Not run. Cabinet writes only run after the user confirms propose_plan. If required facts are missing, do only the minimum required read-only lookup(s), then call propose_plan by itself; otherwise call propose_plan now. Use CURRENT PAGE ids and do not retry this write.";

    public const string ExternalCapabilityNeedsPlan =
        "Not run. Python code and connected external tools may cause side effects, so they only run as steps in a standalone propose_plan after user confirmation. If facts are missing, use only the minimum required built-in read-only lookup(s) first, then propose_plan by itself. Do not retry this call.";

    /// <summary>
    /// Stuck-loop fuse. A normal task is not cut here. Past this, the turn must close
    /// with a sentence or a plan instead of calling tools forever.
    /// </summary>
    public const int RunawayToolRounds = 40;

    /// <summary>Shown when a turn used tools and still produced no sentence and no plan.</summary>
    public static string SilentTurnFallback(string? locale) =>
        string.Equals(locale, "en", StringComparison.OrdinalIgnoreCase)
            ? "I stopped without an answer. Send that again and I will reply."
            : "Я остановился и ничего не ответил. Напишите ещё раз — отвечу.";

    /// <summary>
    /// The latest user message is only “answer now”, not a new question.
    /// </summary>
    public static bool AsksToAnswerNow(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().Trim('"', '\'', '«', '»').Trim();
        s = s.TrimEnd('.', '!', '?', '…', ' ').Trim();
        if (s.Length == 0 || s.Length > 48) return false;
        s = CollapseSpaces(s).ToLowerInvariant();
        return s is "ответь" or "отвечай" or "ответьте" or "ну ответь" or "дай ответ"
            or "скажи уже" or "хватит" or "хватит искать" or "достаточно"
            or "стоп" or "ответь пожалуйста"
            or "answer" or "answer now" or "just answer" or "please answer"
            or "stop" or "enough" or "that's enough" or "that is enough";
    }

    public static bool SpeakOnly(bool answerNow, int toolRounds) =>
        answerNow || toolRounds >= RunawayToolRounds;

    private static string CollapseSpaces(string s)
    {
        var parts = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts);
    }

    public static bool IsProposePlan(string? toolName) =>
        string.Equals(toolName, ProposePlanTool, StringComparison.OrdinalIgnoreCase);

    public static bool BatchHasProposePlan(IEnumerable<(string Id, string Name, string Json)> tools) =>
        tools.Any(t => IsProposePlan(t.Name));

    public static bool RequiresPlanConfirmation(string? toolName, string? inputJson)
    {
        if (string.Equals(toolName, "run_python", StringComparison.OrdinalIgnoreCase)
            || McpToolNames.TryParse(toolName, out _, out _))
            return true;

        return string.Equals(toolName, PlatformRequestTool, StringComparison.OrdinalIgnoreCase)
            && PlatformRequestPolicy.IsMutating(ReadMethod(inputJson));
    }

    internal static string ReadMethod(string? inputJson)
    {
        if (string.IsNullOrWhiteSpace(inputJson)) return "";
        try
        {
            using var doc = JsonDocument.Parse(inputJson);
            if (doc.RootElement.TryGetProperty("method", out var m))
                return m.GetString() ?? "";
        }
        catch (JsonException)
        {
            // ignore malformed tool JSON
        }
        return "";
    }
}
