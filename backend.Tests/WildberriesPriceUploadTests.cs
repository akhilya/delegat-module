using Delegat.Marketplace.Wildberries;
using Delegat.Marketplace.Wildberries.Models;

namespace Delegat.Tests;

public class WildberriesPriceUploadTests
{
    [Fact]
    public void AlreadyApplied_SkipsUpload()
    {
        var goods = Goods(price: 1500, discount: 10);
        var desired = new WbPriceItem { NmId = 42, Price = 1500, Discount = 10 };

        var action = Assert.Single(WbPriceUploadPlanner.Plan([desired], new Dictionary<long, WbGoodsItem> { [42] = goods }));

        Assert.Equal(WbPriceActionKind.Unchanged, action.Kind);
    }

    [Fact]
    public void DiscountedBuyerPrice_WithSameDiscount_IsUnchanged()
    {
        var goods = new WbGoodsItem
        {
            NmId = 42,
            Discount = 0,
            Sizes = [new WbGoodsSize { SizeId = 1, Price = 0, DiscountedPrice = 1000 }],
        };
        var desired = new WbPriceItem { NmId = 42, Price = 1000, Discount = 0 };

        var action = Assert.Single(WbPriceUploadPlanner.Plan([desired], new Dictionary<long, WbGoodsItem> { [42] = goods }));

        Assert.Equal(WbPriceActionKind.Unchanged, action.Kind);
    }

    [Fact]
    public void DifferentPrice_UploadsNm()
    {
        var goods = Goods(price: 1500, discount: 0);
        var desired = new WbPriceItem { NmId = 42, Price = 1600, Discount = 0 };

        var action = Assert.Single(WbPriceUploadPlanner.Plan([desired], new Dictionary<long, WbGoodsItem> { [42] = goods }));

        Assert.Equal(WbPriceActionKind.NmPrice, action.Kind);
        Assert.Equal(1600, action.Price);
    }

    [Fact]
    public void SizePricedCard_UpdatesSizesInsteadOfNmPrice()
    {
        var goods = Goods(price: 1000, discount: 5, editableSizePrice: true, sizeId: 77);
        var desired = new WbPriceItem { NmId = 42, Price = 1200, Discount = 5 };

        var action = Assert.Single(WbPriceUploadPlanner.Plan([desired], new Dictionary<long, WbGoodsItem> { [42] = goods }));

        Assert.Equal(WbPriceActionKind.SizePrices, action.Kind);
        Assert.True(action.PriceChanged);
        Assert.False(action.DiscountChanged);
        Assert.Equal(77L, Assert.Single(action.SizeIds));
    }

    [Fact]
    public void UnknownCard_StillAttemptsNmUpload()
    {
        var desired = new WbPriceItem { NmId = 42, Price = 900, Discount = 0 };

        var action = Assert.Single(WbPriceUploadPlanner.Plan([desired], new Dictionary<long, WbGoodsItem>()));

        Assert.Equal(WbPriceActionKind.NmPrice, action.Kind);
    }

    [Theory]
    [InlineData("All item Nos. are specified incorrectly, or the specified prices and discounts are already set")]
    [InlineData("BadRequest: {\"errorText\":\"prices and discounts are already set\"}")]
    public void AlreadySetMessage_IsRecognized(string message)
    {
        Assert.True(WbPriceUploadPlanner.IsAlreadySetOrUnknownNm(message));
    }

    private static WbGoodsItem Goods(int price, int discount, bool editableSizePrice = false, long sizeId = 1) =>
        new()
        {
            NmId = 42,
            Discount = discount,
            EditableSizePrice = editableSizePrice,
            Sizes = [new WbGoodsSize { SizeId = sizeId, Price = price }],
        };
}
