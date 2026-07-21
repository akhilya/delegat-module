using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace Delegat.Infrastructure.Auth;

/// <summary>
/// Requires a permission claim. Optionally requires membership in the target store.
/// </summary>
public sealed class RequiresPermissionAttribute : AuthorizeAttribute
{
    public RequiresPermissionAttribute(string permission, bool requireStoreMembership = false)
    {
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme;
        Policy = requireStoreMembership
            ? PermissionPolicyProvider.StorePolicyName(permission)
            : PermissionPolicyProvider.PolicyName(permission);
    }
}
