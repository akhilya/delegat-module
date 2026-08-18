using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Ensures an external marketplace category is a mappable leaf (terminal) category
/// with a field schema when the adapter supports it.
/// </summary>
public static class ExternalCategoryEligibility
{
    public const int MaxNodesVisited = 5000;
    public const int MaxDepth = 50;

    /// <summary>
    /// Validate that <paramref name="externalCategoryId"/> is a leaf category suitable for
    /// mapping/import. When the adapter supports <see cref="MarketplaceCapabilities.GetCategoryFields"/>,
    /// also requires a non-empty field schema (meta keys like <c>_hasSizes</c> excluded).
    /// </summary>
    public static async Task<MappableExternalCategoryResult> EnsureMappableAsync(
        IMarketplaceAdapter adapter,
        MarketplaceConnection connection,
        string externalCategoryId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(externalCategoryId))
        {
            return MappableExternalCategoryResult.Fail("External category ID is required");
        }

        var id = externalCategoryId.Trim();
        var fields = new List<ExternalField>();
        var hasFieldCap = adapter.Capabilities.HasFlag(MarketplaceCapabilities.GetCategoryFields);

        if (hasFieldCap)
        {
            try
            {
                fields = await adapter.GetCategoryFieldsAsync(connection, id, ct);
            }
            catch (Exception ex)
            {
                return MappableExternalCategoryResult.Fail(
                    $"Failed to get category fields from the marketplace: {ex.Message}");
            }

            fields = fields.Where(f => f.Key != "_hasSizes").ToList();
            if (fields.Count > 0)
            {
                // Non-empty schema means a mappable leaf; skip walking the category tree.
                return MappableExternalCategoryResult.Ok(category: null, fields);
            }
        }

        var resolved = await ResolveCategoryAsync(adapter, connection, id, ct);
        if (resolved is { HasChildren: true })
        {
            return MappableExternalCategoryResult.Fail(
                "Selected marketplace category is not a leaf category. Choose a terminal category that accepts products and has fields.");
        }

        if (hasFieldCap)
        {
            return MappableExternalCategoryResult.Fail(
                "Selected marketplace category has no fields. Choose a terminal category that can be used for export.");
        }

        return MappableExternalCategoryResult.Ok(resolved, fields);
    }

    /// <summary>
    /// Breadth-first lookup of a category by id via <see cref="IMarketplaceAdapter.GetCategoriesAsync"/>.
    /// Returns null when the adapter cannot list categories or the id is not found within caps.
    /// </summary>
    public static async Task<ExternalCategory?> ResolveCategoryAsync(
        IMarketplaceAdapter adapter,
        MarketplaceConnection connection,
        string externalCategoryId,
        CancellationToken ct = default)
    {
        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.GetCategories))
            return null;

        var visitedParents = new HashSet<string?>(StringComparer.Ordinal);
        var queue = new Queue<(string? parentId, int depth)>();
        queue.Enqueue((null, 0));

        var nodesVisited = 0;

        while (queue.Count > 0)
        {
            var (parentId, depth) = queue.Dequeue();
            if (depth > MaxDepth)
                continue;

            if (!visitedParents.Add(parentId))
                continue;

            List<ExternalCategory> children;
            try
            {
                children = await adapter.GetCategoriesAsync(connection, parentId, searchQuery: null, ct);
            }
            catch
            {
                continue;
            }

            foreach (var cat in children)
            {
                if (++nodesVisited > MaxNodesVisited)
                    return null;

                if (string.Equals(cat.Id, externalCategoryId, StringComparison.Ordinal))
                    return cat;

                if (cat.HasChildren)
                    queue.Enqueue((cat.Id, depth + 1));
            }
        }

        return null;
    }
}

/// <summary>
/// Result of <see cref="ExternalCategoryEligibility.EnsureMappableAsync"/>.
/// </summary>
public sealed class MappableExternalCategoryResult
{
    public bool IsSuccess { get; private init; }
    public string? ErrorMessage { get; private init; }
    public ExternalCategory? Category { get; private init; }
    public IReadOnlyList<ExternalField> Fields { get; private init; } = Array.Empty<ExternalField>();

    public static MappableExternalCategoryResult Ok(
        ExternalCategory? category,
        IReadOnlyList<ExternalField> fields)
        => new()
        {
            IsSuccess = true,
            Category = category,
            Fields = fields,
        };

    public static MappableExternalCategoryResult Fail(string errorMessage)
        => new()
        {
            IsSuccess = false,
            ErrorMessage = errorMessage,
        };
}
