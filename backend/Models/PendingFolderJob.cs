namespace Delegat.Models;

public enum FolderJobStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}

public enum FolderJobType
{
    Archive = 0,
    Delete = 1
}

public enum FolderJobScope
{
    Product = 0,
    Store = 1
}

/// <summary>
/// Queued background folder archive or recursive delete job.
/// </summary>
public class PendingFolderJob
{
    public int Id { get; set; }

    public int storeId { get; set; }

    public int authorUserId { get; set; }

    /// <summary>Matches Operation.operationId / batch id.</summary>
    public string operationId { get; set; } = string.Empty;

    public FolderJobType jobType { get; set; }

    public FolderJobScope scope { get; set; }

    public int? productId { get; set; }

    /// <summary>Null means product/store root scope.</summary>
    public int? folderId { get; set; }

    public FolderJobStatus status { get; set; } = FolderJobStatus.Pending;

    public string? lastError { get; set; }

    public DateTime createdAt { get; set; } = DateTime.UtcNow;

    public DateTime? lastAttemptAt { get; set; }

    public DateTime? completedAt { get; set; }

    /// <summary>MinIO object key for completed archive ZIP.</summary>
    public string? resultObjectKey { get; set; }

    public string? resultFileName { get; set; }

    public long? resultBytes { get; set; }

    public int totalItems { get; set; }

    public int processedItems { get; set; }
}
