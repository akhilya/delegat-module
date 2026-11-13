using Microsoft.EntityFrameworkCore;

namespace Delegat.Services;

/// <summary>Deletes a bounded set of trashed products inside the caller's transaction.</summary>
internal static class ProductPermanentDeletion
{
    public static async Task<int> DeleteBatchAsync(DataContext db, int storeId, List<int> ids, CancellationToken ct)
    {
        // Products and variants still use SaveChanges and the barcode invariant.
        // Everything else is removed in sets without loading files/attribute values.
        var products = await db.products.IgnoreQueryFilters().AsSplitQuery()
            .Where(p => ids.Contains(p.Id) && p.storeId == storeId && p.deletedAt != null)
            .Include(p => p.variants)
            .ToListAsync(ct);
        ids = products.Select(p => p.Id).ToList();
        if (ids.Count == 0)
            return 0;

        await db.pendingProductExports.Where(p => ids.Contains(p.productId)).ExecuteDeleteAsync(ct);
        await db.pendingProductImports.Where(p => p.importedProductId.HasValue && ids.Contains(p.importedProductId.Value)).ExecuteDeleteAsync(ct);
        await db.pendingImageDownloads.Where(p => ids.Contains(p.productId)).ExecuteDeleteAsync(ct);
        await db.pendingProductTransfers.Where(p => p.newProductId.HasValue && ids.Contains(p.newProductId.Value)).ExecuteDeleteAsync(ct);
        await db.productVariantLinks.Where(l => ids.Contains(l.variant.productId)).ExecuteDeleteAsync(ct);
        await db.productMarketplaceLinks.Where(l => ids.Contains(l.productId)).ExecuteDeleteAsync(ct);
        await db.productGroupMembers.Where(m => ids.Contains(m.productId)).ExecuteDeleteAsync(ct);

        // Remove File attribute references before deleting files (RESTRICT).
        await db.productAttributeFiles.Where(f => ids.Contains(f.attributeValue.productId)).ExecuteDeleteAsync(ct);
        await db.storeProductAttributeFiles.Where(f => ids.Contains(f.attributeValue.productId)).ExecuteDeleteAsync(ct);
        await db.productAttributeValues.Where(v => ids.Contains(v.productId)).ExecuteDeleteAsync(ct);
        await db.storeProductAttributeValues.Where(v => ids.Contains(v.productId)).ExecuteDeleteAsync(ct);
        await db.notes.Where(n => ids.Contains(n.productId)).ExecuteDeleteAsync(ct);
        await db.images.Where(i => ids.Contains(i.productId)).ExecuteDeleteAsync(ct);
        await db.fileShareTokens.Where(t => ids.Contains(t.file.productId)).ExecuteDeleteAsync(ct);
        await db.tags.Where(t => ids.Contains(t.file.productId)).ExecuteDeleteAsync(ct);
        await db.files.Where(f => ids.Contains(f.productId)).ExecuteDeleteAsync(ct);

        // Include root shares, whose folderId is null. Break the hierarchy before
        // deletion: MySQL checks self-referencing RESTRICT constraints row by row.
        await db.folderShareTokens.Where(t => ids.Contains(t.productId)).ExecuteDeleteAsync(ct);
        await db.productFolders.Where(f => ids.Contains(f.productId))
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.parentFolderId, (int?)null), ct);
        await db.productFolders.Where(f => ids.Contains(f.productId)).ExecuteDeleteAsync(ct);

        db.productVariants.RemoveRange(products.SelectMany(p => p.variants));
        db.products.RemoveRange(products);
        await db.SaveChangesAsync(ct);
        return products.Count;
    }
}
