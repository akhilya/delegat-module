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
public class CatalogScrapeController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly CatalogScrapeService _catalogScrape;

    public CatalogScrapeController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        CatalogScrapeService catalogScrape)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _catalogScrape = catalogScrape;
    }

    /// <summary>
    /// Search the public storefront for a loaded adapter. Seller credentials
    /// are not required; a store connection is used only when one already exists.
    /// Query may be a URL, product id, several ids, or text.
    /// </summary>
    [HttpPost]
    [Route("/marketplace/catalog-scrape/{adapterKey}/search")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> SearchByAdapter(
        string adapterKey,
        [FromBody] CatalogScrapeSearchInput? input,
        CancellationToken ct)
    {
        var storeId = await RequireStoreIdAsync();
        var result = await _catalogScrape.SearchAsync(
            storeId,
            adapterKey,
            input?.query,
            input?.pageSize ?? CatalogScrapeService.DefaultPageSize,
            input?.cursor,
            ct);
        return Ok(result);
    }

    /// <summary>
    /// Copy selected public cards into the local catalog without marketplace links.
    /// A store connection is optional: without one, cards copy with no category mapping.
    /// </summary>
    [HttpPost]
    [Route("/marketplace/catalog-scrape/{adapterKey}/apply")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> ApplyByAdapter(
        string adapterKey,
        [FromBody] CatalogScrapeApplyInput input,
        CancellationToken ct)
    {
        if (input.publicIds == null || input.publicIds.Count == 0)
            throw ApiException.BadRequest(code: "public_product_ids_required");

        var storeId = await RequireStoreIdAsync();
        var userId = _currentUserService.GetUserId() ?? 0;
        var result = await _catalogScrape.ApplyAsync(
            storeId,
            adapterKey,
            input.publicIds,
            userId,
            ct,
            input.operationId);
        return Ok(result);
    }

    /// <summary>
    /// Search the public storefront for this connection's adapter.
    /// Query may be a URL, product id, several ids, or text.
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/catalog-scrape/search")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> Search(
        int connectionId,
        [FromBody] CatalogScrapeSearchInput? input,
        CancellationToken ct)
    {
        var connection = await GetConnectionAsync(connectionId);
        var result = await _catalogScrape.SearchAsync(
            connection,
            input?.query,
            input?.pageSize ?? CatalogScrapeService.DefaultPageSize,
            input?.cursor,
            ct);
        return Ok(result);
    }

    /// <summary>
    /// Copy selected public cards into the local catalog without marketplace links.
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/catalog-scrape/apply")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> Apply(
        int connectionId,
        [FromBody] CatalogScrapeApplyInput input,
        CancellationToken ct)
    {
        if (input.publicIds == null || input.publicIds.Count == 0)
            throw ApiException.BadRequest(code: "public_product_ids_required");

        var connection = await GetConnectionAsync(connectionId);
        var userId = _currentUserService.GetUserId() ?? 0;
        var result = await _catalogScrape.ApplyAsync(
            connection,
            input.publicIds,
            userId,
            ct,
            input.operationId);
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
        var storeId = await RequireStoreIdAsync();
        var connection = await _unitOfWork.Repository<MarketplaceConnection>().FindAsync(
            c => c.Id == connectionId && c.storeId == storeId);

        if (connection == null)
            throw ApiException.NotFound(code: "connection_not_found");

        return connection;
    }

    public class CatalogScrapeSearchInput
    {
        public string? query { get; set; }
        public int pageSize { get; set; } = CatalogScrapeService.DefaultPageSize;
        public string? cursor { get; set; }
    }

    public class CatalogScrapeApplyInput
    {
        public List<string> publicIds { get; set; } = [];
        public string? operationId { get; set; }
    }
}
