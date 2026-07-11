using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Enums;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Products;
using Delegat.Services;
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

    public ProductSyncController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider,
        IMarketplaceAdapterRegistry adapterRegistry,
        ProductSyncService productSyncService,
        PriceSyncService priceSyncService,
        DataContext context,
        ILogger<ProductSyncController> logger,
        IOperationLogger operationLogger)
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
            return Ok(new { items = Array.Empty<object>(), totalCount = 0 });

        // Get product IDs already successfully linked to this connection
        // (exclude NotSynced/SyncError — those need to be re-exported)
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
    public async Task<IActionResult> ExportProduct(
        int connectionId,
        int productId,
        [FromBody] ExportProductInput? input,
        CancellationToken ct)
    {
        var connection = await GetConnectionAsync(connectionId);
        var userId = _currentUserService.GetUserId() ?? 0;
        var storeId = _currentUserService.GetSelectedStoreId() ?? connection.storeId;
        var operationId = input?.OperationId ?? Guid.NewGuid().ToString("N");

        var opExists = !string.IsNullOrEmpty(input?.OperationId) &&
            await _context.operations.AsNoTracking().AnyAsync(o => o.operationId == operationId, ct);

        if (!opExists)
            await _operationLogger.StartAsync(operationId, storeId, userId, "single-export",
                $"Экспорт товара #{productId}", connectionId: connectionId, connectionName: connection.name);

        var result = await _productSyncService.ExportProductAsync(
            productId, connectionId, ct,
            operationId: operationId, operationStoreId: storeId);

        await _operationLogger.FinishAsync(operationId, storeId,
            result.IsSuccess ? "completed" : "failed",
            result.IsSuccess ? result.Message : result.ErrorMessage);

        if (!result.IsSuccess)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Ошибка экспорта");

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
    public async Task<IActionResult> LinkProduct(
        int connectionId,
        int productId,
        [FromBody] LinkProductInput input,
        CancellationToken ct)
    {
        var connection = await GetConnectionAsync(connectionId);

        if (string.IsNullOrWhiteSpace(input.externalProductId) || input.externalProductId.Length > 100)
            throw ApiException.BadRequest("externalProductId обязателен (макс. 100 символов)");

        // Check product exists and belongs to the same store
        var product = await _context.products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId && p.storeId == connection.storeId, ct);
        if (product == null)
            throw ApiException.NotFound("Товар не найден");

        // Check no existing link
        var alreadyLinked = await _context.productMarketplaceLinks
            .AnyAsync(l => l.productId == productId && l.connectionId == connectionId, ct);
        if (alreadyLinked)
            throw ApiException.BadRequest("Товар уже привязан к этому подключению");

        // Check if externalProductId is already used by another product on this connection
        var duplicateExternalId = await _context.productMarketplaceLinks
            .AnyAsync(l => l.externalProductId == input.externalProductId.Trim()
                && l.connectionId == connectionId, ct);
        if (duplicateExternalId)
            throw ApiException.BadRequest(
                $"Внешний ID \"{input.externalProductId.Trim()}\" уже привязан к другому товару в этом подключении");

        // Resolve category mapping (optional — same logic as available-connections)
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
            externalProductId = input.externalProductId.Trim(),
            status = ProductMarketplaceStatus.NotSynced,
            categoryMappingId = categoryMappingId
        });

        _context.productMarketplaceLinks.Add(link);
        await _context.SaveChangesAsync(ct);

        return Ok(new
        {
            linkId = link.Id,
            externalProductId = link.externalProductId,
            status = link.status.ToString()
        });
    }

    /// <summary>
    /// Remove a local marketplace link without deleting the external product.
    /// </summary>
    [HttpDelete]
    [Route("/marketplace/links/{linkId}")]
    public async Task<IActionResult> UnlinkProduct(int linkId, CancellationToken ct)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var link = await _context.productMarketplaceLinks
            .Include(l => l.connection)
            .FirstOrDefaultAsync(l => l.Id == linkId && l.connection.storeId == selectedStoreId.Value, ct);

        if (link == null)
            throw ApiException.NotFound("Связь не найдена");

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
    /// Re-link a product to a different external marketplace product.
    /// </summary>
    [HttpPost]
    [Route("/marketplace/links/{linkId}/relink")]
    public async Task<IActionResult> RelinkProduct(
        int linkId,
        [FromBody] LinkProductInput input,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.externalProductId) || input.externalProductId.Length > 100)
            throw ApiException.BadRequest("externalProductId обязателен (макс. 100 символов)");

        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var link = await _context.productMarketplaceLinks
            .Include(l => l.connection)
            .FirstOrDefaultAsync(l => l.Id == linkId && l.connection.storeId == selectedStoreId.Value, ct);

        if (link == null)
            throw ApiException.NotFound("Связь не найдена");

        link.externalProductId = input.externalProductId.Trim();
        link.externalSku = null;
        link.status = ProductMarketplaceStatus.NotSynced;
        link.lastErrorMessage = null;

        await _context.SaveChangesAsync(ct);

        return Ok(new
        {
            linkId = link.Id,
            externalProductId = link.externalProductId,
            status = link.status.ToString()
        });
    }

    /// <summary>
    /// Bulk export products
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/products/export-bulk")]
    public async Task<IActionResult> ExportProductsBulk(
        int connectionId,
        [FromBody] ExportBulkInput input,
        CancellationToken ct)
    {
        await GetConnectionAsync(connectionId);

        var bulkResult = await _productSyncService.ExportProductsBulkAsync(
            input.productIds, connectionId, ct);

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
    public async Task<IActionResult> CreateExportBatch(
        int connectionId,
        [FromBody] ExportBulkInput input,
        CancellationToken ct)
    {
        if (input.productIds == null || input.productIds.Count == 0)
            throw ApiException.BadRequest("Не выбраны товары для экспорта");

        var connection = await GetConnectionAsync(connectionId);

        var batchId = Guid.NewGuid().ToString("N");
        var userId = _currentUserService.GetUserId() ?? 0;
        var storeId = _currentUserService.GetSelectedStoreId() ?? connection.storeId;

        // Batch-load product names
        var productNames = await _context.products
            .AsNoTracking()
            .Where(p => input.productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.name, ct);

        foreach (var productId in input.productIds)
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
            $"Экспорт {input.productIds.Count} товаров",
            connectionId: connectionId, connectionName: connection.name,
            batchId: batchId, batchType: "export", totalItems: input.productIds.Count);

        return Ok(new
        {
            batchId,
            queued = input.productIds.Count,
            message = $"Добавлено {input.productIds.Count} товаров в очередь экспорта"
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
        // Запрет кэширования - критически важно для polling
        Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";

        var entities = await _context.pendingProductExports
            .AsNoTracking()
            .Where(p => p.batchId == batchId)
            .OrderBy(p => p.Id)
            .ToListAsync(ct);

        if (entities.Count == 0)
            throw ApiException.NotFound("Пакет не найден");

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
    public async Task<IActionResult> RetryFailedExports(string batchId, CancellationToken ct)
    {
        var failedItems = await _context.pendingProductExports
            .Where(p => p.batchId == batchId && p.status == ProductExportStatus.Failed)
            .ToListAsync(ct);

        if (failedItems.Count == 0)
            return Ok(new { message = "Нет неудавшихся экспортов для повтора", retried = 0 });

        foreach (var item in failedItems)
        {
            item.status = ProductExportStatus.Pending;
            item.retryCount = 0;
            item.lastError = null;
        }

        await _context.SaveChangesAsync(ct);

        return Ok(new { message = $"Поставлено в очередь на повтор: {failedItems.Count} товаров", retried = failedItems.Count });
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
            throw ApiException.BadRequest($"Неизвестный адаптер: {connection.adapterKey}");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportAllProducts))
            throw ApiException.BadRequest("Адаптер не поддерживает получение списка товаров");

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
                $"Не удалось получить товары из адаптера {adapter.DisplayName}: {ex.Message}",
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

            if (isLinked)
            {
                if (existingLink!.categoryMappingId.HasValue)
                {
                    mappingsById.TryGetValue(existingLink.categoryMappingId.Value, out resolvedMapping);
                }
                else if (existingLink.product?.categoryId is int localCategoryId &&
                         mappingsByLocalCategoryId.TryGetValue(localCategoryId, out var localCategoryMappings) &&
                         localCategoryMappings.Count == 1)
                {
                    resolvedMapping = localCategoryMappings[0];
                }
            }

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
                mappingMissing = isLinked && resolvedMapping == null,
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
            throw ApiException.BadRequest($"Неизвестный адаптер: {connection.adapterKey}");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportAllProducts))
            throw ApiException.BadRequest("Адаптер не поддерживает импорт товаров");

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
    public async Task<IActionResult> ImportSelectedProducts(
        int connectionId,
        [FromBody] ImportSelectedInput input,
        CancellationToken ct)
    {
        if (input.products == null || input.products.Count == 0)
            throw ApiException.BadRequest("Не выбраны товары для импорта");

        var connection = await GetConnectionAsync(connectionId);

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest($"Неизвестный адаптер: {connection.adapterKey}");

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
        var storeId = _currentUserService.GetSelectedStoreId() ?? connection.storeId;

        var skippedProducts = new List<object>();
        int queued = 0;

        foreach (var productInput in input.products)
        {
            // Check if forcing import without mapping
            int? targetCategoryId = null;

            if (!string.IsNullOrEmpty(productInput.externalCategoryId))
            {
                if (mappingsByExternalCategory.TryGetValue(productInput.externalCategoryId, out var mapping))
                {
                    targetCategoryId = mapping.localCategoryId;
                }
                else if (!productInput.forceImportWithoutMapping)
                {
                    // No mapping and not forced - skip (don't queue)
                    skippedProducts.Add(new
                    {
                        externalId = productInput.externalId,
                        error = $"Категория '{productInput.externalCategoryId}' не имеет маппинга"
                    });
                    continue;
                }
            }

            // Override with explicit target category if provided
            if (productInput.targetCategoryId.HasValue)
            {
                targetCategoryId = productInput.targetCategoryId.Value;
            }

            // Add to queue
            var pending = new PendingProductImport
            {
                connectionId = connectionId,
                externalProductId = productInput.externalId,
                targetCategoryId = targetCategoryId,
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
                op.title = $"Импорт {queued} товаров";
                op.batchType = "import";
                await _context.SaveChangesAsync(ct);
                await _operationLogger.ProgressAsync(batchId, storeId, queued, 0, 0);
            }
            else
            {
                await _operationLogger.StartAsync(batchId, storeId, userId, "batch-import",
                    $"Импорт {queued} товаров",
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
                    $"Загрузка изображений",
                    connectionId: connectionId, connectionName: connection.name,
                    batchId: imageBatchId, batchType: "image-download", totalItems: 0);
            }

            await _operationLogger.LogAsync(batchId, storeId, "info",
                $"В очередь добавлено {queued} товаров, ожидание обработки...");

            // Log skipped products
            foreach (var skipped in skippedProducts)
            {
                var sp = (dynamic)skipped;
                await _operationLogger.LogAsync(batchId, storeId, "warning",
                    $"Пропущен: {sp.externalId} — {sp.error}");
            }
        }

        return Ok(new
        {
            batchId,
            queued,
            skipped = skippedProducts.Count,
            skippedProducts,
            message = $"Добавлено {queued} товаров в очередь импорта"
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
        // Запрет кэширования - критически важно для polling
        Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";

        // Загружаем сущности напрямую с AsNoTracking
        var entities = await _context.pendingProductImports
            .AsNoTracking()
            .Where(p => p.batchId == batchId)
            .OrderBy(p => p.Id)
            .ToListAsync(ct);

        _logger.LogWarning("[BatchStatus] Получено {Count} записей из БД, статусы: [{Statuses}]",
            entities.Count,
            string.Join(", ", entities.Select(e => $"{e.Id}:{e.status}")));

        if (entities.Count == 0)
            throw ApiException.NotFound("Пакет не найден");

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
            "[BatchStatus] ИТОГО: pending={Pending}, inProgress={InProgress}, completed={Completed}, failed={Failed}",
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
    public async Task<IActionResult> RetryFailedImports(string batchId, CancellationToken ct)
    {
        var failedItems = await _context.pendingProductImports
            .Where(p => p.batchId == batchId && p.status == ProductImportStatus.Failed)
            .ToListAsync(ct);

        if (failedItems.Count == 0)
            return Ok(new { message = "Нет неудавшихся импортов для повтора", retried = 0 });

        foreach (var item in failedItems)
        {
            item.status = ProductImportStatus.Pending;
            item.retryCount = 0;
            item.lastError = null;
        }

        await _context.SaveChangesAsync(ct);

        return Ok(new { message = $"Поставлено в очередь на повтор: {failedItems.Count} товаров", retried = failedItems.Count });
    }

    /// <summary>
    /// Import a single product from the marketplace
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/products/import/{externalId}")]
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
            throw ApiException.BadRequest(result.ErrorMessage ?? "Ошибка импорта");

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
    public async Task<IActionResult> ImportAllProducts(
        int connectionId,
        [FromBody] ImportAllInput? input,
        CancellationToken ct)
    {
        var connection = await GetConnectionAsync(connectionId);

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest($"Неизвестный адаптер: {connection.adapterKey}");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportAllProducts))
            throw ApiException.BadRequest("Адаптер не поддерживает массовый импорт");

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
            errors.Add($"Импорт прерван: {ex.Message}");
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
    public async Task<IActionResult> UpdateSku(
        int connectionId,
        int productId,
        [FromBody] UpdateSkuInput input,
        CancellationToken ct)
    {
        await GetConnectionAsync(connectionId);

        // Находим связь товара
        var linkRepo = _unitOfWork.Repository<ProductMarketplaceLink>();
        var link = await linkRepo.FindAsync(
            l => l.connectionId == connectionId && l.productId == productId);

        if (link == null)
            throw ApiException.NotFound("Товар не привязан к этому маркетплейсу");

        var result = await _productSyncService.UpdateSkuAsync(link.Id, input.newSku, ct);

        if (!result.IsSuccess)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Ошибка обновления артикула");

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
            throw ApiException.BadRequest(result.ErrorMessage ?? "Ошибка сравнения цен");

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
    public async Task<IActionResult> ImportPrices(
        int connectionId,
        [FromBody] PriceSyncInput? input,
        CancellationToken ct)
    {
        var connection = await GetConnectionAsync(connectionId);

        var operationId = Guid.NewGuid().ToString("N");
        var userId = _currentUserService.GetUserId() ?? 0;
        var storeId = _currentUserService.GetSelectedStoreId() ?? connection.storeId;

        await _operationLogger.StartAsync(operationId, storeId, userId, "price-import",
            "Импорт цен", connectionId: connectionId, connectionName: connection.name);

        var result = await _priceSyncService.ImportPricesAsync(
            connectionId,
            input?.linkIds,
            ct,
            operationId,
            storeId);

        var status = result.IsSuccess ? "completed" : "failed";
        await _operationLogger.FinishAsync(operationId, storeId, status, result.Message ?? result.ErrorMessage);

        if (!result.IsSuccess)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Ошибка импорта цен");

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
    public async Task<IActionResult> ExportPrices(
        int connectionId,
        [FromBody] PriceSyncInput? input,
        CancellationToken ct)
    {
        var connection = await GetConnectionAsync(connectionId);

        var operationId = Guid.NewGuid().ToString("N");
        var userId = _currentUserService.GetUserId() ?? 0;
        var storeId = _currentUserService.GetSelectedStoreId() ?? connection.storeId;

        await _operationLogger.StartAsync(operationId, storeId, userId, "price-export",
            "Экспорт цен", connectionId: connectionId, connectionName: connection.name);

        await _operationLogger.LogAsync(operationId, storeId, "info", $"Экспорт цен для {input?.linkIds?.Count ?? 0} товаров...");

        var result = await _priceSyncService.ExportPricesAsync(
            connectionId,
            input?.linkIds,
            ct,
            operationId,
            storeId);

        var status = result.IsSuccess ? "completed" : "failed";
        await _operationLogger.FinishAsync(operationId, storeId, status, result.Message ?? result.ErrorMessage);

        if (!result.IsSuccess)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Ошибка экспорта цен");

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
    public async Task<IActionResult> ExportStocks(
        int connectionId,
        [FromBody] PriceSyncInput? input,
        CancellationToken ct)
    {
        var connection = await GetConnectionAsync(connectionId);

        var operationId = Guid.NewGuid().ToString("N");
        var userId = _currentUserService.GetUserId() ?? 0;
        var storeId = _currentUserService.GetSelectedStoreId() ?? connection.storeId;

        await _operationLogger.StartAsync(operationId, storeId, userId, "stock-export",
            "Экспорт остатков", connectionId: connectionId, connectionName: connection.name);

        await _operationLogger.LogAsync(operationId, storeId, "info", $"Экспорт остатков для {input?.linkIds?.Count ?? 0} товаров...");

        var result = await _priceSyncService.ExportStocksAsync(
            connectionId,
            input?.linkIds,
            ct,
            operationId,
            storeId);

        var stockStatus = result.IsSuccess ? "completed" : "failed";
        await _operationLogger.FinishAsync(operationId, storeId, stockStatus, result.Message ?? result.ErrorMessage);

        if (!result.IsSuccess)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Ошибка экспорта остатков");

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
            throw ApiException.BadRequest("Магазин не выбран");

        var context = _serviceProvider.GetRequiredService<DataContext>();

        // Статистика по статусам
        var stats = await context.pendingImageDownloads
            .Where(p => p.storeId == selectedStoreId)
            .GroupBy(p => p.status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var pendingCount = stats.FirstOrDefault(s => s.Status == Models.ImageDownloadStatus.Pending)?.Count ?? 0;
        var inProgressCount = stats.FirstOrDefault(s => s.Status == Models.ImageDownloadStatus.InProgress)?.Count ?? 0;
        var completedCount = stats.FirstOrDefault(s => s.Status == Models.ImageDownloadStatus.Completed)?.Count ?? 0;
        var failedCount = stats.FirstOrDefault(s => s.Status == Models.ImageDownloadStatus.Failed)?.Count ?? 0;

        // Последние неудавшиеся загрузки
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
            throw ApiException.BadRequest("Магазин не выбран");

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
    public async Task<IActionResult> RetryFailedImageDownloads(int productId, CancellationToken ct)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

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
            throw ApiException.BadRequest("Магазин не выбран");

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
            throw ApiException.BadRequest("Магазин не выбран");

        // Get product with category
        var productRepo = _unitOfWork.Repository<Product>();
        var product = await productRepo.FindAsync(
            p => p.Id == productId && p.storeId == selectedStoreId.Value,
            q => q.Include(p => p.category));

        if (product == null)
            throw ApiException.NotFound("Товар не найден");

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

        // Возвращаем ВСЕ подключения с информацией о статусе
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

                return new
                {
                    connectionId = conn.Id,
                    connectionName = conn.name,
                    adapterKey = conn.adapterKey,
                    adapterName = adapter?.DisplayName ?? conn.adapterKey,
                    productEditUrlTemplate = adapter?.ProductEditUrlTemplate,
                    // Статус маппинга
                    hasMapping,
                    needsMapping = !hasMapping && product.categoryId.HasValue,
                    // Multi-mapping: all available mappings + selected one from link
                    selectedMappingId = link?.categoryMappingId,
                    mappingMissing = isLinked && (!hasMapping || savedMappingWasDeleted),
                    availableMappings = connMappings
                        .Select(m => (object)new {
                            id = m.Id,
                            name = m.name,
                            externalCategoryName = m.externalCategoryName
                        }).ToList(),
                    // Статус связи
                    isLinked,
                    linkId = link?.Id,
                    linkStatus = link?.status.ToString(),
                    externalProductId = link?.externalProductId,
                    externalSku = link?.externalSku,
                    // Возможности
                    canExport = adapter?.Capabilities.HasFlag(MarketplaceCapabilities.ExportProduct) ?? false,
                    canImport = adapter?.Capabilities.HasFlag(MarketplaceCapabilities.ImportProduct) ?? false,
                    canArchive = adapter?.Capabilities.HasFlag(MarketplaceCapabilities.ArchiveProduct) ?? false
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
            allConnections = result
        });
    }

    /// <summary>
    /// Import changes from the marketplace into the local product
    /// </summary>
    public record ImportFromLinkInput(List<string>? ExcludeFields = null, string? OperationId = null);

    [HttpPost]
    [Route("/marketplace/links/{linkId}/import")]
    public async Task<IActionResult> ImportFromLink(int linkId, [FromBody] ImportFromLinkInput? input, CancellationToken ct)
    {
        var excludeFields = input?.ExcludeFields != null ? new HashSet<string>(input.ExcludeFields) : null;
        var userId = _currentUserService.GetUserId() ?? 0;
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        // Получаем связь с маркетплейсом (с tracking для обновления полей)
        var linkRepo = _unitOfWork.Repository<ProductMarketplaceLink>();
        var link = await linkRepo.GetByIdWithIncludesTrackingAsync(
            linkId,
            q => q.Include(l => l.connection).Include(l => l.product));

        if (link == null)
            throw ApiException.NotFound("Связь не найдена");

        if (link.connection?.storeId != selectedStoreId.Value)
            throw ApiException.Forbidden("Доступ запрещён");

        var operationId = input?.OperationId ?? Guid.NewGuid().ToString("N");
        var storeId = selectedStoreId.Value;

        var opExists = !string.IsNullOrEmpty(input?.OperationId) &&
            await _context.operations.AsNoTracking().AnyAsync(o => o.operationId == operationId, ct);

        if (!opExists)
            await _operationLogger.StartAsync(operationId, storeId, userId, "single-import",
                $"Импорт: {link.product?.name ?? link.externalProductId}",
                connectionId: link.connectionId, connectionName: link.connection.name);

        // Получаем адаптер
        var adapter = _adapterRegistry.GetByKey(link.connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest($"Адаптер '{link.connection.adapterKey}' не найден");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportProduct))
            throw ApiException.BadRequest("Адаптер не поддерживает импорт");

        await _operationLogger.LogAsync(operationId, storeId, "info", "Получение данных с маркетплейса...");

        // Получаем актуальные данные с маркетплейса
        var externalProduct = await adapter.ImportProductAsync(link.connection, link.externalProductId, ct);
        if (externalProduct == null)
            throw ApiException.BadRequest($"Товар с ID '{link.externalProductId}' не найден на маркетплейсе");

        await _operationLogger.LogAsync(operationId, storeId, "info", "Данные получены, сравнение полей...");

        // Сравниваем и обновляем локальный товар
        var updatedFields = new List<string>();
        var oldValues = new Dictionary<string, object?>();
        var newValues = new Dictionary<string, object?>();

        var productRepo = _unitOfWork.Repository<Product>();
        var product = link.product;

        if (product == null)
            throw ApiException.BadRequest("Локальный товар не найден");

        // Обновляем название
        if (excludeFields?.Contains("name") != true &&
            !string.IsNullOrEmpty(externalProduct.Name) && product.name != externalProduct.Name)
        {
            oldValues["name"] = product.name;
            newValues["name"] = externalProduct.Name;
            product.name = externalProduct.Name;
            updatedFields.Add("name");
        }

        // Обновляем описание
        if (excludeFields?.Contains("description") != true &&
            externalProduct.Description != null && product.description != externalProduct.Description)
        {
            oldValues["description"] = product.description;
            newValues["description"] = externalProduct.Description;
            product.description = externalProduct.Description;
            updatedFields.Add("description");
        }

        // Обновляем цену
        if (excludeFields?.Contains("price") != true &&
            externalProduct.Price.HasValue && product.price != externalProduct.Price.Value)
        {
            oldValues["price"] = product.price;
            newValues["price"] = externalProduct.Price.Value;
            product.price = externalProduct.Price.Value;
            updatedFields.Add("price");
        }

        // Обновляем остаток
        if (excludeFields?.Contains("storageQuantity") != true &&
            externalProduct.Quantity.HasValue && product.storageQuantity != externalProduct.Quantity.Value)
        {
            oldValues["storageQuantity"] = product.storageQuantity;
            newValues["storageQuantity"] = externalProduct.Quantity.Value;
            product.storageQuantity = externalProduct.Quantity.Value;
            updatedFields.Add("storageQuantity");
        }

        // Обновляем атрибуты на основе маппинга категории
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

        if (externalProduct.Attributes?.Count > 0 && product.categoryId.HasValue)
        {
            var attrChanges = await _productSyncService.UpdateProductAttributesFromExternalAsync(
                product.Id,
                link.connectionId,
                product.categoryId.Value,
                externalProduct.Attributes,
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

        // Обновляем связь с актуальными данными МП
        link.externalPrice = externalProduct.Price;
        link.externalStock = externalProduct.Quantity;
        link.lastSyncAt = DateTime.UtcNow;
        link.status = ProductMarketplaceStatus.Synced;
        link.lastErrorMessage = null;

        await _unitOfWork.SaveChangesAsync(ct);

        var summary = updatedFields.Count > 0
            ? $"Обновлено полей: {updatedFields.Count}"
            : "Изменений не обнаружено";
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
    public async Task<IActionResult> ImportFromLinkPreview(int linkId, CancellationToken ct)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var linkRepo = _unitOfWork.Repository<ProductMarketplaceLink>();
        var link = await linkRepo.FindAsync(
            l => l.Id == linkId,
            q => q.Include(l => l.connection).Include(l => l.product));

        if (link == null)
            throw ApiException.NotFound("Связь не найдена");

        if (link.connection?.storeId != selectedStoreId.Value)
            throw ApiException.Forbidden("Доступ запрещён");

        var adapter = _adapterRegistry.GetByKey(link.connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest($"Адаптер '{link.connection.adapterKey}' не найден");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportProduct))
            throw ApiException.BadRequest("Адаптер не поддерживает импорт");

        var externalProduct = await adapter.ImportProductAsync(link.connection, link.externalProductId, ct);
        if (externalProduct == null)
            throw ApiException.BadRequest($"Товар с ID '{link.externalProductId}' не найден на маркетплейсе");

        var updatedFields = new List<string>();
        var oldValues = new Dictionary<string, object?>();
        var newValues = new Dictionary<string, object?>();
        var product = link.product;

        if (product == null)
            throw ApiException.BadRequest("Локальный товар не найден");

        if (!string.IsNullOrEmpty(externalProduct.Name) && product.name != externalProduct.Name)
        {
            oldValues["name"] = product.name;
            newValues["name"] = externalProduct.Name;
            updatedFields.Add("name");
        }

        if (externalProduct.Description != null && product.description != externalProduct.Description)
        {
            oldValues["description"] = product.description;
            newValues["description"] = externalProduct.Description;
            updatedFields.Add("description");
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

        var irreversibleFields = new List<string>();

        if (externalProduct.Attributes?.Count > 0 && product.categoryId.HasValue)
        {
            var attrChanges = await _productSyncService.GetAttributeChangesDetailedAsync(
                product.Id,
                link.connectionId,
                product.categoryId.Value,
                externalProduct.Attributes,
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

    /// <summary>
    /// Bulk preview import changes from the marketplace for multiple links.
    /// Groups links by connection and uses batch API for efficiency.
    /// </summary>
    [HttpPost]
    [Route("/marketplace/links/import-bulk-preview")]
    public async Task<IActionResult> ImportLinksBulkPreview(
        [FromBody] LinksBulkInput input,
        CancellationToken ct)
    {
        if (input.linkIds == null || input.linkIds.Count == 0)
            throw ApiException.BadRequest("Связи не указаны");

        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

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
                results.Add(new BulkPreviewItemResult(linkId, null, false, [], empty, empty, "Связь не найдена"));
                continue;
            }

            if (!externalProductMap.TryGetValue(link.externalProductId, out var externalProduct))
            {
                results.Add(new BulkPreviewItemResult(linkId, link.product?.name, false, [], empty, empty, "Товар не найден на маркетплейсе"));
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

                if (externalProduct.Description != null && product.description != externalProduct.Description)
                {
                    oldValues["description"] = product.description;
                    newValues["description"] = externalProduct.Description;
                    updatedFields.Add("description");
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

                if (externalProduct.Attributes?.Count > 0 && product.categoryId.HasValue)
                {
                    var attrChanges = await _productSyncService.GetAttributeChangesDetailedAsync(
                        product.Id,
                        link.connectionId,
                        product.categoryId.Value,
                        externalProduct.Attributes,
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
    public async Task<IActionResult> ExportFromLink(int linkId, [FromBody] ExportFromLinkInput? input, CancellationToken ct)
    {
        var userId = _currentUserService.GetUserId() ?? 0;
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        // Получаем связь с маркетплейсом
        var linkRepo = _unitOfWork.Repository<ProductMarketplaceLink>();
        var link = await linkRepo.FindAsync(
            l => l.Id == linkId,
            q => q.Include(l => l.connection).Include(l => l.product));

        if (link == null)
            throw ApiException.NotFound("Связь не найдена");

        if (link.connection?.storeId != selectedStoreId.Value)
            throw ApiException.Forbidden("Доступ запрещён");

        if (link.product == null)
            throw ApiException.BadRequest("Локальный товар не найден");

        var operationId = input?.OperationId ?? Guid.NewGuid().ToString("N");
        var storeId = selectedStoreId.Value;

        var opExists = !string.IsNullOrEmpty(input?.OperationId) &&
            await _context.operations.AsNoTracking().AnyAsync(o => o.operationId == operationId, ct);

        if (!opExists)
            await _operationLogger.StartAsync(operationId, storeId, userId, "single-export",
                $"Экспорт: {link.product.name ?? link.externalProductId}",
                connectionId: link.connectionId, connectionName: link.connection.name);

        // Экспортируем через сервис
        var result = await _productSyncService.ExportProductAsync(
            link.productId, link.connectionId, ct,
            operationId: operationId, operationStoreId: storeId);

        await _operationLogger.FinishAsync(operationId, storeId,
            result.IsSuccess ? "completed" : "failed",
            result.IsSuccess ? (result.Message ?? "Товар экспортирован") : result.ErrorMessage);

        if (!result.IsSuccess)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Ошибка экспорта");

        return Ok(new
        {
            operationId,
            success = true,
            externalProductId = result.ExternalProductId,
            message = result.Message ?? "Товар успешно экспортирован"
        });
    }

    public struct RecreateInput { public string newSku { get; set; } }

    /// <summary>
    /// Recreate product on marketplace with a new SKU: export as new, archive old
    /// </summary>
    [HttpPost]
    [Route("/marketplace/links/{linkId}/recreate")]
    public async Task<IActionResult> RecreateProductOnMarketplace(int linkId, [FromBody] RecreateInput input, CancellationToken ct)
    {
        // 1. Validate
        if (string.IsNullOrWhiteSpace(input.newSku) || input.newSku.Length > 50)
            throw ApiException.BadRequest("Новый артикул обязателен (макс. 50 символов)");

        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        // 2. Load link with connection and product (tracking)
        var link = await _context.productMarketplaceLinks
            .Include(l => l.connection)
            .Include(l => l.product)
            .FirstOrDefaultAsync(l => l.Id == linkId, ct);

        if (link == null)
            throw ApiException.NotFound("Связь не найдена");
        if (link.connection?.storeId != selectedStoreId.Value)
            throw ApiException.Forbidden("Доступ запрещён");
        if (link.product == null)
            throw ApiException.BadRequest("Локальный товар не найден");

        // 3. Check adapter capabilities
        var adapter = _adapterRegistry.GetByKey(link.connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest("Адаптер маркетплейса не найден");
        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ExportProduct))
            throw ApiException.BadRequest("Адаптер не поддерживает экспорт товаров");
        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ArchiveProduct))
            throw ApiException.BadRequest("Адаптер не поддерживает архивацию товаров");

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
                throw ApiException.BadRequest(exportResult.ErrorMessage ?? "Ошибка экспорта нового товара");
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

            // 8. Archive old product (best effort — don't fail the operation)
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
            // Critical failure — try to restore
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

            throw ApiException.BadRequest($"Ошибка пересоздания: {ex.Message}");
        }
    }

    /// <summary>
    /// Bulk import changes from the marketplace for multiple linked products.
    /// Groups links by connection and uses batch API for efficiency.
    /// </summary>
    [HttpPost]
    [Route("/marketplace/links/import-bulk")]
    public async Task<IActionResult> ImportLinksBulk(
        [FromBody] LinksBulkInput input,
        CancellationToken ct)
    {
        if (input.linkIds == null || input.linkIds.Count == 0)
            throw ApiException.BadRequest("Связи не указаны");

        var userId = _currentUserService.GetUserId() ?? 0;
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var operationId = Guid.NewGuid().ToString("N");
        var storeId = selectedStoreId.Value;
        await _operationLogger.StartAsync(operationId, storeId, userId, "batch-import",
            $"Импорт изменений ({input.linkIds.Count} товаров)", totalItems: input.linkIds.Count);

        var linkRepo = _unitOfWork.Repository<ProductMarketplaceLink>();

        // Load all links with tracking for updates
        var allLinks = await _context.productMarketplaceLinks
            .Include(l => l.connection)
            .Include(l => l.product)
            .Where(l => input.linkIds.Contains(l.Id))
            .ToListAsync(ct);

        await _operationLogger.LogAsync(operationId, storeId, "info", $"Загрузка данных для {allLinks.Count} товаров...");

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
                foreach (var ep in batchResult)
                    externalProductMap[ep.ExternalId] = ep;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Batch import failed for connection {ConnectionId}", connection.Id);
            }
        }

        await _operationLogger.LogAsync(operationId, storeId, "info", $"Данные получены: {externalProductMap.Count} товаров");

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
                    results.Add(new { linkId, success = false, error = "Связь не найдена или доступ запрещён" });
                    failedCount++;
                    continue;
                }

                if (!externalProductMap.TryGetValue(link.externalProductId, out var externalProduct))
                {
                    results.Add(new { linkId, success = false, error = "Товар не найден на маркетплейсе" });
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

                    if (externalProduct.Description != null && product.description != externalProduct.Description)
                    {
                        product.description = externalProduct.Description;
                        updatedFields.Add("description");
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

                    if (externalProduct.Attributes?.Count > 0 && product.categoryId.HasValue)
                    {
                        var attrChanges = await _productSyncService.UpdateProductAttributesFromExternalAsync(
                            product.Id,
                            link.connectionId,
                            product.categoryId.Value,
                            externalProduct.Attributes,
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
                await _operationLogger.LogAsync(operationId, storeId, "success", $"Импортирован: {product?.name ?? link.externalProductId}");
                await _operationLogger.ProgressAsync(operationId, storeId, input.linkIds.Count, successCount, failedCount);
            }
            catch (Exception ex)
            {
                results.Add(new { linkId, success = false, error = ex.Message });
                failedCount++;
                await _operationLogger.LogAsync(operationId, storeId, "error", $"Ошибка: {ex.Message}");
                await _operationLogger.ProgressAsync(operationId, storeId, input.linkIds.Count, successCount, failedCount);
            }
        }

        await _unitOfWork.SaveChangesAsync(ct);

        await _operationLogger.FinishAsync(operationId, storeId,
            failedCount == 0 ? "completed" : (successCount == 0 ? "failed" : "partial"),
            $"Импортировано: {successCount}, ошибок: {failedCount}");

        return Ok(new { operationId, results, successCount, failedCount });
    }

    /// <summary>
    /// Bulk export local changes to the marketplace for multiple linked products
    /// </summary>
    [HttpPost]
    [Route("/marketplace/links/export-bulk")]
    public async Task<IActionResult> ExportLinksBulk(
        [FromBody] LinksBulkInput input,
        CancellationToken ct)
    {
        if (input.linkIds == null || input.linkIds.Count == 0)
            throw ApiException.BadRequest("Связи не указаны");

        var userId = _currentUserService.GetUserId() ?? 0;
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var bulkOpId = Guid.NewGuid().ToString("N");
        var bulkStoreId = selectedStoreId.Value;
        await _operationLogger.StartAsync(bulkOpId, bulkStoreId, userId, "batch-export",
            $"Экспорт изменений ({input.linkIds.Count} товаров)", totalItems: input.linkIds.Count);

        var linkRepo = _unitOfWork.Repository<ProductMarketplaceLink>();

        // Load all links in one query
        var links = await linkRepo.FindAllAsync(
            l => input.linkIds.Contains(l.Id),
            q => q.Include(l => l.connection));

        await _operationLogger.LogAsync(bulkOpId, bulkStoreId, "info", $"Экспорт {links.Count()} товаров...");

        var results = new List<object>();
        int successCount = 0;
        int failedCount = 0;

        // Report missing/unauthorized links
        var foundLinkIds = links.Select(l => l.Id).ToHashSet();
        foreach (var linkId in input.linkIds.Where(id => !foundLinkIds.Contains(id)))
        {
            results.Add(new { linkId, success = false, error = "Связь не найдена или доступ запрещён" });
            failedCount++;
        }

        // Filter by store access
        var authorizedLinks = links.Where(l => l.connection?.storeId == selectedStoreId.Value).ToList();
        foreach (var link in links.Where(l => l.connection?.storeId != selectedStoreId.Value))
        {
            results.Add(new { linkId = link.Id, success = false, error = "Связь не найдена или доступ запрещён" });
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
                            await _operationLogger.LogAsync(bulkOpId, bulkStoreId, "success", $"Экспортирован: #{item.ProductId}");
                            var details = FormatExportDetails(item.Details);
                            if (details != null)
                                await _operationLogger.LogAsync(bulkOpId, bulkStoreId, "info", $"#{item.ProductId}: {details}");
                        }
                        else
                        {
                            results.Add(new { linkId, success = false, error = item.ErrorMessage });
                            failedCount++;
                            await _operationLogger.LogAsync(bulkOpId, bulkStoreId, "error", $"Ошибка #{item.ProductId}: {item.ErrorMessage}");
                            var details = FormatExportDetails(item.Details);
                            if (details != null)
                                await _operationLogger.LogAsync(bulkOpId, bulkStoreId, "error", $"#{item.ProductId}: {details}");
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
                await _operationLogger.LogAsync(bulkOpId, bulkStoreId, "error", $"Ошибка группы: {ex.Message}");
                await _operationLogger.ProgressAsync(bulkOpId, bulkStoreId, input.linkIds.Count, successCount, failedCount);
            }
        }

        await _operationLogger.FinishAsync(bulkOpId, bulkStoreId,
            failedCount == 0 ? "completed" : (successCount == 0 ? "failed" : "partial"),
            $"Экспортировано: {successCount}, ошибок: {failedCount}");

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
            throw ApiException.BadRequest("Магазин не выбран");

        var product = await _context.products
            .FirstOrDefaultAsync(p => p.Id == productId && p.storeId == selectedStoreId.Value, ct);
        if (product == null)
            throw ApiException.NotFound("Товар не найден");

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
    public async Task<IActionResult> CreateProductVariant(
        int productId,
        [FromBody] CreateVariantInput input,
        CancellationToken ct)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var product = await _context.products
            .FirstOrDefaultAsync(p => p.Id == productId && p.storeId == selectedStoreId.Value, ct);
        if (product == null)
            throw ApiException.NotFound("Товар не найден");

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
    public async Task<IActionResult> UpdateProductVariant(
        int productId,
        int variantId,
        [FromBody] UpdateVariantInput input,
        CancellationToken ct)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var product = await _context.products
            .FirstOrDefaultAsync(p => p.Id == productId && p.storeId == selectedStoreId.Value, ct);
        if (product == null)
            throw ApiException.NotFound("Товар не найден");

        var variant = await _context.productVariants
            .FirstOrDefaultAsync(v => v.Id == variantId && v.productId == productId, ct);
        if (variant == null)
            throw ApiException.NotFound("Вариант не найден");

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
    public async Task<IActionResult> DeleteProductVariant(
        int productId,
        int variantId,
        CancellationToken ct)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var product = await _context.products
            .FirstOrDefaultAsync(p => p.Id == productId && p.storeId == selectedStoreId.Value, ct);
        if (product == null)
            throw ApiException.NotFound("Товар не найден");

        var variant = await _context.productVariants
            .FirstOrDefaultAsync(v => v.Id == variantId && v.productId == productId, ct);
        if (variant == null)
            throw ApiException.NotFound("Вариант не найден");

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
            throw ApiException.BadRequest("Магазин не выбран");

        var repo = _unitOfWork.Repository<MarketplaceConnection>();

        var connection = await repo.FindAsync(
            c => c.Id == connectionId && c.storeId == selectedStoreId.Value);

        if (connection == null)
            throw ApiException.NotFound("Подключение не найдено");

        return connection;
    }

    private static string? FormatExportDetails(Dictionary<string, object>? details)
    {
        if (details == null || details.Count == 0)
            return null;

        try
        {
            if (details.TryGetValue("images", out var images))
                return $"Детали экспорта изображений: {JsonSerializer.Serialize(images)}";

            return $"Детали экспорта: {JsonSerializer.Serialize(details)}";
        }
        catch
        {
            return "Детали экспорта получены, но не удалось сериализовать их для лога";
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
        public List<int> productIds { get; set; }
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
