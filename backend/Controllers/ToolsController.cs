using Microsoft.AspNetCore.Mvc;
using Delegat.Infrastructure.Auth;
using Delegat.Marketplace.SDK;
using Delegat.Services.Tools;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
public class ToolsController : ControllerBase
{
    private readonly ICurrentUserService _currentUser;
    private readonly ToolCatalogService _catalog;

    public ToolsController(ICurrentUserService currentUser, ToolCatalogService catalog)
    {
        _currentUser = currentUser;
        _catalog = catalog;
    }

    /// <summary>
    /// Merged Tools catalog: platform pages plus job tools from loaded adapters.
    /// </summary>
    [HttpGet]
    [Route("/tools")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var storeId = await _currentUser.GetSelectedStoreIdAsync(ct);
        var entries = await _catalog.ListAsync(UserPermissions(), storeId, ct);
        return Ok(entries.Select(ToDto));
    }

    [HttpGet]
    [Route("/tools/{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var storeId = await _currentUser.GetSelectedStoreIdAsync(ct);
        var entry = await _catalog.GetAsync(id, UserPermissions(), storeId, ct);
        return Ok(ToDto(entry));
    }

    [HttpPost]
    [Route("/tools/{id}/run")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> Run(
        string id,
        [FromBody] ToolRunInput? input,
        CancellationToken ct)
    {
        var storeId = await _currentUser.GetSelectedStoreIdAsync(ct);
        var userId = _currentUser.GetUserId() ?? 0;
        var result = await _catalog.RunJobAsync(
            id,
            input?.connectionId,
            input?.parameters ?? new Dictionary<string, string?>(),
            UserPermissions(),
            userId,
            storeId,
            ct);
        return Ok(new
        {
            operationId = result.OperationId,
            success = result.Success,
            message = result.Message,
            table = result.Table,
        });
    }

    private HashSet<string> UserPermissions()
    {
        return User.FindAll(Permissions.ClaimType)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static object ToDto(ToolCatalogEntry entry)
    {
        return new
        {
            id = entry.Id,
            source = entry.Source,
            adapterKey = entry.AdapterKey,
            mode = entry.Mode,
            path = entry.Path,
            title = entry.Title,
            description = entry.Description,
            titleKey = entry.TitleKey,
            descriptionKey = entry.DescriptionKey,
            icon = entry.Icon,
            permissions = entry.Permissions,
            available = entry.Available,
            unavailableReason = entry.UnavailableReason,
            requiresConnection = entry.RequiresConnection,
            paramsSchema = entry.Params.Select(MapParam),
            resultKind = entry.ResultKind,
        };
    }

    private static object MapParam(CredentialField field)
    {
        return new
        {
            key = field.Key,
            displayName = field.DisplayName,
            description = field.Description,
            type = field.Type,
            isRequired = field.IsRequired,
            isSecret = field.IsSecret,
            defaultValue = field.DefaultValue,
            placeholder = field.Placeholder,
            options = field.Options?.Select(o => new { value = o.Value, label = o.Label }),
        };
    }
}

public class ToolRunInput
{
    public int? connectionId { get; set; }
    public Dictionary<string, string?> parameters { get; set; } = [];
}
