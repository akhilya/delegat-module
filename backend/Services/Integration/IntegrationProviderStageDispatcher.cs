using System.Text.Json;
using Delegat.Marketplace.SDK;

namespace Delegat.Services.Integration;

internal sealed record IntegrationExecutionBinding(
    IMarketplaceExecutionProvider Provider,
    ConnectionContext Connection,
    IntegrationHostRouteBinding RouteBinding);

internal interface IIntegrationExecutionBindingResolver
{
    Task<IntegrationExecutionBinding?> ResolveAsync(DispatchStageRequest request, CancellationToken ct);
}

/// <summary>
/// Host stage dispatcher that bridges <see cref="IntegrationExecutionWorker"/> to
/// an adapter's <see cref="IMarketplaceExecutionProvider"/> using the shared
/// <see cref="IntegrationHttpTransport"/> and <see cref="IntegrationRoutePolicyCatalog"/>.
/// Fails closed (Unknown, zero HTTP) when no execution provider is bound.
/// </summary>
internal sealed class IntegrationProviderStageDispatcher(
    IntegrationHttpTransport transport,
    IIntegrationExecutionBindingResolver resolver) : IIntegrationStageDispatcher
{
    public Task<IntegrationDispatchResult> DispatchAsync(DispatchStageRequest request, CancellationToken ct) =>
        ExecuteAsync(request, isDispatch: true, ct);

    public Task<IntegrationDispatchResult> ConfirmAsync(DispatchStageRequest request, CancellationToken ct) =>
        ExecuteAsync(request, isDispatch: false, ct);

    private async Task<IntegrationDispatchResult> ExecuteAsync(
        DispatchStageRequest request, bool isDispatch, CancellationToken ct)
    {
        if (request.TransportAttempt is not { } transportAttempt)
        {
            return new IntegrationDispatchResult(
                IntegrationDispatchOutcome.Unknown,
                ResponseSummary: "transport_attempt_missing");
        }

        var binding = await resolver.ResolveAsync(request, ct);
        if (binding is null)
        {
            return new IntegrationDispatchResult(
                IntegrationDispatchOutcome.Unknown,
                ResponseSummary: "provider_not_execution_capable");
        }

        var boundAttempt = transportAttempt with
        {
            AdapterKey = string.IsNullOrWhiteSpace(binding.Connection.AdapterKey)
                ? transportAttempt.AdapterKey
                : binding.Connection.AdapterKey
        };

        var attemptCtx = new IntegrationAttemptContext(
            OperationId: boundAttempt.OperationId ?? request.CommandGuid,
            CommandId: request.CommandGuid,
            ItemKey: request.ItemKey,
            StageKey: request.StageKey,
            AttemptNumber: request.AttemptNumber,
            RequestKey: request.RequestKey);

        binding.Connection.ExecutionContext = attemptCtx;
        binding.Connection.Transport = transport.Bind(
            boundAttempt,
            req => IntegrationRoutePolicyCatalog.Resolve(binding.RouteBinding, req));

        var stageKind = Enum.TryParse<IntegrationStageKind>(request.StageKind, ignoreCase: true, out var parsed)
            ? parsed
            : IntegrationStageKind.PrimaryObject;

        var preparedStage = new IntegrationPreparedStage(
            StageKey: request.StageKey,
            Kind: stageKind,
            RequestKey: request.RequestKey,
            PayloadJson: request.PayloadJson ?? "{}",
            DependsOn: [],
            IsEffectful: boundAttempt.AllowEffects);

        IntegrationStageOutcome outcome;
        if (isDispatch)
        {
            outcome = await binding.Provider.ExecuteStageAsync(
                new IntegrationStageRequest(binding.Connection, attemptCtx, preparedStage), ct);
        }
        else
        {
            IntegrationProviderReceipt? receipt = null;
            if (!string.IsNullOrWhiteSpace(request.ReceiptJson))
            {
                try
                {
                    receipt = JsonSerializer.Deserialize<IntegrationProviderReceipt>(request.ReceiptJson);
                }
                catch (JsonException)
                {
                    receipt = new IntegrationProviderReceipt("raw", request.ReceiptJson);
                }
            }
            outcome = await binding.Provider.ConfirmAsync(
                new IntegrationConfirmationRequest(binding.Connection, attemptCtx, preparedStage, receipt), ct);
        }

        return MapOutcome(outcome);
    }

    private static IntegrationDispatchResult MapOutcome(IntegrationStageOutcome outcome)
    {
        var mapped = outcome.Kind switch
        {
            IntegrationStageOutcomeKind.Rejected => IntegrationDispatchOutcome.Rejected,
            IntegrationStageOutcomeKind.Accepted => IntegrationDispatchOutcome.Accepted,
            IntegrationStageOutcomeKind.Pending => IntegrationDispatchOutcome.Pending,
            IntegrationStageOutcomeKind.Applied => IntegrationDispatchOutcome.Applied,
            IntegrationStageOutcomeKind.PartiallyApplied => IntegrationDispatchOutcome.PartiallyApplied,
            IntegrationStageOutcomeKind.Cancelled or IntegrationStageOutcomeKind.NotApplicable => IntegrationDispatchOutcome.Cancelled,
            _ => IntegrationDispatchOutcome.Unknown
        };

        var retryable = outcome.Issues is { Count: > 0 } && outcome.Issues.All(i => i.Retryable);
        return new IntegrationDispatchResult(
            Outcome: mapped,
            ReceiptJson: outcome.Receipt is null ? null : JsonSerializer.Serialize(outcome.Receipt),
            NextCheckAt: outcome.NextCheckAt?.UtcDateTime,
            EvidenceJson: outcome.AppliedEvidence is null ? null : JsonSerializer.Serialize(outcome.AppliedEvidence),
            IssuesJson: outcome.Issues is null ? null : JsonSerializer.Serialize(outcome.Issues),
            ResponseSummary: outcome.Kind.ToString().ToLowerInvariant(),
            Retryable: retryable);
    }
}
