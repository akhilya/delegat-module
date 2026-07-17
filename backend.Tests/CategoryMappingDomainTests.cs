using Delegat.Models.Marketplace;
using Delegat.Tests.Infrastructure;

namespace Delegat.Tests;

public class CategoryMappingDomainTests
{
    [Fact]
    public async Task CreateMapping_ThroughTestDataBuilder_PersistsFields()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var category = data.AddCategory(store.Id, "Shoes");
        var connection = data.AddConnection(store.Id);
        var mapping = data.AddMapping(connection.Id, category.Id, "ozon-7", "Footwear");
        await data.SaveAsync();

        var saved = db.categoryMappings.Single();
        Assert.Equal(mapping.Id, saved.Id);
        Assert.Equal(connection.Id, saved.connectionId);
        Assert.Equal(category.Id, saved.localCategoryId);
        Assert.Equal("ozon-7", saved.externalCategoryId);
        Assert.Equal("Footwear", saved.externalCategoryName);
    }

    [Fact]
    public async Task Mapping_UpdateFill_ChangesProvidedFields()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var mapping = data.AddMapping(data.AddConnection(store.Id).Id, data.AddCategory(store.Id).Id, "old", "Old");
        await data.SaveAsync();

        Assert.True(mapping.UpdateFill(new CategoryMapping.UpdateForm { externalCategoryId = "new", externalCategoryName = "New", name = "Renamed" }));
        await db.SaveChangesAsync();

        var saved = db.categoryMappings.Single();
        Assert.Equal("new", saved.externalCategoryId);
        Assert.Equal("New", saved.externalCategoryName);
        Assert.Equal("Renamed", saved.name);
    }

    [Fact]
    public async Task Mapping_UpdateFill_DoesNotReplaceFieldsWithEmptyValues()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var mapping = data.AddMapping(data.AddConnection(store.Id).Id, data.AddCategory(store.Id).Id, "original", "Original");
        await data.SaveAsync();

        mapping.UpdateFill(new CategoryMapping.UpdateForm { externalCategoryId = "" });
        await db.SaveChangesAsync();

        Assert.Equal("original", db.categoryMappings.Single().externalCategoryId);
    }

    [Fact]
    public async Task Mapping_GraphJson_PersistsWholeGraph()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var mapping = data.AddMapping(data.AddConnection(store.Id).Id, data.AddCategory(store.Id).Id);
        mapping.graphJson = """{"nodes":[{"id":"fields","type":"adapterFields","data":{"fields":[{"key":"brand"},{"key":"size"}]}}],"edges":[]}""";
        await data.SaveAsync();

        Assert.Equal(mapping.graphJson, db.categoryMappings.Single().graphJson);
    }

    [Fact]
    public async Task Mapping_Delete_RemovesOnlyTargetMapping()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id);
        var category = data.AddCategory(store.Id);
        var first = data.AddMapping(connection.Id, category.Id, "one");
        var second = data.AddMapping(connection.Id, category.Id, "two");
        await data.SaveAsync();

        db.categoryMappings.Remove(first);
        await db.SaveChangesAsync();

        Assert.Single(db.categoryMappings);
        Assert.Equal(second.Id, db.categoryMappings.Single().Id);
    }

    [Fact]
    public async Task Mapping_QueryByConnection_DoesNotReturnOtherStoreMappings()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var firstStore = data.AddStore("First");
        var secondStore = data.AddStore("Second");
        var firstConnection = data.AddConnection(firstStore.Id);
        var secondConnection = data.AddConnection(secondStore.Id);
        data.AddMapping(firstConnection.Id, data.AddCategory(firstStore.Id).Id, "first");
        data.AddMapping(secondConnection.Id, data.AddCategory(secondStore.Id).Id, "second");
        await data.SaveAsync();

        var firstMappings = db.categoryMappings.Where(m => m.connectionId == firstConnection.Id).ToList();

        Assert.Single(firstMappings);
        Assert.Equal("first", firstMappings[0].externalCategoryId);
    }

    [Fact]
    public async Task Mapping_SameCategoryAndConnection_AllowsDistinctMappings()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id);
        var category = data.AddCategory(store.Id);
        data.AddMapping(connection.Id, category.Id, "first", "First");
        data.AddMapping(connection.Id, category.Id, "second", "Second");
        await data.SaveAsync();

        var mappings = db.categoryMappings.Where(m => m.connectionId == connection.Id && m.localCategoryId == category.Id).ToList();
        Assert.Equal(2, mappings.Count);
    }

    [Theory]
    [InlineData("same", "Name one")]
    [InlineData("same", "Name two")]
    public async Task Mapping_DuplicateExternalFieldValues_CanBePersistedWithDifferentNames(string externalId, string name)
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id);
        var category = data.AddCategory(store.Id);
        data.AddMapping(connection.Id, category.Id, externalId, name);
        await data.SaveAsync();

        Assert.Equal(externalId, db.categoryMappings.Single().externalCategoryId);
        Assert.Equal(name, db.categoryMappings.Single().externalCategoryName);
    }

    [Fact]
    public async Task Mapping_AddFill_AssignsConnectionCategoryAndDisplayFields()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id);
        var category = data.AddCategory(store.Id);
        var mapping = new CategoryMapping();

        Assert.True(mapping.AddFill(new CategoryMapping.AddForm { connectionId = connection.Id, localCategoryId = category.Id, externalCategoryId = "external", externalCategoryName = "External", name = "Primary" }));
        db.categoryMappings.Add(mapping);
        await data.SaveAsync();

        Assert.Equal("Primary", db.categoryMappings.Single().name);
    }
}
