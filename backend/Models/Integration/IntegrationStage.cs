namespace Delegat.Models.Integration;

/// <summary>
/// One prepared provider stage inside an <see cref="IntegrationCommand"/> item.
/// State transitions and effect evidence are authoritative for recovery.
/// </summary>
public class IntegrationStage
{
    public int Id { get; set; }

    public int commandId { get; set; }
    public IntegrationCommand command { get; set; } = null!;

    /// <summary>Stable business item identity inside the command.</summary>
    public string itemKey { get; set; } = null!;

    /// <summary>Stable stage identity inside the item (unique per command).</summary>
    public string stageKey { get; set; } = null!;

    /// <summary>Semantic stage category (see SDK IntegrationStageKind names).</summary>
    public string kind { get; set; } = null!;

    /// <summary>Ordering position inside the item for deterministic evaluation.</summary>
    public int position { get; set; }

    /// <summary>Exact serialized provider payload for this stage.</summary>
    public string? payloadJson { get; set; }

    /// <summary>Reference to an immutable file payload instead of inline JSON.</summary>
    public string? payloadFileKey { get; set; }

    /// <summary>JSON array of stageKeys that must reach applied state first.</summary>
    public string dependsOnJson { get; set; } = "[]";

    /// <summary>Stages with the same batch key may be combined in one provider call.</summary>
    public string? batchKey { get; set; }

    /// <summary>Whether dispatch can change provider state.</summary>
    public bool isEffectful { get; set; } = true;

    /// <summary>
    /// ready | inflight | accepted | applied | partiallyapplied | deferred |
    /// rejected | unknown | cancelled
    /// </summary>
    public string state { get; set; } = IntegrationStageState.Ready;

    /// <summary>Provider receipt JSON (kind/value/expiresAt) persisted by accepted stages.</summary>
    public string? receiptJson { get; set; }

    /// <summary>Adapter/provider evidence proving the final effect.</summary>
    public string? evidenceJson { get; set; }

    /// <summary>Structured issue list (IntegrationIssue JSON) from the last outcome.</summary>
    public string? issuesJson { get; set; }

    /// <summary>Next time this stage becomes eligible (deferred wait, poll time, retry backoff).</summary>
    public DateTime? nextCheckAt { get; set; }

    /// <summary>Dispatch/confirmation attempts made for this stage.</summary>
    public int attemptCount { get; set; }

    /// <summary>Optimistic claim version; incremented on every lease change.</summary>
    public int claimVersion { get; set; }
    public string? claimedBy { get; set; }
    public DateTime? claimedUntil { get; set; }

    public DateTime createdAt { get; set; } = DateTime.UtcNow;
    public DateTime updatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? finishedAt { get; set; }
}

/// <summary>
/// Fixed stage state vocabulary from the implementation plan. Accepted and
/// Unknown are not successful completion; Cancelled/Rejected are
/// before-effect states only.
/// </summary>
public static class IntegrationStageState
{
    /// <summary>Prepared and eligible for dispatch once dependencies applied.</summary>
    public const string Ready = "ready";

    /// <summary>A committed attempt exists; response not yet recorded.</summary>
    public const string InFlight = "inflight";

    /// <summary>Provider accepted the effect; confirmation required.</summary>
    public const string Accepted = "accepted";

    /// <summary>The requested effect is confirmed applied.</summary>
    public const string Applied = "applied";

    /// <summary>Part of the requested effect is confirmed applied.</summary>
    public const string PartiallyApplied = "partiallyapplied";

    /// <summary>Persisted wait; eligible again at nextCheckAt.</summary>
    public const string Deferred = "deferred";

    /// <summary>Provider rejected the stage before applying any effect.</summary>
    public const string RejectedBeforeEffect = "rejected";

    /// <summary>External effect is ambiguous and requires reconciliation.</summary>
    public const string Unknown = "unknown";

    /// <summary>Cancelled before a known external effect.</summary>
    public const string CancelledBeforeEffect = "cancelled";

    public static bool IsTerminal(string? state) =>
        state is Applied or PartiallyApplied or RejectedBeforeEffect
            or Unknown or CancelledBeforeEffect;

    /// <summary>Terminal states that satisfy a dependency edge.</summary>
    public static bool SatisfiesDependency(string? state) =>
        state is Applied or PartiallyApplied;
}
