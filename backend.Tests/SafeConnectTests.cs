using System.Net;
using System.Net.Sockets;
using System.Text;
using Delegat.Infrastructure.Security;

namespace Delegat.Tests;

public sealed class SafeConnectTests
{
    [Fact]
    public async Task RejectsRebindToLoopbackAfterPublicAnswer()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var answers = new Queue<IPAddress[]>([
            [IPAddress.Parse("93.184.216.34")],
            [IPAddress.Loopback]
        ]);
        SafeConnect.HostResolver resolver = (_, _) => Task.FromResult(answers.Dequeue());

        // The first answer is what a pre-validation lookup would have seen.
        var validated = await resolver("rebind.test", CancellationToken.None);
        Assert.DoesNotContain(validated, UrlSafety.IsBlockedIp);

        using var client = CreateClient(SafeConnect.CreateConnectCallback(resolver));
        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync($"http://rebind.test:{port}/latest/meta-data/"));

        Assert.IsType<UnsafeDestinationException>(ex.InnerException);
        Assert.False(listener.Pending());
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("10.0.0.5")]
    public async Task RejectsLiteralPrivateAddressWithoutResolving(string host)
    {
        var resolved = false;
        using var client = CreateClient(SafeConnect.CreateConnectCallback((_, _) =>
        {
            resolved = true;
            return Task.FromResult(Array.Empty<IPAddress>());
        }));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync($"http://{host}:1/"));

        Assert.IsType<UnsafeDestinationException>(ex.InnerException);
        Assert.False(resolved);
    }

    [Fact]
    public async Task RejectsWhenAnyResolvedAddressIsBlocked()
    {
        using var client = CreateClient(SafeConnect.CreateConnectCallback((_, _) =>
            Task.FromResult(new[] { IPAddress.Parse("93.184.216.34"), IPAddress.Parse("192.168.1.10") })));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("http://mixed.test/"));

        Assert.IsType<UnsafeDestinationException>(ex.InnerException);
    }

    [Fact]
    public async Task ConnectsToTheCheckedAddress()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serve = ServeOnceAsync(listener);

        var checkedAddresses = new List<IPAddress>();
        using var client = CreateClient(SafeConnect.CreateConnectCallback(
            (_, _) => Task.FromResult(new[] { IPAddress.Loopback }),
            ip =>
            {
                checkedAddresses.Add(ip);
                return false;
            }));

        var body = await client.GetStringAsync($"http://pinned.test:{port}/");
        await serve;

        Assert.Equal("ok", body);
        Assert.Equal([IPAddress.Loopback], checkedAddresses);
    }

    private static HttpClient CreateClient(
        Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> connect)
        => new(new SocketsHttpHandler { UseProxy = false, ConnectCallback = connect })
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

    private static async Task ServeOnceAsync(TcpListener listener)
    {
        using var socket = await listener.AcceptTcpClientAsync();
        await using var stream = socket.GetStream();
        var buffer = new byte[4096];
        var read = 0;
        while (!Encoding.ASCII.GetString(buffer, 0, read).Contains("\r\n\r\n"))
            read += await stream.ReadAsync(buffer.AsMemory(read));
        var response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok");
        await stream.WriteAsync(response);
    }
}
