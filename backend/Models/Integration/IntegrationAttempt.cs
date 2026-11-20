namespace Delegat.Models.Integration;

/// <summary>
/// Durable attempt evidence committed <b>before</b> physical dispatch. A
/// missing outcome never means "not sent" - recovery reconciles through the
/// confirmation path first.
/// </summary>
public class IntegrationAttempt
{
    public int Id { get; set; }

    public int commandId { get; set; }
    public int stageId { get; set; }

    /// <summary>Denormalized for diagnostics without joining the stage.</summary>
    public string stageKey { get; set; } = null!;

    /// <summary>Provider batch/dispatch identity; attempts sharing it were sent together.</summary>
    public string dispatchId { get; set; } = null!;

    /// <summary>One-based attempt number for the stage.</summary>
    public int attemptNumber { get; set; }

    /// <summary>Unique request/receipt key of this dispatch (idempotency).</summary>
    public string requestKey { get; set; } = null!;

    /// <summary>Hash of the exact payload sent.</summary>
    public string requestHash { get; set; } = null!;

    /// <summary>
    /// Recorded outcome kind, or null while the dispatch result is not yet
    /// recorded (crash or lost response). Never treat null as "not sent".
    /// </summary>
    public string? outcome { get; set; }

    /// <summary>none | possible | applied - what the provider may have done.</summary>
    public string? effectCertainty { get; set; }

    /// <summary>Sanitized response metadata (status code, receipt refs). No secrets or bodies.</summary>
    public string? responseSummary { get; set; }

    /// <summary>Structured issues reported for this attempt.</summary>
    public string? issuesJson { get; set; }

    public DateTime createdAt { get; set; } = DateTime.UtcNow;
    public DateTime? completedAt { get; set; }
}

public static class IntegrationAttemptOutcome
{
    public const string Rejected = "rejected";
    public const string Accepted = "accepted";
    public const string Pending = "pending";
    public const string Applied = "applied";
    public const string PartiallyApplied = "partiallyapplied";
    public const string Unknown = "unknown";
    public const string Cancelled = "cancelled";
}

public static class IntegrationEffectCertainty
{
    public const string None = "none";
    public const string Possible = "possible";
    public const string Applied = "applied";
}
