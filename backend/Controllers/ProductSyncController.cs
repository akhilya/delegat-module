using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrintO.Enums;
using PrintO.Infrastructure.Auth;
using PrintO.Infrastructure.Data;
using PrintO.Infrastructure.Middleware;
using PrintO.Models;
using PrintO.Models.Marketplace;
using PrintO.Models.Products;
using PrintO.Services.Marketplace;

namespace PrintO.Controllers;

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

    public ProductSyncController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider,
        IMarketplaceAdapterRegistry adapterRegistry,
        ProductSyncService productSyncService,
        PriceSyncService priceSyncService,
        DataContext context,
        ILogger<ProductSyncController> logger)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
        _adapterRegistry = adapterRegistry;
        _productSyncService = productSyncService;
        _priceSyncService = priceSyncService;
        _context = context;
        _logger = logger;
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

    #region Export

    /// <summary>
    /// Export a product to the marketplace
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/products/{productId}/export")]
    public async Task<IActionResult> ExportProduct(
        int connectionId,
        int productId,
        CancellationToken ct)
    {
        await GetConnectionAsync(connectionId);

        var result = await _productSyncService.ExportProductAsync(productId, connectionId, ct);

        if (!result.IsSuccess)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Ошибка экспорта");

        return Ok(new
        {
            linkId = result.LinkId,
            externalProductId = result.ExternalProductId,
            isNewProduct = result.IsNewProduct,
            message = result.Message
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

        var results = new List<object>();
        int successCount = 0;
        int failedCount = 0;

        foreach (var productId in input.productIds)
        {
            var result = await _productSyncService.ExportProductAsync(productId, connectionId, ct);

            results.Add(new
            {
                productId,
                success = result.IsSuccess,
                linkId = result.LinkId,
                externalProductId = result.ExternalProductId,
                errorMessage = result.ErrorMessage
            });

            if (result.IsSuccess)
                successCount++;
            else
                failedCount++;
        }

        return Ok(new
        {
            results,
            successCount,
            failedCount
        });
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

        // Get products from marketplace
        var pagedResult = await adapter.ImportAllProductsAsync(connection, pageSize, cursor, offerIdSearch, ct);

        // Get existing links to detect linked products
        var linkRepo = _unitOfWork.Repository<ProductMarketplaceLink>();
        var externalIds = pagedResult.Items.Select(p => p.ExternalId).ToList();
        var existingLinks = await linkRepo.FindAllAsync(
            l => l.connectionId == connectionId && externalIds.Contains(l.externalProductId),
            q => q.Include(l => l.product));
        var linkedExternalIds = existingLinks.ToDictionary(l => l.externalProductId, l => l);

        // Build items with link status
        var items = pagedResult.Items.Select(product =>
        {
            var isLinked = linkedExternalIds.TryGetValue(product.ExternalId, out var existingLink);

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

        // Generate batch ID
        var batchId = Guid.NewGuid().ToString("N");
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
        await GetConnectionAsync(connectionId);

        var result = await _priceSyncService.ImportPricesAsync(
            connectionId,
            input?.linkIds,
            ct);

        if (!result.IsSuccess)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Ошибка импорта цен");

        return Ok(new
        {
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
        await GetConnectionAsync(connectionId);

        var result = await _priceSyncService.ExportPricesAsync(
            connectionId,
            input?.linkIds,
            ct);

        if (!result.IsSuccess)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Ошибка экспорта цен");

        return Ok(new
        {
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
        await GetConnectionAsync(connectionId);

        var result = await _priceSyncService.ExportStocksAsync(
            connectionId,
            input?.linkIds,
            ct);

        if (!result.IsSuccess)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Ошибка экспорта остатков");

        return Ok(new
        {
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
        var existingLinks = await linkRepo.FindAllAsync(l => l.productId == productId);
        var linkByConnectionId = existingLinks.ToDictionary(l => l.connectionId, l => l);

        // Get all active connections
        var connRepo = _unitOfWork.Repository<MarketplaceConnection>();
        var connections = await connRepo.FindAllAsync(
            c => c.storeId == selectedStoreId.Value && c.isActive);

        // Get category mappings (handle duplicates by taking first)
        var mappingRepo = _unitOfWork.Repository<CategoryMapping>();
        var mappings = await mappingRepo.FindAllAsync(
            m => product.categoryId.HasValue && m.localCategoryId == product.categoryId.Value);
        var mappingByConnectionId = mappings
            .GroupBy(m => m.connectionId)
            .ToDictionary(g => g.Key, g => g.First());

        // Возвращаем ВСЕ подключения с информацией о статусе
        var result = connections
            .Select(conn =>
            {
                var adapter = _adapterRegistry.GetByKey(conn.adapterKey);
                var hasMapping = mappingByConnectionId.ContainsKey(conn.Id);
                var isLinked = linkByConnectionId.ContainsKey(conn.Id);
                var link = isLinked ? linkByConnectionId[conn.Id] : null;

                return new
                {
                    connectionId = conn.Id,
                    connectionName = conn.name,
                    adapterKey = conn.adapterKey,
                    adapterName = adapter?.DisplayName ?? conn.adapterKey,
                    // Статус маппинга
                    hasMapping,
                    needsMapping = !hasMapping && product.categoryId.HasValue,
                    mappingId = hasMapping ? mappingByConnectionId[conn.Id].Id : (int?)null,
                    // Статус связи
                    isLinked,
                    linkId = link?.Id,
                    linkStatus = link?.status.ToString(),
                    externalProductId = link?.externalProductId,
                    // Возможности
                    canExport = adapter?.Capabilities.HasFlag(MarketplaceCapabilities.ExportProduct) ?? false,
                    canImport = adapter?.Capabilities.HasFlag(MarketplaceCapabilities.ImportProduct) ?? false
                };
            })
            .ToList();

        return Ok(new
        {
            productId,
            productName = product.name,
            categoryId = product.categoryId,
            categoryName = product.category?.name,
            allConnections = result
        });
    }

    /// <summary>
    /// Import changes from the marketplace into the local product
    /// </summary>
    [HttpPost]
    [Route("/marketplace/links/{linkId}/import")]
    public async Task<IActionResult> ImportFromLink(int linkId, CancellationToken ct)
    {
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

        // Получаем адаптер
        var adapter = _adapterRegistry.GetByKey(link.connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest($"Адаптер '{link.connection.adapterKey}' не найден");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportProduct))
            throw ApiException.BadRequest("Адаптер не поддерживает импорт");

        // Получаем актуальные данные с маркетплейса
        var externalProduct = await adapter.ImportProductAsync(link.connection, link.externalProductId, ct);
        if (externalProduct == null)
            throw ApiException.BadRequest($"Товар с ID '{link.externalProductId}' не найден на маркетплейсе");

        // Сравниваем и обновляем локальный товар
        var updatedFields = new List<string>();
        var oldValues = new Dictionary<string, object?>();
        var newValues = new Dictionary<string, object?>();

        var productRepo = _unitOfWork.Repository<Product>();
        var product = link.product;

        if (product == null)
            throw ApiException.BadRequest("Локальный товар не найден");

        // Обновляем цену
        if (externalProduct.Price.HasValue && product.price != externalProduct.Price.Value)
        {
            oldValues["price"] = product.price;
            newValues["price"] = externalProduct.Price.Value;
            product.price = externalProduct.Price.Value;
            updatedFields.Add("price");
        }

        // Обновляем остаток
        if (externalProduct.Quantity.HasValue && product.storageQuantity != externalProduct.Quantity.Value)
        {
            oldValues["storageQuantity"] = product.storageQuantity;
            newValues["storageQuantity"] = externalProduct.Quantity.Value;
            product.storageQuantity = externalProduct.Quantity.Value;
            updatedFields.Add("storageQuantity");
        }

        // Обновляем связь с актуальными данными МП
        link.externalPrice = externalProduct.Price;
        link.externalStock = externalProduct.Quantity;
        link.lastSyncAt = DateTime.UtcNow;
        link.status = ProductMarketplaceStatus.Synced;
        link.lastErrorMessage = null;

        await _unitOfWork.SaveChangesAsync(ct);

        return Ok(new
        {
            success = true,
            updatedFields,
            oldValues,
            newValues,
            message = updatedFields.Count > 0
                ? $"Обновлено полей: {updatedFields.Count}"
                : "Изменений не обнаружено"
        });
    }

    /// <summary>
    /// Export local changes to the marketplace
    /// </summary>
    [HttpPost]
    [Route("/marketplace/links/{linkId}/export")]
    public async Task<IActionResult> ExportFromLink(int linkId, CancellationToken ct)
    {
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

        // Экспортируем через сервис
        var result = await _productSyncService.ExportProductAsync(link.productId, link.connectionId, ct);

        if (!result.IsSuccess)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Ошибка экспорта");

        return Ok(new
        {
            success = true,
            externalProductId = result.ExternalProductId,
            message = result.Message ?? "Товар успешно экспортирован"
        });
    }

    /// <summary>
    /// Bulk import changes from the marketplace for multiple linked products
    /// </summary>
    [HttpPost]
    [Route("/marketplace/links/import-bulk")]
    public async Task<IActionResult> ImportLinksBulk(
        [FromBody] LinksBulkInput input,
        CancellationToken ct)
    {
        if (input.linkIds == null || input.linkIds.Count == 0)
            throw ApiException.BadRequest("Связи не указаны");

        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var linkRepo = _unitOfWork.Repository<ProductMarketplaceLink>();
        var productRepo = _unitOfWork.Repository<Product>();

        var results = new List<object>();
        int successCount = 0;
        int failedCount = 0;

        foreach (var linkId in input.linkIds)
        {
            try
            {
                var link = await linkRepo.FindAsync(
                    l => l.Id == linkId,
                    q => q.Include(l => l.connection).Include(l => l.product));

                if (link == null || link.connection?.storeId != selectedStoreId.Value)
                {
                    results.Add(new { linkId, success = false, error = "Связь не найдена или доступ запрещён" });
                    failedCount++;
                    continue;
                }

                var adapter = _adapterRegistry.GetByKey(link.connection.adapterKey);
                if (adapter == null || !adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportProduct))
                {
                    results.Add(new { linkId, success = false, error = "Адаптер не найден или не поддерживает импорт" });
                    failedCount++;
                    continue;
                }

                var externalProduct = await adapter.ImportProductAsync(link.connection, link.externalProductId, ct);
                if (externalProduct == null)
                {
                    results.Add(new { linkId, success = false, error = "Товар не найден на маркетплейсе" });
                    failedCount++;
                    continue;
                }

                var updatedFields = new List<string>();
                var product = link.product;

                if (product != null)
                {
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
                }

                link.externalPrice = externalProduct.Price;
                link.externalStock = externalProduct.Quantity;
                link.lastSyncAt = DateTime.UtcNow;
                link.status = ProductMarketplaceStatus.Synced;
                link.lastErrorMessage = null;

                results.Add(new { linkId, success = true, updatedFields });
                successCount++;
            }
            catch (Exception ex)
            {
                results.Add(new { linkId, success = false, error = ex.Message });
                failedCount++;
            }
        }

        await _unitOfWork.SaveChangesAsync(ct);

        return Ok(new { results, successCount, failedCount });
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

        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var linkRepo = _unitOfWork.Repository<ProductMarketplaceLink>();

        var results = new List<object>();
        int successCount = 0;
        int failedCount = 0;

        foreach (var linkId in input.linkIds)
        {
            try
            {
                var link = await linkRepo.FindAsync(
                    l => l.Id == linkId,
                    q => q.Include(l => l.connection));

                if (link == null || link.connection?.storeId != selectedStoreId.Value)
                {
                    results.Add(new { linkId, success = false, error = "Связь не найдена или доступ запрещён" });
                    failedCount++;
                    continue;
                }

                var result = await _productSyncService.ExportProductAsync(link.productId, link.connectionId, ct);

                if (result.IsSuccess)
                {
                    results.Add(new { linkId, success = true, externalProductId = result.ExternalProductId });
                    successCount++;
                }
                else
                {
                    results.Add(new { linkId, success = false, error = result.ErrorMessage });
                    failedCount++;
                }
            }
            catch (Exception ex)
            {
                results.Add(new { linkId, success = false, error = ex.Message });
                failedCount++;
            }
        }

        return Ok(new { results, successCount, failedCount });
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

    #endregion

    #region Input DTOs

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

    #endregion
}
