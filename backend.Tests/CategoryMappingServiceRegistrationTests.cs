using System.Text.Json;
using Delegat.Controllers;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Security;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services;
using Delegat.Services.Ai;
using Delegat.Services.Marketplace;
using Delegat.Services.Marketplace.Graph;
using Delegat.Services.Plugins;
using Delegat.Tests.Fakes;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Delegat.Tests;

public class CategoryMappingServiceRegistrationTests
{
    [Fact]
    public async Task ExternalCategoryFields_IncludeNormalizedNamesAndImportSettings()
    {
        var adapter = new OrdersAdapterFake
        {
            Capabilities = MarketplaceCapabilities.GetCategoryFields,
            CategoryFields =
            [
                new ExternalField(
                    "height", "Height (mm)", false, "number",
                    Description: "Product height in millimeters",
                    MinValue: 1,
                    MaxValue: 100,
                    ValidationPattern: "^[0-9]+$"),
                new ExternalField(
                    "height_with_packaging", "Height (mm)", false, "number",
                    Description: "Product height in millimeters (including packaging)"
                        + "Product height in millimeters (including packaging)"),
                new ExternalField(
                    "brand", "Brand", true, "select",
                    [new ExternalFieldOption("none", "No brand")],
                    Description: "Choose a brand",
                    MaxValues: 4,
                    AllowCustomValues: true),
                new ExternalField(
                    "21841", "Озон.Видео: ссылка", false, "string",
                    MaxLength: 100, ValidationPattern: "^https://", MaxValues: 3,
                    AllowCustomValues: true),
            ],
        };
        await using var provider = CreateProvider(aiEnabled: false, adapter: adapter);
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<DataContext>();
        var store = new TestDataBuilder(db).AddStore();
        var connection = new TestDataBuilder(db).AddConnection(store.Id);
        await db.SaveChangesAsync();
        ((FakeCurrentUserService)sp.GetRequiredService<ICurrentUserService>()).SelectedStoreId = store.Id;

        var controller = ActivatorUtilities.CreateInstance<CategoryMappingController>(sp);
        var result = await controller.GetExternalCategoryFields(connection.Id, "remote-category");
        var body = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(result).Value);
        var field = body.EnumerateArray()
            .Single(item => item.GetProperty("key").GetString() == "height");

        Assert.Equal("Height (mm)", field.GetProperty("name").GetString());
        Assert.Equal("Высота", field.GetProperty("importName").GetString());
        Assert.Equal("Number", field.GetProperty("importType").GetString());
        Assert.Equal("mm", field.GetProperty("unitCode").GetString());
        Assert.Equal(1, field.GetProperty("minValue").GetInt32());
        Assert.Equal(100, field.GetProperty("maxValue").GetInt32());
        Assert.Equal("^[0-9]+$", field.GetProperty("validationPattern").GetString());
        Assert.True(field.GetProperty("descriptionInTooltip").GetBoolean());
        Assert.Equal(JsonValueKind.Null, field.GetProperty("options").ValueKind);

        var packagedHeight = body.EnumerateArray()
            .Single(item => item.GetProperty("key").GetString() == "height_with_packaging");
        Assert.Equal("Высота с упаковкой", packagedHeight.GetProperty("importName").GetString());

        var video = body.EnumerateArray()
            .Single(item => item.GetProperty("key").GetString() == "21841");
        Assert.Equal("Видео", video.GetProperty("importName").GetString());
        Assert.Equal("File", video.GetProperty("importType").GetString());
        Assert.Equal("string", video.GetProperty("type").GetString());
        Assert.True(video.GetProperty("allowMultipleFiles").GetBoolean());
        Assert.False(video.GetProperty("allowCustomValues").GetBoolean());
        Assert.Equal(JsonValueKind.Null, video.GetProperty("maxLength").ValueKind);
        Assert.Equal(JsonValueKind.Null, video.GetProperty("validationPattern").ValueKind);
        Assert.Equal(JsonValueKind.Null, video.GetProperty("maxValues").ValueKind);

        var detailsResult = await controller.GetExternalCategoryFields(
            connection.Id, "remote-category", "brand");
        var detailsBody = JsonSerializer.SerializeToElement(
            Assert.IsType<OkObjectResult>(detailsResult).Value);
        var brand = Assert.Single(detailsBody.EnumerateArray());

        Assert.Equal("Бренд", brand.GetProperty("importName").GetString());
        Assert.Equal("brand", brand.GetProperty("attributeKey").GetString());
        Assert.Equal("none", brand.GetProperty("defaultValue").GetString());
        Assert.True(brand.GetProperty("allowCustomValues").GetBoolean());
        Assert.Equal(4, brand.GetProperty("maxValues").GetInt32());
        Assert.Equal("ozon", brand.GetProperty("attributeGroup").GetString());
        Assert.Equal("adapter:ozon", brand.GetProperty("groupIcon").GetString());
        var option = Assert.Single(brand.GetProperty("options").EnumerateArray());
        Assert.Equal("none", option.GetProperty("value").GetString());
        Assert.Equal("No brand", option.GetProperty("label").GetString());
    }

    [Theory]
    [InlineData(false, "mappings")]
    [InlineData(false, "local-categories")]
    [InlineData(false, "external-categories")]
    [InlineData(true, "mappings")]
    [InlineData(true, "local-categories")]
    [InlineData(true, "external-categories")]
    public async Task ReadEndpoints_ActivateControllerAndReturnData(bool aiEnabled, string endpoint)
    {
        await using var provider = CreateProvider(aiEnabled);
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<DataContext>();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var category = data.AddCategory(store.Id);
        var connection = data.AddConnection(store.Id);
        var mapping = data.AddMapping(connection.Id, category.Id, "external-1");
        await db.SaveChangesAsync();
        ((FakeCurrentUserService)sp.GetRequiredService<ICurrentUserService>()).SelectedStoreId = store.Id;

        // Use DI activation rather than manually supplying the graph service: this failed with AI off.
        var controller = ActivatorUtilities.CreateInstance<CategoryMappingController>(sp);
        var result = endpoint switch
        {
            "mappings" => await controller.GetMappings(connection.Id),
            "local-categories" => await controller.GetLocalCategories(connection.Id),
            _ => await controller.GetExternalCategories(connection.Id),
        };

        var body = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(result).Value);
        if (endpoint == "external-categories")
            Assert.Equal(0, body.GetArrayLength()); // The fake adapter has no remote categories.
        else
        {
            var item = Assert.Single(body.EnumerateArray());
            Assert.Equal(endpoint == "mappings" ? mapping.Id : category.Id, item.GetProperty("id").GetInt32());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GraphEditing_PersistsDomainMutationWithoutRequiringAi(bool aiEnabled)
    {
        await using var provider = CreateProvider(aiEnabled);
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<DataContext>();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var category = data.AddCategory(store.Id);
        var connection = data.AddConnection(store.Id);
        var mapping = data.AddMapping(connection.Id, category.Id, "external-1");
        await db.SaveChangesAsync();
        ((FakeCurrentUserService)sp.GetRequiredService<ICurrentUserService>()).SelectedStoreId = store.Id;

        var controller = ActivatorUtilities.CreateInstance<CategoryMappingController>(sp);
        Assert.IsType<OkObjectResult>(await controller.ConnectMappingGraphField(
            connection.Id, mapping.Id,
            new CategoryMappingController.GraphConnectInput
            {
                adapterFieldKey = "name",
                sourceType = "productField",
                sourceField = "name",
            }, CancellationToken.None));

        db.ChangeTracker.Clear();
        var saved = await db.categoryMappings.FindAsync(mapping.Id);
        var graph = JsonSerializer.Deserialize<MappingGraph>(saved!.graphJson!);
        var sink = Assert.Single(graph!.nodes, n => n.type == "adapterFields");
        var field = Assert.Single(sink.data!.fields!, f => f.fieldKey == "name");
        Assert.Equal("productField", field.source!.type);
        Assert.Equal("name", field.source.field);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MappingServices_AreScoped_AndKeepAiOptional(bool aiEnabled)
    {
        await using var provider = CreateProvider(aiEnabled);
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var graph = first.ServiceProvider.GetRequiredService<GraphMutationService>();
        Assert.Same(graph, first.ServiceProvider.GetRequiredService<GraphMutationService>());
        Assert.NotSame(graph, second.ServiceProvider.GetRequiredService<GraphMutationService>());
        Assert.NotNull(first.ServiceProvider.GetRequiredService<MappingService>());
        Assert.NotNull(first.ServiceProvider.GetRequiredService<ProductMappingSelectionService>());
        Assert.Equal(aiEnabled, first.ServiceProvider.GetService<GenerativeMappingService>() != null);
        if (!aiEnabled)
            Assert.Null(first.ServiceProvider.GetService<AiChatService>());
    }

    private static ServiceProvider CreateProvider(bool aiEnabled, OrdersAdapterFake? adapter = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSignalR();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddScoped(_ => TestDbFactory.CreateDb());
        services.AddScoped<ICurrentUserService, FakeCurrentUserService>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<IMarketplaceAdapterRegistry>(new SingleAdapterRegistry(adapter ?? new OrdersAdapterFake
        {
            Capabilities = MarketplaceCapabilities.GetCategories,
        }));
        services.AddScoped<CategoryImportQueueService>();
        services.AddSingleton<PluginManager>();
        services.AddScoped<PluginVersionSyncService>();
        services.AddSingleton<Delegat.Marketplace.SDK.IMarketplaceMetadataCache, MarketplaceMetadataCache>();
        services.AddSingleton<IOperationLogger, OperationLogger>();
        services.AddSingleton(new EncryptionService(Convert.ToBase64String(new byte[32])));
        if (aiEnabled)
            services.AddAiAssistant();
        services.AddMarketplaceMappings(aiEnabled);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
