using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Delegat.Marketplace.SDK;
using Delegat.Marketplace.Wildberries;

namespace Delegat.Tests;

public class WbPublicCatalogTests
{
    [Fact]
    public void Capabilities_IncludeSearchPublicCatalog()
    {
        var plugin = new WildberriesPlugin();
        Assert.True(plugin.Capabilities.HasFlag(MarketplaceCapabilities.SearchPublicCatalog));
    }

    [Fact]
    public async Task Search_MapsHitToExternalProduct()
    {
        var json = """
            {"products":[{"id":12345678,"name":"Tee","brand":"Acme","supplier":"Seller LLC","salePriceU":149900,"priceU":199900,"pics":2,"subjectId":42,"entity":"Футболки","sizes":[{"optionId":99,"name":"M","origName":"M","skus":["2000000000001"]}]}],"total":1}
            """;
        var http = Scripted("search.wb.ru", json);
        var client = new WbPublicCatalogClient(http);

        var result = await client.SearchAsync("nike", 50, null, CancellationToken.None);

        var product = Assert.Single(result.Items);
        Assert.Equal("12345678", product.ExternalId);
        Assert.Equal("Tee", product.Name);
        Assert.Equal("Acme", product.Brand);
        Assert.Equal(1499.00m, product.Price);
        Assert.Equal("42", product.CategoryId);
        Assert.Equal("Футболки", product.CategoryName);
        Assert.Equal("2000000000001", product.Barcode);
        Assert.Equal("Seller LLC", product.RawData?["supplier"]?.ToString());
        Assert.NotNull(product.ImageUrls);
        Assert.Contains(product.ImageUrls!, u =>
            u.Contains("/vol123/part12345/12345678/images/big/1.webp", StringComparison.Ordinal)
            && u.Contains("wbbasket.ru", StringComparison.Ordinal));
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task GetProduct_UsesNmId()
    {
        var json = """
            {"products":[{"id":555,"name":"Card","brand":"B","salePriceU":10000,"pics":1,"subjectId":7,"entity":"Hats"}]}
            """;
        var http = Scripted("card.wb.ru", json);
        var client = new WbPublicCatalogClient(http);

        var product = await client.GetProductAsync("555", CancellationToken.None);

        Assert.NotNull(product);
        Assert.Equal("555", product!.ExternalId);
        Assert.Equal("Card", product.Name);
        Assert.Equal("555", product.Sku);
        Assert.Equal(100.00m, product.Price);
    }

    [Theory]
    [InlineData(14671602L, 2)]
    [InlineData(882600000L, 39)]
    [InlineData(1097975275L, 42)]
    [InlineData(1500000123L, 48)]
    public void BasketNumber_MatchesLiveCdnVolRanges(long nmId, int basket)
    {
        Assert.Equal(basket, WbPublicCatalogClient.BasketNumber(nmId));
    }

    [Fact]
    public void ImageUrls_UsesLiveBasketHost()
    {
        var url = Assert.Single(WbPublicCatalogClient.ImageUrls(1097975275L, 1));
        Assert.Equal(
            "https://basket-42.wbbasket.ru/vol10979/part1097975/1097975275/images/big/1.webp",
            url);
    }

    [Fact]
    public void CardsDetailUrl_JoinsNmIds()
    {
        var url = WbPublicCatalogClient.CardsDetailUrl([555, 777]);
        Assert.Contains("card.wb.ru/cards/v4/detail", url, StringComparison.Ordinal);
        Assert.Contains("nm=555;777", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetProducts_MapsEachNmId()
    {
        var json = """
            {"products":[{"id":555,"name":"Card A","salePriceU":10000,"pics":0},{"id":777,"name":"Card B","salePriceU":20000,"pics":0}]}
            """;
        var http = Scripted("card.wb.ru", json);
        var client = new WbPublicCatalogClient(http);

        var products = await client.GetProductsAsync([555, 777], CancellationToken.None);

        Assert.Equal(2, products.Count);
        Assert.Equal("Card A", products.First(p => p.ExternalId == "555").Name);
        Assert.Equal("Card B", products.First(p => p.ExternalId == "777").Name);
    }

    [Fact]
    public void MapProduct_ReadsNestedSizePriceAndNamedOptions()
    {
        using var doc = JsonDocument.Parse(
            """
            {"id":1,"name":"Tee","brand":"Acme","options":[{"name":"Color","value":"Grey"}],"sizes":[{"price":{"product":149900}}]}
            """);
        var product = WbPublicCatalogClient.MapProduct(doc.RootElement);

        Assert.NotNull(product);
        Assert.Equal(1499.00m, product!.Price);
        Assert.Equal("Acme", product.Brand);
        Assert.Equal("Grey", product.Attributes!["Color"]);
    }

    [Fact]
    public void Parse_HostUrl_YieldsNmIdForCardFetch()
    {
        var parsed = Delegat.Services.Marketplace.PublicCatalogQuery.Parse(
            "https://www.wildberries.ru/catalog/555/detail.aspx");
        Assert.Equal(["555"], parsed.ProductIds);
        Assert.Equal("555", WbPublicCatalogClient.PublicNmId(parsed.ProductIds[0]));
    }

    [Fact]
    public async Task Search_RetriesOnceOn429_ThenSucceeds()
    {
        var json = """{"products":[{"id":1,"name":"Ok","salePriceU":100,"pics":0}]}""";
        var handler = new SequenceHandler(
            (HttpStatusCode.TooManyRequests, null),
            (HttpStatusCode.OK, json));
        var client = new WbPublicCatalogClient(new HttpClient(handler));

        var result = await client.SearchAsync("nike", 50, null, CancellationToken.None);

        Assert.Equal("1", Assert.Single(result.Items).ExternalId);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Search_Exhausted429_ThrowsWildberriesApiException()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.TooManyRequests, null),
            (HttpStatusCode.TooManyRequests, null));
        var client = new WbPublicCatalogClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<WildberriesApiException>(() =>
            client.SearchAsync("nike", 50, null, CancellationToken.None));

        Assert.Equal(429, ex.StatusCode);
        Assert.DoesNotContain("does not indicate success", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient Scripted(string hostContains, string json)
    {
        return new HttpClient(new ScriptedHandler(hostContains, json), disposeHandler: true);
    }

    private sealed class ScriptedHandler(string hostContains, string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            Assert.Null(request.Headers.Authorization);
            if (!url.Contains(hostContains, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                });
            }
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class SequenceHandler(params (HttpStatusCode Status, string? Json)[] steps)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var step = steps[Math.Min(Calls, steps.Length - 1)];
            Calls++;
            var response = new HttpResponseMessage(step.Status)
            {
                Content = new StringContent(step.Json ?? "{}", Encoding.UTF8, "application/json")
            };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                TimeSpan.FromSeconds(1));
            return Task.FromResult(response);
        }
    }
}
