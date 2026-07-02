using System.Text.Json.Serialization;

namespace Delegat.Models;

/// <summary>
/// Status of a product export operation.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProductExportStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}

/// <summary>
/// Represents a queued background product export task to a marketplace.
/// </summary>
public class PendingProductExport
{
    /// <summary>
    /// Unique record identifier.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Marketplace connection identifier.
    /// </summary>
    public int connectionId { get; set; }

    /// <summary>
    /// Local product identifier.
    /// </summary>
    public int productId { get; set; }

    /// <summary>
    /// Product name cached at creation time for display purposes.
    /// </summary>
    public string? productName { get; set; }

    /// <summary>
    /// Identifier of the user who initiated the export.
    /// </summary>
    public int authorUserId { get; set; }

    /// <summary>
    /// Store identifier.
    /// </summary>
    public int storeId { get; set; }

    /// <summary>
    /// Current export status.
    /// </summary>
    public ProductExportStatus status { get; set; } = ProductExportStatus.Pending;

    /// <summary>
    /// External product identifier on the marketplace (set after successful export).
    /// </summary>
    public string? externalProductId { get; set; }

    /// <summary>
    /// Product-marketplace link identifier (set after successful export).
    /// </summary>
    public int? linkId { get; set; }

    /// <summary>
    /// Last error message if the export failed.
    /// </summary>
    public string? lastError { get; set; }

    /// <summary>
    /// Number of export attempts made.
    /// </summary>
    public int retryCount { get; set; } = 0;

    /// <summary>
    /// Timestamp when the record was created.
    /// </summary>
    public DateTime createdAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp of the last export attempt.
    /// </summary>
    public DateTime? lastAttemptAt { get; set; }

    /// <summary>
    /// Timestamp when the export completed successfully.
    /// </summary>
    public DateTime? completedAt { get; set; }

    /// <summary>
    /// Batch identifier for grouping related exports.
    /// </summary>
    public string? batchId { get; set; }

    /// <summary>
    /// Maximum number of retry attempts allowed.
    /// </summary>
    public const int MAX_RETRIES = 3;
}
