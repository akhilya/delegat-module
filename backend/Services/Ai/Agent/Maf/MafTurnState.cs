using System.Threading.Channels;
using Microsoft.Extensions.AI;

namespace Delegat.Services.Ai.Agent.Maf;

/// <summary>
/// Mutable per-turn state shared by the MAF turn gate, the DeepSeek DSML decorator, and
/// the tool adapters of one chat turn. It carries exactly the per-turn variables the
/// legacy StreamChatAsync loop keeps on the stack (tool rounds, plan flag, nudge flags,
/// current-batch composition) plus the SSE side-channel adapters write tool/plan events
/// into while the framework streams updates.
/// </summary>
internal sealed class MafTurnState
{
    public required int StoreId { get; init; }
    public required int UserId { get; init; }
    public required string? SessionId { get; init; }
    public required string LocaleCode { get; init; }
    public required int ModelConfigId { get; init; }
    public required bool ReviewMode { get; init; }
    public required bool AnswerNow { get; init; }
    public required AgentToolHost ToolHost { get; init; }
    public required AiTurnTelemetry Telemetry { get; init; }
    public required ILogger Logger { get; init; }
    public required Channel<string> Events { get; init; }

    /// <summary>Provider requests already forwarded downstream (== legacy iteration index).</summary>
    public int RequestsSent { get; set; }

    /// <summary>0-based index of the request currently in flight; feeds dsml call ids.</summary>
    public int CurrentRequestIndex { get; set; }

    /// <summary>Dispatched tool batches; mirrors the legacy toolRounds counter.</summary>
    public int ToolRounds { get; set; }

    /// <summary>A plan was minted and the Plan SSE was written; the turn ends.</summary>
    public bool SawPlan { get; set; }

    /// <summary>The engine emitted a visible reply delta (mirrors legacy spoke).</summary>
    public bool Spoke { get; set; }

    // ----- per-batch flags, set by the gate when a response carries tool calls -----

    /// <summary>Current dispatch batch contains propose_plan; siblings defer.</summary>
    public bool WaitForPlan { get; set; }

    /// <summary>Current dispatch batch contains read_chat_attachment.</summary>
    public bool AttachmentReadInBatch { get; set; }

    /// <summary>Read+plan in one batch: the read still runs, the plan defers.</summary>
    public bool DeferPlanForAttachmentRead => WaitForPlan && AttachmentReadInBatch;

    /// <summary>Text of the last provider response (per model round, like legacy textBuffer).</summary>
    public string LastRoundText { get; set; } = "";

    /// <summary>The last forwarded response carried calls the gate stripped (speak-only).</summary>
    public bool LastRoundStrippedCalls { get; set; }

    /// <summary>The last request was forwarded without the tool surface.</summary>
    public bool LastRequestSpeakOnly { get; set; }

    /// <summary>The gate refused a request past the iteration cap; ends like a legacy loop exit.</summary>
    public bool TerminalByCap { get; set; }

    /// <summary>A downstream provider call threw; the engine maps it to the Error SSE.</summary>
    public bool ProviderError { get; set; }

    /// <summary>Character estimate of the request being forwarded (telemetry only).</summary>
    public long CurrentRequestInputChars { get; set; }

    /// <summary>Speak-only decision for the request about to be sent.</summary>
    public bool SpeakOnly =>
        AnswerNow
        || ToolRounds >= AgentChatTurnGate.RunawayToolRounds
        || (ReviewMode && RequestsSent >= 2);

    /// <summary>Request cap reached (review: 3 requests; normal: RunawayToolRounds + 2).</summary>
    public bool CapReached =>
        RequestsSent >= (ReviewMode ? 3 : AgentChatTurnGate.RunawayToolRounds + 2);

    // ----- tool surface for FunctionInvokingChatClient dispatch -----

    /// <summary>
    /// Names with a registered adapter (advertised or AdditionalTools). The gate adds a
    /// policy adapter for any other call name the model emits so every tool_use still
    /// ends in the application-owned result (ReviewToolNotRun / DeferredForPlan /
    /// "Unknown tool") instead of the framework's "function not found" text.
    /// </summary>
    public HashSet<string> KnownToolNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The same list instance handed to FunctionInvokingChatClient.AdditionalTools; the
    /// gate appends late-bound adapters here for uncatalogued call names. These adapters
    /// are never advertised - they only exist so FICC dispatches through the policy path.
    /// </summary>
    public List<AITool> AdditionalToolSink { get; set; } = [];
}
