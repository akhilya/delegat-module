namespace Delegat.Marketplace.SDK.DTOs;

/// <summary>
/// Connection test result
/// </summary>
public record ConnectionTestResult(
    /// <summary>
    /// Whether the connection test succeeded.
    /// </summary>
    bool Success,

    /// <summary>
    /// Human-readable result message.
    /// </summary>
    string? Message = null,

    /// <summary>
    /// Additional details about the connection test.
    /// </summary>
    Dictionary<string, object>? Details = null
);

/// <summary>
/// Import operation result
/// </summary>
public record ImportResult(
    /// <summary>
    /// Total number of items processed.
    /// </summary>
    int TotalCount,

    /// <summary>
    /// Number of successfully imported items.
    /// </summary>
    int SuccessCount,

    /// <summary>
    /// Number of items that failed to import.
    /// </summary>
    int FailedCount,

    /// <summary>
    /// List of errors for failed items.
    /// </summary>
    List<ImportError>? Errors = null
)
{
    /// <summary>
    /// True if all items were imported successfully.
    /// </summary>
    public bool IsSuccess => FailedCount == 0;

    /// <summary>
    /// True if some items succeeded and some failed.
    /// </summary>
    public bool IsPartialSuccess => SuccessCount > 0 && FailedCount > 0;
}

/// <summary>
/// Import error
/// </summary>
public record ImportError(
    /// <summary>
    /// External product ID that caused the error.
    /// </summary>
    string? ExternalId,

    /// <summary>
    /// Error message.
    /// </summary>
    string Message,

    /// <summary>
    /// Additional error details.
    /// </summary>
    string? Details = null
);

/// <summary>
/// Single product export result
/// </summary>
public record ExportResult(
    /// <summary>
    /// Whether the export operation succeeded.
    /// </summary>
    bool Success,

    /// <summary>
    /// External product ID assigned by the marketplace.
    /// </summary>
    string? ExternalProductId = null,

    /// <summary>
    /// Error message if the export failed.
    /// </summary>
    string? ErrorMessage = null,

    /// <summary>
    /// Additional details about the export result.
    /// </summary>
    Dictionary<string, object>? Details = null
);

/// <summary>
/// Bulk export result
/// </summary>
public record BulkExportResult(
    /// <summary>
    /// Total number of items in the bulk export.
    /// </summary>
    int TotalCount,

    /// <summary>
    /// Number of successfully exported items.
    /// </summary>
    int SuccessCount,

    /// <summary>
    /// Number of items that failed to export.
    /// </summary>
    int FailedCount,

    /// <summary>
    /// Per-item export results.
    /// </summary>
    List<ExportItemResult>? Items = null
)
{
    /// <summary>
    /// True if all items were exported successfully.
    /// </summary>
    public bool IsSuccess => FailedCount == 0;
}

/// <summary>
/// Single item result in bulk export
/// </summary>
public record ExportItemResult(
    /// <summary>
    /// Local product ID in the system.
    /// </summary>
    int LocalProductId,

    /// <summary>
    /// Whether this item was exported successfully.
    /// </summary>
    bool Success,

    /// <summary>
    /// External product ID assigned by the marketplace.
    /// </summary>
    string? ExternalProductId = null,

    /// <summary>
    /// Error message if the export failed.
    /// </summary>
    string? ErrorMessage = null,

    /// <summary>
    /// Additional details about this item export.
    /// </summary>
    Dictionary<string, object>? Details = null
);

/// <summary>
/// Input item for bulk export
/// </summary>
public record BulkExportItem(
    /// <summary>
    /// Local product ID in the system.
    /// </summary>
    int LocalProductId,

    /// <summary>
    /// Product data according to field mapping.
    /// </summary>
    Dictionary<string, object> ProductData,

    /// <summary>
    /// Existing external product ID (null to create new).
    /// </summary>
    string? ExistingExternalId = null
);

/// <summary>
/// Paginated result
/// </summary>
public record PagedResult<T>(
    /// <summary>
    /// Items on the current page.
    /// </summary>
    List<T> Items,

    /// <summary>
    /// Whether more pages are available.
    /// </summary>
    bool HasMore,

    /// <summary>
    /// Cursor for fetching the next page.
    /// </summary>
    string? NextCursor = null,

    /// <summary>
    /// Total number of items across all pages, if known.
    /// </summary>
    int? TotalCount = null
);
