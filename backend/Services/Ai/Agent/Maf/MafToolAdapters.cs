using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace Delegat.Services.Ai.Agent.Maf;

/// <summary>Serialization helpers shared by the MAF tool adapters.</summary>
internal static class MafToolAdapterShared
{
    internal static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Arguments arrive as the framework's dictionary; emit the same JSON shape
    /// the legacy loop reads from the raw tool_use input block.</summary>
    internal static string SerializeArguments(AIFunctionArguments? arguments)
    {
        if (arguments == null || arguments.Count == 0) return "{}";
        try { return JsonSerializer.Serialize(arguments, JsonOpts); }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return "{}";
        }
    }
}

/// <summary>
/// One catalog/MCP tool exposed to the framework as an <see cref="AIFunction"/>.
/// The batch gate flags in <see cref="MafTurnState"/> decide whether a call dispatches:
/// plan batches defer siblings, review mode refuses everything but propose_plan, and
/// write/external capabilities return the exact legacy synthetic results - FICC then
/// turns each return value into the matching <see cref="FunctionResultContent"/>.
/// </summary>
internal sealed class MafToolAdapter(AiTool tool, MafTurnState state) : AIFunction
{
    public override string Name => tool.name;
    public override string Description => tool.description ?? "";
    public override JsonElement JsonSchema => AiApiClient.ToolSchemaElement(tool.input_schema);

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var argsJson = MafToolAdapterShared.SerializeArguments(arguments);
        var toolName = tool.name;
        var callId = FunctionInvokingChatClient.CurrentContext?.CallContent?.CallId ?? "";

        // Same decision order as the legacy dispatch loop.
        if (state.ReviewMode)
            return AgentChatTurnGate.ReviewToolNotRun;

        if (state.WaitForPlan
            && !(state.DeferPlanForAttachmentRead
                && string.Equals(toolName, AgentChatTurnGate.ReadChatAttachmentTool, StringComparison.OrdinalIgnoreCase)))
            return AgentChatTurnGate.DeferredForPlan;

        if (AgentChatTurnGate.RequiresPlanConfirmation(toolName, argsJson))
        {
            return McpToolNames.TryParse(toolName, out _, out _)
                || string.Equals(toolName, "run_python", StringComparison.OrdinalIgnoreCase)
                ? AgentChatTurnGate.ExternalCapabilityNeedsPlan
                : AgentChatTurnGate.WriteNeedsPlan;
        }

        state.Events.Writer.TryWrite(Sse(SseEventType.Tool,
            new { id = callId, name = toolName, status = "running", input = argsJson }));

        object result;
        string summary;
        var toolTimer = Stopwatch.StartNew();
        using var toolSpan = state.Telemetry.StartToolExecution(toolName);
        try
        {
            (result, summary) = await state.ToolHost.DispatchAsync(
                toolName, argsJson, state.StoreId, state.UserId, cancellationToken,
                allowConnectedMcp: true,
                allowChatAttachments: true,
                chatSessionId: state.SessionId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            state.Logger.LogWarning(ex, "Agent tool {Tool} threw", toolName);
            result = new { error = ex.Message };
            summary = $"Error: {ex.Message}";
        }
        toolTimer.Stop();
        var toolFailed = AgentToolResult.IsFailure(result, summary);
        toolSpan.SetOutcome(toolFailed ? "failed" : "completed");
        state.Telemetry.RecordToolExecution(toolTimer.ElapsedMilliseconds, toolFailed);
        var resultJson = AgentToolResult.TruncateForModel(
            JsonSerializer.Serialize(result, MafToolAdapterShared.JsonOpts));
        AiChatService.ToolEffects.TryGetValue(toolName, out var effects);
        state.Events.Writer.TryWrite(Sse(SseEventType.Tool,
            new { id = callId, name = toolName, status = "done", summary, result = resultJson, effects }));
        return resultJson;
    }

    private static string Sse(string type, object data) => AiSseTurnRecorder.Format(type, data);
}

/// <summary>
/// propose_plan is host-controlled: the adapter validates the plan, mints the signed
/// token, writes the Plan SSE into the turn event channel, and flips
/// <see cref="MafTurnState.SawPlan"/> so the gate ends the turn after this batch -
/// matching the legacy loop, which never queries the model again after a valid plan.
/// </summary>
internal sealed class MafProposePlanAdapter(
    AiTool tool,
    MafTurnState state) : AIFunction
{
    public override string Name => tool.name;
    public override string Description => tool.description ?? "";
    public override JsonElement JsonSchema => AiApiClient.ToolSchemaElement(tool.input_schema);

    protected override ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var argsJson = MafToolAdapterShared.SerializeArguments(arguments);

        if (state.SawPlan)
            return ValueTask.FromResult<object?>(AgentChatTurnGate.AdditionalPlanDeferred);

        if (state.DeferPlanForAttachmentRead)
            return ValueTask.FromResult<object?>(AgentChatTurnGate.AttachmentDeferralPlanText);

        var planInput = ProposePlanReader.Read(argsJson);
        if (!planInput.HasContent)
            return ValueTask.FromResult<object?>(AgentChatTurnGate.InvalidPlanText);

        var feasibilityReason = state.ToolHost.Capabilities.PlanStepRejection(planInput.Steps);
        if (feasibilityReason != null)
            return ValueTask.FromResult<object?>(feasibilityReason);

        var efficiencyReason = AgentPlanEfficiencyGuard.RejectionReason(planInput.Steps);
        if (efficiencyReason != null)
            return ValueTask.FromResult<object?>(efficiencyReason);

        var planCreatedAt = DateTime.UtcNow;
        var serializedSteps = JsonSerializer.Serialize(planInput.Steps, MafToolAdapterShared.JsonOpts);
        var stepsNode = JsonNode.Parse(serializedSteps)!;
        var stepsHash = AiConversationTurnStore.ComputeStepsHash(stepsNode);
        var expiresAt = planCreatedAt.AddMinutes(15);
        var (proposalId, proposalNonce) = AiPlanProposalToken.Mint(
            state.SessionId ?? "", state.StoreId, state.UserId, stepsHash, expiresAt);
        state.Telemetry.Complete("plan_proposed");
        state.Events.Writer.TryWrite(AiSseTurnRecorder.Format(SseEventType.Plan, new
        {
            proposalId,
            proposalNonce,
            stepsHash,
            createdAtUtc = planCreatedAt,
            expiresAtUtc = expiresAt,
            modelConfigId = state.ModelConfigId,
            discardDraftText = true,
            summary = planInput.Summary,
            steps = planInput.Steps,
            affectedCount = planInput.AffectedCount,
            isDestructive = planInput.IsDestructive
        }));
        state.SawPlan = true;
        return ValueTask.FromResult<object?>(AgentChatTurnGate.PlanPresentedWaitingText);
    }
}
