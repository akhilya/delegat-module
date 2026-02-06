using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrintO.Infrastructure.Auth;
using PrintO.Infrastructure.Data;
using PrintO.Infrastructure.Middleware;
using PrintO.Models;
using PrintO.Models.Marketplace;
using PrintO.Models.Products;

namespace PrintO.Controllers;

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

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductController"/> class.
    /// </summary>
    /// <param name="currentUserService">Service for resolving the current authenticated user.</param>
    /// <param name="unitOfWork">Unit of work for database operations.</param>
    /// <param name="serviceProvider">Service provider for dependency resolution.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="context">Database context for direct query access.</param>
    public ProductController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider,
        ILogger<ProductController> logger,
        DataContext context)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _context = context;
    }

    /// <summary>
    /// Retrieves a paginated list of products for the current store, with optional search filtering.
    /// </summary>
    /// <param name="searchQuery">Optional search string to filter by name or SKU.</param>
    /// <param name="startIndex">Zero-based index of the first item to return.</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <returns>A paged result containing product review DTOs, total count, and last-page indicator.</returns>
    [HttpGet]
    [Route("/products")]
    public async Task<IActionResult> GetAllProducts(string? searchQuery, int startIndex = 0, int pageSize = 12)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var productRepo = _unitOfWork.Repository<Product>();

        Expression<Func<Product, bool>> predicate = string.IsNullOrEmpty(searchQuery)
            ? p => p.storeId == selectedStoreId.Value
            : p => p.storeId == selectedStoreId.Value &&
                   (p.name.Contains(searchQuery) || p.sku.Contains(searchQuery));

        var (items, total) = await productRepo.FindPagedAsync(
            predicate,
            query => query
                .Include(p => p.files)
                .Include(p => p.images)
                .Include(p => p.category)
                .Include(p => p.storeAttributeValues)
                    .ThenInclude(sav => sav.storeAttribute),
            q => q.OrderByDescending(p => p.Id),
            startIndex,
            pageSize);

        var paged = items
            .Select(p => ((Infrastructure.Interfaces.IMapToDTO<Product.ProductReviewDTO>)p).MapToDTO(_serviceProvider))
            .ToList();

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
                .Include(p => p.store)
                .Include(p => p.category)
                    .ThenInclude(c => c!.attributes)
                .Include(p => p.files)
                    .ThenInclude(f => f.tags)
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

        var productRepo = _unitOfWork.Repository<Product>();

        // Check SKU uniqueness
        var existingProduct = await productRepo.FindAsync(
            p => p.storeId == selectedStoreId.Value && p.sku == input.sku);
        if (existingProduct != null)
            throw ApiException.BadRequest("Товар с таким артикулом уже существует");

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

        return Ok(new { next = $"/products/{product.Id}/edit" });
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

        await _unitOfWork.BeginTransactionAsync();
        try
        {
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
            await _unitOfWork.CommitTransactionAsync();
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync();
            throw;
        }

        return Ok(new { success = true });
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

        var otherMembers = allMembers
            .Where(m => m.productId != productId)
            .OrderBy(m => m.sortOrder)
            .Select(m => new
            {
                productId = m.productId,
                productName = m.product?.name ?? "",
                productSku = m.product?.sku ?? "",
                imageUrl = m.product?.images?.FirstOrDefault()?.file?.GetFullPath(_serviceProvider)
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
                otherMembers
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

        if (request.productIds.Count > 100)
            throw ApiException.BadRequest("Невозможно обновить более 100 товаров за раз");

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

        await _unitOfWork.BeginTransactionAsync();
        try
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

                if (hasChanges)
                {
                    productRepo.UpdateWithoutSave(product);
                }

                if (request.updates.attributeValues != null)
                {
                    attrValueRepo.DeleteRange(product.attributeValues.ToList());

                    foreach (var av in request.updates.attributeValues)
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

                if (request.updates.storeAttributeValues != null)
                {
                    storeAttrValueRepo.DeleteRange(product.storeAttributeValues.ToList());

                    foreach (var sav in request.updates.storeAttributeValues)
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

                updatedCount++;
            }

            await _unitOfWork.SaveChangesAsync();
            await _unitOfWork.CommitTransactionAsync();
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync();
            throw;
        }

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
        /// <summary>Category attribute values to replace, or null to leave unchanged.</summary>
        public List<ProductAttributeValue.UserValueForm>? attributeValues { get; set; }
        /// <summary>Store attribute values to replace, or null to leave unchanged.</summary>
        public List<StoreProductAttributeValue.UserValueForm>? storeAttributeValues { get; set; }
    }

    /// <summary>
    /// Deletes a product and all its related entities (files, images, notes, attributes, group memberships).
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

        var productRepo = _unitOfWork.Repository<Product>();

        var product = await productRepo.FindAsync(
            p => p.Id == productId && p.storeId == selectedStoreId.Value,
            query => query
                .Include(p => p.files)
                .Include(p => p.images)
                .Include(p => p.notes)
                .Include(p => p.attributeValues)
                .Include(p => p.storeAttributeValues));

        if (product == null)
            throw ApiException.NotFound("Товар не найден");

        await _unitOfWork.BeginTransactionAsync();
        try
        {
            // Batch delete related entities using ExecuteDeleteAsync (single SQL DELETE per table)
            await _context.pendingProductImports
                .Where(pi => pi.importedProductId == productId)
                .ExecuteDeleteAsync();

            await _context.pendingImageDownloads
                .Where(pid => pid.productId == productId)
                .ExecuteDeleteAsync();

            await _context.productMarketplaceLinks
                .Where(ml => ml.productId == productId)
                .ExecuteDeleteAsync();

            await _context.productGroupMembers
                .Where(gm => gm.productId == productId)
                .ExecuteDeleteAsync();

            // Delete tracked related entities in batch
            var storeAttrValueRepo = _unitOfWork.Repository<StoreProductAttributeValue>();
            var attrValueRepo = _unitOfWork.Repository<ProductAttributeValue>();
            var noteRepo = _unitOfWork.Repository<Note>();
            var imageRepo = _unitOfWork.Repository<ImageReference>();
            var fileRepo = _unitOfWork.Repository<Models.File>();

            storeAttrValueRepo.DeleteRange(product.storeAttributeValues.ToList());
            attrValueRepo.DeleteRange(product.attributeValues.ToList());
            noteRepo.DeleteRange(product.notes.ToList());
            imageRepo.DeleteRange(product.images.ToList());
            fileRepo.DeleteRange(product.files.ToList());
            productRepo.DeleteWithoutSave(product);

            await _unitOfWork.SaveChangesAsync();
            await _unitOfWork.CommitTransactionAsync();
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync();
            throw;
        }

        return Ok(new { next = "/products" });
    }

    /// <summary>
    /// Bulk deletes multiple products and all their related entities within a transaction.
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

        if (request.productIds.Count > 100)
            throw ApiException.BadRequest("Невозможно удалить более 100 товаров за раз");

        // Verify all products belong to the current store
        var productRepo = _unitOfWork.Repository<Product>();
        var products = await productRepo.FindAllAsync(
            p => request.productIds.Contains(p.Id) && p.storeId == selectedStoreId.Value,
            query => query
                .Include(p => p.files)
                .Include(p => p.images)
                .Include(p => p.notes)
                .Include(p => p.attributeValues)
                .Include(p => p.storeAttributeValues));

        var foundIds = products.Select(p => p.Id).ToList();
        var notFoundIds = request.productIds.Except(foundIds).ToList();
        if (notFoundIds.Any())
            throw ApiException.NotFound($"Товары не найдены: {string.Join(", ", notFoundIds)}");

        await _unitOfWork.BeginTransactionAsync();
        try
        {
            // Batch delete related entities using ExecuteDeleteAsync
            await _context.pendingProductImports
                .Where(pi => foundIds.Contains(pi.importedProductId ?? 0))
                .ExecuteDeleteAsync();

            await _context.pendingImageDownloads
                .Where(pid => foundIds.Contains(pid.productId))
                .ExecuteDeleteAsync();

            await _context.productMarketplaceLinks
                .Where(ml => foundIds.Contains(ml.productId))
                .ExecuteDeleteAsync();

            await _context.productGroupMembers
                .Where(gm => foundIds.Contains(gm.productId))
                .ExecuteDeleteAsync();

            // Delete tracked related entities in batch
            var storeAttrValueRepo = _unitOfWork.Repository<StoreProductAttributeValue>();
            var attrValueRepo = _unitOfWork.Repository<ProductAttributeValue>();
            var noteRepo = _unitOfWork.Repository<Note>();
            var imageRepo = _unitOfWork.Repository<ImageReference>();
            var fileRepo = _unitOfWork.Repository<Models.File>();

            foreach (var product in products)
            {
                storeAttrValueRepo.DeleteRange(product.storeAttributeValues.ToList());
                attrValueRepo.DeleteRange(product.attributeValues.ToList());
                noteRepo.DeleteRange(product.notes.ToList());
                imageRepo.DeleteRange(product.images.ToList());
                fileRepo.DeleteRange(product.files.ToList());
                productRepo.DeleteWithoutSave(product);
            }

            await _unitOfWork.SaveChangesAsync();
            await _unitOfWork.CommitTransactionAsync();
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync();
            throw;
        }

        return Ok(new { success = true, deletedCount = products.Count });
    }

    /// <summary>
    /// Request body for bulk-deleting multiple products.
    /// </summary>
    public struct BulkDeleteRequest
    {
        /// <summary>The list of product IDs to delete.</summary>
        public List<int> productIds { get; set; }
    }
}
