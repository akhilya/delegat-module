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

    [Theory]
    [InlineData("new", "UNPAID")]
    [InlineData("NEW", "UNPAID")]
    [InlineData("unpaid", "UNPAID")]
    [InlineData("awaiting_pack", "PROCESSING")]
    [InlineData("AWAITING_PACK", "PROCESSING")]
    [InlineData("confirmed", "PROCESSING")]
    [InlineData("awaiting_deliver", "PICKUP")]
    [InlineData("delivering", "DELIVERY")]
    [InlineData("PROCESSING", "PROCESSING")]
    public void ToListStatus_MapsCabinetFiltersToPartnerStatuses(string status, string expected)
    {
        Assert.Equal(expected, YandexCommerceMapper.ToListStatus(status));
    }

    [Fact]
    public void ToListStatus_DoesNotSendUnknownCabinetValues()
    {
        Assert.Null(YandexCommerceMapper.ToListStatus("nope"));
        Assert.Null(YandexCommerceMapper.ToListStatus(""));
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
        Assert.Null(order.Items[0].ImageUrl);
    }

    [Fact]
    public void ApplyOfferPictures_FillsFromFirstPictureByOfferId()
    {
        var order = YandexCommerceMapper.FromOrder(new YandexOrderDto
        {
            Id = 1,
            Status = "PROCESSING",
            Items =
            [
                new YandexOrderItemDto { OfferId = "hat-1", OfferName = "Hat", Count = 1, Price = 100 },
                new YandexOrderItemDto { OfferId = "keep", OfferName = "Kept", Count = 1, Price = 1 }
            ]
        });
        order.Items[1].ImageUrl = "https://cdn.example/already.webp";

        YandexCommerceMapper.ApplyOfferPictures(
            [order],
            [
                new GetOfferMappingDto
                {
                    Offer = new GetOfferDto
                    {
                        OfferId = "hat-1",
                        Pictures =
                        [
                            "https://avatars.mds.yandex.net/get-mpic/a.jpg",
                            "https://avatars.mds.yandex.net/get-mpic/b.jpg"
                        ]
                    }
                }
            ]);

        Assert.Equal("https://avatars.mds.yandex.net/get-mpic/a.jpg", order.Items[0].ImageUrl);
        Assert.Equal("https://cdn.example/already.webp", order.Items[1].ImageUrl);
    }

    [Fact]
    public void ApplyOfferPictures_MatchesSkuWhenOfferIdMissing()
    {
        var order = new ExternalOrder
        {
            Items = [new ExternalOrderItem { Sku = "sku-9" }]
        };

        YandexCommerceMapper.ApplyOfferPictures(
            [order],
            [
                new GetOfferMappingDto
                {
                    Offer = new GetOfferDto
                    {
                        OfferId = "sku-9",
                        Pictures = ["https://avatars.mds.yandex.net/get-mpic/c.jpg"]
                    }
                }
            ]);

        Assert.Equal("https://avatars.mds.yandex.net/get-mpic/c.jpg", order.Items[0].ImageUrl);
    }

    [Fact]
    public void ApplyOfferPictures_PrefixesProtocolRelativeUrls()
    {
        var order = new ExternalOrder
        {
            Items = [new ExternalOrderItem { OfferId = "hat-1" }]
        };

        YandexCommerceMapper.ApplyOfferPictures(
            [order],
            [
                new GetOfferMappingDto
                {
                    Offer = new GetOfferDto
                    {
                        OfferId = "hat-1",
                        Pictures = ["//avatars.mds.yandex.net/get-mpic/d.jpg"]
                    }
                }
            ]);

        Assert.Equal("https://avatars.mds.yandex.net/get-mpic/d.jpg", order.Items[0].ImageUrl);
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
    public void OrdersListResponse_ReadsYandexUpdatedAtDateString()
    {
        const string json = """
            {"orders":[{"id":12,"status":"DELIVERY","updatedAt":"04-09-2026 17:36:11"}]}
            """;

        var parsed = JsonSerializer.Deserialize<YandexOrdersListResponse>(
            json,
            YandexRequestChunking.JsonOptions);
        Assert.NotNull(parsed);
        var order = YandexCommerceMapper.FromOrder(Assert.Single(parsed!.GetOrders()));
        Assert.Equal(
            new DateTimeOffset(2026, 9, 4, 17, 36, 11, TimeSpan.Zero),
            order.UpdatedAt);
    }

    [Fact]
    public void OrderEnvelope_ReadsRootOrder()
    {
        const string json = """{"order":{"id":12,"status":"DELIVERY"}}""";
        var parsed = JsonSerializer.Deserialize<YandexOrderEnvelope>(json);
        Assert.Equal(12, parsed!.GetOrder()?.Id);
        Assert.Equal("DELIVERY", parsed.GetOrder()?.Status);
    }

    [Theory]
    [InlineData(MarketplaceOrderStatuses.Confirmed, "fbs", true)]
    [InlineData(MarketplaceOrderStatuses.AwaitingPack, "dbs", true)]
    [InlineData(MarketplaceOrderStatuses.Unpaid, "fbs", false)]
    [InlineData(MarketplaceOrderStatuses.Delivering, "fbs", false)]
    [InlineData(MarketplaceOrderStatuses.Confirmed, "fby", false)]
    public void CanGetOrderLabel_WhilePackingNotFby(string status, string channel, bool expected)
    {
        Assert.Equal(expected, YandexCommerceMapper.CanGetOrderLabel(status, channel));
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
