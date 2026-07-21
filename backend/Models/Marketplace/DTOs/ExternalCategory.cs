namespace Delegat.Models.Marketplace.DTOs;

/// <summary>
/// Category from an external marketplace.
/// </summary>
/// <param name="Id">Category identifier on the marketplace.</param>
/// <param name="Name">Category name.</param>
/// <param name="ParentId">Parent category ID (null for root categories).</param>
/// <param name="ParentName">Parent category name.</param>
/// <param name="FullPath">Full category path (e.g. "Clothing / Men's / T-Shirts").</param>
/// <param name="HasChildren">Whether the category has child categories.</param>
/// <param name="ChildrenCount">Number of child categories.</param>
/// <param name="HasSizes">Hint from plugin: whether this category supports sizes (e.g. clothing).</param>
public record ExternalCategory(
    string Id,
    string Name,
    string? ParentId = null,
    string? ParentName = null,
    string? FullPath = null,
    bool HasChildren = false,
    int? ChildrenCount = null,
    bool? HasSizes = null
);
