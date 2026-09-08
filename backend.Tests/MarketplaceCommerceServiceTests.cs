using Delegat.Marketplace.SDK;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Services.Marketplace;
using HostCaps = Delegat.Services.Marketplace.MarketplaceCapabilities;
using SdkCaps = Delegat.Marketplace.SDK.MarketplaceCapabilities;

namespace Delegat.Tests;

public class MarketplaceCommerceServiceTests
{
    [Fact]
    public void BuildOrderQuery_CopiesFilters()
    {
        var from = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var to = DateTimeOffset.Parse("2026-01-31T00:00:00Z");
        var query = MarketplaceCommerceService.BuildOrderQuery(
            2, 25, "cursor-1", MarketplaceOrderStatuses.New, from, to, "fbs", "123");

        Assert.Equal(2, query.Page);
        Assert.Equal(25, query.PageSize);
        Assert.Equal("cursor-1", query.Cursor);
        Assert.Equal(MarketplaceOrderStatuses.New, query.Status);
        Assert.Equal(from, query.From);
        Assert.Equal(to, query.To);
        Assert.Equal("fbs", query.Channel);
        Assert.Equal("123", query.Search);
    }

    [Fact]
    public void CabinetOrderEndpoints_RequireOrdersManage()
    {
        var src = File.ReadAllText(FindRepoFile("backend", "Controllers", "MarketplaceCommerceController.cs"))
            .Replace("\r\n", "\n");
        Assert.Contains(
            "[RequiresPermission(Permissions.OrdersManage, requireStoreMembership: true)]\n    public async Task<IActionResult> ListStoreOrders",
            src);
        Assert.Contains(
            "[RequiresPermission(Permissions.OrdersManage, requireStoreMembership: true)]\n    public async Task<IActionResult> SyncStoreOrders",
            src);
        Assert.Contains(
            "[RequiresPermission(Permissions.OrdersManage, requireStoreMembership: true)]\n    public async Task<IActionResult> SyncConnectionOrders",
            src);
        Assert.Contains(
            "[RequiresPermission(Permissions.OrdersManage, requireStoreMembership: true)]\n    public async Task<IActionResult> ListOrders",
            src);
        Assert.Contains(
            "[RequiresPermission(Permissions.OrdersManage, requireStoreMembership: true)]\n    public async Task<IActionResult> UpdateOrderStatus",
            src);
        Assert.Contains(
            "[RequiresPermission(Permissions.OrdersManage, requireStoreMembership: true)]\n    public async Task<IActionResult> GetOrderLabel",
            src);
        Assert.Contains(
            "[RequiresPermission(Permissions.OrdersManage, requireStoreMembership: true)]\n    public async Task<IActionResult> ListFulfillmentLocations",
            src);
        Assert.Contains(
            "[RequiresPermission(Permissions.OrdersManage, requireStoreMembership: true)]\n    public async Task<IActionResult> CreateShipment",
            src);
        Assert.Contains(
            "[RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]\n    public async Task<IActionResult> ListPrices",
            src);
        Assert.Contains(
            "[RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]\n    public async Task<IActionResult> ListPromotions",
            src);
    }

    private static string FindRepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join('/', parts));
    }

    [Fact]
    public void BuildShipmentQuery_CopiesFilters()
    {
        var query = MarketplaceCommerceService.BuildShipmentQuery(25, "13833711", true);
        Assert.Equal(25, query.Limit);
        Assert.Equal("13833711", query.Cursor);
        Assert.True(query.OpenOnly);
    }

    [Fact]
    public void HostAndSdkPromotionFlags_ShareTheSameBits()
    {
        Assert.Equal((int)SdkCaps.GetOrders, (int)HostCaps.GetOrders);
        Assert.Equal((int)SdkCaps.UpdateOrderStatus, (int)HostCaps.UpdateOrderStatus);
        Assert.Equal((int)SdkCaps.GetPromotions, (int)HostCaps.GetPromotions);
        Assert.Equal((int)SdkCaps.UpdatePromotions, (int)HostCaps.UpdatePromotions);
        Assert.Equal((int)SdkCaps.GetOrderLabels, (int)HostCaps.GetOrderLabels);
        Assert.Equal((int)SdkCaps.GetFulfillmentLocations, (int)HostCaps.GetFulfillmentLocations);
        Assert.Equal((int)SdkCaps.ManageShipments, (int)HostCaps.ManageShipments);
        Assert.Equal(1 << 22, (int)SdkCaps.GetPromotions);
        Assert.Equal(1 << 23, (int)SdkCaps.UpdatePromotions);
        Assert.Equal(1 << 24, (int)SdkCaps.GetOrderLabels);
        Assert.Equal(1 << 25, (int)SdkCaps.GetFulfillmentLocations);
        Assert.Equal(1 << 26, (int)SdkCaps.ManageShipments);
    }

    [Fact]
    public void ApplyLocalOrderItemImages_FillsMissingFromLinkKeys()
    {
        var order = new ExternalOrder
        {
            Items =
            [
                new ExternalOrderItem { ExternalProductId = "nm-1", Sku = "ART" },
                new ExternalOrderItem
                {
                    ExternalProductId = "keep",
                    ImageUrl = "https://cdn.example/already.webp"
                }
            ]
        };

        MarketplaceCommerceService.ApplyLocalOrderItemImages(
            [order],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ART"] = "/api/files/9/thumbnail?w=256"
            });

        Assert.Equal("/api/files/9/thumbnail?w=256", order.Items[0].ImageUrl);
        Assert.Equal("https://cdn.example/already.webp", order.Items[1].ImageUrl);
    }

    [Fact]
    public void ApplyLocalOrderItemNames_FillsMissingFromLinkKeys()
    {
        var order = new ExternalOrder
        {
            Items =
            [
                new ExternalOrderItem { ExternalProductId = "1001", Sku = "ART" },
                new ExternalOrderItem
                {
                    ExternalProductId = "keep",
                    Name = "Already titled"
                }
            ]
        };

        MarketplaceCommerceService.ApplyLocalOrderItemNames(
            [order],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["1001"] = "Local miniatures"
            });

        Assert.Equal("Local miniatures", order.Items[0].Name);
        Assert.Equal("Already titled", order.Items[1].Name);
    }

    [Fact]
    public void NeedsLocalOrderItemLookup_WhenNameAndImageExistButProductIdMissing()
    {
        var named = new ExternalOrderItem
        {
            ExternalProductId = "nm-1",
            Name = "Cabinet title",
            ImageUrl = "https://cdn.example/card.webp"
        };
        var linked = new ExternalOrderItem
        {
            ExternalProductId = "nm-2",
            Name = "Cabinet title",
            ImageUrl = "https://cdn.example/card.webp",
            LocalProductId = 42
        };

        Assert.True(MarketplaceCommerceService.NeedsLocalOrderItemLookup(named));
        Assert.False(MarketplaceCommerceService.NeedsLocalOrderItemLookup(linked));
        Assert.True(MarketplaceCommerceService.NeedsLocalOrderItemLookup(
            new ExternalOrderItem { LocalProductId = 7, Name = "x" }));
    }

    [Fact]
    public void ApplyLocalOrderItemProductIds_FillsMissingEvenWhenNamed()
    {
        var order = new ExternalOrder
        {
            Items =
            [
                new ExternalOrderItem
                {
                    ExternalProductId = "nm-1",
                    Sku = "ART",
                    Name = "Cabinet title",
                    ImageUrl = "https://cdn.example/card.webp"
                },
                new ExternalOrderItem
                {
                    OfferId = "keep",
                    LocalProductId = 9
                }
            ]
        };

        MarketplaceCommerceService.ApplyLocalOrderItemProductIds(
            [order],
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["ART"] = 15,
                ["keep"] = 99
            });

        Assert.Equal(15, order.Items[0].LocalProductId);
        Assert.Equal(9, order.Items[1].LocalProductId);
    }
}
