namespace PrintO.Models.Marketplace.DTOs;

/// <summary>
/// Request to create or update a marketplace category from a local category definition.
/// </summary>
public sealed record CreateCategoryRequest
{
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public string? ParentId { get; init; }
    public int? StoreId { get; init; }
    public int? LanguageId { get; init; }
    public int SortOrder { get; init; }
    public List<CreateCategoryField> Fields { get; init; } = new();
}

/// <summary>
/// Local category field to create on the marketplace.
/// </summary>
public sealed record CreateCategoryField
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public string Type { get; init; } = "string";
    public bool IsRequired { get; init; }
    public string? Description { get; init; }
    public string? Group { get; init; }
    public int SortOrder { get; init; }
    public List<ExternalFieldOption>? Options { get; init; }
}

/// <summary>
/// Result of category creation on the marketplace.
/// </summary>
public sealed record CreateCategoryResult(
    bool Success,
    string? ExternalCategoryId = null,
    string? ExternalCategoryName = null,
    List<ExternalField>? Fields = null,
    string? ErrorMessage = null,
    Dictionary<string, object>? Details = null);
