using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Delegat;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services;

namespace Delegat.Services.Marketplace;

public sealed class CatalogScrapeHitDto
{
    public string ExternalId { get; init; } = "";
    public string? Name { get; init; }
    public string? Brand { get; init; }
    public string? Seller { get; init; }
    public decimal? Price { get; init; }
    public string? ThumbnailUrl { get; init; }
    public string? CategoryName { get; init; }
}

public sealed class CatalogScrapeSearchResult
{
    public List<CatalogScrapeHitDto> Items { get; init; } = [];
    public bool HasMore { get; init; }
    public string? NextCursor { get; init; }
    public int? TotalCount { get; init; }
}

public sealed class CatalogScrapeApplyResult
{
    public string OperationId { get; init; } = "";
    public int Imported { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
    public int CategoryJobs { get; init; }
    public bool QuotaStopped { get; init; }
}

/// <summary>
/// Search a public storefront and copy selected cards into the local catalog
/// without ProductMarketplaceLink rows.
/// </summary>
public sealed class CatalogScrapeService
{
    public const int DefaultPageSize = 50;

    internal TimeSpan CategoryWaitPoll { get; set; } = TimeSpan.FromMilliseconds(400);
    internal TimeSpan CategoryWaitTimeout { get; set; } = TimeSpan.FromMinutes(5);
    internal Func<Task>? OnCategoryWaitTick { get; set; }

    private readonly DataContext _context;
    private readonly IMarketplaceAdapterRegistry _adapterRegistry;
    private readonly ProductSyncService _productSync;
    private readonly CategoryImportQueueService _categoryQueue;
    private readonly IOperationLogger _operationLogger;
    private readonly QuotaService _quotaService;

    public CatalogScrapeService(
        DataContext context,
        IMarketplaceAdapterRegistry adapterRegistry,
        ProductSyncService productSync,
        CategoryImportQueueService categoryQueue,
        IOperationLogger operationLogger,
        QuotaService quotaService)
    {
        _context = context;
        _adapterRegistry = adapterRegistry;
        _productSync = productSync;
        _categoryQueue = categoryQueue;
        _operationLogger = operationLogger;
        _quotaService = quotaService;
    }

    public async Task<CatalogScrapeSearchResult> SearchAsync(
        MarketplaceConnection connection,
        string? query,
        int pageSize,
        string? cursor,
        CancellationToken ct)
    {
        var adapter = RequireAdapter(connection.adapterKey);
        pageSize = Math.Clamp(pageSize <= 0 ? DefaultPageSize : pageSize, 1, DefaultPageSize);
        var parsed = PublicCatalogQuery.Parse(query);

        if (parsed.Kind == PublicCatalogQueryKind.ProductIds)
        {
            var items = new List<CatalogScrapeHitDto>();
            foreach (var id in parsed.ProductIds)
            {
                var product = await MarketplaceStorefrontError.GuardAsync(
                    () => adapter.GetPublicProductAsync(connection, id, ct));
                if (product != null)
                    items.Add(MapHit(product));
            }

            return new CatalogScrapeSearchResult
            {
                Items = items,
                HasMore = false,
                TotalCount = items.Count
            };
        }

        var result = await MarketplaceStorefrontError.GuardAsync(
            () => adapter.SearchPublicCatalogAsync(
                connection, parsed.Text, pageSize, cursor, ct));
        return new CatalogScrapeSearchResult
        {
            Items = result.Items.Select(MapHit).ToList(),
            HasMore = result.HasMore,
            NextCursor = result.NextCursor,
            TotalCount = result.TotalCount
        };
    }

    public async Task<CatalogScrapeApplyResult> ApplyAsync(
        MarketplaceConnection connection,
        IReadOnlyList<string> publicIds,
        int userId,
        CancellationToken ct,
        string? operationId = null)
    {
        var adapter = RequireAdapter(connection.adapterKey);
        var ids = publicIds
            .Select(id => (id ?? "").Trim())
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (ids.Count == 0)
            throw ApiException.BadRequest("No public product ids to import", code: "required_fields_missing");

        var clientStarted = !string.IsNullOrWhiteSpace(operationId);
        operationId = clientStarted ? operationId!.Trim() : Guid.NewGuid().ToString("N");
        if (!clientStarted)
        {
            await _operationLogger.StartAsync(
                operationId,
                connection.storeId,
                userId,
                "catalog-scrape",
                "Copy public catalog cards",
                connectionId: connection.Id > 0 ? connection.Id : null,
                connectionName: connection.name,
                batchId: operationId,
                batchType: "catalog-scrape",
                totalItems: ids.Count);
        }

        var imported = 0;
        var failed = 0;
        var skipped = 0;
        var categoryJobs = 0;
        var quotaStopped = false;
        var seenCategories = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            for (var i = 0; i < ids.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (await OperationLifecycle.IsStopRequestedAsync(_context, operationId, ct))
                {
                    skipped += ids.Count - i;
                    await _operationLogger.FinishAsync(
                        operationId, connection.storeId, "cancelled",
                        $"Stopped. Imported {imported}, skipped {skipped}, failed {failed}");
                    break;
                }
                var publicId = ids[i];
                try
                {
                    await _quotaService.EnsureProductQuotaAsync(connection.storeId);
                }
                catch (ApiException ex) when (ex.Code == "product_limit_reached")
                {
                    quotaStopped = true;
                    skipped += ids.Count - i;
                    await _operationLogger.LogAsync(
                        operationId, connection.storeId, "warning",
                        $"Product limit reached. Skipped {skipped} remaining cards.");
                    break;
                }

                await _operationLogger.LogAsync(
                    operationId, connection.storeId, "info", $"Loading public card {publicId}...");
                ExternalProduct? external;
                try
                {
                    external = await MarketplaceStorefrontError.GuardAsync(
                        () => adapter.GetPublicProductAsync(connection, publicId, ct));
                }
                catch (ApiException ex) when (ex.StatusCode == StatusCodes.Status429TooManyRequests)
                {
                    skipped += ids.Count - i;
                    await _operationLogger.LogAsync(
                        operationId, connection.storeId, "warning",
                        MarketplaceStorefrontError.RateLimitedMessage);
                    if (imported == 0)
                    {
                        await _operationLogger.FinishAsync(
                            operationId, connection.storeId, "failed", ex.Message);
                        throw;
                    }

                    break;
                }
                if (external == null)
                {
                    failed++;
                    await _operationLogger.LogAsync(
                        operationId, connection.storeId, "error", $"Public card {publicId} was not found");
                    await _operationLogger.ProgressAsync(
                        operationId, connection.storeId, ids.Count, imported, failed);
                    continue;
                }

                int? targetCategoryId = null;
                var extCat = (external.CategoryId ?? "").Trim();
                if (connection.Id > 0 && extCat.Length > 0)
                {
                    targetCategoryId = await LookupLocalCategoryIdAsync(connection.Id, extCat, ct);

                    if (seenCategories.Add(extCat))
                    {
                        var job = await _categoryQueue.EnqueueIfNeededAsync(
                            connection,
                            extCat,
                            external.CategoryName,
                            external.CategoryName,
                            localCategoryId: null,
                            selectedFieldKeys: null,
                            operationId,
                            userId,
                            ct);
                        if (job != null)
                            categoryJobs++;
                    }

                    if (targetCategoryId == null)
                    {
                        await _operationLogger.LogAsync(
                            operationId, connection.storeId, "info",
                            $"Waiting for category {external.CategoryName ?? extCat}...");
                        targetCategoryId = await WaitForLocalCategoryAsync(
                            connection.Id, extCat, operationId, ct);
                    }
                }

                var result = await _productSync.ImportUnlinkedExternalProductAsync(
                    connection, external, targetCategoryId, userId, ct, operationId);
                if (result.IsSuccess)
                {
                    imported++;
                }
                else if (result.ErrorCode == "product_limit_reached")
                {
                    quotaStopped = true;
                    skipped += ids.Count - i;
                    await _operationLogger.LogAsync(
                        operationId, connection.storeId, "warning",
                        $"Product limit reached. Skipped {skipped} remaining cards.");
                    break;
                }
                else
                {
                    failed++;
                    await _operationLogger.LogAsync(
                        operationId, connection.storeId, "error",
                        result.ErrorMessage ?? $"Failed to copy {publicId}");
                }

                await _operationLogger.ProgressAsync(
                    operationId, connection.storeId, ids.Count, imported, failed);
            }

            var status = quotaStopped || failed > 0
                ? (imported > 0 ? "partial" : (quotaStopped ? "failed" : "failed"))
                : "completed";
            await _operationLogger.FinishAsync(
                operationId,
                connection.storeId,
                status,
                $"Imported {imported}, skipped {skipped}, failed {failed}, category jobs {categoryJobs}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await _operationLogger.FinishAsync(operationId, connection.storeId, "cancelled", "Stopped");
            return new CatalogScrapeApplyResult
            {
                OperationId = operationId,
                Imported = imported,
                Failed = failed,
                Skipped = skipped,
                CategoryJobs = categoryJobs,
                QuotaStopped = quotaStopped,
            };
        }
        catch (Exception ex)
        {
            await _operationLogger.LogAsync(operationId, connection.storeId, "error", ex.Message);
            await _operationLogger.FinishAsync(operationId, connection.storeId, "failed", ex.Message);
            throw;
        }

        return new CatalogScrapeApplyResult
        {
            OperationId = operationId,
            Imported = imported,
            Failed = failed,
            Skipped = skipped,
            CategoryJobs = categoryJobs,
            QuotaStopped = quotaStopped,
        };
    }

    public async Task<CatalogScrapeSearchResult> SearchAsync(
        int storeId,
        string adapterKey,
        string? query,
        int pageSize,
        string? cursor,
        CancellationToken ct)
    {
        var handle = await ResolveHandleAsync(storeId, adapterKey, ct);
        return await SearchAsync(handle, query, pageSize, cursor, ct);
    }

    public async Task<CatalogScrapeApplyResult> ApplyAsync(
        int storeId,
        string adapterKey,
        IReadOnlyList<string> publicIds,
        int userId,
        CancellationToken ct,
        string? operationId = null)
    {
        var handle = await ResolveHandleAsync(storeId, adapterKey, ct);
        return await ApplyAsync(handle, publicIds, userId, ct, operationId);
    }

    private async Task<MarketplaceConnection> ResolveHandleAsync(
        int storeId, string adapterKey, CancellationToken ct)
    {
        var key = NormalizeAdapterKey(adapterKey);
        RequireAdapter(key);
        var existing = await _context.marketplaceConnections
            .AsNoTracking()
            .Where(c => c.storeId == storeId && c.adapterKey == key)
            .OrderByDescending(c => c.isActive)
            .ThenBy(c => c.Id)
            .FirstOrDefaultAsync(ct);
        if (existing != null)
            return existing;

        var adapter = _adapterRegistry.GetByKey(key)!;
        return new MarketplaceConnection
        {
            Id = 0,
            storeId = storeId,
            adapterKey = key,
            name = adapter.DisplayName,
            isActive = true,
        };
    }

    private IMarketplaceAdapter RequireAdapter(string adapterKey)
    {
        var key = NormalizeAdapterKey(adapterKey);
        var adapter = _adapterRegistry.GetByKey(key)
            ?? throw ApiException.BadRequest($"Unknown adapter: {key}", code: "unknown_adapter");
        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.SearchPublicCatalog))
        {
            throw ApiException.BadRequest(
                "This adapter cannot search a public storefront",
                code: "capability_not_supported");
        }

        return adapter;
    }

    internal static string NormalizeAdapterKey(string? raw)
    {
        var key = (raw ?? "").Trim().ToLowerInvariant();
        if (key.Length is < 1 or > MarketplaceConnection.ADAPTER_KEY_MAX_LENGTH)
            throw ApiException.BadRequest("Adapter is required", code: "required_fields_missing");
        foreach (var ch in key)
        {
            if (ch is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')
                continue;
            throw ApiException.BadRequest("Unknown adapter", code: "unknown_adapter");
        }

        return key;
    }

    private Task<int?> LookupLocalCategoryIdAsync(
        int connectionId, string externalCategoryId, CancellationToken ct)
        => _context.categoryMappings.AsNoTracking()
            .Where(m => m.connectionId == connectionId && m.externalCategoryId == externalCategoryId)
            .Select(m => (int?)m.localCategoryId)
            .FirstOrDefaultAsync(ct);

    private async Task<int?> WaitForLocalCategoryAsync(
        int connectionId, string externalCategoryId, string operationId, CancellationToken ct)
    {
        if (CategoryWaitTimeout <= TimeSpan.Zero)
            return await LookupLocalCategoryIdAsync(connectionId, externalCategoryId, ct);

        var deadline = DateTime.UtcNow + CategoryWaitTimeout;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (await OperationLifecycle.IsStopRequestedAsync(_context, operationId, ct))
                throw new OperationCanceledException("Catalog scrape stopped");
            var localId = await LookupLocalCategoryIdAsync(connectionId, externalCategoryId, ct);
            if (localId != null)
                return localId;

            var job = await _context.pendingCategoryImports.AsNoTracking()
                .Where(j => j.connectionId == connectionId && j.externalCategoryId == externalCategoryId)
                .OrderByDescending(j => j.Id)
                .FirstOrDefaultAsync(ct);
            if (job == null)
                return null;
            if (job.status == CategoryImportStatus.Completed && job.resultLocalCategoryId != null)
                return job.resultLocalCategoryId;
            if (job.status == CategoryImportStatus.Cancelled)
                return null;
            if (job.status == CategoryImportStatus.Failed
                && job.retryCount >= PendingCategoryImport.MAX_RETRIES)
                return null;
            if (DateTime.UtcNow >= deadline)
                return null;

            if (OnCategoryWaitTick != null)
                await OnCategoryWaitTick();
            else
                await Task.Delay(CategoryWaitPoll, ct);
        }
    }

    private static CatalogScrapeHitDto MapHit(ExternalProduct product)
    {
        var seller = RawString(product.RawData, "supplier")
            ?? RawString(product.RawData, "seller")
            ?? RawString(product.RawData, "shop");

        return new CatalogScrapeHitDto
        {
            ExternalId = product.ExternalId,
            Name = product.Name,
            Brand = product.Brand,
            Seller = seller,
            Price = product.Price,
            ThumbnailUrl = product.ImageUrls?.FirstOrDefault(),
            CategoryName = product.CategoryName,
        };
    }

    private static string? RawString(Dictionary<string, object>? raw, string key)
    {
        if (raw == null || !raw.TryGetValue(key, out var value) || value == null)
            return null;
        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
