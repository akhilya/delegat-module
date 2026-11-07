using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Interfaces;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Services;
using Delegat.Services.Marketplace;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
public class ProductGroupController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly DataContext _db;
    private readonly GroupSyncService _groupSyncService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProductGroupController> _logger;

    public ProductGroupController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        DataContext db,
        GroupSyncService groupSyncService,
        IServiceProvider serviceProvider,
        ILogger<ProductGroupController> logger)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _db = db;
        _groupSyncService = groupSyncService;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Get list of store groups
    /// </summary>
    [HttpGet]
    [Route("/productgroups")]
    public async Task<IActionResult> GetAllGroups()
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var groupRepo = _unitOfWork.Repository<ProductGroup>();

        var groups = await groupRepo.FindAllAsync(
            g => g.storeId == selectedStoreId.Value,
            query => query
                .Include(g => g.members)
                    .ThenInclude(m => m.product)
                .Include(g => g.marketplaceLinks)
                    .ThenInclude(l => l.connection));

        var result = groups
            .OrderBy(g => g.name)
            .Select(g => ((IMapToDTO<ProductGroup.GroupDTO>)g).MapToDTO(_serviceProvider))
            .ToList();

        return Ok(result);
    }

    /// <summary>
    /// Get group by ID
    /// </summary>
    [HttpGet]
    [Route("/productgroups/{groupId}")]
    public async Task<IActionResult> GetGroup(int groupId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var groupRepo = _unitOfWork.Repository<ProductGroup>();

        var group = await groupRepo.FindAsync(
            g => g.Id == groupId && g.storeId == selectedStoreId.Value,
            query => query
                .Include(g => g.members)
                    .ThenInclude(m => m.product)
                        .ThenInclude(p => p.images)
                            .ThenInclude(i => i.file)
                .Include(g => g.members)
                    .ThenInclude(m => m.product)
                        .ThenInclude(p => p.category)
                .Include(g => g.marketplaceLinks)
                    .ThenInclude(l => l.connection));

        if (group == null)
            throw ApiException.NotFound(code: "group_not_found");

        return Ok(((Infrastructure.Interfaces.IMapToDTO<ProductGroup.GroupDetailDTO>)group).MapToDTO(_serviceProvider));
    }

    /// <summary>
    /// Create a group
    /// </summary>
    [HttpPost]
    [Route("/productgroups")]
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> CreateGroup([FromBody] ProductGroup.UserAddForm input)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var groupRepo = _unitOfWork.Repository<ProductGroup>();

        // Check name uniqueness
        var existingGroup = await groupRepo.FindAsync(
            g => g.storeId == selectedStoreId.Value && g.name == input.name);
        if (existingGroup != null)
            throw ApiException.BadRequest(code: "group_name_taken");

        var group = new ProductGroup();
        group.AddFill(new ProductGroup.AddForm
        {
            name = input.name,
            description = input.description,
            storeId = selectedStoreId.Value
        });

        await groupRepo.AddAsync(group);

        if (input.productIds != null && input.productIds.Count > 0)
        {
            await ProductGroupMembership.AddOrMoveToGroupAsync(
                _db, selectedStoreId.Value, group.Id, input.productIds);
        }

        return Ok(new { id = group.Id, name = group.name, description = group.description });
    }

    /// <summary>
    /// Update a group
    /// </summary>
    [HttpPatch]
    [Route("/productgroups/{groupId}")]
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> UpdateGroup(int groupId, [FromBody] ProductGroup.UpdateForm input)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var groupRepo = _unitOfWork.Repository<ProductGroup>();

        var group = await groupRepo.FindAsync(
            g => g.Id == groupId && g.storeId == selectedStoreId.Value);

        if (group == null)
            throw ApiException.NotFound(code: "group_not_found");

        // Check name uniqueness if changing
        if (input.name != null && input.name != group.name)
        {
            var existingGroup = await groupRepo.FindAsync(
                g => g.storeId == selectedStoreId.Value && g.name == input.name && g.Id != groupId);
            if (existingGroup != null)
                throw ApiException.BadRequest(code: "group_name_taken");
        }

        group.UpdateFill(input);
        await groupRepo.UpdateAsync(group);

        return Ok(new { success = true });
    }

    /// <summary>
    /// Delete a group
    /// </summary>
    [HttpDelete]
    [Route("/productgroups/{groupId}")]
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> DeleteGroup(int groupId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var groupRepo = _unitOfWork.Repository<ProductGroup>();

        var group = await groupRepo.FindAsync(
            g => g.Id == groupId && g.storeId == selectedStoreId.Value,
            query => query
                .Include(g => g.members)
                .Include(g => g.marketplaceLinks));

        if (group == null)
            throw ApiException.NotFound(code: "group_not_found");

        // Delete related records
        var memberRepo = _unitOfWork.Repository<ProductGroupMember>();
        foreach (var member in group.members.ToList())
        {
            await memberRepo.DeleteAsync(member);
        }

        var linkRepo = _unitOfWork.Repository<ProductGroupMarketplaceLink>();
        foreach (var link in group.marketplaceLinks.ToList())
        {
            await linkRepo.DeleteAsync(link);
        }

        await groupRepo.DeleteAsync(group);

        return Ok(new { success = true });
    }

    #region Group Membership

    /// <summary>Merge local source groups into this group. No marketplace requests are made.</summary>
    [HttpPost]
    [Route("/productgroups/{groupId:int}/merge")]
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> MergeGroups(int groupId, [FromBody] MergeGroupsRequest request,
        CancellationToken ct = default)
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync();
        if (storeId == null)
            throw ApiException.BadRequest(code: "store_not_selected");
        return Ok(await ProductGroupMerge.MergeAsync(_db, storeId.Value, groupId, request.sourceGroupIds, ct));
    }

    public sealed class MergeGroupsRequest
    {
        public List<int>? sourceGroupIds { get; set; }
    }

    /// <summary>
    /// Add products to a group
    /// </summary>
    [HttpPost]
    [Route("/productgroups/{groupId}/products")]
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> AddProductsToGroup(int groupId, [FromBody] AddProductsRequest request)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var groupRepo = _unitOfWork.Repository<ProductGroup>();

        var group = await groupRepo.FindAsync(
            g => g.Id == groupId && g.storeId == selectedStoreId.Value);

        if (group == null)
            throw ApiException.NotFound(code: "group_not_found");

        var addedCount = await ProductGroupMembership.AddOrMoveToGroupAsync(
            _db, selectedStoreId.Value, groupId, request.productIds);

        return Ok(new { addedCount });
    }

    /// <summary>
    /// Remove a product from a group
    /// </summary>
    [HttpDelete]
    [Route("/productgroups/{groupId}/products/{productId}")]
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> RemoveProductFromGroup(int groupId, int productId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var groupRepo = _unitOfWork.Repository<ProductGroup>();

        var group = await groupRepo.FindAsync(
            g => g.Id == groupId && g.storeId == selectedStoreId.Value);

        if (group == null)
            throw ApiException.NotFound(code: "group_not_found");

        var memberRepo = _unitOfWork.Repository<ProductGroupMember>();
        var member = await memberRepo.FindAsync(
            m => m.groupId == groupId && m.productId == productId);

        if (member == null)
            throw ApiException.NotFound(code: "product_not_in_group");

        await memberRepo.DeleteAsync(member);

        return Ok(new { success = true });
    }

    /// <summary>
    /// Update sort order or leader flag for a product in the group
    /// </summary>
    [HttpPatch]
    [Route("/productgroups/{groupId}/products/{productId}")]
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> UpdateGroupMember(
        int groupId, int productId, [FromBody] ProductGroupMember.UpdateForm input)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var groupRepo = _unitOfWork.Repository<ProductGroup>();

        var group = await groupRepo.FindAsync(
            g => g.Id == groupId && g.storeId == selectedStoreId.Value);

        if (group == null)
            throw ApiException.NotFound(code: "group_not_found");

        var memberRepo = _unitOfWork.Repository<ProductGroupMember>();
        var member = await memberRepo.FindAsync(
            m => m.groupId == groupId && m.productId == productId);

        if (member == null)
            throw ApiException.NotFound(code: "product_not_in_group");

        member.UpdateFill(input);
        await memberRepo.UpdateAsync(member);

        return Ok(new { success = true });
    }

    #endregion

    #region Marketplace Integration

    /// <summary>
    /// Export a group to a marketplace
    /// </summary>
    [HttpPost]
    [Route("/productgroups/{groupId}/export/{connectionId}")]
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> ExportGroup(int groupId, int connectionId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var groupRepo = _unitOfWork.Repository<ProductGroup>();

        var group = await groupRepo.FindAsync(
            g => g.Id == groupId && g.storeId == selectedStoreId.Value);

        if (group == null)
            throw ApiException.NotFound(code: "group_not_found");

        var result = await _groupSyncService.ExportGroupAsync(groupId, connectionId);

        if (!result.IsSuccess)
            throw ApiException.BadRequest(code: "export_failed");

        return Ok(new
        {
            success = true,
            linkId = result.LinkId,
            externalGroupId = result.ExternalGroupId,
            isFieldBased = result.IsFieldBased,
            message = result.Message
        });
    }

    /// <summary>
    /// Import groups from a marketplace
    /// </summary>
    [HttpPost]
    [Route("/productgroups/import/{connectionId}")]
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> ImportGroups(int connectionId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var result = await _groupSyncService.ImportGroupsAsync(connectionId, selectedStoreId.Value);

        if (!result.IsSuccess)
            throw ApiException.BadRequest(code: "import_failed");

        return Ok(new
        {
            success = true,
            imported = result.ImportedCount,
            updated = result.UpdatedCount,
            skipped = result.SkippedCount,
            errors = result.Errors
        });
    }

    /// <summary>
    /// Get group synchronization status
    /// </summary>
    [HttpGet]
    [Route("/productgroups/{groupId}/sync-status")]
    public async Task<IActionResult> GetSyncStatus(int groupId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var groupRepo = _unitOfWork.Repository<ProductGroup>();

        var group = await groupRepo.FindAsync(
            g => g.Id == groupId && g.storeId == selectedStoreId.Value,
            query => query
                .Include(g => g.marketplaceLinks)
                    .ThenInclude(l => l.connection));

        if (group == null)
            throw ApiException.NotFound(code: "group_not_found");

        var syncStatuses = group.marketplaceLinks.Select(l => new
        {
            connectionId = l.connectionId,
            connectionName = l.connection.name,
            adapterKey = l.connection.adapterKey,
            externalGroupId = l.externalGroupId,
            externalGroupName = l.externalGroupName,
            status = l.status.ToString(),
            lastSyncAt = l.lastSyncAt,
            lastErrorMessage = l.lastErrorMessage
        }).ToList();

        return Ok(syncStatuses);
    }

    /// <summary>
    /// Delete a group on the marketplace
    /// </summary>
    [HttpDelete]
    [Route("/productgroups/{groupId}/marketplace/{connectionId}")]
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> DeleteMarketplaceGroup(int groupId, int connectionId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var groupRepo = _unitOfWork.Repository<ProductGroup>();

        var group = await groupRepo.FindAsync(
            g => g.Id == groupId && g.storeId == selectedStoreId.Value);

        if (group == null)
            throw ApiException.NotFound(code: "group_not_found");

        var result = await _groupSyncService.DeleteMarketplaceGroupAsync(groupId, connectionId);

        if (!result.Success)
            throw ApiException.BadRequest(code: "group_marketplace_sync_failed");

        return Ok(new { success = true });
    }

    /// <summary>
    /// Add products to a marketplace group (for endpoint-based)
    /// </summary>
    [HttpPost]
    [Route("/productgroups/{groupId}/marketplace/{connectionId}/products")]
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> AddProductsToMarketplaceGroup(
        int groupId, int connectionId, [FromBody] AddProductsRequest request)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var groupRepo = _unitOfWork.Repository<ProductGroup>();

        var group = await groupRepo.FindAsync(
            g => g.Id == groupId && g.storeId == selectedStoreId.Value);

        if (group == null)
            throw ApiException.NotFound(code: "group_not_found");

        var result = await _groupSyncService.AddProductsToMarketplaceGroupAsync(
            groupId, connectionId, request.productIds);

        if (!result.Success)
            throw ApiException.BadRequest(code: "group_marketplace_sync_failed");

        return Ok(new
        {
            success = true,
            addedCount = result.AddedCount,
            failedCount = result.FailedCount,
            errors = result.Errors
        });
    }

    /// <summary>
    /// Remove products from a marketplace group (for endpoint-based)
    /// </summary>
    [HttpDelete]
    [Route("/productgroups/{groupId}/marketplace/{connectionId}/products")]
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> RemoveProductsFromMarketplaceGroup(
        int groupId, int connectionId, [FromBody] AddProductsRequest request)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var groupRepo = _unitOfWork.Repository<ProductGroup>();

        var group = await groupRepo.FindAsync(
            g => g.Id == groupId && g.storeId == selectedStoreId.Value);

        if (group == null)
            throw ApiException.NotFound(code: "group_not_found");

        var result = await _groupSyncService.RemoveProductsFromMarketplaceGroupAsync(
            groupId, connectionId, request.productIds);

        if (!result.Success)
            throw ApiException.BadRequest(code: "group_marketplace_sync_failed");

        return Ok(new
        {
            success = true,
            removedCount = result.RemovedCount,
            failedCount = result.FailedCount,
            errors = result.Errors
        });
    }

    #endregion
}

#region Request DTOs

public class AddProductsRequest
{
    public List<int> productIds { get; set; } = new();
}

#endregion
