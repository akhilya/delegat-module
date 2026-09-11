using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Delegat.Marketplace.SDK;
using Delegat.Marketplace.SDK.DTOs;

namespace Delegat.Marketplace.Medusa.Tests;

public class MedusaPluginTests
{
    [Fact]
    public async Task TestConnection_HealthOk_ReturnsSuccess()
    {
        var handler = new FakeHandler(_ =>
            JsonResponse(new
            {
                success = true,
                store_name = "Shop",
                medusa_version = "2.20.1",
                api_version = "1.0.0",
                capabilities = new[] { "products.upsert" }
            }));

        var plugin = new TestPlugin(handler);
        var result = await plugin.TestConnectionAsync(Context());

        Assert.True(result.Success);
        Assert.Equal("Shop", result.Details?["storeName"]?.ToString());
    }

    [Fact]
    public void Capabilities_IncludeCatalogSyncAndOrders()
    {
        var plugin = new MedusaPlugin();
        Assert.True(plugin.Capabilities.HasFlag(MarketplaceCapabilities.ExportProduct));
        Assert.True(plugin.Capabilities.HasFlag(MarketplaceCapabilities.ExportProductBulk));
        Assert.True(plugin.Capabilities.HasFlag(MarketplaceCapabilities.CreateCategory));
        Assert.True(plugin.Capabilities.HasFlag(MarketplaceCapabilities.ExportPrices));
        Assert.True(plugin.Capabilities.HasFlag(MarketplaceCapabilities.ExportStocks));
        Assert.True(plugin.Capabilities.HasFlag(MarketplaceCapabilities.EndpointBasedGrouping));
        Assert.True(plugin.Capabilities.HasFlag(MarketplaceCapabilities.ImportGroups));
        Assert.True(plugin.Capabilities.HasFlag(MarketplaceCapabilities.GetOrders));
        Assert.True(plugin.Capabilities.HasFlag(MarketplaceCapabilities.UpdateOrderStatus));
    }

    [Fact]
    public void Credentials_RequireBridgeUrlAndApiKey()
    {
        var schema = new MedusaPlugin().GetCredentialsSchema();
        Assert.Contains(schema.Fields, f => f.Key == "apiBaseUrl" && f.IsRequired);
        Assert.Contains(schema.Fields, f => f.Key == "apiKey" && f.IsRequired && f.IsSecret);
    }

    [Fact]
    public async Task ExportProduct_Simple_PostsUpsertWithImages()
    {
        string? body = null;
        AuthenticationHeaderValue? auth = null;
        var handler = new FakeHandler(req =>
        {
            auth = req.Headers.Authorization;
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Contains("/products/upsert", req.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(new { success = true, product_id = "prod_42" });
        });

        var plugin = new TestPlugin(handler);
        var result = await plugin.ExportProductAsync(Context(), new Dictionary<string, object>
        {
            ["name"] = "Miniature Knight",
            ["sku"] = "KNIGHT-01",
            ["price"] = 1500m,
            ["quantity"] = 3,
            ["_categoryId"] = "pcat_7",
            ["_imageUrls"] = new List<string> { "https://cdn.example/knight.jpg" },
            ["delegat_product_id"] = "99"
        });

        Assert.True(result.Success);
        Assert.Equal("prod_42", result.ExternalProductId);
        Assert.Equal("Basic", auth?.Scheme);
        using var doc = JsonDocument.Parse(body!);
        var root = doc.RootElement;
        Assert.Equal("Miniature Knight", root.GetProperty("name").GetString());
        Assert.Equal("KNIGHT-01", root.GetProperty("sku").GetString());
        Assert.Equal("pcat_7", root.GetProperty("category_ids")[0].GetString());
        Assert.Equal("99", root.GetProperty("delegat_product_id").GetString());
        Assert.Equal("https://cdn.example/knight.jpg", root.GetProperty("image_urls")[0].GetString());
    }

    [Fact]
    public async Task ExportProduct_NewIdAfterWipe_SetsResolvedFrom()
    {
        var handler = new FakeHandler(_ =>
            JsonResponse(new { success = true, product_id = "prod_NEW" }));

        var plugin = new TestPlugin(handler);
        var result = await plugin.ExportProductAsync(
            Context(),
            new Dictionary<string, object> { ["name"] = "Cerberus", ["sku"] = "MI9" },
            existingExternalId: "prod_OLD");

        Assert.True(result.Success);
        Assert.Equal("prod_NEW", result.ExternalProductId);
        Assert.Equal("prod_OLD", result.Details?["resolvedFrom"]?.ToString());
    }

    [Fact]
    public async Task ExportProductsBulk_NewIdAfterWipe_SetsResolvedFrom()
    {
        var handler = new FakeHandler(_ =>
            JsonResponse(new
            {
                success = true,
                results = new[]
                {
                    new { index = 0, success = true, product_id = "prod_NEW", local_product_id = 12 }
                }
            }));

        var plugin = new TestPlugin(handler);
        var result = await plugin.ExportProductsBulkAsync(Context(), [
            new BulkExportItem(12, new Dictionary<string, object> { ["name"] = "Cerberus" }, "prod_OLD")
        ]);

        var item = Assert.Single(result.Items!);
        Assert.True(item.Success);
        Assert.Equal("prod_NEW", item.ExternalProductId);
        Assert.Equal("prod_OLD", item.Details?["resolvedFrom"]?.ToString());
    }

    [Fact]
    public async Task ExportProduct_UsesFieldLabelsAndJoinsListAttributeValues()
    {
        string? body = null;
        var handler = new FakeHandler(req =>
        {
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(new { success = true, product_id = "prod_55" });
        });

        var plugin = new TestPlugin(handler);
        var result = await plugin.ExportProductAsync(Context(), new Dictionary<string, object>
        {
            ["name"] = "Chang'e Bust",
            ["sku"] = "BUST-01",
            ["price"] = 1000m,
            ["4975"] = new List<string> { "Китайская мифология", "Фэнтези" },
            ["21837"] = "100мм",
            ["color"] = "61576",
            ["_fieldLabels"] = new Dictionary<string, string>
            {
                ["4975"] = "Жанр",
                ["21837"] = "Масштаб",
                ["color"] = "Цвет товара",
            },
            ["_fieldOptions"] = new Dictionary<string, Dictionary<string, string>>
            {
                ["color"] = new Dictionary<string, string>
                {
                    ["61576"] = "Серебристый",
                }
            }
        });

        Assert.True(result.Success);
        using var doc = JsonDocument.Parse(body!);
        var attrs = doc.RootElement.GetProperty("attributes");
        Assert.Equal("Китайская мифология, Фэнтези", attrs.GetProperty("Жанр").GetString());
        Assert.Equal("100мм", attrs.GetProperty("Масштаб").GetString());
        Assert.Equal("Серебристый", attrs.GetProperty("Цвет товара").GetString());
    }

    [Fact]
    public async Task ExportProduct_PassesShopSpecsFromPim()
    {
        string? body = null;
        var handler = new FakeHandler(req =>
        {
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(new { success = true, product_id = "prod_specs" });
        });

        var plugin = new TestPlugin(handler);
        var result = await plugin.ExportProductAsync(Context(), new Dictionary<string, object>
        {
            ["name"] = "Nahida",
            ["sku"] = "GE32",
            ["price"] = 900m,
            ["color"] = "green",
            ["_shopSpecs"] = new List<object>
            {
                new Dictionary<string, object>
                {
                    ["key"] = "material",
                    ["label"] = "Материал",
                    ["value"] = "Фотополимер"
                },
                new Dictionary<string, object>
                {
                    ["key"] = "height",
                    ["label"] = "Высота",
                    ["value"] = "75 мм"
                }
            }
        });

        Assert.True(result.Success);
        using var doc = JsonDocument.Parse(body!);
        var specs = doc.RootElement.GetProperty("specs");
        Assert.Equal(2, specs.GetArrayLength());
        Assert.Equal("Материал", specs[0].GetProperty("label").GetString());
        Assert.Equal("Фотополимер", specs[0].GetProperty("value").GetString());
        Assert.False(doc.RootElement.GetProperty("attributes").TryGetProperty("_shopSpecs", out _));
    }

    [Fact]
    public async Task ExportProduct_UsesLocalGroupIdAndName()
    {
        string? body = null;
        var handler = new FakeHandler(req =>
        {
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(new { success = true, product_id = "prod_g" });
        });

        var plugin = new TestPlugin(handler);
        var result = await plugin.ExportProductAsync(Context(), new Dictionary<string, object>
        {
            ["name"] = "Nahida",
            ["sku"] = "GE32",
            ["price"] = 900m,
            ["_localGroupId"] = "12",
            ["_localGroupName"] = "Genshin Impact",
        });

        Assert.True(result.Success);
        using var doc = JsonDocument.Parse(body!);
        Assert.Equal("12", doc.RootElement.GetProperty("group_id").GetString());
        Assert.Equal("Genshin Impact", doc.RootElement.GetProperty("group_name").GetString());
        Assert.False(doc.RootElement.GetProperty("attributes").TryGetProperty("_localGroupId", out _));
    }

    [Fact]
    public async Task ExportProduct_EmptyMappedGroupId_FallsBackToLocalGroup()
    {
        string? body = null;
        var handler = new FakeHandler(req =>
        {
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(new { success = true, product_id = "prod_g" });
        });

        var plugin = new TestPlugin(handler);
        var result = await plugin.ExportProductAsync(Context(), new Dictionary<string, object>
        {
            ["name"] = "Nahida",
            ["sku"] = "GE32",
            ["groupId"] = "",
            ["_localGroupId"] = "16",
            ["_localGroupName"] = "Genshin Impact",
        });

        Assert.True(result.Success);
        using var doc = JsonDocument.Parse(body!);
        Assert.Equal("16", doc.RootElement.GetProperty("group_id").GetString());
        Assert.Equal("Genshin Impact", doc.RootElement.GetProperty("group_name").GetString());
    }

    [Fact]
    public async Task ExportProduct_WithVariants_IncludesVariantPayload()
    {
        string? body = null;
        var handler = new FakeHandler(req =>
        {
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(new
            {
                success = true,
                product_id = "prod_100",
                variant_ids = new Dictionary<string, string> { ["v1"] = "variant_101", ["v2"] = "variant_102" }
            });
        });

        var plugin = new TestPlugin(handler);
        var result = await plugin.ExportProductAsync(Context(), new Dictionary<string, object>
        {
            ["name"] = "Sized Figure",
            ["sku"] = "FIG-S",
            ["price"] = 900m,
            ["_variants"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["VariantId"] = "v1",
                    ["TechSize"] = "S",
                    ["Price"] = 900m,
                    ["Quantity"] = 2
                },
                new Dictionary<string, object?>
                {
                    ["VariantId"] = "v2",
                    ["TechSize"] = "L",
                    ["Price"] = 1100m,
                    ["Quantity"] = 1
                }
            }
        }, existingExternalId: "prod_100");

        Assert.True(result.Success);
        using var doc = JsonDocument.Parse(body!);
        var variants = doc.RootElement.GetProperty("variants");
        Assert.Equal(2, variants.GetArrayLength());
        Assert.Equal("v1", variants[0].GetProperty("variant_id").GetString());
        Assert.Equal("S", variants[0].GetProperty("tech_size").GetString());
    }

    [Fact]
    public async Task CreateCategory_PostsUpsertAndMergesFields()
    {
        string? body = null;
        var handler = new FakeHandler(req =>
        {
            Assert.Contains("/categories/upsert", req.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(new { success = true, category_id = "pcat_15", name = "Miniatures" });
        });

        var plugin = new TestPlugin(handler);
        var result = await plugin.CreateCategoryAsync(Context(), new CreateCategoryRequest
        {
            Name = "Miniatures",
            Description = "Resin figures",
            Fields =
            [
                new CreateCategoryField
                {
                    Key = "material",
                    Name = "Material",
                    Type = "string",
                    IsRequired = true,
                    Group = "Attributes",
                    SortOrder = 10
                }
            ]
        });

        Assert.True(result.Success);
        Assert.Equal("pcat_15", result.ExternalCategoryId);
        Assert.Contains(result.Fields!, f => f.Key == "sku" && f.IsRequired);
        Assert.Contains(result.Fields!, f => f.Key == "material" && f.Name == "Material" && !f.IsRequired);
        using var doc = JsonDocument.Parse(body!);
        Assert.Equal("Miniatures", doc.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetCategoryFields_KeepsBridgeFieldsWhenSuccessOmitted()
    {
        var handler = new FakeHandler(req =>
        {
            Assert.Contains("/categories/pcat_15/fields", req.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
            return JsonResponse(new
            {
                fields = new[]
                {
                    new { key = "material", name = "Material", type = "string", group = "Attributes", sort_order = 10 }
                }
            });
        });

        var fields = await new TestPlugin(handler).GetCategoryFieldsAsync(Context(), "pcat_15");
        Assert.Contains(fields, f => f.Key == "material" && f.Name == "Material");
        Assert.Contains(fields, f => f.Key == "sku");
    }

    [Fact]
    public async Task GetPriceFields_ReturnsPriceAndSalePrice()
    {
        var fields = await new MedusaPlugin().GetPriceFieldsAsync(Context(), "pcat_15");
        Assert.Contains(fields, f => f.Key == "price" && f.IsRequired);
        Assert.Contains(fields, f => f.Key == "sale_price" && !f.IsRequired);
    }

    [Fact]
    public async Task ExportPrices_PostsBulkPayload()
    {
        string? body = null;
        var handler = new FakeHandler(req =>
        {
            Assert.Contains("/prices/bulk", req.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(new { success = true });
        });

        var plugin = new TestPlugin(handler);
        var result = await plugin.ExportPricesAsync(Context(),
        [
            new PriceInfo("prod_1", 800m, OldPrice: 1000m, Currency: "RUB")
        ]);

        Assert.True(result.IsSuccess);
        using var doc = JsonDocument.Parse(body!);
        var item = doc.RootElement.GetProperty("prices")[0];
        Assert.Equal("prod_1", item.GetProperty("product_id").GetString());
        Assert.Equal(1000, item.GetProperty("price").GetDecimal());
        Assert.Equal(800, item.GetProperty("sale_price").GetDecimal());
    }

    [Fact]
    public async Task ExportStocks_PostsBulkPayload()
    {
        string? body = null;
        var handler = new FakeHandler(req =>
        {
            Assert.Contains("/stocks/bulk", req.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(new { success = true });
        });

        var plugin = new TestPlugin(handler);
        var result = await plugin.ExportStocksAsync(Context(),
        [
            new StockInfo("prod_1", 4, VariantId: "variant_9")
        ]);

        Assert.True(result.IsSuccess);
        using var doc = JsonDocument.Parse(body!);
        var item = doc.RootElement.GetProperty("stocks")[0];
        Assert.Equal("prod_1", item.GetProperty("product_id").GetString());
        Assert.Equal("variant_9", item.GetProperty("variant_id").GetString());
        Assert.Equal(4, item.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task ImportProduct_MapsCategoryIdAndName()
    {
        var handler = new FakeHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Contains("/products/prod_42", req.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
            return JsonResponse(new
            {
                success = true,
                product_id = "prod_42",
                sku = "KNIGHT-01",
                name = "Knight",
                description = "Resin",
                price = 1500,
                quantity = 3,
                status = "published",
                image_urls = Array.Empty<string>(),
                category_id = "pcat_15",
                category_name = "Miniatures",
            });
        });

        var product = await new TestPlugin(handler).ImportProductAsync(Context(), "prod_42");
        Assert.NotNull(product);
        Assert.Equal("prod_42", product!.ExternalId);
        Assert.Equal("pcat_15", product.CategoryId);
        Assert.Equal("Miniatures", product.CategoryName);
        Assert.Equal(1500m, product.Price);
    }

    [Fact]
    public async Task CreateGroup_PostsUpsertWithNameAndProductIds()
    {
        string? body = null;
        var handler = new FakeHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Contains("/groups/upsert", req.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(new { success = true, collection_id = "pcol_1", name = "Cerberus" });
        });

        var plugin = new TestPlugin(handler);
        var result = await plugin.CreateGroupAsync(
            Context(),
            ["prod_a", "prod_b"],
            "Cerberus");

        Assert.True(result.Success);
        Assert.Equal("pcol_1", result.ExternalGroupId);
        Assert.Contains("Cerberus", body);
        Assert.Contains("prod_a", body);
    }

    [Fact]
    public async Task GetOrders_MapsBridgePayload()
    {
        var handler = new FakeHandler(req =>
        {
            Assert.Contains("/orders?", req.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
            return JsonResponse(new
            {
                success = true,
                has_more = false,
                items = new[]
                {
                    new
                    {
                        external_id = "order_1",
                        display_id = "12",
                        status = "awaiting_pack",
                        customer_email = "a@test.ru",
                        total = 1500,
                        items = new[]
                        {
                            new { sku = "WA150", name = "Фигурка", quantity = 1, price = 1500 }
                        }
                    }
                }
            });
        });

        var page = await new TestPlugin(handler).GetOrdersAsync(Context(), new OrderQuery { Page = 1, PageSize = 20 });
        var order = Assert.Single(page.Items);
        Assert.Equal("order_1", order.ExternalId);
        Assert.Equal("12", order.DisplayId);
        Assert.Equal("awaiting_pack", order.Status);
        Assert.Equal("WA150", order.Items[0].Sku);
    }

    [Fact]
    public async Task UpdateOrderStatus_ShipsViaBridge()
    {
        var handler = new FakeHandler(req =>
        {
            Assert.Contains("/orders/order_1/status", req.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
            Assert.Equal(HttpMethod.Post, req.Method);
            return JsonResponse(new
            {
                success = true,
                order = new { external_id = "order_1", status = "delivering" }
            });
        });

        var result = await new TestPlugin(handler).UpdateOrderStatusAsync(
            Context(),
            "order_1",
            new OrderStatusUpdate { Action = MarketplaceOrderActions.Ship });

        Assert.True(result.Success);
        Assert.Equal("delivering", result.Order?.Status);
    }

    private static ConnectionContext Context() => new()
    {
        ConnectionId = 1,
        Credentials = new Dictionary<string, string>
        {
            ["apiBaseUrl"] = "http://localhost:9000/admin/delegat",
            ["apiKey"] = "sk_test"
        }
    };

    private static HttpResponseMessage JsonResponse(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class TestPlugin : MedusaPlugin
    {
        private readonly HttpMessageHandler _handler;

        public TestPlugin(HttpMessageHandler handler) => _handler = handler;

        protected override MedusaApiClient CreateClient(ConnectionContext context, HttpClient? httpClient = null)
        {
            var http = new HttpClient(_handler) { Timeout = TimeSpan.FromSeconds(30) };
            return base.CreateClient(context, http);
        }
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }
}
