using System.Runtime.CompilerServices;
using Delegat.Infrastructure.Configuration;
using Delegat.Models.Ai;
using Delegat.Services.Ai.Agent;

namespace Delegat.Services.Ai;

internal interface IAiDurableTurnProducer
{
    IAsyncEnumerable<AiTurnEvent> RunAsync(
        AiTurnJob job, AiDurableTurnRequest request, AiExecutionResume resume, CancellationToken ct);
}

internal sealed class AiDurableTurnProducer(AiChatService chat, AgentSessionResolver session)
    : IAiDurableTurnProducer
{
    public async IAsyncEnumerable<AiTurnEvent> RunAsync(
        AiTurnJob job, AiDurableTurnRequest request, AiExecutionResume resume,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await session.ResolveForUserAsync(job.userId, job.storeId, ct);
        if (!FeatureFlags.IsAiEnabled())
            throw new InvalidOperationException("AI assistant is disabled.");
        var stream = job.kind == "execution"
            ? chat.ExecutePlanEventsAsync(request.Steps, job.storeId, job.userId, request.ProposalId!,
                ct, job.sessionId, request.Locale, resume, job.engine)
            : chat.StreamChatEventsAsync(request.Messages, job.storeId, job.userId,
                request.ModelId, request.PageContext, job.sessionId, request.Locale,
                ct, runtimeOverride: job.engine);
        await foreach (var item in stream.WithCancellation(ct))
            yield return item;
    }
}
