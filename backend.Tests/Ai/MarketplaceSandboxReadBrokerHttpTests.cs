using System.Net;
using System.Text;
using Delegat.Marketplace.SDK;

namespace Delegat.Tests.Ai;

public sealed class MarketplaceSandboxReadBrokerHttpTests
{
    [Theory]
    [InlineData("http://shop.example/api")]
    [InlineData("https://localhost/api")]
    [InlineData("https://store.local/api")]
    [InlineData("https://127.0.0.1/api")]
    [InlineData("https://shop.example:8443/api")]
    [InlineData("https://user:pass@shop.example/api")]
    [InlineData("https://shop.example/api#fragment")]
    public void PublicApiBase_RejectsUnsafeOrUnsupportedOrigins(string value)
    {
        Assert.False(MarketplaceSandboxReadBrokerHttp.TryCreatePublicApiBaseUri(value, out _));
    }

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("192.168.1.10", false)]
    [InlineData("100.64.0.2", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("198.18.0.1", false)]
    [InlineData("203.0.113.10", false)]
    [InlineData("::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("2001:20::1", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("2001:4860:4860::8888", true)]
    [InlineData("::ffff:192.168.1.10", false)]
    public void AddressPolicy_RejectsPrivateAndSpecialUseRanges(string value, bool expectedPublic)
    {
        Assert.Equal(expectedPublic, MarketplaceSandboxReadBrokerHttp.IsPublicAddress(IPAddress.Parse(value)));
    }

    [Fact]
    public void PathTarget_PreservesBasePathAndMergesQueries()
    {
        var baseUri = new Uri("https://store.example/wp-json/delegat/v1?tenant=42");

        var ok = MarketplaceSandboxReadBrokerHttp.TryCreatePathTargetUri(
            baseUri, "/products?page=2", out var target);

        Assert.True(ok);
        Assert.Equal(
            "https://store.example/wp-json/delegat/v1/products?tenant=42&page=2",
            target!.ToString());
    }

    [Theory]
    [InlineData("//attacker.example/path")]
    [InlineData("https://attacker.example/path")]
    [InlineData("/../admin")]
    [InlineData("/%2e%2e/admin")]
    [InlineData("/%252e%252e/admin")]
    [InlineData("/products%2f..%2fadmin")]
    [InlineData("/products#fragment")]
    public void PathTarget_RejectsTraversalAndOriginReplacement(string path)
    {
        var baseUri = new Uri("https://store.example/api/v1");

        Assert.False(MarketplaceSandboxReadBrokerHttp.TryCreatePathTargetUri(baseUri, path, out _));
    }

    [Fact]
    public void OpenCartTarget_MapsQueryRouteWithoutReplacingSavedRoute()
    {
        var baseUri = new Uri("https://store.example/index.php?route=extension/delegat/api");

        var ok = MarketplaceSandboxReadBrokerHttp.TryCreateOpenCartTargetUri(
            baseUri, "/categories/list?page=3", out var target);

        Assert.True(ok);
        Assert.Equal(
            "https://store.example/index.php?route=extension/delegat/api&action=categories.list&page=3",
            target!.ToString());
        Assert.False(MarketplaceSandboxReadBrokerHttp.TryCreateOpenCartTargetUri(
            baseUri, "/products?action=evil", out _));
        Assert.False(MarketplaceSandboxReadBrokerHttp.TryCreateOpenCartTargetUri(
            baseUri, "/products?%2561ction=evil", out _));
    }

    [Theory]
    [InlineData("/categories/12/fields", "https://store.example/index.php?route=extension/delegat/api&action=category.fields&category_id=12")]
    [InlineData("/orders/ord-42", "https://store.example/index.php?route=extension/delegat/api&action=order&order_id=ord-42")]
    [InlineData("/promotions/promo-7", "https://store.example/index.php?route=extension/delegat/api&action=promotion&promotion_id=promo-7")]
    [InlineData("/categories?parent_id=4&search=summer", "https://store.example/index.php?route=extension/delegat/api&action=categories&parent_id=4&search=summer")]
    public void OpenCartTarget_MatchesBridgeQueryRouteContract(string path, string expected)
    {
        var baseUri = new Uri("https://store.example/index.php?route=extension/delegat/api");

        Assert.True(MarketplaceSandboxReadBrokerHttp.TryCreateOpenCartTargetUri(baseUri, path, out var target));
        Assert.Equal(expected, target!.ToString());
    }

    [Theory]
    [InlineData("/categories/12/fields?category_id=13")]
    [InlineData("/orders/ord-42?order_id=ord-43")]
    [InlineData("/promotions/promo-7?promotion_id=promo-8")]
    public void OpenCartTarget_RejectsConflictingPathIdentifiers(string path)
    {
        var baseUri = new Uri("https://store.example/index.php?route=extension/delegat/api");

        Assert.False(MarketplaceSandboxReadBrokerHttp.TryCreateOpenCartTargetUri(baseUri, path, out _));
    }

    [Fact]
    public async Task GetAsync_UsesAdapterHeadersAndCapsTheResponse()
    {
        var baseUri = new Uri("https://store.example/wp-json/delegat/v1");
        Assert.True(MarketplaceSandboxReadBrokerHttp.TryCreatePathTargetUri(
            baseUri, "/products", out var target));
        using var handler = new CaptureHandler(new string('x', MarketplaceSandboxReadBrokerHttp.MaxResponseBytes + 50));

        var result = await MarketplaceSandboxReadBrokerHttp.GetAsync(
            baseUri,
            target!,
            new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer api-secret",
                ["X-Delegat-Api-Key"] = "api-secret",
            },
            handler,
            CancellationToken.None);

        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("https://store.example/wp-json/delegat/v1/products", handler.RequestUri!.ToString());
        Assert.Equal("Bearer api-secret", handler.Headers!["Authorization"]);
        Assert.Equal("api-secret", handler.Headers["X-Delegat-Api-Key"]);
        Assert.Equal(200, result.StatusCode);
        Assert.True(result.Truncated);
        Assert.Equal(MarketplaceSandboxReadBrokerHttp.MaxResponseBytes, Encoding.UTF8.GetByteCount(result.Body!));
    }

    [Fact]
    public async Task GetAsync_RejectsTargetOnAnotherOriginBeforeSending()
    {
        using var handler = new CaptureHandler("{}");
        var result = await MarketplaceSandboxReadBrokerHttp.GetAsync(
            new Uri("https://store.example/api"),
            new Uri("https://attacker.example/api/products"),
            new Dictionary<string, string>(),
            handler,
            CancellationToken.None);

        Assert.Equal("host_not_allowed", result.Error);
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task PostJsonAsync_SendsOnlyBoundedJsonToValidatedTarget()
    {
        var baseUri = new Uri("https://api-seller.ozon.ru/");
        Assert.True(MarketplaceSandboxReadBrokerHttp.TryCreatePathTargetUri(
            baseUri, "/v3/product/list", out var target));
        using var handler = new CaptureHandler("{\"items\":[]}");

        var result = await MarketplaceSandboxReadBrokerHttp.PostJsonAsync(
            baseUri,
            target!,
            new Dictionary<string, string>
            {
                ["Client-Id"] = "seller-client",
                ["Api-Key"] = "seller-key",
            },
            "{\"limit\":10}",
            handler,
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://api-seller.ozon.ru/v3/product/list", handler.RequestUri!.ToString());
        Assert.Equal("application/json", handler.ContentType);
        Assert.Equal("{\"limit\":10}", handler.RequestBody);
        Assert.Equal("seller-client", handler.Headers!["Client-Id"]);
        Assert.Equal("seller-key", handler.Headers["Api-Key"]);
        Assert.Equal("{\"items\":[]}", result.Body);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("not-json")]
    public async Task PostJsonAsync_RejectsNonObjectBodiesBeforeSending(string body)
    {
        var baseUri = new Uri("https://api-seller.ozon.ru/");
        Assert.True(MarketplaceSandboxReadBrokerHttp.TryCreatePathTargetUri(
            baseUri, "/v3/product/list", out var target));
        using var handler = new CaptureHandler("{}");

        var result = await MarketplaceSandboxReadBrokerHttp.PostJsonAsync(
            baseUri, target!, new Dictionary<string, string>(), body, handler, CancellationToken.None);

        Assert.Equal("invalid_arguments", result.Error);
        Assert.Null(handler.RequestUri);
    }

    private sealed class CaptureHandler(string body) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public Dictionary<string, string>? Headers { get; private set; }
        public string? ContentType { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            Headers = request.Headers.ToDictionary(
                header => header.Key,
                header => string.Join(",", header.Value),
                StringComparer.OrdinalIgnoreCase);
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            RequestBody = request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
