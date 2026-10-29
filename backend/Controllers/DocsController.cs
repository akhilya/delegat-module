using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Services.Docs;
using Microsoft.AspNetCore.Mvc;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
[Route("docs")]
public class DocsController : ControllerBase
{
    private readonly IPublicDocsClient _docs;
    private readonly ICurrentUserService _currentUser;

    public DocsController(IPublicDocsClient docs, ICurrentUserService currentUser)
    {
        _docs = docs;
        _currentUser = currentUser;
    }

    [HttpGet("catalog")]
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> Catalog([FromQuery] string? locale, CancellationToken ct)
    {
        await RequireStoreAsync(ct);
        try
        {
            return Ok(await _docs.GetCatalogAsync(locale ?? "ru", ct));
        }
        catch (DocsClientException ex)
        {
            throw Map(ex);
        }
    }

    [HttpGet("page")]
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> Page(
        [FromQuery] string? slug,
        [FromQuery] string? locale,
        CancellationToken ct)
    {
        await RequireStoreAsync(ct);
        try
        {
            return Ok(await _docs.GetPageAsync(slug ?? "", locale ?? "ru", ct));
        }
        catch (DocsClientException ex)
        {
            throw Map(ex);
        }
    }

    private async Task RequireStoreAsync(CancellationToken ct)
    {
        var storeId = await _currentUser.GetSelectedStoreIdAsync(ct);
        if (storeId == null)
            throw ApiException.BadRequest(code: "store_not_selected");
    }

    private static ApiException Map(DocsClientException ex) =>
        new(ex.Message, ex.StatusCode, code: ex.Code);
}
