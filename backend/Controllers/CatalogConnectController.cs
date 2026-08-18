using Microsoft.AspNetCore.Mvc;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Models.Marketplace;
using Delegat.Services;
using Delegat.Services.Marketplace;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
public class CatalogConnectController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly CatalogMatchService _catalogMatch;

    public CatalogConnectController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        CatalogMatchService catalogMatch)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _catalogMatch = catalogMatch;
    }

    /// <summary>
    /// Queue every unlinked remote product for import. Unique marketplace
    /// categories are queued first so products wait for a mapping.
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/products/import-unlinked")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> ImportUnlinked(int connectionId, CancellationToken ct)
    {
        var connection = await GetConnectionAsync(connectionId);
        var userId = _currentUserService.GetUserId() ?? 0;
        var result = await _catalogMatch.QueueUnlinkedImportAsync(connection, userId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Preview barcode/SKU matches for unlinked remote products against the local catalog.
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/match/preview")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> Preview(
        int connectionId,
        [FromBody] CatalogMatchPreviewInput? input,
        CancellationToken ct)
    {
        var connection = await GetConnectionAsync(connectionId);
        var result = await _catalogMatch.PreviewAsync(
            connection,
            input?.page ?? 1,
            input?.pageSize ?? 50,
            input?.status,
            ct);
        return Ok(result);
    }

    /// <summary>
    /// Bulk-link reviewed matches (load cards by id, copy mapped fields).
    /// Unmatched rows stay unlinked unless importUnmatched is set.
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/match/apply")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> Apply(
        int connectionId,
        [FromBody] CatalogMatchApplyInput input,
        CancellationToken ct)
    {
        if (input.items == null || input.items.Count == 0)
            throw ApiException.BadRequest("No match rows to apply", code: "required_fields_missing");

        var connection = await GetConnectionAsync(connectionId);
        var userId = _currentUserService.GetUserId() ?? 0;
        var result = await _catalogMatch.ApplyAsync(
            connection,
            input.items,
            input.importUnmatched,
            userId,
            ct,
            input.operationId);
        return Ok(result);
    }

    /// <summary>
    /// Create category mappings for existing links that were saved without one.
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/match/repair-mappings")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> RepairMappings(int connectionId, CancellationToken ct)
    {
        var connection = await GetConnectionAsync(connectionId);
        var userId = _currentUserService.GetUserId() ?? 0;
        var result = await _catalogMatch.RepairMissingMappingsAsync(connection, userId, ct);
        return Ok(result);
    }

    private async Task<MarketplaceConnection> GetConnectionAsync(int connectionId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var connection = await _unitOfWork.Repository<MarketplaceConnection>().FindAsync(
            c => c.Id == connectionId && c.storeId == selectedStoreId.Value);

        if (connection == null)
            throw ApiException.NotFound("Connection not found", code: "connection_not_found");

        return connection;
    }

    public class CatalogMatchPreviewInput
    {
        public int page { get; set; } = 1;
        public int pageSize { get; set; } = 50;
        public string? status { get; set; }
    }

    public class CatalogMatchApplyInput
    {
        public List<CatalogMatchApplyItem> items { get; set; } = [];
        public bool importUnmatched { get; set; }
        public string? operationId { get; set; }
    }
}
