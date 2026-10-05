using System.Text.Json;
using Delegat.Marketplace.Ozon.Models;

namespace Delegat.Tests;

public class OzonTotalItemsTests
{
    [Fact]
    public void ProductList_UsesTotalItemsWhenTotalIsAbsent()
    {
        var parsed = JsonSerializer.Deserialize<ProductListResponse>(
            """{"result":{"items":[],"total_items":40,"last_id":"abc"}}""");

        Assert.NotNull(parsed);
        Assert.Equal(0, parsed.Result.Total);
        Assert.Equal(40, parsed.Result.ResolvedTotal);
    }

    [Fact]
    public void ActionProducts_KeepsLegacyTotal()
    {
        var parsed = JsonSerializer.Deserialize<OzonActionProductsResponse>(
            """{"result":{"products":[],"total":12}}""");

        Assert.Equal(12, parsed!.Result!.ResolvedTotal);
    }

    [Fact]
    public void PricesAndStocks_PreferTotalItems()
    {
        var prices = JsonSerializer.Deserialize<PriceInfoResponse>(
            """{"items":[],"cursor":"","total":1,"total_items":9}""");
        var stocks = JsonSerializer.Deserialize<StockInfoResponse>(
            """{"items":[],"cursor":"","total_items":4}""");

        Assert.Equal(9, prices!.ResolvedTotal);
        Assert.Equal(4, stocks!.ResolvedTotal);
    }

    [Fact]
    public void ActionUpdate_ReadsActiveIdsAndMoneyPrice()
    {
        var update = JsonSerializer.Deserialize<OzonActionMutateResponse>(
            """{"active_product_ids":[7],"deactivated_product_ids":[8],"rejected":[]}""");
        var legacy = JsonSerializer.Deserialize<OzonActionMutateResponse>(
            """{"result":{"product_ids":[3],"rejected":[{"product_id":4,"reason":"limit"}]}}""");
        var products = JsonSerializer.Deserialize<OzonActionProductsResponse>(
            """{"result":{"products":[{"id":1,"price":10,"action_price":{"currency_code":"RUB","units":"990","nanos":500000000},"max_action_price":"1000.5"}],"total":1}}""");

        Assert.Equal([7L], update!.AcceptedIds());
        Assert.Equal([8L], update.DeactivatedIds());
        Assert.Equal([3L], legacy!.AcceptedIds());
        Assert.Equal("limit", legacy.RejectedItems()[0].Reason);
        Assert.Equal(990.5m, products!.Result!.Products![0].ActionPrice);
        Assert.Equal(1000.5m, products.Result.Products[0].MaxActionPrice);
    }
}
