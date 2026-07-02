namespace Delegat.Models.Marketplace.DTOs;

/// <summary>
/// Category from an external marketplace
/// </summary>
public record ExternalCategory(
    /// <summary>
    /// Category identifier on the marketplace
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
    /// Full category path (e.g. "Clothing / Men's / T-Shirts")
    /// </summary>
    string? FullPath = null,

    /// <summary>
    /// Whether the category has child categories
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
