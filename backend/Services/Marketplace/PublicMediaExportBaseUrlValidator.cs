using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Net.Http;

namespace Delegat.Services.Marketplace;

public interface IPublicMediaExportBaseUrlValidator
{
    Task ValidateAsync(Uri baseUri, CancellationToken ct = default);
}

/// <summary>Rejects local, private, and unresolvable origins for permanent marketplace media URLs.</summary>
public sealed class PublicMediaExportBaseUrlValidator : IPublicMediaExportBaseUrlValidator
{
    private static readonly HttpClient ProbeClient = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        ConnectTimeout = TimeSpan.FromSeconds(5)
    })
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    private static readonly ConcurrentDictionary<string, DateTimeOffset> ValidatedOrigins = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan ValidationCacheDuration = TimeSpan.FromMinutes(2);

    public async Task ValidateAsync(Uri baseUri, CancellationToken ct = default)
    {
        var origin = baseUri.GetLeftPart(UriPartial.Authority);
        if (ValidatedOrigins.TryGetValue(origin, out var validatedAt)
            && DateTimeOffset.UtcNow - validatedAt < ValidationCacheDuration)
        {
            return;
        }

        var host = baseUri.IdnHost.TrimEnd('.');
        if (IsLocalHostname(host))
            throw new InvalidOperationException("The configured public media URL uses a local-only hostname.");

        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(host, ct);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            throw new InvalidOperationException("The configured public media URL hostname could not be resolved.", ex);
        }

        if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
            throw new InvalidOperationException("The configured public media URL must resolve only to public internet addresses.");

        var probeUrl = new Uri(baseUri, "/api/pub/media-export/" + new string('0', 64));
        using var request = new HttpRequestMessage(HttpMethod.Head, probeUrl);
        using var response = await ProbeClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                "The configured public media URL did not reach this instance's unauthenticated media endpoint over HTTPS.");
        }

        ValidatedOrigins[origin] = DateTimeOffset.UtcNow;
    }

    private static bool IsLocalHostname(string host)
        => string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".test", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "host.docker.internal", StringComparison.OrdinalIgnoreCase);

    private static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
            || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || address.IsIPv6Teredo)
            return false;

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var a = bytes[0];
            var b = bytes[1];
            return a != 0 && a < 224
                && a != 10
                && !(a == 100 && b is >= 64 and <= 127)
                && !(a == 169 && b == 254)
                && !(a == 172 && b is >= 16 and <= 31)
                && !(a == 192 && b == 0)
                && !(a == 192 && b == 168)
                && !(a == 198 && b is 18 or 19)
                && !(a == 198 && b == 51 && bytes[2] == 100)
                && !(a == 203 && b == 0 && bytes[2] == 113);
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return false;

        // Unique-local (fc00::/7), unspecified, and documentation (2001:db8::/32).
        return (bytes[0] & 0xe0) == 0x20
            && (bytes[0] & 0xfe) != 0xfc
            && !(bytes.All(b => b == 0))
            && !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8);
    }
}
