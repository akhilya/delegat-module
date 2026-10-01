using Delegat;
using Delegat.Models.Products;
using Delegat.Services.Ai.Agent;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Resolves which catalog cards to queue for shop export.
/// A <c>where</c> clause uses the same allowlisted SQL dialect as <c>sql_readonly</c>.
/// </summary>
public static class ExportBatchSelection
{
    public static async Task<List<int>> ResolveProductIdsAsync(
        DataContext db,
        int storeId,
        IReadOnlyList<int>? productIds,
        bool allMatching,
        bool skipWithoutImages,
        CancellationToken ct,
        string? where = null,
        SqlReadonlyService? catalogFilter = null)
    {
        var src = db.products.AsNoTracking().Where(p => p.storeId == storeId && p.deletedAt == null);
        var hasWhere = !string.IsNullOrWhiteSpace(where);
        if (productIds is { Count: > 0 })
            src = src.Where(p => productIds.Contains(p.Id));
        else if (!allMatching && !hasWhere && !skipWithoutImages)
            return [];

        if (hasWhere)
        {
            if (catalogFilter == null)
                throw new InvalidOperationException("Catalog filter is not available");
            src = catalogFilter.ApplyProductWhereClause(src, storeId, where!);
        }
        else if (skipWithoutImages)
        {
            src = catalogFilter == null
                ? src.Where(p => p.images.Any())
                : catalogFilter.ApplyProductWhereClause(
                    src, storeId, "id IN (SELECT productId FROM product_images)");
        }

        return await src.Select(p => p.Id).ToListAsync(ct);
    }
}
