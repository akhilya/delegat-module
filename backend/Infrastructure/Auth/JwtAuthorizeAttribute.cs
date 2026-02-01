using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace PrintO.Infrastructure.Auth;

/// <summary>
/// JWT Authorization attribute for protecting endpoints
/// Replaces Zorro.Modules.JwtBearer.Attributes.JwtAuthorize
/// </summary>
public class JwtAuthorizeAttribute : AuthorizeAttribute
{
    public JwtAuthorizeAttribute()
    {
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme;
    }
}
