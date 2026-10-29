using System.Text;
using System.Text.Json;
using Delegat.Marketplace.Ozon;
using Delegat.Marketplace.SDK;
using Delegat.Marketplace.Wildberries;
using Delegat.Marketplace.YandexMarket;

namespace Delegat.Tests.Ai;

public sealed class MarketplaceReadOperationPluginTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"limit\":50,\"updated_at\":\"2025-01-01T00:00:00Z\",\"nm_id\":12345,\"text_search\":\"coat\",\"object_ids\":[123],\"brands\":[\"North\"],\"imt_id\":987,\"with_photo\":1,\"allowed_categories_only\":true,\"ascending\":true}")]
    public void Wildberries_BuildsOnlyBoundedCardListBodies(string argumentsJson)
    {
        using var arguments = JsonDocument.Parse(argumentsJson);

        Assert.True(WildberriesPlugin.TryBuildBrokerReadRequest(arguments.RootElement, out var body));
        Assert.True(Encoding.UTF8.GetByteCount(body) <= 16_384);
        using var request = JsonDocument.Parse(body);
        var expectedLimit = arguments.RootElement.TryGetProperty("limit", out var limit)
            ? limit.GetInt32()
            : 100;
        Assert.Equal(expectedLimit,
            request.RootElement.GetProperty("settings").GetProperty("cursor").GetProperty("limit").GetInt32());
    }

    [Theory]
    [InlineData("{\"limit\":101}")]
    [InlineData("{\"path\":\"/content/v2/cards/delete/trash\"}")]
    [InlineData("{\"with_photo\":2}")]
    [InlineData("{\"object_ids\":[-1]}")]
    [InlineData("{\"limit\":10,\"limit\":20}")]
    public void Wildberries_RejectsInvalidOrUnrecognizedCardListArguments(string argumentsJson)
    {
        using var arguments = JsonDocument.Parse(argumentsJson);

        Assert.False(WildberriesPlugin.TryBuildBrokerReadRequest(arguments.RootElement, out _));
    }

    [Fact]
    public async Task Wildberries_ReadOperation_UsesTokenEnvironmentFixedRouteAndAuth()
    {
        Uri? origin = null;
        Uri? target = null;
        IReadOnlyDictionary<string, string>? headers = null;
        string? body = null;
        var plugin = new WildberriesPlugin((baseUri, targetUri, requestHeaders, json, _) =>
        {
            origin = baseUri;
            target = targetUri;
            headers = requestHeaders;
            body = json;
            return Task.FromResult(new MarketplaceSandboxReadResponse(200, "{\"cards\":[]}", false));
        });
        var context = new ConnectionContext
        {
            Credentials = new Dictionary<string, string>
            {
                ["apiKey"] = "a.eyJ0Ijp0cnVlfQ.sig",
                ["sandbox"] = "auto",
            },
        };
        using var arguments = JsonDocument.Parse("""{"limit":50,"nm_id":12345,"updated_at":"2025-01-01T00:00:00Z"}""");

        var result = await plugin.ReadOperationAsync(context, "product_cards", arguments.RootElement);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal("https://content-api-sandbox.wildberries.ru/", origin!.ToString());
        Assert.Equal("https://content-api-sandbox.wildberries.ru/content/v2/get/cards/list?locale=ru", target!.ToString());
        Assert.Equal("a.eyJ0Ijp0cnVlfQ.sig", headers!["Authorization"]);
        Assert.Contains("updatedAt", body!, StringComparison.Ordinal);
        Assert.Contains("nmID", body!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("category_tree", "{}", "/v2/categories/tree")]
    [InlineData("category_parameters", "{\"category_id\":123,\"business_id\":456}", "/v2/category/123/parameters?businessId=456")]
    [InlineData("offer_mappings", "{\"business_id\":456,\"offer_ids\":[\"sku-1\"],\"page_token\":\"next/page\",\"limit\":50}", "/v2/businesses/456/offer-mappings?limit=50&pageToken=next%2Fpage")]
    [InlineData("offer_cards", "{\"business_id\":456,\"offer_ids\":[\"sku-1\"]}", "/v2/businesses/456/offer-cards")]
    [InlineData("prices", "{\"business_id\":456,\"offer_ids\":[\"sku-1\"]}", "/v2/businesses/456/offer-prices")]
    [InlineData("stocks", "{\"campaign_id\":789,\"offer_ids\":[\"sku-1\"]}", "/v2/campaigns/789/offers/stocks")]
    [InlineData("promotions", "{\"business_id\":456}", "/v2/businesses/456/promos")]
    public void Yandex_BuildsAllowlistedPathsAndJsonBodies(string operation, string argumentsJson, string expectedPath)
    {
        using var arguments = JsonDocument.Parse(argumentsJson);

        Assert.True(YandexMarketPlugin.TryBuildBrokerReadRequest(
            operation, arguments.RootElement, out var path, out var body));
        Assert.Equal(expectedPath, path);
        Assert.True(Encoding.UTF8.GetByteCount(body) <= 16_384);
        using var request = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Object, request.RootElement.ValueKind);
    }

    [Theory]
    [InlineData("offer_cards", "{\"business_id\":456,\"offer_ids\":[]}")]
    [InlineData("prices", "{\"business_id\":456,\"offer_ids\":[\"sku-1\"],\"path\":\"/v2/businesses/1/offer-prices/updates\"}")]
    [InlineData("stocks", "{\"campaign_id\":0,\"offer_ids\":[\"sku-1\"]}")]
    [InlineData("category_tree", "{\"language\":\"en\"}")]
    [InlineData("promotions", "{}")]
    public void Yandex_RejectsInvalidOrUnrecognizedArguments(string operation, string argumentsJson)
    {
        using var arguments = JsonDocument.Parse(argumentsJson);

        Assert.False(YandexMarketPlugin.TryBuildBrokerReadRequest(operation, arguments.RootElement, out _, out _));
    }

    [Fact]
    public void Yandex_OfferMappingsWithNoFilterRetainsEmptyRequestBody()
    {
        using var arguments = JsonDocument.Parse("""{"business_id":456,"offer_ids":[]}""");

        Assert.True(YandexMarketPlugin.TryBuildBrokerReadRequest(
            "offer_mappings", arguments.RootElement, out _, out var body));
        Assert.Equal("{}", body);
    }

    [Fact]
    public async Task Yandex_ReadOperation_UsesFixedRouteAndServerGeneratedApiKey()
    {
        Uri? origin = null;
        Uri? target = null;
        IReadOnlyDictionary<string, string>? headers = null;
        string? body = null;
        var plugin = new YandexMarketPlugin((baseUri, targetUri, requestHeaders, json, _) =>
        {
            origin = baseUri;
            target = targetUri;
            headers = requestHeaders;
            body = json;
            return Task.FromResult(new MarketplaceSandboxReadResponse(200, "{\"status\":\"OK\"}", false));
        });
        var context = new ConnectionContext { Credentials = new Dictionary<string, string> { ["apiKey"] = "market-key" } };
        using var arguments = JsonDocument.Parse("""{"business_id":456,"offer_ids":["sku-1"]}""");

        var result = await plugin.ReadOperationAsync(context, "prices", arguments.RootElement);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal("https://api.partner.market.yandex.ru/", origin!.ToString());
        Assert.Equal("https://api.partner.market.yandex.ru/v2/businesses/456/offer-prices", target!.ToString());
        Assert.Equal("market-key", headers!["Api-Key"]);
        Assert.Contains("sku-1", body!, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionOperationCatalogsExposeValidJsonSchemas()
    {
        var ozon = new OzonPlugin().GetReadOperations();
        var wildberries = new WildberriesPlugin().GetReadOperations();
        var yandex = new YandexMarketPlugin().GetReadOperations();
        Assert.Equal(10, ozon.Count);
        Assert.Single(wildberries);
        Assert.Equal(7, yandex.Count);
        Assert.Equal(ozon.Count, ozon.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(wildberries.Count, wildberries.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(yandex.Count, yandex.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count());
        var descriptors = ozon.Concat(wildberries).Concat(yandex).ToArray();

        Assert.Equal(18, descriptors.Length);
        foreach (var descriptor in descriptors)
        {
            using var schema = JsonDocument.Parse(descriptor.ArgumentsSchemaJson);
            Assert.Equal(JsonValueKind.Object, schema.RootElement.ValueKind);
            Assert.NotEmpty(descriptor.Description);
        }
    }
}
