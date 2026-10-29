using System.Net;
using System.Text;
using Delegat.Marketplace.Avito;
using Delegat.Marketplace.SDK;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Infrastructure.Security;
using Delegat.Models.Marketplace;
using Delegat.Services.Plugins;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class AvitoApiClientTests
{
    [Fact]
    public async Task TestConnection_UsesClientCredentialsThenChecksAccountIdentity()
    {
        var handler = new StubHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/token")
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Contains("grant_type=client_credentials", request.Content!.ReadAsStringAsync().Result);
                Assert.Contains("client_id=app-id", request.Content.ReadAsStringAsync().Result);
                return Json(HttpStatusCode.OK, "{\"access_token\":\"access-token\",\"expires_in\":3600}");
            }

            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("access-token", request.Headers.Authorization?.Parameter);
            Assert.Equal("/core/v1/accounts/self", request.RequestUri.AbsolutePath);
            return Json(HttpStatusCode.OK, "{\"id\":12345,\"name\":\"Seller\"}");
        });
        var context = new ConnectionContext
        {
            Credentials = new Dictionary<string, string>
            {
                ["clientId"] = "app-id",
                ["clientSecret"] = "app-secret"
            }
        };
        var plugin = new AvitoPlugin(() => handler);

        var result = await plugin.TestConnectionAsync(context);

        Assert.True(result.Success);
        Assert.Equal("12345", result.Details!["accountId"]);
        Assert.Equal("Seller", result.Details["name"]);
    }

    [Fact]
    public async Task TestConnection_ExplainsRejectedCredentialsWithoutExposingSecrets()
    {
        var plugin = new AvitoPlugin(() => new StubHandler((_, _) =>
            Json(HttpStatusCode.Unauthorized, "{\"error\":\"invalid_client\"}")));
        var context = new ConnectionContext
        {
            Credentials = new Dictionary<string, string>
            {
                ["clientId"] = "bad-id",
                ["clientSecret"] = "do-not-leak-this"
            }
        };

        var result = await plugin.TestConnectionAsync(context);

        Assert.False(result.Success);
        Assert.Contains("Avito rejected", result.Message);
        Assert.DoesNotContain("do-not-leak-this", result.Message);
    }

    [Fact]
    public async Task ImportAllProducts_UsesAvitoItemsPagingAndMapsListingSummary()
    {
        var handler = new StubHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/token")
                return Json(HttpStatusCode.OK, "{\"access_token\":\"access-token\",\"expires_in\":3600}");
            Assert.Equal("/core/v1/items", request.RequestUri.AbsolutePath);
            Assert.Contains("page=1", request.RequestUri.Query);
            Assert.Contains("per_page=10", request.RequestUri.Query);
            return Json(HttpStatusCode.OK,
                "{\"resources\":[{\"id\":24122231,\"title\":\"Desk\",\"price\":35000,\"category\":{\"id\":111,\"name\":\"Furniture\"},\"status\":\"active\",\"url\":\"https://www.avito.ru/item\"}],\"meta\":{\"page\":1,\"per_page\":10}}");
        });
        var plugin = new AvitoPlugin(() => handler);

        var result = await plugin.ImportAllProductsAsync(CredentialsContext(), pageSize: 10);

        Assert.False(result.HasMore);
        var item = Assert.Single(result.Items);
        Assert.Equal("24122231", item.ExternalId);
        Assert.Equal("Desk", item.Name);
        Assert.Equal(35000m, item.Price);
        Assert.Equal("111", item.CategoryId);
    }

    [Fact]
    public async Task GetCategories_MapsAvitoTreeAndParentRelationships()
    {
        var plugin = new AvitoPlugin(() => new StubHandler((request, _) => request.RequestUri!.AbsolutePath == "/token"
            ? Json(HttpStatusCode.OK, "{\"access_token\":\"access-token\",\"expires_in\":3600}")
            : Json(HttpStatusCode.OK,
                "{\"categories\":[{\"slug\":\"goods\",\"name\":\"Goods\",\"nested\":[{\"slug\":\"tools\",\"name\":\"Tools\",\"nested\":[]}]}]}")));

        var roots = await plugin.GetCategoriesAsync(CredentialsContext());
        var children = await plugin.GetCategoriesAsync(CredentialsContext(), "goods");

        Assert.Equal("goods", Assert.Single(roots).Id);
        Assert.Equal("tools", Assert.Single(children).Id);
        Assert.Equal("goods", children[0].ParentId);
    }

    [Fact]
    public async Task ExportPrices_UsesTheDedicatedPriceEndpoint()
    {
        var handler = new StubHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/token")
                return Json(HttpStatusCode.OK, "{\"access_token\":\"access-token\",\"expires_in\":3600}");
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/core/v1/items/123/update_price", request.RequestUri.AbsolutePath);
            Assert.Contains("\"price\":1250", request.Content!.ReadAsStringAsync().Result);
            return Json(HttpStatusCode.OK, "{\"result\":{\"success\":true}}");
        });
        var plugin = new AvitoPlugin(() => handler);

        var result = await plugin.ExportPricesAsync(CredentialsContext(), [new PriceInfo("123", 1250m)]);

        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(0, result.FailedCount);
    }

    [Fact]
    public async Task GetCategoryFields_MapsRequiredSelectAndNumericFields()
    {
        var plugin = new AvitoPlugin(() => new StubHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/token")
                return Json(HttpStatusCode.OK, "{\"access_token\":\"access-token\",\"expires_in\":3600}");
            Assert.Equal("/autoload/v1/user-docs/node/goods/fields", request.RequestUri.AbsolutePath);
            return Json(HttpStatusCode.OK,
                "{\"fields\":[{\"tag\":\"Condition\",\"label\":\"Condition\",\"descriptions\":\"Item condition\",\"content\":[{\"field_type\":\"select\",\"data_type\":\"string\",\"required\":true,\"required_by_dependency\":false,\"values\":[{\"value\":\"new\",\"description\":\"New\"}]}]},{\"tag\":\"Area\",\"label\":\"Area\",\"content\":[{\"field_type\":\"input\",\"data_type\":\"float\",\"required\":false,\"required_by_dependency\":false,\"values_range\":{\"min\":1,\"max\":100}}]}]}");
        }));

        var fields = await plugin.GetCategoryFieldsAsync(CredentialsContext(), "goods");

        Assert.Equal(2, fields.Count);
        Assert.Equal("select", fields[0].Type);
        Assert.True(fields[0].IsRequired);
        Assert.Equal(new ExternalFieldOption("new", "New"), Assert.Single(fields[0].Options!));
        Assert.Equal("number", fields[1].Type);
        Assert.Equal(1m, fields[1].MinValue);
        Assert.Equal(100m, fields[1].MaxValue);
    }

    [Fact]
    public async Task ReadBroker_UsesAvitoOAuthOnFixedHostAndRedactsSecrets()
    {
        var handler = new StubHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/token")
                return Json(HttpStatusCode.OK, "{\"access_token\":\"broker-access-token\",\"expires_in\":3600}");

            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("api.avito.ru", request.RequestUri.Host);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("broker-access-token", request.Headers.Authorization?.Parameter);
            Assert.Equal("/core/v1/accounts/self", request.RequestUri.AbsolutePath);
            return Json(HttpStatusCode.OK, "{\"account\":\"app-secret\",\"echo\":\"broker-access-token\"}");
        });
        var context = CredentialsContext();
        context.ConnectionId = 9817;
        var plugin = new AvitoPlugin(() => handler);

        var result = await plugin.ReadAsync(context, "api", "/core/v1/accounts/self");

        Assert.Equal(200, result.StatusCode);
        Assert.False(result.Truncated);
        Assert.DoesNotContain("app-secret", result.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("broker-access-token", result.Body, StringComparison.Ordinal);
        Assert.Contains("[redacted]", result.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadBroker_RejectsUnknownHostAndAbsoluteUrl()
    {
        var plugin = new AvitoPlugin(() => throw new InvalidOperationException("Must not send"));
        var context = CredentialsContext();
        context.ConnectionId = 9818;

        var badHost = await plugin.ReadAsync(context, "shop", "/core/v1/accounts/self");
        var badPath = await plugin.ReadAsync(context, "api", "https://attacker.example/path");

        Assert.Equal("host_not_allowed", badHost.Error);
        Assert.Equal("invalid_path", badPath.Error);
    }

    [Fact]
    public async Task PluginAdapterWrapper_ForwardsOptionalBrokerCapabilityWithDecryptedCredentials()
    {
        var encryption = new EncryptionService(
            Convert.ToBase64String(Enumerable.Range(1, 32).Select(x => (byte)x).ToArray()));
        var handler = new StubHandler((request, _) => request.RequestUri!.AbsolutePath == "/token"
            ? Json(HttpStatusCode.OK, "{\"access_token\":\"wrapper-token\",\"expires_in\":3600}")
            : Json(HttpStatusCode.OK, "{\"ok\":true}"));
        var wrapper = new PluginAdapterWrapper(
            new AvitoPlugin(() => handler),
            NullLoggerFactory.Instance,
            encryption);
        var connection = new MarketplaceConnection
        {
            Id = 9819,
            storeId = 27,
            name = "Avito",
            adapterKey = "avito",
            credentialsJson = encryption.Encrypt("""{"clientId":"app-id","clientSecret":"app-secret"}"""),
        };

        var result = await wrapper.ReadAsync(connection, "api", "/core/v1/accounts/self", CancellationToken.None);

        Assert.Equal(200, result?.StatusCode);
        Assert.Equal("{\"ok\":true}", result?.Body);
    }

    private static ConnectionContext CredentialsContext() => new()
    {
        Credentials = new Dictionary<string, string>
        {
            ["clientId"] = "app-id",
            ["clientSecret"] = "app-secret"
        }
    };

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(send(request, cancellationToken));
    }
}
