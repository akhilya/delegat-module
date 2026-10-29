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
    private readonly CatalogConnectJobService _jobs;
    private readonly CatalogMatchService _catalogMatch;

    public CatalogConnectController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        CatalogConnectJobService jobs,
        CatalogMatchService catalogMatch)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _jobs = jobs;
        _catalogMatch = catalogMatch;
    }

    /// <summary>
    /// Active Catalog Connect job for the selected store, plus the last completed match preview.
    /// </summary>
    [HttpGet]
    [Route("/marketplace/catalog-connect/jobs")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> Jobs(CancellationToken ct)
    {
        var storeId = await RequireStoreIdAsync();
        var state = await _jobs.GetStateAsync(storeId, ct);
        return Ok(state);
    }

    /// <summary>
    /// Queue a background scan that pages the remote catalog and enqueues unlinked products.
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/products/import-unlinked")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> ImportUnlinked(
        int connectionId,
        [FromBody] CatalogConnectJobStartInput? input,
        CancellationToken ct)
    {
        var connection = await GetConnectionAsync(connectionId);
        var userId = _currentUserService.GetUserId() ?? 0;
        var job = await _jobs.EnqueueImportAsync(connection, userId, input?.operationId, ct);
        return Ok(job);
    }

    /// <summary>
    /// Queue a background barcode/SKU match preview. Results stay until replaced.
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
        var userId = _currentUserService.GetUserId() ?? 0;
        var job = await _jobs.EnqueueMatchAsync(connection, userId, input?.operationId, ct);
        return Ok(job);
    }

    /// <summary>
    /// Bulk-link reviewed matches in the background.
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
            throw ApiException.BadRequest(code: "match_rows_required");

        var connection = await GetConnectionAsync(connectionId);
        var userId = _currentUserService.GetUserId() ?? 0;
        var job = await _jobs.EnqueueApplyAsync(
            connection,
            userId,
            input.items,
            input.importUnmatched,
            input.operationId,
            ct);
        return Ok(job);
    }

    /// <summary>
    /// Create category mappings for existing links that were saved without one,
    /// and copy marketplace fields into local attributes.
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

    private async Task<int> RequireStoreIdAsync()
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");
        return selectedStoreId.Value;
    }

    private async Task<MarketplaceConnection> GetConnectionAsync(int connectionId)
    {
        var selectedStoreId = await RequireStoreIdAsync();
        var connection = await _unitOfWork.Repository<MarketplaceConnection>().FindAsync(
            c => c.Id == connectionId && c.storeId == selectedStoreId);

        if (connection == null)
            throw ApiException.NotFound(code: "connection_not_found");

        CatalogMatchService.EnsureConnectionActive(connection);
        return connection;
    }

    public class CatalogConnectJobStartInput
    {
        public string? operationId { get; set; }
    }

    public class CatalogMatchPreviewInput
    {
        public int page { get; set; } = 1;
        public int pageSize { get; set; } = 50;
        public string? status { get; set; }
        public string? operationId { get; set; }
    }

    public class CatalogMatchApplyInput
    {
        public List<CatalogMatchApplyItem> items { get; set; } = [];
        public bool importUnmatched { get; set; }
        public string? operationId { get; set; }
    }
}
