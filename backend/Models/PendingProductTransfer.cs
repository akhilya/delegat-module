using System.Text.Json.Serialization;
using PrintO.Models.Products;

namespace PrintO.Models;

/// <summary>
/// Status of a product transfer (clone or move) operation.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProductTransferStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4,
    Partial = 5
}

/// <summary>
/// Queued background task for cloning or moving a product into another store.
/// Mirrors the TransferRequest fields plus runtime state (status, result, error).
/// </summary>
public class PendingProductTransfer
{
    /// <summary>Unique record identifier.</summary>
    public int Id { get; set; }

    /// <summary>Source product to clone/move.</summary>
    public int sourceProductId { get; set; }

    /// <summary>Source store (the product's current store at enqueue time).</summary>
    public int sourceStoreId { get; set; }

    /// <summary>Target store (where the new product will land).</summary>
    public int targetStoreId { get; set; }

    /// <summary>"clone" or "move".</summary>
    public string mode { get; set; } = "clone";

    /// <summary>Optional SKU override (used when target store has a collision).</summary>
    public string? newSku { get; set; }

    /// <summary>"match" | "use" | "create" | "none".</summary>
    public string categoryAction { get; set; } = "match";
    public int? categoryId { get; set; }

    /// <summary>"none" | "use" | "create".</summary>
    public string groupAction { get; set; } = "none";
    public int? groupId { get; set; }
    public string? groupName { get; set; }

    /// <summary>Snapshot of the user's "yes I understand links will break" flag.</summary>
    public bool confirmBreakLinks { get; set; }

    /// <summary>When true, missing attribute definitions are added to the existing target category.</summary>
    public bool syncMissingCategoryAttributes { get; set; }

    /// <summary>User who initiated the transfer.</summary>
    public int authorUserId { get; set; }

    /// <summary>Operation/batch identifier — same value used by OperationLogger so the panel surfaces this transfer.</summary>
    public string batchId { get; set; } = string.Empty;

    /// <summary>Current execution status.</summary>
    public ProductTransferStatus status { get; set; } = ProductTransferStatus.Pending;

    /// <summary>Last error message if status is Failed/Partial.</summary>
    public string? lastError { get; set; }

    /// <summary>Set on success — the newly created product's id.</summary>
    public int? newProductId { get; set; }

    /// <summary>Navigation to created product (if any).</summary>
    public Product? newProduct { get; set; }

    public DateTime createdAt { get; set; } = DateTime.UtcNow;
    public DateTime? startedAt { get; set; }
    public DateTime? finishedAt { get; set; }
}
