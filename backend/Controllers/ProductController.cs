using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Products;
using Delegat.Services;
using Delegat.Services.Marketplace;

namespace Delegat.Controllers;

/// <summary>
/// Controller for managing products within a store.
/// </summary>
[ApiController]
[JwtAuthorize]
public class ProductController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProductController> _logger;
    private readonly DataContext _context;
    private readonly ExcelProductService _excelService;
    private readonly QuotaService _quotaService;
    private readonly IOperationLogger _operationLogger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductController"/> class.
    /// </summary>
    public ProductController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider,
        ILogger<ProductController> logger,
        DataContext context,
        ExcelProductService excelService,
        QuotaService quotaService,
        IOperationLogger operationLogger)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _context = context;
        _excelService = excelService;
        _quotaService = quotaService;
        _operationLogger = operationLogger;
    }

    /// <summary>
    /// Retrieves a paginated list of products for the current store, with optional search, category filtering, and sorting.
    /// </summary>
    /// <param name="searchQuery">Optional search string to filter by name or SKU.</param>
    /// <param name="categoryId">Optional category ID to filter products by.</param>
    /// <param name="startIndex">Zero-based index of the first item to return.</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <param name="sortBy">Column to sort by (name, sku, price, storageQuantity, category). Default: id.</param>
    /// <param name="sortDesc">Whether to sort descending. Default: true.</param>
    /// <returns>A paged result containing product review DTOs, total count, and last-page indicator.</returns>
    [HttpGet]
    [Route("/products")]
    public async Task<IActionResult> GetAllProducts(string? searchQuery, int? categoryId = null, int startIndex = 0, int pageSize = 12, string? sortBy = null, bool sortDesc = true)
    {
        if (startIndex < 0) startIndex = 0;
        if (pageSize < 1) pageSize = 12;

        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var productRepo = _unitOfWork.Repository<Product>();

        // Build predicate combining all filters
        var hasSearch = !string.IsNullOrEmpty(searchQuery);
        var hasCategoryFilter = categoryId.HasValue;
        var isNoCategoryFilter = categoryId == -1;

        Expression<Func<Product, bool>> predicate = (hasSearch, hasCategoryFilter, isNoCategoryFilter) switch
        {
            (false, false, _) => p => p.storeId == selectedStoreId.Value,
            (true, false, _) => p => p.storeId == selectedStoreId.Value &&
                                     (p.name.Contains(searchQuery!) || p.sku.Contains(searchQuery!)),
            (false, _, true) => p => p.storeId == selectedStoreId.Value && p.categoryId == null,
            (false, true, false) => p => p.storeId == selectedStoreId.Value && p.categoryId == categoryId!.Value,
            (true, _, true) => p => p.storeId == selectedStoreId.Value &&
                                    (p.name.Contains(searchQuery!) || p.sku.Contains(searchQuery!)) &&
                                    p.categoryId == null,
            (true, true, false) => p => p.storeId == selectedStoreId.Value &&
                                        (p.name.Contains(searchQuery!) || p.sku.Contains(searchQuery!)) &&
                                        p.categoryId == categoryId!.Value,
        };

        // Build sort expression
        // When searching, prioritize exact matches (name == query or sku == query) first
        var sortByLower = sortBy?.ToLower();

        // Check if sorting by a store attribute (storeattr_<id>)
        int? storeAttrSortId = null;
        if (sortByLower != null && sortByLower.StartsWith("storeattr_") &&
            int.TryParse(sortByLower["storeattr_".Length..], out var parsedAttrId))
        {
            storeAttrSortId = parsedAttrId;
        }

        Func<IQueryable<Product>, IOrderedQueryable<Product>> secondarySort;
        if (storeAttrSortId.HasValue)
        {
            var attrId = storeAttrSortId.Value;
            secondarySort = sortDesc
                ? q => q.OrderByDescending(p => p.storeAttributeValues.Where(v => v.storeAttributeId == attrId).Select(v => v.value).FirstOrDefault())
                : q => q.OrderBy(p => p.storeAttributeValues.Where(v => v.storeAttributeId == attrId).Select(v => v.value).FirstOrDefault());
        }
        else
        {
            secondarySort = sortByLower switch
            {
                "name" => sortDesc ? q => q.OrderByDescending(p => p.name) : q => q.OrderBy(p => p.name),
                "sku" => sortDesc ? q => q.OrderByDescending(p => p.sku) : q => q.OrderBy(p => p.sku),
                "price" => sortDesc ? q => q.OrderByDescending(p => p.price) : q => q.OrderBy(p => p.price),
                "storagequantity" => sortDesc ? q => q.OrderByDescending(p => p.storageQuantity) : q => q.OrderBy(p => p.storageQuantity),
                "category" => sortDesc ? q => q.OrderByDescending(p => p.category!.name) : q => q.OrderBy(p => p.category!.name),
                "createdat" => sortDesc ? q => q.OrderByDescending(p => p.createdAt) : q => q.OrderBy(p => p.createdAt),
                "updatedat" => sortDesc ? q => q.OrderByDescending(p => p.updatedAt) : q => q.OrderBy(p => p.updatedAt),
                _ => sortDesc ? q => q.OrderByDescending(p => p.Id) : q => q.OrderBy(p => p.Id),
            };
        }

        Func<IQueryable<Product>, IOrderedQueryable<Product>> orderBy;
        if (hasSearch)
        {
            orderBy = q =>
            {
                var ordered = q.OrderByDescending(p => p.name == searchQuery || p.sku == searchQuery)
                               .ThenByDescending(p => p.name.StartsWith(searchQuery!) || p.sku.StartsWith(searchQuery!));
                if (storeAttrSortId.HasValue)
                {
                    var attrId = storeAttrSortId.Value;
                    return sortDesc
                        ? ordered.ThenByDescending(p => p.storeAttributeValues.Where(v => v.storeAttributeId == attrId).Select(v => v.value).FirstOrDefault())
                        : ordered.ThenBy(p => p.storeAttributeValues.Where(v => v.storeAttributeId == attrId).Select(v => v.value).FirstOrDefault());
                }
                return sortByLower switch
                {
                    "name" => sortDesc ? ordered.ThenByDescending(p => p.name) : ordered.ThenBy(p => p.name),
                    "sku" => sortDesc ? ordered.ThenByDescending(p => p.sku) : ordered.ThenBy(p => p.sku),
                    "price" => sortDesc ? ordered.ThenByDescending(p => p.price) : ordered.ThenBy(p => p.price),
                    "storagequantity" => sortDesc ? ordered.ThenByDescending(p => p.storageQuantity) : ordered.ThenBy(p => p.storageQuantity),
                    "category" => sortDesc ? ordered.ThenByDescending(p => p.category!.name) : ordered.ThenBy(p => p.category!.name),
                    "createdat" => sortDesc ? ordered.ThenByDescending(p => p.createdAt) : ordered.ThenBy(p => p.createdAt),
                    "updatedat" => sortDesc ? ordered.ThenByDescending(p => p.updatedAt) : ordered.ThenBy(p => p.updatedAt),
                    _ => sortDesc ? ordered.ThenByDescending(p => p.Id) : ordered.ThenBy(p => p.Id),
                };
            };
        }
        else
        {
            orderBy = secondarySort;
        }

        // Ensure deterministic ordering for AsSplitQuery — without a unique
        // tiebreaker, split-query sub-queries may select different rows for
        // the same LIMIT/OFFSET, losing collection data (files, images).
        var stableOrderBy = orderBy;
        orderBy = q => stableOrderBy(q).ThenBy(p => p.Id);

        var (items, total) = await productRepo.FindPagedAsync(
            predicate,
            query => query
                .AsSplitQuery()
                .Include(p => p.files)
                .Include(p => p.images)
                    .ThenInclude(i => i.file)
                .Include(p => p.category)
                .Include(p => p.storeAttributeValues)
                    .ThenInclude(sav => sav.storeAttribute),
            orderBy,
            startIndex,
            pageSize);

        var paged = new List<Product.ProductReviewDTO>();
        foreach (var p in items)
        {
            var dto = ((Infrastructure.Interfaces.IMapToDTO<Product.ProductReviewDTO>)p).MapToDTO(_serviceProvider);
            paged.Add(dto);
        }

        var lastPage = startIndex + pageSize >= total;
        return Ok(new
        {
            items = paged,
            totalCount = total,
            lastPage
        });
    }

    /// <summary>
    /// Retrieves a single product by its identifier, including all related entities.
    /// </summary>
    /// <param name="productId">The product identifier.</param>
    /// <returns>The full product DTO.</returns>
    [HttpGet]
    [Route("/products/{productId}")]
    public async Task<IActionResult> GetProduct(int productId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var productRepo = _unitOfWork.Repository<Product>();

        var product = await productRepo.FindAsync(
            p => p.Id == productId && p.storeId == selectedStoreId.Value,
            query => query
                .AsSplitQuery()
                .Include(p => p.store)
                .Include(p => p.category)
                    .ThenInclude(c => c!.attributes)
                .Include(p => p.files)
                    .ThenInclude(f => f.tags)
                    .ThenInclude(t => t.tagDefinition)
                .Include(p => p.images)
                .Include(p => p.notes)
                .Include(p => p.attributeValues)
                    .ThenInclude(av => av.attribute)
                .Include(p => p.storeAttributeValues)
                    .ThenInclude(sav => sav.storeAttribute));

        if (product == null)
            throw ApiException.NotFound("Товар не найден");

        return Ok(((Infrastructure.Interfaces.IMapToDTO<object>)product).MapToDTO(_serviceProvider));
    }

    /// <summary>
    /// Creates a new product in the current store.
    /// </summary>
    /// <param name="input">The product creation form data.</param>
    /// <returns>A redirect path to the newly created product's edit page.</returns>
    [HttpPost]
    [Route("/products")]
    public async Task<IActionResult> CreateProduct([FromBody] Product.UserAddForm input)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        await _quotaService.EnsureProductQuotaAsync(selectedStoreId.Value);

        var productRepo = _unitOfWork.Repository<Product>();

        // Check SKU uniqueness
        var existingProduct = await productRepo.FindAsync(
            p => p.storeId == selectedStoreId.Value && p.sku == input.sku);
        if (existingProduct != null)
            throw ApiException.BadRequest("Товар с таким артикулом уже существует");

        // Validate barcode if provided
        if (!string.IsNullOrEmpty(input.barcode) && !IsValidEanBarcode(input.barcode))
            throw ApiException.BadRequest("Невалидный штрихкод. Допустимы EAN-8 (8 цифр) или EAN-13 (13 цифр)");

        var product = new Product();
        product.AddFill(new Product.AddForm(input, selectedStoreId.Value, input.categoryId));

        await productRepo.AddAsync(product);

        // Add attribute values if provided
        if (input.attributeValues != null && input.attributeValues.Count > 0)
        {
            var attrValueRepo = _unitOfWork.Repository<ProductAttributeValue>();
            foreach (var av in input.attributeValues)
            {
                var attrValue = new ProductAttributeValue();
                attrValue.AddFill(new ProductAttributeValue.AddForm
                {
                    productId = product.Id,
                    attributeId = av.attributeId,
                    value = av.value
                });
                await attrValueRepo.AddWithoutSaveAsync(attrValue);
            }
        }

        // Add store attribute values if provided
        if (input.storeAttributeValues != null && input.storeAttributeValues.Count > 0)
        {
            var storeAttrValueRepo = _unitOfWork.Repository<StoreProductAttributeValue>();
            foreach (var sav in input.storeAttributeValues)
            {
                var storeAttrValue = new StoreProductAttributeValue();
                storeAttrValue.AddFill(new StoreProductAttributeValue.AddForm
                {
                    productId = product.Id,
                    storeAttributeId = sav.storeAttributeId,
                    value = sav.value
                });
                await storeAttrValueRepo.AddWithoutSaveAsync(storeAttrValue);
            }
        }

        if ((input.attributeValues != null && input.attributeValues.Count > 0) ||
            (input.storeAttributeValues != null && input.storeAttributeValues.Count > 0))
        {
            await _unitOfWork.SaveChangesAsync();
        }

        return Ok(new { id = product.Id, next = $"/products/{product.Id}/edit" });
    }

    /// <summary>
    /// Updates an existing product and its attribute values within a transaction.
    /// </summary>
    /// <param name="productId">The product identifier.</param>
    /// <param name="input">The fields to update.</param>
    /// <returns>A success indicator.</returns>
    [HttpPatch]
    [Route("/products/{productId}")]
    public async Task<IActionResult> UpdateProduct(int productId, [FromBody] Product.UpdateForm input)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var productRepo = _unitOfWork.Repository<Product>();

        // Use tracking query so EF detects changes automatically
        var product = await productRepo.GetByIdWithIncludesTrackingAsync(
            productId,
            query => query
                .Include(p => p.attributeValues)
                .Include(p => p.storeAttributeValues));

        if (product == null || product.storeId != selectedStoreId.Value)
            throw ApiException.NotFound("Товар не найден");

        // Validate barcode if provided
        if (input.barcode != null && input.barcode != "" && !IsValidEanBarcode(input.barcode))
            throw ApiException.BadRequest("Невалидный штрихкод. Допустимы EAN-8 (8 цифр) или EAN-13 (13 цифр)");

        if (input.categoryId.HasValue)
        {
            var categoryRepo = _unitOfWork.Repository<Category>();
            var category = await categoryRepo.FindAsync(
                c => c.Id == input.categoryId.Value && c.storeId == selectedStoreId.Value);
            if (category == null)
                throw ApiException.BadRequest("Категория не найдена");
        }

        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (input.categoryId.HasValue)
                product.categoryId = input.categoryId.Value;

            product.UpdateFill(input);

            // Update attribute values if provided
            if (input.attributeValues != null)
            {
                var attrValueRepo = _unitOfWork.Repository<ProductAttributeValue>();
                attrValueRepo.DeleteRange(product.attributeValues.ToList());

                foreach (var av in input.attributeValues)
                {
                    var attrValue = new ProductAttributeValue();
                    attrValue.AddFill(new ProductAttributeValue.AddForm
                    {
                        productId = product.Id,
                        attributeId = av.attributeId,
                        value = av.value
                    });
                    await attrValueRepo.AddWithoutSaveAsync(attrValue);
                }
            }

            // Update store attribute values if provided
            if (input.storeAttributeValues != null)
            {
                var storeAttrValueRepo = _unitOfWork.Repository<StoreProductAttributeValue>();
                storeAttrValueRepo.DeleteRange(product.storeAttributeValues.ToList());

                foreach (var sav in input.storeAttributeValues)
                {
                    var storeAttrValue = new StoreProductAttributeValue();
                    storeAttrValue.AddFill(new StoreProductAttributeValue.AddForm
                    {
                        productId = product.Id,
                        storeAttributeId = sav.storeAttributeId,
                        value = sav.value
                    });
                    await storeAttrValueRepo.AddWithoutSaveAsync(storeAttrValue);
                }
            }

            await _unitOfWork.SaveChangesAsync();
        });

        return Ok(new { success = true });
    }

    /// <summary>
    /// Updates the product SKU locally and cascades the change to all linked marketplaces.
    /// </summary>
    /// <param name="productId">The product identifier.</param>
    /// <param name="input">The new SKU value.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result with local update status and per-marketplace results.</returns>
    [HttpPost]
    [Route("/products/{productId}/update-sku")]
    public async Task<IActionResult> UpdateProductSku(int productId, [FromBody] UpdateSkuInput input, CancellationToken ct)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        // Validate input
        if (string.IsNullOrWhiteSpace(input.newSku))
            throw ApiException.BadRequest("Новый артикул не может быть пустым");

        var newSku = input.newSku.Trim();
        if (newSku.Length > 50)
            throw ApiException.BadRequest("Артикул не может быть длиннее 50 символов");

        // Load product
        var product = await _context.products
            .FirstOrDefaultAsync(p => p.Id == productId && p.storeId == selectedStoreId.Value, ct);

        if (product == null)
            throw ApiException.NotFound("Товар не найден");

        if (product.sku == newSku)
            throw ApiException.BadRequest("Новый артикул совпадает с текущим");

        // Check uniqueness within the store
        var duplicate = await _context.products
            .AnyAsync(p => p.storeId == selectedStoreId.Value && p.sku == newSku && p.Id != productId, ct);

        if (duplicate)
            throw ApiException.BadRequest($"Артикул '{newSku}' уже используется другим товаром");

        // Update locally
        product.sku = newSku;
        await _context.SaveChangesAsync(ct);

        // Cascade to marketplaces
        var links = await _context.productMarketplaceLinks
            .Include(l => l.connection)
            .Where(l => l.productId == productId)
            .ToListAsync(ct);

        var marketplaceResults = new List<object>();

        if (links.Count > 0)
        {
            var syncService = _serviceProvider.GetRequiredService<ProductSyncService>();

            foreach (var link in links)
            {
                try
                {
                    var result = await syncService.UpdateSkuAsync(link.Id, newSku, ct);
                    marketplaceResults.Add(new
                    {
                        connectionId = link.connectionId,
                        name = link.connection.name ?? link.connection.adapterKey,
                        success = result.IsSuccess,
                        error = result.ErrorMessage
                    });
                }
                catch (Exception ex)
                {
                    marketplaceResults.Add(new
                    {
                        connectionId = link.connectionId,
                        name = link.connection.name ?? link.connection.adapterKey,
                        success = false,
                        error = ex.Message
                    });
                }
            }
        }

        return Ok(new
        {
            success = true,
            updatedLocal = true,
            newSku,
            marketplaceResults
        });
    }

    public struct UpdateSkuInput
    {
        public string newSku { get; set; }
    }

    /// <summary>
    /// Retrieves the product group for a given product, if one exists.
    /// </summary>
    /// <param name="productId">The product identifier.</param>
    /// <returns>The product group with its members, or null if the product has no group.</returns>
    [HttpGet]
    [Route("/products/{productId}/group")]
    public async Task<IActionResult> GetProductGroup(int productId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var memberRepo = _unitOfWork.Repository<ProductGroupMember>();

        // Find membership for this product
        var membership = await memberRepo.FindAsync(
            m => m.productId == productId,
            query => query
                .Include(m => m.group));

        if (membership == null)
            return Ok(new { group = (object?)null, hasGroup = false });

        var group = membership.group;
        if (group.storeId != selectedStoreId.Value)
            return Ok(new { group = (object?)null, hasGroup = false });

        // Get all members of this group with their products
        var allMembers = await memberRepo.FindAllAsync(
            m => m.groupId == group.Id,
            query => query
                .Include(m => m.product)
                    .ThenInclude(p => p.images)
                        .ThenInclude(i => i.file));

        var members = allMembers
            .OrderBy(m => m.sortOrder)
            .Select(m => new
            {
                productId = m.productId,
                productName = m.product?.name ?? "",
                productSku = m.product?.sku ?? "",
                isCurrent = m.productId == productId,
                imageUrl = m.product?.images?.FirstOrDefault()?.file?.GetDownloadUrl(),
                imageThumbnailUrl = m.product?.images?.FirstOrDefault()?.file is { } f
                    && f.contentType.StartsWith("image/")
                    ? $"/api/files/{f.Id}/thumbnail?w=128"
                    : null
            })
            .ToList();

        return Ok(new
        {
            group = new
            {
                id = group.Id,
                name = group.name,
                description = group.description,
                membersCount = allMembers.Count(),
                members
            }
        });
    }

    /// <summary>
    /// Bulk updates multiple products at once within a transaction.
    /// </summary>
    /// <param name="request">The list of product IDs and the fields to update.</param>
    /// <returns>A success indicator with the count of updated products.</returns>
    [HttpPatch]
    [Route("/products/bulk")]
    public async Task<IActionResult> BulkUpdateProducts([FromBody] BulkUpdateRequest request)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        if (request.productIds == null || request.productIds.Count == 0)
            throw ApiException.BadRequest("Товары не указаны");

        var productRepo = _unitOfWork.Repository<Product>();
        var attrValueRepo = _unitOfWork.Repository<ProductAttributeValue>();
        var storeAttrValueRepo = _unitOfWork.Repository<StoreProductAttributeValue>();

        // Find all products and verify they belong to the current store
        var products = await productRepo.FindAllAsync(
            p => request.productIds.Contains(p.Id) && p.storeId == selectedStoreId.Value,
            query => query
                .Include(p => p.attributeValues)
                .Include(p => p.storeAttributeValues));

        var foundIds = products.Select(p => p.Id).ToList();
        var notFoundIds = request.productIds.Except(foundIds).ToList();
        if (notFoundIds.Any())
            throw ApiException.NotFound($"Товары не найдены: {string.Join(", ", notFoundIds)}");

        var updatedCount = 0;

        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            foreach (var product in products)
            {
                var hasChanges = false;

                if (request.updates.name != null)
                {
                    product.name = request.updates.name;
                    hasChanges = true;
                }

                if (request.updates.description != null)
                {
                    product.description = request.updates.description;
                    hasChanges = true;
                }

                if (request.updates.storageQuantity.HasValue)
                {
                    product.storageQuantity = request.updates.storageQuantity.Value;
                    hasChanges = true;
                }

                if (request.updates.price.HasValue)
                {
                    product.price = request.updates.price.Value;
                    hasChanges = true;
                }

                if (request.updates.oldPrice.HasValue) { product.oldPrice = request.updates.oldPrice; hasChanges = true; }
                if (request.updates.minPrice.HasValue) { product.minPrice = request.updates.minPrice; hasChanges = true; }
                if (request.updates.netPrice.HasValue) { product.netPrice = request.updates.netPrice; hasChanges = true; }
                if (request.updates.currency != null) { product.currency = request.updates.currency == "" ? null : request.updates.currency; hasChanges = true; }

                if (request.updates.price.HasValue || request.updates.oldPrice.HasValue)
                {
                    if (product.price.HasValue)
                        product.discountedPrice = product.price;
                    product.discount = Delegat.Infrastructure.ProductPriceHelper.ComputeDiscountPercent(
                        product.oldPrice, product.price);
                    hasChanges = true;
                }

                if (request.updates.barcode != null)
                {
                    var newBarcode = request.updates.barcode == "" ? null : request.updates.barcode;
                    if (newBarcode != null && !IsValidEanBarcode(newBarcode))
                        throw ApiException.BadRequest($"Невалидный штрихкод: {newBarcode}");
                    product.barcode = newBarcode;
                    hasChanges = true;
                }

                if (request.updates.generateBarcodes == true && string.IsNullOrEmpty(product.barcode))
                {
                    product.barcode = GenerateEan13();
                    hasChanges = true;
                }

                if (hasChanges)
                {
                    productRepo.UpdateWithoutSave(product);
                }

                if (request.updates.attributeValues != null)
                {
                    foreach (var av in request.updates.attributeValues)
                    {
                        var existing = product.attributeValues
                            .FirstOrDefault(a => a.attributeId == av.attributeId);
                        if (existing != null)
                        {
                            existing.value = av.value;
                        }
                        else
                        {
                            var attrValue = new ProductAttributeValue();
                            attrValue.AddFill(new ProductAttributeValue.AddForm
                            {
                                productId = product.Id,
                                attributeId = av.attributeId,
                                value = av.value
                            });
                            await attrValueRepo.AddWithoutSaveAsync(attrValue);
                        }
                    }
                    hasChanges = true;
                }

                if (request.updates.storeAttributeValues != null)
                {
                    foreach (var sav in request.updates.storeAttributeValues)
                    {
                        var existing = product.storeAttributeValues
                            .FirstOrDefault(a => a.storeAttributeId == sav.storeAttributeId);
                        if (existing != null)
                        {
                            existing.value = sav.value;
                        }
                        else
                        {
                            var storeAttrValue = new StoreProductAttributeValue();
                            storeAttrValue.AddFill(new StoreProductAttributeValue.AddForm
                            {
                                productId = product.Id,
                                storeAttributeId = sav.storeAttributeId,
                                value = sav.value
                            });
                            await storeAttrValueRepo.AddWithoutSaveAsync(storeAttrValue);
                        }
                    }
                    hasChanges = true;
                }

                updatedCount++;
            }

            await _unitOfWork.SaveChangesAsync();
        });

        return Ok(new { success = true, updatedCount });
    }

    /// <summary>
    /// Request body for bulk-updating multiple products.
    /// </summary>
    public struct BulkUpdateRequest
    {
        /// <summary>The list of product IDs to update.</summary>
        public List<int> productIds { get; set; }
        /// <summary>The field values to apply to every specified product.</summary>
        public BulkUpdateFields updates { get; set; }
    }

    /// <summary>
    /// Fields that can be set during a bulk product update. Null values are skipped.
    /// </summary>
    public struct BulkUpdateFields
    {
        /// <summary>New product name, or null to leave unchanged.</summary>
        public string? name { get; set; }
        /// <summary>New product description, or null to leave unchanged.</summary>
        public string? description { get; set; }
        /// <summary>New storage quantity, or null to leave unchanged.</summary>
        public int? storageQuantity { get; set; }
        /// <summary>New price, or null to leave unchanged.</summary>
        public decimal? price { get; set; }
        public decimal? oldPrice { get; set; }
        public decimal? minPrice { get; set; }
        public decimal? netPrice { get; set; }
        public decimal? discountedPrice { get; set; }
        public int? discount { get; set; }
        public string? currency { get; set; }
        /// <summary>Category attribute values to replace, or null to leave unchanged.</summary>
        public List<ProductAttributeValue.UserValueForm>? attributeValues { get; set; }
        /// <summary>Store attribute values to replace, or null to leave unchanged.</summary>
        public List<StoreProductAttributeValue.UserValueForm>? storeAttributeValues { get; set; }
        /// <summary>New barcode (EAN-8/EAN-13), or null to leave unchanged. Empty string clears barcode.</summary>
        public string? barcode { get; set; }
        /// <summary>When true, generates a unique EAN-13 barcode for each product that doesn't already have one.</summary>
        public bool? generateBarcodes { get; set; }
    }

    /// <summary>
    /// Soft-deletes a product by setting its deletedAt timestamp.
    /// </summary>
    /// <param name="productId">The product identifier.</param>
    /// <returns>A redirect path to the products list.</returns>
    [HttpDelete]
    [Route("/products/{productId}")]
    public async Task<IActionResult> DeleteProduct(int productId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var product = await _context.products
            .FirstOrDefaultAsync(p => p.Id == productId && p.storeId == selectedStoreId.Value);

        if (product == null)
            throw ApiException.NotFound("Товар не найден");

        product.deletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { next = "/products" });
    }

    /// <summary>
    /// Soft-deletes multiple products by setting their deletedAt timestamp.
    /// </summary>
    /// <param name="request">The list of product IDs to delete.</param>
    /// <returns>A success indicator with the count of deleted products.</returns>
    [HttpPost]
    [Route("/products/bulk-delete")]
    public async Task<IActionResult> BulkDeleteProducts([FromBody] BulkDeleteRequest request)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        if (request.productIds == null || request.productIds.Count == 0)
            throw ApiException.BadRequest("Товары не указаны");

        var now = DateTime.UtcNow;
        var deletedCount = await _context.products
            .Where(p => request.productIds.Contains(p.Id) && p.storeId == selectedStoreId.Value)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.deletedAt, now));

        if (deletedCount == 0)
            throw ApiException.NotFound("Товары не найдены");

        return Ok(new { success = true, deletedCount });
    }

    /// <summary>
    /// Retrieves a paginated list of soft-deleted products for the current store.
    /// </summary>
    [HttpGet]
    [Route("/products/trash")]
    public async Task<IActionResult> GetTrash(string? searchQuery, int startIndex = 0, int pageSize = 25)
    {
        if (startIndex < 0) startIndex = 0;
        if (pageSize < 1) pageSize = 25;

        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var query = _context.products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.storeId == selectedStoreId.Value && p.deletedAt != null);

        if (!string.IsNullOrEmpty(searchQuery))
            query = query.Where(p => p.name.Contains(searchQuery) || p.sku.Contains(searchQuery));

        var totalCount = await query.CountAsync();

        var products = await query
            .OrderByDescending(p => p.deletedAt)
            .Skip(startIndex)
            .Take(pageSize)
            .AsSplitQuery()
            .Include(p => p.files)
            .Include(p => p.images)
                .ThenInclude(i => i.file)
            .Include(p => p.category)
            .Include(p => p.storeAttributeValues)
                .ThenInclude(sav => sav.storeAttribute)
            .ToListAsync();

        var items = new List<Product.ProductReviewDTO>();
        foreach (var p in products)
        {
            var dto = ((Infrastructure.Interfaces.IMapToDTO<Product.ProductReviewDTO>)p).MapToDTO(_serviceProvider);
            items.Add(dto);
        }

        return Ok(new
        {
            items,
            totalCount,
            lastPage = startIndex + pageSize >= totalCount
        });
    }

    /// <summary>
    /// Returns the count of soft-deleted products for the current store.
    /// </summary>
    [HttpGet]
    [Route("/products/trash/count")]
    public async Task<IActionResult> GetTrashCount()
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            return Ok(new { count = 0 });

        var count = await _context.products
            .IgnoreQueryFilters()
            .CountAsync(p => p.storeId == selectedStoreId.Value && p.deletedAt != null);

        return Ok(new { count });
    }

    /// <summary>
    /// Restores a soft-deleted product.
    /// </summary>
    [HttpPost]
    [Route("/products/{productId}/restore")]
    public async Task<IActionResult> RestoreProduct(int productId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var product = await _context.products
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == productId && p.storeId == selectedStoreId.Value && p.deletedAt != null);

        if (product == null)
            throw ApiException.NotFound("Товар не найден в корзине");

        product.deletedAt = null;
        await _context.SaveChangesAsync();

        return Ok(new { success = true });
    }

    /// <summary>
    /// Restores multiple soft-deleted products.
    /// </summary>
    [HttpPost]
    [Route("/products/bulk-restore")]
    public async Task<IActionResult> BulkRestore([FromBody] BulkDeleteRequest request)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        if (request.productIds == null || request.productIds.Count == 0)
            throw ApiException.BadRequest("Товары не указаны");

        var restoredCount = await _context.products
            .IgnoreQueryFilters()
            .Where(p => request.productIds.Contains(p.Id) && p.storeId == selectedStoreId.Value && p.deletedAt != null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.deletedAt, (DateTime?)null));

        return Ok(new { success = true, restoredCount });
    }

    /// <summary>
    /// Permanently deletes a soft-deleted product and all its related data.
    /// </summary>
    [HttpDelete]
    [Route("/products/{productId}/permanent")]
    public async Task<IActionResult> PermanentDeleteProduct(int productId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var product = await _context.products
            .IgnoreQueryFilters()
            .Include(p => p.files)
            .Include(p => p.images)
            .Include(p => p.notes)
            .Include(p => p.attributeValues)
            .Include(p => p.storeAttributeValues)
            .FirstOrDefaultAsync(p => p.Id == productId && p.storeId == selectedStoreId.Value && p.deletedAt != null);

        if (product == null)
            throw ApiException.NotFound("Товар не найден в корзине");

        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await _context.Set<PendingProductExport>()
                .Where(pe => pe.productId == productId)
                .ExecuteDeleteAsync();

            await _context.pendingProductImports
                .Where(pi => pi.importedProductId == productId)
                .ExecuteDeleteAsync();

            await _context.pendingImageDownloads
                .Where(pid => pid.productId == productId)
                .ExecuteDeleteAsync();

            await _context.pendingProductTransfers
                .Where(pt => pt.newProductId == productId)
                .ExecuteDeleteAsync();

            await _context.productVariantLinks
                .Where(vl => vl.variant.productId == productId)
                .ExecuteDeleteAsync();

            await _context.productVariants
                .Where(v => v.productId == productId)
                .ExecuteDeleteAsync();

            await _context.productMarketplaceLinks
                .Where(ml => ml.productId == productId)
                .ExecuteDeleteAsync();

            await _context.productGroupMembers
                .Where(gm => gm.productId == productId)
                .ExecuteDeleteAsync();

            // Delete file share tokens
            var fileIds = product.files.Select(f => f.Id).ToList();
            if (fileIds.Any())
            {
                await _context.fileShareTokens
                    .Where(fst => fileIds.Contains(fst.fileId))
                    .ExecuteDeleteAsync();
                await _context.tags
                    .Where(t => fileIds.Contains(t.fileId))
                    .ExecuteDeleteAsync();
            }

            // Delete folder share tokens
            var folderIds = await _context.productFolders
                .Where(pf => pf.productId == productId)
                .Select(pf => pf.Id)
                .ToListAsync();
            if (folderIds.Any())
            {
                await _context.folderShareTokens
                    .Where(fst => fst.folderId.HasValue && folderIds.Contains(fst.folderId.Value))
                    .ExecuteDeleteAsync();
            }

            var storeAttrValueRepo = _unitOfWork.Repository<StoreProductAttributeValue>();
            var attrValueRepo = _unitOfWork.Repository<ProductAttributeValue>();
            var noteRepo = _unitOfWork.Repository<Note>();
            var imageRepo = _unitOfWork.Repository<ImageReference>();
            var fileRepo = _unitOfWork.Repository<Models.File>();
            var productRepo = _unitOfWork.Repository<Product>();

            storeAttrValueRepo.DeleteRange(product.storeAttributeValues.ToList());
            attrValueRepo.DeleteRange(product.attributeValues.ToList());
            noteRepo.DeleteRange(product.notes.ToList());
            imageRepo.DeleteRange(product.images.ToList());
            fileRepo.DeleteRange(product.files.ToList());

            // Delete folders (children first due to Restrict)
            await _context.productFolders
                .Where(pf => pf.productId == productId && pf.parentFolderId != null)
                .ExecuteDeleteAsync();
            await _context.productFolders
                .Where(pf => pf.productId == productId)
                .ExecuteDeleteAsync();

            productRepo.DeleteWithoutSave(product);
            await _unitOfWork.SaveChangesAsync();
        });

        return Ok(new { success = true });
    }

    /// <summary>
    /// Permanently deletes multiple soft-deleted products.
    /// </summary>
    [HttpPost]
    [Route("/products/bulk-permanent-delete")]
    public async Task<IActionResult> BulkPermanentDelete([FromBody] BulkDeleteRequest request)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        if (request.productIds == null || request.productIds.Count == 0)
            throw ApiException.BadRequest("Товары не указаны");

        var products = await _context.products
            .IgnoreQueryFilters()
            .Include(p => p.files)
            .Include(p => p.images)
            .Include(p => p.notes)
            .Include(p => p.attributeValues)
            .Include(p => p.storeAttributeValues)
            .Where(p => request.productIds.Contains(p.Id) && p.storeId == selectedStoreId.Value && p.deletedAt != null)
            .ToListAsync();

        if (!products.Any())
            throw ApiException.NotFound("Товары не найдены в корзине");

        var foundIds = products.Select(p => p.Id).ToList();

        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await _context.Set<PendingProductExport>()
                .Where(pe => foundIds.Contains(pe.productId))
                .ExecuteDeleteAsync();

            await _context.pendingProductImports
                .Where(pi => foundIds.Contains(pi.importedProductId ?? 0))
                .ExecuteDeleteAsync();

            await _context.pendingImageDownloads
                .Where(pid => foundIds.Contains(pid.productId))
                .ExecuteDeleteAsync();

            // Clear transfer records referencing these products
            await _context.pendingProductTransfers
                .Where(pt => foundIds.Contains(pt.newProductId ?? 0))
                .ExecuteDeleteAsync();

            await _context.productVariantLinks
                .Where(vl => foundIds.Contains(vl.variant.productId))
                .ExecuteDeleteAsync();

            await _context.productVariants
                .Where(v => foundIds.Contains(v.productId))
                .ExecuteDeleteAsync();

            await _context.productMarketplaceLinks
                .Where(ml => foundIds.Contains(ml.productId))
                .ExecuteDeleteAsync();

            await _context.productGroupMembers
                .Where(gm => foundIds.Contains(gm.productId))
                .ExecuteDeleteAsync();

            var allFileIds = products.SelectMany(p => p.files.Select(f => f.Id)).ToList();
            if (allFileIds.Any())
            {
                await _context.fileShareTokens
                    .Where(fst => allFileIds.Contains(fst.fileId))
                    .ExecuteDeleteAsync();
                await _context.tags
                    .Where(t => allFileIds.Contains(t.fileId))
                    .ExecuteDeleteAsync();
            }

            var allFolderIds = await _context.productFolders
                .Where(pf => foundIds.Contains(pf.productId))
                .Select(pf => pf.Id)
                .ToListAsync();
            if (allFolderIds.Any())
            {
                await _context.folderShareTokens
                    .Where(fst => fst.folderId.HasValue && allFolderIds.Contains(fst.folderId.Value))
                    .ExecuteDeleteAsync();
            }

            var storeAttrValueRepo = _unitOfWork.Repository<StoreProductAttributeValue>();
            var attrValueRepo = _unitOfWork.Repository<ProductAttributeValue>();
            var noteRepo = _unitOfWork.Repository<Note>();
            var imageRepo = _unitOfWork.Repository<ImageReference>();
            var fileRepo = _unitOfWork.Repository<Models.File>();
            var productRepo = _unitOfWork.Repository<Product>();

            foreach (var product in products)
            {
                storeAttrValueRepo.DeleteRange(product.storeAttributeValues.ToList());
                attrValueRepo.DeleteRange(product.attributeValues.ToList());
                noteRepo.DeleteRange(product.notes.ToList());
                imageRepo.DeleteRange(product.images.ToList());
                fileRepo.DeleteRange(product.files.ToList());
                productRepo.DeleteWithoutSave(product);
            }

            // Delete folders (children first due to Restrict)
            await _context.productFolders
                .Where(pf => foundIds.Contains(pf.productId) && pf.parentFolderId != null)
                .ExecuteDeleteAsync();
            await _context.productFolders
                .Where(pf => foundIds.Contains(pf.productId))
                .ExecuteDeleteAsync();

            await _unitOfWork.SaveChangesAsync();
        });

        return Ok(new { success = true, deletedCount = products.Count });
    }

    /// <summary>
    /// Exports selected products to an Excel file (.xlsx).
    /// </summary>
    [HttpPost]
    [Route("/products/export-template")]
    public async Task<IActionResult> ExportTemplate([FromBody] ExportRequest request)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        if (request.productIds == null || request.productIds.Count == 0)
            throw ApiException.BadRequest("Товары не указаны");

        var productRepo = _unitOfWork.Repository<Product>();
        var products = await productRepo.FindAllAsync(
            p => request.productIds.Contains(p.Id) && p.storeId == selectedStoreId.Value,
            query => query
                .AsSplitQuery()
                .Include(p => p.category)
                .Include(p => p.groupMemberships)
                    .ThenInclude(gm => gm.group)
                .Include(p => p.attributeValues)
                    .ThenInclude(av => av.attribute)
                .Include(p => p.storeAttributeValues)
                    .ThenInclude(sav => sav.storeAttribute));

        // Collect all unique category attributes from the products' categories
        var categoryIds = products
            .Where(p => p.categoryId.HasValue)
            .Select(p => p.categoryId!.Value)
            .Distinct()
            .ToList();

        var categoryAttributes = categoryIds.Count > 0
            ? await _context.categoryAttributes
                .Where(ca => categoryIds.Contains(ca.categoryId))
                .OrderBy(ca => ca.categoryId)
                .ThenBy(ca => ca.sortOrder)
                .ToListAsync()
            : new List<CategoryAttribute>();

        // Get all store attributes
        var storeAttributes = await _context.storeAttributes
            .Where(sa => sa.storeId == selectedStoreId.Value)
            .OrderBy(sa => sa.sortOrder)
            .ToListAsync();

        var stream = _excelService.ExportToExcel(products.ToList(), categoryAttributes, storeAttributes);
        return File(stream,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "products-export.xlsx");
    }

    /// <summary>
    /// Previews changes from an imported Excel file without applying them.
    /// </summary>
    [HttpPost]
    [Route("/products/import-preview")]
    public async Task<IActionResult> ImportPreview(IFormFile file)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        if (file == null || file.Length == 0)
            throw ApiException.BadRequest("Файл не загружен");

        using var stream = file.OpenReadStream();
        var parsed = _excelService.ParseImportFile(stream);

        if (parsed.Rows.Count == 0)
            return Ok(new ImportChangeset
            {
                Summary = new ImportSummary { TotalRows = 0 }
            });

        var productIds = parsed.Rows.Select(r => r.ProductId).Distinct().ToList();
        var productRepo = _unitOfWork.Repository<Product>();
        var products = await productRepo.FindAllAsync(
            p => productIds.Contains(p.Id) && p.storeId == selectedStoreId.Value,
            query => query
                .AsSplitQuery()
                .Include(p => p.attributeValues)
                    .ThenInclude(av => av.attribute)
                .Include(p => p.storeAttributeValues)
                    .ThenInclude(sav => sav.storeAttribute));

        var changeset = _excelService.BuildChangeset(parsed, products.ToList());
        return Ok(changeset);
    }

    /// <summary>
    /// Applies changes from an imported Excel file to the database.
    /// </summary>
    [HttpPost]
    [Route("/products/import-apply")]
    public async Task<IActionResult> ImportApply(IFormFile file)
    {
        var userId = _currentUserService.GetUserId() ?? 0;
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        if (file == null || file.Length == 0)
            throw ApiException.BadRequest("Файл не загружен");

        var operationId = Guid.NewGuid().ToString("N");
        var storeId = selectedStoreId.Value;
        await _operationLogger.StartAsync(operationId, storeId, userId, "excel-import",
            $"Импорт из Excel: {file.FileName}");

        using var stream = file.OpenReadStream();
        var parsed = _excelService.ParseImportFile(stream);

        await _operationLogger.LogAsync(operationId, storeId, "info", $"Найдено строк: {parsed.Rows.Count}");

        if (parsed.Rows.Count == 0)
        {
            await _operationLogger.FinishAsync(operationId, storeId, "completed", "Нет данных для импорта");
            return Ok(new ImportApplyResult());
        }

        var productIds = parsed.Rows.Select(r => r.ProductId).Distinct().ToList();
        var productRepo = _unitOfWork.Repository<Product>();

        // Load with tracking for updates
        var products = await productRepo.FindAllAsync(
            p => productIds.Contains(p.Id) && p.storeId == selectedStoreId.Value,
            query => query
                .AsSplitQuery()
                .Include(p => p.attributeValues)
                    .ThenInclude(av => av.attribute)
                .Include(p => p.storeAttributeValues)
                    .ThenInclude(sav => sav.storeAttribute));

        var productsList = products.ToList();
        var changeset = _excelService.BuildChangeset(parsed, productsList);

        await _operationLogger.LogAsync(operationId, storeId, "info", $"Изменений: {changeset.Changes.Count}");

        if (changeset.Changes.Count == 0)
        {
            await _operationLogger.FinishAsync(operationId, storeId, "completed", "Изменений не обнаружено");
            return Ok(new ImportApplyResult());
        }

        // Re-load with tracking enabled for applying changes
        var trackedProducts = await _context.products
            .AsSplitQuery()
            .Include(p => p.attributeValues)
            .Include(p => p.storeAttributeValues)
            .Where(p => productIds.Contains(p.Id) && p.storeId == selectedStoreId.Value)
            .ToListAsync();

        await _operationLogger.LogAsync(operationId, storeId, "info", "Применение изменений...");

        ImportApplyResult result = null!;
        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            result = await _excelService.ApplyChangeset(changeset, trackedProducts, _unitOfWork);
        });

        await _operationLogger.FinishAsync(operationId, storeId,
            result.Errors.Count == 0 ? "completed" : "partial",
            $"Применено: {result.AppliedCount}, ошибок: {result.Errors.Count}");

        return Ok(result);
    }

    public struct ExportRequest
    {
        public List<int> productIds { get; set; }
    }

    /// <summary>
    /// Request body for bulk-deleting multiple products.
    /// </summary>
    public struct BulkDeleteRequest
    {
        /// <summary>The list of product IDs to delete.</summary>
        public List<int> productIds { get; set; }
    }

    /// <summary>
    /// Validates an EAN-8 or EAN-13 barcode string (digits only, correct check digit).
    /// </summary>
    private static readonly Random _random = new();

    /// <summary>Generates a random valid EAN-13 barcode with prefix 200-299 (internal use).</summary>
    public static string GenerateEan13()
    {
        var body = "20";
        for (int i = 0; i < 10; i++)
            body += _random.Next(0, 10).ToString();

        int sum = 0;
        for (int i = 0; i < body.Length; i++)
        {
            int digit = body[i] - '0';
            sum += (i % 2 == 0) ? digit : digit * 3;
        }
        int check = (10 - (sum % 10)) % 10;
        return body + check;
    }

    public static bool IsValidEanBarcode(string barcode)
    {
        if (string.IsNullOrEmpty(barcode))
            return false;

        if (barcode.Length != 8 && barcode.Length != 13)
            return false;

        if (!barcode.All(char.IsDigit))
            return false;

        // Verify check digit (modulo 10 algorithm)
        int sum = 0;
        for (int i = 0; i < barcode.Length - 1; i++)
        {
            int digit = barcode[i] - '0';
            // For EAN-13: positions 0,2,4,... weight 1; positions 1,3,5,... weight 3
            // For EAN-8: same pattern
            sum += (i % 2 == 0) ? digit : digit * 3;
        }

        int checkDigit = (10 - (sum % 10)) % 10;
        return checkDigit == (barcode[^1] - '0');
    }
}
