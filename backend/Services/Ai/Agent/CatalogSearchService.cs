using Delegat.Models.Products;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services.Ai.Agent;

public sealed class CatalogSearchHit
{
    public int Id { get; init; }
    public string Sku { get; init; } = "";
    public string? Barcode { get; init; }
    public string Name { get; init; } = "";
    public int? CategoryId { get; init; }
    public string Kind { get; init; } = "product";
}

public sealed class CatalogSearchService
{
    private readonly DataContext _db;

    public CatalogSearchService(DataContext db) => _db = db;

    public async Task<(object result, string summary)> SearchAsync(
        int storeId, string? query, int take, CancellationToken ct)
    {
        take = Math.Clamp(take <= 0 ? 50 : take, 1, 100);
        var q = (query ?? "").Trim();
        IQueryable<Product> products = _db.products.AsNoTracking()
            .Where(p => p.storeId == storeId && p.deletedAt == null);

        if (q.Length > 0)
        {
            products = products.Where(p =>
                p.sku.Contains(q) || p.name.Contains(q) || (p.barcode != null && p.barcode.Contains(q)));
        }

        var items = await products
            .OrderBy(p => p.name)
            .Take(take)
            .Select(p => new CatalogSearchHit
            {
                Id = p.Id,
                Sku = p.sku,
                Barcode = p.barcode,
                Name = p.name,
                CategoryId = p.categoryId,
                Kind = "product",
            })
            .ToListAsync(ct);

        List<CatalogSearchHit> files = [];
        if (q.Length > 0)
        {
            files = await _db.files.AsNoTracking()
                .Where(f => f.product.storeId == storeId && f.product.deletedAt == null
                    && ((f.displayName != null && f.displayName.Contains(q))
                        || f.filePath.Contains(q)))
                .Take(take)
                .Select(f => new CatalogSearchHit
                {
                    Id = f.Id,
                    Sku = f.product.sku,
                    Barcode = f.product.barcode,
                    Name = f.displayName ?? f.filePath,
                    CategoryId = f.productId,
                    Kind = "file",
                })
                .ToListAsync(ct);
        }

        var summary = q.Length == 0
            ? $"{items.Count} products"
            : $"{items.Count} products, {files.Count} files for '{q}'";
        return (new { products = items, files, query = q }, summary);
    }

    public async Task<IReadOnlyList<ProductFileMatchProduct>> ListMatchProductsAsync(
        int storeId, CancellationToken ct)
    {
        return await _db.products.AsNoTracking()
            .Where(p => p.storeId == storeId && p.deletedAt == null)
            .Select(p => new ProductFileMatchProduct(p.Id, p.sku, p.barcode, p.name))
            .ToListAsync(ct);
    }
}
