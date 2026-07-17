using Microsoft.AspNetCore.Authorization;

namespace Delegat.Infrastructure.Auth;

/// <summary>
/// Protects Public API v1 endpoints with the ApiKey authentication scheme.
/// </summary>
public class PublicApiAuthorizeAttribute : AuthorizeAttribute
{
    public const string Scheme = "ApiKey";

    public PublicApiAuthorizeAttribute()
    {
        AuthenticationSchemes = Scheme;
    }
}
