namespace Delegat.Models.Integration;

/// <summary>
/// Authoritative durable command for one user integration intent. The
/// <see cref="Operation"/> row it projects to is UI history only - command
/// evidence and idempotency live here.
/// </summary>
public class IntegrationCommand
{
    public int Id { get; set; }

    /// <summary>Authoritative command identity (unique).</summary>
    public string commandId { get; set; } = null!;

    /// <summary>Logical operation identity projected to <see cref="Models.Operation"/>.</summary>
    public string operationId { get; set; } = null!;

    public int storeId { get; set; }
    public int authorUserId { get; set; }
    public int? authorApiKeyId { get; set; }
    public int? connectionId { get; set; }

    /// <summary>
    /// Deduplication scope (store, connection/account, operation kind). The
    /// request key is unique inside this scope.
    /// </summary>
    public string scopeKey { get; set; } = null!;

    /// <summary>Business operation kind (see SDK IntegrationOperationKind names).</summary>
    public string kind { get; set; } = null!;

    /// <summary>Client-supplied idempotency key; null for legacy callers without one.</summary>
    public string? requestKey { get; set; }

    /// <summary>SHA-256 of the normalized input used for key/body conflict checks.</summary>
    public string requestHash { get; set; } = null!;

    /// <summary>Immutable normalized input snapshot; never credentials or media bytes.</summary>
    public string inputJson { get; set; } = null!;

    /// <summary>Version of the prepared input (edits create a new version).</summary>
    public int inputVersion { get; set; } = 1;

    /// <summary>Executor recipe generation pinned at admission.</summary>
    public int recipeVersion { get; set; } = 1;

    /// <summary>admitted | executing | completed | partial | failed | cancelled | unknown.</summary>
    public string state { get; set; } = IntegrationCommandState.Admitted;

    /// <summary>User stop flag: prevents new writes, not in-flight confirmation.</summary>
    public bool stopRequested { get; set; }

    public string? title { get; set; }
    public string? summary { get; set; }

    public DateTime createdAt { get; set; } = DateTime.UtcNow;
    public DateTime updatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? finishedAt { get; set; }
}

public static class IntegrationCommandState
{
    public const string Admitted = "admitted";
    public const string Executing = "executing";
    public const string Completed = "completed";
    public const string Partial = "partial";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";

    /// <summary>Terminal state: at least one effect is ambiguous and requires reconciliation.</summary>
    public const string Unknown = "unknown";

    public static bool IsTerminal(string? state) =>
        state is Completed or Partial or Failed or Cancelled or Unknown;
}
