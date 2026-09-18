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
