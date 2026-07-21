using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Interfaces;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;

namespace Delegat.Controllers.PublicApi;

/// <summary>Public API v1 - categories CRUD.</summary>
[ApiController]
[PublicApiAuthorize]
[EnableRateLimiting("public-api")]
[Route("/v1/stores/{storeId:int}/categories")]
[Tags("Categories")]
public class PublicCategoriesController : ControllerBase
{
    private readonly PublicApiStoreContext _storeCtx;
    private readonly DataContext _db;
    private readonly IUnitOfWork _uow;
    private readonly IServiceProvider _sp;

    public PublicCategoriesController(
        PublicApiStoreContext storeCtx,
        DataContext db,
        IUnitOfWork uow,
        IServiceProvider sp)
    {
        _storeCtx = storeCtx;
        _db = db;
        _uow = uow;
        _sp = sp;
    }

    [HttpGet]
    public async Task<IActionResult> List(int storeId, CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);

        var categories = await _db.categories
            .AsNoTracking()
            .Include(c => c.attributes)
            .Where(c => c.storeId == storeId)
            .OrderBy(c => c.name)
            .ToListAsync(ct);

        var productCounts = await _db.products
            .Where(p => p.storeId == storeId && p.categoryId != null)
            .GroupBy(p => p.categoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.CategoryId!.Value, g => g.Count, ct);

        var items = categories.Select(c => new
        {
            id = c.Id,
            name = c.name,
            description = c.description,
            hasSizes = c.hasSizes,
            createdAt = c.createdAt,
            productCount = productCounts.GetValueOrDefault(c.Id, 0),
            attributes = c.attributes.OrderBy(a => a.sortOrder).Select(a => new
            {
                id = a.Id,
                name = a.name,
                key = a.key,
                type = a.type.ToString(),
                isRequired = a.isRequired
            })
        }).ToList();

        return Ok(PublicApiResults.Page(items, 1, items.Count, items.Count));
    }

    [HttpGet("{categoryId:int}")]
    public async Task<IActionResult> Get(int storeId, int categoryId, CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);

        var category = await _db.categories
            .AsNoTracking()
            .Include(c => c.attributes)
            .FirstOrDefaultAsync(c => c.Id == categoryId && c.storeId == storeId, ct);

        if (category == null)
            throw ApiException.NotFound("Category not found", code: "category_not_found");

        return Ok(((IMapToDTO<Category.CategoryDetailDTO>)category).MapToDTO(_sp));
    }

    [HttpPost]
    public async Task<IActionResult> Create(int storeId, [FromBody] Category.UserAddForm input, CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);

        var category = new Category();
        category.AddFill(new Category.AddForm
        {
            name = input.name,
            description = input.description,
            storeId = storeId
        });

        var repo = _uow.Repository<Category>();
        await repo.AddAsync(category);
        return StatusCode(StatusCodes.Status201Created, new { id = category.Id });
    }

    [HttpPatch("{categoryId:int}")]
    public async Task<IActionResult> Update(int storeId, int categoryId, [FromBody] Category.UpdateForm input, CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);

        var repo = _uow.Repository<Category>();
        var category = await repo.FindAsync(c => c.Id == categoryId && c.storeId == storeId);
        if (category == null)
            throw ApiException.NotFound("Category not found", code: "category_not_found");

        category.UpdateFill(input);
        await _uow.SaveChangesAsync();
        return Ok(new { id = category.Id, success = true });
    }

    [HttpDelete("{categoryId:int}")]
    public async Task<IActionResult> Delete(int storeId, int categoryId, CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);

        var category = await _db.categories
            .FirstOrDefaultAsync(c => c.Id == categoryId && c.storeId == storeId, ct);
        if (category == null)
            throw ApiException.NotFound("Category not found", code: "category_not_found");

        var hasProducts = await _db.products.AnyAsync(p => p.categoryId == categoryId, ct);
        if (hasProducts)
            throw ApiException.Conflict("Cannot delete a category that has products");

        _db.categories.Remove(category);
        await _db.SaveChangesAsync(ct);
        return Ok(new { success = true });
    }
}
