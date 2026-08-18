using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Delegat.Models.Marketplace;

namespace Delegat.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CategoryImportStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}

/// <summary>
/// Queued background import of a marketplace category (local category + attributes + mapping).
/// </summary>
public class PendingCategoryImport
{
    public int Id { get; set; }

    public int connectionId { get; set; }

    public int storeId { get; set; }

    public int authorUserId { get; set; }

    /// <summary>Matches <see cref="Operation.operationId"/> (standalone or product batch id).</summary>
    [StringLength(OPERATION_ID_MAX_LENGTH)]
    public string operationId { get; set; } = string.Empty;

    [StringLength(CategoryMapping.EXTERNAL_CATEGORY_ID_MAX_LENGTH, MinimumLength = 1)]
    public string externalCategoryId { get; set; } = string.Empty;

    [StringLength(CategoryMapping.EXTERNAL_CATEGORY_NAME_MAX_LENGTH)]
    public string? externalCategoryName { get; set; }

    [StringLength(Category.CATEGORY_NAME_MAX_LENGTH)]
    public string? localCategoryName { get; set; }

    public int? localCategoryId { get; set; }

    /// <summary>JSON array of marketplace field keys, or null for all fields.</summary>
    public string? selectedFieldKeysJson { get; set; }

    public CategoryImportStatus status { get; set; } = CategoryImportStatus.Pending;

    public string? lastError { get; set; }

    public int retryCount { get; set; }

    public DateTime createdAt { get; set; } = DateTime.UtcNow;

    public DateTime? lastAttemptAt { get; set; }

    public DateTime? completedAt { get; set; }

    public int? resultLocalCategoryId { get; set; }

    public int? resultMappingId { get; set; }

    public const int MAX_RETRIES = 3;
    public const int OPERATION_ID_MAX_LENGTH = 64;
}
