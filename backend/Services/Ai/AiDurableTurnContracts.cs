using System.Text.Json;
using Delegat.Models.Ai;

namespace Delegat.Services.Ai;

internal sealed record AiDurableTurnRequest(
    List<ChatMessage> Messages,
    int? ModelId,
    AiPageContext? PageContext,
    string? Locale,
    string? ProposalId,
    List<PlanStepInput> Steps);

public sealed record AiExecutionStepCheckpoint(
    int Index, string Tool, string Input, string ToolCallId, JsonElement Outcome);

public sealed record AiExecutionResume(
    IReadOnlyList<AiExecutionStepCheckpoint> Steps, bool ExecutionEnded);

internal sealed class AiTurnLeaseLostException : OperationCanceledException
{
    public AiTurnLeaseLostException() : base("The durable turn is no longer owned by this worker.") { }
}
