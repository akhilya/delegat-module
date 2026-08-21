using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Interfaces;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Services;

namespace Delegat.Controllers.PublicApi;

/// <summary>Public API v1 - product groups CRUD and membership.</summary>
[ApiController]
[PublicApiAuthorize]
[EnableRateLimiting("public-api")]
[Route("/v1/stores/{storeId:int}/groups")]
[Tags("Groups")]
public class PublicGroupsController : ControllerBase
{
    private readonly PublicApiStoreContext _storeCtx;
    private readonly DataContext _db;
    private readonly IUnitOfWork _uow;
    private readonly IServiceProvider _sp;

    public PublicGroupsController(
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

        var groups = await _db.productGroups
            .AsNoTracking()
            .Include(g => g.members)
            .Where(g => g.storeId == storeId)
            .OrderBy(g => g.name)
            .ToListAsync(ct);

        var items = groups.Select(g => new
        {
            id = g.Id,
            name = g.name,
            description = g.description,
            memberCount = g.members.Count,
            createdAt = g.createdAt
        }).ToList();

        return Ok(PublicApiResults.Page(items, 1, items.Count, items.Count));
    }

    [HttpGet("{groupId:int}")]
    public async Task<IActionResult> Get(int storeId, int groupId, CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);

        var group = await _db.productGroups
            .AsNoTracking()
            .Include(g => g.members)
            .FirstOrDefaultAsync(g => g.Id == groupId && g.storeId == storeId, ct);

        if (group == null)
            throw ApiException.NotFound("Group not found", code: "group_not_found");

        return Ok(new
        {
            id = group.Id,
            name = group.name,
            description = group.description,
            productIds = group.members.Select(m => m.productId).ToList(),
            createdAt = group.createdAt
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(int storeId, [FromBody] ProductGroup.UserAddForm input, CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);

        var group = new ProductGroup();
        group.AddFill(new ProductGroup.AddForm
        {
            name = input.name,
            description = input.description,
            storeId = storeId
        });

        await _uow.Repository<ProductGroup>().AddAsync(group);
        return StatusCode(StatusCodes.Status201Created, new { id = group.Id });
    }

    [HttpPatch("{groupId:int}")]
    public async Task<IActionResult> Update(int storeId, int groupId, [FromBody] ProductGroup.UpdateForm input, CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);

        // Must be tracked — Repository.FindAsync is AsNoTracking and would no-op SaveChanges.
        var group = await _uow.Repository<ProductGroup>().QueryTracking()
            .FirstOrDefaultAsync(g => g.Id == groupId && g.storeId == storeId, ct);
        if (group == null)
            throw ApiException.NotFound("Group not found", code: "group_not_found");

        group.UpdateFill(input);
        await _uow.SaveChangesAsync();
        return Ok(new { id = group.Id, success = true });
    }

    [HttpDelete("{groupId:int}")]
    public async Task<IActionResult> Delete(int storeId, int groupId, CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);

        var group = await _db.productGroups
            .Include(g => g.members)
            .FirstOrDefaultAsync(g => g.Id == groupId && g.storeId == storeId, ct);
        if (group == null)
            throw ApiException.NotFound("Group not found", code: "group_not_found");

        _db.productGroupMembers.RemoveRange(group.members);
        _db.productGroups.Remove(group);
        await _db.SaveChangesAsync(ct);
        return Ok(new { success = true });
    }

    [HttpPost("{groupId:int}/products")]
    public async Task<IActionResult> AddProducts(
        int storeId,
        int groupId,
        [FromBody] AddProductsRequest body,
        CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);

        var group = await _db.productGroups
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == groupId && g.storeId == storeId, ct);
        if (group == null)
            throw ApiException.NotFound("Group not found", code: "group_not_found");

        var added = await ProductGroupMembership.AddOrMoveToGroupAsync(
            _db, storeId, groupId, body.productIds, ct);

        return Ok(new { success = true, added });
    }

    [HttpDelete("{groupId:int}/products/{productId:int}")]
    public async Task<IActionResult> RemoveProduct(int storeId, int groupId, int productId, CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);

        var groupExists = await _db.productGroups
            .AnyAsync(g => g.Id == groupId && g.storeId == storeId, ct);
        if (!groupExists)
            throw ApiException.NotFound("Group not found", code: "group_not_found");

        var member = await _db.productGroupMembers
            .FirstOrDefaultAsync(m => m.groupId == groupId && m.productId == productId, ct);
        if (member == null)
            throw ApiException.NotFound("Product is not in the group", code: "group_member_not_found");

        _db.productGroupMembers.Remove(member);
        await _db.SaveChangesAsync(ct);
        return Ok(new { success = true });
    }

    public class AddProductsRequest
    {
        public List<int>? productIds { get; set; }
    }
}
