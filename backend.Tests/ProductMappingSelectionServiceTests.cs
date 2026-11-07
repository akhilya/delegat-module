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

    [Fact]
    public async Task SaveAsync_AssignsMultipleCategories_AndPreservesExistingRemoteLinks()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var firstCategory = data.AddCategory(store.Id, "Figures");
        var secondCategory = data.AddCategory(store.Id, "Books");
        var first = data.AddProduct(store.Id, "First", categoryId: firstCategory.Id);
        var second = data.AddProduct(store.Id, "Second", categoryId: firstCategory.Id);
        var third = data.AddProduct(store.Id, "Third", categoryId: secondCategory.Id);
        var connection = data.AddConnection(store.Id, adapterKey: "wildberries");
        var otherConnection = data.AddConnection(store.Id, adapterKey: "ozon");
        var firstMapping = data.AddMapping(connection.Id, firstCategory.Id, "figures", "Figures");
        var secondMapping = data.AddMapping(connection.Id, secondCategory.Id, "books", "Books");
        var existing = data.AddProductLink(connection.Id, first.Id, "remote-existing");
        var untouched = data.AddProductLink(otherConnection.Id, first.Id, "other-remote");
        await data.SaveAsync();

        var service = new ProductMappingSelectionService(db);
        Assert.Equal(3, await service.SaveAsync(store.Id, connection.Id, [
            new(first.Id, firstMapping.Id), new(second.Id, firstMapping.Id), new(third.Id, secondMapping.Id),
        ]));
        db.ChangeTracker.Clear();
        var links = await db.productMarketplaceLinks.ToListAsync();
        Assert.Equal(4, links.Count);
        Assert.Equal("remote-existing", links.Single(l => l.Id == existing.Id).externalProductId);
        Assert.Equal(firstMapping.Id, links.Single(l => l.Id == existing.Id).categoryMappingId);
        Assert.Null(links.Single(l => l.Id == untouched.Id).categoryMappingId);
        Assert.Equal("other-remote", links.Single(l => l.Id == untouched.Id).externalProductId);
        Assert.Equal(firstMapping.Id, links.Single(l => l.productId == second.Id).categoryMappingId);
        Assert.Equal(secondMapping.Id, links.Single(l => l.productId == third.Id).categoryMappingId);
    }

    [Theory]
    [InlineData("category", "mapping_category_mismatch")]
    [InlineData("connection", "mapping_not_found")]
    [InlineData("store", "product_not_found")]
    public async Task SaveAsync_RejectsTheEntireBatchWhenOneSelectionIsInvalid(string invalid, string code)
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var otherStore = data.AddStore();
        var category = data.AddCategory(store.Id, "Figures");
        var otherCategory = data.AddCategory(store.Id, "Books");
        var first = data.AddProduct(store.Id, "First", categoryId: category.Id);
        var second = data.AddProduct(store.Id, "Second", categoryId: category.Id);
        var foreignProduct = data.AddProduct(otherStore.Id, "Foreign");
        var connection = data.AddConnection(store.Id, adapterKey: "wildberries");
        var otherConnection = data.AddConnection(store.Id, adapterKey: "ozon");
        var original = data.AddMapping(connection.Id, category.Id, "original", "Original");
        var replacement = data.AddMapping(connection.Id, category.Id, "replacement", "Replacement");
        var wrongCategory = data.AddMapping(connection.Id, otherCategory.Id, "books", "Books");
        var wrongConnection = data.AddMapping(otherConnection.Id, category.Id, "other", "Other");
        var existing = data.AddProductLink(connection.Id, first.Id, "remote-existing");
        existing.categoryMappingId = original.Id;
        await data.SaveAsync();

        var service = new ProductMappingSelectionService(db);
        var error = await Assert.ThrowsAsync<ApiException>(() => service.SaveAsync(store.Id, connection.Id, [
            new(first.Id, replacement.Id),
            new(invalid == "store" ? foreignProduct.Id : second.Id,
                invalid == "category" ? wrongCategory.Id : invalid == "connection" ? wrongConnection.Id : replacement.Id),
        ]));
        Assert.Equal(code, error.Code);
        db.ChangeTracker.Clear();
        var retained = Assert.Single(await db.productMarketplaceLinks.ToListAsync());
        Assert.Equal(original.Id, retained.categoryMappingId);
        Assert.Equal("remote-existing", retained.externalProductId);
    }
}
