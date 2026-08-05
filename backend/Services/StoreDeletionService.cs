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
    private readonly ILogger<StoreDeletionService> _logger;

    public StoreDeletionService(
        DataContext db,
        IStorageService storage,
        ILogger<StoreDeletionService> logger)
    {
        _db = db;
        _storage = storage;
        _logger = logger;
    }

    public async Task DeleteStoreAsync(int storeId, string confirmationName, CancellationToken ct = default)
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
        _db.pendingProductTransfers.RemoveRange(
            await _db.pendingProductTransfers
                .Where(p => p.sourceStoreId == storeId || p.targetStoreId == storeId)
                .ToListAsync(ct));

        _db.adapterHttpLogs.RemoveRange(
            await _db.adapterHttpLogs.Where(l => l.storeId == storeId).ToListAsync(ct));
    }

    private async Task TryDeleteStoragePrefixAsync(int storeId, CancellationToken ct)
    {
        var prefix = $"{storeId}/";
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
