namespace Delegat.Models;

public enum CatalogConnectJobKind
{
    Import = 0,
    Match = 1,
    Apply = 2,
}

public enum CatalogConnectJobStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4,
}

/// <summary>
/// Background Catalog Connect scan (import list or match preview) or apply.
/// Survives SPA navigation and API request abort.
/// </summary>
public class PendingCatalogConnectJob
{
    public const int OPERATION_ID_MAX_LENGTH = 64;
    public const int MODE_MAX_LENGTH = 16;

    public int Id { get; set; }

    public int storeId { get; set; }

    public int connectionId { get; set; }

    public int authorUserId { get; set; }

    public string operationId { get; set; } = string.Empty;

    public CatalogConnectJobKind kind { get; set; }

    public CatalogConnectJobStatus status { get; set; } = CatalogConnectJobStatus.Pending;

    /// <summary>fromLocal / fromRemote when known.</summary>
    public string? mode { get; set; }

    public int totalItems { get; set; }

    public int processedItems { get; set; }

    public int queuedCount { get; set; }

    /// <summary>Apply payload JSON.</summary>
    public string? payloadJson { get; set; }

    /// <summary>Import/match/apply result JSON.</summary>
    public string? resultJson { get; set; }

    public string? lastError { get; set; }

    public DateTime createdAt { get; set; } = DateTime.UtcNow;

    public DateTime? lastAttemptAt { get; set; }

    public DateTime? completedAt { get; set; }
}
