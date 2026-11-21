namespace Delegat.Services.Integration;

/// <summary>
/// Admission input for one durable integration command. Items and stages are
/// already prepared by the caller's preparation path (P4 recipes); the
/// coordinator owns admission, deduplication, ownership and lifecycle.
/// </summary>
public sealed record IntegrationAdmitRequest(
    string Kind,
    int StoreId,
    int AuthorUserId,
    int? AuthorApiKeyId,
    int? ConnectionId,
    string? RequestKey,
    string InputJson,
    string? Title,
    IReadOnlyList<IntegrationStageSeed> Stages);

/// <summary>Prepared stage as admitted; payloads are exact provider payloads.</summary>
public sealed record IntegrationStageSeed(
    string ItemKey,
    string StageKey,
    string Kind,
    int Position,
    string? PayloadJson = null,
    string? PayloadFileKey = null,
    IReadOnlyList<string>? DependsOn = null,
    string? BatchKey = null,
    bool IsEffectful = true);

/// <summary>Result of command admission.</summary>
public sealed record IntegrationAdmitResult(
    string CommandId,
    string OperationId,
    bool IsDuplicate);

/// <summary>
/// Raised when a request key is reused with a different body inside the same
/// scope. Maps to HTTP 409 integration_request_conflict at the API boundary.
/// </summary>
public sealed class IntegrationRequestConflictException(string message)
    : Exception(message);

/// <summary>Raised when V1 admission is disabled by configuration.</summary>
public sealed class IntegrationExecutionDisabledException(string message)
    : Exception(message);

/// <summary>Outcome kinds a dispatcher may report for one stage attempt.</summary>
public enum IntegrationDispatchOutcome
{
    /// <summary>Admission deferred before any effectful send was authorized; retry at NextCheckAt without consuming send budget.</summary>
    DeferredBeforeSend,

    /// <summary>Provider rejected before any effect.</summary>
    Rejected,

    /// <summary>Provider accepted; confirmation required later.</summary>
    Accepted,

    /// <summary>Still scheduled; check again at NextCheckAt.</summary>
    Pending,

    /// <summary>Effect confirmed applied.</summary>
    Applied,

    /// <summary>Part of the effect confirmed applied.</summary>
    PartiallyApplied,

    /// <summary>External effect ambiguous; reconcile before any replay.</summary>
    Unknown,

    /// <summary>Cancelled before a known external effect.</summary>
    Cancelled
}

/// <summary>Structured dispatch result; carries no secrets or raw bodies.</summary>
public sealed record IntegrationDispatchResult(
    IntegrationDispatchOutcome Outcome,
    string? ReceiptJson = null,
    DateTime? NextCheckAt = null,
    string? EvidenceJson = null,
    string? IssuesJson = null,
    string? ResponseSummary = null,
    bool Retryable = false);

/// <summary>
/// The seam between the durable executor and physical transport. P3 wires the
/// real SDK IMarketplaceTransport adapter; tests inject fakes. Dispatch is for
/// first sends; Confirm reconciles in-flight or accepted/unknown work and must
/// never repeat an effectful request.
/// </summary>
public interface IIntegrationStageDispatcher
{
    /// <summary>First physical dispatch of a prepared stage.</summary>
    Task<IntegrationDispatchResult> DispatchAsync(
        DispatchStageRequest request, CancellationToken ct);

    /// <summary>
    /// Confirmation/reconciliation for a stage that already has a committed
    /// attempt (lost response, accepted-not-confirmed, unknown effect).
    /// </summary>
    Task<IntegrationDispatchResult> ConfirmAsync(
        DispatchStageRequest request, CancellationToken ct);
}

/// <summary>
/// Dispatch input handed to the transport seam. RequestKey is the stable
/// per-stage idempotency key (command + stage identity): dispatch and the
/// later confirmation present the same key, so a lost response reconciles
/// the original request instead of a fresh one.
/// </summary>
public sealed record DispatchStageRequest(
    int CommandId,
    string CommandGuid,
    string Kind,
    int StageId,
    string ItemKey,
    string StageKey,
    string StageKind,
    string? PayloadJson,
    string? PayloadFileKey,
    string? ReceiptJson,
    int AttemptNumber,
    string RequestKey)
{
    internal IntegrationTransportAttempt? TransportAttempt { get; init; }
}

/// <summary>
/// The single instance switch for the V1 executor. Bound from the
/// IntegrationExecutionV1 configuration key; defaults off.
/// </summary>
public sealed class IntegrationExecutionOptions
{
    public bool Enabled { get; init; }
}

/// <summary>
/// Placeholder for stages without a bound adapter recipe. Reports Unknown so
/// an effect is never presumed, and never sends HTTP. Nothing admits commands
/// while IntegrationExecutionV1 is off, so this is unreachable in production.
/// </summary>
public sealed class NotConfiguredStageDispatcher : IIntegrationStageDispatcher
{
    public Task<IntegrationDispatchResult> DispatchAsync(
        DispatchStageRequest request, CancellationToken ct) =>
        Task.FromResult(new IntegrationDispatchResult(
            IntegrationDispatchOutcome.Unknown,
            ResponseSummary: "transport_not_configured"));

    public Task<IntegrationDispatchResult> ConfirmAsync(
        DispatchStageRequest request, CancellationToken ct) =>
        Task.FromResult(new IntegrationDispatchResult(
            IntegrationDispatchOutcome.Unknown,
            ResponseSummary: "transport_not_configured"));
}
