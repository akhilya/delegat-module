using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using Delegat.Controllers;
using Delegat.Enums;
using Delegat.Infrastructure;
using Delegat.Infrastructure.Interfaces;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Models.Products;
using Delegat.Services;
using Delegat.Services.Marketplace;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Delegat.Tests;

[Collection("EnvVars")]
public class LocalMySqlCatalogTests(ITestOutputHelper output)
{
    [LocalMySqlFact]
    public async Task FullMigrationChain_InstallsCatalogIndexesAndLegacyCheckpointDefault()
    {
        await using var database = await LocalMySqlDatabase.CreateAsync(migrate: true);
        await using var db = database.CreateContext();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var indexes = await db.Database.SqlQueryRaw<string>(
            "SELECT INDEX_NAME AS Value FROM information_schema.statistics WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'products'")
            .ToListAsync();
        Assert.Contains("IX_products_storeId_barcode", indexes);
        Assert.Contains("IX_products_storeId_deletedAt_categoryId_Id", indexes);
        var logIndexes = await db.Database.SqlQueryRaw<string>(
            "SELECT INDEX_NAME AS Value FROM information_schema.statistics WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'operationLogEntries'")
            .ToListAsync();
        Assert.Contains("IX_operationLogEntries_timestamp", logIndexes);
        var defaults = await db.Database.SqlQueryRaw<string>(
            "SELECT COLUMN_DEFAULT AS Value FROM information_schema.columns WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'pendingProductBulkUpdates' AND COLUMN_NAME = 'processedCount'")
            .SingleAsync();
        Assert.Equal("-1", defaults);
        output.WriteLine("Fresh MySQL schema migrated through every EF migration; catalog indexes and legacy checkpoint default verified.");
    }

    [LocalMySqlFact]
    public async Task ImportRollbackBarcodeLocksAndRetry_UseRealMySqlTransactions()
    {
        await using var database = await LocalMySqlDatabase.CreateAsync();
        var fault = new MySqlWriteFault { Table = "pendingImageDownloads" };
        await using var db = database.CreateContext(fault);
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id, adapterKey: "fake");
        await db.SaveChangesAsync();
        var adapter = DispatchProxy.Create<IMarketplaceAdapter, TransactionImportAdapterProxy>();
        var proxy = (TransactionImportAdapterProxy)(object)adapter;
        proxy.Db = db;
        proxy.Source = new ExternalProduct("external", "SKU", "Imported", Barcode: "2000000000015",
            ImageUrls: ["https://8.8.8.8/fake-image.jpg"]);
        var service = CatalogTransactionTestSupport.ImportService(db, adapter);
        fault.Enabled = true;
        var failed = await service.ImportProductAsync(connection.Id, "external", importByUserId: user.Id);
        Assert.False(failed.IsSuccess);
        Assert.True(fault.Failures > 0);
        Assert.Equal(0, await db.products.CountAsync());
        Assert.Equal(0, await db.productMarketplaceLinks.CountAsync());
        Assert.Equal(0, await db.pendingImageDownloads.CountAsync());
        fault.Enabled = false;
        var success = await service.ImportProductAsync(connection.Id, "external", importByUserId: user.Id);
        Assert.True(success.IsSuccess, success.ErrorMessage);
        Assert.True((await service.ImportProductAsync(connection.Id, "external")).AlreadyLinked);
        Assert.Equal(1, await db.products.CountAsync());
        Assert.Equal(1, await db.pendingImageDownloads.CountAsync());
    }

    [LocalMySqlFact]
    public async Task UpgradeFromPreFixSchema_PreservesLegacyCountersButDisablesUnsafeReplay()
    {
        await using var database = await LocalMySqlDatabase.CreateAsync(migrationTarget: "20261110202346_AddProductMediaExportGrants");
        await using var db = database.CreateContext();
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO pendingProductBulkUpdates
                (storeId, authorUserId, batchId, productIdsJson, updatesJson, status, completedCount, failedCount, createdAt)
            VALUES (1, 1, 'legacy-upgrade', '[1]', {0}, 1, 50, 0, UTC_TIMESTAMP())
            """, new object[] { "{\"priceDelta\":10}" });
        await db.Database.MigrateAsync();
        var legacy = await db.pendingProductBulkUpdates.SingleAsync();
        Assert.Equal(-1, legacy.processedCount);
        Assert.Equal(50, legacy.completedCount);
        var service = new ProductBulkUpdateService(db, TestDbFactory.CreateUnitOfWork(db), new AttributeUniquenessService(db));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(1, [1], new ProductBulkUpdateFields { priceDelta = 10m }, job: legacy));
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(0, await db.Set<ProductImportReceipt>().CountAsync());
    }

    [LocalMySqlFact]
    public async Task BulkEdit_CancellationAndRestartDoesNotRepeatPriceDeltaOnMySql()
    {
        await using var database = await LocalMySqlDatabase.CreateAsync();
        var fault = new MySqlWriteFault { Table = "products", UpdateOnly = true };
        await using var db = database.CreateContext(fault);
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var ids = Enumerable.Range(0, 105).Select(_ => data.AddProduct(store.Id, price: 100m).Id).ToList();
        var job = new PendingProductBulkUpdate { storeId = store.Id, batchId = "mysql-resume" };
        db.pendingProductBulkUpdates.Add(job);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        job = await db.pendingProductBulkUpdates.SingleAsync();
        using var cancellation = new CancellationTokenSource();
        var logger = new RecordingCatalogLogger(db)
        {
            OnProgress = (completed, _) =>
            {
                if (completed == 50) { fault.Enabled = true; fault.Cancel = cancellation; }
                return Task.CompletedTask;
            }
        };
        var service = new ProductBulkUpdateService(db, TestDbFactory.CreateUnitOfWork(db), new AttributeUniquenessService(db));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExecuteAsync(store.Id, ids,
            new ProductBulkUpdateFields { priceDelta = 10m }, cancellation.Token, logger, job.batchId, job));
        Assert.Equal(50, job.processedCount);
        fault.Enabled = false;
        db.ChangeTracker.Clear();
        job = await db.pendingProductBulkUpdates.SingleAsync();
        var result = await service.ExecuteAsync(store.Id, ids, new ProductBulkUpdateFields { priceDelta = 10m }, job: job);
        Assert.Equal(105, result.updatedCount);
        Assert.Equal(0, result.failedCount);
        Assert.All(await db.products.AsNoTracking().ToListAsync(), p => Assert.Equal(110m, p.price));
    }

    [LocalMySqlFact]
    public async Task PermanentDelete_WithNestedFoldersAndFileAttributesIsAtomicOnMySql()
    {
        await using var database = await LocalMySqlDatabase.CreateAsync();
        var fault = new MySqlWriteFault { Table = "products", DeleteOnly = true };
        await using var db = database.CreateContext(fault);
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore();
        var category = data.AddCategory(store.Id);
        var attribute = data.AddCategoryAttribute(category.Id, type: AttributeType.File);
        var ids = Enumerable.Range(0, 105).Select(_ => data.AddProduct(store.Id, categoryId: category.Id, deletedAt: DateTime.UtcNow).Id).ToList();
        await db.SaveChangesAsync();
        var root = new ProductFolder { productId = ids[0], name = "root" };
        var child = new ProductFolder { productId = ids[0], name = "child", parentFolder = root };
        db.productFolders.Add(new ProductFolder { productId = ids[0], name = "leaf", parentFolder = child });
        var file = new Models.File { productId = ids[0], authorUserId = user.Id, filePath = "test/manual.pdf", contentType = "application/pdf" };
        var value = new ProductAttributeValue { productId = ids[0], attributeId = attribute.Id, value = "" };
        db.files.Add(file);
        db.productAttributeValues.Add(value);
        await db.SaveChangesAsync();
        db.productAttributeFiles.Add(new ProductAttributeFile { attributeValueId = value.Id, fileId = file.Id });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        fault.Enabled = true;
        await Assert.ThrowsAnyAsync<Exception>(() => ProductCatalogPerformanceTests.Controller(db, store.Id)
            .BulkPermanentDelete(new ProductController.BulkDeleteRequest { productIds = ids }));
        Assert.Equal(105, await db.products.IgnoreQueryFilters().CountAsync());
        Assert.Equal(3, await db.productFolders.CountAsync());
        Assert.Equal(1, await db.productAttributeFiles.CountAsync());
        fault.Enabled = false;
        db.ChangeTracker.Clear();
        Assert.IsType<OkObjectResult>(await ProductCatalogPerformanceTests.Controller(db, store.Id)
            .BulkPermanentDelete(new ProductController.BulkDeleteRequest { productIds = ids }));
        Assert.Equal(0, await db.products.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await db.productFolders.CountAsync());
    }

    [LocalMySqlFact]
    public async Task SingleCategoryDelete_FailureDoesNotDetachTrashedProducts()
    {
        await using var database = await LocalMySqlDatabase.CreateAsync();
        var fault = new MySqlWriteFault { Table = "categories", DeleteOnly = true };
        await using var db = database.CreateContext(fault);
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var category = data.AddCategory(store.Id);
        var product = data.AddProduct(store.Id, categoryId: category.Id, deletedAt: DateTime.UtcNow);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var controller = new CategoryController(new FakeCurrentUserService { SelectedStoreId = store.Id },
            TestDbFactory.CreateUnitOfWork(db), new ServiceCollection().BuildServiceProvider(),
            NullLogger<CategoryController>.Instance, db);
        fault.Enabled = true;
        await Assert.ThrowsAnyAsync<Exception>(() => controller.DeleteCategory(category.Id));
        db.ChangeTracker.Clear();
        Assert.Equal(category.Id, (await db.products.IgnoreQueryFilters().SingleAsync()).categoryId);
        fault.Enabled = false;
        await controller.DeleteCategory(category.Id);
        Assert.Null((await db.products.IgnoreQueryFilters().AsNoTracking().SingleAsync()).categoryId);
    }

    [LocalMySqlFact]
    public async Task StoreDelete_HandlesRetryStrategyTrashedFolderTreesAndModernStoragePrefixes()
    {
        await using var database = await LocalMySqlDatabase.CreateAsync();
        var fault = new MySqlWriteFault { Table = "stores", DeleteOnly = true };
        await using var db = database.CreateContext(fault);
        var data = new TestDataBuilder(db);
        var keep = data.AddStore("Keep");
        var victim = data.AddStore("Victim");
        var connection = data.AddConnection(victim.Id, adapterKey: "fake");
        var user = data.AddUser();
        user.selectedStoreId = victim.Id;
        var category = data.AddCategory(victim.Id);
        var attribute = data.AddCategoryAttribute(category.Id, type: AttributeType.File);
        var product = data.AddProduct(victim.Id, categoryId: category.Id, deletedAt: DateTime.UtcNow);
        await db.SaveChangesAsync();
        var root = new ProductFolder { productId = product.Id, name = "root" };
        db.productFolders.Add(new ProductFolder { productId = product.Id, name = "child", parentFolder = root });
        var file = new Models.File
        {
            productId = product.Id,
            authorUserId = user.Id,
            filePath = $"stores/{victim.Id}/products/{product.Id}/manual.pdf",
            contentType = "application/pdf"
        };
        var value = new ProductAttributeValue { productId = product.Id, attributeId = attribute.Id, value = "" };
        db.files.Add(file);
        db.productAttributeValues.Add(value);
        await db.SaveChangesAsync();
        db.productAttributeFiles.Add(new ProductAttributeFile { attributeValueId = value.Id, fileId = file.Id });
        db.operations.Add(new Operation
        {
            operationId = "store-delete",
            storeId = victim.Id,
            authorUserId = user.Id,
            type = "test",
            title = "Fixture",
            status = "completed"
        });
        db.operationLogEntries.Add(new OperationLogEntry { operationId = "store-delete", level = "info", message = "Keep until commit" });
        db.pendingCatalogConnectJobs.Add(new PendingCatalogConnectJob
        {
            storeId = victim.Id,
            connectionId = connection.Id,
            authorUserId = user.Id,
            operationId = "store-catalog-job"
        });
        db.aiChatAttachments.Add(new Delegat.Models.Ai.AiChatAttachment
        {
            storeId = victim.Id,
            userId = user.Id,
            sessionId = "store-chat",
            storageKey = "__ai-chat-attach/test-file"
        });
        db.pendingFolderJobs.Add(new PendingFolderJob
        {
            storeId = victim.Id,
            authorUserId = user.Id,
            operationId = "store-archive",
            resultObjectKey = "test-artifacts/store-archive.zip"
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var storage = new RecordingCatalogStorage();
        storage.Objects.Add(file.filePath);
        storage.Objects.Add($"{victim.Id}/legacy.bin");
        storage.Objects.Add($"stores/{keep.Id}/keep.bin");
        storage.Objects.Add("__ai-chat-attach/test-file");
        storage.Objects.Add("test-artifacts/store-archive.zip");
        var branding = DispatchProxy.Create<IBrandingSettingsService, CatalogBrandingProxy>();
        var service = new StoreDeletionService(db, storage, branding, NullLogger<StoreDeletionService>.Instance);
        fault.Enabled = true;
        await Assert.ThrowsAnyAsync<Exception>(() => service.DeleteStoreAsync(victim.Id, "Victim"));
        Assert.True(fault.Failures > 0);
        Assert.Equal(2, await db.stores.CountAsync());
        Assert.Equal(victim.Id, (await db.Users.AsNoTracking().SingleAsync()).selectedStoreId);
        Assert.Equal(1, await db.operationLogEntries.CountAsync());
        Assert.Equal(1, await db.productAttributeFiles.CountAsync());
        Assert.Equal(1, await db.aiChatAttachments.CountAsync());
        Assert.Empty(storage.Deleted);
        fault.Enabled = false;
        await service.DeleteStoreAsync(victim.Id, "Victim");
        Assert.Equal(1, await db.stores.CountAsync());
        Assert.Null((await db.Users.AsNoTracking().SingleAsync()).selectedStoreId);
        Assert.Equal(0, await db.products.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await db.pendingCatalogConnectJobs.CountAsync());
        Assert.Equal(0, await db.operationLogEntries.CountAsync());
        Assert.Contains(file.filePath, storage.Deleted);
        Assert.Contains($"{victim.Id}/legacy.bin", storage.Deleted);
        Assert.Contains("__ai-chat-attach/test-file", storage.Deleted);
        Assert.Contains("test-artifacts/store-archive.zip", storage.Deleted);
        Assert.DoesNotContain($"stores/{keep.Id}/keep.bin", storage.Deleted);
    }

    [LocalMySqlFact]
    public async Task ProductLists_During92FakeImportsAnd184FakeMediaDownloads_NoExports()
    {
        await using var database = await LocalMySqlDatabase.CreateAsync();
        int storeId, userId, categoryId, connectionId;
        await using (var db = database.CreateContext())
        {
            var data = new TestDataBuilder(db);
            var user = data.AddUser();
            var store = data.AddStore(members: user);
            var category = data.AddCategory(store.Id);
            var connection = data.AddConnection(store.Id, adapterKey: "fake");
            var mapping = data.AddMapping(connection.Id, category.Id);
            mapping.graphJson = System.Text.Json.JsonSerializer.Serialize(new { nodes = Array.Empty<object>(), padding = new string('x', 512 * 1024) });
            var attribute = data.AddStoreAttribute(store.Id, type: AttributeType.Select,
                options: System.Text.Json.JsonSerializer.Serialize(Enumerable.Range(0, 100).Select(i => new { value = i.ToString(), label = $"Option {i}" })));
            var products = Enumerable.Range(0, 1000).Select(i => data.AddProduct(store.Id,
                name: $"Fixture product {i}", categoryId: category.Id)).ToList();
            foreach (var product in products) product.description = new string('d', 4000);
            await db.SaveChangesAsync();
            foreach (var product in products)
            {
                db.storeProductAttributeValues.Add(new StoreProductAttributeValue { productId = product.Id, storeAttributeId = attribute.Id, value = "1" });
                db.productMarketplaceLinks.Add(new ProductMarketplaceLink
                {
                    productId = product.Id,
                    connectionId = connection.Id,
                    categoryMappingId = mapping.Id,
                    externalProductId = $"fixture-{product.Id}"
                });
                for (var i = 0; i < 4; i++)
                {
                    var file = new Models.File
                    {
                        productId = product.Id,
                        authorUserId = user.Id,
                        contentType = "image/jpeg",
                        filePath = $"fixture/{product.Id}/{i}.jpg"
                    };
                    db.images.Add(new ImageReference { productId = product.Id, file = file, index = i });
                }
            }
            await db.SaveChangesAsync();
            (storeId, userId, categoryId, connectionId) = (store.Id, user.Id, category.Id, connection.Id);
        }

        async Task<double> ReadPage(bool legacy)
        {
            await using var db = database.CreateContext();
            var timer = Stopwatch.StartNew();
            if (legacy)
            {
                var total = await db.products.Where(p => p.storeId == storeId).CountAsync();
                var rows = await db.products.AsNoTracking().Where(p => p.storeId == storeId).OrderByDescending(p => p.Id)
                    .AsSplitQuery().Include(p => p.files).Include(p => p.images).ThenInclude(i => i.file)
                    .Include(p => p.category).Include(p => p.storeAttributeValues).ThenInclude(v => v.storeAttribute)
                    .Include(p => p.marketplaceLinks).ThenInclude(l => l.connection)
                    .Include(p => p.marketplaceLinks).ThenInclude(l => l.categoryMapping).Take(25).ToListAsync();
                using var provider = new ServiceCollection().BuildServiceProvider();
                var dtos = rows.Select(p => ((IMapToDTO<Product.ProductReviewDTO>)p).MapToDTO(provider)).ToList();
                _ = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { items = dtos, totalCount = total });
                Assert.Equal(25, rows.Count);
            }
            else
            {
                var response = Assert.IsType<OkObjectResult>(await ProductCatalogPerformanceTests.Controller(db, storeId)
                    .GetAllProducts(null, pageSize: 25));
                _ = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(response.Value);
            }
            return timer.Elapsed.TotalMilliseconds;
        }
        await ReadPage(true); // Warm both query pipelines before comparing steady-state reads.
        await ReadPage(false);
        var legacyTimes = new List<double>();
        var idleTimes = new List<double>();
        for (var i = 0; i < 8; i++) legacyTimes.Add(await ReadPage(true));
        for (var i = 0; i < 12; i++) idleTimes.Add(await ReadPage(false));

        var mediaQueue = System.Threading.Channels.Channel.CreateUnbounded<int>();
        var storage = new RecordingCatalogStorage();
        var writer = Task.Run(async () =>
        {
            try
            {
                for (var i = 0; i < 92; i++)
                {
                    await using var db = database.CreateContext();
                    var adapter = DispatchProxy.Create<IMarketplaceAdapter, TransactionImportAdapterProxy>();
                    var proxy = (TransactionImportAdapterProxy)(object)adapter;
                    proxy.Db = db;
                    proxy.Source = new ExternalProduct($"load-{i}", $"LOAD-{i}", $"Imported {i}", Price: 100m,
                        Barcode: ProductController.GenerateEan13(), ImageUrls: ["https://8.8.8.8/a.jpg", "https://8.8.8.8/b.jpg"]);
                    var result = await CatalogTransactionTestSupport.ImportService(db, adapter)
                        .ImportProductAsync(connectionId, $"load-{i}", categoryId, userId);
                    Assert.True(result.IsSuccess, result.ErrorMessage);
                    foreach (var id in await db.pendingImageDownloads.Where(p => p.productId == result.ProductId).Select(p => p.Id).ToListAsync())
                        await mediaQueue.Writer.WriteAsync(id);
                }
            }
            finally { mediaQueue.Writer.TryComplete(); }
        });
        var media = Task.Run(async () =>
        {
            await foreach (var id in mediaQueue.Reader.ReadAllAsync())
            {
                await using var db = database.CreateContext();
                var logger = new RecordingCatalogLogger(db);
                var worker = new ImageDownloadBackgroundService(null!, NullLogger<ImageDownloadBackgroundService>.Instance, logger);
                await worker.ProcessSingleImageAsync(db, storage, new QuotaService(TestDbFactory.CreateUnitOfWork(db), db),
                    await db.pendingImageDownloads.SingleAsync(p => p.Id == id), CancellationToken.None);
            }
        });
        var loadedTimes = new System.Collections.Concurrent.ConcurrentBag<double>();
        var overlap = 0;
        async Task ReadLoop()
        {
            for (var i = 0; i < 30; i++)
            {
                if (!writer.IsCompleted || !media.IsCompleted) Interlocked.Increment(ref overlap);
                loadedTimes.Add(await ReadPage(false));
            }
        }
        await Task.WhenAll(writer, media, ReadLoop(), ReadLoop());
        await using (var db = database.CreateContext())
        {
            Assert.Equal(1092, await db.products.CountAsync());
            Assert.Equal(184, await db.pendingImageDownloads.CountAsync(p => p.status == ImageDownloadStatus.Completed));
            Assert.Equal(0, await db.pendingImageDownloads.CountAsync(p => p.status == ImageDownloadStatus.Failed));
        }
        void Report(string name, IEnumerable<double> times)
        {
            var sorted = times.Order().ToArray();
            output.WriteLine($"{name}: n={sorted.Length}; p50={sorted[sorted.Length / 2]:F1}ms; p95={sorted[(int)Math.Ceiling(sorted.Length * .95) - 1]:F1}ms; max={sorted[^1]:F1}ms");
        }
        Report("Legacy list query shape, idle", legacyTimes);
        Report("Projected controller list plus serialization, idle", idleTimes);
        Report("Projected controller list plus serialization, two readers during import/media", loadedTimes);
        output.WriteLine($"Overlapping reads: {overlap}/60. Synthetic fixture: 1000 cards, 4 images/card, 512 KiB graph, one 100-option store dictionary. No real marketplace, storage HTTP, API host, or exports were started.");
        await using (var connection = new MySqlConnector.MySqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "EXPLAIN ANALYZE SELECT Id FROM products WHERE storeId=@store AND deletedAt IS NULL AND categoryId=@category ORDER BY Id DESC LIMIT 25";
            command.Parameters.AddWithValue("@store", storeId);
            command.Parameters.AddWithValue("@category", categoryId);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) output.WriteLine("Category-page plan: " + reader.GetString(0));
        }
        Assert.True(overlap > 0);
    }

    [LocalMySqlFact]
    public async Task Transfer_StagesBytesWithoutDbLocksAndRollsBackCategoryGroupAndProduct()
    {
        await using var database = await LocalMySqlDatabase.CreateAsync();
        var fault = new MySqlWriteFault { Table = "products" };
        await using var db = database.CreateContext(fault);
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var sourceStore = data.AddStore("Source", members: user);
        var targetStore = data.AddStore("Target", members: user);
        var category = data.AddCategory(sourceStore.Id, "Source category");
        var source = data.AddProduct(sourceStore.Id, categoryId: category.Id, barcode: "2000000000015");
        await db.SaveChangesAsync();
        db.files.Add(new Models.File
        {
            productId = source.Id,
            authorUserId = user.Id,
            contentType = "image/jpeg",
            filePath = $"stores/{sourceStore.Id}/products/{source.Id}/photo.jpg"
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var storage = new RecordingCatalogStorage { OnCopy = () => Assert.Null(db.Database.CurrentTransaction) };
        var unit = TestDbFactory.CreateUnitOfWork(db);
        var service = new ProductTransferService(db, unit, storage, new QuotaService(unit, db), NullLogger<ProductTransferService>.Instance);
        var request = new TransferRequest
        {
            sourceProductId = source.Id,
            targetStoreId = targetStore.Id,
            mode = "move",
            categoryAction = "create",
            groupAction = "create",
            groupName = "New group",
            copyFiles = true
        };
        using (var cancellation = new CancellationTokenSource())
        {
            storage.OnCopy = () => { Assert.Null(db.Database.CurrentTransaction); cancellation.Cancel(); };
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExecuteAsync(request, sourceStore.Id,
                user.Id, cancellation.Token));
        }
        await db.SaveChangesAsync();
        Assert.Equal(0, await db.categories.CountAsync(c => c.storeId == targetStore.Id));
        Assert.Equal(0, await db.productGroups.CountAsync(g => g.storeId == targetStore.Id));
        storage.Deleted.Clear();
        storage.OnCopy = () => Assert.Null(db.Database.CurrentTransaction);
        fault.Enabled = true;
        await Assert.ThrowsAnyAsync<Exception>(() => service.ExecuteAsync(request, sourceStore.Id, user.Id,
            operationLogger: new RecordingCatalogLogger(db), operationId: "transfer-failure"));
        Assert.True(fault.Failures > 0);
        await db.SaveChangesAsync(); // A later queue-status save must not resurrect rolled-back entities.
        Assert.Equal(0, await db.categories.CountAsync(c => c.storeId == targetStore.Id));
        Assert.Equal(0, await db.productGroups.CountAsync(g => g.storeId == targetStore.Id));
        Assert.Equal(0, await db.products.CountAsync(p => p.storeId == targetStore.Id));
        Assert.Null((await db.products.AsNoTracking().SingleAsync()).deletedAt);
        Assert.Single(storage.Deleted);

        fault.Enabled = false;
        var result = await service.ExecuteAsync(request, sourceStore.Id, user.Id,
            operationLogger: new RecordingCatalogLogger(db), operationId: "transfer-success");
        Assert.True(result.success);
        Assert.Equal(1, result.filesCopied);
        Assert.Equal(1, await db.categories.CountAsync(c => c.storeId == targetStore.Id));
        Assert.Equal(1, await db.productGroups.CountAsync(g => g.storeId == targetStore.Id));
        Assert.NotNull((await db.products.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == source.Id)).deletedAt);
        var copiedFile = await db.files.SingleAsync(f => f.productId == result.newProductId);
        Assert.DoesNotContain(copiedFile.filePath, storage.Deleted);
    }

    [LocalMySqlFact]
    public async Task Retention_DeletesOperationsWithLogsInBoundedTransactions()
    {
        await using var database = await LocalMySqlDatabase.CreateAsync();
        var fault = new MySqlWriteFault { Table = "operations", DeleteOnly = true };
        await using var db = database.CreateContext(fault);
        var old = DateTime.UtcNow.AddDays(-10);
        for (var i = 0; i < 1005; i++)
        {
            db.operations.Add(new Operation
            {
                operationId = $"expired-{i}",
                storeId = 1,
                authorUserId = 1,
                type = "test",
                title = "Fixture",
                status = "completed",
                createdAt = old,
                finishedAt = old
            });
            db.operationLogEntries.Add(new OperationLogEntry { operationId = $"expired-{i}", level = "info", message = "Fixture", timestamp = old });
        }
        db.operations.Add(new Operation
        {
            operationId = "active",
            storeId = 1,
            authorUserId = 1,
            type = "test",
            title = "Active",
            status = "running",
            createdAt = old
        });
        db.operationLogEntries.Add(new OperationLogEntry { operationId = "active", level = "info", message = "Keep", timestamp = old });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        fault.Enabled = true;
        await Assert.ThrowsAnyAsync<Exception>(() => QueueCleanupBackgroundService.DeleteTerminalOperationsAsync(db,
            DateTime.UtcNow.AddDays(-7), CancellationToken.None));
        Assert.Equal(1006, await db.operations.CountAsync());
        Assert.Equal(1006, await db.operationLogEntries.CountAsync());
        fault.Enabled = false;
        Assert.Equal(1005, await QueueCleanupBackgroundService.DeleteTerminalOperationsAsync(db, DateTime.UtcNow.AddDays(-7), CancellationToken.None));
        Assert.Equal("active", (await db.operations.SingleAsync()).operationId);
        Assert.Equal("active", (await db.operationLogEntries.SingleAsync()).operationId);
        db.operationLogEntries.AddRange(
            new OperationLogEntry { operationId = "already-deleted", level = "info", message = "Old orphan", timestamp = old },
            new OperationLogEntry { operationId = "not-yet-started", level = "info", message = "Recent orphan", timestamp = DateTime.UtcNow });
        await db.SaveChangesAsync();
        Assert.Equal(1, await QueueCleanupBackgroundService.DeleteInBatchesAsync(db.operationLogEntries.Where(e =>
            e.timestamp < DateTime.UtcNow.AddDays(-7) && !db.operations.Any(o => o.operationId == e.operationId)), CancellationToken.None));
        Assert.Equal(2, await db.operationLogEntries.CountAsync());
    }

    [LocalMySqlFact]
    public async Task Transfer_RealLockTimeoutRetriesOnlyDatabaseWorkWithoutRecopyingFiles()
    {
        await using var database = await LocalMySqlDatabase.CreateAsync();
        var observer = new MySqlWriteFault();
        await using var db = database.CreateContext(observer);
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET SESSION innodb_lock_wait_timeout=1");
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var sourceStore = data.AddStore("Source", members: user);
        var targetStore = data.AddStore("Target", members: user);
        var category = data.AddCategory(sourceStore.Id, "To clone");
        var product = data.AddProduct(sourceStore.Id, categoryId: category.Id);
        await db.SaveChangesAsync();
        db.files.Add(new Models.File { productId = product.Id, authorUserId = user.Id, contentType = "image/jpeg", filePath = "source.jpg" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await using var blocker = new MySqlConnector.MySqlConnection(database.ConnectionString);
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using (var command = blocker.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT Id FROM stores WHERE Id = @id FOR UPDATE";
            command.Parameters.AddWithValue("@id", targetStore.Id);
            await command.ExecuteScalarAsync();
        }
        Task? release = null;
        var storage = new RecordingCatalogStorage
        {
            OnCopy = () =>
            {
                Assert.Null(db.Database.CurrentTransaction);
                release = Task.Run(async () => { await Task.Delay(1600); await transaction.CommitAsync(); });
            }
        };
        var unit = TestDbFactory.CreateUnitOfWork(db);
        var service = new ProductTransferService(db, unit, storage, new QuotaService(unit, db), NullLogger<ProductTransferService>.Instance);
        TransferResult result;
        try
        {
            result = await service.ExecuteAsync(new TransferRequest
            {
                sourceProductId = product.Id,
                targetStoreId = targetStore.Id,
                mode = "clone",
                categoryAction = "create",
                groupAction = "create",
                groupName = "Retried group",
                copyFiles = true
            }, sourceStore.Id, user.Id);
        }
        finally
        {
            if (release != null) await release;
            else await transaction.RollbackAsync();
        }
        Assert.True(observer.LockWaitFailures > 0, "The fixture must exercise a real MySQL lock-wait retry.");
        Assert.True(result.success);
        Assert.Single(storage.Objects);
        Assert.Empty(storage.Deleted);
        Assert.Equal(1, await db.products.CountAsync(p => p.storeId == targetStore.Id));
        Assert.Equal(1, await db.categories.CountAsync(c => c.storeId == targetStore.Id));
        Assert.Equal(1, await db.productGroups.CountAsync(g => g.storeId == targetStore.Id));
        Assert.Equal(1, await db.files.CountAsync(f => f.productId == result.newProductId));
    }

    [LocalMySqlFact]
    public async Task AttributeReplacement_FailedInsertRollsBackOldValuesAndProductFields()
    {
        await using var database = await LocalMySqlDatabase.CreateAsync();
        var fault = new MySqlWriteFault { Table = "productAttributeValues" };
        await using var db = database.CreateContext(fault);
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var category = data.AddCategory(store.Id);
        var attribute = data.AddCategoryAttribute(category.Id);
        var product = data.AddProduct(store.Id, name: "Original", categoryId: category.Id);
        db.productAttributeValues.Add(new ProductAttributeValue { productId = product.Id, attributeId = attribute.Id, value = "old" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var unit = TestDbFactory.CreateUnitOfWork(db);
        var uniqueness = new AttributeUniquenessService(db);
        var controller = new ProductController(new FakeCurrentUserService { SelectedStoreId = store.Id }, unit,
            new ServiceCollection().BuildServiceProvider(), NullLogger<ProductController>.Instance, db,
            new ExcelProductService(), new QuotaService(unit, db), new RecordingCatalogLogger(db), uniqueness,
            new ProductAttributeFileService(db), new ProductBulkUpdateService(db, unit, uniqueness));
        var update = new Product.UpdateForm { name = "Changed", attributeValues = [new() { attributeId = attribute.Id, value = "new" }] };
        fault.Enabled = true;
        await Assert.ThrowsAnyAsync<Exception>(() => controller.UpdateProduct(product.Id, update));
        Assert.True(fault.Failures > 0);
        db.ChangeTracker.Clear();
        Assert.Equal("Original", (await db.products.AsNoTracking().SingleAsync()).name);
        Assert.Equal("old", (await db.productAttributeValues.AsNoTracking().SingleAsync()).value);
        fault.Enabled = false;
        await controller.UpdateProduct(product.Id, update);
        db.ChangeTracker.Clear();
        Assert.Equal("Changed", (await db.products.AsNoTracking().SingleAsync()).name);
        Assert.Equal("new", (await db.productAttributeValues.AsNoTracking().SingleAsync()).value);
    }

    private sealed class MySqlWriteFault : DbCommandInterceptor
    {
        public string Table { get; init; } = "";
        public bool Enabled { get; set; }
        public bool UpdateOnly { get; init; }
        public bool DeleteOnly { get; init; }
        public int Failures { get; private set; }
        public CancellationTokenSource? Cancel { get; set; }
        public int LockWaitFailures { get; private set; }
        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken ct = default)
        {
            if (eventData.Exception is MySqlConnector.MySqlException { Number: 1205 }) LockWaitFailures++;
            return base.CommandFailedAsync(command, eventData, ct);
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            Check(command, ct);
            return base.ReaderExecutingAsync(command, eventData, result, ct);
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            Check(command, ct);
            return base.NonQueryExecutingAsync(command, eventData, result, ct);
        }
        private void Check(DbCommand command, CancellationToken ct)
        {
            var table = System.Text.RegularExpressions.Regex.Escape($"`{Table}`");
            var pattern = DeleteOnly ? $@"DELETE\s+(?:FROM\s+|`[^`]+`\s+FROM\s+){table}"
                : $@"{(UpdateOnly ? "UPDATE" : "INSERT INTO")}\s+{table}";
            if (Enabled && System.Text.RegularExpressions.Regex.IsMatch(command.CommandText, pattern))
            {
                Failures++;
                Cancel?.Cancel();
                ct.ThrowIfCancellationRequested();
                throw new InvalidOperationException("Injected MySQL catalog failure");
            }
        }
    }
}
