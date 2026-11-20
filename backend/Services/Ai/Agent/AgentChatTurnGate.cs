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
    public const string ReadChatAttachmentTool = "read_chat_attachment";
    public const string AskFormTool = "ask_form";

    public const string DeferredForForm =
        "Not run. This batch requests user input. Present a valid ask_form and wait for the user's submitted answers. Do not infer answers from placeholders, examples, or defaults, or propose work before those answers arrive.";

    public const string FormPresentedWaitingText =
        "Form presented. Waiting for the user's answers. Do not call more tools or propose a plan in this turn.";

    public const string DeferredForPlan =
        "Not run. This tool batch includes propose_plan. Present the plan and wait for confirmation; do not run other tools from this batch. Required read-only lookups belong in an earlier model step.";

    public const string AdditionalPlanDeferred =
        "Not run. This batch already proposed a plan. Present only the first valid plan and wait for confirmation before proposing another.";

    public const string WriteNeedsPlan =
        "Not run. Cabinet writes only run after the user confirms propose_plan. If required facts are missing, do only the minimum required read-only lookup(s), then call propose_plan by itself; otherwise call propose_plan now. Use CURRENT PAGE ids and do not retry this write.";

    public const string ExternalCapabilityNeedsPlan =
        "Not run. Python code and connected external tools may cause side effects, so they only run as steps in a standalone propose_plan after user confirmation. If facts are missing, use only the minimum required built-in read-only lookup(s) first, then propose_plan by itself. Do not retry this call.";

    /// <summary>Review turns allow built-in read-only lookups and propose_plan; mutations, Python, and connected MCP get this refusal.</summary>
    public const string ReviewToolNotRun =
        "Not run. Execution is already finished. Review the actual results and answer, run a built-in read-only lookup, or propose a corrected plan for fresh user confirmation. Mutations, Python, and connected MCP calls cannot run during this review.";

    /// <summary>Review rounds get a bounded request count independent of chat rounds.</summary>
    public const int ReviewMaxRequests = 6;

    /// <summary>
    /// Post-execution review may run built-in read-only lookups and propose_plan —
    /// never mutations, Python, or connected MCP execution.
    /// </summary>
    public static bool IsReviewToolAllowed(string? toolName, string? inputJson) =>
        IsProposePlan(toolName)
        || toolName is "search_actions" or "search_knowledge" or "get_doc"
            or "search_catalog" or "sql_readonly" or "memory_search"
        || (string.Equals(toolName, PlatformRequestTool, StringComparison.OrdinalIgnoreCase)
            && ReadMethod(inputJson).ToUpperInvariant() is "GET" or "HEAD");

    /// <summary>Tools advertised to the model while a review round is open.</summary>
    public static bool IsReviewToolAdvertised(string? toolName) =>
        IsProposePlan(toolName)
        || string.Equals(toolName, PlatformRequestTool, StringComparison.OrdinalIgnoreCase)
        || toolName is "search_actions" or "search_knowledge" or "get_doc"
            or "search_catalog" or "sql_readonly" or "memory_search";

    /// <summary>A plan batched together with an attachment read waits for the real file data.</summary>
    public const string AttachmentDeferralPlanText =
        "Plan not presented. First read the attached file and use its actual contents; then propose the exact plan by itself.";

    public const string InvalidPlanText =
        "Plan not presented: provide a nonempty summary and at least one step. Every step needs a tool other than propose_plan, a nonempty description in the store language, and an args object with exact arguments. A step may contain only tool, description, and args. Put every tool argument, including run_python outputs, inside args; never beside args. For a named export, productIds must be nonempty, unique, and not combined with allMatching/where; affectedCount, if provided, must match the number of unique ids. Call propose_plan again by itself.";

    public const string PlanPresentedWaitingText =
        "Plan presented to user. Waiting for confirmation. Do not execute any operations until user confirms.";

    /// <summary>One-shot nudge after a text-only reply that ends with unfulfilled intent.</summary>
    public const string ContinueAfterIntentNudge =
        "Continue in this turn. If you announced a cabinet write, do only the minimum explicitly required read-only lookup(s) if needed, then call propose_plan by itself; if no lookup is needed, call propose_plan now. Never perform a write before confirmation. If you announced a live-data question, call the required read now. Do not reply with text only.";

    /// <summary>One-shot nudge after a round that produced neither a reply nor a plan.</summary>
    public const string SilentTurnNudge =
        "You stopped with no reply. The task is not finished. If a fact is still missing, call the next tool. If you already have it, answer or propose_plan now. Do not end the turn on tools alone.";

    /// <summary>Injected once tool rounds reach <see cref="RunawayToolRounds"/>.</summary>
    public const string CloseTaskNowMessage =
        "Close the task now from what you already have: a sentence or propose_plan. Do not call another tool.";

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

    /// <summary>
    /// A bounded result review that produced neither a reply nor a valid proposal is
    /// reported as incomplete; the completed execution is never restated as stopped.
    /// </summary>
    public static string ReviewIncompleteMessage(string? locale) =>
        string.Equals(locale, "en", StringComparison.OrdinalIgnoreCase)
            ? "Execution has already finished, but I could not complete the result review or prepare a follow-up plan. Do not repeat the completed steps. Ask me to continue from the existing results."
            : "Выполнение плана уже завершено, но не удалось закончить проверку результата или подготовить следующий план. Не повторяйте выполненные шаги. Попросите продолжить с уже полученного результата.";

    public static bool IsAskForm(string? toolName) =>
        string.Equals(toolName, AskFormTool, StringComparison.OrdinalIgnoreCase);

    /// <summary>True only when an ask_form dispatch actually returned a valid form.</summary>
    public static bool IsPresentedForm(string? toolName, object? result)
    {
        if (!IsAskForm(toolName) || result == null) return false;
        var json = JsonSerializer.SerializeToElement(result);
        return json.ValueKind == JsonValueKind.Object
            && json.TryGetProperty("form", out var form)
            && form.ValueKind == JsonValueKind.True;
    }

    public static bool IsProposePlan(string? toolName) =>
        string.Equals(toolName, ProposePlanTool, StringComparison.OrdinalIgnoreCase);

    public static bool BatchHasProposePlan(IEnumerable<(string Id, string Name, string Json)> tools) =>
        tools.Any(t => IsProposePlan(t.Name));

    public static bool RequiresPlanConfirmation(string? toolName, string? inputJson)
    {
        if (string.Equals(toolName, "run_python", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, GeneratedFileContract.ImportToolName, StringComparison.OrdinalIgnoreCase)
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
