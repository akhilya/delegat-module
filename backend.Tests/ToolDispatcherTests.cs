using System.Text.Json;
using Delegat.Services.Ai;
using Delegat.Services.Ai.Tools;
using Delegat.Services.Ai.Tools.Handlers;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class ToolDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_UnknownTool_ReturnsError()
    {
        await using var db = TestDbFactory.CreateDb();
        var dispatcher = CreateDispatcher(db);

        var (result, summary) = await dispatcher.DispatchAsync("does_not_exist", "{}", 1, 1, default);

        Assert.Contains("Unknown tool: does_not_exist", JsonSerializer.Serialize(result));
        Assert.Contains("Неизвестный инструмент", summary);
    }

    [Theory]
    [InlineData("""{"url":"/products/12","label":"Products"}""", true)]
    [InlineData("""{"url":"javascript:alert(1)"}""", false)]
    public async Task DispatchAsync_NavigateTo_AcceptsOnlySafePaths(string input, bool expected)
    {
        await using var db = TestDbFactory.CreateDb();
        var dispatcher = CreateDispatcher(db);

        var (result, _) = await dispatcher.DispatchAsync("navigate_to", input, 1, 1, default);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.Equal(expected, json.RootElement.GetProperty("navigate").GetBoolean());
    }

    [Fact]
    public async Task DispatchAsync_NavigateTo_RejectsOverlongPath()
    {
        await using var db = TestDbFactory.CreateDb();
        var dispatcher = CreateDispatcher(db);
        var input = JsonSerializer.Serialize(new { url = "/" + new string('a', 200) });

        var (result, _) = await dispatcher.DispatchAsync("navigate_to", input, 1, 1, default);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.False(json.RootElement.GetProperty("navigate").GetBoolean());
    }

    [Fact]
    public async Task DispatchAsync_ListTools_ReturnSeededStoreData()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var category = data.AddCategory(store.Id, "Books");
        var product = data.AddProduct(store.Id, "Foundation", categoryId: category.Id);
        data.AddGroup(store.Id, "Sci-fi", product);
        data.AddConnection(store.Id, "yandex", "Yandex");
        data.AddStoreAttribute(store.Id, "Brand");
        await data.SaveAsync();
        var dispatcher = CreateDispatcher(db);

        var productList = await dispatcher.DispatchAsync("list_products", "{}", store.Id, 1, default);
        var categoryList = await dispatcher.DispatchAsync("list_categories", "{}", store.Id, 1, default);
        var groupList = await dispatcher.DispatchAsync("list_groups", "{}", store.Id, 1, default);
        var connectionList = await dispatcher.DispatchAsync("list_connections", "{}", store.Id, 1, default);
        var attributes = await dispatcher.DispatchAsync("list_store_attributes", "{}", store.Id, 1, default);
        var stats = await dispatcher.DispatchAsync("get_store_stats", "{}", store.Id, 1, default);

        Assert.Equal(1, GetInt(productList.result, "count"));
        Assert.Equal(1, GetInt(categoryList.result, "count"));
        Assert.Equal(1, GetInt(groupList.result, "count"));
        Assert.Equal(1, GetInt(connectionList.result, "count"));
        Assert.Equal(1, GetInt(attributes.result, "count"));
        Assert.Equal(1, GetInt(stats.result, "products"));
        Assert.Equal(1, GetInt(stats.result, "categories"));
        Assert.Equal(1, GetInt(stats.result, "groups"));
    }

    [Fact]
    public async Task DispatchAsync_CreateTools_PersistProductCategoryAndGroup()
    {
        await using var db = TestDbFactory.CreateDb();
        var store = new TestDataBuilder(db).AddStore();
        await db.SaveChangesAsync();
        var dispatcher = CreateDispatcher(db);

        await dispatcher.DispatchAsync("create_category", """{"name":"Books","description":"Print"}""", store.Id, 1, default);
        var category = await db.categories.SingleAsync();
        await dispatcher.DispatchAsync("create_product", $$"""{"name":"Foundation","sku":"BK-1","price":10,"categoryId":{{category.Id}}}""", store.Id, 1, default);
        var product = await db.products.SingleAsync();
        await dispatcher.DispatchAsync("create_group", $$"""{"name":"Sci-fi","productIds":[{{product.Id}}]}""", store.Id, 1, default);

        Assert.Equal("Books", category.name);
        Assert.Equal("Foundation", product.name);
        Assert.Equal(store.Id, product.storeId);
        var group = await db.productGroups.SingleAsync();
        Assert.Equal("Sci-fi", group.name);
        Assert.Equal(product.Id, (await db.productGroupMembers.SingleAsync()).productId);
    }

    [Fact]
    public async Task DispatchAsync_DeleteProducts_SoftDeletesMatchingProducts()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var product = data.AddProduct(store.Id);
        await data.SaveAsync();
        var dispatcher = CreateDispatcher(db);

        await dispatcher.DispatchAsync("delete_products", $$"""{"ids":[{{product.Id}}]}""", store.Id, 1, default);

        var deleted = await db.products.IgnoreQueryFilters().SingleAsync();
        Assert.NotNull(deleted.deletedAt);
    }

    [Fact]
    public async Task DispatchAsync_MissingEntity_ReturnsErrorObject()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        await data.SaveAsync();
        var dispatcher = CreateDispatcher(db);

        var (result, summary) = await dispatcher.DispatchAsync(
            "get_product", """{"id":999999}""", store.Id, 1, default);

        Assert.Contains("error", JsonSerializer.Serialize(result), StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(summary));
    }

    private static ToolDispatcher CreateDispatcher(DataContext db)
    {
        var operations = new AiOperationService(db);
        var graphMutation = new GraphMutationService(db);
        var intelligence = new MappingIntelligenceService(db);

        return new ToolDispatcher(
            new ProductToolHandlers(db, operations, NullLogger<ProductToolHandlers>.Instance),
            new CategoryToolHandlers(db, operations, NullLogger<CategoryToolHandlers>.Instance),
            new MappingToolHandlers(db, operations, graphMutation, intelligence, null!, NullLogger<MappingToolHandlers>.Instance),
            new GroupToolHandlers(db, operations, NullLogger<GroupToolHandlers>.Instance),
            new SyncToolHandlers(db, operations, null!, null!, NullLogger<SyncToolHandlers>.Instance),
            new FileToolHandlers(db, operations, null!, null!, NullLogger<FileToolHandlers>.Instance),
            new AnalyticsToolHandlers(db, graphMutation, NullLogger<AnalyticsToolHandlers>.Instance),
            new RollbackToolHandlers(db, operations, NullLogger<RollbackToolHandlers>.Instance),
            NullLogger<ToolDispatcher>.Instance);
    }

    private static int GetInt(object result, string property)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        return json.RootElement.GetProperty(property).GetInt32();
    }
}
