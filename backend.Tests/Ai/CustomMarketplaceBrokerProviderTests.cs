using System.Text.Json;
using Delegat.Marketplace.Medusa;
using Delegat.Marketplace.OpenCart;
using Delegat.Marketplace.SDK;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Marketplace.WooCommerce;
using Delegat.Infrastructure.Security;
using Delegat.Models.Marketplace;
using Delegat.Services.Ai.Agent;
using Delegat.Services.Marketplace;
using Delegat.Services.Plugins;
using Delegat.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests.Ai;

public sealed class CustomMarketplaceBrokerProviderTests
{
    [Fact]
    public async Task WooCommerce_RejectsLegacyHttpOriginsForBroker()
    {
        var plugin = new WooCommercePlugin();
        var context = Context("http://localhost/wp-json/delegat/v1");

        var wrongHost = await plugin.ReadAsync(context, "seller", "/products");
        var legacyHttpHost = await plugin.ReadAsync(context, "api", "/products");

        Assert.Equal("host_not_allowed", wrongHost.Error);
        Assert.Equal("host_not_allowed", legacyHttpHost.Error);
    }

    [Fact]
    public async Task OpenCart_RejectsPrivateBridgeOriginBeforeBrokerRequest()
    {
        var plugin = new OpenCartPlugin();
        var context = Context("https://127.0.0.1/index.php?route=extension/delegat/api");

        var result = await plugin.ReadAsync(context, "api", "/products");

        Assert.Equal("host_not_allowed", result.Error);
    }

    [Fact]
    public async Task Medusa_LocalhostRemainsAvailableOnlyThroughLegacyPath()
    {
        var plugin = new MedusaPlugin();
        var context = Context("http://localhost:9000/admin/delegat");

        var result = await plugin.ReadAsync(context, "api", "/products");

        Assert.Equal("host_not_allowed", result.Error);
    }

    [Fact]
    public async Task Broker_DispatchesEncryptedCustomProviderConnectionsThroughRegistryAndWrapper()
    {
        using var db = TestDbFactory.CreateDb();
        var encryption = NewEncryption();
        var connections = new[]
        {
            (Id: 11, Key: "woocommerce", Plugin: (IMarketplacePlugin)new WooCommercePlugin(), ApiBaseUrl: "https://localhost/wp-json/delegat/v1"),
            (Id: 12, Key: "opencart", Plugin: (IMarketplacePlugin)new OpenCartPlugin(), ApiBaseUrl: "https://127.0.0.1/index.php?route=extension/delegat/api"),
            (Id: 13, Key: "medusa", Plugin: (IMarketplacePlugin)new MedusaPlugin(), ApiBaseUrl: "http://localhost:9000/admin/delegat"),
        };
        db.marketplaceConnections.AddRange(connections.Select(item => new MarketplaceConnection
        {
            Id = item.Id,
            storeId = 4,
            name = item.Key,
            adapterKey = item.Key,
            isActive = true,
            credentialsJson = encryption.Encrypt(JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["apiBaseUrl"] = item.ApiBaseUrl,
                ["apiKey"] = "broker-test-secret",
            })),
        }));
        await db.SaveChangesAsync();

        var wrappers = connections.ToDictionary(
            item => item.Key,
            item => (IMarketplaceAdapter)new PluginAdapterWrapper(
                item.Plugin, NullLoggerFactory.Instance, encryption),
            StringComparer.OrdinalIgnoreCase);
        var registry = new TestAdapterRegistry(wrappers);
        using var http = new HttpClient(new RejectingHandler());
        var broker = new MarketplaceCredentialBroker(
            db,
            encryption,
            http,
            NullLogger<MarketplaceCredentialBroker>.Instance,
            registry);

        foreach (var connection in connections)
        {
            var result = await broker.ReadAsync(4, 7, connection.Id, "api", "/products", CancellationToken.None);
            Assert.False(result.Ok);
            Assert.Equal("host_not_allowed", result.Error);
        }

        Assert.Empty(db.aiAgentAudits);
    }

    [Fact]
    public async Task Broker_DispatchesNamedReadOperationAndRedactsProviderResponse()
    {
        using var db = TestDbFactory.CreateDb();
        var encryption = NewEncryption();
        const string secret = "operation-secret-value";
        db.marketplaceConnections.Add(new MarketplaceConnection
        {
            Id = 14,
            storeId = 4,
            name = "Operation test",
            adapterKey = "operation-test",
            isActive = true,
            credentialsJson = encryption.Encrypt(JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["apiKey"] = secret,
            })),
        });
        await db.SaveChangesAsync();

        var plugin = new OperationTestPlugin();
        var wrapper = new PluginAdapterWrapper(plugin, NullLoggerFactory.Instance, encryption);
        var registry = new TestAdapterRegistry(new Dictionary<string, IMarketplaceAdapter>
        {
            [plugin.Key] = wrapper,
        });
        using var http = new HttpClient(new RejectingHandler());
        var broker = new MarketplaceCredentialBroker(
            db, encryption, http, NullLogger<MarketplaceCredentialBroker>.Instance, registry);
        using var argumentDocument = JsonDocument.Parse("""{"limit":12,"cursor":"next"}""");

        var result = await broker.ReadOperationAsync(
            4, 7, 14, "product_list", argumentDocument.RootElement, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(200, result.Status);
        Assert.Contains("[redacted]", result.Body!, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, result.Body!, StringComparison.Ordinal);
        Assert.Equal(secret, plugin.SeenCredential);
        Assert.Equal("{\"limit\":12,\"cursor\":\"next\"}", plugin.SeenArguments);
        var audit = Assert.Single(db.aiAgentAudits);
        Assert.Contains("operation product_list", audit.summary, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, audit.summary, StringComparison.Ordinal);
        Assert.DoesNotContain("next", audit.summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Broker_RejectsInvalidOrUnimplementedReadOperationWithoutFallback()
    {
        using var db = TestDbFactory.CreateDb();
        var encryption = NewEncryption();
        var plugin = new OperationTestPlugin();
        db.marketplaceConnections.Add(new MarketplaceConnection
        {
            Id = 15,
            storeId = 4,
            name = "Operation test",
            adapterKey = plugin.Key,
            isActive = true,
            credentialsJson = encryption.Encrypt("{}"),
        });
        await db.SaveChangesAsync();
        var wrapper = new PluginAdapterWrapper(plugin, NullLoggerFactory.Instance, encryption);
        var registry = new TestAdapterRegistry(new Dictionary<string, IMarketplaceAdapter> { [plugin.Key] = wrapper });
        using var http = new HttpClient(new RejectingHandler());
        var broker = new MarketplaceCredentialBroker(
            db, encryption, http, NullLogger<MarketplaceCredentialBroker>.Instance, registry);
        using var objectArgs = JsonDocument.Parse("{}");
        using var arrayArgs = JsonDocument.Parse("[]");
        using var oversizedArgs = JsonDocument.Parse(JsonSerializer.Serialize(new { payload = new string('x', 16_400) }));

        Assert.Equal("invalid_operation", (await broker.ReadOperationAsync(
            4, 7, 15, "POST", objectArgs.RootElement, CancellationToken.None)).Error);
        Assert.Equal("invalid_arguments", (await broker.ReadOperationAsync(
            4, 7, 15, "product_list", arrayArgs.RootElement, CancellationToken.None)).Error);
        Assert.Equal("invalid_arguments", (await broker.ReadOperationAsync(
            4, 7, 15, "product_list", oversizedArgs.RootElement, CancellationToken.None)).Error);
        Assert.Equal("operation_not_supported", (await broker.ReadOperationAsync(
            4, 7, 15, "write_product", objectArgs.RootElement, CancellationToken.None)).Error);
        Assert.Equal(1, plugin.Calls);
        Assert.Equal("write_product", plugin.LastOperation);
        Assert.Empty(db.aiAgentAudits);
    }

    [Fact]
    public async Task Broker_DiscoversAdapterOwnedReadOperationsForAnActiveStoreConnection()
    {
        using var db = TestDbFactory.CreateDb();
        var encryption = NewEncryption();
        var plugin = new OperationTestPlugin();
        db.marketplaceConnections.Add(new MarketplaceConnection
        {
            Id = 16,
            storeId = 4,
            name = "Operation test",
            adapterKey = plugin.Key,
            isActive = true,
            credentialsJson = encryption.Encrypt("{}"),
        });
        await db.SaveChangesAsync();

        var wrapper = new PluginAdapterWrapper(plugin, NullLoggerFactory.Instance, encryption);
        var registry = new TestAdapterRegistry(new Dictionary<string, IMarketplaceAdapter> { [plugin.Key] = wrapper });
        using var http = new HttpClient(new RejectingHandler());
        var broker = new MarketplaceCredentialBroker(
            db, encryption, http, NullLogger<MarketplaceCredentialBroker>.Instance, registry);

        var result = await broker.GetReadOperationsAsync(4, 16, CancellationToken.None);

        Assert.True(result.Ok);
        var operation = Assert.Single(result.Operations!);
        Assert.Equal("product_list", operation.Name);
        Assert.Equal("{}", operation.ArgumentsSchemaJson);
    }

    private static ConnectionContext Context(string apiBaseUrl) => new()
    {
        Credentials = new Dictionary<string, string>
        {
            ["apiBaseUrl"] = apiBaseUrl,
            ["apiKey"] = "test-api-key",
        },
    };

    private static EncryptionService NewEncryption() =>
        new(Convert.ToBase64String(Enumerable.Range(1, 32).Select(x => (byte)x).ToArray()));

    private sealed class TestAdapterRegistry(IReadOnlyDictionary<string, IMarketplaceAdapter> adapters)
        : IMarketplaceAdapterRegistry
    {
        public IReadOnlyList<IMarketplaceAdapter> GetAll() => adapters.Values.ToList();
        public IMarketplaceAdapter? GetByKey(string key) => adapters.GetValueOrDefault(key);
        public bool Exists(string key) => adapters.ContainsKey(key);
        public IReadOnlyList<MarketplaceInfo> GetAvailableMarketplaces() => [];
    }

    private sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Custom providers must not use the fixed-host HTTP client.");
    }

    private sealed class OperationTestPlugin : MarketplacePluginBase, IMarketplaceSandboxReadOperationBroker
    {
        public override string Key => "operation-test";
        public override string DisplayName => "Operation test";
        public override string Version => "1.0.0";
        public override Delegat.Marketplace.SDK.MarketplaceCapabilities Capabilities =>
            (Delegat.Marketplace.SDK.MarketplaceCapabilities)0;
        public override Delegat.Marketplace.SDK.CredentialsSchema GetCredentialsSchema() => new();
        public override Task<ConnectionTestResult> TestConnectionAsync(
            ConnectionContext context, CancellationToken ct = default) => Task.FromResult(new ConnectionTestResult(true));

        public int Calls { get; private set; }
        public string? LastOperation { get; private set; }
        public string? SeenCredential { get; private set; }
        public string? SeenArguments { get; private set; }

        public IReadOnlyList<MarketplaceSandboxReadOperationDescriptor> GetReadOperations() =>
        [
            new("product_list", "Read product listings.", "{}"),
        ];

        public Task<MarketplaceSandboxReadResponse> ReadOperationAsync(
            ConnectionContext context, string operation, JsonElement arguments, CancellationToken ct = default)
        {
            Calls++;
            LastOperation = operation;
            if (operation != "product_list")
                return Task.FromResult(new MarketplaceSandboxReadResponse(null, null, false, "operation_not_supported"));

            SeenCredential = context.GetCredential("apiKey");
            SeenArguments = arguments.GetRawText();
            var body = JsonSerializer.Serialize(new { echoedCredential = SeenCredential, received = arguments });
            return Task.FromResult(new MarketplaceSandboxReadResponse(200, body, false));
        }
    }
}
