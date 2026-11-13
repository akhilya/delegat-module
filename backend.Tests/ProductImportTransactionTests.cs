using System.Reflection;
using Delegat.Enums;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services.Marketplace;
using Delegat.Services.Marketplace.Graph;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

public class ProductImportTransactionTests
{
    [Theory]
    [InlineData("products")]
    [InlineData("productGroupMarketplaceLinks")]
    [InlineData("productAttributeValues")]
    [InlineData("storeProductAttributeValues")]
    [InlineData("productMarketplaceLinks")]
    [InlineData("productVariantLinks")]
    [InlineData("pendingImageDownloads")]
    public async Task WriteFailure_RollsBackEntireImportAndRetryDoesNotDuplicate(string table)
    {
        var fault = new CatalogWriteFault();
        await using var db = await CreateDbAsync(fault);
        var (connection, userId, categoryId) = await SeedAsync(db);
        var adapter = Adapter(db);
        var proxy = (TransactionImportAdapterProxy)(object)adapter;
        var service = CatalogTransactionTestSupport.ImportService(db, adapter);
        fault.ShouldFail = command => command.CommandText.Contains($"INSERT INTO \"{table}\"");

        var failed = await service.ImportProductAsync(connection.Id, "external", categoryId, userId,
            batchId: "import", batchStoreId: connection.storeId, imageBatchId: "import-img");

        Assert.False(failed.IsSuccess);
        Assert.True(fault.Failures > 0);
        Assert.Equal(1, proxy.Calls); // Local retry does not fetch the marketplace again.
        await AssertNoImportAsync(db);
        Assert.DoesNotContain(db.ChangeTracker.Entries(), e => e.State is EntityState.Added or EntityState.Modified);
        fault.ShouldFail = null;
        // The queue worker's failure/status write cannot resurrect the failed graph.
        await db.SaveChangesAsync();
        await AssertNoImportAsync(db);

        var success = await service.ImportProductAsync(connection.Id, "external", categoryId, userId, imageBatchId: "import-img");
        Assert.True(success.IsSuccess, success.ErrorMessage);
        Assert.Equal(1, await db.products.CountAsync());
        Assert.Equal(1, await db.productAttributeValues.CountAsync());
        Assert.Equal(1, await db.storeProductAttributeValues.CountAsync());
        Assert.Equal(1, await db.productVariants.CountAsync());
        Assert.Equal(1, await db.productVariantLinks.CountAsync());
        Assert.Equal(1, await db.productMarketplaceLinks.CountAsync());
        Assert.Equal(1, await db.productGroups.CountAsync());
        Assert.Equal(1, await db.productGroupMembers.CountAsync());
        Assert.Equal(2, await db.pendingImageDownloads.CountAsync());
        Assert.Equal(2, proxy.Calls);

        var repeated = await service.ImportProductAsync(connection.Id, "external", categoryId, userId);
        Assert.True(repeated.AlreadyLinked);
        Assert.Equal(success.ProductId, repeated.ProductId);
        Assert.Equal(2, proxy.Calls);
        Assert.Equal(2, await db.pendingImageDownloads.CountAsync());
    }

    [Fact]
    public async Task ReplayFindsTrashedLinkedProductWithoutFetchingOrCreatingAnother()
    {
        await using var db = await CreateDbAsync(new CatalogWriteFault());
        var (connection, userId, categoryId) = await SeedAsync(db);
        var adapter = Adapter(db);
        var service = CatalogTransactionTestSupport.ImportService(db, adapter);
        var first = await service.ImportProductAsync(connection.Id, "external", categoryId, userId);
        Assert.True(first.IsSuccess, first.ErrorMessage);
        var product = await db.products.SingleAsync();
        product.deletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var second = await service.ImportProductAsync(connection.Id, "external", categoryId, userId);
        Assert.True(second.AlreadyLinked);
        Assert.Equal(first.ProductId, second.ProductId);
        Assert.Equal(1, await db.products.IgnoreQueryFilters().CountAsync());
        Assert.Equal(1, ((TransactionImportAdapterProxy)(object)adapter).Calls);
    }

    [Fact]
    public async Task PublicCopy_ReceiptIsAtomicAndScopesReplayToOneOperation()
    {
        var fault = new CatalogWriteFault();
        await using var db = await CreateDbAsync(fault);
        var (connection, userId, categoryId) = await SeedAsync(db);
        var adapter = Adapter(db);
        var source = ((TransactionImportAdapterProxy)(object)adapter).Source;
        var service = CatalogTransactionTestSupport.ImportService(db, adapter);
        var receiptTable = db.Model.FindEntityType(typeof(ProductImportReceipt))!.GetTableName();
        fault.ShouldFail = command => command.CommandText.Contains($"INSERT INTO \"{receiptTable}\"");
        var failed = await service.ImportUnlinkedExternalProductAsync(connection, source, categoryId, userId,
            CancellationToken.None, "public-op");
        Assert.False(failed.IsSuccess);
        await AssertNoImportAsync(db);
        Assert.Equal(0, await db.Set<ProductImportReceipt>().CountAsync());
        fault.ShouldFail = null;
        var first = await service.ImportUnlinkedExternalProductAsync(connection, source, categoryId, userId,
            CancellationToken.None, "public-op");
        var replay = await service.ImportUnlinkedExternalProductAsync(connection, source, categoryId, userId,
            CancellationToken.None, "public-op");
        Assert.True(first.IsSuccess, first.ErrorMessage);
        Assert.Equal(first.ProductId, replay.ProductId);
        Assert.Equal(1, await db.products.CountAsync());
        Assert.Equal(1, await db.Set<ProductImportReceipt>().CountAsync());
        Assert.Equal(0, await db.productMarketplaceLinks.CountAsync());
        Assert.Equal(0, await db.productVariantLinks.CountAsync());
        var explicitNewCopy = await service.ImportUnlinkedExternalProductAsync(connection, source, categoryId, userId,
            CancellationToken.None, "another-public-op");
        Assert.True(explicitNewCopy.IsSuccess, explicitNewCopy.ErrorMessage);
        Assert.NotEqual(first.ProductId, explicitNewCopy.ProductId);
    }

    [Fact]
    public async Task CancellationDuringSave_RollsBackAndLeavesNoTrackedWrites()
    {
        var fault = new CatalogWriteFault();
        await using var db = await CreateDbAsync(fault);
        var (connection, userId, categoryId) = await SeedAsync(db);
        var service = CatalogTransactionTestSupport.ImportService(db, Adapter(db));
        using var cancellation = new CancellationTokenSource();
        fault.ShouldFail = command => command.CommandText.Contains("INSERT INTO \"productVariantLinks\"");
        fault.BeforeFailure = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ImportProductAsync(connection.Id,
            "external", categoryId, userId, cancellation.Token));
        fault.ShouldFail = null;
        await db.SaveChangesAsync();
        await AssertNoImportAsync(db);
    }

    private static IMarketplaceAdapter Adapter(DataContext db)
    {
        var adapter = DispatchProxy.Create<IMarketplaceAdapter, TransactionImportAdapterProxy>();
        var proxy = (TransactionImportAdapterProxy)(object)adapter;
        proxy.Db = db;
        proxy.Source = new ExternalProduct("external", "SKU", "Imported", Price: 100m,
            Attributes: new Dictionary<string, object> { ["material"] = "Cotton", ["brand"] = "Brand" },
            ImageUrls: ["https://cdn.example.test/photo.jpg", "https://cdn.example.test/photo.jpg"],
            Variants: [new ExternalProductVariant("size-1", "M", "46", Price: 120m)],
            GroupId: "remote-group", GroupName: "Imported group")
        {
            Media = [new Delegat.Marketplace.SDK.DTOs.ExternalMedia("video", "https://8.8.8.8/video.mp4", FileName: "clip.mp4")]
        };
        return adapter;
    }

    [Fact]
    public async Task VariantBarcode_IsNotAlsoAssignedToParentCard()
    {
        await using var db = TestDbFactory.CreateDb();
        var (connection, userId, categoryId) = await SeedAsync(db);
        var adapter = Adapter(db);
        var proxy = (TransactionImportAdapterProxy)(object)adapter;
        proxy.Source = proxy.Source with
        {
            Barcode = "2000000000015",
            Variants = [new ExternalProductVariant("size-1", "M", "46", Barcodes: ["2000000000015"])]
        };
        var result = await CatalogTransactionTestSupport.ImportService(db, adapter)
            .ImportProductAsync(connection.Id, "external", categoryId, userId);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Null((await db.products.SingleAsync()).barcode);
        Assert.Equal("2000000000015", (await db.productVariants.SingleAsync()).barcode);
    }

    private static async Task<(MarketplaceConnection Connection, int UserId, int CategoryId)> SeedAsync(DataContext db)
    {
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore();
        var category = data.AddCategory(store.Id);
        var attribute = data.AddCategoryAttribute(category.Id);
        var storeAttribute = data.AddStoreAttribute(store.Id);
        var connection = data.AddConnection(store.Id, adapterKey: "fake");
        var mapping = data.AddMapping(connection.Id, category.Id, "remote-category");
        mapping.graphJson = GraphGenerator.BuildGraphJson([
            new FieldSpec { externalFieldKey = "material", externalFieldType = "string", sourceType = FieldMappingSourceType.CategoryAttribute, sourceAttributeId = attribute.Id },
            new FieldSpec { externalFieldKey = "brand", externalFieldType = "string", sourceType = FieldMappingSourceType.StoreAttribute, sourceAttributeId = storeAttribute.Id }
        ]);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return (connection, user.Id, category.Id);
    }

    private static async Task AssertNoImportAsync(DataContext db)
    {
        Assert.Equal(0, await db.products.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await db.productAttributeValues.CountAsync());
        Assert.Equal(0, await db.storeProductAttributeValues.CountAsync());
        Assert.Equal(0, await db.productMarketplaceLinks.CountAsync());
        Assert.Equal(0, await db.productVariantLinks.CountAsync());
        Assert.Equal(0, await db.productVariants.CountAsync());
        Assert.Equal(0, await db.productGroups.CountAsync());
        Assert.Equal(0, await db.pendingImageDownloads.CountAsync());
    }

    private static async Task<DataContext> CreateDbAsync(CatalogWriteFault fault)
    {
        var db = new DataContext(new DbContextOptionsBuilder<DataContext>().UseSqlite("Data Source=:memory:").AddInterceptors(fault).Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        return db;
    }
}
