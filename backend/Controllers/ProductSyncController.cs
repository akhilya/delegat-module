using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Enums;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Security;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Products;
using Delegat.Services;
using Delegat.Services.Ai.Agent;
using Delegat.Services.Marketplace;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
public class ProductSyncController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceProvider _serviceProvider;
    private readonly IMarketplaceAdapterRegistry _adapterRegistry;
    private readonly ProductSyncService _productSyncService;
    private readonly PriceSyncService _priceSyncService;
    private readonly DataContext _context;
    private readonly ILogger<ProductSyncController> _logger;
    private readonly IOperationLogger _operationLogger;
    private readonly CategoryImportQueueService _categoryImportQueue;
    private readonly QuotaService _quotaService;
    private readonly EncryptionService? _encryptionService;
    private readonly LinkVerifyQueue _linkVerifyQueue;
    private readonly SqlReadonlyService? _catalogFilter;

    public ProductSyncController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider,
        IMarketplaceAdapterRegistry adapterRegistry,
        ProductSyncService productSyncService,
        PriceSyncService priceSyncService,
        DataContext context,
        ILogger<ProductSyncController> logger,
        IOperationLogger operationLogger,
        CategoryImportQueueService categoryImportQueue,
        QuotaService quotaService,
        LinkVerifyQueue linkVerifyQueue,
        EncryptionService? encryptionService = null,
        SqlReadonlyService? catalogFilter = null)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
        _adapterRegistry = adapterRegistry;
        _productSyncService = productSyncService;
        _priceSyncService = priceSyncService;
        _context = context;
        _logger = logger;
        _operationLogger = operationLogger;
        _categoryImportQueue = categoryImportQueue;
        _quotaService = quotaService;
        _linkVerifyQueue = linkVerifyQueue;
        _encryptionService = encryptionService;
        _catalogFilter = catalogFilter;
    }

    #region Product Links

    /// <summary>
    /// Get product-marketplace links
    /// </summary>
    [HttpGet]
    [Route("/marketplace/connections/{connectionId}/product-links")]
    public async Task<IActionResult> GetProductLinks(
        int connectionId,
        int startIndex = 0,
        int pageSize = 50,
        string? status = null)
    {
        var connection = await GetConnectionAsync(connectionId);

        var repo = _unitOfWork.Repository<ProductMarketplaceLink>();

        Expression<Func<ProductMarketplaceLink, bool>> predicate;
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<ProductMarketplaceStatus>(status, out var statusFilter))
        {
            predicate = l => l.connectionId == connectionId && l.status == statusFilter;
        }
        else
        {
            predicate = l => l.connectionId == connectionId;
        }

        var (items, total) = await repo.FindPagedAsync(
            predicate,
            query => query.Include(l => l.product),
            q => q.OrderByDescending(l => l.Id),
            startIndex,
            pageSize);

        var paged = items
            .Select(l => new
            {
                id = l.Id,
                productId = l.productId,
                productName = l.product?.name,
                productSku = l.product?.sku,
                externalProductId = l.externalProductId,
                externalSku = l.externalSku,
                status = l.status.ToString(),
                lastSyncAt = l.lastSyncAt,
                lastErrorMessage = l.lastErrorMessage,
                localPrice = l.product?.price,
                externalPrice = l.externalPrice,
                localStock = l.product?.storageQuantity,
                externalStock = l.externalStock
            })
            .ToList();

        return Ok(new
        {
            items = paged,
            totalCount = total,
            lastPage = startIndex + pageSize >= total
        });
    }

    #endregion

    #region Local Products for Export

    /// <summary>
    /// Get local products available for export (not yet linked to this connection)
    /// </summary>
    [HttpGet]
    [Route("/marketplace/connections/{connectionId}/local-products-for-export")]
    public async Task<IActionResult> GetLocalProductsForExport(
        int connectionId,
        string? searchQuery = null,
        int startIndex = 0,
        int pageSize = 50)
    {
        var connection = await GetConnectionAsync(connectionId);

        // Get local category IDs that have mappings for this connection
        var mappedLocalCategoryIds = await _context.categoryMappings
            .AsNoTracking()
            .Where(m => m.connectionId == connectionId)
            .Select(m => m.localCategoryId)
            .Distinct()
            .ToListAsync();

        if (mappedLocalCategoryIds.Count == 0)
            return Ok(new { items = Array.Empty<object>(), totalCount = 0, reason = "no_mappings" });

        // Get product IDs already successfully linked to this connection
        // (exclude NotSynced/SyncError - those need to be re-exported)
        var linkedProductIds = await _context.productMarketplaceLinks
            .AsNoTracking()
            .Where(l => l.connectionId == connectionId
                && (l.status == ProductMarketplaceStatus.Synced
                    || l.status == ProductMarketplaceStatus.Modified))
            .Select(l => l.productId)
            .ToListAsync();

        // Query local products: in mapped categories, not yet successfully linked
        var query = _context.products
            .AsNoTracking()
            .Include(p => p.category)
            .Where(p => p.storeId == connection.storeId
                && p.categoryId.HasValue
                && mappedLocalCategoryIds.Contains(p.categoryId.Value)
                && !linkedProductIds.Contains(p.Id));

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var search = searchQuery.Trim().ToLower();
            query = query.Where(p =>
                p.name.ToLower().Contains(search) ||
                p.sku.ToLower().Contains(search));
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderBy(p => p.name)
            .Skip(startIndex)
            .Take(pageSize)
            .Select(p => new
            {
                id = p.Id,
                name = p.name,
                sku = p.sku,
                price = p.price,
                storageQuantity = p.storageQuantity,
                categoryName = p.category != null ? p.category.name : null,
                categoryId = p.categoryId
            })
            .ToListAsync();

        return Ok(new { items, totalCount });
    }

    #endregion

    #region Export

    public record ExportProductInput(string? OperationId = null);

    /// <summary>
    /// Export a product to the marketplace
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/products/{productId}/export")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> ExportProduct(
        int connectionId,
        int productId,
        [FromBody] ExportProductInput? input,
        CancellationToken ct)
    {
        var connection = await GetConnectionAsync(connectionId);
        await ProductNotTrashed.EnsureAsync(_context, productId, ct);
        var userId = _currentUserService.GetUserId() ?? 0;
        var storeId = connection.storeId;
        var operationId = input?.OperationId ?? Guid.NewGuid().ToString("N");

        var opExists = !string.IsNullOrEmpty(input?.OperationId) &&
            await _context.operations.AsNoTracking().AnyAsync(o => o.operationId == operationId, ct);

        if (!opExists)
            await _operationLogger.StartAsync(operationId, storeId, userId, "single-export",
                $"Export product #{productId}", connectionId: connectionId, connectionName: connection.name);

        var result = await _productSyncService.ExportProductAsync(
            productId, connectionId, ct,
            operationId: operationId, operationStoreId: storeId);

        await _operationLogger.FinishAsync(operationId, storeId,
            result.IsSuccess ? "completed" : "failed",
            result.IsSuccess ? result.Message : result.ErrorMessage);

        if (!result.IsSuccess)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Export failed", code: "export_failed");

        return Ok(new
        {
            operationId,
            linkId = result.LinkId,
            externalProductId = result.ExternalProductId,
            isNewProduct = result.IsNewProduct,
            message = result.Message
        });
    }

    /// <summary>
    /// Manually link a product to an existing marketplace product by external ID.
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/products/{productId}/link")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> LinkProduct(
        int connectionId,
        int productId,
        [FromBody] LinkProductInput input,
        CancellationToken ct)
    {
        var connection = await GetConnectionAsync(connectionId);
        await ProductNotTrashed.EnsureAsync(_context, productId, ct);

        if (string.IsNullOrWhiteSpace(input.externalProductId) || input.externalProductId.Length > 100)
            throw ApiException.BadRequest("externalProductId is required (max 100 characters)", code: "required_fields_missing");

        // Check product exists and belongs to the same store
        var product = await _context.products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId && p.storeId == connection.storeId, ct);
        if (product == null)
            throw ApiException.NotFound("Product not found", code: "product_not_found");

        // Check no existing link
        var alreadyLinked = await _context.productMarketplaceLinks
            .AnyAsync(l => l.productId == productId && l.connectionId == connectionId, ct);
        if (alreadyLinked)
            throw ApiException.BadRequest("Product is already linked to this connection");

        var rawExternalId = input.externalProductId.Trim();
        var canonicalExternalId = rawExternalId;
        string? externalSku = null;

        // Resolve product_id / sku / offer_id via the adapter when import is supported (Ozon normalize).
        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter != null && adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportProduct))
        {
            var remote = await adapter.ImportProductAsync(connection, rawExternalId, ct);
            if (remote == null)
                throw ApiException.BadRequest(
                    $"Product '{rawExternalId}' was not found on the marketplace (tried as external id / sku / article).",
                    code: "remote_product_missing");
            canonicalExternalId = remote.ExternalId;
            externalSku = string.IsNullOrWhiteSpace(remote.Sku) ? null : remote.Sku;
        }

        // Check if externalProductId is already used by another product on this connection
        var duplicateExternalId = await _context.productMarketplaceLinks
            .AnyAsync(l => l.externalProductId == canonicalExternalId
                && l.connectionId == connectionId, ct);
        if (duplicateExternalId)
            throw ApiException.BadRequest(
                $"External ID \"{canonicalExternalId}\" is already linked to another product on this connection");

        // Resolve category mapping (optional - same logic as available-connections)
        int? categoryMappingId = null;
        if (product.categoryId.HasValue)
        {
            var mapping = await _context.categoryMappings
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.connectionId == connectionId
                    && m.localCategoryId == product.categoryId.Value, ct);
            categoryMappingId = mapping?.Id;
        }

        var link = new ProductMarketplaceLink();
        link.AddFill(new ProductMarketplaceLink.AddForm
        {
            productId = productId,
            connectionId = connectionId,
            externalProductId = canonicalExternalId,
            externalSku = externalSku,
            status = ProductMarketplaceStatus.NotSynced,
            categoryMappingId = categoryMappingId
        });

        _context.productMarketplaceLinks.Add(link);
        await _context.SaveChangesAsync(ct);

        return Ok(new
        {
            linkId = link.Id,
            externalProductId = link.externalProductId,
            externalSku = link.externalSku,
            status = link.status.ToString()
        });
    }

    /// <summary>
    /// Remove a local marketplace link without deleting the external product.
    /// </summary>
    [HttpDelete]
    [Route("/marketplace/links/{linkId}")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> UnlinkProduct(int linkId, CancellationToken ct)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var link = await _context.productMarketplaceLinks
            .Include(l => l.connection)
            .FirstOrDefaultAsync(l => l.Id == linkId && l.connection.storeId == selectedStoreId.Value, ct);

        if (link == null)
            throw ApiException.NotFound("Marketplace link not found");

        await ProductNotTrashed.EnsureAsync(_context, link.productId, ct);

        var productId = link.productId;
        var externalProductId = link.externalProductId;

        _context.productMarketplaceLinks.Remove(link);
        await _context.SaveChangesAsync(ct);

        return Ok(new
        {
            success = true,
            productId,
            externalProductId
        });
    }

    /// <summary>
    /// Queue a background check that every link on the connection still points at a remote product.
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/links/verify")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> VerifyConnectionLinks(int connectionId, CancellationToken ct)
    {
        var connection = await GetConnectionAsync(connectionId);
        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null || !adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportProduct))
            throw ApiException.BadRequest(
                "Adapter does not support product import — cannot verify remote product existence");

        var existing = _linkVerifyQueue.ActiveOperationId(connection.Id);
        if (existing != null)
        {
            return Ok(new { success = true, operationId = existing, alreadyRunning = true });
        }

        var operationId = Guid.NewGuid().ToString("N");
        var userId = _currentUserService.GetUserId() ?? 0;
        var work = new LinkVerifyWork
        {
            OperationId = operationId,
            ConnectionId = connection.Id,
            StoreId = connection.storeId,
            UserId = userId,
            ConnectionName = connection.name,
        };
        await _operationLogger.StartAsync(
            operationId,
            connection.storeId,
            userId,
            LinkVerifyQueue.OperationType,
            "Verify links",
            connectionId: connection.Id,
            connectionName: connection.name);

        if (!_linkVerifyQueue.TryEnqueue(work))
        {
            await _operationLogger.FinishAsync(
                operationId, connection.storeId, OperationLifecycle.Cancelled, "Already running");
            return Ok(new
            {
                success = true,
                operationId = _linkVerifyQueue.ActiveOperationId(connection.Id) ?? operationId,
                alreadyRunning = true,
            });
        }

        return Ok(new { success = true, operationId, alreadyRunning = false });
    }

    /// <summary>
    /// Verify a single marketplace link against the remote product.
    /// </summary>
    [HttpPost]
    [Route("/marketplace/links/{linkId}/verify")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> VerifyLink(int linkId, CancellationToken ct)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var link = await _context.productMarketplaceLinks
            .Include(l => l.connection)
            .FirstOrDefaultAsync(l => l.Id == linkId && l.connection.storeId == selectedStoreId.Value, ct);

        if (link == null)
            throw ApiException.NotFound("Marketplace link not found");

        await ProductNotTrashed.EnsureAsync(_context, link.productId, ct);

        try
        {
            var result = await _productSyncService.VerifyLinksAsync(link.connectionId, linkId, ct);
            var refreshed = await _context.productMarketplaceLinks.AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == linkId, ct);

            return Ok(new
            {
                success = true,
                linkId,
                status = refreshed?.status.ToString(),
                lastErrorMessage = refreshed?.lastErrorMessage,
                checkedCount = result.Checked,
                confirmed = result.Confirmed,
                broken = result.Broken,
                recovered = result.Recovered
            });
        }
        catch (InvalidOperationException ex)
        {
            throw ApiException.BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Re-link a product to a different external marketplace product.
    /// </summary>
    [HttpPost]
    [Route("/marketplace/links/{linkId}/relink")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> RelinkProduct(
        int linkId,
        [FromBody] LinkProductInput input,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.externalProductId) || input.externalProductId.Length > 100)
            throw ApiException.BadRequest("externalProductId is required (max 100 characters)", code: "required_fields_missing");

        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var link = await _context.productMarketplaceLinks
            .Include(l => l.connection)
            .FirstOrDefaultAsync(l => l.Id == linkId && l.connection.storeId == selectedStoreId.Value, ct);

        if (link == null)
            throw ApiException.NotFound("Marketplace link not found");

        await ProductNotTrashed.EnsureAsync(_context, link.productId, ct);

        var rawExternalId = input.externalProductId.Trim();
        var canonicalExternalId = rawExternalId;
        string? externalSku = null;

        var adapter = _adapterRegistry.GetByKey(link.connection.adapterKey);
        if (adapter != null && adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportProduct))
        {
            var remote = await adapter.ImportProductAsync(link.connection, rawExternalId, ct);
            if (remote == null)
                throw ApiException.BadRequest(
                    $"Product '{rawExternalId}' was not found on the marketplace (tried as external id / sku / article).",
                    code: "remote_product_missing");
            canonicalExternalId = remote.ExternalId;
            externalSku = string.IsNullOrWhiteSpace(remote.Sku) ? null : remote.Sku;
        }

        var duplicateExternalId = await _context.productMarketplaceLinks
            .AnyAsync(l => l.externalProductId == canonicalExternalId
                && l.connectionId == link.connectionId
                && l.Id != link.Id, ct);
        if (duplicateExternalId)
            throw ApiException.BadRequest(
                $"External ID \"{canonicalExternalId}\" is already linked to another product on this connection");

        link.externalProductId = canonicalExternalId;
        link.externalSku = externalSku;
        link.status = ProductMarketplaceStatus.NotSynced;
        link.lastErrorMessage = null;

        await _context.SaveChangesAsync(ct);

        return Ok(new
        {
            linkId = link.Id,
            externalProductId = link.externalProductId,
            externalSku = link.externalSku,
            status = link.status.ToString()
        });
    }

    /// <summary>
    /// Bulk export products
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/products/export-bulk")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> ExportProductsBulk(
        int connectionId,
        [FromBody] ExportBulkInput input,
        CancellationToken ct)
    {
        await GetConnectionAsync(connectionId);
        await ProductNotTrashed.EnsureNoneAsync(_context, input.productIds ?? new List<int>(), ct);

        var bulkResult = await _productSyncService.ExportProductsBulkAsync(
            input.productIds ?? new List<int>(), connectionId, ct);

        var results = bulkResult.Items.Select(r => new
        {
            productId = r.ProductId,
            success = r.IsSuccess,
            externalProductId = r.ExternalProductId,
            errorMessage = r.ErrorMessage
        }).ToList();

        return Ok(new
        {
            results,
            successCount = bulkResult.SuccessCount,
            failedCount = bulkResult.FailedCount
        });
    }

    #endregion

    #region Resolve product links (productIds → linkIds for a connection)

    public record ResolveProductLinksInput(List<int> ProductIds);

    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/resolve-product-links")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> ResolveProductLinks(
        int connectionId,
        [FromBody] ResolveProductLinksInput input,
        CancellationToken ct)
    {
        var connection = await GetConnectionAsync(connectionId);

        var links = await _context.productMarketplaceLinks
            .AsNoTracking()
            .Where(l => l.connectionId == connectionId && input.ProductIds.Contains(l.productId))
            .Select(l => new { l.productId, linkId = l.Id, status = l.status.ToString() })
            .ToListAsync(ct);

        var linkedProductIds = links.Select(l => l.productId).ToHashSet();
        var unlinked = input.ProductIds.Where(id => !linkedProductIds.Contains(id)).ToList();

        return Ok(new { linked = links, unlinked });
    }

    #endregion

    #region Export Batch (async background)

    /// <summary>
    /// Create a batch of products to export in background
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/products/export-batch")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> CreateExportBatch(
        int connectionId,
        [FromBody] ExportBulkInput input,
        CancellationToken ct)
    {
        var hasIds = input.productIds is { Count: > 0 };
        var hasWhere = !string.IsNullOrWhiteSpace(input.where);
        if (!hasIds && !input.allMatching && !hasWhere && !input.skipWithoutImages)
            throw ApiException.BadRequest("No products selected for export", code: "required_fields_missing");

        var connection = await GetConnectionAsync(connectionId);
        List<int> productIds;
        try
        {
            productIds = await ExportBatchSelection.ResolveProductIdsAsync(
                _context,
                connection.storeId,
                input.productIds,
                input.allMatching || hasWhere,
                input.skipWithoutImages && !hasWhere,
                ct,
                input.where,
                _catalogFilter);
        }
        catch (AgentSqlFilterException ex)
        {
            throw ApiException.BadRequest(ex.Message, code: "invalid_filter");
        }
        if (productIds.Count == 0)
            throw ApiException.BadRequest("No products selected for export", code: "required_fields_missing");

        await ProductNotTrashed.EnsureNoneAsync(_context, productIds, ct);

        var batchId = Guid.NewGuid().ToString("N");
        var userId = _currentUserService.GetUserId() ?? 0;
        var storeId = connection.storeId;

        // Batch-load product names
        var productNames = await _context.products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.name, ct);

        foreach (var productId in productIds)
        {
            var pending = new PendingProductExport
            {
                connectionId = connectionId,
                productId = productId,
                productName = productNames.GetValueOrDefault(productId),
                authorUserId = userId,
                storeId = storeId,
                batchId = batchId,
                status = ProductExportStatus.Pending,
                createdAt = DateTime.UtcNow
            };

            _context.pendingProductExports.Add(pending);
        }

        await _context.SaveChangesAsync(ct);

        await _operationLogger.StartAsync(batchId, storeId, userId, "batch-export",
            $"Export {productIds.Count} products",
            connectionId: connectionId, connectionName: connection.name,
            batchId: batchId, batchType: "export", totalItems: productIds.Count);

        return Ok(new
        {
            batchId,
            queued = productIds.Count,
            message = $"Added {productIds.Count} products to the export queue"
        });
    }

    /// <summary>
    /// Get batch export status
    /// </summary>
    [HttpGet]
    [Route("/marketplace/export-batches/{batchId}/status")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetExportBatchStatus(string batchId, CancellationToken ct)
    {
        // Disable caching - critical for polling
        Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";

        var entities = await _context.pendingProductExports
            .AsNoTracking()
            .Where(p => p.batchId == batchId)
            .OrderBy(p => p.Id)
            .ToListAsync(ct);

        if (entities.Count == 0)
            throw ApiException.NotFound("Batch not found");

        var items = entities.Select(p => new
        {
            id = p.Id,
            productId = p.productId,
            productName = p.productName,
            status = p.status,
            externalProductId = p.externalProductId,
            linkId = p.linkId,
            lastError = p.lastError,
            retryCount = p.retryCount
        }).ToList();

        var pendingCount = entities.Count(e => e.status == ProductExportStatus.Pending);
        var inProgressCount = entities.Count(e => e.status == ProductExportStatus.InProgress);
        var completedCount = entities.Count(e => e.status == ProductExportStatus.Completed);
        var failedCount = entities.Count(e => e.status == ProductExportStatus.Failed);

        return Ok(new
        {
            batchId,
            total = items.Count,
            pending = pendingCount,
            inProgress = inProgressCount,
            completed = completedCount,
            failed = failedCount,
            isFinished = pendingCount == 0 && inProgressCount == 0,
            products = items
        });
    }

    /// <summary>
    /// Retry failed product exports in a batch
    /// </summary>
    [HttpPost]
    [Route("/marketplace/export-batches/{batchId}/retry-failed")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> RetryFailedExports(string batchId, CancellationToken ct)
    {
        var failedItems = await _context.pendingProductExports
            .Where(p => p.batchId == batchId && p.status == ProductExportStatus.Failed)
            .ToListAsync(ct);

        if (failedItems.Count == 0)
            return Ok(new { message = "No failed exports to retry", retried = 0 });

        foreach (var item in failedItems)
        {
            item.status = ProductExportStatus.Pending;
            item.retryCount = 0;
            item.lastError = null;
        }

        await _context.SaveChangesAsync(ct);

        return Ok(new { message = $"Queued for retry: {failedItems.Count} products", retried = failedItems.Count });
    }

    #endregion

    #region External Products

    /// <summary>
    /// Get ALL products from the marketplace with link information
    /// </summary>
    [HttpGet]
    [Route("/marketplace/connections/{connectionId}/external-products")]
    public async Task<IActionResult> GetExternalProducts(
        int connectionId,
        int pageSize = 50,
        string? cursor = null,
        string? offerIdSearch = null,
        CancellationToken ct = default)
    {
        var connection = await GetConnectionAsync(connectionId);

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest($"Unknown adapter: {connection.adapterKey}");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportAllProducts))
        {
            return Ok(new
            {
                items = Array.Empty<object>(),
                total = 0,
                linkedCount = 0,
                hasMore = false,
                nextCursor = (string?)null,
                supported = false,
                message = "Adapter does not support fetching the product list"
            });
        }

        Models.Marketplace.DTOs.PagedResult<Models.Marketplace.DTOs.ExternalProduct> pagedResult;
        try
        {
            pagedResult = await adapter.ImportAllProductsAsync(connection, pageSize, cursor, offerIdSearch, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to get external products from adapter {AdapterKey} for connection {ConnectionId}",
                connection.adapterKey,
                connectionId);

            throw ApiException.BadRequest(
                $"Failed to get products from adapter {adapter.DisplayName}: {ex.Message}",
                new
                {
                    adapterKey = connection.adapterKey,
                    connectionId,
                    operation = "external-products",
                    errorType = ex.GetType().FullName
                },
                ex);
        }

        var externalIds = pagedResult.Items.Select(p => p.ExternalId).ToList();
        var existingLinks = await _context.productMarketplaceLinks
            .AsNoTracking()
            .Where(l => l.connectionId == connectionId && externalIds.Contains(l.externalProductId))
            .Include(l => l.product)
            .ToListAsync(ct);
        var linkedExternalIds = existingLinks.ToDictionary(l => l.externalProductId, l => l);

        var selectedMappingIds = existingLinks
            .Where(l => l.categoryMappingId.HasValue)
            .Select(l => l.categoryMappingId!.Value)
            .Distinct()
            .ToList();

        var productCategoryIds = existingLinks
            .Where(l => l.product != null && l.product.categoryId.HasValue)
            .Select(l => l.product.categoryId!.Value)
            .Distinct()
            .ToList();

        var availableMappings = selectedMappingIds.Count > 0 || productCategoryIds.Count > 0
            ? await _context.categoryMappings
                .AsNoTracking()
                .Where(m => m.connectionId == connectionId &&
                    (selectedMappingIds.Contains(m.Id) ||
                     productCategoryIds.Contains(m.localCategoryId)))
                .ToListAsync(ct)
            : new List<CategoryMapping>();
        var mappingsById = availableMappings.ToDictionary(m => m.Id, m => m);
        var mappingsByLocalCategoryId = availableMappings
            .GroupBy(m => m.localCategoryId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Build items with link status
        var items = pagedResult.Items.Select(product =>
        {
            var isLinked = linkedExternalIds.TryGetValue(product.ExternalId, out var existingLink);
            CategoryMapping? resolvedMapping = null;

            var productCategoryId = isLinked ? existingLink!.product?.categoryId : null;
            if (isLinked)
            {
                if (existingLink!.categoryMappingId.HasValue
                    && mappingsById.TryGetValue(existingLink.categoryMappingId.Value, out var savedMapping)
                    && savedMapping.localCategoryId == productCategoryId)
                {
                    resolvedMapping = savedMapping;
                }
                else if (productCategoryId is int localCategoryId &&
                         mappingsByLocalCategoryId.TryGetValue(localCategoryId, out var localCategoryMappings) &&
                         localCategoryMappings.Count == 1)
                {
                    resolvedMapping = localCategoryMappings[0];
                }
            }

            var mappingCount = productCategoryId is int categoryId
                && mappingsByLocalCategoryId.TryGetValue(categoryId, out var categoryMappings)
                ? categoryMappings.Count
                : 0;
            var mappingUnselected = isLinked && mappingCount > 1 && resolvedMapping == null;

            return new
            {
                externalId = product.ExternalId,
                name = product.Name,
                sku = product.Sku,
                externalPrice = product.Price,
                externalStock = product.Quantity,
                // Category info
                externalCategoryId = product.CategoryId,
                externalCategoryName = product.CategoryName,
                // Link info
                isLinked,
                linkId = isLinked ? existingLink!.Id : (int?)null,
                linkStatus = isLinked ? existingLink!.status.ToString() : null,
                categoryMappingId = resolvedMapping?.Id,
                categoryMappingName = resolvedMapping?.name,
                mappingUnselected,
                mappingMissing = isLinked && resolvedMapping == null && !mappingUnselected,
                localProductId = isLinked ? existingLink!.productId : (int?)null,
                localProductSku = isLinked ? existingLink!.product?.sku : null,
                localProductName = isLinked ? existingLink!.product?.name : null,
                localPrice = isLinked ? existingLink!.product?.price : null,
                localStock = isLinked ? existingLink!.product?.storageQuantity : null
            };
        }).ToList();

        // Calculate stats
        var linkedCount = items.Count(i => i.isLinked);

        return Ok(new
        {
            items,
            total = items.Count,
            linkedCount,
            hasMore = pagedResult.HasMore,
            nextCursor = pagedResult.NextCursor
        });
    }

    #endregion

    #region Import

    /// <summary>
    /// Import preview - shows marketplace products and category mapping status
    /// </summary>
    [HttpGet]
    [Route("/marketplace/connections/{connectionId}/products/import-preview")]
    public async Task<IActionResult> GetImportPreview(
        int connectionId,
        int pageSize = 50,
        string? cursor = null,
        CancellationToken ct = default)
    {
        var connection = await GetConnectionAsync(connectionId);

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest($"Unknown adapter: {connection.adapterKey}");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportAllProducts))
            throw ApiException.BadRequest("Adapter does not support importing products");

        // Get products from marketplace
        var pagedResult = await adapter.ImportAllProductsAsync(connection, pageSize, cursor, null, ct);

        // Get existing links to detect already imported products
        var linkRepo = _unitOfWork.Repository<ProductMarketplaceLink>();
        var externalIds = pagedResult.Items.Select(p => p.ExternalId).ToList();
        var existingLinks = await linkRepo.FindAllAsync(
            l => l.connectionId == connectionId && externalIds.Contains(l.externalProductId),
            q => q.Include(l => l.product));
        var linkedExternalIds = existingLinks.ToDictionary(l => l.externalProductId, l => l);

        // Get category mappings for this connection
        var mappingRepo = _unitOfWork.Repository<CategoryMapping>();
        var categoryMappings = await mappingRepo.FindAllAsync(
            m => m.connectionId == connectionId,
            q => q.Include(m => m.localCategory));

        // Group by external category ID (handle duplicates by taking first)
        var mappingsByExternalCategory = categoryMappings
            .GroupBy(m => m.externalCategoryId)
            .ToDictionary(g => g.Key, g => g.First());

        // Build preview items with mapping status
        var previewItems = pagedResult.Items.Select(product =>
        {
            var isAlreadyLinked = linkedExternalIds.TryGetValue(product.ExternalId, out var existingLink);

            CategoryMapping? mapping = null;
            if (!string.IsNullOrEmpty(product.CategoryId))
            {
                mappingsByExternalCategory.TryGetValue(product.CategoryId, out mapping);
            }

            return new
            {
                externalId = product.ExternalId,
                sku = product.Sku,
                name = product.Name,
                price = product.Price,
                quantity = product.Quantity,
                externalCategoryId = product.CategoryId,
                externalCategoryName = product.CategoryName,
                // Mapping info
                hasMapping = mapping != null,
                localCategoryId = mapping?.localCategoryId,
                localCategoryName = mapping?.localCategory?.name,
                // Already linked info
                isAlreadyLinked,
                linkedProductId = isAlreadyLinked ? existingLink!.productId : (int?)null,
                linkedProductName = isAlreadyLinked ? existingLink!.product?.name : null
            };
        }).ToList();

        // Group categories for summary
        var categorySummary = previewItems
            .Where(p => !string.IsNullOrEmpty(p.externalCategoryId))
            .GroupBy(p => p.externalCategoryId)
            .Select(g => new
            {
                externalCategoryId = g.Key,
                externalCategoryName = g.First().externalCategoryName,
                productCount = g.Count(),
                hasMapping = g.First().hasMapping,
                localCategoryId = g.First().localCategoryId,
                localCategoryName = g.First().localCategoryName
            })
            .ToList();

        return Ok(new
        {
            items = previewItems,
            hasMore = pagedResult.HasMore,
            nextCursor = pagedResult.NextCursor,
            totalNew = previewItems.Count(p => !p.isAlreadyLinked),
            totalAlreadyLinked = previewItems.Count(p => p.isAlreadyLinked),
            totalWithMapping = previewItems.Count(p => p.hasMapping && !p.isAlreadyLinked),
            totalWithoutMapping = previewItems.Count(p => !p.hasMapping && !p.isAlreadyLinked),
            categorySummary
        });
    }

    /// <summary>
    /// Import selected products from the marketplace (in background)
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/products/import-selected")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> ImportSelectedProducts(
        int connectionId,
        [FromBody] ImportSelectedInput input,
        CancellationToken ct)
    {
        if (input.products == null || input.products.Count == 0)
            throw ApiException.BadRequest("No products selected for import", code: "required_fields_missing");

        var connection = await GetConnectionAsync(connectionId);
        await _quotaService.EnsureProductQuotaAsync(connection.storeId);
        var remainingSlots = await _quotaService.GetRemainingProductSlotsAsync(connection.storeId);

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest($"Unknown adapter: {connection.adapterKey}");

        // Get category mappings (handle duplicates by taking first)
        var mappingRepo = _unitOfWork.Repository<CategoryMapping>();
        var categoryMappings = await mappingRepo.FindAllAsync(
            m => m.connectionId == connectionId);
        var mappingsByExternalCategory = categoryMappings
            .GroupBy(m => m.externalCategoryId)
            .ToDictionary(g => g.Key, g => g.First());

        // Use provided operationId or generate new batch ID
        var batchId = !string.IsNullOrEmpty(input.operationId) ? input.operationId : Guid.NewGuid().ToString("N");
        var userId = _currentUserService.GetUserId() ?? 0;
        var storeId = connection.storeId;

        var skippedProducts = new List<object>();
        int queued = 0;
        var skippedQuota = 0;
        var categoryJobs = 0;
        var enqueuedCategories = new HashSet<string>(StringComparer.Ordinal);

        foreach (var productInput in input.products)
        {
            if (remainingSlots is int cap && queued >= cap)
            {
                skippedQuota++;
                continue;
            }
            int? targetCategoryId = productInput.targetCategoryId;
            var extCatId = string.IsNullOrWhiteSpace(productInput.externalCategoryId)
                ? null
                : productInput.externalCategoryId.Trim();

            if (targetCategoryId == null && extCatId != null)
            {
                if (mappingsByExternalCategory.TryGetValue(extCatId, out var mapping))
                {
                    targetCategoryId = mapping.localCategoryId;
                }
                else if (!productInput.forceImportWithoutMapping)
                {
                    if (enqueuedCategories.Add(extCatId))
                    {
                        var job = await _categoryImportQueue.EnqueueIfNeededAsync(
                            connection,
                            extCatId,
                            productInput.externalCategoryName,
                            productInput.targetCategoryName,
                            localCategoryId: null,
                            selectedFieldKeys: null,
                            batchId,
                            userId,
                            ct);
                        if (job != null)
                            categoryJobs++;
                    }
                }
            }

            if (productInput.targetCategoryId.HasValue)
                targetCategoryId = productInput.targetCategoryId.Value;

            var pending = new PendingProductImport
            {
                connectionId = connectionId,
                externalProductId = productInput.externalId,
                targetCategoryId = targetCategoryId,
                externalCategoryId = extCatId,
                forceImportWithoutMapping = productInput.forceImportWithoutMapping,
                authorUserId = userId,
                storeId = storeId,
                batchId = batchId,
                productName = productInput.productName,
                status = ProductImportStatus.Pending,
                createdAt = DateTime.UtcNow
            };

            _context.pendingProductImports.Add(pending);
            queued++;
        }

        await _context.SaveChangesAsync(ct);

        if (queued > 0)
        {
            // Operation may already exist (created by frontend for cascading flow)
            var opExists = await _context.operations.AsNoTracking()
                .AnyAsync(o => o.operationId == batchId, ct);
            if (opExists)
            {
                // Update existing operation with final counts
                var op = await _context.operations.FirstAsync(o => o.operationId == batchId, ct);
                op.totalItems = queued;
                op.title = $"Import {queued} products";
                op.batchType = "import";
                await _context.SaveChangesAsync(ct);
                await _operationLogger.ProgressAsync(batchId, storeId, queued, 0, 0);
            }
            else
            {
                await _operationLogger.StartAsync(batchId, storeId, userId, "batch-import",
                    $"Import {queued} products",
                    connectionId: connectionId, connectionName: connection.name,
                    batchId: batchId, batchType: "import", totalItems: queued);
            }

            // Pre-create image download operation (will be populated as images are queued)
            var imageBatchId = $"{batchId}-img";
            var imgOpExists = await _context.operations.AsNoTracking()
                .AnyAsync(o => o.operationId == imageBatchId, ct);
            if (!imgOpExists)
            {
                await _operationLogger.StartAsync(imageBatchId, storeId, userId, "image-download",
                    $"Image download",
                    connectionId: connectionId, connectionName: connection.name,
                    batchId: imageBatchId, batchType: "image-download", totalItems: 0);
            }

            await _operationLogger.LogAsync(batchId, storeId, "info",
                $"Added {queued} products to the queue, awaiting processing...");
            if (categoryJobs > 0)
            {
                await _operationLogger.LogAsync(batchId, storeId, "info",
                    $"Queued {categoryJobs} {(categoryJobs == 1 ? "category" : "categories")} for import before products.");
            }

            if (skippedQuota > 0)
            {
                await _operationLogger.LogAsync(batchId, storeId, "warning",
                    $"Only {queued} of {input.products.Count} products were queued (product limit).");
            }

            // Log skipped products
            foreach (var skipped in skippedProducts)
            {
                var sp = (dynamic)skipped;
                await _operationLogger.LogAsync(batchId, storeId, "warning",
                    $"Skipped: {sp.externalId} - {sp.error}");
            }
        }

        return Ok(new
        {
            batchId,
            queued,
            skipped = skippedProducts.Count,
            skippedProducts,
            message = $"Added {queued} products to the import queue"
        });
    }

    /// <summary>
    /// Get batch import status
    /// </summary>
    [HttpGet]
    [Route("/marketplace/import-batches/{batchId}/status")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetImportBatchStatus(string batchId, CancellationToken ct)
    {
        // Disable caching - critical for polling
        Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";

        // Load entities directly with AsNoTracking
        var entities = await _context.pendingProductImports
            .AsNoTracking()
            .Where(p => p.batchId == batchId)
            .OrderBy(p => p.Id)
            .ToListAsync(ct);

        _logger.LogWarning("[BatchStatus] Retrieved {Count} records from DB, statuses: [{Statuses}]",
            entities.Count,
            string.Join(", ", entities.Select(e => $"{e.Id}:{e.status}")));

        if (entities.Count == 0)
            throw ApiException.NotFound("Batch not found");

        var items = entities.Select(p => new
        {
            id = p.Id,
            externalProductId = p.externalProductId,
            productName = p.productName,
            status = p.status,
            importedProductId = p.importedProductId,
            lastError = p.lastError,
            retryCount = p.retryCount
        }).ToList();

        var pendingCount = entities.Count(e => e.status == ProductImportStatus.Pending);
        var inProgressCount = entities.Count(e => e.status == ProductImportStatus.InProgress);
        var completedCount = entities.Count(e => e.status == ProductImportStatus.Completed);
        var failedCount = entities.Count(e => e.status == ProductImportStatus.Failed);

        _logger.LogWarning(
            "[BatchStatus] TOTAL: pending={Pending}, inProgress={InProgress}, completed={Completed}, failed={Failed}",
            pendingCount, inProgressCount, completedCount, failedCount);

        return Ok(new
        {
            batchId,
            total = items.Count,
            pending = pendingCount,
            inProgress = inProgressCount,
            completed = completedCount,
            failed = failedCount,
            isFinished = pendingCount == 0 && inProgressCount == 0,
            products = items
        });
    }

    /// <summary>
    /// Retry failed product imports in a batch
    /// </summary>
    [HttpPost]
    [Route("/marketplace/import-batches/{batchId}/retry-failed")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> RetryFailedImports(string batchId, CancellationToken ct)
    {
        var failedItems = await _context.pendingProductImports
            .Where(p => p.batchId == batchId && p.status == ProductImportStatus.Failed)
            .ToListAsync(ct);

        if (failedItems.Count == 0)
            return Ok(new { message = "No failed imports to retry", retried = 0 });

        foreach (var item in failedItems)
        {
            item.status = ProductImportStatus.Pending;
            item.retryCount = 0;
            item.lastError = null;
        }

        await _context.SaveChangesAsync(ct);

        return Ok(new { message = $"Queued for retry: {failedItems.Count} products", retried = failedItems.Count });
    }

    /// <summary>
    /// Import a single product from the marketplace
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/products/import/{externalId}")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> ImportProduct(
        int connectionId,
        string externalId,
        [FromBody] ImportProductInput? input,
        CancellationToken ct)
    {
        await GetConnectionAsync(connectionId);

        var operationId = Guid.NewGuid().ToString("N");

        var result = await _productSyncService.ImportProductAsync(
            connectionId,
            externalId,
            input?.targetCategoryId,
            _currentUserService.GetUserId(),
            ct);

        if (!result.IsSuccess && !result.AlreadyLinked)
        {
            if (result.ErrorCode == "product_limit_reached")
                throw ApiException.Conflict(
                    result.ErrorMessage ?? "Product limit reached",
                    result.ErrorData,
                    code: "product_limit_reached");
            throw ApiException.BadRequest(result.ErrorMessage ?? "Import failed", code: "import_failed");
        }

        return Ok(new
        {
            operationId,
            success = result.IsSuccess,
            alreadyLinked = result.AlreadyLinked,
            productId = result.ProductId,
            linkId = result.LinkId,
            productName = result.ProductName,
            message = result.Message ?? result.ErrorMessage
        });
    }

    /// <summary>
    /// Import all products from the marketplace (deprecated, use import-preview + import-selected)
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/products/import-all")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> ImportAllProducts(
        int connectionId,
        [FromBody] ImportAllInput? input,
        CancellationToken ct)
    {
        var connection = await GetConnectionAsync(connectionId);

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest($"Unknown adapter: {connection.adapterKey}");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportAllProducts))
            throw ApiException.BadRequest("Adapter does not support bulk import");

        int pageSize = input?.pageSize ?? 100;
        int maxProducts = input?.maxProducts ?? 1000;
        string? cursor = null;
        int imported = 0;
        int skipped = 0;
        int failed = 0;

        var errors = new List<string>();

        try
        {
            while (imported + skipped + failed < maxProducts)
            {
                var pagedResult = await adapter.ImportAllProductsAsync(
                    connection, pageSize, cursor, null, ct);
                var products = pagedResult.Items;
                var hasMore = pagedResult.HasMore;
                var nextCursor = pagedResult.NextCursor;

                foreach (var externalProduct in products)
                {
                    if (imported + skipped + failed >= maxProducts)
                        break;

                    var result = await _productSyncService.ImportProductAsync(
                        connectionId,
                        externalProduct.ExternalId,
                        input?.targetCategoryId,
                        _currentUserService.GetUserId(),
                        ct);

                    if (result.IsSuccess)
                        imported++;
                    else if (result.AlreadyLinked)
                        skipped++;
                    else
                    {
                        failed++;
                        errors.Add($"{externalProduct.ExternalId}: {result.ErrorMessage}");
                    }
                }

                if (!hasMore || string.IsNullOrEmpty(nextCursor))
                    break;

                cursor = nextCursor;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during bulk import for connection {ConnectionId}", connectionId);
            errors.Add($"Import aborted: {ex.Message}");
        }

        return Ok(new
        {
            imported,
            skipped,
            failed,
            errors = errors.Take(10).ToList()
        });
    }

    #endregion

    #region SKU Update

    /// <summary>
    /// Update a product's SKU on the marketplace
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/products/{productId}/update-sku")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> UpdateSku(
        int connectionId,
        int productId,
        [FromBody] UpdateSkuInput input,
        CancellationToken ct)
    {
        await GetConnectionAsync(connectionId);

        // Find the product's marketplace link
        var linkRepo = _unitOfWork.Repository<ProductMarketplaceLink>();
        var link = await linkRepo.FindAsync(
            l => l.connectionId == connectionId && l.productId == productId);

        if (link == null)
            throw ApiException.NotFound("Product is not linked to this marketplace");

        var result = await _productSyncService.UpdateSkuAsync(link.Id, input.newSku, ct);

        if (!result.IsSuccess)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Failed to update SKU");

        return Ok(new
        {
            success = true,
            message = result.Message
        });
    }

    #endregion

    #region Price Sync

    /// <summary>
    /// Compare local product prices with the marketplace
    /// </summary>
    [HttpGet]
    [Route("/marketplace/connections/{connectionId}/prices")]
    public async Task<IActionResult> ComparePrices(int connectionId, CancellationToken ct)
    {
        await GetConnectionAsync(connectionId);

        var result = await _priceSyncService.ComparePricesAsync(connectionId, ct);

        if (!result.IsSuccess)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Failed to compare prices");

        return Ok(new
        {
            items = result.Items.Select(i => new
            {
                linkId = i.LinkId,
                productId = i.ProductId,
                productSku = i.ProductSku,
                productName = i.ProductName,
                externalProductId = i.ExternalProductId,
                localPrice = i.LocalPrice,
                externalPrice = i.ExternalPrice,
                localStock = i.LocalStock,
                externalStock = i.ExternalStock,
                hasPriceDifference = i.HasPriceDifference,
                hasStockDifference = i.HasStockDifference
            }),
            message = result.Message
        });
    }

    /// <summary>
    /// Import prices from the marketplace
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/prices/import")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> ImportPrices(
        int connectionId,
        [FromBody] PriceSyncInput? input,
        CancellationToken ct)
    {
        var connection = await GetConnectionAsync(connectionId);
        var importLinkIds = input?.linkIds;
        if (importLinkIds is { Count: > 0 })
            await ProductNotTrashed.EnsureLinksAsync(_context, importLinkIds, ct);

        var operationId = Guid.NewGuid().ToString("N");
        var userId = _currentUserService.GetUserId() ?? 0;
        var storeId = connection.storeId;

        await _operationLogger.StartAsync(operationId, storeId, userId, "price-import",
            "Import prices", connectionId: connectionId, connectionName: connection.name);

        var result = await _priceSyncService.ImportPricesAsync(
            connectionId,
            input?.linkIds,
            ct,
            operationId,
            storeId);

        var status = result.IsSuccess ? "completed" : "failed";
        await _operationLogger.FinishAsync(operationId, storeId, status, result.Message ?? result.ErrorMessage);

        if (!result.IsSuccess)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Failed to import prices", code: "import_failed");

        return Ok(new
        {
            operationId,
            updatedCount = result.UpdatedCount,
            skippedCount = result.SkippedCount,
            message = result.Message
        });
    }

    /// <summary>
    /// Export prices to the marketplace
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/prices/export")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> ExportPrices(
        int connectionId,
        [FromBody] PriceSyncInput? input,
        CancellationToken ct)
    {
        var connection = await GetConnectionAsync(connectionId);
        var priceLinkIds = input?.linkIds;
        if (priceLinkIds is { Count: > 0 })
            await ProductNotTrashed.EnsureLinksAsync(_context, priceLinkIds, ct);

        var operationId = Guid.NewGuid().ToString("N");
        var userId = _currentUserService.GetUserId() ?? 0;
        var storeId = connection.storeId;

        await _operationLogger.StartAsync(operationId, storeId, userId, "price-export",
            "Export prices", connectionId: connectionId, connectionName: connection.name);

        await _operationLogger.LogAsync(operationId, storeId, "info", $"Exporting prices for {input?.linkIds?.Count ?? 0} products...");

        var result = await _priceSyncService.ExportPricesAsync(
            connectionId,
            input?.linkIds,
            ct,
            operationId,
            storeId);

        var status = result.IsSuccess ? "completed" : "failed";
        await _operationLogger.FinishAsync(operationId, storeId, status, result.Message ?? result.ErrorMessage);

        if (!result.IsSuccess)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Failed to export prices", code: "export_failed");

        return Ok(new
        {
            operationId,
            updatedCount = result.UpdatedCount,
            skippedCount = result.SkippedCount,
            message = result.Message
        });
    }

    /// <summary>
    /// Export stock quantities to the marketplace
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/stocks/export")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> ExportStocks(
        int connectionId,
        [FromBody] PriceSyncInput? input,
        CancellationToken ct)
    {
        var connection = await GetConnectionAsync(connectionId);
        var stockLinkIds = input?.linkIds;
        if (stockLinkIds is { Count: > 0 })
            await ProductNotTrashed.EnsureLinksAsync(_context, stockLinkIds, ct);

        var operationId = Guid.NewGuid().ToString("N");
        var userId = _currentUserService.GetUserId() ?? 0;
        var storeId = connection.storeId;

        await _operationLogger.StartAsync(operationId, storeId, userId, "stock-export",
            "Export stock levels", connectionId: connectionId, connectionName: connection.name);

        await _operationLogger.LogAsync(operationId, storeId, "info", $"Exporting stock levels for {input?.linkIds?.Count ?? 0} products...");

        var result = await _priceSyncService.ExportStocksAsync(
            connectionId,
            input?.linkIds,
            ct,
            operationId,
            storeId);

        var stockStatus = result.IsSuccess ? "completed" : "failed";
        await _operationLogger.FinishAsync(operationId, storeId, stockStatus, result.Message ?? result.ErrorMessage);

        if (!result.IsSuccess)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Failed to export stock levels", code: "export_failed");

        return Ok(new
        {
            operationId,
            updatedCount = result.UpdatedCount,
            skippedCount = result.SkippedCount,
            message = result.Message
        });
    }

    #endregion

    #region Sync Status

    /// <summary>
    /// Get product synchronization status
    /// </summary>
    [HttpGet]
    [Route("/marketplace/connections/{connectionId}/sync-status")]
    public async Task<IActionResult> GetSyncStatus(
        int connectionId,
        [FromQuery] List<int>? productIds,
        CancellationToken ct)
    {
        await GetConnectionAsync(connectionId);

        var statuses = await _productSyncService.GetProductSyncStatusAsync(connectionId, productIds, ct);

        return Ok(statuses.Select(s => new
        {
            productId = s.ProductId,
            productName = s.ProductName,
            productSku = s.ProductSku,
            linkId = s.LinkId,
            externalProductId = s.ExternalProductId,
            externalSku = s.ExternalSku,
            status = s.Status.ToString(),
            lastSyncAt = s.LastSyncAt,
            lastErrorMessage = s.LastErrorMessage,
            localPrice = s.LocalPrice,
            externalPrice = s.ExternalPrice,
            localStock = s.LocalStock,
            externalStock = s.ExternalStock
        }));
    }

    #endregion

    #region Image Download Status

    /// <summary>
    /// Get image download status for the store
    /// </summary>
    [HttpGet]
    [Route("/marketplace/image-downloads/status")]
    public async Task<IActionResult> GetImageDownloadStatus(CancellationToken ct)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var context = _serviceProvider.GetRequiredService<DataContext>();

        // Statistics by status
        var stats = await context.pendingImageDownloads
            .Where(p => p.storeId == selectedStoreId)
            .GroupBy(p => p.status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var pendingCount = stats.FirstOrDefault(s => s.Status == Models.ImageDownloadStatus.Pending)?.Count ?? 0;
        var inProgressCount = stats.FirstOrDefault(s => s.Status == Models.ImageDownloadStatus.InProgress)?.Count ?? 0;
        var completedCount = stats.FirstOrDefault(s => s.Status == Models.ImageDownloadStatus.Completed)?.Count ?? 0;
        var failedCount = stats.FirstOrDefault(s => s.Status == Models.ImageDownloadStatus.Failed)?.Count ?? 0;

        // Most recent failed uploads
        var recentFailed = await context.pendingImageDownloads
            .Where(p => p.storeId == selectedStoreId && p.status == Models.ImageDownloadStatus.Failed)
            .OrderByDescending(p => p.lastAttemptAt)
            .Take(10)
            .Select(p => new
            {
                p.productId,
                p.imageUrl,
                p.retryCount,
                p.lastError,
                p.lastAttemptAt
            })
            .ToListAsync(ct);

        return Ok(new
        {
            pending = pendingCount,
            inProgress = inProgressCount,
            completed = completedCount,
            failed = failedCount,
            total = pendingCount + inProgressCount + completedCount + failedCount,
            recentFailed
        });
    }

    /// <summary>
    /// Get image download status for a specific product
    /// </summary>
    [HttpGet]
    [Route("/products/{productId}/image-downloads")]
    public async Task<IActionResult> GetProductImageDownloadStatus(int productId, CancellationToken ct)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var context = _serviceProvider.GetRequiredService<DataContext>();

        var downloads = await context.pendingImageDownloads
            .Where(p => p.productId == productId && p.storeId == selectedStoreId)
            .OrderBy(p => p.imageIndex)
            .Select(p => new
            {
                p.imageUrl,
                p.imageIndex,
                status = p.status.ToString(),
                p.retryCount,
                p.lastError,
                p.createdAt,
                p.lastAttemptAt,
                p.completedAt
            })
            .ToListAsync(ct);

        var pendingCount = downloads.Count(d => d.status == "Pending");
        var inProgressCount = downloads.Count(d => d.status == "InProgress");
        var completedCount = downloads.Count(d => d.status == "Completed");
        var failedCount = downloads.Count(d => d.status == "Failed");

        return Ok(new
        {
            productId,
            pending = pendingCount,
            inProgress = inProgressCount,
            completed = completedCount,
            failed = failedCount,
            total = downloads.Count,
            images = downloads
        });
    }

    /// <summary>
    /// Retry failed image downloads for a product
    /// </summary>
    [HttpPost]
    [Route("/products/{productId}/image-downloads/retry")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> RetryFailedImageDownloads(int productId, CancellationToken ct)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var context = _serviceProvider.GetRequiredService<DataContext>();

        var failedDownloads = await context.pendingImageDownloads
            .Where(p => p.productId == productId &&
                       p.storeId == selectedStoreId &&
                       p.status == Models.ImageDownloadStatus.Failed)
            .ToListAsync(ct);

        foreach (var download in failedDownloads)
        {
            download.status = Models.ImageDownloadStatus.Pending;
            download.retryCount = 0;
            download.lastError = null;
        }

        await context.SaveChangesAsync(ct);

        return Ok(new { retriedCount = failedDownloads.Count });
    }

    #endregion

    #region Product Links by Product

    /// <summary>
    /// Get all product-marketplace links (for the product page)
    /// </summary>
    [HttpGet]
    [Route("/products/{productId}/marketplace-links")]
    public async Task<IActionResult> GetProductMarketplaceLinks(int productId, CancellationToken ct)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var linkRepo = _unitOfWork.Repository<ProductMarketplaceLink>();
        var links = await linkRepo.FindAllAsync(
            l => l.productId == productId,
            q => q.Include(l => l.connection));

        // Filter by store
        var storeLinks = links.Where(l => l.connection?.storeId == selectedStoreId.Value).ToList();

        // Get adapter info
        var result = storeLinks.Select(link =>
        {
            var adapter = _adapterRegistry.GetByKey(link.connection.adapterKey);

            return new
            {
                id = link.Id,
                connectionId = link.connectionId,
                connectionName = link.connection?.name,
                adapterKey = link.connection?.adapterKey,
                adapterName = adapter?.DisplayName ?? link.connection?.adapterKey,
                productEditUrlTemplate = adapter?.ProductEditUrlTemplate,
                publicCredentials = link.connection == null
                    ? null
                    : ConnectionCredentialsHelper.ExtractPublicCredentials(
                        link.connection.credentialsJson,
                        adapter?.GetCredentialsSchema(),
                        _encryptionService),
                externalProductId = link.externalProductId,
                externalSku = link.externalSku,
                status = link.status.ToString(),
                lastSyncAt = link.lastSyncAt,
                lastErrorMessage = link.lastErrorMessage,
                localPrice = link.product?.price,
                externalPrice = link.externalPrice,
                localStock = link.product?.storageQuantity,
                externalStock = link.externalStock,
                // Capabilities for UI
                canExportPrices = adapter?.Capabilities.HasFlag(MarketplaceCapabilities.ExportPrices) ?? false,
                canExportStocks = adapter?.Capabilities.HasFlag(MarketplaceCapabilities.ExportStocks) ?? false,
                canUpdateSku = adapter?.Capabilities.HasFlag(MarketplaceCapabilities.UpdateSku) ?? false
            };
        }).ToList();

        return Ok(result);
    }

    /// <summary>
    /// Get available connections for product export
    /// </summary>
    [HttpGet]
    [Route("/products/{productId}/available-connections")]
    public async Task<IActionResult> GetAvailableConnectionsForProduct(int productId, CancellationToken ct)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        await ProductNotTrashed.EnsureAsync(_context, productId, ct);

        // Get product with category
        var productRepo = _unitOfWork.Repository<Product>();
        var product = await productRepo.FindAsync(
            p => p.Id == productId && p.storeId == selectedStoreId.Value,
            q => q.Include(p => p.category));

        if (product == null)
            throw ApiException.NotFound("Product not found", code: "product_not_found");

        // Get existing links
        var linkRepo = _unitOfWork.Repository<ProductMarketplaceLink>();
        var existingLinks = await linkRepo.FindAllAsync(
            l => l.productId == productId,
            q => q.Include(l => l.categoryMapping));
        var linkByConnectionId = existingLinks.ToDictionary(l => l.connectionId, l => l);

        // Get all active connections
        var connRepo = _unitOfWork.Repository<MarketplaceConnection>();
        var connections = await connRepo.FindAllAsync(
            c => c.storeId == selectedStoreId.Value && c.isActive);

        // Get category mappings (all per connection for multi-mapping support)
        var mappingRepo = _unitOfWork.Repository<CategoryMapping>();
        var mappings = await mappingRepo.FindAllAsync(
            m => product.categoryId.HasValue && m.localCategoryId == product.categoryId.Value);
        var mappingsByConnectionId = mappings
            .GroupBy(m => m.connectionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Return ALL connections with status information
        var result = connections
            .Select(conn =>
            {
                var adapter = _adapterRegistry.GetByKey(conn.adapterKey);
                mappingsByConnectionId.TryGetValue(conn.Id, out var connMappings);
                connMappings ??= new List<CategoryMapping>();
                var hasMapping = connMappings.Count > 0;
                var isLinked = linkByConnectionId.ContainsKey(conn.Id);
                var link = isLinked ? linkByConnectionId[conn.Id] : null;
                var savedMappingWasDeleted = link?.categoryMappingId != null && link.categoryMapping == null;
                var savedMatches = link?.categoryMapping != null
                    && product.categoryId.HasValue
                    && link.categoryMapping.localCategoryId == product.categoryId.Value;
                var mappingUnselected = connMappings.Count > 1 && !savedMatches;

                return new
                {
                    connectionId = conn.Id,
                    connectionName = conn.name,
                    adapterKey = conn.adapterKey,
                    adapterName = adapter?.DisplayName ?? conn.adapterKey,
                    productEditUrlTemplate = adapter?.ProductEditUrlTemplate,
                    publicCredentials = ConnectionCredentialsHelper.ExtractPublicCredentials(
                        conn.credentialsJson,
                        adapter?.GetCredentialsSchema(),
                        _encryptionService),
                    // Mapping status
                    hasMapping,
                    needsMapping = !hasMapping && product.categoryId.HasValue,
                    // Multi-mapping: all available mappings + selected one from link
                    selectedMappingId = savedMatches ? link!.categoryMappingId : null,
                    mappingUnselected,
                    mappingMissing = isLinked && !mappingUnselected && (!hasMapping || savedMappingWasDeleted),
                    availableMappings = connMappings
                        .Select(m => (object)new {
                            id = m.Id,
                            name = m.name,
                            externalCategoryId = m.externalCategoryId,
                            externalCategoryName = m.externalCategoryName,
                            localCategoryName = product.category?.name
                        }).ToList(),
                    // Link status
                    isLinked,
                    linkId = link?.Id,
                    linkStatus = link?.status.ToString(),
                    externalProductId = link?.externalProductId,
                    externalSku = link?.externalSku,
                    lastSyncAt = link?.lastSyncAt,
                    lastErrorMessage = link?.lastErrorMessage,
                    externalPrice = link?.externalPrice,
                    externalStock = link?.externalStock,
                    // Capabilities
                    canExport = adapter?.Capabilities.HasFlag(MarketplaceCapabilities.ExportProduct) ?? false,
                    canImport = adapter?.Capabilities.HasFlag(MarketplaceCapabilities.ImportProduct) ?? false,
                    canArchive = adapter?.Capabilities.HasFlag(MarketplaceCapabilities.ArchiveProduct) ?? false,
                    canExportPrices = adapter?.Capabilities.HasFlag(MarketplaceCapabilities.ExportPrices) ?? false,
                    canExportStocks = adapter?.Capabilities.HasFlag(MarketplaceCapabilities.ExportStocks) ?? false,
                    preferredExportMode = isLinked
                        ? PreferredExportModes.Normalize(link?.preferredExportMode)
                        : PreferredExportModes.Full
                };
            })
            .ToList();

        return Ok(new
        {
            productId,
            productName = product.name,
            categoryId = product.categoryId,
            categoryName = product.category?.name,
            hasSizes = product.category?.hasSizes ?? false,
            preferredExportAllMode = PreferredExportModes.Normalize(product.preferredExportAllMode),
            allConnections = result
        });
    }

    public record PreferredExportModeInput(string? Mode = null);

    /// <summary>
    /// Persist preferred export mode for a product↔adapter link (Adapters tab UI).
    /// </summary>
    [HttpPut]
    [Route("/marketplace/links/{linkId}/preferred-export-mode")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> SetLinkPreferredExportMode(
        int linkId,
        [FromBody] PreferredExportModeInput? input,
        CancellationToken ct)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var mode = input?.Mode;
        if (!PreferredExportModes.IsValid(mode))
            throw ApiException.BadRequest("Invalid export mode", code: "invalid_export_mode");

        var linkRepo = _unitOfWork.Repository<ProductMarketplaceLink>();
        var link = await linkRepo.GetByIdWithIncludesTrackingAsync(
            linkId,
            q => q.Include(l => l.connection));

        if (link == null)
            throw ApiException.NotFound("Marketplace link not found");

        await ProductNotTrashed.EnsureAsync(_context, link.productId, ct);

        if (link.connection?.storeId != selectedStoreId.Value)
            throw ApiException.Forbidden("Access denied", code: "access_denied_store");

        var adapter = _adapterRegistry.GetByKey(link.connection.adapterKey);
        if (mode == PreferredExportModes.Price &&
            !(adapter?.Capabilities.HasFlag(MarketplaceCapabilities.ExportPrices) ?? false))
            throw ApiException.BadRequest("Adapter does not support price export", code: "capability_missing");

        if (mode == PreferredExportModes.Stock &&
            !(adapter?.Capabilities.HasFlag(MarketplaceCapabilities.ExportStocks) ?? false))
            throw ApiException.BadRequest("Adapter does not support stock export", code: "capability_missing");

        link.preferredExportMode = mode!;
        await _unitOfWork.SaveChangesAsync(ct);

        return Ok(new { linkId, preferredExportMode = link.preferredExportMode });
    }

    /// <summary>
    /// Persist preferred mode for "export to all adapters" on a product.
    /// </summary>
    [HttpPut]
    [Route("/products/{productId}/preferred-export-all-mode")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> SetProductPreferredExportAllMode(
        int productId,
        [FromBody] PreferredExportModeInput? input,
        CancellationToken ct)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var mode = input?.Mode;
        if (!PreferredExportModes.IsValid(mode))
            throw ApiException.BadRequest("Invalid export mode", code: "invalid_export_mode");

        await ProductNotTrashed.EnsureAsync(_context, productId, ct);

        var productRepo = _unitOfWork.Repository<Product>();
        var product = await productRepo.GetByIdTrackingAsync(productId, ct);

        if (product == null || product.storeId != selectedStoreId.Value)
            throw ApiException.NotFound("Product not found", code: "product_not_found");

        if (mode == PreferredExportModes.Price || mode == PreferredExportModes.Stock)
        {
            var linkRepo = _unitOfWork.Repository<ProductMarketplaceLink>();
            var links = await linkRepo.FindAllAsync(
                l => l.productId == productId,
                q => q.Include(l => l.connection));

            var hasCapability = links.Any(l =>
            {
                var adapter = _adapterRegistry.GetByKey(l.connection?.adapterKey ?? "");
                if (adapter == null) return false;
                return mode == PreferredExportModes.Price
                    ? adapter.Capabilities.HasFlag(MarketplaceCapabilities.ExportPrices)
                    : adapter.Capabilities.HasFlag(MarketplaceCapabilities.ExportStocks);
            });

            if (!hasCapability)
                throw ApiException.BadRequest(
                    mode == PreferredExportModes.Price
                        ? "No linked adapters support price export"
                        : "No linked adapters support stock export",
                    code: "capability_missing");
        }

        product.preferredExportAllMode = mode!;
        await _unitOfWork.SaveChangesAsync(ct);

        return Ok(new { productId, preferredExportAllMode = product.preferredExportAllMode });
    }

    /// <summary>
    /// Import changes from the marketplace into the local product
    /// </summary>
    public record ImportFromLinkInput(List<string>? ExcludeFields = null, string? OperationId = null);

    [HttpPost]
    [Route("/marketplace/links/{linkId}/import")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> ImportFromLink(int linkId, [FromBody] ImportFromLinkInput? input, CancellationToken ct)
    {
        var excludeFields = input?.ExcludeFields != null ? new HashSet<string>(input.ExcludeFields) : null;
        var userId = _currentUserService.GetUserId() ?? 0;
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        // Get the marketplace link (with tracking to allow updating fields)
        var linkRepo = _unitOfWork.Repository<ProductMarketplaceLink>();
        var link = await linkRepo.GetByIdWithIncludesTrackingAsync(
            linkId,
            q => q.Include(l => l.connection).Include(l => l.product));

        if (link == null)
            throw ApiException.NotFound("Marketplace link not found");

        await ProductNotTrashed.EnsureAsync(_context, link.productId, ct);

        if (link.connection?.storeId != selectedStoreId.Value)
            throw ApiException.Forbidden("Access denied", code: "access_denied_store");

        var operationId = input?.OperationId ?? Guid.NewGuid().ToString("N");
        var storeId = selectedStoreId.Value;

        var opExists = !string.IsNullOrEmpty(input?.OperationId) &&
            await _context.operations.AsNoTracking().AnyAsync(o => o.operationId == operationId, ct);

        if (!opExists)
            await _operationLogger.StartAsync(operationId, storeId, userId, "single-import",
                $"Import: {link.product?.name ?? link.externalProductId}",
                connectionId: link.connectionId, connectionName: link.connection.name);

        // Get the adapter
        var adapter = _adapterRegistry.GetByKey(link.connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest($"Adapter '{link.connection.adapterKey}' not found");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportProduct))
            throw ApiException.BadRequest("Adapter does not support import");

        await _operationLogger.LogAsync(operationId, storeId, "info", "Fetching data from the marketplace...");

        // Get up-to-date data from the marketplace
        var externalProduct = await adapter.ImportProductAsync(link.connection, link.externalProductId, ct);
        if (externalProduct == null)
        {
            var missingMsg = $"Product with ID '{link.externalProductId}' not found on the marketplace";
            await _productSyncService.MarkLinkRemoteMissingAsync(link.Id, missingMsg, ct);
            await _operationLogger.LogAsync(operationId, storeId, "error", missingMsg);
            await _operationLogger.FinishAsync(operationId, storeId, "failed", missingMsg);
            throw ApiException.BadRequest(missingMsg, code: "remote_product_missing");
        }

        externalProduct = await _productSyncService.WithImportedPriceAsync(
            adapter, link.connection, externalProduct, ct);

        await _operationLogger.LogAsync(operationId, storeId, "info", "Data received, comparing fields...");

        // Compare and update the local product
        var updatedFields = new List<string>();
        var oldValues = new Dictionary<string, object?>();
        var newValues = new Dictionary<string, object?>();

        var productRepo = _unitOfWork.Repository<Product>();
        var product = link.product;

        if (product == null)
            throw ApiException.BadRequest("Local product not found", code: "product_not_found");

        // Update the name
        if (excludeFields?.Contains("name") != true &&
            !string.IsNullOrEmpty(externalProduct.Name) && product.name != externalProduct.Name)
        {
            oldValues["name"] = product.name;
            newValues["name"] = externalProduct.Name;
            product.name = externalProduct.Name;
            updatedFields.Add("name");
        }

        // Update the description
        if (excludeFields?.Contains("description") != true &&
            externalProduct.Description != null)
        {
            var description = Delegat.Marketplace.SDK.DescriptionFormatting.FromMarketplaceHtml(externalProduct.Description);
            if (description != null && Delegat.Marketplace.SDK.DescriptionFormatting.Differ(product.description, description))
            {
                oldValues["description"] = product.description;
                newValues["description"] = description;
                product.description = description;
                updatedFields.Add("description");
            }
        }

        // Update the price
        if (excludeFields?.Contains("price") != true &&
            externalProduct.Price.HasValue && product.price != externalProduct.Price.Value)
        {
            oldValues["price"] = product.price;
            newValues["price"] = externalProduct.Price.Value;
            product.price = externalProduct.Price.Value;
            updatedFields.Add("price");
        }

        // Update the stock quantity
        if (excludeFields?.Contains("storageQuantity") != true &&
            externalProduct.Quantity.HasValue && product.storageQuantity != externalProduct.Quantity.Value)
        {
            oldValues["storageQuantity"] = product.storageQuantity;
            newValues["storageQuantity"] = externalProduct.Quantity.Value;
            product.storageQuantity = externalProduct.Quantity.Value;
            updatedFields.Add("storageQuantity");
        }

        // Update barcode (valid EAN-8/13 from marketplace / variants)
        if (excludeFields?.Contains("barcode") != true &&
            MarketplaceImportBarcode.WouldChange(product, externalProduct, out var oldBarcode, out var newBarcode))
        {
            oldValues["barcode"] = oldBarcode;
            newValues["barcode"] = newBarcode;
            product.barcode = newBarcode;
            updatedFields.Add("barcode");
        }

        // Update attributes based on the category mapping
        // Build set of excluded attribute field names (strip "attr:" prefix)
        HashSet<string>? excludeAttrFieldNames = null;
        if (excludeFields != null)
        {
            excludeAttrFieldNames = new HashSet<string>();
            foreach (var ef in excludeFields)
            {
                if (ef.StartsWith("attr:"))
                    excludeAttrFieldNames.Add(ef[5..]);
            }
            if (excludeAttrFieldNames.Count == 0)
                excludeAttrFieldNames = null;
        }

        var importedAttributes = ImportedAttributeValue.Collect(externalProduct);
        if (importedAttributes.Count > 0 && product.categoryId.HasValue)
        {
            var attrChanges = await _productSyncService.UpdateProductAttributesFromExternalAsync(
                product.Id,
                link.connectionId,
                product.categoryId.Value,
                importedAttributes,
                ct,
                excludeAttrFieldNames);
            foreach (var change in attrChanges)
            {
                var key = $"attr:{change.FieldName}";
                updatedFields.Add(key);
                oldValues[key] = change.OldValue;
                newValues[key] = change.NewValue;
            }
        }

        // Update the link with up-to-date marketplace data
        link.externalPrice = externalProduct.Price;
        link.externalStock = externalProduct.Quantity;
        link.lastSyncAt = DateTime.UtcNow;
        link.status = ProductMarketplaceStatus.Synced;
        link.lastErrorMessage = null;

        await _unitOfWork.SaveChangesAsync(ct);

        var summary = updatedFields.Count > 0
            ? $"Fields updated: {updatedFields.Count}"
            : "No changes detected";
        await _operationLogger.FinishAsync(operationId, storeId, "completed", summary);

        return Ok(new
        {
            operationId,
            success = true,
            updatedFields,
            oldValues,
            newValues,
            message = summary
        });
    }

    /// <summary>
    /// Preview import changes from the marketplace (read-only, no save)
    /// </summary>
    [HttpPost]
    [Route("/marketplace/links/{linkId}/import-preview")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> ImportFromLinkPreview(int linkId, CancellationToken ct)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var linkRepo = _unitOfWork.Repository<ProductMarketplaceLink>();
        var link = await linkRepo.FindAsync(
            l => l.Id == linkId,
            q => q.Include(l => l.connection).Include(l => l.product));

        if (link == null)
            throw ApiException.NotFound("Marketplace link not found");

        await ProductNotTrashed.EnsureAsync(_context, link.productId, ct);

        if (link.connection?.storeId != selectedStoreId.Value)
            throw ApiException.Forbidden("Access denied", code: "access_denied_store");

        var adapter = _adapterRegistry.GetByKey(link.connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest($"Adapter '{link.connection.adapterKey}' not found");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportProduct))
            throw ApiException.BadRequest("Adapter does not support import");

        var externalProduct = await adapter.ImportProductAsync(link.connection, link.externalProductId, ct);
        if (externalProduct == null)
        {
            var missingMsg = $"Product with ID '{link.externalProductId}' not found on the marketplace";
            await _productSyncService.MarkLinkRemoteMissingAsync(link.Id, missingMsg, ct);
            throw ApiException.BadRequest(missingMsg, code: "remote_product_missing");
        }

        externalProduct = await _productSyncService.WithImportedPriceAsync(
            adapter, link.connection, externalProduct, ct);

        var updatedFields = new List<string>();
        var oldValues = new Dictionary<string, object?>();
        var newValues = new Dictionary<string, object?>();
        var product = link.product;

        if (product == null)
            throw ApiException.BadRequest("Local product not found", code: "product_not_found");

        if (!string.IsNullOrEmpty(externalProduct.Name) && product.name != externalProduct.Name)
        {
            oldValues["name"] = product.name;
            newValues["name"] = externalProduct.Name;
            updatedFields.Add("name");
        }

        if (externalProduct.Description != null)
        {
            var description = Delegat.Marketplace.SDK.DescriptionFormatting.FromMarketplaceHtml(externalProduct.Description);
            if (description != null && Delegat.Marketplace.SDK.DescriptionFormatting.Differ(product.description, description))
            {
                oldValues["description"] = product.description;
                newValues["description"] = description;
                updatedFields.Add("description");
            }
        }

        if (externalProduct.Price.HasValue && product.price != externalProduct.Price.Value)
        {
            oldValues["price"] = product.price;
            newValues["price"] = externalProduct.Price.Value;
            updatedFields.Add("price");
        }

        if (externalProduct.Quantity.HasValue && product.storageQuantity != externalProduct.Quantity.Value)
        {
            oldValues["storageQuantity"] = product.storageQuantity;
            newValues["storageQuantity"] = externalProduct.Quantity.Value;
            updatedFields.Add("storageQuantity");
        }

        if (MarketplaceImportBarcode.WouldChange(product, externalProduct, out var previewOldBarcode, out var previewNewBarcode))
        {
            oldValues["barcode"] = previewOldBarcode;
            newValues["barcode"] = previewNewBarcode;
            updatedFields.Add("barcode");
        }

        var irreversibleFields = new List<string>();

        var importedAttributes = ImportedAttributeValue.Collect(externalProduct);
        if (importedAttributes.Count > 0 && product.categoryId.HasValue)
        {
            var attrChanges = await _productSyncService.GetAttributeChangesDetailedAsync(
                product.Id,
                link.connectionId,
                product.categoryId.Value,
                importedAttributes,
                ct);
            foreach (var change in attrChanges)
            {
                var key = $"attr:{change.FieldName}";
                updatedFields.Add(key);
                oldValues[key] = change.OldValue;
                newValues[key] = change.NewValue;
                if (change.HasIrreversibleModifiers)
                    irreversibleFields.Add(key);
            }
        }

        return Ok(new
        {
            hasChanges = updatedFields.Count > 0,
            updatedFields,
            oldValues,
            newValues,
            irreversibleFields
        });
    }

    public record ImportImagesFromLinkInput(List<string>? Urls = null, string? OperationId = null);

    /// <summary>
    /// Preview marketplace photos for a linked product (append-only; no downloads).
    /// </summary>
    [HttpPost]
    [Route("/marketplace/links/{linkId}/import-images-preview")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> ImportImagesFromLinkPreview(int linkId, CancellationToken ct)
    {
        var (link, externalProduct) = await LoadImportableLinkProductAsync(linkId, ct);
        var preview = await _productSyncService.PreviewImportImagesAsync(
            link.productId, externalProduct.ImageUrls, ct);

        return Ok(new
        {
            images = preview.Images.Select(i => new { url = i.Url, status = i.Status }),
            localGalleryCount = preview.LocalGalleryCount
        });
    }

    /// <summary>
    /// Queue selected marketplace photos for background download (append-only).
    /// </summary>
    [HttpPost]
    [Route("/marketplace/links/{linkId}/import-images")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> ImportImagesFromLink(
        int linkId,
        [FromBody] ImportImagesFromLinkInput? input,
        CancellationToken ct)
    {
        var userId = _currentUserService.GetUserId() ?? 0;
        var (link, externalProduct) = await LoadImportableLinkProductAsync(linkId, ct);
        var storeId = link.connection!.storeId;

        var operationId = input?.OperationId ?? Guid.NewGuid().ToString("N");
        var opExists = !string.IsNullOrEmpty(input?.OperationId) &&
            await _context.operations.AsNoTracking().AnyAsync(o => o.operationId == operationId, ct);

        if (!opExists)
        {
            await _operationLogger.StartAsync(operationId, storeId, userId, "image-download",
                $"Import photos: {link.product?.name ?? link.externalProductId}",
                connectionId: link.connectionId, connectionName: link.connection.name,
                batchId: operationId, batchType: "image-download", totalItems: 0);
        }
        else
        {
            var existingOp = await _context.operations
                .FirstOrDefaultAsync(o => o.operationId == operationId, ct);
            if (existingOp != null)
            {
                if (string.IsNullOrEmpty(existingOp.batchType))
                    existingOp.batchType = "image-download";
                if (string.IsNullOrEmpty(existingOp.batchId))
                    existingOp.batchId = operationId;
                await _context.SaveChangesAsync(ct);
            }
        }

        var result = await _productSyncService.QueueImportImagesAsync(
            link.productId,
            storeId,
            userId,
            input?.Urls,
            externalProduct.ImageUrls,
            operationId,
            ct);

        if (result.QueuedCount == 0)
        {
            await _operationLogger.FinishAsync(operationId, storeId, "completed", "No photos to download");
        }
        else
        {
            var existingOp = await _context.operations
                .FirstOrDefaultAsync(o => o.operationId == operationId, ct);
            if (existingOp != null)
            {
                existingOp.totalItems = result.QueuedCount;
                existingOp.title = $"Downloading {result.QueuedCount} images";
                await _context.SaveChangesAsync(ct);
            }

            await _operationLogger.LogAsync(operationId, storeId, "info",
                $"Queued {result.QueuedCount} photos for download (skipped {result.SkippedCount})");
        }

        return Ok(new
        {
            operationId,
            success = true,
            queued = result.QueuedCount,
            skipped = result.SkippedCount
        });
    }

    private async Task<(ProductMarketplaceLink Link, Models.Marketplace.DTOs.ExternalProduct External)>
        LoadImportableLinkProductAsync(int linkId, CancellationToken ct)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var linkRepo = _unitOfWork.Repository<ProductMarketplaceLink>();
        var link = await linkRepo.FindAsync(
            l => l.Id == linkId,
            q => q.Include(l => l.connection).Include(l => l.product));

        if (link == null)
            throw ApiException.NotFound("Marketplace link not found");

        await ProductNotTrashed.EnsureAsync(_context, link.productId, ct);

        if (link.connection?.storeId != selectedStoreId.Value)
            throw ApiException.Forbidden("Access denied", code: "access_denied_store");

        var adapter = _adapterRegistry.GetByKey(link.connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest($"Adapter '{link.connection.adapterKey}' not found");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportProduct))
            throw ApiException.BadRequest("Adapter does not support import");

        var externalProduct = await adapter.ImportProductAsync(link.connection, link.externalProductId, ct);
        if (externalProduct == null)
        {
            var missingMsg = $"Product with ID '{link.externalProductId}' not found on the marketplace";
            await _productSyncService.MarkLinkRemoteMissingAsync(link.Id, missingMsg, ct);
            throw ApiException.BadRequest(missingMsg, code: "remote_product_missing");
        }

        return (link, externalProduct);
    }

    /// <summary>
    /// Bulk preview import changes from the marketplace for multiple links.
    /// Groups links by connection and uses batch API for efficiency.
    /// </summary>
    [HttpPost]
    [Route("/marketplace/links/import-bulk-preview")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> ImportLinksBulkPreview(
        [FromBody] LinksBulkInput input,
        CancellationToken ct)
    {
        if (input.linkIds == null || input.linkIds.Count == 0)
            throw ApiException.BadRequest("No links specified", code: "required_fields_missing");

        await ProductNotTrashed.EnsureLinksAsync(_context, input.linkIds, ct);

        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        // Batch-load all links in one query
        var allLinks = await _context.productMarketplaceLinks
            .AsNoTracking()
            .Include(l => l.connection)
            .Include(l => l.product)
            .Where(l => input.linkIds.Contains(l.Id))
            .ToListAsync(ct);

        var linkMap = allLinks.ToDictionary(l => l.Id);

        // Group links by connection for batch API calls
        var grouped = allLinks
            .Where(l => l.connection?.storeId == selectedStoreId.Value)
            .GroupBy(l => l.connectionId)
            .ToList();

        // Fetch external products in batch per connection
        var externalProductMap = new Dictionary<string, Models.Marketplace.DTOs.ExternalProduct>();

        foreach (var group in grouped)
        {
            var connection = group.First().connection!;
            var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
            if (adapter == null || !adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportProduct))
                continue;

            var externalIds = group.Select(l => l.externalProductId).Distinct().ToList();
            try
            {
                var batchResult = await adapter.ImportProductsBatchAsync(connection, externalIds, ct);
                batchResult = await _productSyncService.WithImportedPricesAsync(
                    adapter, connection, batchResult, ct);
                foreach (var ep in batchResult)
                    externalProductMap[ep.ExternalId] = ep;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Batch import failed for connection {ConnectionId}, falling back", connection.Id);
            }
        }

        // Build preview results per link
        var results = new List<BulkPreviewItemResult>();
        var empty = new Dictionary<string, object?>();

        foreach (var linkId in input.linkIds)
        {
            if (!linkMap.TryGetValue(linkId, out var link) || link.connection?.storeId != selectedStoreId.Value)
            {
                results.Add(new BulkPreviewItemResult(linkId, null, false, [], empty, empty, "Marketplace link not found"));
                continue;
            }

            if (!externalProductMap.TryGetValue(link.externalProductId, out var externalProduct))
            {
                results.Add(new BulkPreviewItemResult(linkId, link.product?.name, false, [], empty, empty, "Product not found on the marketplace"));
                continue;
            }

            var updatedFields = new List<string>();
            var oldValues = new Dictionary<string, object?>();
            var newValues = new Dictionary<string, object?>();
            var product = link.product;

            if (product != null)
            {
                if (!string.IsNullOrEmpty(externalProduct.Name) && product.name != externalProduct.Name)
                {
                    oldValues["name"] = product.name;
                    newValues["name"] = externalProduct.Name;
                    updatedFields.Add("name");
                }

                if (externalProduct.Description != null)
                {
                    var description = Delegat.Marketplace.SDK.DescriptionFormatting.FromMarketplaceHtml(externalProduct.Description);
                    if (description != null && Delegat.Marketplace.SDK.DescriptionFormatting.Differ(product.description, description))
                    {
                        oldValues["description"] = product.description;
                        newValues["description"] = description;
                        updatedFields.Add("description");
                    }
                }

                if (externalProduct.Price.HasValue && product.price != externalProduct.Price.Value)
                {
                    oldValues["price"] = product.price;
                    newValues["price"] = externalProduct.Price.Value;
                    updatedFields.Add("price");
                }

                if (externalProduct.Quantity.HasValue && product.storageQuantity != externalProduct.Quantity.Value)
                {
                    oldValues["storageQuantity"] = product.storageQuantity;
                    newValues["storageQuantity"] = externalProduct.Quantity.Value;
                    updatedFields.Add("storageQuantity");
                }

                if (MarketplaceImportBarcode.WouldChange(product, externalProduct, out var bulkPreviewOldBarcode, out var bulkPreviewNewBarcode))
                {
                    oldValues["barcode"] = bulkPreviewOldBarcode;
                    newValues["barcode"] = bulkPreviewNewBarcode;
                    updatedFields.Add("barcode");
                }

                var importedAttributes = ImportedAttributeValue.Collect(externalProduct);
                if (importedAttributes.Count > 0 && product.categoryId.HasValue)
                {
                    var attrChanges = await _productSyncService.GetAttributeChangesDetailedAsync(
                        product.Id,
                        link.connectionId,
                        product.categoryId.Value,
                        importedAttributes,
                        ct);
                    foreach (var change in attrChanges)
                    {
                        var key = $"attr:{change.FieldName}";
                        updatedFields.Add(key);
                        oldValues[key] = change.OldValue;
                        newValues[key] = change.NewValue;
                    }
                }
            }

            var hasChanges = updatedFields.Count > 0;
            results.Add(new BulkPreviewItemResult(linkId, product?.name, hasChanges, updatedFields, oldValues, newValues, null));
        }

        int totalWithChanges = results.Count(r => r.hasChanges);
        int totalWithoutChanges = results.Count(r => !r.hasChanges);

        return Ok(new
        {
            items = results,
            totalWithChanges,
            totalWithoutChanges
        });
    }

    private record BulkPreviewItemResult(
        int linkId,
        string? productName,
        bool hasChanges,
        List<string> updatedFields,
        Dictionary<string, object?> oldValues,
        Dictionary<string, object?> newValues,
        string? error);

    public record ExportFromLinkInput(string? OperationId = null);

    /// <summary>
    /// Export local changes to the marketplace
    /// </summary>
    [HttpPost]
    [Route("/marketplace/links/{linkId}/export")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> ExportFromLink(int linkId, [FromBody] ExportFromLinkInput? input, CancellationToken ct)
    {
        var userId = _currentUserService.GetUserId() ?? 0;
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        // Get the marketplace link
        var linkRepo = _unitOfWork.Repository<ProductMarketplaceLink>();
        var link = await linkRepo.FindAsync(
            l => l.Id == linkId,
            q => q.Include(l => l.connection).Include(l => l.product));

        if (link == null)
            throw ApiException.NotFound("Marketplace link not found");

        await ProductNotTrashed.EnsureAsync(_context, link.productId, ct);

        if (link.connection?.storeId != selectedStoreId.Value)
            throw ApiException.Forbidden("Access denied", code: "access_denied_store");

        if (link.product == null)
            throw ApiException.BadRequest("Local product not found", code: "product_not_found");

        var operationId = input?.OperationId ?? Guid.NewGuid().ToString("N");
        var storeId = selectedStoreId.Value;

        var opExists = !string.IsNullOrEmpty(input?.OperationId) &&
            await _context.operations.AsNoTracking().AnyAsync(o => o.operationId == operationId, ct);

        if (!opExists)
            await _operationLogger.StartAsync(operationId, storeId, userId, "single-export",
                $"Export: {link.product.name ?? link.externalProductId}",
                connectionId: link.connectionId, connectionName: link.connection.name);

        // Export through the service
        var result = await _productSyncService.ExportProductAsync(
            link.productId, link.connectionId, ct,
            operationId: operationId, operationStoreId: storeId);

        await _operationLogger.FinishAsync(operationId, storeId,
            result.IsSuccess ? "completed" : "failed",
            result.IsSuccess ? (result.Message ?? "Product exported") : result.ErrorMessage);

        if (!result.IsSuccess)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Export failed", code: "export_failed");

        return Ok(new
        {
            operationId,
            success = true,
            externalProductId = result.ExternalProductId,
            message = result.Message ?? "Product exported successfully"
        });
    }

    public struct RecreateInput { public string newSku { get; set; } }

    /// <summary>
    /// Recreate product on marketplace with a new SKU: export as new, archive old
    /// </summary>
    [HttpPost]
    [Route("/marketplace/links/{linkId}/recreate")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> RecreateProductOnMarketplace(int linkId, [FromBody] RecreateInput input, CancellationToken ct)
    {
        // 1. Validate
        if (string.IsNullOrWhiteSpace(input.newSku) || input.newSku.Length > 50)
            throw ApiException.BadRequest("New SKU is required (max 50 characters)", code: "required_fields_missing");

        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        // 2. Load link with connection and product (tracking)
        var link = await _context.productMarketplaceLinks
            .Include(l => l.connection)
            .Include(l => l.product)
            .FirstOrDefaultAsync(l => l.Id == linkId, ct);

        if (link == null)
            throw ApiException.NotFound("Marketplace link not found");
        if (link.connection?.storeId != selectedStoreId.Value)
            throw ApiException.Forbidden("Access denied", code: "access_denied_store");
        await ProductNotTrashed.EnsureAsync(_context, link.productId, ct);
        if (link.product == null)
            throw ApiException.BadRequest("Local product not found", code: "product_not_found");

        // 3. Check adapter capabilities
        var adapter = _adapterRegistry.GetByKey(link.connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest("Marketplace adapter not found");
        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ExportProduct))
            throw ApiException.BadRequest("Adapter does not support exporting products");
        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ArchiveProduct))
            throw ApiException.BadRequest("Adapter does not support archiving products");

        // 4. Save old data
        var oldExternalProductId = link.externalProductId;
        var oldExternalSku = link.externalSku;
        var originalSku = link.product.sku;
        var productId = link.productId;
        var connectionId = link.connectionId;

        // 5. Transaction: remove old link + temporarily set product.sku = newSku
        try
        {
            // Remove the old link so ExportProductAsync creates a new product
            _context.productMarketplaceLinks.Remove(link);
            // Temporarily change product SKU for export
            link.product.sku = input.newSku;
            await _context.SaveChangesAsync(ct);

            // 6. Export as new product
            var exportResult = await _productSyncService.ExportProductAsync(productId, connectionId, ct);

            if (!exportResult.IsSuccess)
            {
                // Rollback: restore product SKU and re-create old link
                link.product.sku = originalSku;
                _context.productMarketplaceLinks.Add(new ProductMarketplaceLink
                {
                    productId = productId,
                    connectionId = connectionId,
                    externalProductId = oldExternalProductId,
                    externalSku = oldExternalSku,
                    status = ProductMarketplaceStatus.SyncError
                });
                await _context.SaveChangesAsync(ct);
                throw ApiException.BadRequest(exportResult.ErrorMessage ?? "Failed to export the new product", code: "export_failed");
            }

            // 7. Success: restore product.sku + update new link
            link.product.sku = originalSku;
            await _context.SaveChangesAsync(ct);

            // Update the newly created link's externalSku
            var newLink = await _context.productMarketplaceLinks
                .FirstOrDefaultAsync(l => l.productId == productId && l.connectionId == connectionId, ct);
            if (newLink != null)
            {
                newLink.externalSku = input.newSku;
                await _context.SaveChangesAsync(ct);
            }

            // 8. Archive old product (best effort - don't fail the operation)
            string? archiveError = null;
            try
            {
                var archiveResult = await adapter.ArchiveProductAsync(link.connection, oldExternalProductId, ct);
                if (!archiveResult.Success)
                    archiveError = archiveResult.ErrorMessage;
            }
            catch (Exception ex)
            {
                archiveError = ex.Message;
                _logger.LogWarning(ex, "Failed to archive old product {ExternalId} on {Adapter}",
                    oldExternalProductId, link.connection.adapterKey);
            }

            return Ok(new
            {
                success = true,
                newExternalProductId = exportResult.ExternalProductId,
                newExternalSku = input.newSku,
                archiveError
            });
        }
        catch (ApiException)
        {
            throw; // re-throw API exceptions
        }
        catch (Exception ex)
        {
            // Critical failure - try to restore
            _logger.LogError(ex, "Recreate product failed for link {LinkId}", linkId);

            try
            {
                var product = await _context.products.FindAsync(new object[] { productId }, ct);
                if (product != null && product.sku != originalSku)
                {
                    product.sku = originalSku;
                }
                // Check if the old link was lost
                var existingLink = await _context.productMarketplaceLinks
                    .AnyAsync(l => l.productId == productId && l.connectionId == connectionId, ct);
                if (!existingLink)
                {
                    _context.productMarketplaceLinks.Add(new ProductMarketplaceLink
                    {
                        productId = productId,
                        connectionId = connectionId,
                        externalProductId = oldExternalProductId,
                        externalSku = oldExternalSku,
                        status = ProductMarketplaceStatus.SyncError
                    });
                }
                await _context.SaveChangesAsync(ct);
            }
            catch (Exception rollbackEx)
            {
                _logger.LogError(rollbackEx, "Rollback also failed for link {LinkId}", linkId);
            }

            throw ApiException.BadRequest($"Failed to recreate: {ex.Message}");
        }
    }

    /// <summary>
    /// Bulk import changes from the marketplace for multiple linked products.
    /// Groups links by connection and uses batch API for efficiency.
    /// </summary>
    [HttpPost]
    [Route("/marketplace/links/import-bulk")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> ImportLinksBulk(
        [FromBody] LinksBulkInput input,
        CancellationToken ct)
    {
        if (input.linkIds == null || input.linkIds.Count == 0)
            throw ApiException.BadRequest("No links specified", code: "required_fields_missing");

        await ProductNotTrashed.EnsureLinksAsync(_context, input.linkIds, ct);

        var userId = _currentUserService.GetUserId() ?? 0;
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var operationId = Guid.NewGuid().ToString("N");
        var storeId = selectedStoreId.Value;
        await _operationLogger.StartAsync(operationId, storeId, userId, "batch-import",
            $"Import changes ({input.linkIds.Count} products)", totalItems: input.linkIds.Count);

        var linkRepo = _unitOfWork.Repository<ProductMarketplaceLink>();

        // Load all links with tracking for updates
        var allLinks = await _context.productMarketplaceLinks
            .Include(l => l.connection)
            .Include(l => l.product)
            .Where(l => input.linkIds.Contains(l.Id))
            .ToListAsync(ct);

        await _operationLogger.LogAsync(operationId, storeId, "info", $"Loading data for {allLinks.Count} products...");

        var linkMap = allLinks.ToDictionary(l => l.Id);

        // Group links by connection for batch API calls
        var grouped = allLinks
            .Where(l => l.connection?.storeId == selectedStoreId.Value)
            .GroupBy(l => l.connectionId)
            .ToList();

        // Fetch external products in batch per connection
        var externalProductMap = new Dictionary<string, Models.Marketplace.DTOs.ExternalProduct>();

        foreach (var group in grouped)
        {
            var connection = group.First().connection!;
            var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
            if (adapter == null || !adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportProduct))
                continue;

            var externalIds = group.Select(l => l.externalProductId).Distinct().ToList();
            try
            {
                var batchResult = await adapter.ImportProductsBatchAsync(connection, externalIds, ct);
                batchResult = await _productSyncService.WithImportedPricesAsync(
                    adapter, connection, batchResult, ct);
                foreach (var ep in batchResult)
                    externalProductMap[ep.ExternalId] = ep;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Batch import failed for connection {ConnectionId}", connection.Id);
            }
        }

        await _operationLogger.LogAsync(operationId, storeId, "info", $"Data received: {externalProductMap.Count} products");

        // Apply updates per link
        var results = new List<object>();
        int successCount = 0;
        int failedCount = 0;

        foreach (var linkId in input.linkIds)
        {
            try
            {
                if (!linkMap.TryGetValue(linkId, out var link) || link.connection?.storeId != selectedStoreId.Value)
                {
                    results.Add(new { linkId, success = false, error = "Marketplace link not found or access denied" });
                    failedCount++;
                    continue;
                }

                if (!externalProductMap.TryGetValue(link.externalProductId, out var externalProduct))
                {
                    results.Add(new { linkId, success = false, error = "Product not found on the marketplace" });
                    failedCount++;
                    continue;
                }

                var updatedFields = new List<string>();
                var product = link.product;

                if (product != null)
                {
                    if (!string.IsNullOrEmpty(externalProduct.Name) && product.name != externalProduct.Name)
                    {
                        product.name = externalProduct.Name;
                        updatedFields.Add("name");
                    }

                    if (externalProduct.Description != null)
                    {
                        var description = Delegat.Marketplace.SDK.DescriptionFormatting.FromMarketplaceHtml(externalProduct.Description);
                        if (description != null && Delegat.Marketplace.SDK.DescriptionFormatting.Differ(product.description, description))
                        {
                            product.description = description;
                            updatedFields.Add("description");
                        }
                    }

                    if (externalProduct.Price.HasValue && product.price != externalProduct.Price.Value)
                    {
                        product.price = externalProduct.Price.Value;
                        updatedFields.Add("price");
                    }

                    if (externalProduct.Quantity.HasValue && product.storageQuantity != externalProduct.Quantity.Value)
                    {
                        product.storageQuantity = externalProduct.Quantity.Value;
                        updatedFields.Add("storageQuantity");
                    }

                    if (MarketplaceImportBarcode.WouldChange(product, externalProduct, out _, out var bulkNewBarcode))
                    {
                        product.barcode = bulkNewBarcode;
                        updatedFields.Add("barcode");
                    }

                    var importedAttributes = ImportedAttributeValue.Collect(externalProduct);
                    if (importedAttributes.Count > 0 && product.categoryId.HasValue)
                    {
                        var attrChanges = await _productSyncService.UpdateProductAttributesFromExternalAsync(
                            product.Id,
                            link.connectionId,
                            product.categoryId.Value,
                            importedAttributes,
                            ct);
                        foreach (var change in attrChanges)
                            updatedFields.Add($"attr:{change.FieldName}");
                    }
                }

                link.externalPrice = externalProduct.Price;
                link.externalStock = externalProduct.Quantity;
                link.lastSyncAt = DateTime.UtcNow;
                link.status = ProductMarketplaceStatus.Synced;
                link.lastErrorMessage = null;

                results.Add(new { linkId, success = true, updatedFields });
                successCount++;
                await _operationLogger.LogAsync(operationId, storeId, "success",
                    product != null
                        ? $"Imported: {OperationProductRef.Format(product.Id, product.name)}"
                        : $"Imported: {link.externalProductId}");
                await _operationLogger.ProgressAsync(operationId, storeId, input.linkIds.Count, successCount, failedCount);
            }
            catch (Exception ex)
            {
                results.Add(new { linkId, success = false, error = ex.Message });
                failedCount++;
                await _operationLogger.LogAsync(operationId, storeId, "error", $"Error: {ex.Message}");
                await _operationLogger.ProgressAsync(operationId, storeId, input.linkIds.Count, successCount, failedCount);
            }
        }

        await _unitOfWork.SaveChangesAsync(ct);

        await _operationLogger.FinishAsync(operationId, storeId,
            failedCount == 0 ? "completed" : (successCount == 0 ? "failed" : "partial"),
            $"Imported: {successCount}, errors: {failedCount}");

        return Ok(new { operationId, results, successCount, failedCount });
    }

    /// <summary>
    /// Bulk export local changes to the marketplace for multiple linked products
    /// </summary>
    [HttpPost]
    [Route("/marketplace/links/export-bulk")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> ExportLinksBulk(
        [FromBody] LinksBulkInput input,
        CancellationToken ct)
    {
        if (input.linkIds == null || input.linkIds.Count == 0)
            throw ApiException.BadRequest("No links specified", code: "required_fields_missing");

        await ProductNotTrashed.EnsureLinksAsync(_context, input.linkIds, ct);

        var userId = _currentUserService.GetUserId() ?? 0;
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var bulkOpId = Guid.NewGuid().ToString("N");
        var bulkStoreId = selectedStoreId.Value;
        await _operationLogger.StartAsync(bulkOpId, bulkStoreId, userId, "batch-export",
            $"Export changes ({input.linkIds.Count} products)", totalItems: input.linkIds.Count);

        var linkRepo = _unitOfWork.Repository<ProductMarketplaceLink>();

        // Load all links in one query
        var links = await linkRepo.FindAllAsync(
            l => input.linkIds.Contains(l.Id),
            q => q.Include(l => l.connection));

        await _operationLogger.LogAsync(bulkOpId, bulkStoreId, "info", $"Exporting {links.Count()} products...");

        var results = new List<object>();
        int successCount = 0;
        int failedCount = 0;

        // Report missing/unauthorized links
        var foundLinkIds = links.Select(l => l.Id).ToHashSet();
        foreach (var linkId in input.linkIds.Where(id => !foundLinkIds.Contains(id)))
        {
            results.Add(new { linkId, success = false, error = "Marketplace link not found or access denied" });
            failedCount++;
        }

        // Filter by store access
        var authorizedLinks = links.Where(l => l.connection?.storeId == selectedStoreId.Value).ToList();
        foreach (var link in links.Where(l => l.connection?.storeId != selectedStoreId.Value))
        {
            results.Add(new { linkId = link.Id, success = false, error = "Marketplace link not found or access denied" });
            failedCount++;
        }

        // Group by connectionId and call bulk export per group
        var groups = authorizedLinks.GroupBy(l => l.connectionId);
        foreach (var group in groups)
        {
            var connectionId = group.Key;
            var groupLinks = group.ToList();
            var productIds = groupLinks.Select(l => l.productId).Distinct().ToList();

            // Build productId -> linkIds mapping (multiple links can reference same product)
            var linksByProductId = groupLinks.GroupBy(l => l.productId)
                .ToDictionary(g => g.Key, g => g.Select(l => l.Id).ToList());

            try
            {
                var bulkResult = await _productSyncService.ExportProductsBulkAsync(productIds, connectionId, ct);

                foreach (var item in bulkResult.Items)
                {
                    if (!linksByProductId.TryGetValue(item.ProductId, out var relatedLinkIds))
                        continue;

                    foreach (var linkId in relatedLinkIds)
                    {
                        if (item.IsSuccess)
                        {
                            results.Add(new { linkId, success = true, externalProductId = item.ExternalProductId });
                            successCount++;
                            await _operationLogger.LogAsync(bulkOpId, bulkStoreId, "success",
                                $"Exported: {OperationProductRef.Format(item.ProductId, null)}");
                            var details = FormatExportDetails(item.Details);
                            if (details != null)
                                await _operationLogger.LogAsync(bulkOpId, bulkStoreId, "info",
                                    $"{OperationProductRef.Format(item.ProductId, null)}: {details}");
                        }
                        else
                        {
                            results.Add(new { linkId, success = false, error = item.ErrorMessage });
                            failedCount++;
                            await _operationLogger.LogAsync(bulkOpId, bulkStoreId, "error",
                                $"Error: {OperationProductRef.Format(item.ProductId, null)} - {item.ErrorMessage}");
                            var details = FormatExportDetails(item.Details);
                            if (details != null)
                                await _operationLogger.LogAsync(bulkOpId, bulkStoreId, "error",
                                    $"{OperationProductRef.Format(item.ProductId, null)}: {details}");
                        }
                    }
                }
                await _operationLogger.ProgressAsync(bulkOpId, bulkStoreId, input.linkIds.Count, successCount, failedCount);
            }
            catch (Exception ex)
            {
                foreach (var link in groupLinks)
                {
                    results.Add(new { linkId = link.Id, success = false, error = ex.Message });
                    failedCount++;
                }
                await _operationLogger.LogAsync(bulkOpId, bulkStoreId, "error", $"Group error: {ex.Message}");
                await _operationLogger.ProgressAsync(bulkOpId, bulkStoreId, input.linkIds.Count, successCount, failedCount);
            }
        }

        await _operationLogger.FinishAsync(bulkOpId, bulkStoreId,
            failedCount == 0 ? "completed" : (successCount == 0 ? "failed" : "partial"),
            $"Exported: {successCount}, errors: {failedCount}");

        return Ok(new { operationId = bulkOpId, results, successCount, failedCount });
    }

    #endregion

    #region Product Variants

    /// <summary>
    /// Get all variants for a product
    /// </summary>
    [HttpGet]
    [Route("/products/{productId}/variants")]
    public async Task<IActionResult> GetProductVariants(int productId, CancellationToken ct)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var product = await _context.products
            .FirstOrDefaultAsync(p => p.Id == productId && p.storeId == selectedStoreId.Value, ct);
        if (product == null)
            throw ApiException.NotFound("Product not found", code: "product_not_found");

        var variants = await _context.productVariants
            .Where(v => v.productId == productId)
            .OrderBy(v => v.sortOrder)
            .Select(v => new
            {
                v.Id,
                v.productId,
                v.sku,
                v.barcode,
                v.size,
                v.price,
                v.quantity,
                v.sortOrder,
                v.createdAt,
                v.updatedAt
            })
            .ToListAsync(ct);

        return Ok(variants);
    }

    /// <summary>
    /// Create a new product variant
    /// </summary>
    [HttpPost]
    [Route("/products/{productId}/variants")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> CreateProductVariant(
        int productId,
        [FromBody] CreateVariantInput input,
        CancellationToken ct)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var product = await _context.products
            .FirstOrDefaultAsync(p => p.Id == productId && p.storeId == selectedStoreId.Value, ct);
        if (product == null)
            throw ApiException.NotFound("Product not found", code: "product_not_found");

        // Determine next sort order
        var maxSortOrder = await _context.productVariants
            .Where(v => v.productId == productId)
            .MaxAsync(v => (int?)v.sortOrder, ct) ?? -1;

        var variant = new ProductVariant();
        variant.AddFill(new ProductVariant.AddForm
        {
            productId = productId,
            sku = input.sku,
            barcode = input.barcode,
            size = input.size,
            price = input.price,
            quantity = input.quantity,
            sortOrder = maxSortOrder + 1
        });

        _context.productVariants.Add(variant);
        await _context.SaveChangesAsync(ct);

        return Ok(new { id = variant.Id });
    }

    /// <summary>
    /// Update a product variant
    /// </summary>
    [HttpPut]
    [Route("/products/{productId}/variants/{variantId}")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> UpdateProductVariant(
        int productId,
        int variantId,
        [FromBody] UpdateVariantInput input,
        CancellationToken ct)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var product = await _context.products
            .FirstOrDefaultAsync(p => p.Id == productId && p.storeId == selectedStoreId.Value, ct);
        if (product == null)
            throw ApiException.NotFound("Product not found", code: "product_not_found");

        var variant = await _context.productVariants
            .FirstOrDefaultAsync(v => v.Id == variantId && v.productId == productId, ct);
        if (variant == null)
            throw ApiException.NotFound("Variant not found");

        variant.UpdateFill(new ProductVariant.UpdateForm
        {
            sku = input.sku,
            barcode = input.barcode,
            size = input.size,
            price = input.price,
            quantity = input.quantity,
            sortOrder = input.sortOrder
        });

        await _context.SaveChangesAsync(ct);

        return Ok(new { success = true });
    }

    /// <summary>
    /// Delete a product variant
    /// </summary>
    [HttpDelete]
    [Route("/products/{productId}/variants/{variantId}")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> DeleteProductVariant(
        int productId,
        int variantId,
        CancellationToken ct)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var product = await _context.products
            .FirstOrDefaultAsync(p => p.Id == productId && p.storeId == selectedStoreId.Value, ct);
        if (product == null)
            throw ApiException.NotFound("Product not found", code: "product_not_found");

        var variant = await _context.productVariants
            .FirstOrDefaultAsync(v => v.Id == variantId && v.productId == productId, ct);
        if (variant == null)
            throw ApiException.NotFound("Variant not found");

        _context.productVariants.Remove(variant);
        await _context.SaveChangesAsync(ct);

        return Ok(new { success = true });
    }

    #endregion

    #region Helpers

    private async Task<MarketplaceConnection> GetConnectionAsync(int connectionId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var repo = _unitOfWork.Repository<MarketplaceConnection>();

        var connection = await repo.FindAsync(
            c => c.Id == connectionId && c.storeId == selectedStoreId.Value);

        if (connection == null)
            throw ApiException.NotFound("Connection not found", code: "connection_not_found");

        return connection;
    }

    private static readonly JsonSerializerOptions ExportDetailsJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static string? FormatExportDetails(Dictionary<string, object>? details)
    {
        if (details == null || details.Count == 0)
            return null;

        try
        {
            if (details.TryGetValue("images", out var images))
                return $"Image export details: {JsonSerializer.Serialize(images, ExportDetailsJsonOptions)}";

            return $"Export details: {JsonSerializer.Serialize(details, ExportDetailsJsonOptions)}";
        }
        catch
        {
            return "Export details were received but could not be serialized for the log";
        }
    }

    #endregion

    #region Input DTOs

    public struct LinkProductInput
    {
        public string externalProductId { get; set; }
    }

    public struct ExportBulkInput
    {
        public List<int>? productIds { get; set; }
        /// <summary>When true and productIds is empty, queue every live catalog card.</summary>
        public bool allMatching { get; set; }
        /// <summary>
        /// sql_readonly WHERE on products. Examples: <c>price &gt;= 1000</c>,
        /// <c>id IN (SELECT productId FROM product_images)</c>.
        /// Selects the matching live catalog when productIds is empty.
        /// </summary>
        public string? where { get; set; }
        /// <summary>Kept for older clients. Prefer <see cref="where"/>.</summary>
        public bool skipWithoutImages { get; set; }
    }

    public struct ImportProductInput
    {
        public int? targetCategoryId { get; set; }
    }

    public struct ImportAllInput
    {
        public int? targetCategoryId { get; set; }
        public int? pageSize { get; set; }
        public int? maxProducts { get; set; }
    }

    public struct ImportSelectedInput
    {
        public List<ImportSelectedProductInput> products { get; set; }
        public string? operationId { get; set; }
    }

    public struct ImportSelectedProductInput
    {
        public string externalId { get; set; }
        public string? externalCategoryId { get; set; }
        public int? targetCategoryId { get; set; }
        public bool forceImportWithoutMapping { get; set; }
        public string? productName { get; set; }
        public string? targetCategoryName { get; set; }
        public string? externalCategoryName { get; set; }
    }

    public struct UpdateSkuInput
    {
        public string newSku { get; set; }
    }

    public struct PriceSyncInput
    {
        public List<int>? linkIds { get; set; }
    }

    public struct LinksBulkInput
    {
        public List<int> linkIds { get; set; }
    }

    public struct CreateVariantInput
    {
        public string? sku { get; set; }
        public string? barcode { get; set; }
        public string? size { get; set; }
        public decimal? price { get; set; }
        public int? quantity { get; set; }
    }

    public struct UpdateVariantInput
    {
        public string? sku { get; set; }
        public string? barcode { get; set; }
        public string? size { get; set; }
        public decimal? price { get; set; }
        public int? quantity { get; set; }
        public int? sortOrder { get; set; }
    }

    #endregion
}
