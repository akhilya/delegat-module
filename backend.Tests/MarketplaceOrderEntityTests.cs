using Delegat.Models.Marketplace;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

public class MarketplaceOrderEntityTests
{
    [Fact]
    public async Task RoundTrip_StoresPayloadAndDenormalizedColumns()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id, "ozon");
        await data.SaveAsync();
        db.marketplaceOrders.Add(new MarketplaceOrder
        {
            storeId = store.Id,
            connectionId = connection.Id,
            marketplaceOrderId = "123-1",
            displayId = "123",
            status = "awaiting_pack",
            channel = "fbs",
            currency = "RUB",
            total = 10.5m,
            orderCreatedAt = DateTime.UtcNow,
            syncedAt = DateTime.UtcNow,
            payloadJson = """{"externalId":"123-1","status":"awaiting_pack"}"""
        });
        await db.SaveChangesAsync();

        var row = await db.marketplaceOrders.AsNoTracking().SingleAsync();
        Assert.Equal("123-1", row.marketplaceOrderId);
        Assert.Equal("awaiting_pack", row.status);
        Assert.Contains("123-1", row.payloadJson);
    }

    [Fact]
    public void Total_HasCatalogMoneyPrecision()
    {
        using var db = TestDbFactory.CreateDb();
        var property = db.Model
            .FindEntityType(typeof(MarketplaceOrder))!
            .FindProperty(nameof(MarketplaceOrder.total))!;

        Assert.Equal("decimal(18,2)", property.FindAnnotation("Relational:ColumnType")?.Value);
        Assert.Equal(18, property.GetPrecision());
        Assert.Equal(2, property.GetScale());
    }

    [Fact]
    public async Task RoundTrip_StoresSyncState()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id, "ozon");
        await data.SaveAsync();
        db.marketplaceOrderSyncStates.Add(new MarketplaceOrderSyncState
        {
            connectionId = connection.Id,
            status = "awaiting_pack",
            lastError = null
        });
        await db.SaveChangesAsync();

        var row = await db.marketplaceOrderSyncStates.AsNoTracking().SingleAsync();
        Assert.Equal(connection.Id, row.connectionId);
        Assert.Equal("awaiting_pack", row.status);
        Assert.Null(row.lastPulledAt);
        Assert.Null(row.lastError);
    }

    [Fact]
    public void LastPulledAt_IsNullable()
    {
        using var db = TestDbFactory.CreateDb();
        var property = db.Model
            .FindEntityType(typeof(MarketplaceOrderSyncState))!
            .FindProperty(nameof(MarketplaceOrderSyncState.lastPulledAt))!;

        Assert.True(property.IsNullable);
        Assert.Equal(typeof(DateTime?), property.ClrType);
    }
}
