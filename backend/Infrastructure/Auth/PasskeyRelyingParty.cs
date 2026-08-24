using Microsoft.AspNetCore.Http;

namespace Delegat.Infrastructure.Auth;

/// <summary>
/// WebAuthn rp.id is the browser host (no parent-domain sharing). Origin may include a port; rp.id never does.
/// Prefer the Origin header so Vite's API proxy still verifies against the page origin.
/// </summary>
public static class PasskeyRelyingParty
{
    public static string Origin(HttpRequest request)
    {
        if (TryBrowserOrigin(request, out var origin))
            return origin;
        return $"{request.Scheme}://{request.Host.Value}";
    }

    public static string Domain(HttpRequest request)
    {
        if (TryBrowserOrigin(request, out var origin) && Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            return uri.Host;
        return request.Host.Host;
    }

    private static bool TryBrowserOrigin(HttpRequest request, out string origin)
    {
        origin = "";
        var header = request.Headers.Origin.ToString();
        if (!Uri.TryCreate(header, UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;
        if (string.IsNullOrEmpty(uri.Host))
            return false;
        origin = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }
}
