using Delegat.Infrastructure.Middleware;
using Delegat.Services.Marketplace;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

public class ProductNotTrashedTests
{
    [Fact]
    public async Task EnsureAsync_AllowsActiveProduct()
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var product = data.AddProduct(data.AddStore().Id);
        await data.SaveAsync();

        await ProductNotTrashed.EnsureAsync(db, product.Id);
    }

    [Fact]
    public async Task EnsureAsync_RejectsTrashedProduct()
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var product = data.AddProduct(data.AddStore().Id, deletedAt: DateTime.UtcNow);
        await data.SaveAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() => ProductNotTrashed.EnsureAsync(db, product.Id));
        Assert.Equal(ProductNotTrashed.ErrorCode, ex.Code);
    }

    [Fact]
    public async Task IsTrashedAsync_DetectsSoftDeleted()
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var storeId = data.AddStore().Id;
        var active = data.AddProduct(storeId);
        var trashed = data.AddProduct(storeId, deletedAt: DateTime.UtcNow);
        await data.SaveAsync();

        Assert.False(await ProductNotTrashed.IsTrashedAsync(db, active.Id));
        Assert.True(await ProductNotTrashed.IsTrashedAsync(db, trashed.Id));
        Assert.Equal(0, await db.products.CountAsync(p => p.Id == trashed.Id));
    }
}
