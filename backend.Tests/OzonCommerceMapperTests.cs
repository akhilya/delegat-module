using System.Globalization;
using System.Text;
using System.Text.Json;
using Delegat.Marketplace.Ozon;
using Delegat.Marketplace.Ozon.Models;
using Delegat.Marketplace.SDK.DTOs;

namespace Delegat.Tests;

public class OzonCommerceMapperTests
{
    [Theory]
    [InlineData("awaiting_approve", MarketplaceOrderStatuses.New)]
    [InlineData("awaiting_packaging", MarketplaceOrderStatuses.AwaitingPack)]
    [InlineData("awaiting_registration", MarketplaceOrderStatuses.Confirmed)]
    [InlineData("arbitration", MarketplaceOrderStatuses.Confirmed)]
    [InlineData("awaiting_deliver", MarketplaceOrderStatuses.AwaitingDeliver)]
    [InlineData("delivering", MarketplaceOrderStatuses.Delivering)]
    [InlineData("delivered", MarketplaceOrderStatuses.Delivered)]
    [InlineData("cancelled", MarketplaceOrderStatuses.Cancelled)]
    [InlineData("returned", MarketplaceOrderStatuses.Returned)]
    public void MapFbsStatus_NormalizesSellerStatuses(string raw, string expected)
    {
        Assert.Equal(expected, OzonCommerceMapper.MapFbsStatus(raw));
    }

    [Theory]
    [InlineData(MarketplaceOrderStatuses.New, "awaiting_approve")]
    [InlineData("awaiting_approve", "awaiting_approve")]
    [InlineData(MarketplaceOrderStatuses.AwaitingPack, "awaiting_packaging")]
    [InlineData("awaiting_packaging", "awaiting_packaging")]
    [InlineData(MarketplaceOrderStatuses.Confirmed, "awaiting_registration")]
    [InlineData("awaiting_registration", "awaiting_registration")]
    [InlineData(MarketplaceOrderStatuses.AwaitingDeliver, "awaiting_deliver")]
    [InlineData(MarketplaceOrderStatuses.Delivering, "delivering")]
    [InlineData(MarketplaceOrderStatuses.Delivered, "delivered")]
    [InlineData(MarketplaceOrderStatuses.Cancelled, "cancelled")]
    [InlineData(MarketplaceOrderStatuses.Returned, "not_accepted")]
    public void MapFbsListStatus_SendsOzonAliases(string status, string expected)
    {
        Assert.Equal(expected, OzonCommerceMapper.MapFbsListStatus(status));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(MarketplaceOrderStatuses.Unpaid)]
    [InlineData("unknown")]
    public void MapFbsListStatus_SkipsStatusesOzonRejects(string? status)
    {
        Assert.Null(OzonCommerceMapper.MapFbsListStatus(status));
    }

    [Theory]
    [InlineData(MarketplaceOrderStatuses.New, "awaiting_packaging")]
    [InlineData(MarketplaceOrderStatuses.AwaitingDeliver, "awaiting_deliver")]
    [InlineData(MarketplaceOrderStatuses.Cancelled, "cancelled")]
    public void MapFboListStatus_SendsOzonAliases(string status, string expected)
    {
        Assert.Equal(expected, OzonCommerceMapper.MapFboListStatus(status));
    }

    [Theory]
    [InlineData(MarketplaceOrderStatuses.Confirmed)]
    [InlineData(MarketplaceOrderStatuses.AwaitingPack)]
    [InlineData(MarketplaceOrderStatuses.Unpaid)]
    public void MapFboListStatus_SkipsStatusesWithoutFboFilter(string status)
    {
        Assert.Null(OzonCommerceMapper.MapFboListStatus(status));
    }

    [Fact]
    public void FromFbs_MapsItemsTotalAndChannel()
    {
        var posting = new OzonFbsPosting
        {
            PostingNumber = "1234-0001",
            OrderId = 99,
            OrderNumber = "99",
            Status = "awaiting_packaging",
            Products =
            [
                new OzonPostingProduct { Sku = 11, OfferId = "SKU-1", Name = "Tee", Quantity = 2, Price = "150.00" }
            ]
        };

        var order = OzonCommerceMapper.FromFbs(posting);

        Assert.Equal("1234-0001", order.ExternalId);
        Assert.Equal("99", order.DisplayId);
        Assert.Equal("fbs", order.Channel);
        Assert.Equal(MarketplaceOrderStatuses.AwaitingPack, order.Status);
        Assert.Equal(300m, order.Total);
        Assert.Single(order.Items);
        Assert.Equal("SKU-1", order.Items[0].Sku);
        Assert.Null(order.Items[0].ImageUrl);
    }

    [Fact]
    public void FromFbs_MapsWarehouseFromDeliveryMethodWhenAnalyticsIsEmpty()
    {
        var order = OzonCommerceMapper.FromFbs(new OzonFbsPosting
        {
            PostingNumber = "1234-0001-1",
            OrderId = 99,
            Status = "delivering",
            DeliveryMethod = new OzonDeliveryMethod
            {
                Name = "ПВЗ Ozon",
                Warehouse = "Хоругвино",
                WarehouseId = 15431806189000
            }
        });

        Assert.Equal("Хоругвино", Convert.ToString(order.Extra!["warehouse"], CultureInfo.InvariantCulture));
        Assert.Equal("15431806189000", Convert.ToString(order.Extra["warehouseId"], CultureInfo.InvariantCulture));
        Assert.Equal("ПВЗ Ozon", Convert.ToString(order.Extra["deliveryMethod"], CultureInfo.InvariantCulture));
    }

    [Fact]
    public void FromFbs_PrefersAnalyticsWarehouseName()
    {
        var order = OzonCommerceMapper.FromFbs(new OzonFbsPosting
        {
            PostingNumber = "1-1",
            AnalyticsData = new OzonPostingAnalytics
            {
                WarehouseName = "Тверь",
                WarehouseId = 9
            },
            DeliveryMethod = new OzonDeliveryMethod
            {
                Warehouse = "Хоругвино",
                WarehouseId = 8
            }
        });

        Assert.Equal("Тверь", Convert.ToString(order.Extra!["warehouse"], CultureInfo.InvariantCulture));
        Assert.Equal("8", Convert.ToString(order.Extra["warehouseId"], CultureInfo.InvariantCulture));
    }

    [Fact]
    public void PostingLookupCandidates_AddsFirstPackageWhenSearchIsOrderNumber()
    {
        Assert.Equal(
            ["76728543-0063", "76728543-0063-1"],
            OzonCommerceMapper.PostingLookupCandidates("76728543-0063"));
        Assert.Equal(
            ["76728543-0063-1"],
            OzonCommerceMapper.PostingLookupCandidates("76728543-0063-1"));
        Assert.Empty(OzonCommerceMapper.PostingLookupCandidates("  "));
    }

    [Fact]
    public void MatchesSearch_FindsOrderNumberOnThePostingIdTheCabinetShows()
    {
        var order = OzonCommerceMapper.FromFbs(new OzonFbsPosting
        {
            PostingNumber = "76728543-0063-1",
            OrderNumber = "76728543-0063",
            Status = "awaiting_packaging"
        });

        Assert.True(OzonCommerceMapper.MatchesSearch(order, "76728543-0063"));
        Assert.True(OzonCommerceMapper.MatchesSearch(order, "76728543-0063-1"));
        Assert.False(OzonCommerceMapper.MatchesSearch(order, "11111111-0001"));
        Assert.True(OzonCommerceMapper.MatchesSearch(order, " "));
    }

    [Fact]
    public void ApplyProductImages_FillsFromPrimaryImageBySkuAndOffer()
    {
        var order = OzonCommerceMapper.FromFbs(new OzonFbsPosting
        {
            PostingNumber = "1234-0001",
            Products =
            [
                new OzonPostingProduct { Sku = 3319851038, OfferId = "_WA121", Name = "Tee", Quantity = 1, Price = "10" },
                new OzonPostingProduct { Sku = 99, OfferId = "KEEP", Name = "Kept", Quantity = 1, Price = "1" }
            ]
        });
        order.Items[1].ImageUrl = "https://cdn.example/already.webp";

        OzonCommerceMapper.ApplyProductImages(
            [order],
            [
                new ProductInfoItem
                {
                    Id = 1,
                    Sku = 3319851038,
                    OfferId = "_WA121",
                    PrimaryImage = ["https://cdn.ozon.ru/a.jpg"]
                }
            ]);

        Assert.Equal("https://cdn.ozon.ru/a.jpg", order.Items[0].ImageUrl);
        Assert.Equal("https://cdn.example/already.webp", order.Items[1].ImageUrl);
    }

    [Fact]
    public void ApplyProductImages_ReplacesPresignedObjectStorageUrl()
    {
        var order = new ExternalOrder
        {
            Items =
            [
                new ExternalOrderItem
                {
                    OfferId = "_WA121",
                    ImageUrl = "https://storage.example/x.jpg?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Signature=dead"
                }
            ]
        };

        OzonCommerceMapper.ApplyProductImages(
            [order],
            [
                new ProductInfoItem
                {
                    OfferId = "_WA121",
                    PrimaryImage = ["https://cdn.ozon.ru/fresh.jpg"]
                }
            ]);

        Assert.Equal("https://cdn.ozon.ru/fresh.jpg", order.Items[0].ImageUrl);
    }

    [Fact]
    public void ApplyProductImages_FallsBackToImagesList()
    {
        var order = new ExternalOrder
        {
            Items = [new ExternalOrderItem { OfferId = "VI33" }]
        };

        OzonCommerceMapper.ApplyProductImages(
            [order],
            [
                new ProductInfoItem
                {
                    OfferId = "VI33",
                    Images = ["https://cdn.ozon.ru/b.jpg"]
                }
            ]);

        Assert.Equal("https://cdn.ozon.ru/b.jpg", order.Items[0].ImageUrl);
    }

    [Fact]
    public void MapPrice_UsesMarketingAsDiscountedAndKeepsNetAndAutoAction()
    {
        var item = new PriceInfoItem
        {
            ProductId = 42,
            OfferId = "SKU-1",
            Price = new PriceDetails
            {
                CurrentPrice = "1000",
                OldPrice = "1200",
                MinPrice = "800",
                MarketingPrice = "900",
                NetPrice = "400",
                CurrencyCode = "RUB",
                AutoActionEnabled = true,
                PriceStrategyEnabled = false,
                MinPriceForAutoActionsEnabled = true
            }
        };

        var price = OzonCommerceMapper.MapPrice(item);

        Assert.Equal("42", price.ExternalProductId);
        Assert.Equal(1000m, price.Price);
        Assert.Equal(1200m, price.OldPrice);
        Assert.Equal(900m, price.DiscountedPrice);
        Assert.Equal(800m, price.MinPrice);
        Assert.Equal(400m, price.NetPrice);
        Assert.Equal("ENABLED", price.AutoActionEnabled);
        Assert.Equal("DISABLED", price.PriceStrategyEnabled);
        Assert.True(price.MinPriceForAutoActionsEnabled);
        Assert.Equal("SKU-1", price.ExternalSku);
    }

    [Fact]
    public void FromPackageLabel_DecodesBase64Pdf()
    {
        var pdf = "%PDF-1.4 test"u8.ToArray();
        var label = OzonCommerceMapper.FromPackageLabel(new OzonPackageLabelResponse
        {
            ContentType = "application/pdf",
            FileName = "ticket.pdf",
            FileContent = Convert.ToBase64String(pdf)
        }, "1234-0001");

        Assert.NotNull(label);
        Assert.Equal("application/pdf", label!.ContentType);
        Assert.Equal("ticket.pdf", label.FileName);
        Assert.Equal("pdf", label.Format);
        Assert.Equal(pdf, Convert.FromBase64String(label.ContentBase64));
    }

    [Fact]
    public void FromPackageLabel_AcceptsRawPdfText()
    {
        var label = OzonCommerceMapper.FromPackageLabel(new OzonPackageLabelResponse
        {
            FileContent = "%PDF-1.7 raw"
        }, "p-1");

        Assert.NotNull(label);
        Assert.Equal("ozon-p-1.pdf", label!.FileName);
        Assert.StartsWith("%PDF", System.Text.Encoding.Latin1.GetString(Convert.FromBase64String(label.ContentBase64)));
    }

    [Fact]
    public void ParsePackageLabelHttp_RawPdfBytes_DoesNotRequireJson()
    {
        var pdf = "%PDF-1.4 raw-bytes"u8.ToArray();
        var parsed = OzonCommerceMapper.ParsePackageLabelHttp(pdf, "application/pdf");
        var label = OzonCommerceMapper.FromPackageLabel(parsed, "48173252-0034-4");

        Assert.NotNull(label);
        Assert.Equal("pdf", label!.Format);
        Assert.Equal(pdf, Convert.FromBase64String(label.ContentBase64));
    }

    [Fact]
    public void ParsePackageLabelHttp_JsonEnvelope()
    {
        var pdf = "%PDF-1.4 env"u8.ToArray();
        var json = JsonSerializer.Serialize(new
        {
            content_type = "application/pdf",
            file_name = "ticket.pdf",
            file_content = Convert.ToBase64String(pdf)
        });
        var parsed = OzonCommerceMapper.ParsePackageLabelHttp(Encoding.UTF8.GetBytes(json), "application/json");
        var label = OzonCommerceMapper.FromPackageLabel(parsed, "x");

        Assert.Equal("ticket.pdf", label!.FileName);
        Assert.Equal(pdf, Convert.FromBase64String(label.ContentBase64));
    }

    [Theory]
    [InlineData("pack", MarketplaceOrderActions.Pack)]
    [InlineData("confirm", MarketplaceOrderActions.Pack)]
    [InlineData("ship", MarketplaceOrderActions.Ship)]
    [InlineData("cancel", MarketplaceOrderActions.Cancel)]
    public void NormalizeSellerAction_MapsAssembleAndHandover(string action, string expected)
    {
        Assert.Equal(expected, OzonCommerceMapper.NormalizeSellerAction(action));
    }

    [Theory]
    [InlineData("deliver")]
    [InlineData("complete")]
    [InlineData("")]
    public void NormalizeSellerAction_RejectsDeliveryMarks(string action)
    {
        Assert.Null(OzonCommerceMapper.NormalizeSellerAction(action));
    }

    [Theory]
    [InlineData(MarketplaceOrderStatuses.AwaitingDeliver, "fbs", true)]
    [InlineData(MarketplaceOrderStatuses.AwaitingPack, "fbs", false)]
    [InlineData(MarketplaceOrderStatuses.Delivering, "fbs", false)]
    [InlineData(MarketplaceOrderStatuses.AwaitingDeliver, "fbo", false)]
    public void CanGetPackageLabel_OnlyPackedFbs(string status, string channel, bool expected)
    {
        Assert.Equal(expected, OzonCommerceMapper.CanGetPackageLabel(status, channel));
    }

    [Fact]
    public void FromWarehouse_IsSellerStockLocation()
    {
        var location = OzonCommerceMapper.FromWarehouse(new WarehouseInfo
        {
            WarehouseId = 123,
            Name = "FBS склад",
            Status = "working",
            IsRfbs = true
        });

        Assert.Equal("123", location.ExternalId);
        Assert.Equal("FBS склад", location.Name);
        Assert.Equal(MarketplaceLocationKinds.SellerWarehouse, location.Kind);
        Assert.Equal("working", location.Extra!["status"]?.ToString());
    }
}
