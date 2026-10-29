using System.Text.Json;
using System.Text;
using Delegat.Marketplace.Ozon;
using Delegat.Marketplace.SDK;

namespace Delegat.Tests.Ai;

public sealed class OzonReadOperationBrokerTests
{
    [Theory]
    [InlineData("category_tree", "{}", "/v1/description-category/tree")]
    [InlineData("category_attributes", "{\"description_category_id\":17028922,\"type_id\":91527}", "/v1/description-category/attribute")]
    [InlineData("attribute_values", "{\"description_category_id\":17028922,\"type_id\":91527,\"attribute_id\":4191}", "/v1/description-category/attribute/values")]
    [InlineData("product_list", "{\"limit\":25,\"offer_ids\":[\"SKU-1\"]}", "/v3/product/list")]
    [InlineData("product_info", "{\"product_ids\":[\"123456\"]}", "/v3/product/info/list")]
    [InlineData("product_attributes", "{\"product_ids\":[\"123456\"],\"limit\":25}", "/v4/product/info/attributes")]
    [InlineData("prices", "{\"offer_ids\":[\"SKU-1\"],\"limit\":25}", "/v5/product/info/prices")]
    [InlineData("stocks", "{\"product_ids\":[\"123456\"],\"limit\":25}", "/v4/product/info/stocks")]
    [InlineData("warehouses", "{\"limit\":50}", "/v2/warehouse/list")]
    [InlineData("upload_quota", "{}", "/v4/product/info/limit")]
    public void BuildReadRequest_MapsAllowlistedOperationToFixedEndpoint(
        string operation, string argumentsJson, string expectedPath)
    {
        using var arguments = JsonDocument.Parse(argumentsJson);

        Assert.True(OzonPlugin.TryBuildBrokerReadRequest(
            operation, arguments.RootElement, out var path, out var body));
        Assert.Equal(expectedPath, path);
        Assert.True(Encoding.UTF8.GetByteCount(body) <= 16_384);
        using var bodyDocument = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Object, bodyDocument.RootElement.ValueKind);
    }

    [Theory]
    [InlineData("product_list", "{\"limit\":101}")]
    [InlineData("product_list", "{\"arbitrary_path\":\"/v3/product/import\"}")]
    [InlineData("product_list", "{\"limit\":10,\"limit\":20}")]
    [InlineData("product_info", "{}")]
    [InlineData("product_info", "{\"product_ids\":[\"123\",\"../admin\"]}")]
    [InlineData("category_attributes", "{\"description_category_id\":0,\"type_id\":1}")]
    public void BuildReadRequest_RejectsUnboundedOrUnrecognizedArguments(string operation, string argumentsJson)
    {
        using var arguments = JsonDocument.Parse(argumentsJson);

        Assert.False(OzonPlugin.TryBuildBrokerReadRequest(
            operation, arguments.RootElement, out _, out _));
    }

    [Fact]
    public async Task ReadOperationAsync_UsesFixedOriginGeneratedAuthAndMappedBody()
    {
        Uri? baseUri = null;
        Uri? targetUri = null;
        Dictionary<string, string>? headers = null;
        string? body = null;
        var plugin = new OzonPlugin((origin, target, requestHeaders, json, _) =>
        {
            baseUri = origin;
            targetUri = target;
            headers = requestHeaders.ToDictionary(
                pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            body = json;
            return Task.FromResult(new MarketplaceSandboxReadResponse(200, "{\"items\":[]}", false));
        });
        var context = new ConnectionContext
        {
            Credentials = new Dictionary<string, string>
            {
                ["clientId"] = "seller-client-id",
                ["apiKey"] = "seller-api-key",
            },
        };
        using var arguments = JsonDocument.Parse("""{"last_id":"cursor-1","limit":20,"offer_ids":["SKU-1"]}""");

        var result = await plugin.ReadOperationAsync(
            context, "product_list", arguments.RootElement, CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal("https://api-seller.ozon.ru/", baseUri!.ToString());
        Assert.Equal("https://api-seller.ozon.ru/v3/product/list", targetUri!.ToString());
        Assert.Equal("seller-client-id", headers!["Client-Id"]);
        Assert.Equal("seller-api-key", headers["Api-Key"]);
        using var bodyDocument = JsonDocument.Parse(body!);
        Assert.Equal("cursor-1", bodyDocument.RootElement.GetProperty("last_id").GetString());
        Assert.Equal("ALL", bodyDocument.RootElement.GetProperty("filter").GetProperty("visibility").GetString());
        Assert.Equal("SKU-1", bodyDocument.RootElement.GetProperty("filter").GetProperty("offer_id")[0].GetString());
    }

    [Fact]
    public async Task ReadOperationAsync_RejectsUnknownOperationBeforeTransport()
    {
        var called = false;
        var plugin = new OzonPlugin((_, _, _, _, _) =>
        {
            called = true;
            return Task.FromResult(new MarketplaceSandboxReadResponse(200, "{}", false));
        });
        using var arguments = JsonDocument.Parse("{}");

        var result = await plugin.ReadOperationAsync(
            new ConnectionContext(), "delete_product", arguments.RootElement, CancellationToken.None);

        Assert.Equal("operation_not_supported", result.Error);
        Assert.False(called);
    }
}
