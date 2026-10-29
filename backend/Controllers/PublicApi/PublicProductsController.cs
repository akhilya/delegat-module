using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Interfaces;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Products;
using Delegat.Services;

namespace Delegat.Controllers.PublicApi;

/// <summary>
/// Public API v1 - product catalog CRUD for a store.
/// </summary>
[ApiController]
[PublicApiAuthorize]
[EnableRateLimiting("public-api")]
[Route("/v1/stores/{storeId:int}/products")]
[Tags("Products")]
public class PublicProductsController : ControllerBase
{
    private readonly PublicApiStoreContext _storeCtx;
    private readonly DataContext _db;
    private readonly IUnitOfWork _uow;
    private readonly QuotaService _quota;
    private readonly AttributeUniquenessService _attributeUniqueness;

    public PublicProductsController(
        PublicApiStoreContext storeCtx,
        DataContext db,
        IUnitOfWork uow,
        QuotaService quota,
        AttributeUniquenessService attributeUniqueness)
    {
        _storeCtx = storeCtx;
        _db = db;
        _uow = uow;
        _quota = quota;
        _attributeUniqueness = attributeUniqueness;
    }

    /// <summary>Lists products. Use <c>categoryId=-1</c> for products without a category.</summary>
    /// <param name="storeId">Store id from path (membership required).</param>
    /// <param name="search">Optional filter by name, SKU, barcode, product id, marketplace ids, or variant SKU/barcode.</param>
    /// <param name="categoryId">Optional category filter; <c>-1</c> = no category.</param>
    /// <param name="page">1-based page index.</param>
    /// <param name="pageSize">Page size (max 200).</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        int storeId,
        [FromQuery] string? search = null,
        [FromQuery] int? categoryId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);
        (page, pageSize) = PublicApiPaging.Normalize(page, pageSize);

        var q = _db.products.AsNoTracking().Where(p => p.storeId == storeId);
        q = q.WhereMatchesSearch(search);
        if (categoryId == -1)
            q = q.Where(p => p.categoryId == null);
        else if (categoryId.HasValue)
            q = q.Where(p => p.categoryId == categoryId.Value);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(p => p.updatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                id = p.Id,
                sku = p.sku,
                barcode = p.barcode,
                name = p.name,
                description = p.description,
                categoryId = p.categoryId,
                storageQuantity = p.storageQuantity,
                price = p.price,
                oldPrice = p.oldPrice,
                minPrice = p.minPrice,
                netPrice = p.netPrice,
                currency = p.currency,
                variantCount = p.variants.Count,
                createdAt = p.createdAt,
                updatedAt = p.updatedAt
            })
            .ToListAsync(ct);

        return Ok(PublicApiResults.Page(items, page, pageSize, total));
    }

    /// <summary>Gets a single product with attribute values and size variants.</summary>
    [HttpGet("{productId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int storeId, int productId, CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);

        var product = await _db.products
            .AsNoTracking()
            .Include(p => p.attributeValues)
            .Include(p => p.storeAttributeValues)
            .Include(p => p.variants)
            .FirstOrDefaultAsync(p => p.Id == productId && p.storeId == storeId, ct);

        if (product == null)
            throw ApiException.NotFound(code: "product_not_found");

        return Ok(MapDetail(product));
    }

    /// <summary>Creates a product. Enforces store product quota.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(int storeId, [FromBody] Product.UserAddForm input, CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);
        await _quota.EnsureProductQuotaAsync(storeId);
        await EnsureCategoryInStoreAsync(storeId, input.categoryId, ct);
        EnsureBarcodeValid(input.barcode);

        var repo = _uow.Repository<Product>();
        var existing = await repo.FindAsync(p => p.storeId == storeId && p.sku == input.sku);
        if (existing != null)
            throw new ApiException(
                statusCode: StatusCodes.Status409Conflict,
                data: new { sku = input.sku },
                code: "sku_already_used");

        await _attributeUniqueness.EnsureUniqueAsync(
            storeId,
            input.categoryId,
            input.attributeValues,
            input.storeAttributeValues,
            ct: ct);

        var product = new Product();
        product.AddFill(new Product.AddForm(input, storeId, input.categoryId));
        await repo.AddAsync(product);

        if (input.attributeValues is { Count: > 0 })
        {
            var attrRepo = _uow.Repository<ProductAttributeValue>();
            foreach (var av in input.attributeValues)
            {
                var attrValue = new ProductAttributeValue();
                attrValue.AddFill(new ProductAttributeValue.AddForm
                {
                    productId = product.Id,
                    attributeId = av.attributeId,
                    value = av.value
                });
                await attrRepo.AddWithoutSaveAsync(attrValue);
            }
            await _uow.SaveChangesAsync();
        }

        if (input.storeAttributeValues is { Count: > 0 })
        {
            var storeAttrRepo = _uow.Repository<StoreProductAttributeValue>();
            foreach (var sav in input.storeAttributeValues)
            {
                var storeAttrValue = new StoreProductAttributeValue();
                storeAttrValue.AddFill(new StoreProductAttributeValue.AddForm
                {
                    productId = product.Id,
                    storeAttributeId = sav.storeAttributeId,
                    value = sav.value
                });
                await storeAttrRepo.AddWithoutSaveAsync(storeAttrValue);
            }
            await _uow.SaveChangesAsync();
        }

        return StatusCode(StatusCodes.Status201Created, new { id = product.Id });
    }

    /// <summary>Updates a product.</summary>
    /// <remarks>Partial update. When <c>attributeValues</c> or <c>storeAttributeValues</c> is sent, that collection is replaced.</remarks>
    [HttpPatch("{productId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int storeId, int productId, [FromBody] Product.UpdateForm input, CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);

        var product = await _uow.Repository<Product>().QueryTracking()
            .Include(p => p.attributeValues)
            .Include(p => p.storeAttributeValues)
            .FirstOrDefaultAsync(p => p.Id == productId && p.storeId == storeId, ct);
        if (product == null)
            throw ApiException.NotFound(code: "product_not_found");

        await EnsureCategoryInStoreAsync(storeId, input.categoryId, ct);
        if (!string.IsNullOrEmpty(input.barcode))
            EnsureBarcodeValid(input.barcode);

        await _attributeUniqueness.EnsureUniqueAsync(
            storeId,
            input.categoryId ?? product.categoryId,
            input.attributeValues,
            input.storeAttributeValues,
            excludeProductId: productId,
            ct: ct);

        product.UpdateFill(input);

        if (input.attributeValues != null)
        {
            var attrRepo = _uow.Repository<ProductAttributeValue>();
            attrRepo.DeleteRange(product.attributeValues.ToList());
            foreach (var av in input.attributeValues)
            {
                var attrValue = new ProductAttributeValue();
                attrValue.AddFill(new ProductAttributeValue.AddForm
                {
                    productId = product.Id,
                    attributeId = av.attributeId,
                    value = av.value
                });
                await attrRepo.AddWithoutSaveAsync(attrValue);
            }
        }

        if (input.storeAttributeValues != null)
        {
            var storeAttrRepo = _uow.Repository<StoreProductAttributeValue>();
            storeAttrRepo.DeleteRange(product.storeAttributeValues.ToList());
            foreach (var sav in input.storeAttributeValues)
            {
                var storeAttrValue = new StoreProductAttributeValue();
                storeAttrValue.AddFill(new StoreProductAttributeValue.AddForm
                {
                    productId = product.Id,
                    storeAttributeId = sav.storeAttributeId,
                    value = sav.value
                });
                await storeAttrRepo.AddWithoutSaveAsync(storeAttrValue);
            }
        }

        await _uow.SaveChangesAsync();
        return Ok(new { id = product.Id, success = true });
    }

    /// <summary>Soft-deletes a product (moves to trash).</summary>
    [HttpDelete("{productId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int storeId, int productId, CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);

        var product = await _db.products
            .FirstOrDefaultAsync(p => p.Id == productId && p.storeId == storeId, ct);
        if (product == null)
            throw ApiException.NotFound(code: "product_not_found");

        product.deletedAt = DateTime.UtcNow;
        product.updatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { success = true });
    }

    /// <summary>Lists size variants for a product (SKU, barcode, price, stock).</summary>
    [HttpGet("{productId:int}/variants")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListVariants(int storeId, int productId, CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);
        await RequireProductAsync(storeId, productId, ct);

        var variants = await _db.productVariants
            .AsNoTracking()
            .Where(v => v.productId == productId)
            .OrderBy(v => v.sortOrder)
            .Select(v => new
            {
                id = v.Id,
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

    /// <summary>Creates a size variant on a product.</summary>
    [HttpPost("{productId:int}/variants")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateVariant(
        int storeId,
        int productId,
        [FromBody] VariantWriteRequest input,
        CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);
        await RequireProductAsync(storeId, productId, ct);

        var maxSortOrder = await _db.productVariants
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
        _db.productVariants.Add(variant);
        await _db.SaveChangesAsync(ct);

        return StatusCode(StatusCodes.Status201Created, new { id = variant.Id });
    }

    /// <summary>Partially updates a size variant.</summary>
    [HttpPatch("{productId:int}/variants/{variantId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateVariant(
        int storeId,
        int productId,
        int variantId,
        [FromBody] VariantWriteRequest input,
        CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);
        await RequireProductAsync(storeId, productId, ct);

        var variant = await _db.productVariants
            .FirstOrDefaultAsync(v => v.Id == variantId && v.productId == productId, ct);
        if (variant == null)
            throw ApiException.NotFound(code: "variant_not_found");

        variant.UpdateFill(new ProductVariant.UpdateForm
        {
            sku = input.sku,
            barcode = input.barcode,
            size = input.size,
            price = input.price,
            quantity = input.quantity,
            sortOrder = input.sortOrder
        });
        await _db.SaveChangesAsync(ct);
        return Ok(new { id = variant.Id, success = true });
    }

    /// <summary>Deletes a size variant.</summary>
    [HttpDelete("{productId:int}/variants/{variantId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteVariant(int storeId, int productId, int variantId, CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);
        await RequireProductAsync(storeId, productId, ct);

        var variant = await _db.productVariants
            .FirstOrDefaultAsync(v => v.Id == variantId && v.productId == productId, ct);
        if (variant == null)
            throw ApiException.NotFound(code: "variant_not_found");

        _db.productVariants.Remove(variant);
        await _db.SaveChangesAsync(ct);
        return Ok(new { success = true });
    }

    /// <summary>Body for creating or updating a product size variant.</summary>
    public class VariantWriteRequest
    {
        public string? sku { get; set; }
        public string? barcode { get; set; }
        public string? size { get; set; }
        public decimal? price { get; set; }
        public int? quantity { get; set; }
        public int? sortOrder { get; set; }
    }

    private async Task RequireProductAsync(int storeId, int productId, CancellationToken ct)
    {
        var exists = await _db.products.AnyAsync(p => p.Id == productId && p.storeId == storeId, ct);
        if (!exists)
            throw ApiException.NotFound(code: "product_not_found");
    }

    private async Task EnsureCategoryInStoreAsync(int storeId, int? categoryId, CancellationToken ct)
    {
        if (!categoryId.HasValue)
            return;

        var exists = await _db.categories.AnyAsync(c => c.Id == categoryId.Value && c.storeId == storeId, ct);
        if (!exists)
            throw ApiException.NotFound(code: "category_not_found");
    }

    private static void EnsureBarcodeValid(string? barcode)
    {
        if (string.IsNullOrEmpty(barcode))
            return;
        if (!ProductController.IsValidEanBarcode(barcode))
            throw ApiException.BadRequest(
                "Invalid barcode. EAN-8 (8 digits) or EAN-13 (13 digits) are allowed",
                code: "invalid_barcode");
    }

    private static object MapDetail(Product p) => new
    {
        id = p.Id,
        sku = p.sku,
        barcode = p.barcode,
        name = p.name,
        description = p.description,
        categoryId = p.categoryId,
        storageQuantity = p.storageQuantity,
        price = p.price,
        oldPrice = p.oldPrice,
        minPrice = p.minPrice,
        netPrice = p.netPrice,
        discountedPrice = p.discountedPrice,
        discount = p.discount,
        currency = p.currency,
        createdAt = p.createdAt,
        updatedAt = p.updatedAt,
        attributeValues = p.attributeValues.Select(av => new
        {
            attributeId = av.attributeId,
            value = av.value
        }),
        storeAttributeValues = p.storeAttributeValues.Select(sav => new
        {
            storeAttributeId = sav.storeAttributeId,
            value = sav.value
        }),
        variants = p.variants
            .OrderBy(v => v.sortOrder)
            .Select(v => MapVariant(v.Id, v.sku, v.barcode, v.size, v.price, v.quantity, v.sortOrder, v.createdAt, v.updatedAt))
    };

    private static object MapVariant(
        int id,
        string? sku,
        string? barcode,
        string? size,
        decimal? price,
        int? quantity,
        int sortOrder,
        DateTime createdAt,
        DateTime updatedAt) => new
    {
        id,
        sku,
        barcode,
        size,
        price,
        quantity,
        sortOrder,
        createdAt,
        updatedAt
    };
}
