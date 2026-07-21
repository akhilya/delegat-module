using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Interfaces;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Services.Marketplace;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
public class ProductGroupController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly GroupSyncService _groupSyncService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProductGroupController> _logger;

    public ProductGroupController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        GroupSyncService groupSyncService,
        IServiceProvider serviceProvider,
        ILogger<ProductGroupController> logger)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
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
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

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
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

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
            throw ApiException.NotFound("Group not found", code: "group_not_found");

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
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var groupRepo = _unitOfWork.Repository<ProductGroup>();

        // Check name uniqueness
        var existingGroup = await groupRepo.FindAsync(
            g => g.storeId == selectedStoreId.Value && g.name == input.name);
        if (existingGroup != null)
            throw ApiException.BadRequest("A group with this name already exists");

        var group = new ProductGroup();
        group.AddFill(new ProductGroup.AddForm
        {
            name = input.name,
            description = input.description,
            storeId = selectedStoreId.Value
        });

        // Check if products are already in other groups
        if (input.productIds != null && input.productIds.Count > 0)
        {
            var memberRepo = _unitOfWork.Repository<ProductGroupMember>();
            var productsInOtherGroups = await memberRepo.FindAllAsync(
                m => input.productIds.Contains(m.productId),
                query => query.Include(m => m.group).Include(m => m.product));

            if (productsInOtherGroups.Any())
            {
                var conflictMessages = productsInOtherGroups
                    .Select(m => $"Product \"{m.product.name}\" is already in group \"{m.group.name}\"")
                    .ToList();
                throw ApiException.BadRequest(string.Join("; ", conflictMessages));
            }
        }

        await groupRepo.AddAsync(group);

        // Add products if specified
        if (input.productIds != null && input.productIds.Count > 0)
        {
            var memberRepo = _unitOfWork.Repository<ProductGroupMember>();
            var sortOrder = 0;

            foreach (var productId in input.productIds)
            {
                var member = new ProductGroupMember();
                member.AddFill(new ProductGroupMember.AddForm
                {
                    groupId = group.Id,
                    productId = productId,
                    sortOrder = sortOrder++
                });
                await memberRepo.AddAsync(member);
            }
        }

        return Ok(new { id = group.Id });
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
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var groupRepo = _unitOfWork.Repository<ProductGroup>();

        var group = await groupRepo.FindAsync(
            g => g.Id == groupId && g.storeId == selectedStoreId.Value);

        if (group == null)
            throw ApiException.NotFound("Group not found", code: "group_not_found");

        // Check name uniqueness if changing
        if (input.name != null && input.name != group.name)
        {
            var existingGroup = await groupRepo.FindAsync(
                g => g.storeId == selectedStoreId.Value && g.name == input.name && g.Id != groupId);
            if (existingGroup != null)
                throw ApiException.BadRequest("A group with this name already exists");
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
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var groupRepo = _unitOfWork.Repository<ProductGroup>();

        var group = await groupRepo.FindAsync(
            g => g.Id == groupId && g.storeId == selectedStoreId.Value,
            query => query
                .Include(g => g.members)
                .Include(g => g.marketplaceLinks));

        if (group == null)
            throw ApiException.NotFound("Group not found", code: "group_not_found");

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
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var groupRepo = _unitOfWork.Repository<ProductGroup>();

        var group = await groupRepo.FindAsync(
            g => g.Id == groupId && g.storeId == selectedStoreId.Value,
            query => query.Include(g => g.members));

        if (group == null)
            throw ApiException.NotFound("Group not found", code: "group_not_found");

        var memberRepo = _unitOfWork.Repository<ProductGroupMember>();
        var existingProductIds = group.members.Select(m => m.productId).ToHashSet();
        var maxSortOrder = group.members.Any() ? group.members.Max(m => m.sortOrder) : -1;

        // Check if any products are already in other groups
        var productsInOtherGroups = await memberRepo.FindAllAsync(
            m => request.productIds.Contains(m.productId) && m.groupId != groupId,
            query => query.Include(m => m.group).Include(m => m.product));

        if (productsInOtherGroups.Any())
        {
            var conflictMessages = productsInOtherGroups
                .Select(m => $"Product \"{m.product.name}\" is already in group \"{m.group.name}\"")
                .ToList();
            throw ApiException.BadRequest(string.Join("; ", conflictMessages));
        }

        var addedCount = 0;
        foreach (var productId in request.productIds)
        {
            if (existingProductIds.Contains(productId))
                continue;

            var member = new ProductGroupMember();
            member.AddFill(new ProductGroupMember.AddForm
            {
                groupId = groupId,
                productId = productId,
                sortOrder = ++maxSortOrder
            });
            await memberRepo.AddAsync(member);
            addedCount++;
        }

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
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var groupRepo = _unitOfWork.Repository<ProductGroup>();

        var group = await groupRepo.FindAsync(
            g => g.Id == groupId && g.storeId == selectedStoreId.Value);

        if (group == null)
            throw ApiException.NotFound("Group not found", code: "group_not_found");

        var memberRepo = _unitOfWork.Repository<ProductGroupMember>();
        var member = await memberRepo.FindAsync(
            m => m.groupId == groupId && m.productId == productId);

        if (member == null)
            throw ApiException.NotFound("Product is not in the group");

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
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var groupRepo = _unitOfWork.Repository<ProductGroup>();

        var group = await groupRepo.FindAsync(
            g => g.Id == groupId && g.storeId == selectedStoreId.Value);

        if (group == null)
            throw ApiException.NotFound("Group not found", code: "group_not_found");

        var memberRepo = _unitOfWork.Repository<ProductGroupMember>();
        var member = await memberRepo.FindAsync(
            m => m.groupId == groupId && m.productId == productId);

        if (member == null)
            throw ApiException.NotFound("Product is not in the group");

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
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var groupRepo = _unitOfWork.Repository<ProductGroup>();

        var group = await groupRepo.FindAsync(
            g => g.Id == groupId && g.storeId == selectedStoreId.Value);

        if (group == null)
            throw ApiException.NotFound("Group not found", code: "group_not_found");

        var result = await _groupSyncService.ExportGroupAsync(groupId, connectionId);

        if (!result.IsSuccess)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Export failed", code: "export_failed");

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
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var result = await _groupSyncService.ImportGroupsAsync(connectionId, selectedStoreId.Value);

        if (!result.IsSuccess)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Import failed", code: "import_failed");

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
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var groupRepo = _unitOfWork.Repository<ProductGroup>();

        var group = await groupRepo.FindAsync(
            g => g.Id == groupId && g.storeId == selectedStoreId.Value,
            query => query
                .Include(g => g.marketplaceLinks)
                    .ThenInclude(l => l.connection));

        if (group == null)
            throw ApiException.NotFound("Group not found", code: "group_not_found");

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
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var groupRepo = _unitOfWork.Repository<ProductGroup>();

        var group = await groupRepo.FindAsync(
            g => g.Id == groupId && g.storeId == selectedStoreId.Value);

        if (group == null)
            throw ApiException.NotFound("Group not found", code: "group_not_found");

        var result = await _groupSyncService.DeleteMarketplaceGroupAsync(groupId, connectionId);

        if (!result.Success)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Delete failed");

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
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var groupRepo = _unitOfWork.Repository<ProductGroup>();

        var group = await groupRepo.FindAsync(
            g => g.Id == groupId && g.storeId == selectedStoreId.Value);

        if (group == null)
            throw ApiException.NotFound("Group not found", code: "group_not_found");

        var result = await _groupSyncService.AddProductsToMarketplaceGroupAsync(
            groupId, connectionId, request.productIds);

        if (!result.Success)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Failed to add products");

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
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var groupRepo = _unitOfWork.Repository<ProductGroup>();

        var group = await groupRepo.FindAsync(
            g => g.Id == groupId && g.storeId == selectedStoreId.Value);

        if (group == null)
            throw ApiException.NotFound("Group not found", code: "group_not_found");

        var result = await _groupSyncService.RemoveProductsFromMarketplaceGroupAsync(
            groupId, connectionId, request.productIds);

        if (!result.Success)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Failed to remove products");

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
