using Delegat.Models.Products;

namespace Delegat.Infrastructure;

/// <summary>
/// Shared catalog product text search (list, trash, etc.).
/// </summary>
public static class ProductSearchHelper
{
    /// <summary>
    /// Filters products: name is substring; SKU / barcode / id / marketplace &amp; variant codes are exact equality only
    /// (<c>_ПК105</c> ≠ <c>ПК105</c>). Exact hits are still included and ranked first via
    /// <see cref="OrderBySearchRelevance"/>.
    /// </summary>
    public static IQueryable<Product> WhereMatchesSearch(this IQueryable<Product> query, string? searchQuery)
    {
        if (string.IsNullOrWhiteSpace(searchQuery))
            return query;

        var q = searchQuery.Trim();
        var parsedId = int.TryParse(q, out var id) ? (int?)id : null;

        return query.Where(p =>
            p.name.Contains(q)
            || p.sku == q
            || (p.barcode != null && p.barcode == q)
            || (parsedId != null && p.Id == parsedId.Value)
            || p.marketplaceLinks.Any(l =>
                l.externalProductId == q
                || (l.externalSku != null && l.externalSku == q))
            || p.variants.Any(v =>
                (v.sku != null && v.sku == q)
                || (v.barcode != null && v.barcode == q)));
    }

    /// <summary>
    /// Relevance: exact id → exact code/name → name prefix / name contains (broader name hits last).
    /// </summary>
    public static IOrderedQueryable<Product> OrderBySearchRelevance(
        this IQueryable<Product> query,
        string searchQuery)
    {
        var q = searchQuery.Trim();
        var parsedId = int.TryParse(q, out var id) ? (int?)id : null;

        return query
            .OrderByDescending(p => parsedId != null && p.Id == parsedId.Value)
            .ThenByDescending(p =>
                p.sku == q
                || (p.barcode != null && p.barcode == q)
                || p.marketplaceLinks.Any(l =>
                    l.externalProductId == q
                    || (l.externalSku != null && l.externalSku == q))
                || p.variants.Any(v =>
                    (v.sku != null && v.sku == q)
                    || (v.barcode != null && v.barcode == q)))
            .ThenByDescending(p => p.name == q)
            .ThenByDescending(p => p.name.StartsWith(q))
            .ThenByDescending(p => p.name.Contains(q));
    }
}
