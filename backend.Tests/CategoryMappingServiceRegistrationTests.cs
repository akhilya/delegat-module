using System.Text.Json;
using Delegat.Controllers;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Security;
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

    private static ServiceProvider CreateProvider(bool aiEnabled)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSignalR();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddScoped(_ => TestDbFactory.CreateDb());
        services.AddScoped<ICurrentUserService, FakeCurrentUserService>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<IMarketplaceAdapterRegistry>(new SingleAdapterRegistry(new OrdersAdapterFake
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
