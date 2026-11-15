using System.Net;
using System.Net.Sockets;

namespace Delegat.Infrastructure.Security;

/// <summary>Raised when an outbound connection would reach a private or reserved address.</summary>
public sealed class UnsafeDestinationException(string message) : IOException(message);

/// <summary>
/// <see cref="SocketsHttpHandler.ConnectCallback"/> that resolves the host once, rejects private/reserved
/// addresses, and connects to the checked address, closing the DNS rebinding gap left by URL pre-validation.
/// </summary>
public static class SafeConnect
{
    public delegate Task<IPAddress[]> HostResolver(string host, CancellationToken ct);

    public static Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> CreateConnectCallback(
        HostResolver? resolver = null,
        Func<IPAddress, bool>? isBlocked = null)
    {
        resolver ??= Dns.GetHostAddressesAsync;
        isBlocked ??= UrlSafety.IsBlockedIp;

        return async (context, ct) =>
        {
            var endpoint = context.DnsEndPoint;
            var addresses = IPAddress.TryParse(endpoint.Host, out var literal)
                ? [literal]
                : await resolver(endpoint.Host, ct).ConfigureAwait(false);

            if (addresses.Length == 0)
                throw new UnsafeDestinationException($"Could not resolve host '{endpoint.Host}'");
            if (addresses.Any(isBlocked))
                throw new UnsafeDestinationException($"Host '{endpoint.Host}' resolves to a private or reserved IP address");

            Exception? last = null;
            foreach (var address in addresses)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), ct).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception ex) when (ex is SocketException or OperationCanceledException)
                {
                    socket.Dispose();
                    if (ex is OperationCanceledException)
                        throw;
                    last = ex;
                }
            }

            throw last!;
        };
    }
}
