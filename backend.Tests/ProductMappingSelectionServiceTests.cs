using Delegat.Infrastructure.Middleware;
using Delegat.Services.Marketplace;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

public class ProductMappingSelectionServiceTests
{
    [Fact]
    public async Task SaveAsync_MovesAProductBetweenTwoMappingsOfTheSameCategory()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var figures = data.AddCategory(store.Id, "Фигурки");
        var other = data.AddCategory(store.Id, "Другое");
        var product = data.AddProduct(store.Id, "Люк", categoryId: figures.Id);
        var connection = data.AddConnection(store.Id, adapterKey: "wildberries", name: "WB");
        var first = data.AddMapping(connection.Id, figures.Id, "wb-toys", "Игрушки");
        var second = data.AddMapping(connection.Id, figures.Id, "wb-collect", "Коллекционные");
        var foreign = data.AddMapping(connection.Id, other.Id, "wb-other", "Прочее");
        var link = data.AddProductLink(connection.Id, product.Id, "nm-1");
        link.categoryMappingId = first.Id;
        await data.SaveAsync();

        var svc = new ProductMappingSelectionService(db);
        var resolved = await svc.ResolveAsync(store.Id, connection.Id, [product.Id]);
        var choice = Assert.Single(resolved);
        Assert.Equal(first.Id, choice.SelectedMappingId);
        Assert.Equal(2, choice.AvailableMappings.Count);
        Assert.False(choice.NeedsSelection);

        var updated = await svc.SaveAsync(store.Id, connection.Id, [new MappingSelection(product.Id, second.Id)]);
        Assert.Equal(1, updated);
        Assert.Equal(second.Id, (await db.productMarketplaceLinks.SingleAsync()).categoryMappingId);

        var mismatch = await Assert.ThrowsAsync<ApiException>(() =>
            svc.SaveAsync(store.Id, connection.Id, [new MappingSelection(product.Id, foreign.Id)]));
        Assert.Equal("mapping_category_mismatch", mismatch.Code);
    }

    [Fact]
    public async Task SaveAsync_CreatesAPendingLinkWhenTheProductIsNotOnTheConnectionYet()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var figures = data.AddCategory(store.Id, "Фигурки");
        var product = data.AddProduct(store.Id, "Люк", categoryId: figures.Id);
        var connection = data.AddConnection(store.Id, adapterKey: "wildberries");
        var mapping = data.AddMapping(connection.Id, figures.Id, "wb-toys", "Игрушки");
        await data.SaveAsync();

        var svc = new ProductMappingSelectionService(db);
        await svc.SaveAsync(store.Id, connection.Id, [new MappingSelection(product.Id, mapping.Id)]);

        var link = await db.productMarketplaceLinks.SingleAsync();
        Assert.Equal(mapping.Id, link.categoryMappingId);
        Assert.Equal($"pending:{product.Id}", link.externalProductId);
    }
}
