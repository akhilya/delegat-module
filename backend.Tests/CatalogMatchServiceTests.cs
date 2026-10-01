using Delegat.Infrastructure.Middleware;
using Delegat.Marketplace.SDK.RateLimit;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services;
using Delegat.Services.Marketplace;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

[Collection("EnvVars")]
public class CatalogMatchServiceTests
{
    [Fact]
    public async Task Preview_InactiveConnection_Throws()
    {
        var (svc, connection, _) = await SeedAsync();
        connection.isActive = false;

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            svc.PreviewAsync(connection, 1, 50, "all", CancellationToken.None));

        Assert.Equal("connection_inactive", ex.Code);
    }

    [Fact]
    public async Task QueueUnlinkedImport_InactiveConnection_Throws()
    {
        var (svc, connection, _) = await SeedAsync();
        connection.isActive = false;

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            svc.QueueUnlinkedImportAsync(connection, userId: 1, CancellationToken.None));

        Assert.Equal("connection_inactive", ex.Code);
    }

    [Fact]
    public async Task Apply_InactiveConnection_Throws()
    {
        var (svc, connection, _) = await SeedAsync();
        connection.isActive = false;

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            svc.ApplyAsync(connection, [], importUnmatched: false, userId: 1, CancellationToken.None));

        Assert.Equal("connection_inactive", ex.Code);
    }

    [Fact]
    public async Task Preview_FromLocal_DoesNotRequireImportAllProducts()
    {
        var (svc, connection, adapter) = await SeedAsync();
        adapter.Add(new ExternalProduct("oz-1", "ART-1", "Remote card", CategoryId: "10:2"));

        var preview = await svc.PreviewAsync(connection, 1, 50, "all", CancellationToken.None);

        Assert.Equal("fromLocal", preview.Mode);
        var row = Assert.Single(preview.Items);
        Assert.Equal("matched", row.Status);
        Assert.Equal("sku", row.MatchedBy);
        Assert.Equal("oz-1", row.ExternalProductId);
        Assert.Equal("10:2", row.ExternalCategoryId);
    }

    [Fact]
    public void PreviewMode_LooksUpLocalProductsWhenFewEvenIfAdapterCanListCatalog()
    {
        Assert.Equal(
            "fromLocal",
            CatalogMatchService.ChoosePreviewMode(hasAll: true, hasOne: true, localCount: 10));
    }

    [Fact]
    public void PreviewMode_ListsRemoteCatalogOnlyWhenLocalLookupWouldBeTooLarge()
    {
        Assert.Equal(
            "fromRemote",
            CatalogMatchService.ChoosePreviewMode(
                hasAll: true,
                hasOne: true,
                localCount: CatalogMatchService.LocalLookupThreshold + 1));
    }

    [Fact]
    public async Task Preview_FromLocal_DoesNotScanWholeRemoteCatalogWhenFewLocalProducts()
    {
        var (svc, connection, adapter) = await SeedAsync();
        adapter.Capabilities |= MarketplaceCapabilities.ImportAllProducts;
        adapter.Add(new ExternalProduct("wb-1", "ART-1", "Remote card", CategoryId: "10:2"));
        adapter.Catalog.Add(new ExternalProduct("wb-1", "ART-1", "Remote card", CategoryId: "10:2"));
        adapter.Catalog.Add(new ExternalProduct("wb-2", "OTHER", "Unrelated"));

        var preview = await svc.PreviewAsync(connection, 1, 50, "all", CancellationToken.None);

        Assert.Equal("fromLocal", preview.Mode);
        Assert.Equal(0, adapter.ImportAllCalls);
        Assert.True(adapter.ImportProductsBatchCalls + adapter.ImportProductCalls > 0);
        var row = Assert.Single(preview.Items);
        Assert.Equal("matched", row.Status);
        Assert.Equal("wb-1", row.ExternalProductId);
        Assert.Equal("10:2", row.ExternalCategoryId);
    }

    [Fact]
    public async Task Preview_FromLocal_RejectsLookupThatDoesNotMatchSkuOrBarcode()
    {
        var (svc, connection, adapter) = await SeedAsync();
        adapter.OnMiss = new ExternalProduct("oz-9", "OTHER", "Wrong card");

        var preview = await svc.PreviewAsync(connection, 1, 50, "all", CancellationToken.None);

        var row = Assert.Single(preview.Items);
        Assert.Equal("unmatched", row.Status);
        Assert.Equal(1, row.ProductId);
    }

    [Fact]
    public async Task Apply_CreatesMappingForRemoteCategoryAndReusesIt()
    {
        var (svc, connection, adapter, db, p1, p2) = await SeedTwoAsync();
        Assert.NotNull(p2);
        adapter.Add(new ExternalProduct("oz-1", "ART-1", "One", CategoryId: "10:2", CategoryName: "T-shirts"));
        adapter.Add(new ExternalProduct("oz-2", "ART-2", "Two", CategoryId: "10:2", CategoryName: "T-shirts"));
        adapter.Fields["10:2"] =
        [
            new ExternalField("sku", "SKU", true, "string"),
            new ExternalField("name", "Name", true, "string"),
            new ExternalField("brand", "Brand", false, "string"),
        ];

        var result = await svc.ApplyAsync(
            connection,
            [
                new CatalogMatchApplyItem { ProductId = p1.Id, ExternalProductId = "oz-1" },
                new CatalogMatchApplyItem { ProductId = p2.Id, ExternalProductId = "oz-2" },
            ],
            importUnmatched: false,
            userId: 1,
            CancellationToken.None);

        Assert.Equal(2, result.Linked);
        Assert.Equal(0, result.Failed);
        Assert.Equal(1, result.MappingsCreated);
        Assert.Equal(1, await db.categoryMappings.CountAsync());

        var links = await db.productMarketplaceLinks.OrderBy(l => l.productId).ToListAsync();
        Assert.Equal(2, links.Count);
        Assert.NotNull(links[0].categoryMappingId);
        Assert.Equal(links[0].categoryMappingId, links[1].categoryMappingId);

        var mapping = await db.categoryMappings.SingleAsync();
        Assert.Equal("T-shirts", mapping.externalCategoryName);
        Assert.Equal("T-shirts", mapping.name);
    }

    [Fact]
    public async Task Apply_MappingUsesLocalCategoryNameWhenRemoteOmitsCategoryName()
    {
        var (svc, connection, adapter, db, p1, _) = await SeedTwoAsync(secondProduct: false);
        adapter.Add(new ExternalProduct("oz-1", "ART-1", "One", CategoryId: "10:2"));
        adapter.Fields["10:2"] =
        [
            new ExternalField("sku", "SKU", true, "string"),
            new ExternalField("name", "Name", true, "string"),
        ];

        var result = await svc.ApplyAsync(
            connection,
            [new CatalogMatchApplyItem { ProductId = p1.Id, ExternalProductId = "oz-1" }],
            importUnmatched: false,
            userId: 1,
            CancellationToken.None);

        Assert.Equal(1, result.Linked);
        Assert.Equal(1, result.MappingsCreated);
        var mapping = await db.categoryMappings.SingleAsync();
        Assert.Equal("Local", mapping.externalCategoryName);
        Assert.Equal("Local", mapping.name);
        Assert.DoesNotContain("Category ", mapping.externalCategoryName);
    }

    [Fact]
    public async Task Apply_StillLinksWhenRemoteHasNoCategory()
    {
        var (svc, connection, adapter, db, p1, _) = await SeedTwoAsync(secondProduct: false);
        adapter.Add(new ExternalProduct("oz-1", "ART-1", "One"));

        var result = await svc.ApplyAsync(
            connection,
            [new CatalogMatchApplyItem { ProductId = p1.Id, ExternalProductId = "oz-1" }],
            importUnmatched: false,
            userId: 1,
            CancellationToken.None);

        Assert.Equal(1, result.Linked);
        Assert.Equal(0, result.MappingsCreated);
        Assert.Empty(db.categoryMappings);
        Assert.Null((await db.productMarketplaceLinks.SingleAsync()).categoryMappingId);
    }

    [Fact]
    public async Task Apply_CreatesLocalCategoryWhenProductHasNone()
    {
        var (svc, connection, adapter, db, p1, _) = await SeedUncategorizedAsync();
        adapter.Add(new ExternalProduct("oz-1", "ART-1", "One", CategoryId: "10:2", CategoryName: "T-shirts"));
        adapter.Fields["10:2"] =
        [
            new ExternalField("sku", "SKU", true, "string"),
            new ExternalField("name", "Name", true, "string"),
            new ExternalField("brand", "Brand", false, "string"),
        ];

        var result = await svc.ApplyAsync(
            connection,
            [new CatalogMatchApplyItem { ProductId = p1.Id, ExternalProductId = "oz-1" }],
            importUnmatched: false,
            userId: 1,
            CancellationToken.None);

        Assert.Equal(1, result.Linked);
        Assert.Equal(1, result.MappingsCreated);
        Assert.Equal(1, await db.categories.CountAsync());
        var product = await db.products.SingleAsync();
        Assert.NotNull(product.categoryId);
        var mapping = await db.categoryMappings.SingleAsync();
        Assert.Equal(product.categoryId, mapping.localCategoryId);
        Assert.Equal("10:2", mapping.externalCategoryId);
        Assert.Equal("T-shirts", mapping.externalCategoryName);
        Assert.Equal(mapping.Id, (await db.productMarketplaceLinks.SingleAsync()).categoryMappingId);
    }

    [Fact]
    public async Task Apply_ReusesCreatedCategoryForUncategorizedSiblings()
    {
        var (svc, connection, adapter, db, p1, p2) = await SeedUncategorizedAsync(secondProduct: true);
        Assert.NotNull(p2);
        adapter.Add(new ExternalProduct("oz-1", "ART-1", "One", CategoryId: "10:2", CategoryName: "T-shirts"));
        adapter.Add(new ExternalProduct("oz-2", "ART-2", "Two", CategoryId: "10:2", CategoryName: "T-shirts"));
        adapter.Fields["10:2"] =
        [
            new ExternalField("sku", "SKU", true, "string"),
            new ExternalField("name", "Name", true, "string"),
        ];

        var result = await svc.ApplyAsync(
            connection,
            [
                new CatalogMatchApplyItem { ProductId = p1.Id, ExternalProductId = "oz-1" },
                new CatalogMatchApplyItem { ProductId = p2.Id, ExternalProductId = "oz-2" },
            ],
            importUnmatched: false,
            userId: 1,
            CancellationToken.None);

        Assert.Equal(2, result.Linked);
        Assert.Equal(1, result.MappingsCreated);
        Assert.Equal(1, await db.categories.CountAsync());
        var categoryId = (await db.categories.SingleAsync()).Id;
        Assert.All(await db.products.ToListAsync(), p => Assert.Equal(categoryId, p.categoryId));
        Assert.All(await db.productMarketplaceLinks.ToListAsync(), l => Assert.NotNull(l.categoryMappingId));
    }

    [Fact]
    public async Task Apply_KeepsPreviewCategoryWhenLookupReturnsPlaceholder()
    {
        var (svc, connection, adapter, db, p1, _) = await SeedTwoAsync(secondProduct: false);
        adapter.Add(new ExternalProduct("oz-1", "ART-1", "One", CategoryId: "0"));
        adapter.Fields["10:2"] =
        [
            new ExternalField("sku", "SKU", true, "string"),
            new ExternalField("name", "Name", true, "string"),
        ];

        var result = await svc.ApplyAsync(
            connection,
            [new CatalogMatchApplyItem
            {
                ProductId = p1.Id,
                ExternalProductId = "oz-1",
                ExternalCategoryId = "10:2",
                ExternalCategoryName = "T-shirts",
            }],
            importUnmatched: false,
            userId: 1,
            CancellationToken.None);

        Assert.Equal(1, result.Linked);
        Assert.Equal(1, result.MappingsCreated);
        Assert.Equal("10:2", (await db.categoryMappings.SingleAsync()).externalCategoryId);
    }

    [Fact]
    public async Task Apply_LoadsCardsInOneBatchInsteadOfPerLink()
    {
        var (svc, connection, adapter, _, p1, p2) = await SeedTwoAsync();
        Assert.NotNull(p2);
        adapter.Add(new ExternalProduct("wb-1", "ART-1", "One", CategoryId: "10:2", CategoryName: "T-shirts"));
        adapter.Add(new ExternalProduct("wb-2", "ART-2", "Two", CategoryId: "10:2", CategoryName: "T-shirts"));
        adapter.Fields["10:2"] =
        [
            new ExternalField("sku", "SKU", true, "string"),
            new ExternalField("name", "Name", true, "string"),
        ];

        var result = await svc.ApplyAsync(
            connection,
            [
                new CatalogMatchApplyItem { ProductId = p1.Id, ExternalProductId = "wb-1", ExternalCategoryId = "10:2" },
                new CatalogMatchApplyItem { ProductId = p2.Id, ExternalProductId = "wb-2", ExternalCategoryId = "10:2" },
            ],
            importUnmatched: false,
            userId: 1,
            CancellationToken.None);

        Assert.Equal(2, result.Linked);
        Assert.Equal(1, adapter.ImportProductsBatchCalls);
        Assert.Equal(0, adapter.ImportProductCalls);
    }

    [Fact]
    public async Task Apply_CopiesMappedAttributeValuesFromBatchCards()
    {
        var (svc, connection, adapter, db, p1, _) = await SeedTwoAsync(secondProduct: false);
        adapter.Add(new ExternalProduct(
            "wb-1",
            "ART-1",
            "One",
            CategoryId: "10:2",
            CategoryName: "T-shirts",
            Attributes: new Dictionary<string, object> { ["brand"] = "Acme" },
            Brand: "Acme"));
        adapter.Fields["10:2"] =
        [
            new ExternalField("sku", "SKU", true, "string"),
            new ExternalField("name", "Name", true, "string"),
            new ExternalField("brand", "Brand", false, "string"),
        ];

        var result = await svc.ApplyAsync(
            connection,
            [new CatalogMatchApplyItem
            {
                ProductId = p1.Id,
                ExternalProductId = "wb-1",
                ExternalCategoryId = "10:2",
                ExternalCategoryName = "T-shirts",
            }],
            importUnmatched: false,
            userId: 1,
            CancellationToken.None);

        Assert.Equal(1, result.Linked);
        Assert.True(result.AttributesImported > 0);
        Assert.Contains(
            await db.productAttributeValues.ToListAsync(),
            v => v.value == "Acme");
    }

    [Fact]
    public async Task Apply_UnmatchedImportQueuesCategoryJob()
    {
        var (svc, connection, adapter, db, _, _) = await SeedTwoAsync(secondProduct: false);
        adapter.Add(new ExternalProduct("oz-9", "OTHER", "New card", CategoryId: "10:2", CategoryName: "T-shirts"));

        var result = await svc.ApplyAsync(
            connection,
            [new CatalogMatchApplyItem
            {
                ExternalProductId = "oz-9",
                ExternalCategoryId = "10:2",
                ExternalCategoryName = "T-shirts",
            }],
            importUnmatched: true,
            userId: 1,
            CancellationToken.None);

        Assert.Equal(1, result.Imported);
        var pending = await db.pendingProductImports.SingleAsync();
        Assert.Equal("10:2", pending.externalCategoryId);
        Assert.Equal("10:2", (await db.pendingCategoryImports.SingleAsync()).externalCategoryId);
    }

    [Fact]
    public async Task QueueUnlinkedImport_QueuesCategoryJobs()
    {
        var (svc, connection, adapter, db, _, _) = await SeedTwoAsync(secondProduct: false);
        adapter.Capabilities |= MarketplaceCapabilities.ImportAllProducts;
        adapter.Catalog.Add(new ExternalProduct("wb-1", "ART-9", "Remote", CategoryId: "42", CategoryName: "Tees"));

        var result = await svc.QueueUnlinkedImportAsync(connection, userId: 1, CancellationToken.None);

        Assert.Equal(1, result.Queued);
        var pending = await db.pendingProductImports.SingleAsync();
        Assert.Equal("42", pending.externalCategoryId);
        Assert.False(pending.forceImportWithoutMapping);
        Assert.Equal("42", (await db.pendingCategoryImports.SingleAsync()).externalCategoryId);
    }

    [Fact]
    public async Task RepairMissingMappings_CreatesCategoryForExistingLink()
    {
        var (svc, connection, adapter, db, p1, _) = await SeedUncategorizedAsync();
        new TestDataBuilder(db).AddProductLink(connection.Id, p1.Id, "oz-1");
        await db.SaveChangesAsync();
        adapter.Add(new ExternalProduct("oz-1", "ART-1", "One", CategoryId: "10:2", CategoryName: "T-shirts"));
        adapter.Fields["10:2"] =
        [
            new ExternalField("sku", "SKU", true, "string"),
            new ExternalField("name", "Name", true, "string"),
        ];

        var result = await svc.RepairMissingMappingsAsync(connection, userId: 1, CancellationToken.None);

        Assert.Equal(1, result.MappingsCreated);
        var product = await db.products.SingleAsync();
        Assert.NotNull(product.categoryId);
        Assert.Equal(1, await db.categoryMappings.CountAsync());
        Assert.NotNull((await db.productMarketplaceLinks.SingleAsync()).categoryMappingId);
    }

    [Fact]
    public async Task RepairMissingMappings_CopiesAttributesFromRemoteCard()
    {
        var (svc, connection, adapter, db, p1, _) = await SeedTwoAsync(secondProduct: false);
        new TestDataBuilder(db).AddProductLink(connection.Id, p1.Id, "ym-1");
        await db.SaveChangesAsync();
        adapter.Add(new ExternalProduct(
            "ym-1",
            "ART-1",
            "One",
            CategoryId: "79461128",
            CategoryName: "Hats",
            Attributes: new Dictionary<string, object> { ["brand"] = "Acme" },
            Brand: "Acme"));
        adapter.Fields["79461128"] =
        [
            new ExternalField("sku", "SKU", true, "string"),
            new ExternalField("name", "Name", true, "string"),
            new ExternalField("brand", "Brand", false, "string"),
        ];

        var result = await svc.RepairMissingMappingsAsync(connection, userId: 1, CancellationToken.None);

        Assert.Equal(1, result.MappingsCreated);
        Assert.True(result.AttributesImported > 0);
        Assert.Contains(
            await db.productAttributeValues.ToListAsync(),
            v => v.value == "Acme");
    }

    [Fact]
    public async Task QueueUnlinkedImport_CapsToRemainingSlots()
    {
        using var _ = SetEnv("MAX_PRODUCTS", "2");
        var (svc, connection, adapter, db, _, _) = await SeedTwoAsync(secondProduct: false);
        adapter.Capabilities |= MarketplaceCapabilities.ImportAllProducts;
        adapter.Catalog.AddRange([
            new ExternalProduct("oz-1", "A-1", "One"),
            new ExternalProduct("oz-2", "A-2", "Two"),
            new ExternalProduct("oz-3", "A-3", "Three"),
        ]);

        var result = await svc.QueueUnlinkedImportAsync(connection, userId: 1, CancellationToken.None);

        Assert.Equal(1, result.Queued);
        Assert.Equal(1, await db.pendingProductImports.CountAsync());
        Assert.Equal("oz-1", (await db.pendingProductImports.SingleAsync()).externalProductId);
    }

    [Fact]
    public async Task CatalogConnectJob_MatchPreview_PersistsRowsForLaterNavigation()
    {
        var (svc, connection, adapter, db, _, _) = await SeedTwoAsync(secondProduct: false);
        adapter.Add(new ExternalProduct("oz-1", "ART-1", "Remote card", CategoryId: "10:2"));
        var jobs = CreateJobService(db, svc);

        var enqueued = await jobs.EnqueueMatchAsync(connection, userId: 1, operationId: null, CancellationToken.None);
        var row = await db.pendingCatalogConnectJobs.SingleAsync();
        Assert.Equal(enqueued.OperationId, row.operationId);
        await jobs.ProcessJobAsync(row, CancellationToken.None);

        Assert.Equal(CatalogConnectJobStatus.Completed, row.status);
        var state = await jobs.GetStateAsync(connection.storeId, CancellationToken.None);
        Assert.Null(state.Active);
        var preview = Assert.Single(state.LatestPreview!.Preview!.Items);
        Assert.Equal("matched", preview.Status);
        Assert.Equal("oz-1", preview.ExternalProductId);
    }

    [Fact]
    public async Task CatalogConnectJob_Import_PagesRemoteCatalogAndRecordsTotal()
    {
        var (svc, connection, adapter, db, _, _) = await SeedTwoAsync(secondProduct: false);
        adapter.Capabilities |= MarketplaceCapabilities.ImportAllProducts;
        adapter.MaxPageSize = 1;
        adapter.Catalog.AddRange([
            new ExternalProduct("a", "A", "A"),
            new ExternalProduct("b", "B", "B"),
        ]);
        var jobs = CreateJobService(db, svc);

        await jobs.EnqueueImportAsync(connection, userId: 1, operationId: null, CancellationToken.None);
        var row = await db.pendingCatalogConnectJobs.SingleAsync();
        await jobs.ProcessJobAsync(row, CancellationToken.None);

        Assert.Equal(2, adapter.ImportAllCalls);
        Assert.Equal(2, await db.pendingProductImports.CountAsync());
        Assert.Equal(2, row.queuedCount);
        Assert.Equal(2, row.totalItems);
        Assert.Equal(CatalogConnectJobStatus.Completed, row.status);
    }

    [Fact]
    public async Task CatalogConnectJob_SecondEnqueueSameKind_ReturnsExisting()
    {
        var (svc, connection, _, db, _, _) = await SeedTwoAsync(secondProduct: false);
        var jobs = CreateJobService(db, svc);
        var first = await jobs.EnqueueMatchAsync(connection, userId: 1, operationId: null, CancellationToken.None);
        var second = await jobs.EnqueueMatchAsync(connection, userId: 1, operationId: null, CancellationToken.None);
        Assert.Equal(first.OperationId, second.OperationId);
        Assert.Equal(1, await db.pendingCatalogConnectJobs.CountAsync());
    }

    [Fact]
    public async Task CatalogConnectJob_OtherKind_QueuesBehindActive()
    {
        var (svc, connection, _, db, _, _) = await SeedTwoAsync(secondProduct: false);
        var jobs = CreateJobService(db, svc);
        var import = await jobs.EnqueueImportAsync(connection, userId: 1, operationId: null, CancellationToken.None);
        var match = await jobs.EnqueueMatchAsync(connection, userId: 1, operationId: null, CancellationToken.None);
        Assert.NotEqual(import.OperationId, match.OperationId);
        Assert.Equal(2, await db.pendingCatalogConnectJobs.CountAsync());
        Assert.All(await db.pendingCatalogConnectJobs.ToListAsync(), j =>
            Assert.Equal(CatalogConnectJobStatus.Pending, j.status));
    }

    [Fact]
    public async Task CatalogConnectJob_OtherConnectionApply_QueuesWhileBusy()
    {
        var (svc, ozon, _, db, p1, _) = await SeedTwoAsync(secondProduct: false);
        var yandex = new MarketplaceConnection
        {
            Id = ozon.Id + 10,
            storeId = ozon.storeId,
            adapterKey = "fake",
            name = "Yandex",
            credentialsJson = "{\"apiKey\":\"secret\"}",
            isActive = true,
            createdAt = DateTime.UtcNow,
        };
        db.marketplaceConnections.Add(yandex);
        await db.SaveChangesAsync();
        var jobs = CreateJobService(db, svc);
        var first = await jobs.EnqueueApplyAsync(
            ozon,
            userId: 1,
            [new CatalogMatchApplyItem { ProductId = p1.Id, ExternalProductId = "oz-1" }],
            importUnmatched: false,
            operationId: null,
            CancellationToken.None);
        var second = await jobs.EnqueueApplyAsync(
            yandex,
            userId: 1,
            [new CatalogMatchApplyItem { ProductId = p1.Id, ExternalProductId = "ym-1" }],
            importUnmatched: false,
            operationId: null,
            CancellationToken.None);
        Assert.NotEqual(first.OperationId, second.OperationId);
        Assert.Equal("pending", second.Status);
        Assert.Equal(2, await db.pendingCatalogConnectJobs.CountAsync());
    }

    [Fact]
    public void PickNext_SkipsPendingWhileStoreHasInProgress()
    {
        var running = new PendingCatalogConnectJob
        {
            Id = 1,
            storeId = 9,
            status = CatalogConnectJobStatus.InProgress,
            createdAt = DateTime.UtcNow.AddMinutes(-2),
        };
        var queued = new PendingCatalogConnectJob
        {
            Id = 2,
            storeId = 9,
            status = CatalogConnectJobStatus.Pending,
            createdAt = DateTime.UtcNow.AddMinutes(-1),
        };
        Assert.Null(CatalogConnectBackgroundService.PickNext([running, queued]));
    }

    [Fact]
    public void PickNext_RunsPendingOnIdleStore()
    {
        var running = new PendingCatalogConnectJob
        {
            Id = 1,
            storeId = 9,
            status = CatalogConnectJobStatus.InProgress,
            createdAt = DateTime.UtcNow.AddMinutes(-2),
        };
        var other = new PendingCatalogConnectJob
        {
            Id = 3,
            storeId = 10,
            status = CatalogConnectJobStatus.Pending,
            createdAt = DateTime.UtcNow,
        };
        Assert.Equal(3, CatalogConnectBackgroundService.PickNext([running, other])!.Id);
    }

    private static CatalogConnectJobService CreateJobService(DataContext db, CatalogMatchService match) =>
        new(db, match, new NoopOperationLogger(), NullLogger<CatalogConnectJobService>.Instance);

    private static IDisposable SetEnv(string name, string? value)
    {
        var previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
        return new EnvRestore(name, previous);
    }

    private sealed class EnvRestore(string name, string? previous) : IDisposable
    {
        public void Dispose() => Environment.SetEnvironmentVariable(name, previous);
    }

    private static async Task<(CatalogMatchService svc, MarketplaceConnection connection, LookupAdapter adapter)> SeedAsync()
    {
        var (svc, connection, adapter, _, _, _) = await SeedTwoAsync(secondProduct: false);
        return (svc, connection, adapter);
    }

    private static async Task<(
        CatalogMatchService svc,
        MarketplaceConnection connection,
        LookupAdapter adapter,
        DataContext db,
        Delegat.Models.Products.Product p1,
        Delegat.Models.Products.Product? p2)> SeedUncategorizedAsync(bool secondProduct = false)
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var p1 = data.AddProduct(store.Id, "Local one", "ART-1");
        Delegat.Models.Products.Product? p2 = null;
        if (secondProduct)
            p2 = data.AddProduct(store.Id, "Local two", "ART-2");
        var connection = data.AddConnection(store.Id, adapterKey: "fake");
        await db.SaveChangesAsync();

        var adapter = new LookupAdapter();
        var svc = CreateService(db, adapter);
        return (svc, connection, adapter, db, p1, p2);
    }

    private static async Task<(
        CatalogMatchService svc,
        MarketplaceConnection connection,
        LookupAdapter adapter,
        DataContext db,
        Delegat.Models.Products.Product p1,
        Delegat.Models.Products.Product? p2)> SeedTwoAsync(bool secondProduct = true)
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var category = data.AddCategory(store.Id, "Local");
        var p1 = data.AddProduct(store.Id, "Local one", "ART-1", category.Id);
        Delegat.Models.Products.Product? p2 = null;
        if (secondProduct)
            p2 = data.AddProduct(store.Id, "Local two", "ART-2", category.Id);
        var connection = data.AddConnection(store.Id, adapterKey: "fake");
        await db.SaveChangesAsync();

        var adapter = new LookupAdapter();
        var svc = CreateService(db, adapter);
        return (svc, connection, adapter, db, p1, p2);
    }

    private static CatalogMatchService CreateService(DataContext db, IMarketplaceAdapter adapter)
    {
        var registry = new SingleAdapterRegistry(adapter);
        var quota = new QuotaService(TestDbFactory.CreateUnitOfWork(db), db);
        var productSync = new ProductSyncService(
            db,
            registry,
            mappingService: null!,
            storageService: null!,
            NullLogger<ProductSyncService>.Instance,
            new NoopOperationLogger(),
            quota);
        return new CatalogMatchService(
            db,
            registry,
            new NoopOperationLogger(),
            NullLogger<CatalogMatchService>.Instance,
            quota,
            new CategoryImportService(db, registry),
            new CategoryImportQueueService(db),
            productSync);
    }

    private sealed class SingleAdapterRegistry(IMarketplaceAdapter adapter) : IMarketplaceAdapterRegistry
    {
        public IReadOnlyList<IMarketplaceAdapter> GetAll() => [adapter];
        public IMarketplaceAdapter? GetByKey(string key) =>
            key == adapter.Key ? adapter : null;
        public bool Exists(string key) => key == adapter.Key;
        public IReadOnlyList<MarketplaceInfo> GetAvailableMarketplaces() => [];
    }

    private sealed class NoopOperationLogger : IOperationLogger
    {
        public Task StartAsync(string operationId, int storeId, int authorUserId, string type, string title,
            int? connectionId = null, string? connectionName = null,
            string? batchId = null, string? batchType = null, int? totalItems = null, int? authorApiKeyId = null)
            => Task.CompletedTask;
        public Task LogAsync(string operationId, int storeId, string level, string message) => Task.CompletedTask;
        public Task ProgressAsync(string operationId, int storeId, int total, int completed, int failed) => Task.CompletedTask;
        public Task FinishAsync(string operationId, int storeId, string status, string? summary = null) => Task.CompletedTask;
        public Task UpdateStatusAsync(string operationId, int storeId, string status) => Task.CompletedTask;
    }

    private sealed class LookupAdapter : IMarketplaceAdapter
    {
        private readonly Dictionary<string, ExternalProduct> _byKey = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<ExternalField>> Fields { get; } = new(StringComparer.Ordinal);
        public ExternalProduct? OnMiss { get; set; }

        public void Add(ExternalProduct product)
        {
            _byKey[product.ExternalId] = product;
            if (!string.IsNullOrWhiteSpace(product.Sku))
                _byKey[product.Sku] = product;
            if (!string.IsNullOrWhiteSpace(product.Barcode))
                _byKey[product.Barcode] = product;
        }

        public string Key => "fake";
        public string DisplayName => "Fake";
        public string? Description => null;
        public string? LogoUrl => null;
        public List<ExternalProduct> Catalog { get; } = [];
        public int ImportProductCalls { get; private set; }
        public int ImportAllCalls { get; private set; }
        public int ImportProductsBatchCalls { get; private set; }
        public MarketplaceCapabilities Capabilities { get; set; } =
            MarketplaceCapabilities.ImportProduct
            | MarketplaceCapabilities.ExportProduct
            | MarketplaceCapabilities.GetCategories
            | MarketplaceCapabilities.GetCategoryFields;
        public string? ProductEditUrlTemplate => null;

        public CredentialsSchema GetCredentialsSchema() => new();
        public RateLimitConfig? GetRateLimitConfig() => null;
        public Task<List<QuotaInfo>> GetQuotasAsync(MarketplaceConnection connection, CancellationToken ct = default)
            => Task.FromResult(new List<QuotaInfo>());
        public MarketplaceRateLimiter? GetRateLimiterForConnection(MarketplaceConnection connection) => null;
        public int ClearMetadataCaches(MarketplaceConnection connection) => 0;
        public Task<ConnectionTestResult> TestConnectionAsync(MarketplaceConnection connection, CancellationToken ct = default)
            => Task.FromResult(new ConnectionTestResult(true));
        public Task<List<ExternalCategory>> GetCategoriesAsync(MarketplaceConnection connection, string? parentId = null, string? searchQuery = null, CancellationToken ct = default)
            => Task.FromResult(new List<ExternalCategory>());
        public Task<List<ExternalField>> GetCategoryFieldsAsync(MarketplaceConnection connection, string externalCategoryId, CancellationToken ct = default)
            => Task.FromResult(Fields.GetValueOrDefault(externalCategoryId) ?? []);
        public Task<List<ExternalField>> GetPriceFieldsAsync(MarketplaceConnection connection, string externalCategoryId, CancellationToken ct = default)
            => Task.FromResult(new List<ExternalField>());
        public Task<CreateCategoryResult> CreateCategoryAsync(MarketplaceConnection connection, CreateCategoryRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ExternalProduct?> ImportProductAsync(MarketplaceConnection connection, string externalProductId, CancellationToken ct = default)
        {
            ImportProductCalls++;
            if (_byKey.TryGetValue(externalProductId.Trim(), out var product))
                return Task.FromResult<ExternalProduct?>(product);
            return Task.FromResult(OnMiss);
        }
        public Task<List<ExternalProduct>> ImportProductsBatchAsync(MarketplaceConnection connection, List<string> externalProductIds, CancellationToken ct = default)
        {
            ImportProductsBatchCalls++;
            var results = new List<ExternalProduct>();
            foreach (var id in externalProductIds)
            {
                if (_byKey.TryGetValue(id.Trim(), out var product))
                    results.Add(product);
            }

            return Task.FromResult(results);
        }
        public int MaxPageSize { get; set; } = 10_000;
        public Task<PagedResult<ExternalProduct>> ImportAllProductsAsync(MarketplaceConnection connection, int pageSize = 100, string? cursor = null, string? offerIdFilter = null, CancellationToken ct = default)
        {
            ImportAllCalls++;
            var limit = Math.Max(1, Math.Min(pageSize, MaxPageSize));
            var offset = 0;
            if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var parsed))
                offset = Math.Max(0, parsed);
            var slice = Catalog.Skip(offset).Take(limit).ToList();
            var next = offset + slice.Count;
            return Task.FromResult(new PagedResult<ExternalProduct>(
                slice,
                next < Catalog.Count,
                next < Catalog.Count ? next.ToString() : null,
                Catalog.Count));
        }
        public Task<ExportResult> ExportProductAsync(MarketplaceConnection connection, Dictionary<string, object> productData, string? existingExternalId = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<BulkExportResult> ExportProductsBulkAsync(MarketplaceConnection connection, List<BulkExportItem> items, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ExportResult> UpdateSkuAsync(MarketplaceConnection connection, string externalProductId, string newSku, string? currentSku = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ExportResult> ArchiveProductAsync(MarketplaceConnection connection, string externalProductId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<List<PriceInfo>> ImportPricesAsync(MarketplaceConnection connection, List<string>? externalProductIds = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ImportResult> ExportPricesAsync(MarketplaceConnection connection, List<PriceInfo> prices, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<List<StockInfo>> ImportStocksAsync(MarketplaceConnection connection, List<string>? externalProductIds = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ImportResult> ExportStocksAsync(MarketplaceConnection connection, List<StockInfo> stocks, CancellationToken ct = default)
            => throw new NotImplementedException();
        public FieldBasedGroupingConfig? GetFieldBasedGroupingConfig() => null;
        public Task<CreateGroupResult> CreateGroupAsync(MarketplaceConnection connection, List<string> productExternalIds, string? groupName = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<GroupMembershipResult> AddProductsToGroupAsync(MarketplaceConnection connection, string externalGroupId, List<string> productExternalIds, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<GroupMembershipResult> RemoveProductsFromGroupAsync(MarketplaceConnection connection, string externalGroupId, List<string> productExternalIds, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ExportResult> DeleteGroupAsync(MarketplaceConnection connection, string externalGroupId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<PagedResult<ExternalGroup>> ImportGroupsAsync(MarketplaceConnection connection, int pageSize = 100, string? cursor = null, CancellationToken ct = default)
            => throw new NotImplementedException();
    }
}
