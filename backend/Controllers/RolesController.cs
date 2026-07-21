using Microsoft.AspNetCore.Mvc;
using Delegat.Infrastructure.Auth;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
[Route("/roles")]
public class RolesController : ControllerBase
{
    [HttpGet]
    [RequiresPermission(Permissions.RolesManage)]
    public IActionResult GetRoles()
    {
        var roles = AppRoles.All.Select(name => new
        {
            name,
            permissions = AppRoles.RolePermissions.TryGetValue(name, out var perms)
                ? perms
                : Array.Empty<string>(),
        });
        return Ok(roles);
    }

    [HttpGet("/permissions")]
    [RequiresPermission(Permissions.RolesManage)]
    public IActionResult GetPermissions()
    {
        return Ok(new
        {
            all = Permissions.All,
            assignable = Permissions.Assignable,
            adminOnly = Permissions.AdminOnly,
            platform = Permissions.Platform,
            store = Permissions.StoreScoped,
        });
    }
}
