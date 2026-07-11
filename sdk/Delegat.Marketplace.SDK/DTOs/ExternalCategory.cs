namespace Delegat.Marketplace.SDK.DTOs;

/// <summary>
/// Category from external marketplace
/// </summary>
public record ExternalCategory(
    /// <summary>
    /// Category ID on the marketplace
    /// </summary>
    string Id,

    /// <summary>
    /// Category name
    /// </summary>
    string Name,

    /// <summary>
    /// Parent category ID (null for root categories)
    /// </summary>
    string? ParentId = null,

    /// <summary>
    /// Parent category name
    /// </summary>
    string? ParentName = null,

    /// <summary>
    /// Full path (e.g. "Clothing / Men / T-shirts")
    /// </summary>
    string? FullPath = null,

    /// <summary>
    /// Has child categories
    /// </summary>
    bool HasChildren = false,

    /// <summary>
    /// Number of child categories
    /// </summary>
    int? ChildrenCount = null,

    /// <summary>
    /// Hint from plugin: whether this category supports sizes (e.g. clothing).
    /// </summary>
    bool? HasSizes = null
);
