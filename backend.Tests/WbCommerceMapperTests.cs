using System.Globalization;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Marketplace.Wildberries;
using Delegat.Marketplace.Wildberries.Models;

namespace Delegat.Tests;

public class WbCommerceMapperTests
{
    [Theory]
    [InlineData("new", "waiting", MarketplaceOrderStatuses.AwaitingPack)]
    [InlineData("new", "", MarketplaceOrderStatuses.AwaitingPack)]
    [InlineData("confirm", "waiting", MarketplaceOrderStatuses.AwaitingDeliver)]
    [InlineData("complete", "waiting", MarketplaceOrderStatuses.Delivering)]
    [InlineData("complete", "sold", MarketplaceOrderStatuses.Delivered)]
    [InlineData("confirm", "sorted", MarketplaceOrderStatuses.Delivering)]
    [InlineData("complete", "ready_for_pickup", MarketplaceOrderStatuses.Delivering)]
    [InlineData("cancel", "canceled", MarketplaceOrderStatuses.Cancelled)]
    [InlineData("confirm", "canceled_by_client", MarketplaceOrderStatuses.Cancelled)]
    public void MapStatus_NormalizesSupplierAndWbStatus(string supplier, string wb, string expected)
    {
        Assert.Equal(expected, WbCommerceMapper.MapStatus(supplier, wb));
    }

    [Fact]
    public void MapStatus_NewAssemblyListWithoutPayloadStatus_IsAwaitingPack()
    {
        Assert.Equal(
            MarketplaceOrderStatuses.AwaitingPack,
            WbCommerceMapper.MapStatus(null, null, newAssemblyList: true));
    }

    [Theory]
    [InlineData("new")]
    [InlineData("unpaid")]
    [InlineData("confirmed")]
    [InlineData("returned")]
    public void IsEmptyWildberriesQueue_UnusedCabinetStatuses(string status)
    {
        Assert.True(WbCommerceMapper.IsEmptyWildberriesQueue(status));
    }

    [Fact]
    public void UsesNewAssemblyList_OnlyAwaitingPack()
    {
        Assert.True(WbCommerceMapper.UsesNewAssemblyList(MarketplaceOrderStatuses.AwaitingPack));
        Assert.False(WbCommerceMapper.UsesNewAssemblyList(MarketplaceOrderStatuses.New));
        Assert.False(WbCommerceMapper.UsesNewAssemblyList(MarketplaceOrderStatuses.Confirmed));
        Assert.False(WbCommerceMapper.UsesNewAssemblyList(MarketplaceOrderStatuses.AwaitingDeliver));
    }

    [Fact]
    public void MatchesListStatus_DoesNotPutConfirmInPackConfirmedFilter()
    {
        var packed = WbCommerceMapper.FromOrder(new WbOrder
        {
            Id = 1,
            SupplierStatus = "new"
        });
        var inSupply = WbCommerceMapper.FromOrder(new WbOrder
        {
            Id = 2,
            SupplierStatus = "confirm",
            WbStatus = "waiting"
        });

        Assert.True(WbCommerceMapper.MatchesListStatus(packed, MarketplaceOrderStatuses.AwaitingPack));
        Assert.False(WbCommerceMapper.MatchesListStatus(packed, MarketplaceOrderStatuses.New));
        Assert.False(WbCommerceMapper.MatchesListStatus(inSupply, MarketplaceOrderStatuses.Confirmed));
        Assert.True(WbCommerceMapper.MatchesListStatus(inSupply, MarketplaceOrderStatuses.AwaitingDeliver));
    }

    [Fact]
    public void PageItems_UsesSkipCursor()
    {
        var items = Enumerable.Range(1, 3)
            .Select(i => new ExternalOrder { ExternalId = i.ToString(CultureInfo.InvariantCulture) })
            .ToList();

        var page = WbCommerceMapper.PageItems(items, 2, "2");
        Assert.False(page.HasMore);
        Assert.Single(page.Items);
        Assert.Equal("3", page.Items[0].ExternalId);
    }

    [Fact]
    public void FromOrder_ConvertsKopecksAndAttachesNmId()
    {
        var order = WbCommerceMapper.FromOrder(new WbOrder
        {
            Id = 55,
            Rid = "rid-1",
            NmId = 1001,
            Price = 19900,
            ConvertedPrice = 19900,
            CurrencyCode = "RUB",
            Article = "ART-1",
            SupplierStatus = "new",
            WbStatus = "waiting",
            SupplyId = "WB-GI-1"
        });

        Assert.Equal("55", order.ExternalId);
        Assert.Equal(199m, order.Total);
        Assert.Equal("RUB", order.Currency);
        Assert.Equal(MarketplaceOrderStatuses.AwaitingPack, order.Status);
        Assert.Equal("WB-GI-1", WbCommerceMapper.SupplyIdOf(order));
        Assert.Equal("1001", order.Items[0].ExternalProductId);
        Assert.Equal("ART-1", order.Items[0].Sku);
        Assert.Contains("/1001/", order.Items[0].ImageUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void OrdersList_AcceptsNumericCurrencyCode()
    {
        const string json = """{"orders":[{"id":1,"currencyCode":643,"price":19900}],"next":0}""";
        var parsed = System.Text.Json.JsonSerializer.Deserialize<WbOrdersListResponse>(json);
        Assert.NotNull(parsed?.Orders);
        Assert.Equal("643", parsed.Orders[0].CurrencyCode);
        Assert.Equal("RUB", WbCommerceMapper.FromOrder(parsed.Orders[0]).Currency);
    }

    [Theory]
    [InlineData("RUB", "RUB")]
    [InlineData("usd", "USD")]
    [InlineData("398", "KZT")]
    [InlineData("", "RUB")]
    public void MapCurrency_NormalizesAlphaAndNumeric(string raw, string expected)
    {
        Assert.Equal(expected, WbCommerceMapper.MapCurrency(raw));
    }

    [Fact]
    public void MapPriceRow_CopiesDiscountAndDiscountedSizePrice()
    {
        var result = new List<PriceInfo>();
        WbCommerceMapper.MapPriceRow(new WbGoodsItem
        {
            NmId = 7,
            VendorCode = "VC",
            CurrencyCode = "RUB",
            Discount = 15,
            Sizes = [new WbGoodsSize { Price = 2000, DiscountedPrice = 1700 }]
        }, result);

        Assert.Single(result);
        Assert.Equal("7", result[0].ExternalProductId);
        Assert.Equal(1700m, result[0].Price);
        Assert.Equal(2000m, result[0].OldPrice);
        Assert.Equal(1700m, result[0].DiscountedPrice);
        Assert.Equal(15, result[0].Discount);
        Assert.Equal("VC", result[0].ExternalSku);
    }

    [Fact]
    public void FromSticker_MapsBarcodeAndPng()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var label = WbCommerceMapper.FromSticker(new WbSticker
        {
            OrderId = 5346346,
            PartA = "231648",
            PartB = "9753",
            Barcode = "!uKEtQZVx",
            File = Convert.ToBase64String(png)
        });

        Assert.NotNull(label);
        Assert.Equal("image/png", label!.ContentType);
        Assert.Equal("png", label.Format);
        Assert.Equal("!uKEtQZVx", label.Barcode);
        Assert.Equal("wb-5346346.png", label.FileName);
        Assert.Equal("231648", label.Extra!["partA"]);
        Assert.Equal(png, Convert.FromBase64String(label.ContentBase64));
    }

    [Fact]
    public void FromSticker_EmptyFile_ReturnsNull()
    {
        Assert.Null(WbCommerceMapper.FromSticker(new WbSticker { OrderId = 1, File = "" }));
    }
}
