using Microsoft.EntityFrameworkCore;
using Delegat.Enums;
using Delegat.Infrastructure.Middleware;
using Delegat.Models.Marketplace;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Picks which category mapping a product uses on a connection when the local
/// category has more than one marketplace category.
/// </summary>
public class ProductMappingSelectionService
{
    private readonly DataContext _db;

    public ProductMappingSelectionService(DataContext db) => _db = db;

    public async Task<IReadOnlyList<MappingListItem>> ListAsync(
        int storeId,
        int connectionId,
        CancellationToken ct = default)
    {
        await RequireConnectionAsync(storeId, connectionId, ct);
        return await _db.categoryMappings.AsNoTracking()
            .Where(m => m.connectionId == connectionId)
            .OrderBy(m => m.localCategory.name)
            .ThenBy(m => m.externalCategoryName)
            .Select(m => new MappingListItem(
                m.Id,
                m.name,
                m.localCategoryId,
                m.localCategory.name,
                m.externalCategoryId,
                m.externalCategoryName))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ProductMappingChoice>> ResolveAsync(
        int storeId,
        int connectionId,
        IReadOnlyList<int> productIds,
        CancellationToken ct = default)
    {
        await RequireConnectionAsync(storeId, connectionId, ct);
        if (productIds.Count == 0)
            return [];

        var ids = productIds.Where(id => id > 0).Distinct().ToList();
        var products = await _db.products
            .AsNoTracking()
            .Include(p => p.category)
            .Where(p => p.storeId == storeId && ids.Contains(p.Id))
            .ToListAsync(ct);

        var links = await _db.productMarketplaceLinks
            .AsNoTracking()
            .Where(l => ids.Contains(l.productId) && l.connectionId == connectionId)
            .ToListAsync(ct);
        var linkByProduct = links.ToDictionary(l => l.productId);

        var categoryIds = products
            .Where(p => p.categoryId.HasValue)
            .Select(p => p.categoryId!.Value)
            .Distinct()
            .ToList();
        var mappings = categoryIds.Count == 0
            ? []
            : await _db.categoryMappings.AsNoTracking()
                .Where(m => m.connectionId == connectionId && categoryIds.Contains(m.localCategoryId))
                .ToListAsync(ct);
        var byCategory = mappings.GroupBy(m => m.localCategoryId).ToDictionary(g => g.Key, g => g.ToList());

        return products.Select(product =>
        {
            var available = product.categoryId is int categoryId
                && byCategory.TryGetValue(categoryId, out var rows)
                    ? rows
                    : [];
            linkByProduct.TryGetValue(product.Id, out var link);
            var selected = link?.categoryMappingId;
            return new ProductMappingChoice(
                product.Id,
                product.name,
                product.categoryId,
                product.category?.name,
                selected,
                available.Select(m => new MappingChoice(
                    m.Id,
                    m.name,
                    m.externalCategoryId,
                    m.externalCategoryName,
                    product.category?.name)).ToList(),
                available.Count > 1 && selected == null);
        }).ToList();
    }

    public async Task<int> SaveAsync(
        int storeId,
        int connectionId,
        IReadOnlyList<MappingSelection> selections,
        CancellationToken ct = default)
    {
        await RequireConnectionAsync(storeId, connectionId, ct);
        if (selections.Count == 0)
            return 0;

        var productIds = selections.Select(s => s.ProductId).Where(id => id > 0).Distinct().ToList();
        if (productIds.Count == 0)
            throw ApiException.BadRequest(code: "product_id_required");

        await ProductNotTrashed.EnsureNoneAsync(_db, productIds, ct);

        var products = await _db.products
            .Where(p => p.storeId == storeId && productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.categoryId })
            .ToListAsync(ct);
        if (products.Count != productIds.Count)
            throw ApiException.NotFound(code: "product_not_found");
        var categoryByProduct = products.ToDictionary(p => p.Id, p => p.categoryId);

        var mappingIds = selections.Select(s => s.CategoryMappingId).Where(id => id > 0).Distinct().ToList();
        var mappings = await _db.categoryMappings
            .AsNoTracking()
            .Where(m => m.connectionId == connectionId && mappingIds.Contains(m.Id))
            .Select(m => new { m.Id, m.localCategoryId, m.name, m.externalCategoryName })
            .ToListAsync(ct);
        var mappingById = mappings.ToDictionary(m => m.Id);

        foreach (var selection in selections)
        {
            if (selection.ProductId <= 0 || selection.CategoryMappingId <= 0)
                throw ApiException.BadRequest(code: "product_and_mapping_required");
            if (!categoryByProduct.TryGetValue(selection.ProductId, out var categoryId) || categoryId == null)
                throw ApiException.BadRequest(code: "product_has_no_category");
            if (!mappingById.TryGetValue(selection.CategoryMappingId, out var mapping))
                throw ApiException.NotFound(code: "mapping_not_found");
            if (mapping.localCategoryId != categoryId.Value)
                throw ApiException.BadRequest(
                    "That mapping belongs to a different local category",
                    code: "mapping_category_mismatch");
        }

        var links = await _db.productMarketplaceLinks
            .Where(l => productIds.Contains(l.productId) && l.connectionId == connectionId)
            .ToListAsync(ct);
        var linkByProduct = links.ToDictionary(l => l.productId);

        var updated = 0;
        foreach (var selection in selections)
        {
            if (linkByProduct.TryGetValue(selection.ProductId, out var link))
            {
                link.categoryMappingId = selection.CategoryMappingId;
            }
            else
            {
                var created = new ProductMarketplaceLink();
                created.AddFill(new ProductMarketplaceLink.AddForm
                {
                    productId = selection.ProductId,
                    connectionId = connectionId,
                    externalProductId = $"pending:{selection.ProductId}",
                    status = ProductMarketplaceStatus.NotSynced,
                    categoryMappingId = selection.CategoryMappingId,
                });
                _db.productMarketplaceLinks.Add(created);
                linkByProduct[selection.ProductId] = created;
            }
            updated++;
        }

        await _db.SaveChangesAsync(ct);
        return updated;
    }

    private async Task RequireConnectionAsync(int storeId, int connectionId, CancellationToken ct)
    {
        var exists = await _db.marketplaceConnections.AsNoTracking()
            .AnyAsync(c => c.Id == connectionId && c.storeId == storeId, ct);
        if (!exists)
            throw ApiException.NotFound(code: "connection_not_found");
    }
}

public sealed record MappingListItem(
    int Id,
    string? Name,
    int LocalCategoryId,
    string? LocalCategoryName,
    string ExternalCategoryId,
    string? ExternalCategoryName);

public sealed record MappingChoice(
    int Id,
    string? Name,
    string ExternalCategoryId,
    string? ExternalCategoryName,
    string? LocalCategoryName);

public sealed record ProductMappingChoice(
    int ProductId,
    string? ProductName,
    int? CategoryId,
    string? CategoryName,
    int? SelectedMappingId,
    IReadOnlyList<MappingChoice> AvailableMappings,
    bool NeedsSelection);

public sealed record MappingSelection(int ProductId, int CategoryMappingId);
