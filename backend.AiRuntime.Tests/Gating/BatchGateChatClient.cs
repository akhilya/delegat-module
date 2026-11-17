using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Delegat.AiRuntime.Tests.Gating;

/// <summary>
/// Policy hook evaluated once per complete model round over the WHOLE tool-call batch,
/// before <see cref="FunctionInvokingChatClient"/> dispatches any member of that batch.
/// This is the §9.2 pre-dispatch batch-inspection seam: a response-boundary interceptor
/// inside the single framework loop, not a second loop and not per-call middleware.
/// </summary>
internal interface IBatchPolicy
{
    /// <summary>Returns the calls of the batch that must NOT be dispatched this round.</summary>
    IReadOnlyList<FunctionCallContent> Evaluate(IReadOnlyList<FunctionCallContent> batch);
}

/// <summary>What the gate saw for one provider round.</summary>
internal sealed record InspectedCall(string CallId, string Name, string ArgumentsJson);

internal sealed record BatchInspection
{
    public static readonly BatchInspection NoCalls = new()
    {
        Calls = [],
        AllowedCallIds = [],
        DeferredCallIds = [],
        UnknownToolNames = [],
    };

    /// <summary>Every FunctionCallContent in the round's batch, with full arguments.</summary>
    public required IReadOnlyList<InspectedCall> Calls { get; init; }

    /// <summary>CallIds the policy allowed (forwarded to FICC and thus dispatchable).</summary>
    public required IReadOnlyList<string> AllowedCallIds { get; init; }

    /// <summary>CallIds stripped from the forwarded response (never dispatched this round).</summary>
    public required IReadOnlyList<string> DeferredCallIds { get; init; }

    /// <summary>Batch names not present in the known-tool set supplied to the gate.</summary>
    public required IReadOnlyList<string> UnknownToolNames { get; init; }
}

/// <summary>
/// M1 proof seam: an <see cref="IChatClient"/> decorator positioned BELOW
/// FunctionInvokingChatClient in the composed pipeline
/// (FICC → BatchGateChatClient → provider IChatClient). Because FICC already buffers a
/// complete provider round before invoking functions, placing the gate here lets it inspect
/// every FunctionCallContent of the batch BEFORE any invocation — per-call middleware
/// cannot see future siblings.
///
/// Streaming: the provider stream is buffered until the round completes, assembled via
/// <c>ToChatResponse()</c> (which coalesces fragmented tool-call argument deltas), filtered
/// by the policy, and re-emitted via <c>ToChatResponseUpdates()</c>. Non-streaming: the
/// complete <see cref="ChatResponse"/> is filtered directly.
///
/// M3 requirement recorded here, not solved in M1: stripping deferred calls keeps wire
/// history consistent (no dangling tool_use/tool_calls without results), but to ever
/// execute a deferred call the host must append synthetic FunctionResultContent for its
/// CallId before the next request, or re-request the tool in a fresh round.
/// </summary>
internal sealed class BatchGateChatClient : DelegatingChatClient
{
    private readonly IBatchPolicy _policy;
    private readonly HashSet<string> _knownTools;

    public BatchGateChatClient(IChatClient innerClient, IBatchPolicy policy, IEnumerable<string>? knownTools = null)
        : base(innerClient)
    {
        _policy = policy;
        _knownTools = new HashSet<string>(knownTools ?? [], StringComparer.Ordinal);
    }

    /// <summary>One entry per provider round that the gate inspected, in order.</summary>
    public List<BatchInspection> Inspections { get; } = new();

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        return Apply(response);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Buffer the whole round: batch gating needs every FunctionCallContent, and FICC
        // never dispatches mid-round anyway, so this adds no provider-visible behavior.
        var buffered = new List<ChatResponseUpdate>();
        await foreach (var update in base
                           .GetStreamingResponseAsync(messages, options, cancellationToken)
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            buffered.Add(update);
        }

        var filtered = Apply(buffered.ToChatResponse());
        foreach (var update in filtered.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    private ChatResponse Apply(ChatResponse response)
    {
        var calls = response.Messages
            .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
            .ToList();

        if (calls.Count == 0)
        {
            Inspections.Add(BatchInspection.NoCalls);
            return response;
        }

        var deferred = _policy.Evaluate(calls);
        var deferredIds = new HashSet<string>(deferred.Select(c => c.CallId), StringComparer.Ordinal);

        Inspections.Add(new BatchInspection
        {
            Calls = calls.Select(c => new InspectedCall(
                c.CallId ?? "",
                c.Name ?? "",
                c.Arguments is null ? "" : JsonSerializer.Serialize(c.Arguments))).ToList(),
            AllowedCallIds = calls.Where(c => !deferredIds.Contains(c.CallId ?? "")).Select(c => c.CallId ?? "").ToList(),
            DeferredCallIds = deferred.Select(c => c.CallId ?? "").ToList(),
            UnknownToolNames = calls
                .Where(c => c.Name is not null && !_knownTools.Contains(c.Name))
                .Select(c => c.Name!)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
        });

        if (deferredIds.Count == 0)
            return response;

        // Rebuild the response without the deferred calls so FICC never sees them.
        // Response metadata (ResponseId, FinishReason, Usage, ...) is preserved so the
        // re-emitted round still terminates correctly and usage accounting survives.
        var kept = response.Messages
            .Select(m => new ChatMessage(
                m.Role,
                m.Contents.Where(c => c is not FunctionCallContent f || !deferredIds.Contains(f.CallId ?? "")).ToList())
            {
                AuthorName = m.AuthorName,
                CreatedAt = m.CreatedAt,
                MessageId = m.MessageId,
                AdditionalProperties = m.AdditionalProperties,
                RawRepresentation = m.RawRepresentation,
            })
            .ToList();

        return new ChatResponse(kept)
        {
            ResponseId = response.ResponseId,
            ConversationId = response.ConversationId,
            ModelId = response.ModelId,
            CreatedAt = response.CreatedAt,
            FinishReason = response.FinishReason,
            Usage = response.Usage,
            ContinuationToken = response.ContinuationToken,
            AdditionalProperties = response.AdditionalProperties,
        };
    }
}

/// <summary>
/// Sample M1 policy mirroring the existing sibling-deferral contract
/// (PlanInMixedBatch_DefersEverySiblingTool / MultiplePlansInOneBatch_PresentsOnlyTheFirstValidPlan):
/// if the batch contains propose_plan, only the FIRST proposal is allowed through and every
/// sibling — including extra proposals — is deferred (stripped before FICC can dispatch it).
/// </summary>
internal sealed class ProposePlanSiblingDeferralPolicy : IBatchPolicy
{
    public const string PlanToolName = "propose_plan";

    public IReadOnlyList<FunctionCallContent> Evaluate(IReadOnlyList<FunctionCallContent> batch)
    {
        var firstPlan = batch.FirstOrDefault(
            c => string.Equals(c.Name, PlanToolName, StringComparison.Ordinal));
        if (firstPlan is null)
            return [];

        return batch.Where(c => !ReferenceEquals(c, firstPlan)).ToList();
    }
}
