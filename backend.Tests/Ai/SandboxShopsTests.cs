using System.Text;
using System.Text.Json;
using Delegat.Infrastructure.Security;
using Delegat.Marketplace.Medusa;
using Delegat.Marketplace.Ozon;
using Delegat.Marketplace.SDK;
using Delegat.Models.Marketplace;
using Delegat.Services.Ai.Agent;
using Delegat.Services.Marketplace;
using Delegat.Services.Plugins;
using Delegat.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests.Ai;

public class SandboxShopsTests
{
    [Fact]
    public void Wildberries_ProductionHost_WhenFlagSaysProduction()
    {
        var hosts = SandboxShops.Hosts("wildberries", new Dictionary<string, string>
        {
            ["apiKey"] = "live-key",
            ["sandbox"] = "production",
        });
        Assert.Equal("https://content-api.wildberries.ru", hosts["content"]);
        Assert.Equal("https://marketplace-api.wildberries.ru", hosts["marketplace"]);
    }

    [Fact]
    public void Wildberries_SandboxHost_WhenTokenClaimSaysTest()
    {
        var hosts = SandboxShops.Hosts("wildberries", new Dictionary<string, string>
        {
            ["apiKey"] = Jwt("{\"t\":true}"),
            ["sandbox"] = "auto",
        });
        Assert.Equal("https://content-api-sandbox.wildberries.ru", hosts["content"]);
        Assert.Equal("https://discounts-prices-api-sandbox.wildberries.ru", hosts["calendar"]);
    }

    [Fact]
    public void SellerShops_UseThePlatformSnapshot()
    {
        Assert.Equal("yandex", SandboxShops.Docs("yandex")!["platform"]);
        Assert.Equal("yandex", SandboxShops.Docs("yandexmarket")!["platform"]);
        Assert.Equal("wildberries", SandboxShops.Docs("wildberries")!["platform"]);
        Assert.Equal("ozon", SandboxShops.Docs("ozon")!["platform"]);
        Assert.Null(SandboxShops.Docs("opencart"));
        Assert.Null(SandboxShops.Docs("medusa"));
    }

    [Fact]
    public void Ozon_AuthNamesTheSellerHeaders()
    {
        Assert.Contains("Client-Id", SandboxShops.Auth("ozon"), StringComparison.Ordinal);
        var hosts = SandboxShops.Hosts("ozon", new Dictionary<string, string>());
        Assert.Equal("https://api-seller.ozon.ru", hosts["seller"]);
    }

    [Fact]
    public void Avito_UsesFixedApiHostAndDocumentsOAuthCredentials()
    {
        var credentials = new Dictionary<string, string>
        {
            ["clientId"] = "avito-client-id",
            ["clientSecret"] = "avito-client-secret",
        };

        var shop = JsonSerializer.Serialize(SandboxShops.Describe(
            9, "Avito", "avito", true, credentials));

        Assert.Contains("https://api.avito.ru", shop, StringComparison.Ordinal);
        Assert.Contains("client_credentials", shop, StringComparison.Ordinal);
        Assert.Contains("/core/v1/items", shop, StringComparison.Ordinal);
        Assert.Contains("do not follow URLs to other hosts", shop, StringComparison.Ordinal);
        Assert.Contains("avito-client-secret", shop, StringComparison.Ordinal);
    }

    [Fact]
    public void BrokerMode_OmitsCredentialsAndAuthForSupportedAdapters()
    {
        const string secret = "seller-secret-long-enough";
        var shop = JsonSerializer.Serialize(SandboxShops.Describe(
            21,
            "Ozon",
            "ozon",
            true,
            new Dictionary<string, string> { ["clientId"] = "seller-id", ["apiKey"] = secret },
            brokerRollout: true,
            brokerProviderAvailable: true,
            brokerGetAvailable: true,
            brokerOperationsAvailable: true));

        Assert.Contains("\"access\":\"broker\"", shop, StringComparison.Ordinal);
        Assert.Contains("\"broker\":{\"get\":true,\"operations\":true}", shop, StringComparison.Ordinal);
        Assert.Contains("\"id\":21", shop, StringComparison.Ordinal);
        Assert.Contains("https://api-seller.ozon.ru", shop, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, shop, StringComparison.Ordinal);
        Assert.DoesNotContain("credentials", shop, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("auth", shop, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BrokerMode_KeepsLegacyCredentialsForIneligibleLocalBridge()
    {
        const string secret = "medusa-secret-long-enough";
        var shop = JsonSerializer.Serialize(SandboxShops.Describe(
            22,
            "Local Medusa",
            "medusa",
            true,
            new Dictionary<string, string>
            {
                ["apiBaseUrl"] = "http://localhost:9000/admin/delegat",
                ["apiKey"] = secret,
            },
            brokerRollout: true));

        Assert.Contains("\"access\":\"legacy\"", shop, StringComparison.Ordinal);
        Assert.Contains(secret, shop, StringComparison.Ordinal);
        Assert.Contains("http://localhost:9000/admin/delegat", shop, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForStoreAsync_RemovesCredentialsOnlyWhenPluginAndOriginSupportBroker()
    {
        using var db = TestDbFactory.CreateDb();
        var encryption = new EncryptionService(
            Convert.ToBase64String(Enumerable.Range(1, 32).Select(x => (byte)x).ToArray()));
        const string ozonSecret = "ozon-secret-long-enough";
        const string medusaSecret = "medusa-secret-long-enough";
        db.marketplaceConnections.AddRange(
            new MarketplaceConnection
            {
                Id = 41,
                storeId = 4,
                name = "Ozon",
                adapterKey = "ozon",
                isActive = true,
                credentialsJson = encryption.Encrypt(JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["clientId"] = "seller-id",
                    ["apiKey"] = ozonSecret,
                })),
            },
            new MarketplaceConnection
            {
                Id = 42,
                storeId = 4,
                name = "Local Medusa",
                adapterKey = "medusa",
                isActive = true,
                credentialsJson = encryption.Encrypt(JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["apiBaseUrl"] = "http://localhost:9000/admin/delegat",
                    ["apiKey"] = medusaSecret,
                })),
            });
        await db.SaveChangesAsync();

        var adapters = new TestAdapterRegistry(new Dictionary<string, IMarketplaceAdapter>
        {
            ["ozon"] = new PluginAdapterWrapper(new OzonPlugin(), NullLoggerFactory.Instance, encryption),
            ["medusa"] = new PluginAdapterWrapper(new MedusaPlugin(), NullLoggerFactory.Instance, encryption),
        });

        var json = await SandboxShops.ForStoreAsync(
            db, encryption, 4, CancellationToken.None, brokerRollout: true, adapters);
        using var document = JsonDocument.Parse(json);
        var shops = document.RootElement.EnumerateArray().ToArray();

        Assert.Equal("broker", shops[0].GetProperty("access").GetString());
        Assert.False(shops[0].TryGetProperty("credentials", out _));
        Assert.True(shops[0].GetProperty("broker").GetProperty("operations").GetBoolean());
        Assert.DoesNotContain(ozonSecret, json, StringComparison.Ordinal);

        Assert.Equal("legacy", shops[1].GetProperty("access").GetString());
        Assert.Equal(medusaSecret,
            shops[1].GetProperty("credentials").GetProperty("apiKey").GetString());
    }

    [Theory]
    [InlineData("woocommerce", "https://store.example/wp-json/delegat/v1")]
    [InlineData("opencart", "https://store.example/index.php?route=extension/delegat")]
    [InlineData("medusa", "http://localhost:9000/admin/delegat")]
    public void CustomHostAdapters_KeepTheirConfiguredLegacyHost(string adapter, string apiBaseUrl)
    {
        var hosts = SandboxShops.Hosts(adapter, new Dictionary<string, string>
        {
            ["apiBaseUrl"] = apiBaseUrl,
        });

        Assert.Equal(apiBaseUrl, hosts["api"]);
    }

    [Theory]
    [InlineData("woocommerce")]
    [InlineData("opencart")]
    public void StorefrontBridgeAdapters_DescribeBothAuthenticationHeaders(string adapter)
    {
        var auth = SandboxShops.Auth(adapter);

        Assert.Contains("Authorization=Bearer credentials.apiKey", auth, StringComparison.Ordinal);
        Assert.Contains("X-Delegat-Api-Key=credentials.apiKey", auth, StringComparison.Ordinal);
        Assert.Contains("hosts.api", auth, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_StripsApiKeysFromOutput()
    {
        const string key = "wb-secret-key-value";
        var json = JsonSerializer.Serialize(new[]
        {
            new
            {
                credentials = new Dictionary<string, string>
                {
                    ["apiKey"] = key,
                    ["sandbox"] = "production",
                },
            },
        });
        var secrets = SandboxShops.SecretValues(json);
        var text = SandboxShops.Redact($"brand list ok {key}", secrets);
        Assert.DoesNotContain(key, text, StringComparison.Ordinal);
        Assert.Contains("[redacted]", text, StringComparison.Ordinal);
        Assert.Contains("production", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateRunBody_PassesShopsThrough()
    {
        using var content = PythonSandboxClient.CreateRunBody(
            "print(1)", 4, "http://api", "Bearer t",
            """[{"id":4,"adapter":"ozon"}]""");
        var json = Encoding.UTF8.GetString(await content.ReadAsByteArrayAsync());
        Assert.Contains("\"adapter\":\"ozon\"", json, StringComparison.Ordinal);
        Assert.Contains("\"storeId\":4", json, StringComparison.Ordinal);
    }

    private static string Jwt(string payloadJson)
    {
        static string B64(string s) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(s))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

        return B64("{\"alg\":\"none\"}") + "." + B64(payloadJson) + ".sig";
    }

    private sealed class TestAdapterRegistry(IReadOnlyDictionary<string, IMarketplaceAdapter> adapters)
        : IMarketplaceAdapterRegistry
    {
        public IReadOnlyList<IMarketplaceAdapter> GetAll() => adapters.Values.ToList();
        public IMarketplaceAdapter? GetByKey(string key) => adapters.GetValueOrDefault(key);
        public bool Exists(string key) => adapters.ContainsKey(key);
        public IReadOnlyList<MarketplaceInfo> GetAvailableMarketplaces() => [];
    }
}
