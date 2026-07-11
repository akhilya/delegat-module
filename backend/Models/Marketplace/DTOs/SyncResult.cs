namespace Delegat.Models.Marketplace.DTOs;

/// <summary>
/// Connection test result
/// </summary>
public record ConnectionTestResult(
    bool Success,
    string? Message = null,
    Dictionary<string, object>? Details = null
);

/// <summary>
/// Data import result
/// </summary>
public record ImportResult(
    int TotalCount,
    int SuccessCount,
    int FailedCount,
    List<ImportError>? Errors = null
)
{
    public bool IsSuccess => FailedCount == 0;
    public bool IsPartialSuccess => SuccessCount > 0 && FailedCount > 0;
}

/// <summary>
/// Import error
/// </summary>
public record ImportError(
    string? ExternalId,
    string Message,
    string? Details = null
);

/// <summary>
/// Single product export result
/// </summary>
public record ExportResult(
    bool Success,
    string? ExternalProductId = null,
    string? ErrorMessage = null,
    Dictionary<string, object>? Details = null
);

/// <summary>
/// Bulk export result
/// </summary>
public record BulkExportResult(
    int TotalCount,
    int SuccessCount,
    int FailedCount,
    List<ExportItemResult>? Items = null
)
{
    public bool IsSuccess => FailedCount == 0;
}

/// <summary>
/// Single item result in a bulk export operation
/// </summary>
public record ExportItemResult(
    int LocalProductId,
    bool Success,
    string? ExternalProductId = null,
    string? ErrorMessage = null,
    Dictionary<string, object>? Details = null
);

/// <summary>
/// Input item for bulk export
/// </summary>
public record BulkExportItem(
    int LocalProductId,
    Dictionary<string, object> ProductData,
    string? ExistingExternalId = null
);

/// <summary>
/// Paginated query result
/// </summary>
/// <typeparam name="T">Type of items</typeparam>
public record PagedResult<T>(
    List<T> Items,
    bool HasMore,
    string? NextCursor = null,
    int? TotalCount = null
);
