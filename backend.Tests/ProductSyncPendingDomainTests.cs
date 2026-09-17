using Delegat.Enums;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Products;
using Delegat.Services.Marketplace;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

public class ProductSyncPendingDomainTests
{
    private static (DataContext db, TestDataBuilder data, FakeCurrentUserService currentUser) Create()
    {
        var db = TestDbFactory.CreateDb();
        return (db, new TestDataBuilder(db), new FakeCurrentUserService());
    }

    [Fact]
    public async Task PendingExport_PersistsQueueFields()
    {
        var (db, data, _) = Create();
        var store = data.AddStore();
        var product = data.AddProduct(store.Id, "Desk");
        var connection = data.AddConnection(store.Id);
        db.pendingProductExports.Add(new PendingProductExport
        {
            connectionId = connection.Id, productId = product.Id, productName = product.name,
            authorUserId = 7, storeId = store.Id, batchId = "export-1"
        });
        await db.SaveChangesAsync();

        var pending = await db.pendingProductExports.SingleAsync();

        Assert.Equal(ProductExportStatus.Pending, pending.status);
        Assert.Equal("Desk", pending.productName);
        Assert.Equal("export-1", pending.batchId);
        Assert.Equal(store.Id, pending.storeId);
    }

    [Fact]
    public async Task PendingImport_PersistsQueueFields()
    {
        var (db, data, _) = Create();
        var store = data.AddStore();
        var category = data.AddCategory(store.Id);
        var connection = data.AddConnection(store.Id);
        db.pendingProductImports.Add(new PendingProductImport
        {
            connectionId = connection.Id, externalProductId = "external-1", targetCategoryId = category.Id,
            authorUserId = 8, storeId = store.Id, productName = "External desk", batchId = "import-1"
        });
        await db.SaveChangesAsync();

        var pending = await db.pendingProductImports.SingleAsync();

        Assert.Equal(ProductImportStatus.Pending, pending.status);
        Assert.Equal("external-1", pending.externalProductId);
        Assert.Equal(category.Id, pending.targetCategoryId);
        Assert.Equal("External desk", pending.productName);
    }

    [Fact]
    public async Task PendingExport_CanTransitionToCompleted()
    {
        var (db, data, _) = Create();
        var store = data.AddStore();
        var export = new PendingProductExport { connectionId = 1, productId = 2, authorUserId = 3, storeId = store.Id };
        db.pendingProductExports.Add(export);
        await db.SaveChangesAsync();

        export.status = ProductExportStatus.Completed;
        export.externalProductId = "ext-2";
        export.linkId = 9;
        export.completedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var saved = await db.pendingProductExports.SingleAsync();
        Assert.Equal(ProductExportStatus.Completed, saved.status);
        Assert.Equal("ext-2", saved.externalProductId);
        Assert.Equal(9, saved.linkId);
        Assert.NotNull(saved.completedAt);
    }

    [Fact]
    public async Task PendingExport_CanTransitionToFailed()
    {
        var (db, data, _) = Create();
        var export = new PendingProductExport { connectionId = 1, productId = 2, authorUserId = 3, storeId = 4 };
        db.pendingProductExports.Add(export);
        await db.SaveChangesAsync();

        export.status = ProductExportStatus.Failed;
        export.lastError = "Marketplace rejected SKU";
        export.retryCount = 1;
        await db.SaveChangesAsync();

        var saved = await db.pendingProductExports.SingleAsync();
        Assert.Equal(ProductExportStatus.Failed, saved.status);
        Assert.Equal("Marketplace rejected SKU", saved.lastError);
        Assert.Equal(1, saved.retryCount);
    }

    [Fact]
    public async Task PendingImport_CanTransitionToCompleted()
    {
        var (db, data, _) = Create();
        var import = new PendingProductImport { connectionId = 1, externalProductId = "ext", authorUserId = 2, storeId = 3 };
        db.pendingProductImports.Add(import);
        await db.SaveChangesAsync();

        import.status = ProductImportStatus.Completed;
        import.importedProductId = 77;
        import.completedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var saved = await db.pendingProductImports.SingleAsync();
        Assert.Equal(ProductImportStatus.Completed, saved.status);
        Assert.Equal(77, saved.importedProductId);
        Assert.NotNull(saved.completedAt);
    }

    [Fact]
    public async Task PendingImport_CanTransitionToFailed()
    {
        var (db, data, _) = Create();
        var import = new PendingProductImport { connectionId = 1, externalProductId = "ext", authorUserId = 2, storeId = 3 };
        db.pendingProductImports.Add(import);
        await db.SaveChangesAsync();

        import.status = ProductImportStatus.Failed;
        import.lastError = "Category is missing";
        import.retryCount = 2;
        await db.SaveChangesAsync();

        var saved = await db.pendingProductImports.SingleAsync();
        Assert.Equal(ProductImportStatus.Failed, saved.status);
        Assert.Equal("Category is missing", saved.lastError);
        Assert.Equal(2, saved.retryCount);
    }

    [Fact]
    public async Task ExportBatchGrouping_ReturnsOnlyMatchingBatch()
    {
        var (db, _, _) = Create();
        db.pendingProductExports.AddRange(
            new PendingProductExport { connectionId = 1, productId = 1, authorUserId = 1, storeId = 1, batchId = "a" },
            new PendingProductExport { connectionId = 1, productId = 2, authorUserId = 1, storeId = 1, batchId = "a" },
            new PendingProductExport { connectionId = 1, productId = 3, authorUserId = 1, storeId = 1, batchId = "b" });
        await db.SaveChangesAsync();

        var batch = await db.pendingProductExports.Where(x => x.batchId == "a").ToListAsync();

        Assert.Equal(2, batch.Count);
        Assert.All(batch, item => Assert.Equal("a", item.batchId));
    }

    [Fact]
    public async Task ImportBatchGrouping_ReturnsOnlyMatchingBatch()
    {
        var (db, _, _) = Create();
        db.pendingProductImports.AddRange(
            new PendingProductImport { connectionId = 1, externalProductId = "1", authorUserId = 1, storeId = 1, batchId = "a" },
            new PendingProductImport { connectionId = 1, externalProductId = "2", authorUserId = 1, storeId = 1, batchId = "b" });
        await db.SaveChangesAsync();

        var batch = await db.pendingProductImports.Where(x => x.batchId == "b").ToListAsync();

        Assert.Single(batch);
        Assert.Equal("2", batch[0].externalProductId);
    }

    [Fact]
    public async Task RetryFailedExports_ResetsOnlyFailedItemsInBatch()
    {
        var (db, _, _) = Create();
        db.pendingProductExports.AddRange(
            new PendingProductExport { connectionId = 1, productId = 1, authorUserId = 1, storeId = 1, batchId = "a", status = ProductExportStatus.Failed, retryCount = 2, lastError = "bad" },
            new PendingProductExport { connectionId = 1, productId = 2, authorUserId = 1, storeId = 1, batchId = "a", status = ProductExportStatus.Completed },
            new PendingProductExport { connectionId = 1, productId = 3, authorUserId = 1, storeId = 1, batchId = "b", status = ProductExportStatus.Failed });
        await db.SaveChangesAsync();

        foreach (var item in await db.pendingProductExports.Where(x => x.batchId == "a" && x.status == ProductExportStatus.Failed).ToListAsync())
        {
            item.status = ProductExportStatus.Pending;
            item.retryCount = 0;
            item.lastError = null;
        }
        await db.SaveChangesAsync();

        var retried = await db.pendingProductExports.SingleAsync(x => x.productId == 1);
        Assert.Equal(ProductExportStatus.Pending, retried.status);
        Assert.Equal(0, retried.retryCount);
        Assert.Null(retried.lastError);
        Assert.Equal(ProductExportStatus.Failed, (await db.pendingProductExports.SingleAsync(x => x.productId == 3)).status);
    }

    [Fact]
    public async Task RetryFailedImports_ResetsOnlyFailedItemsInBatch()
    {
        var (db, _, _) = Create();
        db.pendingProductImports.AddRange(
            new PendingProductImport { connectionId = 1, externalProductId = "1", authorUserId = 1, storeId = 1, batchId = "a", status = ProductImportStatus.Failed, retryCount = 3, lastError = "bad" },
            new PendingProductImport { connectionId = 1, externalProductId = "2", authorUserId = 1, storeId = 1, batchId = "b", status = ProductImportStatus.Failed });
        await db.SaveChangesAsync();

        foreach (var item in await db.pendingProductImports.Where(x => x.batchId == "a" && x.status == ProductImportStatus.Failed).ToListAsync())
        {
            item.status = ProductImportStatus.Pending;
            item.retryCount = 0;
            item.lastError = null;
        }
        await db.SaveChangesAsync();

        var retried = await db.pendingProductImports.SingleAsync(x => x.externalProductId == "1");
        Assert.Equal(ProductImportStatus.Pending, retried.status);
        Assert.Equal(0, retried.retryCount);
        Assert.Null(retried.lastError);
    }

    [Fact]
    public async Task PendingQueues_AreIsolatedByStore()
    {
        var (db, data, currentUser) = Create();
        var first = data.AddStore();
        var second = data.AddStore();
        currentUser.SelectedStoreId = first.Id;
        db.pendingProductExports.AddRange(
            new PendingProductExport { connectionId = 1, productId = 1, authorUserId = 1, storeId = first.Id },
            new PendingProductExport { connectionId = 2, productId = 2, authorUserId = 1, storeId = second.Id });
        await db.SaveChangesAsync();

        var visible = await db.pendingProductExports.Where(x => x.storeId == currentUser.GetSelectedStoreId()).ToListAsync();

        Assert.Single(visible);
        Assert.Equal(first.Id, visible[0].storeId);
    }

    [Fact]
    public async Task PendingImageDownload_PersistsQueueFields()
    {
        var (db, data, _) = Create();
        var store = data.AddStore();
        var product = data.AddProduct(store.Id);
        db.pendingImageDownloads.Add(new PendingImageDownload
        {
            productId = product.Id, storeId = store.Id, authorUserId = 1,
            imageUrl = "https://example.test/a.jpg", imageIndex = 2, batchId = "img-1"
        });
        await db.SaveChangesAsync();

        var download = await db.pendingImageDownloads.SingleAsync();
        Assert.Equal(ImageDownloadStatus.Pending, download.status);
        Assert.Equal(2, download.imageIndex);
        Assert.Equal("img-1", download.batchId);
    }

    [Fact]
    public async Task PendingImageDownload_CanComplete()
    {
        var (db, _, _) = Create();
        var download = new PendingImageDownload { productId = 1, storeId = 1, authorUserId = 1, imageUrl = "https://example.test/a.jpg" };
        db.pendingImageDownloads.Add(download);
        await db.SaveChangesAsync();

        download.status = ImageDownloadStatus.Completed;
        download.completedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        Assert.Equal(ImageDownloadStatus.Completed, (await db.pendingImageDownloads.SingleAsync()).status);
        Assert.NotNull((await db.pendingImageDownloads.SingleAsync()).completedAt);
    }

    [Fact]
    public async Task RetryFailedImageDownload_ResetsFailureMetadata()
    {
        var (db, _, _) = Create();
        var download = new PendingImageDownload
        {
            productId = 1, storeId = 1, authorUserId = 1, imageUrl = "https://example.test/a.jpg",
            status = ImageDownloadStatus.Failed, retryCount = 4, lastError = "timeout"
        };
        db.pendingImageDownloads.Add(download);
        await db.SaveChangesAsync();

        download.status = ImageDownloadStatus.Pending;
        download.retryCount = 0;
        download.lastError = null;
        await db.SaveChangesAsync();

        var retried = await db.pendingImageDownloads.SingleAsync();
        Assert.Equal(ImageDownloadStatus.Pending, retried.status);
        Assert.Equal(0, retried.retryCount);
        Assert.Null(retried.lastError);
    }

    [Fact]
    public async Task CreateProductMarketplaceLink_PersistsExternalIdAndStatus()
    {
        var (db, data, _) = Create();
        var store = data.AddStore();
        var product = data.AddProduct(store.Id);
        var connection = data.AddConnection(store.Id);
        data.AddProductLink(connection.Id, product.Id, "external-42", ProductMarketplaceStatus.Synced);
        await data.SaveAsync();

        var link = await db.productMarketplaceLinks.SingleAsync();
        Assert.Equal("external-42", link.externalProductId);
        Assert.Equal(ProductMarketplaceStatus.Synced, link.status);
    }

    [Fact]
    public async Task UnlinkProductMarketplaceLink_DeletesOnlyRequestedLink()
    {
        var (db, data, _) = Create();
        var store = data.AddStore();
        var product = data.AddProduct(store.Id);
        var connection = data.AddConnection(store.Id);
        var remove = data.AddProductLink(connection.Id, product.Id, "remove");
        data.AddProductLink(connection.Id, product.Id + 1, "keep");
        await data.SaveAsync();

        db.productMarketplaceLinks.Remove(remove);
        await db.SaveChangesAsync();

        Assert.Equal(new[] { "keep" }, await db.productMarketplaceLinks.Select(x => x.externalProductId).ToListAsync());
    }

    [Fact]
    public async Task ProductMarketplaceLink_CanUpdateSynchronizationFields()
    {
        var (db, data, _) = Create();
        var store = data.AddStore();
        var product = data.AddProduct(store.Id);
        var connection = data.AddConnection(store.Id);
        var link = data.AddProductLink(connection.Id, product.Id, status: ProductMarketplaceStatus.NotSynced);
        await data.SaveAsync();

        link.UpdateFill(new ProductMarketplaceLink.UpdateForm
        {
            status = ProductMarketplaceStatus.SyncError, lastErrorMessage = "invalid price",
            externalPrice = 199.99m, externalStock = 5
        });
        await db.SaveChangesAsync();

        var saved = await db.productMarketplaceLinks.SingleAsync();
        Assert.Equal(ProductMarketplaceStatus.SyncError, saved.status);
        Assert.Equal("invalid price", saved.lastErrorMessage);
        Assert.Equal(199.99m, saved.externalPrice);
        Assert.Equal(5, saved.externalStock);
    }

    [Fact]
    public async Task ProductVariant_CreatePersistsAllStraightforwardFields()
    {
        var (db, data, _) = Create();
        var product = data.AddProduct(data.AddStore().Id);
        var variant = new ProductVariant();
        variant.AddFill(new ProductVariant.AddForm
        {
            productId = product.Id, sku = "SKU-M", barcode = "123", size = "M",
            price = 150m, quantity = 4, sortOrder = 2
        });
        db.productVariants.Add(variant);
        await db.SaveChangesAsync();

        var saved = await db.productVariants.SingleAsync();
        Assert.Equal("SKU-M", saved.sku);
        Assert.Equal("123", saved.barcode);
        Assert.Equal("M", saved.size);
        Assert.Equal(150m, saved.price);
        Assert.Equal(4, saved.quantity);
        Assert.Equal(2, saved.sortOrder);
    }

    [Fact]
    public async Task ProductVariant_UpdateChangesProvidedFields()
    {
        var (db, data, _) = Create();
        var product = data.AddProduct(data.AddStore().Id);
        var variant = new ProductVariant { productId = product.Id, sku = "SKU-S", size = "S", quantity = 1 };
        db.productVariants.Add(variant);
        await db.SaveChangesAsync();

        variant.UpdateFill(new ProductVariant.UpdateForm { size = "M", quantity = 8, price = 180m, sortOrder = 3 });
        await db.SaveChangesAsync();

        var saved = await db.productVariants.SingleAsync();
        Assert.Equal("M", saved.size);
        Assert.Equal(8, saved.quantity);
        Assert.Equal(180m, saved.price);
        Assert.Equal(3, saved.sortOrder);
    }

    [Fact]
    public async Task ProductVariant_DeleteRemovesVariant()
    {
        var (db, data, _) = Create();
        var product = data.AddProduct(data.AddStore().Id);
        db.productVariants.Add(new ProductVariant { productId = product.Id, sku = "SKU-S" });
        await db.SaveChangesAsync();

        db.productVariants.Remove(await db.productVariants.SingleAsync());
        await db.SaveChangesAsync();

        Assert.Empty(await db.productVariants.ToListAsync());
    }

    [Fact]
    public async Task ExportBatchSelection_AllMatchingSkipsCardsWithoutGallery()
    {
        var (db, data, _) = Create();
        var store = data.AddStore();
        var user = data.AddUser();
        var withPhoto = data.AddProduct(store.Id, "Has photo", sku: "P1");
        data.AddProduct(store.Id, "No photo", sku: "P2");
        db.files.Add(new Delegat.Models.File
        {
            productId = withPhoto.Id,
            authorUserId = user.Id,
            filePath = "a.jpg",
            contentType = "image/jpeg",
            length = 10,
        });
        db.images.Add(new ImageReference { fileId = 1, productId = withPhoto.Id, index = 0 });
        await db.SaveChangesAsync();
        var fileId = await db.files.Select(f => f.Id).SingleAsync();
        var image = await db.images.SingleAsync();
        image.fileId = fileId;
        await db.SaveChangesAsync();

        var ids = await ExportBatchSelection.ResolveProductIdsAsync(
            db, store.Id, productIds: null, allMatching: true, skipWithoutImages: true, default);

        Assert.Equal(new[] { withPhoto.Id }, ids);
    }
}
