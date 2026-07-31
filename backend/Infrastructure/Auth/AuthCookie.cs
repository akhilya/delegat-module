namespace Delegat.Infrastructure.Auth;

/// <summary>
/// Auth cookie flags. Do not tie Secure to ASPNETCORE_ENVIRONMENT=Production:
/// lab instances are Production behind plain HTTP (e.g. http://{label}.lvh.me),
/// and browsers refuse to store Secure cookies on HTTP — signIn then succeeds
/// but /auth/refresh returns "Refresh token is missing".
/// </summary>
public static class AuthCookie
{
    /// <summary>
    /// Prefer <c>COOKIE_SECURE</c> env (<c>true</c>/<c>false</c>); otherwise use HTTPS
    /// as seen by the app (respects <c>X-Forwarded-Proto</c> when ForwardedHeaders is enabled).
    /// </summary>
    public static bool UseSecure(HttpRequest request)
    {
        var env = Environment.GetEnvironmentVariable("COOKIE_SECURE");
        if (!string.IsNullOrWhiteSpace(env))
            return env.Equals("true", StringComparison.OrdinalIgnoreCase);

        return request.IsHttps;
    }

    public static CookieOptions AccessOrRefresh(HttpRequest request, bool rememberMe) =>
        new()
        {
            HttpOnly = true,
            Secure = UseSecure(request),
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = rememberMe ? DateTime.UtcNow.AddDays(30) : DateTime.UtcNow.AddDays(7),
        };

    public static CookieOptions SelectedStore(HttpRequest request) =>
        new()
        {
            HttpOnly = true,
            Secure = UseSecure(request),
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = DateTime.UtcNow.AddDays(30),
        };
}
