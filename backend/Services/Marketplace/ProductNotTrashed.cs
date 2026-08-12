using Delegat;
using Delegat.Infrastructure.Middleware;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Soft-deleted (trash) products must not participate in marketplace integrations.
/// </summary>
public static class ProductNotTrashed
{
    public const string ErrorCode = "product_in_trash";
    public const string Message =
        "Product is in the trash and cannot be used with marketplace integrations";

    public static async Task EnsureAsync(
        DataContext db,
        int productId,
        CancellationToken ct = default)
    {
        var row = await db.products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new { p.deletedAt })
            .FirstOrDefaultAsync(ct);

        if (row == null)
            throw ApiException.NotFound("Product not found", code: "product_not_found");

        if (row.deletedAt != null)
            throw ApiException.BadRequest(Message, code: ErrorCode);
    }

    public static async Task EnsureNoneAsync(
        DataContext db,
        IEnumerable<int> productIds,
        CancellationToken ct = default)
    {
        var ids = productIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0) return;

        var anyTrashed = await db.products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(p => ids.Contains(p.Id) && p.deletedAt != null, ct);

        if (anyTrashed)
            throw ApiException.BadRequest(Message, code: ErrorCode);
    }

    public static async Task EnsureLinkAsync(
        DataContext db,
        int linkId,
        CancellationToken ct = default)
    {
        var productId = await db.productMarketplaceLinks
            .AsNoTracking()
            .Where(l => l.Id == linkId)
            .Select(l => (int?)l.productId)
            .FirstOrDefaultAsync(ct);

        if (productId == null)
            throw ApiException.NotFound("Marketplace link not found");

        await EnsureAsync(db, productId.Value, ct);
    }

    public static async Task EnsureLinksAsync(
        DataContext db,
        IEnumerable<int> linkIds,
        CancellationToken ct = default)
    {
        var ids = linkIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0) return;

        var productIds = await db.productMarketplaceLinks
            .AsNoTracking()
            .Where(l => ids.Contains(l.Id))
            .Select(l => l.productId)
            .ToListAsync(ct);

        await EnsureNoneAsync(db, productIds, ct);
    }

    /// <summary>Non-throwing check for background workers / sync service results.</summary>
    public static async Task<bool> IsTrashedAsync(
        DataContext db,
        int productId,
        CancellationToken ct = default)
    {
        return await db.products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(p => p.Id == productId && p.deletedAt != null, ct);
    }
}
