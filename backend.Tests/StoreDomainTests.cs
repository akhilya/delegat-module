using Delegat.Models;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

/// <summary>
/// Domain tests for store creation defaults — especially baseCurrency, which is
/// required by the DB schema but not present on <see cref="Store.AddForm"/>.
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
    }

    [Fact]
    public void AddFill_SetsNameFromForm()
    {
        var store = new Store();
        Assert.True(store.AddFill(new Store.AddForm { name = "Acme" }));
        Assert.Equal("Acme", store.name);
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
}
