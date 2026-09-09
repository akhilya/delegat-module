using Microsoft.EntityFrameworkCore;
using Delegat.Enums;
using Delegat.Infrastructure;
using Delegat.Infrastructure.Middleware;
using Delegat.Marketplace.SDK;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Models.Products;

namespace Delegat.Services.Marketplace;

public class CatalogMatchPreviewResult
{
    public int Total { get; set; }
    public int MatchedCount { get; set; }
    public int AmbiguousCount { get; set; }
    public int UnmatchedCount { get; set; }
    public int AlreadyLinkedCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    /// <summary>fromLocal = lookup each local SKU/barcode; fromRemote = list marketplace cards.</summary>
    public string Mode { get; set; } = "fromRemote";
    public List<CatalogMatchRowDto> Items { get; set; } = [];
}

public class CatalogMatchRowDto
{
    public string RowKey { get; set; } = "";
    public string ExternalProductId { get; set; } = "";
    public string? Name { get; set; }
    public string? Sku { get; set; }
    public string? Barcode { get; set; }
    public string? ExternalCategoryId { get; set; }
    public string? ExternalCategoryName { get; set; }
    public string Status { get; set; } = "unmatched";
    public string? MatchedBy { get; set; }
    public int? ProductId { get; set; }
    public string? ProductName { get; set; }
    public string? ProductSku { get; set; }
    public List<CatalogMatchCandidateDto> Candidates { get; set; } = [];
}

public class CatalogMatchCandidateDto
{
    public int ProductId { get; set; }
    public string? Sku { get; set; }
    public string? Name { get; set; }
}

public class CatalogMatchApplyItem
{
    public int? ProductId { get; set; }
    public string ExternalProductId { get; set; } = "";
    public string? ExternalCategoryId { get; set; }
    public string? ExternalCategoryName { get; set; }
}

public class CatalogMatchApplyResult
{
    public string OperationId { get; set; } = "";
    public int Linked { get; set; }
    public int Imported { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public int MappingsCreated { get; set; }
    public int AttributesImported { get; set; }
}

public class CatalogImportUnlinkedResult
{
    public string BatchId { get; set; } = "";
    public int Queued { get; set; }
    public int AlreadyLinked { get; set; }
}

public class CatalogMatchService
{
    public const int MaxRemoteProducts = 10_000;
    public const int LocalLookupThreshold = 200;
    /// <summary>Adapters clamp internally (WB 100, OpenCart 200, Ozon 1000).</summary>
    private const int RemotePageSize = 1000;
    private const int LookupConcurrency = 8;
    private const int LocalLookupBatchSize = 100;

    private readonly DataContext _db;
    private readonly IMarketplaceAdapterRegistry _adapterRegistry;
    private readonly IOperationLogger _operationLogger;
    private readonly ILogger<CatalogMatchService> _logger;
    private readonly QuotaService _quotaService;
    private readonly CategoryImportService _categoryImport;
    private readonly CategoryImportQueueService _categoryImportQueue;
    private readonly ProductSyncService _productSync;

    private readonly record struct MappingEnsureResult(int? MappingId, int? LocalCategoryId, bool Created);

    public CatalogMatchService(
        DataContext db,
        IMarketplaceAdapterRegistry adapterRegistry,
        IOperationLogger operationLogger,
        ILogger<CatalogMatchService> logger,
        QuotaService quotaService,
        CategoryImportService categoryImport,
        CategoryImportQueueService categoryImportQueue,
        ProductSyncService productSync)
    {
        _db = db;
        _adapterRegistry = adapterRegistry;
        _operationLogger = operationLogger;
        _logger = logger;
        _quotaService = quotaService;
        _categoryImport = categoryImport;
        _categoryImportQueue = categoryImportQueue;
        _productSync = productSync;
    }

    internal static void EnsureConnectionActive(MarketplaceConnection connection)
    {
        if (connection.isActive)
            return;
        throw ApiException.BadRequest("Connection is inactive", code: "connection_inactive");
    }

    /// <summary>
    /// fromLocal = look up each unlinked local SKU/barcode (used when that set is small).
    /// fromRemote = page the marketplace catalog (only when local lookup would be too large).
    /// </summary>
    internal static string ChoosePreviewMode(bool hasAll, bool hasOne, int localCount)
    {
        if (hasOne && localCount <= LocalLookupThreshold)
            return "fromLocal";
        if (hasAll)
            return "fromRemote";
        return "fromLocal";
    }

    public async Task<CatalogMatchPreviewResult> PreviewAsync(
        MarketplaceConnection connection,
        int page,
        int pageSize,
        string? statusFilter,
        CancellationToken ct,
        string? operationId = null,
        Func<int, int?, string, Task>? onProgress = null)
    {
        EnsureConnectionActive(connection);
        var adapter = RequireImportAdapter(connection);
        var hasAll = adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportAllProducts);
        var hasOne = adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportProduct);
        var localCount = await CountUnlinkedLocalsAsync(connection.storeId, connection.Id, ct);

        List<CatalogMatchRowDto> dtos;
        int alreadyLinkedCount;

        if (!hasAll && !hasOne)
        {
            throw ApiException.BadRequest(
                "Adapter does not support matching products",
                code: "adapter_capability_missing");
        }

        if (!hasAll && hasOne && localCount > LocalLookupThreshold)
        {
            throw ApiException.BadRequest(
                "Too many local products to look up one by one on this adapter",
                code: "catalog_match_too_large");
        }

        var mode = ChoosePreviewMode(hasAll, hasOne, localCount);
        if (onProgress != null)
            await onProgress(0, mode == "fromLocal" ? localCount : null, mode);

        if (mode == "fromRemote")
        {
            (dtos, alreadyLinkedCount) = await PreviewFromRemoteAsync(
                connection, adapter, ct, operationId, onProgress);
        }
        else
        {
            (dtos, alreadyLinkedCount) = await PreviewFromLocalAsync(
                connection, adapter, ct, operationId, onProgress);
        }

        var filter = (statusFilter ?? "all").Trim().ToLowerInvariant();
        var filtered = filter switch
        {
            "matched" => dtos.Where(r => r.Status == "matched").ToList(),
            "ambiguous" => dtos.Where(r => r.Status == "ambiguous").ToList(),
            "unmatched" => dtos.Where(r => r.Status == "unmatched").ToList(),
            _ => dtos,
        };

        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        var unlimited = pageSize >= MaxRemoteProducts;
        if (!unlimited && pageSize > 200) pageSize = 200;

        return new CatalogMatchPreviewResult
        {
            Total = filtered.Count,
            MatchedCount = dtos.Count(r => r.Status == "matched"),
            AmbiguousCount = dtos.Count(r => r.Status == "ambiguous"),
            UnmatchedCount = dtos.Count(r => r.Status == "unmatched"),
            AlreadyLinkedCount = alreadyLinkedCount,
            Page = page,
            PageSize = unlimited ? filtered.Count : pageSize,
            Mode = mode,
            Items = unlimited
                ? filtered
                : filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
        };
    }

    public async Task<CatalogMatchApplyResult> ApplyAsync(
        MarketplaceConnection connection,
        IReadOnlyList<CatalogMatchApplyItem> items,
        bool importUnmatched,
        int userId,
        CancellationToken ct,
        string? operationId = null)
    {
        EnsureConnectionActive(connection);
        var adapter = _adapterRegistry.GetByKey(connection.adapterKey)
            ?? throw ApiException.BadRequest($"Unknown adapter: {connection.adapterKey}");

        var clientStarted = !string.IsNullOrWhiteSpace(operationId);
        operationId = clientStarted
            ? operationId!.Trim()
            : Guid.NewGuid().ToString("N");
        if (!clientStarted)
        {
            await _operationLogger.StartAsync(
                operationId,
                connection.storeId,
                userId,
                "catalog-match",
                "Link marketplace catalog",
                connectionId: connection.Id,
                connectionName: connection.name,
                batchId: operationId,
                batchType: "catalog-match",
                totalItems: items.Count);
        }

        var linked = 0;
        var imported = 0;
        var failed = 0;
        var skipped = 0;
        var attributesImported = 0;
        var mappingCache = new Dictionary<(int LocalCategoryId, string ExternalCategoryId), MappingEnsureResult>();
        var repaired = await RepairLinksWithoutMappingAsync(
            connection, adapter, mappingCache, operationId, ct);
        var mappingsCreated = repaired.MappingsCreated;
        var toImport = new List<(string ExternalId, string? Name, string? ExternalCategoryId, string? ExternalCategoryName)>();
        var linkedPairs = new List<(int ProductId, string ExternalId)>(repaired.Pairs);

        var remotesById = await LoadRemotesByIdAsync(
            adapter,
            connection,
            items
                .Where(i => i.ProductId is > 0)
                .Select(i => (i.ExternalProductId ?? "").Trim())
                .Where(id => id.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            operationId,
            ct);

        foreach (var kv in repaired.Remotes)
            remotesById.TryAdd(kv.Key, kv.Value);

        foreach (var item in items)
        {
            if (await OperationLifecycle.IsStopRequestedAsync(_db, operationId, ct))
            {
                await _operationLogger.FinishAsync(
                    operationId,
                    connection.storeId,
                    "cancelled",
                    $"Stopped. Linked {linked}, failed {failed}");
                return new CatalogMatchApplyResult
                {
                    OperationId = operationId,
                    Linked = linked,
                    Failed = failed,
                    Skipped = skipped,
                    MappingsCreated = mappingsCreated,
                };
            }

            var externalId = (item.ExternalProductId ?? "").Trim();
            if (externalId.Length == 0 || externalId.Length > ProductMarketplaceLink.EXTERNAL_ID_MAX_LENGTH)
            {
                failed++;
                await _operationLogger.LogAsync(operationId, connection.storeId, "warning",
                    "Skipped a row with an empty or oversized external id");
                continue;
            }

            if (item.ProductId is int productId)
            {
                remotesById.TryGetValue(externalId, out var remote);
                var (ok, error, mappingCreated) = await TryLinkAsync(
                    connection,
                    productId,
                    externalId,
                    item.ExternalCategoryId,
                    item.ExternalCategoryName,
                    mappingCache,
                    operationId,
                    remote,
                    ct);
                if (ok)
                {
                    linked++;
                    linkedPairs.Add((productId, remote?.ExternalId ?? externalId));
                    if (mappingCreated) mappingsCreated++;
                    await _operationLogger.LogAsync(operationId, connection.storeId, "info",
                        $"Linked product {productId} → {externalId}");
                    await _operationLogger.ProgressAsync(
                        operationId, connection.storeId, items.Count, linked, failed);
                }
                else
                {
                    failed++;
                    await _operationLogger.LogAsync(operationId, connection.storeId, "error",
                        $"{externalId}: {error}");
                    await _operationLogger.ProgressAsync(
                        operationId, connection.storeId, items.Count, linked, failed);
                }
            }
            else if (importUnmatched)
            {
                toImport.Add((externalId, null, item.ExternalCategoryId, item.ExternalCategoryName));
            }
            else
            {
                skipped++;
            }
        }

        if (toImport.Count > 0)
        {
            var importBatchId = Guid.NewGuid().ToString("N");
            var queued = await QueueImportsAsync(connection, userId, toImport, importBatchId, ct);
            imported = queued.Queued;
            if (imported > 0)
            {
                await StartBatchImportOperationsAsync(
                    connection, userId, importBatchId, imported, queued.CategoryJobs);
                if (toImport.Count > imported)
                    await _operationLogger.LogAsync(importBatchId, connection.storeId, "warning",
                        $"Only {imported} of {toImport.Count} unmatched products were queued (product limit).");
            }
        }

        attributesImported = await ImportLinkedAttributesAsync(
            connection, remotesById, linkedPairs, operationId, ct);

        await _operationLogger.ProgressAsync(
            operationId, connection.storeId, items.Count, linked, failed);
        var status = failed == 0 ? "completed" : (linked > 0 || imported > 0 ? "partial" : "failed");
        await _operationLogger.FinishAsync(
            operationId,
            connection.storeId,
            status,
            $"Linked {linked}, imported {imported}, skipped {skipped}, failed {failed}, mappings {mappingsCreated}, fields {attributesImported}");

        return new CatalogMatchApplyResult
        {
            OperationId = operationId,
            Linked = linked,
            Imported = imported,
            Failed = failed,
            Skipped = skipped,
            MappingsCreated = mappingsCreated,
            AttributesImported = attributesImported,
        };
    }

    public async Task<CatalogImportUnlinkedResult> QueueUnlinkedImportAsync(
        MarketplaceConnection connection,
        int userId,
        CancellationToken ct,
        string? operationId = null,
        string? batchId = null,
        Func<int, int?, int, Task>? onProgress = null)
    {
        EnsureConnectionActive(connection);
        var adapter = RequireImportAllAdapter(connection);
        var linkedIds = await LinkedExternalIdsAsync(connection.Id, ct);
        batchId = string.IsNullOrWhiteSpace(batchId)
            ? Guid.NewGuid().ToString("N")
            : batchId.Trim();

        var totalQueued = 0;
        var categoryJobs = 0;
        var remotesSeen = 0;
        var startedImportOp = false;
        int? remoteTotal = null;

        await ScanUnlinkedRemotesAsync(
            adapter,
            connection,
            linkedIds,
            async (page, scanned, total, pageCt) =>
            {
                remotesSeen += page.Count;
                if (total is int known && known > 0)
                    remoteTotal = known;

                if (page.Count > 0)
                {
                    var queued = await QueueImportsAsync(
                        connection,
                        userId,
                        page.Select(r => (r.ExternalId, (string?)r.Name, r.CategoryId, r.CategoryName)).ToList(),
                        batchId,
                        pageCt);
                    totalQueued += queued.Queued;
                    categoryJobs += queued.CategoryJobs;

                    if (totalQueued > 0 && !startedImportOp)
                    {
                        await StartBatchImportOperationsAsync(
                            connection, userId, batchId, totalQueued, categoryJobs);
                        startedImportOp = true;
                    }
                    else if (startedImportOp)
                    {
                        var importOp = await _db.operations.AsNoTracking()
                            .FirstOrDefaultAsync(o => o.operationId == batchId, pageCt);
                        await _operationLogger.ProgressAsync(
                            batchId,
                            connection.storeId,
                            totalQueued,
                            importOp?.completedItems ?? 0,
                            importOp?.failedItems ?? 0);
                    }
                }

                if (onProgress != null)
                    await onProgress(scanned, remoteTotal, totalQueued);
                if (!string.IsNullOrEmpty(operationId))
                {
                    await _operationLogger.ProgressAsync(
                        operationId,
                        connection.storeId,
                        remoteTotal ?? 0,
                        scanned,
                        0);
                }
            },
            ct);

        if (startedImportOp && remotesSeen > totalQueued)
        {
            var msg = totalQueued == 0
                ? "No products were queued (product limit reached)."
                : $"Only {totalQueued} of {remotesSeen} unlinked products were queued (product limit).";
            if (totalQueued > 0)
                await _operationLogger.LogAsync(batchId, connection.storeId, "warning", msg);
        }
        else if (!startedImportOp && remotesSeen > 0)
        {
            var msg = "No products were queued (product limit reached).";
            if (!string.IsNullOrEmpty(operationId))
                await _operationLogger.LogAsync(operationId, connection.storeId, "warning", msg);
        }

        return new CatalogImportUnlinkedResult
        {
            BatchId = batchId,
            Queued = totalQueued,
            AlreadyLinked = linkedIds.Count,
        };
    }

    public async Task<CatalogMatchApplyResult> RepairMissingMappingsAsync(
        MarketplaceConnection connection,
        int userId,
        CancellationToken ct)
    {
        EnsureConnectionActive(connection);
        var adapter = _adapterRegistry.GetByKey(connection.adapterKey)
            ?? throw ApiException.BadRequest($"Unknown adapter: {connection.adapterKey}");

        var operationId = Guid.NewGuid().ToString("N");
        await _operationLogger.StartAsync(
            operationId,
            connection.storeId,
            userId,
            "catalog-match",
            "Create missing catalog mappings",
            connectionId: connection.Id,
            connectionName: connection.name,
            batchId: operationId,
            batchType: "catalog-match");

        var mappingCache = new Dictionary<(int LocalCategoryId, string ExternalCategoryId), MappingEnsureResult>();
        var repaired = await RepairLinksWithoutMappingAsync(
            connection, adapter, mappingCache, operationId, ct);

        var attributesImported = await ImportLinkedAttributesAsync(
            connection, repaired.Remotes, repaired.Pairs, operationId, ct);

        await _operationLogger.FinishAsync(
            operationId,
            connection.storeId,
            "completed",
            $"Created {repaired.MappingsCreated} mappings, copied {attributesImported} fields");

        return new CatalogMatchApplyResult
        {
            OperationId = operationId,
            MappingsCreated = repaired.MappingsCreated,
            AttributesImported = attributesImported,
        };
    }

    private async Task StartBatchImportOperationsAsync(
        MarketplaceConnection connection,
        int userId,
        string batchId,
        int queued,
        int categoryJobs)
    {
        if (queued > 0)
        {
            var exists = await _db.operations.AsNoTracking()
                .AnyAsync(o => o.operationId == batchId);
            if (!exists)
            {
                await _operationLogger.StartAsync(
                    batchId,
                    connection.storeId,
                    userId,
                    "batch-import",
                    $"Import {queued} products",
                    connectionId: connection.Id,
                    connectionName: connection.name,
                    batchId: batchId,
                    batchType: "import",
                    totalItems: queued);
            }

            var imageBatchId = $"{batchId}-img";
            var imageExists = await _db.operations.AsNoTracking()
                .AnyAsync(o => o.operationId == imageBatchId);
            if (!imageExists)
            {
                await _operationLogger.StartAsync(
                    imageBatchId,
                    connection.storeId,
                    userId,
                    "image-download",
                    "Image download",
                    connectionId: connection.Id,
                    connectionName: connection.name,
                    batchId: imageBatchId,
                    batchType: "image-download",
                    totalItems: 0);
            }

            await _operationLogger.LogAsync(batchId, connection.storeId, "info",
                $"Added {queued} products to the queue.");
            if (categoryJobs > 0)
            {
                await _operationLogger.LogAsync(batchId, connection.storeId, "info",
                    $"Queued {categoryJobs} {(categoryJobs == 1 ? "category" : "categories")} for import before products.");
            }
        }
    }

    private async Task<(int Queued, int CategoryJobs)> QueueImportsAsync(
        MarketplaceConnection connection,
        int userId,
        IReadOnlyList<(string ExternalId, string? Name, string? ExternalCategoryId, string? ExternalCategoryName)> products,
        string batchId,
        CancellationToken ct)
    {
        await _quotaService.EnsureProductQuotaAsync(connection.storeId);
        var remainingSlots = await _quotaService.GetRemainingProductSlotsAsync(connection.storeId);

        var existing = await _db.pendingProductImports
            .AsNoTracking()
            .Where(p => p.connectionId == connection.Id
                && (p.status == ProductImportStatus.Pending || p.status == ProductImportStatus.InProgress))
            .Select(p => p.externalProductId)
            .ToListAsync(ct);
        var existingSet = existing.ToHashSet(StringComparer.Ordinal);

        var mappingRows = await _db.categoryMappings
            .AsNoTracking()
            .Where(m => m.connectionId == connection.Id)
            .Select(m => new { m.externalCategoryId, m.localCategoryId })
            .ToListAsync(ct);
        var mappingsByExternal = mappingRows
            .GroupBy(m => m.externalCategoryId)
            .ToDictionary(g => g.Key, g => g.First().localCategoryId);

        var enqueuedCategories = new HashSet<string>(StringComparer.Ordinal);
        var queued = 0;
        var categoryJobs = 0;
        foreach (var (externalId, name, externalCategoryId, externalCategoryName) in products)
        {
            if (string.IsNullOrWhiteSpace(externalId) || existingSet.Contains(externalId))
                continue;
            if (remainingSlots is int cap && queued >= cap)
                break;

            int? targetCategoryId = null;
            var extCatId = MarketplaceCategoryLabel.IsMappableId(externalCategoryId)
                ? externalCategoryId!.Trim()
                : null;

            if (extCatId != null)
            {
                if (mappingsByExternal.TryGetValue(extCatId, out var localId))
                {
                    targetCategoryId = localId;
                }
                else if (enqueuedCategories.Add(extCatId))
                {
                    var job = await _categoryImportQueue.EnqueueIfNeededAsync(
                        connection,
                        extCatId,
                        externalCategoryName,
                        externalCategoryName,
                        localCategoryId: null,
                        selectedFieldKeys: null,
                        batchId,
                        userId,
                        ct);
                    if (job != null)
                        categoryJobs++;
                }
            }

            _db.pendingProductImports.Add(new PendingProductImport
            {
                connectionId = connection.Id,
                externalProductId = externalId,
                targetCategoryId = targetCategoryId,
                externalCategoryId = extCatId,
                authorUserId = userId,
                storeId = connection.storeId,
                batchId = batchId,
                productName = name,
                status = ProductImportStatus.Pending,
                createdAt = DateTime.UtcNow,
            });
            existingSet.Add(externalId);
            queued++;
        }

        if (queued > 0)
            await _db.SaveChangesAsync(ct);

        return (queued, categoryJobs);
    }

    private async Task<Dictionary<string, ExternalProduct>> LoadRemotesByIdAsync(
        IMarketplaceAdapter adapter,
        MarketplaceConnection connection,
        IReadOnlyList<string> externalIds,
        string operationId,
        CancellationToken ct)
    {
        var map = new Dictionary<string, ExternalProduct>(StringComparer.Ordinal);
        if (externalIds.Count == 0)
            return map;

        await _operationLogger.LogAsync(operationId, connection.storeId, "info",
            $"Loading {externalIds.Count} marketplace cards by id…");

        try
        {
            var batch = await adapter.ImportProductsBatchAsync(connection, externalIds.ToList(), ct);
            foreach (var remote in batch)
            {
                if (!string.IsNullOrWhiteSpace(remote.ExternalId))
                    map[remote.ExternalId] = remote;
            }

            foreach (var id in externalIds)
            {
                if (map.ContainsKey(id))
                    continue;
                var hit = batch.FirstOrDefault(r =>
                    string.Equals(r.Sku, id, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(r.ExternalId, id, StringComparison.OrdinalIgnoreCase));
                if (hit != null)
                    map[id] = hit;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Catalog connect bulk card load failed for connection {ConnectionId}",
                connection.Id);
            await _operationLogger.LogAsync(operationId, connection.storeId, "warning",
                "Could not load marketplace cards in bulk; linking with the ids from the preview.");
        }

        await _operationLogger.LogAsync(operationId, connection.storeId, "info",
            $"Loaded {map.Count} of {externalIds.Count} marketplace cards.");
        return map;
    }

    private async Task<int> ImportLinkedAttributesAsync(
        MarketplaceConnection connection,
        IReadOnlyDictionary<string, ExternalProduct> remotesById,
        IReadOnlyList<(int ProductId, string ExternalId)> linkedPairs,
        string operationId,
        CancellationToken ct)
    {
        if (linkedPairs.Count == 0)
            return 0;

        await _operationLogger.LogAsync(operationId, connection.storeId, "info",
            "Copying marketplace fields into local attributes…");

        var imported = 0;
        var n = 0;
        foreach (var (productId, externalId) in linkedPairs)
        {
            n++;
            if (!remotesById.TryGetValue(externalId, out var remote))
                continue;

            var attrs = ImportedAttributeValue.Collect(remote);
            if (attrs.Count == 0)
                continue;

            var product = await _db.products.FirstOrDefaultAsync(
                p => p.Id == productId && p.storeId == connection.storeId, ct);
            if (product?.categoryId is not int categoryId)
                continue;

            imported += await _productSync.ImportProductAttributesAsync(
                productId, connection.Id, categoryId, attrs, ct);

            if (n == linkedPairs.Count || n % 10 == 0)
            {
                await _operationLogger.LogAsync(operationId, connection.storeId, "info",
                    $"Copied marketplace fields ({n} of {linkedPairs.Count} products).");
            }
        }

        return imported;
    }

    private async Task<(bool Ok, string? Error, bool MappingCreated)> TryLinkAsync(
        MarketplaceConnection connection,
        int productId,
        string rawExternalId,
        string? applyExternalCategoryId,
        string? applyExternalCategoryName,
        Dictionary<(int LocalCategoryId, string ExternalCategoryId), MappingEnsureResult> mappingCache,
        string operationId,
        ExternalProduct? resolved,
        CancellationToken ct)
    {
        if (await ProductNotTrashed.IsTrashedAsync(_db, productId, ct))
            return (false, "Product is in trash", false);

        var product = await _db.products
            .FirstOrDefaultAsync(p => p.Id == productId && p.storeId == connection.storeId, ct);
        if (product == null)
            return (false, "Product not found", false);

        var alreadyLinked = await _db.productMarketplaceLinks
            .AnyAsync(l => l.productId == productId && l.connectionId == connection.Id, ct);
        if (alreadyLinked)
            return (false, "Product is already linked to this connection", false);

        var canonicalExternalId = rawExternalId;
        string? externalSku = null;
        var remoteCategoryId = MarketplaceCategoryLabel.PreferMappableId(null, applyExternalCategoryId);
        string? remoteCategoryName = applyExternalCategoryName;
        if (resolved != null)
        {
            canonicalExternalId = resolved.ExternalId;
            externalSku = string.IsNullOrWhiteSpace(resolved.Sku) ? null : resolved.Sku;
            remoteCategoryId = MarketplaceCategoryLabel.PreferMappableId(
                resolved.CategoryId, remoteCategoryId);
            if (!string.IsNullOrWhiteSpace(resolved.CategoryName))
                remoteCategoryName = resolved.CategoryName;
        }

        var duplicateExternalId = await _db.productMarketplaceLinks
            .AnyAsync(l => l.externalProductId == canonicalExternalId && l.connectionId == connection.Id, ct);
        if (duplicateExternalId)
            return (false, $"External ID \"{canonicalExternalId}\" is already linked", false);

        var (categoryMappingId, mappingCreated) = await AttachMappingAsync(
            connection,
            product,
            remoteCategoryId,
            remoteCategoryName,
            mappingCache,
            operationId,
            ct);

        var link = new ProductMarketplaceLink();
        link.AddFill(new ProductMarketplaceLink.AddForm
        {
            productId = productId,
            connectionId = connection.Id,
            externalProductId = canonicalExternalId,
            externalSku = externalSku,
            status = ProductMarketplaceStatus.NotSynced,
            categoryMappingId = categoryMappingId,
        });
        _db.productMarketplaceLinks.Add(link);
        await _db.SaveChangesAsync(ct);
        return (true, null, mappingCreated);
    }

    private async Task<MappingRepairBatch> RepairLinksWithoutMappingAsync(
        MarketplaceConnection connection,
        IMarketplaceAdapter adapter,
        Dictionary<(int LocalCategoryId, string ExternalCategoryId), MappingEnsureResult> mappingCache,
        string operationId,
        CancellationToken ct)
    {
        var empty = new MappingRepairBatch();
        var links = await _db.productMarketplaceLinks
            .Include(l => l.product)
            .Where(l => l.connectionId == connection.Id && l.categoryMappingId == null)
            .ToListAsync(ct);
        if (links.Count == 0)
            return empty;

        var remotesById = await LoadRemotesByIdAsync(
            adapter,
            connection,
            links
                .Select(l => (l.externalProductId ?? "").Trim())
                .Where(id => id.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            operationId,
            ct);

        var created = 0;
        var pairs = new List<(int ProductId, string ExternalId)>();
        foreach (var link in links)
        {
            if (link.product == null || link.product.storeId != connection.storeId)
                continue;
            if (await ProductNotTrashed.IsTrashedAsync(_db, link.productId, ct))
                continue;

            remotesById.TryGetValue(link.externalProductId, out var remote);
            var remoteCategoryId = MarketplaceCategoryLabel.PreferMappableId(remote?.CategoryId, null);
            var remoteCategoryName = remote?.CategoryName;

            var (mappingId, mappingCreated) = await AttachMappingAsync(
                connection,
                link.product,
                remoteCategoryId,
                remoteCategoryName,
                mappingCache,
                operationId,
                ct);
            if (mappingId == null)
                continue;

            link.categoryMappingId = mappingId;
            if (mappingCreated)
                created++;
            await _db.SaveChangesAsync(ct);
            pairs.Add((link.productId, remote?.ExternalId ?? link.externalProductId));
        }

        if (created > 0)
        {
            await _operationLogger.LogAsync(operationId, connection.storeId, "info",
                $"Created {created} missing category mappings");
        }

        return new MappingRepairBatch
        {
            MappingsCreated = created,
            Remotes = remotesById,
            Pairs = pairs,
        };
    }

    private sealed class MappingRepairBatch
    {
        public int MappingsCreated { get; init; }
        public Dictionary<string, ExternalProduct> Remotes { get; init; } = new(StringComparer.Ordinal);
        public List<(int ProductId, string ExternalId)> Pairs { get; init; } = [];
    }

    private async Task<(int? MappingId, bool Created)> AttachMappingAsync(
        MarketplaceConnection connection,
        Product product,
        string? remoteCategoryId,
        string? remoteCategoryName,
        Dictionary<(int LocalCategoryId, string ExternalCategoryId), MappingEnsureResult> mappingCache,
        string operationId,
        CancellationToken ct)
    {
        if (!MarketplaceCategoryLabel.IsMappableId(remoteCategoryId))
        {
            if (product.categoryId is int localCat)
            {
                var mapping = await _db.categoryMappings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.connectionId == connection.Id
                        && m.localCategoryId == localCat, ct);
                return (mapping?.Id, false);
            }

            return (null, false);
        }

        var extId = remoteCategoryId!.Trim();
        var cacheKey = (product.categoryId ?? 0, extId);
        var created = false;
        if (!mappingCache.TryGetValue(cacheKey, out var cached))
        {
            cached = await EnsureMappingForLinkAsync(
                connection,
                product.categoryId,
                extId,
                remoteCategoryName,
                operationId,
                ct);
            mappingCache[cacheKey] = cached;
            created = cached.Created;
            if (cached.LocalCategoryId is int createdLocal && cacheKey.Item1 == 0)
                mappingCache[(createdLocal, extId)] = cached with { Created = false };
        }

        if (product.categoryId == null && cached.LocalCategoryId is int assignId)
            product.UpdateFill(new Product.UpdateForm { categoryId = assignId });

        return (cached.MappingId, created);
    }

    private async Task<MappingEnsureResult> EnsureMappingForLinkAsync(
        MarketplaceConnection connection,
        int? localCategoryId,
        string externalCategoryId,
        string? externalCategoryName,
        string operationId,
        CancellationToken ct)
    {
        var existingQuery = _db.categoryMappings
            .Where(m => m.connectionId == connection.Id
                && m.externalCategoryId == externalCategoryId);
        if (localCategoryId is int localId)
            existingQuery = existingQuery.Where(m => m.localCategoryId == localId);

        var existing = await existingQuery.FirstOrDefaultAsync(ct);
        if (existing != null)
        {
            var improved = MarketplaceCategoryLabel.FirstReal(
                externalCategoryId, externalCategoryName);
            if (improved != null
                && MarketplaceCategoryLabel.IsSynthetic(existing.externalCategoryName, externalCategoryId))
            {
                existing.externalCategoryName = StringClip.Clip(
                    improved, CategoryMapping.EXTERNAL_CATEGORY_NAME_MAX_LENGTH);
                if (string.IsNullOrWhiteSpace(existing.name)
                    || MarketplaceCategoryLabel.IsSynthetic(existing.name, externalCategoryId))
                {
                    existing.name = StringClip.ClipOptional(improved, CategoryMapping.NAME_MAX_LENGTH);
                }
                await _db.SaveChangesAsync(ct);
            }

            return new MappingEnsureResult(existing.Id, existing.localCategoryId, false);
        }

        try
        {
            var result = await _categoryImport.ImportExternalCategoryAsync(
                connection,
                externalCategoryId,
                localCategoryName: externalCategoryName,
                localCategoryId: localCategoryId,
                externalCategoryName: externalCategoryName,
                ct: ct);
            if (!result.IsSuccess || result.CategoryMappingId == null)
            {
                var error = result.ErrorMessage ?? "Category import failed";
                _logger.LogWarning(
                    "Catalog connect mapping import failed for {ExternalCategory}: {Error}",
                    externalCategoryId,
                    error);
                await _operationLogger.LogAsync(operationId, connection.storeId, "warning",
                    $"Could not create mapping for {externalCategoryId}: {error}");
                return new MappingEnsureResult(null, null, false);
            }

            await _operationLogger.LogAsync(operationId, connection.storeId, "info",
                $"Created mapping for {MarketplaceCategoryLabel.FirstReal(externalCategoryId, externalCategoryName) ?? externalCategoryId}.");

            return new MappingEnsureResult(result.CategoryMappingId, result.LocalCategoryId, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Catalog connect mapping import failed for {ExternalCategory}",
                externalCategoryId);
            await _operationLogger.LogAsync(operationId, connection.storeId, "warning",
                $"Could not create mapping for {externalCategoryId}: {ex.Message}");
            return new MappingEnsureResult(null, null, false);
        }
    }

    private async Task<(List<CatalogMatchRowDto> Rows, int AlreadyLinked)> PreviewFromRemoteAsync(
        MarketplaceConnection connection,
        IMarketplaceAdapter adapter,
        CancellationToken ct,
        string? operationId,
        Func<int, int?, string, Task>? onProgress)
    {
        var linkedIds = await LinkedExternalIdsAsync(connection.Id, ct);
        var remotes = new List<ExternalProduct>();
        await ScanUnlinkedRemotesAsync(
            adapter,
            connection,
            linkedIds,
            async (page, scanned, total, pageCt) =>
            {
                remotes.AddRange(page);
                if (onProgress != null)
                    await onProgress(scanned, total, "fromRemote");
                if (!string.IsNullOrEmpty(operationId))
                {
                    await _operationLogger.ProgressAsync(
                        operationId, connection.storeId, total ?? 0, scanned, 0);
                }
            },
            ct);

        var locals = await LoadLocalCandidatesAsync(connection.storeId, connection.Id, ct);

        var remoteKeys = remotes
            .Select(r => new CatalogMatchEngine.RemoteProduct(
                r.ExternalId,
                r.Sku,
                MarketplaceImportBarcode.Resolve(r),
                r.Name))
            .ToList();

        var byId = remotes.ToDictionary(r => r.ExternalId, StringComparer.Ordinal);
        var rows = CatalogMatchEngine.Match(locals, remoteKeys)
            .Select(row =>
            {
                var dto = ToDto(row);
                if (byId.TryGetValue(row.ExternalId, out var remote))
                {
                    dto.ExternalCategoryId = remote.CategoryId;
                    dto.ExternalCategoryName = remote.CategoryName;
                }

                return dto;
            })
            .ToList();

        return (rows, linkedIds.Count);
    }

    private async Task<(List<CatalogMatchRowDto> Rows, int AlreadyLinked)> PreviewFromLocalAsync(
        MarketplaceConnection connection,
        IMarketplaceAdapter adapter,
        CancellationToken ct,
        string? operationId,
        Func<int, int?, string, Task>? onProgress)
    {
        var locals = await LoadLocalCandidatesAsync(connection.storeId, connection.Id, ct);
        var linkedIds = await LinkedExternalIdsAsync(connection.Id, ct);
        var alreadyLinked = await _db.productMarketplaceLinks
            .AsNoTracking()
            .CountAsync(l => l.connectionId == connection.Id, ct);

        var hits = new List<(CatalogMatchEngine.LocalProduct Local, ExternalProduct Remote, string MatchedBy)>();
        var unmatched = new List<CatalogMatchEngine.LocalProduct>();
        var remaining = locals.ToList();
        var total = locals.Count;
        var processed = 0;

        async Task ReportAsync()
        {
            if (onProgress != null)
                await onProgress(processed, total, "fromLocal");
            if (!string.IsNullOrEmpty(operationId))
            {
                await _operationLogger.ProgressAsync(
                    operationId, connection.storeId, total, processed, 0);
            }
        }

        remaining = await MatchLocalsFromBatchAsync(
            adapter, connection, remaining, hits, linkedIds, "sku",
            local => CatalogMatchEngine.NormalizeSku(local.Sku) == null ? null : local.Sku,
            ct);
        processed = total - remaining.Count;
        await ReportAsync();

        remaining = await MatchLocalsFromBatchAsync(
            adapter, connection, remaining, hits, linkedIds, "barcode",
            local => MarketplaceImportBarcode.NormalizeIfValid(local.Barcode) == null
                ? null
                : local.Barcode,
            ct);
        processed = total - remaining.Count;
        await ReportAsync();

        using var gate = new SemaphoreSlim(LookupConcurrency);
        var lookups = remaining.Select(async local =>
        {
            await gate.WaitAsync(ct);
            try
            {
                return (local, await LookupRemoteForLocalAsync(adapter, connection, local, ct));
            }
            finally
            {
                gate.Release();
            }
        });

        foreach (var (local, hit) in await Task.WhenAll(lookups))
        {
            processed++;
            if (hit.Remote == null || linkedIds.Contains(hit.Remote.ExternalId))
            {
                unmatched.Add(local);
            }
            else
            {
                hits.Add((local, hit.Remote, hit.MatchedBy!));
            }

            if (processed == total || processed % 10 == 0)
                await ReportAsync();
        }

        await ReportAsync();

        var rows = new List<CatalogMatchRowDto>();
        foreach (var group in hits.GroupBy(h => h.Remote.ExternalId, StringComparer.Ordinal))
        {
            var list = group.ToList();
            if (list.Count == 1)
            {
                rows.Add(ToLocalHitDto(list[0].Local, list[0].Remote, "matched", list[0].MatchedBy, list));
                continue;
            }

            foreach (var hit in list)
                rows.Add(ToLocalHitDto(hit.Local, hit.Remote, "ambiguous", hit.MatchedBy, list));
        }

        foreach (var local in unmatched)
            rows.Add(ToUnmatchedLocalDto(local));

        return (rows, alreadyLinked);
    }

    private async Task<List<CatalogMatchEngine.LocalProduct>> MatchLocalsFromBatchAsync(
        IMarketplaceAdapter adapter,
        MarketplaceConnection connection,
        List<CatalogMatchEngine.LocalProduct> remaining,
        List<(CatalogMatchEngine.LocalProduct Local, ExternalProduct Remote, string MatchedBy)> hits,
        HashSet<string> linkedIds,
        string matchedBy,
        Func<CatalogMatchEngine.LocalProduct, string?> keySelector,
        CancellationToken ct)
    {
        if (remaining.Count == 0)
            return remaining;

        var keys = remaining
            .Select(keySelector)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (keys.Count == 0)
            return remaining;

        var remotes = new List<ExternalProduct>();
        for (var i = 0; i < keys.Count; i += LocalLookupBatchSize)
        {
            var chunk = keys.Skip(i).Take(LocalLookupBatchSize).ToList();
            try
            {
                var batch = await adapter.ImportProductsBatchAsync(connection, chunk, ct);
                if (batch.Count > 0)
                    remotes.AddRange(batch);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Catalog connect bulk lookup failed for {Count} keys", chunk.Count);
            }
        }

        if (remotes.Count == 0)
            return remaining;

        var still = new List<CatalogMatchEngine.LocalProduct>();
        var usedRemoteIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var local in remaining)
        {
            var key = keySelector(local);
            if (string.IsNullOrWhiteSpace(key))
            {
                still.Add(local);
                continue;
            }

            var remote = remotes.FirstOrDefault(r => AcceptsLookup(r, key)
                && !linkedIds.Contains(r.ExternalId)
                && !usedRemoteIds.Contains(r.ExternalId));
            if (remote == null)
            {
                still.Add(local);
                continue;
            }

            usedRemoteIds.Add(remote.ExternalId);
            hits.Add((local, remote, matchedBy));
        }

        return still;
    }

    private async Task<(ExternalProduct? Remote, string? MatchedBy)> LookupRemoteForLocalAsync(
        IMarketplaceAdapter adapter,
        MarketplaceConnection connection,
        CatalogMatchEngine.LocalProduct local,
        CancellationToken ct)
    {
        var sku = CatalogMatchEngine.NormalizeSku(local.Sku);
        if (sku != null)
        {
            var remote = await TryImportProductAsync(adapter, connection, local.Sku!, ct);
            if (remote != null && AcceptsLookup(remote, local.Sku!))
                return (remote, "sku");
        }

        var barcode = MarketplaceImportBarcode.NormalizeIfValid(local.Barcode);
        if (barcode != null)
        {
            var remote = await TryImportProductAsync(adapter, connection, local.Barcode!, ct);
            if (remote != null && AcceptsLookup(remote, local.Barcode!))
                return (remote, "barcode");
        }

        return (null, null);
    }

    private async Task<ExternalProduct?> TryImportProductAsync(
        IMarketplaceAdapter adapter,
        MarketplaceConnection connection,
        string key,
        CancellationToken ct)
    {
        try
        {
            return await adapter.ImportProductAsync(connection, key.Trim(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Catalog connect lookup failed for {Key}", key);
            return null;
        }
    }

    private static bool AcceptsLookup(ExternalProduct remote, string key)
    {
        var trimmed = key.Trim();
        if (string.Equals(remote.ExternalId, trimmed, StringComparison.Ordinal))
            return true;

        var sku = CatalogMatchEngine.NormalizeSku(remote.Sku);
        if (sku != null && sku == CatalogMatchEngine.NormalizeSku(trimmed))
            return true;

        var remoteBarcode = MarketplaceImportBarcode.NormalizeIfValid(remote.Barcode)
            ?? MarketplaceImportBarcode.NormalizeIfValid(MarketplaceImportBarcode.Resolve(remote));
        var keyBarcode = MarketplaceImportBarcode.NormalizeIfValid(trimmed);
        return remoteBarcode != null && keyBarcode != null && remoteBarcode == keyBarcode;
    }

    private static CatalogMatchRowDto ToLocalHitDto(
        CatalogMatchEngine.LocalProduct local,
        ExternalProduct remote,
        string status,
        string matchedBy,
        List<(CatalogMatchEngine.LocalProduct Local, ExternalProduct Remote, string MatchedBy)> group)
    {
        return new CatalogMatchRowDto
        {
            RowKey = $"{remote.ExternalId}:{local.ProductId}",
            ExternalProductId = remote.ExternalId,
            Name = remote.Name,
            Sku = remote.Sku,
            Barcode = MarketplaceImportBarcode.Resolve(remote) ?? remote.Barcode,
            ExternalCategoryId = remote.CategoryId,
            ExternalCategoryName = remote.CategoryName,
            Status = status,
            MatchedBy = matchedBy,
            ProductId = status == "matched" ? local.ProductId : null,
            ProductName = local.Name,
            ProductSku = local.Sku,
            Candidates = group
                .Select(h => new CatalogMatchCandidateDto
                {
                    ProductId = h.Local.ProductId,
                    Sku = h.Local.Sku,
                    Name = h.Local.Name,
                })
                .ToList(),
        };
    }

    private static CatalogMatchRowDto ToUnmatchedLocalDto(CatalogMatchEngine.LocalProduct local)
    {
        return new CatalogMatchRowDto
        {
            RowKey = $"local:{local.ProductId}",
            ExternalProductId = "",
            Name = local.Name,
            Sku = local.Sku,
            Barcode = local.Barcode,
            Status = "unmatched",
            ProductId = local.ProductId,
            ProductName = local.Name,
            ProductSku = local.Sku,
        };
    }

    private async Task<int> CountUnlinkedLocalsAsync(int storeId, int connectionId, CancellationToken ct)
    {
        var linkedProductIds = await _db.productMarketplaceLinks
            .AsNoTracking()
            .Where(l => l.connectionId == connectionId)
            .Select(l => l.productId)
            .ToListAsync(ct);

        var query = _db.products.AsNoTracking().Where(p => p.storeId == storeId);
        if (linkedProductIds.Count > 0)
            query = query.Where(p => !linkedProductIds.Contains(p.Id));
        return await query.CountAsync(ct);
    }

    private async Task ScanUnlinkedRemotesAsync(
        IMarketplaceAdapter adapter,
        MarketplaceConnection connection,
        HashSet<string> linkedIds,
        Func<IReadOnlyList<ExternalProduct>, int, int?, CancellationToken, Task> onPage,
        CancellationToken ct)
    {
        var scanned = 0;
        int? total = null;
        string? cursor = null;
        while (scanned < MaxRemoteProducts)
        {
            var page = await adapter.ImportAllProductsAsync(
                connection, RemotePageSize, cursor, null, ct);
            if (page.TotalCount is int known && known > 0)
                total = known;

            var unlinked = new List<ExternalProduct>();
            foreach (var item in page.Items)
            {
                scanned++;
                if (linkedIds.Contains(item.ExternalId))
                    continue;
                unlinked.Add(item);
                if (scanned >= MaxRemoteProducts)
                    break;
            }

            await onPage(unlinked, Math.Min(scanned, total ?? scanned), total, ct);

            if (scanned >= MaxRemoteProducts || !page.HasMore || string.IsNullOrEmpty(page.NextCursor))
                break;
            cursor = page.NextCursor;
        }
    }

    private async Task<List<CatalogMatchEngine.LocalProduct>> LoadLocalCandidatesAsync(
        int storeId,
        int connectionId,
        CancellationToken ct)
    {
        var linkedProductIds = await _db.productMarketplaceLinks
            .AsNoTracking()
            .Where(l => l.connectionId == connectionId)
            .Select(l => l.productId)
            .ToListAsync(ct);

        var query = _db.products.AsNoTracking().Where(p => p.storeId == storeId);
        if (linkedProductIds.Count > 0)
            query = query.Where(p => !linkedProductIds.Contains(p.Id));

        return await query
            .Select(p => new CatalogMatchEngine.LocalProduct(p.Id, p.sku, p.barcode, p.name))
            .ToListAsync(ct);
    }

    private async Task<HashSet<string>> LinkedExternalIdsAsync(int connectionId, CancellationToken ct)
    {
        var ids = await _db.productMarketplaceLinks
            .AsNoTracking()
            .Where(l => l.connectionId == connectionId)
            .Select(l => l.externalProductId)
            .ToListAsync(ct);
        return ids.ToHashSet(StringComparer.Ordinal);
    }

    private IMarketplaceAdapter RequireImportAdapter(MarketplaceConnection connection)
    {
        var adapter = _adapterRegistry.GetByKey(connection.adapterKey)
            ?? throw ApiException.BadRequest($"Unknown adapter: {connection.adapterKey}");
        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportProduct)
            && !adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportAllProducts))
        {
            throw ApiException.BadRequest("Adapter does not support importing products");
        }

        return adapter;
    }

    private IMarketplaceAdapter RequireImportAllAdapter(MarketplaceConnection connection)
    {
        var adapter = RequireImportAdapter(connection);
        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportAllProducts))
            throw ApiException.BadRequest("Adapter does not support fetching the product list");
        return adapter;
    }

    private static CatalogMatchRowDto ToDto(CatalogMatchEngine.MatchRow row)
    {
        return new CatalogMatchRowDto
        {
            RowKey = row.ExternalId,
            ExternalProductId = row.ExternalId,
            Name = row.Name,
            Sku = row.Sku,
            Barcode = row.Barcode,
            Status = row.Status switch
            {
                CatalogMatchStatus.Matched => "matched",
                CatalogMatchStatus.Ambiguous => "ambiguous",
                _ => "unmatched",
            },
            MatchedBy = row.MatchedBy,
            ProductId = row.ProductId,
            ProductName = row.ProductName,
            ProductSku = row.ProductSku,
            Candidates = row.Candidates
                .Select(c => new CatalogMatchCandidateDto
                {
                    ProductId = c.ProductId,
                    Sku = c.Sku,
                    Name = c.Name,
                })
                .ToList(),
        };
    }
}
