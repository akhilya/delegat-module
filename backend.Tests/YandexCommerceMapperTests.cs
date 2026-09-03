using System.Text.Json;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Marketplace.YandexMarket;
using Delegat.Marketplace.YandexMarket.Models;

namespace Delegat.Tests;

public class YandexCommerceMapperTests
{
    [Theory]
    [InlineData("UNPAID", MarketplaceOrderStatuses.Unpaid)]
    [InlineData("PROCESSING", MarketplaceOrderStatuses.Confirmed)]
    [InlineData("DELIVERY", MarketplaceOrderStatuses.Delivering)]
    [InlineData("PICKUP", MarketplaceOrderStatuses.AwaitingDeliver)]
    [InlineData("DELIVERED", MarketplaceOrderStatuses.Delivered)]
    [InlineData("CANCELLED", MarketplaceOrderStatuses.Cancelled)]
    [InlineData("RETURNED", MarketplaceOrderStatuses.Returned)]
    public void MapStatus_NormalizesPartnerStatuses(string raw, string expected)
    {
        Assert.Equal(expected, YandexCommerceMapper.MapStatus(raw));
    }

    [Fact]
    public void ToNativeStatus_MapsPackAndCancel()
    {
        Assert.Equal(("PROCESSING", "STARTED"), YandexCommerceMapper.ToNativeStatus(MarketplaceOrderActions.Confirm));
        Assert.Equal(("PROCESSING", "READY_TO_SHIP"), YandexCommerceMapper.ToNativeStatus(MarketplaceOrderActions.Pack));
        Assert.Equal(("CANCELLED", "SHOP_FAILED"), YandexCommerceMapper.ToNativeStatus(MarketplaceOrderActions.Cancel));
        Assert.Equal(("DELIVERY", "READY_TO_SHIP"), YandexCommerceMapper.ToNativeStatus(MarketplaceOrderActions.Ship));
    }

    [Fact]
    public void FromOrder_UsesBuyerPriceAsDiscounted()
    {
        var order = YandexCommerceMapper.FromOrder(new YandexOrderDto
        {
            Id = 88,
            Status = "PROCESSING",
            Currency = "RUR",
            Total = 500,
            Items =
            [
                new YandexOrderItemDto { OfferId = "o-1", OfferName = "Hat", Count = 1, Price = 600, BuyerPrice = 500 }
            ]
        });

        Assert.Equal("88", order.ExternalId);
        Assert.Equal(MarketplaceOrderStatuses.Confirmed, order.Status);
        Assert.Equal(500m, order.Items[0].DiscountedPrice);
        Assert.Equal(600m, order.Items[0].Price);
    }

    [Fact]
    public void OrdersListResponse_ReadsRootOrdersWithoutResultEnvelope()
    {
        const string json = """
            {"pager":{"total":1,"currentPage":1,"pagesCount":1,"pageSize":50},"orders":[{"id":12,"status":"PROCESSING"}]}
            """;

        var parsed = JsonSerializer.Deserialize<YandexOrdersListResponse>(json);
        Assert.NotNull(parsed);
        var orders = parsed!.GetOrders();
        Assert.Single(orders);
        Assert.Equal(12, orders[0].Id);
        Assert.Equal(1, parsed.GetPager()?.Total);
    }

    [Fact]
    public void OrderEnvelope_ReadsRootOrder()
    {
        const string json = """{"order":{"id":12,"status":"DELIVERY"}}""";
        var parsed = JsonSerializer.Deserialize<YandexOrderEnvelope>(json);
        Assert.Equal(12, parsed!.GetOrder()?.Id);
        Assert.Equal("DELIVERY", parsed.GetOrder()?.Status);
    }

    [Fact]
    public void FromLabelBytes_WrapsPdf()
    {
        var pdf = "%PDF-1.4 yandex"u8.ToArray();
        var label = YandexCommerceMapper.FromLabelBytes(88, pdf, "application/pdf");

        Assert.NotNull(label);
        Assert.Equal("application/pdf", label!.ContentType);
        Assert.Equal("pdf", label.Format);
        Assert.Equal("yandex-88.pdf", label.FileName);
        Assert.Equal(pdf, Convert.FromBase64String(label.ContentBase64));
    }
}
