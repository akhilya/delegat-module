using Delegat;
using Delegat.Models.Products;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Resolves which catalog cards to queue for shop export.
/// </summary>
public static class ExportBatchSelection
{
    public static async Task<List<int>> ResolveProductIdsAsync(
        DataContext db,
        int storeId,
        IReadOnlyList<int>? productIds,
        bool allMatching,
        bool skipWithoutImages,
        CancellationToken ct)
    {
        var src = db.products.AsNoTracking().Where(p => p.storeId == storeId && p.deletedAt == null);
        if (productIds is { Count: > 0 })
            src = src.Where(p => productIds.Contains(p.Id));
        else if (!allMatching)
            return [];

        if (skipWithoutImages)
            src = src.Where(p => p.images.Any());

        return await src.Select(p => p.Id).ToListAsync(ct);
    }
}
