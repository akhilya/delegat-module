using Delegat.Marketplace.Avito;
using Delegat.Marketplace.Medusa;
using Delegat.Marketplace.OpenCart;
using Delegat.Marketplace.Ozon;
using Delegat.Marketplace.SDK;
using Delegat.Marketplace.Wildberries;
using Delegat.Marketplace.WooCommerce;
using Delegat.Marketplace.YandexMarket;

namespace Delegat.Tests;

public sealed class IntegrationExecutionP4AdaptersTests
{
    private sealed class StubTransport(
        Func<IntegrationTransportRequest, CancellationToken, Task<IntegrationTransportResponse>> handler) : IMarketplaceTransport
    {
        public Task<IntegrationTransportResponse> SendAsync(IntegrationTransportRequest request, CancellationToken ct = default)
            => handler(request, ct);
    }

    [Fact]
    public void AllSevenAdapters_ImplementIMarketplaceExecutionProvider()
    {
        object[] plugins =
        [
            new AvitoPlugin(),
            new MedusaPlugin(),
            new OpenCartPlugin(),
            new OzonPlugin(),
            new WildberriesPlugin(),
            new WooCommercePlugin(),
            new YandexMarketPlugin()
        ];

        foreach (var plugin in plugins)
            Assert.IsAssignableFrom<IMarketplaceExecutionProvider>(plugin);
    }

    [Fact]
    public async Task OpenCart_BulkPrice404Fallback_AppliesViaProductUpsertAndConfirms()
    {
        var plugin = new OpenCartPlugin();
        var calls = new List<string>();
        var conn = new ConnectionContext
        {
            ConnectionId = 21,
            AdapterKey = "opencart",
            Credentials = new Dictionary<string, string>
            {
                ["apiBaseUrl"] = "https://shop.example/index.php?route=extension/delegat/api",
                ["apiKey"] = "oc-key-1"
            },
            Transport = new StubTransport((req, _) =>
            {
                calls.Add($"{req.Method} {req.Url.PathAndQuery}");
                if (req.Method == "POST" && req.Url.Query.Contains("action=prices.bulk", StringComparison.Ordinal))
                    return Task.FromResult(new IntegrationTransportResponse(404, new Dictionary<string, string>(), """{"error":"not_found"}"""));
                if (req.Method == "POST" && req.Url.Query.Contains("action=products.upsert", StringComparison.Ordinal))
                    return Task.FromResult(new IntegrationTransportResponse(200, new Dictionary<string, string>(), """{"success":true,"product_id":"42"}"""));
                if (req.Method == "GET" && req.Url.Query.Contains("action=products", StringComparison.Ordinal))
                    return Task.FromResult(new IntegrationTransportResponse(200, new Dictionary<string, string>(), """{"items":[{"external_id":"42","sku":"SKU-42","price":1500}],"has_more":false}"""));
                return Task.FromResult(new IntegrationTransportResponse(500, new Dictionary<string, string>(), ""));
            })
        };

        var prep = await plugin.PrepareAsync(new IntegrationPreparationInput(
            IntegrationOperationKind.PriceExport, conn,
            [new IntegrationItemIntent("item-42", new Dictionary<string, object?> { ["price"] = 1500m }, ExpectedRemoteId: "42")],
            null, "cmd-oc-1"));

        Assert.Empty(prep.Issues);
        var stage = Assert.Single(prep.Items.SelectMany(i => i.Stages));
        var attempt = new IntegrationAttemptContext("op-oc-1", "cmd-oc-1", "item-42", stage.StageKey, 1, "req-oc-1");

        var outcome = await plugin.ExecuteStageAsync(new IntegrationStageRequest(conn, attempt, stage));
        Assert.Equal(IntegrationStageOutcomeKind.Applied, outcome.Kind);
        Assert.Contains(calls, c => c.Contains("action=prices.bulk", StringComparison.Ordinal));
        Assert.Contains(calls, c => c.Contains("action=products.upsert", StringComparison.Ordinal));

        var confirmed = await plugin.ConfirmAsync(new IntegrationConfirmationRequest(conn, attempt, stage, outcome.Receipt));
        Assert.Equal(IntegrationStageOutcomeKind.Applied, confirmed.Kind);
    }

    [Fact]
    public async Task Ozon_ZeroOldPriceResetAllowed_NegativeRejected_AndAsyncLabelCompletes()
    {
        var plugin = new OzonPlugin();
        var conn = new ConnectionContext
        {
            ConnectionId = 22,
            AdapterKey = "ozon",
            Credentials = new Dictionary<string, string>
            {
                ["clientId"] = "12345",
                ["apiKey"] = "ozon-key-1"
            },
            Transport = new StubTransport((req, _) =>
            {
                if (req.Url.AbsolutePath == "/v3/posting/fbs/package-label/create")
                    return Task.FromResult(new IntegrationTransportResponse(200, new Dictionary<string, string>(),
                        """{"tasks":[{"task_id":9001,"task_type":"big_label"}]}"""));
                if (req.Url.AbsolutePath == "/v2/posting/fbs/package-label/get")
                    return Task.FromResult(new IntegrationTransportResponse(200, new Dictionary<string, string>(),
                        """{"status":{"code":"completed"},"file_url":"https://cdn.ozon.example/label-9001.pdf"}"""));
                return Task.FromResult(new IntegrationTransportResponse(404, new Dictionary<string, string>(), ""));
            })
        };

        var zeroResetPrep = await plugin.PrepareAsync(new IntegrationPreparationInput(
            IntegrationOperationKind.PriceExport, conn,
            [new IntegrationItemIntent("p-1", new Dictionary<string, object?> { ["price"] = 1500m, ["oldPrice"] = 0 }, ExpectedRemoteId: "555")],
            null, "cmd-oz-1"));
        Assert.Empty(zeroResetPrep.Issues);
        var priceStage = Assert.Single(zeroResetPrep.Items.SelectMany(i => i.Stages));
        Assert.Contains("\"old_price\":\"0\"", priceStage.PayloadJson);

        var negativePrep = await plugin.PrepareAsync(new IntegrationPreparationInput(
            IntegrationOperationKind.PriceExport, conn,
            [new IntegrationItemIntent("p-2", new Dictionary<string, object?> { ["price"] = 1500m, ["oldPrice"] = -5 }, ExpectedRemoteId: "555")],
            null, "cmd-oz-2"));
        Assert.Contains(negativePrep.Issues, i => i.Code == "invalid_price_format");

        var labelPrep = await plugin.PrepareAsync(new IntegrationPreparationInput(
            IntegrationOperationKind.ShipmentAction, conn,
            [new IntegrationItemIntent("ord-1", new Dictionary<string, object?> { ["postingNumber"] = "12345-0001-1" })],
            null, "cmd-oz-3"));
        Assert.Empty(labelPrep.Issues);
        var labelStage = Assert.Single(labelPrep.Items.SelectMany(i => i.Stages));
        var attempt = new IntegrationAttemptContext("op-oz-3", "cmd-oz-3", "ord-1", labelStage.StageKey, 1, "req-oz-3");

        var dispatched = await plugin.ExecuteStageAsync(new IntegrationStageRequest(conn, attempt, labelStage));
        Assert.Equal(IntegrationStageOutcomeKind.Accepted, dispatched.Kind);
        Assert.Equal("9001", dispatched.Receipt?.Value);

        var confirmed = await plugin.ConfirmAsync(new IntegrationConfirmationRequest(conn, attempt, labelStage, dispatched.Receipt));
        Assert.Equal(IntegrationStageOutcomeKind.Applied, confirmed.Kind);
    }

    [Fact]
    public async Task Wildberries_UnverifiedTrbxAssignRefusedAtPreflight_AndSandboxHostUsedForPriceUpload()
    {
        var plugin = new WildberriesPlugin();
        var seenHosts = new List<string>();
        var conn = new ConnectionContext
        {
            ConnectionId = 23,
            AdapterKey = "wildberries",
            Credentials = new Dictionary<string, string>
            {
                ["apiKey"] = "wb-sandbox-key",
                ["sandbox"] = "true"
            },
            Transport = new StubTransport((req, _) =>
            {
                seenHosts.Add(req.Url.Host);
                return Task.FromResult(new IntegrationTransportResponse(200, new Dictionary<string, string>(),
                    """{"data":{"id":7788,"alreadyExists":false}}"""));
            })
        };

        var refused = await plugin.PrepareAsync(new IntegrationPreparationInput(
            IntegrationOperationKind.ShipmentAction, conn,
            [new IntegrationItemIntent("ship-1", new Dictionary<string, object?>
            {
                ["action"] = "trbx_assign",
                ["supplyId"] = "WB-GI-1",
                ["trbxId"] = "WB-TRBX-1",
                ["orderIds"] = new[] { 101L }
            })], null, "cmd-wb-1"));
        Assert.NotEmpty(refused.Issues);
        Assert.Empty(seenHosts);

        var pricePrep = await plugin.PrepareAsync(new IntegrationPreparationInput(
            IntegrationOperationKind.PriceExport, conn,
            [new IntegrationItemIntent("wb-1", new Dictionary<string, object?> { ["price"] = 1200 }, ExpectedRemoteId: "998877")],
            null, "cmd-wb-2"));
        Assert.Empty(pricePrep.Issues);
        var priceStage = Assert.Single(pricePrep.Items.SelectMany(i => i.Stages));
        var attempt = new IntegrationAttemptContext("op-wb-2", "cmd-wb-2", "wb-1", priceStage.StageKey, 1, "req-wb-2");

        var outcome = await plugin.ExecuteStageAsync(new IntegrationStageRequest(conn, attempt, priceStage));
        Assert.Equal(IntegrationStageOutcomeKind.Accepted, outcome.Kind);
        Assert.Equal("7788", outcome.Receipt?.Value);
        Assert.All(seenHosts, h => Assert.Contains("sandbox", h, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WooCommerce_ProductUpsertUsesBridgeContractAndGuardsNonObjectJson()
    {
        var plugin = new WooCommercePlugin();
        var seenUrls = new List<string>();
        var conn = new ConnectionContext
        {
            ConnectionId = 24,
            AdapterKey = "woocommerce",
            Credentials = new Dictionary<string, string>
            {
                ["siteUrl"] = "https://woo.example.com",
                ["apiKey"] = "woo-key-1"
            },
            Transport = new StubTransport((req, _) =>
            {
                seenUrls.Add(req.Url.AbsolutePath);
                return Task.FromResult(new IntegrationTransportResponse(200, new Dictionary<string, string>(), "[]"));
            })
        };

        var prep = await plugin.PrepareAsync(new IntegrationPreparationInput(
            IntegrationOperationKind.ProductExport, conn,
            [new IntegrationItemIntent("woo-1", new Dictionary<string, object?>
            {
                ["sku"] = "WOO-SKU-1",
                ["name"] = "Sneakers",
                ["price"] = "2500",
                ["_imageUrls"] = new List<string> { "https://cdn.example/shoe.jpg" },
                ["_variants"] = new List<Dictionary<string, object>>
                {
                    new() { ["id"] = "v-42", ["sku"] = "WOO-SKU-1-42", ["tech_size"] = "42", ["price"] = 2500m, ["quantity"] = 5 }
                }
            })], null, "cmd-woo-1"));

        Assert.Empty(prep.Issues);
        var card = Assert.Single(prep.Items.SelectMany(i => i.Stages), s => s.Kind == IntegrationStageKind.Card);
        Assert.Contains("\"image_urls\"", card.PayloadJson);
        Assert.Contains("\"variants\"", card.PayloadJson);

        var attempt = new IntegrationAttemptContext("op-woo-1", "cmd-woo-1", "woo-1", card.StageKey, 1, "req-woo-1");
        var outcome = await plugin.ExecuteStageAsync(new IntegrationStageRequest(conn, attempt, card));

        Assert.Equal(IntegrationStageOutcomeKind.Unknown, outcome.Kind);
        Assert.Contains("/wp-json/delegat/v1/products/upsert", seenUrls);
    }

    [Fact]
    public async Task YandexMarket_PromoAcceptedConfirmedOnPartiallyAuto_And420IsRetryable()
    {
        var plugin = new YandexMarketPlugin();
        var attemptNo = 0;
        var conn = new ConnectionContext
        {
            ConnectionId = 25,
            AdapterKey = "yandex-market",
            Credentials = new Dictionary<string, string>
            {
                ["apiKey"] = "ym-key-1",
                ["businessId"] = "1001",
                ["campaignId"] = "2002"
            },
            Transport = new StubTransport((req, _) =>
            {
                if (req.Url.AbsolutePath.EndsWith("/promos/offers/update", StringComparison.Ordinal))
                {
                    attemptNo++;
                    if (attemptNo == 1)
                        return Task.FromResult(new IntegrationTransportResponse(420, new Dictionary<string, string>(), """{"status":"ERROR"}"""));
                    return Task.FromResult(new IntegrationTransportResponse(200, new Dictionary<string, string>(), """{"status":"OK","result":{}}"""));
                }
                if (req.Url.AbsolutePath.EndsWith("/promos/offers", StringComparison.Ordinal))
                {
                    return Task.FromResult(new IntegrationTransportResponse(200, new Dictionary<string, string>(),
                        """{"status":"OK","result":{"offers":[{"offerId":"OFF-1","status":"PARTIALLY_AUTO","params":{"discountParams":{"price":1000,"promoPrice":800}}}]}}"""));
                }
                return Task.FromResult(new IntegrationTransportResponse(404, new Dictionary<string, string>(), ""));
            })
        };

        var prep = await plugin.PrepareAsync(new IntegrationPreparationInput(
            IntegrationOperationKind.PromotionAction, conn,
            [new IntegrationItemIntent("ym-1", new Dictionary<string, object?>
            {
                ["promotionId"] = "PROMO-1",
                ["action"] = "add",
                ["mechanicsType"] = "DIRECT_DISCOUNT",
                ["price"] = 1000m,
                ["promoPrice"] = 800m
            }, ExpectedRemoteId: "OFF-1")], null, "cmd-ym-1"));

        Assert.Empty(prep.Issues);
        var stage = Assert.Single(prep.Items.SelectMany(i => i.Stages));
        var attempt = new IntegrationAttemptContext("op-ym-1", "cmd-ym-1", "ym-1", stage.StageKey, 1, "req-ym-1");

        var throttled = await plugin.ExecuteStageAsync(new IntegrationStageRequest(conn, attempt, stage));
        Assert.Equal(IntegrationStageOutcomeKind.Rejected, throttled.Kind);
        Assert.Contains(throttled.Issues ?? [], i => i.Retryable);

        var accepted = await plugin.ExecuteStageAsync(new IntegrationStageRequest(conn, attempt, stage));
        Assert.Equal(IntegrationStageOutcomeKind.Accepted, accepted.Kind);
        Assert.NotNull(accepted.Receipt);

        var confirmed = await plugin.ConfirmAsync(new IntegrationConfirmationRequest(conn, attempt, stage, accepted.Receipt));
        Assert.Equal(IntegrationStageOutcomeKind.Applied, confirmed.Kind);
    }
}
