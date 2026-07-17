using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Interfaces;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Products;
using Delegat.Services;

namespace Delegat.Controllers.PublicApi;

/// <summary>
/// Public API v1 — product catalog CRUD for a store.
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
    private readonly IServiceProvider _sp;

    public PublicProductsController(
        PublicApiStoreContext storeCtx,
        DataContext db,
        IUnitOfWork uow,
        QuotaService quota,
        IServiceProvider sp)
    {
        _storeCtx = storeCtx;
        _db = db;
        _uow = uow;
        _quota = quota;
        _sp = sp;
    }

    /// <summary>Lists products. Use <c>categoryId=-1</c> for products without a category.</summary>
    /// <param name="storeId">Store id from path (membership required).</param>
    /// <param name="search">Optional filter by name or SKU.</param>
    /// <param name="categoryId">Optional category filter; <c>-1</c> = no category.</param>
    /// <param name="page">1-based page index.</param>
    /// <param name="pageSize">Page size (max 200).</param>
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
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(p => p.name.Contains(search) || p.sku.Contains(search));
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
                createdAt = p.createdAt,
                updatedAt = p.updatedAt
            })
            .ToListAsync(ct);

        return Ok(PublicApiResults.Page(items, page, pageSize, total));
    }

    /// <summary>Gets a single product with attribute values.</summary>
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
            .FirstOrDefaultAsync(p => p.Id == productId && p.storeId == storeId, ct);

        if (product == null)
            throw ApiException.NotFound("Товар не найден");

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

        var repo = _uow.Repository<Product>();
        var existing = await repo.FindAsync(p => p.storeId == storeId && p.sku == input.sku);
        if (existing != null)
            throw ApiException.Conflict("Товар с таким артикулом уже существует");

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

    /// <summary>Partially updates a product.</summary>
    [HttpPatch("{productId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int storeId, int productId, [FromBody] Product.UpdateForm input, CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);

        var repo = _uow.Repository<Product>();
        var product = await repo.FindAsync(p => p.Id == productId && p.storeId == storeId);
        if (product == null)
            throw ApiException.NotFound("Товар не найден");

        product.UpdateFill(input);
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
            throw ApiException.NotFound("Товар не найден");

        product.deletedAt = DateTime.UtcNow;
        product.updatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { success = true });
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
        })
    };
}
