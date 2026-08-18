using Delegat.Models;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

/// <summary>
/// Domain tests for store creation defaults and AddForm timezone/currency.
/// </summary>
public class StoreDomainTests
{
    private static (DataContext db, TestDataBuilder data) Create()
    {
        var db = TestDbFactory.CreateDb();
        return (db, new TestDataBuilder(db));
    }

    [Fact]
    public void NewStore_DefaultsBaseCurrencyToRub()
    {
        var store = new Store();
        Assert.Equal(Store.DEFAULT_BASE_CURRENCY, store.baseCurrency);
        Assert.Equal("RUB", store.baseCurrency);
        Assert.Null(store.onboardingCompletedAt);
    }

    [Fact]
    public void AddFill_TrimsStoreName()
    {
        var store = new Store();
        Assert.True(store.AddFill(new Store.AddForm { name = "  Acme  " }));
        Assert.Equal("Acme", store.name);
    }

    [Fact]
    public void StoreName_MaxLengthIsForty()
    {
        Assert.Equal(40, Store.STORE_NAME_MAX_LENGTH);
        Assert.True("x".PadRight(40, 'x').Length <= Store.STORE_NAME_MAX_LENGTH);
        Assert.False("x".PadRight(41, 'x').Length <= Store.STORE_NAME_MAX_LENGTH);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddFill_WhenBaseCurrencyMissing_UsesDefaultRub(string? baseCurrency)
    {
        var store = new Store { baseCurrency = baseCurrency! };
        Assert.True(store.AddFill(new Store.AddForm { name = "Shop" }));
        Assert.Equal(Store.DEFAULT_BASE_CURRENCY, store.baseCurrency);
    }

    [Fact]
    public void AddFill_AppliesTimezoneAndBaseCurrencyFromForm()
    {
        var store = new Store();
        Assert.True(store.AddFill(new Store.AddForm
        {
            name = "Shop",
            timezone = "Europe/Berlin",
            baseCurrency = "usd",
        }));
        Assert.Equal("Europe/Berlin", store.timezone);
        Assert.Equal("USD", store.baseCurrency);
    }

    [Fact]
    public void AddFill_WhenFormOmitsTimezoneAndCurrency_UsesDefaults()
    {
        var store = new Store();
        Assert.True(store.AddFill(new Store.AddForm { name = "Shop" }));
        Assert.Equal(Store.DEFAULT_TIMEZONE, store.timezone);
        Assert.Equal(Store.DEFAULT_BASE_CURRENCY, store.baseCurrency);
    }

    [Fact]
    public void AddFill_WhenBaseCurrencyAlreadySet_KeepsIt()
    {
        var store = new Store { baseCurrency = "USD" };
        Assert.True(store.AddFill(new Store.AddForm { name = "Shop" }));
        Assert.Equal("USD", store.baseCurrency);
    }

    [Fact]
    public async Task CreateStoreViaAddFill_PersistsDefaultBaseCurrency()
    {
        // Mirrors StoreController.PostStore: new Store → AddFill(name only) → SaveChanges.
        var (db, _) = Create();
        var store = new Store();
        Assert.True(store.AddFill(new Store.AddForm { name = "Production path" }));

        db.stores.Add(store);
        await db.SaveChangesAsync();

        var saved = await db.stores.SingleAsync(s => s.name == "Production path");
        Assert.Equal(Store.DEFAULT_BASE_CURRENCY, saved.baseCurrency);
        Assert.False(string.IsNullOrWhiteSpace(saved.baseCurrency));
    }

    [Fact]
    public async Task CreateStoreViaAddFill_PersistsTimezoneAndBaseCurrencyFromForm()
    {
        var (db, _) = Create();
        var store = new Store();
        Assert.True(store.AddFill(new Store.AddForm
        {
            name = "Wizard store",
            timezone = "Europe/Berlin",
            baseCurrency = "eur",
        }));

        db.stores.Add(store);
        await db.SaveChangesAsync();

        var saved = await db.stores.SingleAsync(s => s.name == "Wizard store");
        Assert.Equal("Europe/Berlin", saved.timezone);
        Assert.Equal("EUR", saved.baseCurrency);
    }
}
