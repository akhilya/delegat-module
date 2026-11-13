using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Delegat.Models.Ai;
using Delegat.Models.Marketplace;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services;

public interface IStoreDeletionService
{
    /// <summary>
    /// Permanently deletes a store and its scoped data. Caller must authorize (instance owner).
    /// </summary>
    /// <param name="storeId">Store primary key.</param>
    /// <param name="confirmationName">Must match <see cref="Store.name"/> exactly after trim (ordinal).</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteStoreAsync(int storeId, string confirmationName, CancellationToken ct = default);
}

/// <summary>
/// Hard-deletes a store: clears user selection, purges orphan rows without FK, deletes the store
/// (EF cascades catalog/marketplace/files), then best-effort MinIO prefix cleanup.
/// </summary>
public sealed class StoreDeletionService : IStoreDeletionService
{
    private readonly DataContext _db;
    private readonly IStorageService _storage;
    private readonly IBrandingSettingsService _brandingSettings;
    private readonly ILogger<StoreDeletionService> _logger;

    public StoreDeletionService(
        DataContext db,
        IStorageService storage,
        IBrandingSettingsService brandingSettings,
        ILogger<StoreDeletionService> logger)
    {
        _db = db;
        _storage = storage;
        _brandingSettings = brandingSettings;
        _logger = logger;
    }

    public async Task DeleteStoreAsync(int storeId, string confirmationName, CancellationToken ct = default)
    {
        if (!_db.Database.IsRelational())
        {
            await DeleteTrackedStoreAsync(storeId, confirmationName, ct);
            return;
        }

        var validated = false;
        var extraObjectKeys = new List<string>();
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            // Serialize destructive store changes, including the last-store check.
            var stores = _db.Database.IsMySql()
                ? await _db.stores.FromSqlRaw("SELECT * FROM `stores` ORDER BY `Id` FOR UPDATE").AsNoTracking().ToListAsync(ct)
                : await _db.stores.AsNoTracking().ToListAsync(ct);
            var store = stores.FirstOrDefault(s => s.Id == storeId);
            if (store == null)
            {
                if (validated) return; // A retry may follow a committed delete whose acknowledgement was lost.
                throw ApiException.NotFound(code: "store_not_found");
            }
            if (!string.Equals((confirmationName ?? "").Trim(), store.name ?? "", StringComparison.Ordinal))
                throw ApiException.BadRequest(code: "store_delete_name_mismatch");
            if (stores.Count <= 1) throw ApiException.BadRequest(code: "store_delete_last");
            validated = true;
            extraObjectKeys = await _db.aiChatAttachments.Where(a => a.storeId == storeId).Select(a => a.storageKey)
                .Where(key => key != "").ToListAsync(ct);
            extraObjectKeys.AddRange(await _db.pendingFolderJobs.Where(j => j.storeId == storeId && j.resultObjectKey != null)
                .Select(j => j.resultObjectKey!).ToListAsync(ct));
            extraObjectKeys = extraObjectKeys.Where(key => !string.IsNullOrWhiteSpace(key)).Distinct(StringComparer.Ordinal).ToList();

            await _db.Users.Where(u => u.selectedStoreId == storeId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.selectedStoreId, (int?)null), ct);
            await PurgeRelationalStoreDataAsync(storeId, ct);

            // Required product navigations normally hide trash; deletion must include it.
            await _db.productAttributeFiles.IgnoreQueryFilters()
                .Where(f => f.attributeValue.product.storeId == storeId).ExecuteDeleteAsync(ct);
            await _db.storeProductAttributeFiles.IgnoreQueryFilters()
                .Where(f => f.attributeValue.product.storeId == storeId).ExecuteDeleteAsync(ct);
            await _db.productFolders.IgnoreQueryFilters().Where(f => f.product.storeId == storeId)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.parentFolderId, (int?)null), ct);
            await _db.storeFolders.Where(f => f.storeId == storeId)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.parentFolderId, (int?)null), ct);
            // Store cascades may visit categories before products; the optional FK is RESTRICT.
            await _db.products.IgnoreQueryFilters().Where(p => p.storeId == storeId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.categoryId, (int?)null), ct);

            await _db.stores.Where(s => s.Id == storeId).ExecuteDeleteAsync(ct);
            await tx.CommitAsync(ct);
        });

        // External storage is never touched before a successful DB commit.
        foreach (var key in extraObjectKeys)
        {
            try { await _storage.DeleteAsync(key, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to clean up store {StoreId} artifact {Key}", storeId, key); }
        }
        await TryDeleteStoragePrefixAsync(storeId, ct);
        await _brandingSettings.DeleteForStoreAsync(storeId, ct);
    }

    private async Task PurgeRelationalStoreDataAsync(int storeId, CancellationToken ct)
    {
        await _db.operationLogEntries.Where(e => _db.operations.Any(o => o.storeId == storeId && o.operationId == e.operationId)).ExecuteDeleteAsync(ct);
        await _db.operations.Where(o => o.storeId == storeId).ExecuteDeleteAsync(ct);
        await _db.aiConversationMessages.Where(m => _db.aiConversations.Any(c => c.storeId == storeId && c.Id == m.conversationId)).ExecuteDeleteAsync(ct);
        await _db.aiChatAttachments.Where(a => a.storeId == storeId).ExecuteDeleteAsync(ct);
        await _db.aiConversations.Where(c => c.storeId == storeId).ExecuteDeleteAsync(ct);
        await _db.aiOperations.Where(o => o.storeId == storeId).ExecuteDeleteAsync(ct);
        await _db.aiStoreSettings.Where(s => s.StoreId == storeId).ExecuteDeleteAsync(ct);
        await _db.aiMemories.Where(m => m.storeId == storeId).ExecuteDeleteAsync(ct);
        await _db.aiSandboxRuns.Where(r => r.storeId == storeId).ExecuteDeleteAsync(ct);
        await _db.aiAgentAudits.Where(a => a.storeId == storeId).ExecuteDeleteAsync(ct);
        await _db.aiKnowledgeChunks.Where(c => c.storeId == storeId).ExecuteDeleteAsync(ct);
        await _db.mappingGenerativeCaches.Where(c => _db.categoryMappings.Any(m => m.Id == c.mappingId && m.connection.storeId == storeId)).ExecuteDeleteAsync(ct);
        await _db.pendingImageDownloads.Where(p => p.storeId == storeId).ExecuteDeleteAsync(ct);
        await _db.pendingProductImports.Where(p => p.storeId == storeId).ExecuteDeleteAsync(ct);
        await _db.pendingProductExports.Where(p => p.storeId == storeId).ExecuteDeleteAsync(ct);
        await _db.pendingProductBulkUpdates.Where(p => p.storeId == storeId).ExecuteDeleteAsync(ct);
        await _db.pendingFolderJobs.Where(p => p.storeId == storeId).ExecuteDeleteAsync(ct);
        await _db.pendingCategoryImports.Where(p => p.storeId == storeId).ExecuteDeleteAsync(ct);
        await _db.pendingCatalogConnectJobs.Where(p => p.storeId == storeId).ExecuteDeleteAsync(ct);
        await _db.pendingProductTransfers.Where(p => p.sourceStoreId == storeId || p.targetStoreId == storeId).ExecuteDeleteAsync(ct);
        await _db.scheduledTaskRuns.Where(r => r.storeId == storeId).ExecuteDeleteAsync(ct);
        await _db.scheduledTasks.Where(t => t.storeId == storeId).ExecuteDeleteAsync(ct);
        await _db.adapterHttpLogs.Where(l => l.storeId == storeId).ExecuteDeleteAsync(ct);
    }

    private async Task DeleteTrackedStoreAsync(int storeId, string confirmationName, CancellationToken ct)
    {
        var store = await _db.stores.FirstOrDefaultAsync(s => s.Id == storeId, ct);
        if (store == null)
            throw ApiException.NotFound("Store not found", code: "store_not_found");

        var expected = store.name ?? "";
        var provided = (confirmationName ?? "").Trim();
        if (!string.Equals(provided, expected, StringComparison.Ordinal))
            throw ApiException.BadRequest(
                "Confirmation name does not match the store name",
                code: "store_delete_name_mismatch");

        var storeCount = await _db.stores.CountAsync(ct);
        if (storeCount <= 1)
            throw ApiException.BadRequest(
                "Cannot delete the last store",
                code: "store_delete_last");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // FK User.selectedStoreId has no cascade — clear before store row is removed.
            var users = await _db.Users
                .Where(u => u.selectedStoreId == storeId)
                .ToListAsync(ct);
            foreach (var u in users)
                u.selectedStoreId = null;

            await PurgeOrphansWithoutStoreFkAsync(storeId, ct);

            // Self-ref Restrict on folder trees: detach parents so cascade delete of store can proceed.
            var storeFolders = await _db.storeFolders
                .Where(f => f.storeId == storeId)
                .ToListAsync(ct);
            foreach (var f in storeFolders)
                f.parentFolderId = null;

            var productIds = await _db.products
                .Where(p => p.storeId == storeId)
                .Select(p => p.Id)
                .ToListAsync(ct);
            if (productIds.Count > 0)
            {
                var productFolders = await _db.productFolders
                    .Where(f => productIds.Contains(f.productId))
                    .ToListAsync(ct);
                foreach (var f in productFolders)
                    f.parentFolderId = null;
            }

            _db.stores.Remove(store);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        await TryDeleteStoragePrefixAsync(storeId, ct);
        await _brandingSettings.DeleteForStoreAsync(storeId, ct);
    }

    private async Task PurgeOrphansWithoutStoreFkAsync(int storeId, CancellationToken ct)
    {
        // Operations + log lines (string operationId, no FK).
        var ops = await _db.operations.Where(o => o.storeId == storeId).ToListAsync(ct);
        if (ops.Count > 0)
        {
            var opKeys = ops.Select(o => o.operationId).ToList();
            var logEntries = await _db.operationLogEntries
                .Where(e => opKeys.Contains(e.operationId))
                .ToListAsync(ct);
            _db.operationLogEntries.RemoveRange(logEntries);
            _db.operations.RemoveRange(ops);
        }

        var conversations = await _db.aiConversations.Where(c => c.storeId == storeId).ToListAsync(ct);
        if (conversations.Count > 0)
        {
            var conversationIds = conversations.Select(c => c.Id).ToList();
            var messages = await _db.aiConversationMessages
                .Where(m => conversationIds.Contains(m.conversationId))
                .ToListAsync(ct);
            _db.aiConversationMessages.RemoveRange(messages);
            _db.aiConversations.RemoveRange(conversations);
        }

        _db.aiOperations.RemoveRange(
            await _db.aiOperations.Where(o => o.storeId == storeId).ToListAsync(ct));
        _db.aiStoreSettings.RemoveRange(
            await _db.aiStoreSettings.Where(s => s.StoreId == storeId).ToListAsync(ct));
        _db.aiMemories.RemoveRange(
            await _db.aiMemories.Where(m => m.storeId == storeId).ToListAsync(ct));

        // storeBrandingSettings cascades with store FK; MinIO + cache cleaned after commit.

        _db.pendingImageDownloads.RemoveRange(
            await _db.pendingImageDownloads.Where(p => p.storeId == storeId).ToListAsync(ct));
        _db.pendingProductImports.RemoveRange(
            await _db.pendingProductImports.Where(p => p.storeId == storeId).ToListAsync(ct));
        _db.pendingProductExports.RemoveRange(
            await _db.pendingProductExports.Where(p => p.storeId == storeId).ToListAsync(ct));
        _db.pendingProductBulkUpdates.RemoveRange(
            await _db.pendingProductBulkUpdates.Where(p => p.storeId == storeId).ToListAsync(ct));
        _db.pendingFolderJobs.RemoveRange(
            await _db.pendingFolderJobs.Where(p => p.storeId == storeId).ToListAsync(ct));
        _db.pendingCategoryImports.RemoveRange(
            await _db.pendingCategoryImports.Where(p => p.storeId == storeId).ToListAsync(ct));
        _db.pendingProductTransfers.RemoveRange(
            await _db.pendingProductTransfers
                .Where(p => p.sourceStoreId == storeId || p.targetStoreId == storeId)
                .ToListAsync(ct));

        _db.adapterHttpLogs.RemoveRange(
            await _db.adapterHttpLogs.Where(l => l.storeId == storeId).ToListAsync(ct));
    }

    private async Task TryDeleteStoragePrefixAsync(int storeId, CancellationToken ct)
    {
        foreach (var prefix in new[] { $"{storeId}/", $"stores/{storeId}/" })
        {
            try
            {
                var keys = await _storage.ListObjectsByPrefixAsync(prefix, ct);
                foreach (var key in keys)
                {
                    try
                    {
                        await _storage.DeleteAsync(key, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete storage object {Key} for store {StoreId}", key, storeId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to list/delete storage prefix {Prefix} for store {StoreId}", prefix, storeId);
            }
        }
    }
}
