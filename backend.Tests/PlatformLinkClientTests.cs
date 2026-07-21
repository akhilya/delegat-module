using System.Net;
using System.Text;
using Delegat.Infrastructure.Configuration;
using Delegat.Services.PlatformLink;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class PlatformLinkClientTests
{
    [Fact]
    public async Task GetCurrencies_WhenDisabled_ReturnsEmpty()
    {
        var opts = new PlatformLinkOptions();
        var client = CreateClient(opts, _ => new HttpResponseMessage(HttpStatusCode.OK));
        var items = await client.GetCurrenciesAsync();
        Assert.Empty(items);
    }

    [Fact]
    public async Task GetCurrencies_SendsHmacHeaders_AndParsesArray()
    {
        HttpRequestMessage? seen = null;
        var opts = new PlatformLinkOptions
        {
            ControlPlaneUrl = "https://cp.test",
            InstanceId = "inst_1",
            InstanceLinkSecret = "sekrit",
        };

        var handler = new StubHandler(req =>
        {
            seen = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"code":"USD","label":"US Dollar","symbol":"$"}]""",
                    Encoding.UTF8,
                    "application/json"),
            };
        });

        var client = CreateClient(opts, handler);
        var items = await client.GetCurrenciesAsync();

        Assert.Single(items);
        Assert.Equal("USD", items[0].Code);
        Assert.NotNull(seen);
        Assert.True(seen!.Headers.Contains(PlatformLinkHmac.HeaderInstanceId));
        Assert.True(seen.Headers.Contains(PlatformLinkHmac.HeaderTimestamp));
        Assert.True(seen.Headers.Contains(PlatformLinkHmac.HeaderSignature));
        Assert.Equal("inst_1", seen.Headers.GetValues(PlatformLinkHmac.HeaderInstanceId).Single());
    }

    [Fact]
    public async Task GetCurrencies_ParsesItemsWrapper()
    {
        var opts = new PlatformLinkOptions
        {
            ControlPlaneUrl = "https://cp.test",
            InstanceId = "inst_1",
            InstanceLinkSecret = "sekrit",
        };
        var client = CreateClient(opts, _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"items":[{"code":"EUR","label":"Euro"}]}""",
                Encoding.UTF8,
                "application/json"),
        });

        var items = await client.GetCurrenciesAsync();
        Assert.Equal("EUR", Assert.Single(items).Code);
    }

    private static PlatformLinkClient CreateClient(PlatformLinkOptions opts, Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        CreateClient(opts, new StubHandler(respond));

    private static PlatformLinkClient CreateClient(PlatformLinkOptions opts, HttpMessageHandler handler)
    {
        var http = new HttpClient(handler)
        {
            BaseAddress = string.IsNullOrWhiteSpace(opts.ControlPlaneUrl)
                ? null
                : new Uri(opts.ControlPlaneUrl.TrimEnd('/') + "/"),
        };
        return new PlatformLinkClient(http, opts, NullLogger<PlatformLinkClient>.Instance);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond(request));
    }
}
