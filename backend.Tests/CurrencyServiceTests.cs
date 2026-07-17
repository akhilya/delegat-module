using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Services;
using Delegat.Tests.Infrastructure;

namespace Delegat.Tests;

public class CurrencyServiceTests
{
    [Fact]
    public async Task GetStoreBaseCurrency_ReturnsNormalized()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore(baseCurrency: "rub");
        await data.SaveAsync();

        var svc = new CurrencyService(db);
        Assert.Equal("RUB", await svc.GetStoreBaseCurrencyAsync(store.Id));
    }

    [Fact]
    public async Task GetRateToBase_BaseCurrency_IsOne()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore(baseCurrency: "RUB");
        await data.SaveAsync();

        var svc = new CurrencyService(db);
        Assert.Equal(1m, await svc.GetRateToBaseAsync(store.Id, "RUB"));
    }

    [Fact]
    public async Task GetRateToBase_EnabledCurrency_ReturnsRate()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore(baseCurrency: "RUB");
        await data.SaveAsync();
        db.storeCurrencies.Add(new StoreCurrency
        {
            storeId = store.Id,
            code = "USD",
            rateToBase = 90m,
            isEnabled = true,
            updatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = new CurrencyService(db);
        Assert.Equal(90m, await svc.GetRateToBaseAsync(store.Id, "usd"));
    }

    [Fact]
    public async Task GetRateToBase_DisabledCurrency_Throws()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        await data.SaveAsync();
        db.storeCurrencies.Add(new StoreCurrency
        {
            storeId = store.Id,
            code = "EUR",
            rateToBase = 100m,
            isEnabled = false,
            updatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = new CurrencyService(db);
        await Assert.ThrowsAsync<ApiException>(() => svc.GetRateToBaseAsync(store.Id, "EUR"));
    }

    [Fact]
    public async Task ConvertToBase_AppliesRate()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        await data.SaveAsync();
        db.storeCurrencies.Add(new StoreCurrency
        {
            storeId = store.Id,
            code = "USD",
            rateToBase = 100m,
            isEnabled = true,
            updatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = new CurrencyService(db);
        Assert.Equal(200m, await svc.ConvertToBaseAsync(2m, "USD", store.Id));
    }

    [Fact]
    public async Task ConvertFromBase_DividesByRate()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        await data.SaveAsync();
        db.storeCurrencies.Add(new StoreCurrency
        {
            storeId = store.Id,
            code = "USD",
            rateToBase = 100m,
            isEnabled = true,
            updatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = new CurrencyService(db);
        Assert.Equal(1.5m, await svc.ConvertFromBaseAsync(150m, "USD", store.Id));
    }

    [Fact]
    public async Task Convert_SameCodes_ReturnsAmount()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        await data.SaveAsync();

        var svc = new CurrencyService(db);
        Assert.Equal(12.34m, await svc.ConvertAsync(12.34m, "RUB", "RUB", store.Id));
    }

    [Fact]
    public async Task GetStoreCurrencies_IncludesBase()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore(baseCurrency: "RUB");
        await data.SaveAsync();

        var svc = new CurrencyService(db);
        var list = await svc.GetStoreCurrenciesAsync(store.Id);
        Assert.Contains(list, c => c.IsBase && c.Code == "RUB");
    }

    [Fact]
    public async Task GetStoreCurrencies_MissingStore_ThrowsNotFound()
    {
        await using var db = TestDbFactory.CreateDb();
        var svc = new CurrencyService(db);
        var ex = await Assert.ThrowsAsync<ApiException>(() => svc.GetStoreCurrenciesAsync(999));
        Assert.Equal(404, ex.StatusCode);
    }
}
