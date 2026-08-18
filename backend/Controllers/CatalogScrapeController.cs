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
            throw ApiException.BadRequest("No public product ids to import", code: "required_fields_missing");

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
