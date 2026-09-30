using System.Text.Json;

namespace Delegat.Services.Ai.Agent;

/// <summary>
/// Cabinet chat must not scout or write before the user confirms a plan.
/// Confirmed steps run in <c>execute-plan</c>, not in the model loop.
/// </summary>
public static class AgentChatTurnGate
{
    public const string ProposePlanTool = "propose_plan";
    public const string PlatformRequestTool = "platform_request";

    public const string DeferredForPlan =
        "Not run. This turn includes propose_plan. Wait for the user to confirm. Do not GET, search_actions, or write first.";

    public const string WriteNeedsPlan =
        "Not run. Cabinet writes only run after the user confirms propose_plan. Call propose_plan as the only tool this turn; use CURRENT PAGE ids.";

    /// <summary>Tool-using model passes before the turn must answer in text.</summary>
    public const int MaxToolRounds = 3;

    /// <summary>One script per user message. A second script is fishing.</summary>
    public const int MaxRunPythonPerTurn = 1;

    public const string PythonBudgetSpent =
        "Not run. One script already ran for this question. Answer the user now from that result. Do not call another tool.";

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

    public static bool IsRunPython(string? toolName) =>
        string.Equals(toolName, "run_python", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// SpeakOnly: no tools, the model must reply.
    /// ProposePlanOnly: a script already ran; the only remaining tool is the plan.
    /// </summary>
    public static (bool SpeakOnly, bool ProposePlanOnly) NextPass(bool answerNow, int toolRounds, int pythonRuns)
    {
        if (answerNow || toolRounds >= MaxToolRounds)
            return (true, false);
        if (pythonRuns >= MaxRunPythonPerTurn)
            return (false, true);
        return (false, false);
    }

    private static string CollapseSpaces(string s)
    {
        var parts = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts);
    }

    public static bool IsProposePlan(string? toolName) =>
        string.Equals(toolName, ProposePlanTool, StringComparison.OrdinalIgnoreCase);

    public static bool BatchHasProposePlan(IEnumerable<(string Id, string Name, string Json)> tools) =>
        tools.Any(t => IsProposePlan(t.Name));

    public static bool IsMutatingChatTool(string? toolName, string? inputJson)
    {
        if (!string.Equals(toolName, PlatformRequestTool, StringComparison.OrdinalIgnoreCase))
            return false;
        return PlatformRequestPolicy.IsMutating(ReadMethod(inputJson));
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
