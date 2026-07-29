using System.Text.Json.Serialization;

namespace Delegat.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProductBulkUpdateStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4,
    Partial = 5
}

/// <summary>
/// Queued background job for applying the same field/attribute updates to many products.
/// One row = one operation (product IDs + updates payload stored as JSON).
/// </summary>
public class PendingProductBulkUpdate
{
    public int Id { get; set; }

    public int storeId { get; set; }

    public int authorUserId { get; set; }

    /// <summary>Same id as Operation.operationId / OperationLogger batch.</summary>
    public string batchId { get; set; } = string.Empty;

    /// <summary>JSON array of product ids, e.g. [1,2,3].</summary>
    public string productIdsJson { get; set; } = "[]";

    /// <summary>JSON of <see cref="Services.ProductBulkUpdateFields"/>.</summary>
    public string updatesJson { get; set; } = "{}";

    public ProductBulkUpdateStatus status { get; set; } = ProductBulkUpdateStatus.Pending;

    public string? lastError { get; set; }

    public int completedCount { get; set; }

    public int failedCount { get; set; }

    public DateTime createdAt { get; set; } = DateTime.UtcNow;
    public DateTime? startedAt { get; set; }
    public DateTime? finishedAt { get; set; }
}
