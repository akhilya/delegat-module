using System.Net;
using System.Text;
using System.Text.Json;
using Delegat.Infrastructure.Security;
using Delegat.Models.Marketplace;
using Delegat.Services.Ai.Agent;
using Delegat.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests.Ai;

public sealed class MarketplaceCredentialBrokerTests
{
    [Fact]
    public async Task ReadAsync_UsesFixedOzonHostAndKeepsCredentialsOutOfResultAndAudit()
    {
        using var db = TestDbFactory.CreateDb();
        var encryption = NewEncryption();
        db.marketplaceConnections.Add(new MarketplaceConnection
        {
            Id = 12,
            storeId = 4,
            name = "Seller",
            adapterKey = "ozon",
            isActive = true,
            credentialsJson = encryption.Encrypt("""{"clientId":"client-private","apiKey":"ozon-private-key"}"""),
        });
        await db.SaveChangesAsync();

        using var handler = new CaptureHandler("""{"echo":"ozon-private-key","items":[]}""");
        using var http = new HttpClient(handler);
        var broker = new MarketplaceCredentialBroker(db, encryption, http, NullLogger<MarketplaceCredentialBroker>.Instance);

        var result = await broker.ReadAsync(4, 7, 12, "seller", "/v3/product/list", CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(200, result.Status);
        Assert.Equal("https://api-seller.ozon.ru/v3/product/list", handler.RequestUri!.ToString());
        Assert.Equal("client-private", handler.Headers!["Client-Id"]);
        Assert.Equal("ozon-private-key", handler.Headers["Api-Key"]);
        Assert.DoesNotContain("ozon-private-key", result.Body, StringComparison.Ordinal);
        Assert.Contains("[redacted]", result.Body, StringComparison.Ordinal);
        var responseJson = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("client-private", responseJson, StringComparison.Ordinal);
        Assert.DoesNotContain("ozon-private-key", responseJson, StringComparison.Ordinal);

        var audit = Assert.Single(db.aiAgentAudits);
        Assert.Equal("marketplace_broker_read", audit.tool);
        Assert.DoesNotContain("ozon-private-key", audit.summary, StringComparison.Ordinal);
        Assert.DoesNotContain("client-private", audit.summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("//attacker.example/path")]
    [InlineData("https://attacker.example/path")]
    [InlineData("/\\attacker.example/path")]
    [InlineData("/path#fragment")]
    public async Task ReadAsync_RejectsUntrustedPathBeforeSending(string path)
    {
        using var db = TestDbFactory.CreateDb();
        using var http = new HttpClient(new CaptureHandler());
        var broker = new MarketplaceCredentialBroker(db, NewEncryption(), http, NullLogger<MarketplaceCredentialBroker>.Instance);

        var result = await broker.ReadAsync(4, 7, 12, "seller", path, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("invalid_path", result.Error);
    }

    [Fact]
    public async Task ReadAsync_RejectsUnknownHostAndUnsupportedCustomAdapter()
    {
        using var db = TestDbFactory.CreateDb();
        var encryption = NewEncryption();
        db.marketplaceConnections.AddRange(
            new MarketplaceConnection
            {
                Id = 1, storeId = 4, name = "Ozon", adapterKey = "ozon", isActive = true,
                credentialsJson = encryption.Encrypt("""{"clientId":"c","apiKey":"k"}"""),
            },
            new MarketplaceConnection
            {
                Id = 2, storeId = 4, name = "Custom", adapterKey = "opencart", isActive = true,
                credentialsJson = encryption.Encrypt("""{"apiBaseUrl":"https://shop.example"}"""),
            });
        await db.SaveChangesAsync();

        using var handler = new CaptureHandler();
        using var http = new HttpClient(handler);
        var broker = new MarketplaceCredentialBroker(db, encryption, http, NullLogger<MarketplaceCredentialBroker>.Instance);

        var unknownHost = await broker.ReadAsync(4, 7, 1, "arbitrary", "/v3/product/list", CancellationToken.None);
        var customAdapter = await broker.ReadAsync(4, 7, 2, "api", "/products", CancellationToken.None);

        Assert.Equal("host_not_allowed", unknownHost.Error);
        Assert.Equal("adapter_not_supported", customAdapter.Error);
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task ReadAsync_ScopesConnectionLookupToStore()
    {
        using var db = TestDbFactory.CreateDb();
        var encryption = NewEncryption();
        db.marketplaceConnections.Add(new MarketplaceConnection
        {
            Id = 12, storeId = 4, name = "Seller", adapterKey = "ozon", isActive = true,
            credentialsJson = encryption.Encrypt("""{"clientId":"c","apiKey":"k"}"""),
        });
        await db.SaveChangesAsync();
        using var handler = new CaptureHandler();
        using var http = new HttpClient(handler);
        var broker = new MarketplaceCredentialBroker(db, encryption, http, NullLogger<MarketplaceCredentialBroker>.Instance);

        var result = await broker.ReadAsync(99, 7, 12, "seller", "/v3/product/list", CancellationToken.None);

        Assert.Equal("connection_not_found", result.Error);
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task ReadAsync_CapsLargeMarketplaceResponses()
    {
        using var db = TestDbFactory.CreateDb();
        var encryption = NewEncryption();
        db.marketplaceConnections.Add(new MarketplaceConnection
        {
            Id = 12, storeId = 4, name = "Seller", adapterKey = "ozon", isActive = true,
            credentialsJson = encryption.Encrypt("""{"clientId":"client-private","apiKey":"ozon-private-key"}"""),
        });
        await db.SaveChangesAsync();
        using var handler = new CaptureHandler(new string('x', MarketplaceCredentialBroker.MaxResponseBytes + 500));
        using var http = new HttpClient(handler);
        var broker = new MarketplaceCredentialBroker(db, encryption, http, NullLogger<MarketplaceCredentialBroker>.Instance);

        var result = await broker.ReadAsync(4, 7, 12, "seller", "/v3/product/list", CancellationToken.None);

        Assert.True(result.Ok);
        Assert.True(result.Truncated);
        Assert.Equal(MarketplaceCredentialBroker.MaxResponseBytes, Encoding.UTF8.GetByteCount(result.Body!));
    }

    private static EncryptionService NewEncryption() =>
        new(Convert.ToBase64String(Enumerable.Range(1, 32).Select(x => (byte)x).ToArray()));

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public CaptureHandler(string responseBody = "{\"items\":[]}") => _responseBody = responseBody;

        public Uri? RequestUri { get; private set; }
        public Dictionary<string, string>? Headers { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Headers = request.Headers.ToDictionary(
                h => h.Key,
                h => string.Join(",", h.Value),
                StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json"),
            });
        }
    }
}
