using System.Text.Json.Serialization;
using Delegat.Models.Products;

namespace Delegat.Models;

/// <summary>
/// Status of a product import operation.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProductImportStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}

/// <summary>
/// Represents a queued background product import task from a marketplace.
/// </summary>
public class PendingProductImport
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
    /// External product identifier on the marketplace.
    /// </summary>
    public string externalProductId { get; set; } = string.Empty;

    /// <summary>
    /// Optional target category for the imported product.
    /// </summary>
    public int? targetCategoryId { get; set; }

    /// <summary>
    /// Identifier of the user who initiated the import.
    /// </summary>
    public int authorUserId { get; set; }

    /// <summary>
    /// Store identifier.
    /// </summary>
    public int storeId { get; set; }

    /// <summary>
    /// Current import status.
    /// </summary>
    public ProductImportStatus status { get; set; } = ProductImportStatus.Pending;

    /// <summary>
    /// Identifier of the created product (set after successful import).
    /// </summary>
    public int? importedProductId { get; set; }

    /// <summary>
    /// Navigation property to the imported product.
    /// </summary>
    public Product? importedProduct { get; set; }

    /// <summary>
    /// Last error message if the import failed.
    /// </summary>
    public string? lastError { get; set; }

    /// <summary>
    /// Number of import attempts made.
    /// </summary>
    public int retryCount { get; set; } = 0;

    /// <summary>
    /// Timestamp when the record was created.
    /// </summary>
    public DateTime createdAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp of the last import attempt.
    /// </summary>
    public DateTime? lastAttemptAt { get; set; }

    /// <summary>
    /// Timestamp when the import completed successfully.
    /// </summary>
    public DateTime? completedAt { get; set; }

    /// <summary>
    /// Batch identifier for grouping related imports.
    /// </summary>
    public string? batchId { get; set; }

    /// <summary>
    /// Product name cached at creation time for display purposes.
    /// </summary>
    public string? productName { get; set; }

    /// <summary>
    /// Maximum number of retry attempts allowed.
    /// </summary>
    public const int MAX_RETRIES = 3;
}
