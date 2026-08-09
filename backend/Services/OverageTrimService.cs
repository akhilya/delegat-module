using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Products;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services;

public interface IOverageTrimService
{
    Task<OverageTrimPreviewDto> PreviewAsync(CancellationToken ct = default);
    Task<OverageTrimResultDto> ExecuteAsync(CancellationToken ct = default);
}

/// <summary>
/// Deterministic excess selection for plan downgrade overage. Preview and execute use the same rules.
/// </summary>
public sealed class OverageTrimService : IOverageTrimService
{
    private readonly DataContext _db;
    private readonly QuotaService _quota;
    private readonly IStorageService _storage;
    private readonly IPlatformEntitlementsService _entitlements;
    private readonly ILogger<OverageTrimService> _logger;

    public OverageTrimService(
        DataContext db,
        QuotaService quota,
        IStorageService storage,
        IPlatformEntitlementsService entitlements,
        ILogger<OverageTrimService> logger)
    {
        _db = db;
        _quota = quota;
        _storage = storage;
        _entitlements = entitlements;
        _logger = logger;
    }

    public async Task<OverageTrimPreviewDto> PreviewAsync(CancellationToken ct = default)
    {
        var selection = await SelectAsync(ct);
        var entitlements = await _entitlements.GetCachedAsync(ct);
        var isOver = selection.Products.Count > 0
            || selection.Connections.Count > 0
            || selection.Files.Count > 0;

        return new OverageTrimPreviewDto(
            IsOverQuota: isOver,
            OveragePurgeAt: entitlements.OveragePurgeAt,
            CanAutoTrim: entitlements.Linked && entitlements.OveragePurgeAt != null && isOver,
            Products: selection.Products,
            Connections: selection.Connections,
            Files: selection.Files,
            ProductCount: selection.Products.Count,
            ConnectionCount: selection.Connections.Count,
            FileCount: selection.Files.Count,
            FileBytes: selection.Files.Sum(f => f.Bytes ?? 0));
    }

    public async Task<OverageTrimResultDto> ExecuteAsync(CancellationToken ct = default)
    {
        var selection = await SelectAsync(ct);
        var productsRemoved = 0;
        var connectionsRemoved = 0;
        var filesRemoved = 0;
        long bytesFreed = 0;

        foreach (var item in selection.Files)
        {
            if (await DeleteFileItemAsync(item, ct))
            {
                filesRemoved++;
                bytesFreed += item.Bytes ?? 0;
            }
        }

        foreach (var item in selection.Connections)
        {
            if (await DeleteConnectionAsync(item.Id, item.StoreId, ct))
                connectionsRemoved++;
        }

        foreach (var item in selection.Products)
        {
            if (await HardDeleteProductAsync(item.Id, item.StoreId, ct))
                productsRemoved++;
        }

        _logger.LogInformation(
            "Overage trim finished: products={Products}, connections={Connections}, files={Files}, bytes={Bytes}",
            productsRemoved,
            connectionsRemoved,
            filesRemoved,
            bytesFreed);

        return new OverageTrimResultDto(productsRemoved, connectionsRemoved, filesRemoved, bytesFreed);
    }

    private async Task<Selection> SelectAsync(CancellationToken ct)
    {
        var limits = _quota.GetLimits();
        var stores = await _db.stores
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .Select(s => new { s.Id, Name = s.name ?? $"Store {s.Id}" })
            .ToListAsync(ct);

        var products = new List<OverageTrimItemDto>();
        var connections = new List<OverageTrimItemDto>();
        var files = new List<OverageTrimItemDto>();

        foreach (var store in stores)
        {
            products.AddRange(await SelectProductsAsync(store.Id, store.Name, limits.MaxProducts, ct));
            connections.AddRange(await SelectConnectionsAsync(store.Id, store.Name, limits.MaxConnections, ct));
            files.AddRange(await SelectFilesAsync(
                store.Id,
                store.Name,
                limits.MaxFiles,
                limits.MaxStorageBytes,
                ct));
        }

        return new Selection(products, connections, files);
    }

    /// <summary>
    /// When active count exceeds the cap: soft-deleted products first (permanent cleanup),
    /// then oldest active by <c>updatedAt</c> until enough active removals bring the store under the limit.
    /// </summary>
    public static List<Product> SelectProductEntities(
        IEnumerable<Product> allForStore,
        int? maxProducts)
    {
        if (maxProducts is null) return new List<Product>();

        var list = allForStore.ToList();
        var activeCount = list.Count(p => p.deletedAt == null);
        if (activeCount <= maxProducts.Value) return new List<Product>();

        var activeToRemove = activeCount - maxProducts.Value;
        var trash = list
            .Where(p => p.deletedAt != null)
            .OrderBy(p => p.updatedAt)
            .ThenBy(p => p.Id);
        var oldestActive = list
            .Where(p => p.deletedAt == null)
            .OrderBy(p => p.updatedAt)
            .ThenBy(p => p.Id)
            .Take(activeToRemove);

        return trash.Concat(oldestActive).ToList();
    }

    private async Task<List<OverageTrimItemDto>> SelectProductsAsync(
        int storeId,
        string storeName,
        int? maxProducts,
        CancellationToken ct)
    {
        if (maxProducts is null) return new List<OverageTrimItemDto>();

        var all = await _db.products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.storeId == storeId)
            .ToListAsync(ct);

        return SelectProductEntities(all, maxProducts)
            .Select(p => new OverageTrimItemDto(
                Kind: "product",
                Id: p.Id,
                StoreId: storeId,
                StoreName: storeName,
                Label: string.IsNullOrWhiteSpace(p.name) ? p.sku : $"{p.name} ({p.sku})",
                Bytes: null,
                IsTrashed: p.deletedAt != null))
            .ToList();
    }

    private async Task<List<OverageTrimItemDto>> SelectConnectionsAsync(
        int storeId,
        string storeName,
        int? maxConnections,
        CancellationToken ct)
    {
        if (maxConnections is null) return new List<OverageTrimItemDto>();

        var list = await _db.marketplaceConnections
            .AsNoTracking()
            .Where(c => c.storeId == storeId)
            .OrderBy(c => c.createdAt)
            .ThenBy(c => c.Id)
            .ToListAsync(ct);

        if (list.Count <= maxConnections.Value) return new List<OverageTrimItemDto>();

        return list
            .Take(list.Count - maxConnections.Value)
            .Select(c => new OverageTrimItemDto(
                Kind: "connection",
                Id: c.Id,
                StoreId: storeId,
                StoreName: storeName,
                Label: $"{c.name} ({c.adapterKey})",
                Bytes: null,
                IsTrashed: false))
            .ToList();
    }

    private async Task<List<OverageTrimItemDto>> SelectFilesAsync(
        int storeId,
        string storeName,
        int? maxFiles,
        long? maxStorageBytes,
        CancellationToken ct)
    {
        if (maxFiles is null && maxStorageBytes is null)
            return new List<OverageTrimItemDto>();

        var brandingPrefix = $"branding/{storeId}/";

        var storeFiles = await _db.storeFiles
            .AsNoTracking()
            .Where(f => f.storeId == storeId && !f.filePath.StartsWith(brandingPrefix))
            .Select(f => new FileCandidate(f.Id, "storeFile", f.displayName ?? f.filePath, f.length, f.filePath))
            .ToListAsync(ct);

        var productFiles = await _db.files
            .AsNoTracking()
            .Where(f => f.product.storeId == storeId)
            .Select(f => new FileCandidate(f.Id, "productFile", f.displayName ?? f.filePath, f.length, f.filePath))
            .ToListAsync(ct);

        var ordered = storeFiles
            .Concat(productFiles)
            .OrderByDescending(f => f.Length)
            .ThenBy(f => f.Id)
            .ToList();

        long totalBytes = ordered.Sum(f => f.Length);
        var totalCount = ordered.Count;
        var selected = new List<OverageTrimItemDto>();
        long remainingBytes = totalBytes;
        var remainingCount = totalCount;

        foreach (var f in ordered)
        {
            var overFiles = maxFiles is not null && remainingCount > maxFiles.Value;
            var overStorage = maxStorageBytes is not null && remainingBytes > maxStorageBytes.Value;
            if (!overFiles && !overStorage) break;

            selected.Add(new OverageTrimItemDto(
                Kind: f.Kind,
                Id: f.Id,
                StoreId: storeId,
                StoreName: storeName,
                Label: f.Label,
                Bytes: f.Length,
                IsTrashed: false));
            remainingBytes -= f.Length;
            remainingCount--;
        }

        return selected;
    }

    private async Task<bool> DeleteFileItemAsync(OverageTrimItemDto item, CancellationToken ct)
    {
        if (item.Kind == "storeFile")
        {
            var file = await _db.storeFiles.FirstOrDefaultAsync(f => f.Id == item.Id && f.storeId == item.StoreId, ct);
            if (file == null) return false;

            await _db.storeFileTags.Where(t => t.fileId == file.Id).ExecuteDeleteAsync(ct);
            await _db.storeFileShareTokens.Where(t => t.fileId == file.Id).ExecuteDeleteAsync(ct);
            try { await _storage.DeleteAsync(file.filePath, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "MinIO delete failed for store file {Path}", file.filePath); }
            _db.storeFiles.Remove(file);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        if (item.Kind == "productFile")
        {
            var file = await _db.files
                .Include(f => f.product)
                .FirstOrDefaultAsync(f => f.Id == item.Id && f.product.storeId == item.StoreId, ct);
            if (file == null) return false;

            await _db.tags.Where(t => t.fileId == file.Id).ExecuteDeleteAsync(ct);
            await _db.fileShareTokens.Where(t => t.fileId == file.Id).ExecuteDeleteAsync(ct);
            try { await _storage.DeleteAsync(file.filePath, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "MinIO delete failed for product file {Path}", file.filePath); }
            _db.files.Remove(file);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        return false;
    }

    private async Task<bool> DeleteConnectionAsync(int connectionId, int storeId, CancellationToken ct)
    {
        var deleted = await _db.marketplaceConnections
            .Where(c => c.Id == connectionId && c.storeId == storeId)
            .ExecuteDeleteAsync(ct);
        return deleted > 0;
    }

    private async Task<bool> HardDeleteProductAsync(int productId, int storeId, CancellationToken ct)
    {
        var product = await _db.products
            .IgnoreQueryFilters()
            .Include(p => p.files)
            .Include(p => p.images)
            .Include(p => p.notes)
            .Include(p => p.attributeValues)
            .Include(p => p.storeAttributeValues)
            .FirstOrDefaultAsync(p => p.Id == productId && p.storeId == storeId, ct);

        if (product == null) return false;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            await _db.pendingProductExports
                .Where(pe => pe.productId == productId)
                .ExecuteDeleteAsync(ct);
            await _db.pendingProductImports
                .Where(pi => pi.importedProductId == productId)
                .ExecuteDeleteAsync(ct);
            await _db.pendingImageDownloads
                .Where(pid => pid.productId == productId)
                .ExecuteDeleteAsync(ct);
            await _db.pendingProductTransfers
                .Where(pt => pt.newProductId == productId)
                .ExecuteDeleteAsync(ct);
            await _db.productVariantLinks
                .Where(vl => vl.variant.productId == productId)
                .ExecuteDeleteAsync(ct);
            await _db.productVariants
                .Where(v => v.productId == productId)
                .ExecuteDeleteAsync(ct);
            await _db.productMarketplaceLinks
                .Where(ml => ml.productId == productId)
                .ExecuteDeleteAsync(ct);
            await _db.productGroupMembers
                .Where(gm => gm.productId == productId)
                .ExecuteDeleteAsync(ct);

            var fileIds = product.files.Select(f => f.Id).ToList();
            if (fileIds.Count > 0)
            {
                await _db.fileShareTokens.Where(fst => fileIds.Contains(fst.fileId)).ExecuteDeleteAsync(ct);
                await _db.tags.Where(t => fileIds.Contains(t.fileId)).ExecuteDeleteAsync(ct);
                foreach (var f in product.files)
                {
                    try { await _storage.DeleteAsync(f.filePath, ct); }
                    catch (Exception ex) { _logger.LogWarning(ex, "MinIO delete failed for {Path}", f.filePath); }
                }
            }

            var folderIds = await _db.productFolders
                .Where(pf => pf.productId == productId)
                .Select(pf => pf.Id)
                .ToListAsync(ct);
            if (folderIds.Count > 0)
            {
                await _db.folderShareTokens
                    .Where(fst => fst.folderId.HasValue && folderIds.Contains(fst.folderId.Value))
                    .ExecuteDeleteAsync(ct);
            }

            _db.storeProductAttributeValues.RemoveRange(product.storeAttributeValues);
            _db.productAttributeValues.RemoveRange(product.attributeValues);
            _db.notes.RemoveRange(product.notes);
            _db.images.RemoveRange(product.images);
            _db.files.RemoveRange(product.files);

            await _db.productFolders
                .Where(pf => pf.productId == productId && pf.parentFolderId != null)
                .ExecuteDeleteAsync(ct);
            await _db.productFolders
                .Where(pf => pf.productId == productId)
                .ExecuteDeleteAsync(ct);

            _db.products.Remove(product);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private sealed record Selection(
        List<OverageTrimItemDto> Products,
        List<OverageTrimItemDto> Connections,
        List<OverageTrimItemDto> Files);

    private sealed record FileCandidate(int Id, string Kind, string Label, long Length, string Path);
}
