using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Interfaces;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;

namespace Delegat.Controllers.PublicApi;

/// <summary>Public API v1 — store-level (global) attributes.</summary>
[ApiController]
[PublicApiAuthorize]
[EnableRateLimiting("public-api")]
[Route("/v1/stores/{storeId:int}/store-attributes")]
[Tags("Store attributes")]
public class PublicStoreAttributesController : ControllerBase
{
    private readonly PublicApiStoreContext _storeCtx;
    private readonly DataContext _db;
    private readonly IUnitOfWork _uow;
    private readonly IServiceProvider _sp;

    public PublicStoreAttributesController(
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

        var attributes = await _db.storeAttributes
            .AsNoTracking()
            .Where(a => a.storeId == storeId)
            .OrderBy(a => a.sortOrder)
            .ToListAsync(ct);

        var items = attributes
            .Select(a => ((IMapToDTO<StoreAttribute.AttributeDTO>)a).MapToDTO(_sp))
            .ToList();

        return Ok(PublicApiResults.Page(items, 1, items.Count, items.Count));
    }

    [HttpGet("{attributeId:int}")]
    public async Task<IActionResult> Get(int storeId, int attributeId, CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);

        var attribute = await _db.storeAttributes
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attributeId && a.storeId == storeId, ct);

        if (attribute == null)
            throw ApiException.NotFound("Атрибут не найден");

        return Ok(((IMapToDTO<StoreAttribute.AttributeDTO>)attribute).MapToDTO(_sp));
    }

    [HttpPost]
    public async Task<IActionResult> Create(int storeId, [FromBody] StoreAttribute.UserAddForm input, CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);

        var existing = await _db.storeAttributes
            .AnyAsync(a => a.storeId == storeId && a.key == input.key, ct);
        if (existing)
            throw ApiException.Conflict("Атрибут с таким ключом уже существует");

        var maxSort = await _db.storeAttributes
            .Where(a => a.storeId == storeId)
            .Select(a => (int?)a.sortOrder)
            .MaxAsync(ct) ?? -1;

        var attribute = new StoreAttribute();
        attribute.AddFill(new StoreAttribute.AddForm
        {
            name = input.name,
            key = input.key,
            type = input.type,
            isRequired = input.isRequired,
            isMultiline = input.isMultiline,
            showOnProductPage = input.showOnProductPage,
            storeId = storeId,
            sortOrder = maxSort + 1
        });

        await _uow.Repository<StoreAttribute>().AddAsync(attribute);
        return StatusCode(StatusCodes.Status201Created, new { id = attribute.Id });
    }

    [HttpPatch("{attributeId:int}")]
    public async Task<IActionResult> Update(int storeId, int attributeId, [FromBody] StoreAttribute.UpdateForm input, CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);

        var repo = _uow.Repository<StoreAttribute>();
        var attribute = await repo.FindAsync(a => a.Id == attributeId && a.storeId == storeId);
        if (attribute == null)
            throw ApiException.NotFound("Атрибут не найден");

        attribute.UpdateFill(input);
        await _uow.SaveChangesAsync();
        return Ok(new { id = attribute.Id, success = true });
    }

    [HttpDelete("{attributeId:int}")]
    public async Task<IActionResult> Delete(int storeId, int attributeId, CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);

        var attribute = await _db.storeAttributes
            .FirstOrDefaultAsync(a => a.Id == attributeId && a.storeId == storeId, ct);
        if (attribute == null)
            throw ApiException.NotFound("Атрибут не найден");

        _db.storeAttributes.Remove(attribute);
        await _db.SaveChangesAsync(ct);
        return Ok(new { success = true });
    }
}
