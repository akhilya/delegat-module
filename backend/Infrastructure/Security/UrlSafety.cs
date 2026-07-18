using System.Net;
using System.Net.Sockets;

namespace Delegat.Infrastructure.Security;

/// <summary>
/// Validates outbound URLs to mitigate SSRF (private IPs, link-local, cloud metadata, non-HTTP schemes).
/// </summary>
public static class UrlSafety
{
    public static bool TryValidateHttpUrl(string? url, out Uri uri, out string? error, bool allowPrivateHosts = false)
    {
        uri = null!;
        error = null;

        if (string.IsNullOrWhiteSpace(url))
        {
            error = "URL is empty";
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri!))
        {
            error = "Invalid URL";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            error = "Only http and https URLs are allowed";
            return false;
        }

        if (string.IsNullOrEmpty(uri.Host))
        {
            error = "URL host is required";
            return false;
        }

        if (!allowPrivateHosts && (uri.IsLoopback || IsLocalHostName(uri.Host)))
        {
            error = "Loopback and localhost URLs are not allowed";
            return false;
        }

        if (IPAddress.TryParse(uri.Host, out var ip))
        {
            if (!allowPrivateHosts && IsBlockedIp(ip))
            {
                error = "Private or reserved IP addresses are not allowed";
                return false;
            }
            return true;
        }

        if (allowPrivateHosts)
            return true;

        // Resolve DNS and reject if any address is private/reserved (mitigates DNS rebinding to some extent)
        try
        {
            var addresses = Dns.GetHostAddresses(uri.Host);
            if (addresses.Length == 0)
            {
                error = "Could not resolve host";
                return false;
            }

            if (addresses.Any(IsBlockedIp))
            {
                error = "Host resolves to a private or reserved IP address";
                return false;
            }
        }
        catch (SocketException)
        {
            error = "Could not resolve host";
            return false;
        }

        return true;
    }

    public static void EnsureSafeHttpUrl(string? url, bool allowPrivateHosts = false)
    {
        if (!TryValidateHttpUrl(url, out _, out var error, allowPrivateHosts))
            throw new ArgumentException(error ?? "Unsafe URL");
    }

    public static bool TryValidateProxyUrl(string? proxyUrl, out string? error, bool allowPrivateHosts = false)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(proxyUrl))
            return true;

        if (!Uri.TryCreate(proxyUrl.Trim(), UriKind.Absolute, out var uri))
        {
            error = "Invalid proxy URL";
            return false;
        }

        var scheme = uri.Scheme.ToLowerInvariant();
        if (scheme is not ("http" or "https" or "socks5"))
        {
            error = "Proxy scheme must be http, https, or socks5";
            return false;
        }

        if (string.IsNullOrEmpty(uri.Host))
        {
            error = "Proxy host is required";
            return false;
        }

        if (!allowPrivateHosts)
        {
            if (uri.IsLoopback || IsLocalHostName(uri.Host))
            {
                error = "Proxy host must be a public hostname";
                return false;
            }

            if (IPAddress.TryParse(uri.Host, out var ip) && IsBlockedIp(ip))
            {
                error = "Proxy must not target private or reserved IPs";
                return false;
            }
        }

        return true;
    }

    private static bool IsLocalHostName(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
        || host.Equals("metadata.google.internal", StringComparison.OrdinalIgnoreCase);

    private static bool IsBlockedIp(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
            return true;

        if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast)
            return true;

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // IPv4-mapped IPv6
            if (ip.IsIPv4MappedToIPv6)
                return IsBlockedIp(ip.MapToIPv4());

            var bytes = ip.GetAddressBytes();
            // Unique local fc00::/7
            if ((bytes[0] & 0xfe) == 0xfc)
                return true;
            return false;
        }

        var b = ip.GetAddressBytes();
        // 0.0.0.0/8
        if (b[0] == 0) return true;
        // 10.0.0.0/8
        if (b[0] == 10) return true;
        // 127.0.0.0/8
        if (b[0] == 127) return true;
        // 169.254.0.0/16 (link-local / cloud metadata)
        if (b[0] == 169 && b[1] == 254) return true;
        // 172.16.0.0/12
        if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
        // 192.168.0.0/16
        if (b[0] == 192 && b[1] == 168) return true;
        // 100.64.0.0/10 (CGNAT)
        if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return true;
        // 192.0.0.0/24, 192.0.2.0/24, 198.51.100.0/24, 203.0.113.0/24 (docs/special)
        if (b[0] == 192 && b[1] == 0 && b[2] <= 2) return true;
        if (b[0] == 198 && b[1] == 51 && b[2] == 100) return true;
        if (b[0] == 203 && b[1] == 0 && b[2] == 113) return true;
        // Multicast / reserved
        if (b[0] >= 224) return true;

        return false;
    }
}
