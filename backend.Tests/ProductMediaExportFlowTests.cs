using System.Reflection;
using Delegat.Controllers;
using Delegat.Enums;
using Delegat.Infrastructure.Storage;
using Delegat.Marketplace.SDK;
using Delegat.Marketplace.SDK.RateLimit;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Models.Products;
using Delegat.Services;
using Delegat.Services.Marketplace;
using Delegat.Services.Marketplace.Graph;
using Delegat.Tests.Fakes;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public sealed class ProductMediaExportFlowTests
{
    [Fact]
    public async Task SingleExport_CreatesGrantOnlyImmediatelyBeforeFakeAdapterAndSendsStableVideoUrl()
    {
        var (db, product, connection, file, storage) = await CreateScenarioAsync("yandex");
        var adapter = CreateAdapter("yandex", new MediaCapabilities(
            CanExportVideos: true,
            UsesPermanentUrls: true,
            MaxVideos: 6), () => db.productMediaExportGrants.Count());
        var service = CreateProductSyncService(db, adapter, storage);

        Assert.Empty(await db.productMediaExportGrants.ToListAsync());
        var readiness = await service.CheckExportReadinessAsync([product.Id], connection.Id);
        Assert.True(Assert.Single(readiness).IsReady);
        Assert.Empty(await db.productMediaExportGrants.ToListAsync());
        var result = await service.ExportProductAsync(product.Id, connection.Id);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var adapterProxy = Assert.IsAssignableFrom<FakeVideoExportAdapterProxy>((object)adapter);
        Assert.Equal(1, adapterProxy.SingleExportGrantCount);
        var videoUrl = Assert.Single(Assert.IsType<List<string>>(adapterProxy.SingleExportData!["videos"]));
        Assert.StartsWith("https://media.example.test/api/pub/media-export/", videoUrl);
        Assert.DoesNotContain("untrusted", videoUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(file.Id, (await db.productMediaExportGrants.SingleAsync()).fileId);
    }

    [Fact]
    public async Task BulkExport_CreatesGrantsOnlyForAdapterPayloads()
    {
        var (db, product, connection, file, storage) = await CreateScenarioAsync("yandex");
        var adapter = CreateAdapter("yandex", new MediaCapabilities(
            CanExportVideos: true,
            UsesPermanentUrls: true,
            MaxVideos: 6), () => db.productMediaExportGrants.Count());
        var service = CreateProductSyncService(db, adapter, storage);

        Assert.Empty(await db.productMediaExportGrants.ToListAsync());
        var result = await service.ExportProductsBulkAsync([product.Id], connection.Id);

        // The fake intentionally reports a declined item so this test stays focused on
        // the exact adapter-bound payload (successful-result link lookup is a separate path).
        Assert.Equal(1, result.FailedCount);
        var adapterProxy = Assert.IsAssignableFrom<FakeVideoExportAdapterProxy>((object)adapter);
        Assert.Equal(1, adapterProxy.BulkExportGrantCount);
        var exportedItem = Assert.Single(adapterProxy.BulkExportItems!);
        var videoUrl = Assert.Single(Assert.IsType<List<string>>(exportedItem.ProductData["videos"]));
        Assert.StartsWith("https://media.example.test/api/pub/media-export/", videoUrl);
        Assert.Equal(file.Id, (await db.productMediaExportGrants.SingleAsync()).fileId);
    }

    [Fact]
    public async Task WildberriesSingleExport_UsesHostPreparedPermanentVideoGrant()
    {
        var (db, product, connection, file, storage) = await CreateScenarioAsync("wildberries");
        var adapter = CreateAdapter("wildberries", new MediaCapabilities(
            CanImportVideos: true,
            CanExportVideos: true,
            UsesPermanentUrls: true,
            MaxVideos: 1,
            AcceptedVideoContentTypes: ["video/mp4", "video/quicktime"]),
            () => db.productMediaExportGrants.Count());
        var service = CreateProductSyncService(db, adapter, storage);

        var result = await service.ExportProductAsync(product.Id, connection.Id);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var proxy = Assert.IsAssignableFrom<FakeVideoExportAdapterProxy>((object)adapter);
        Assert.Equal(1, proxy.SingleExportGrantCount);
        var url = Assert.Single(Assert.IsType<List<string>>(proxy.SingleExportData!["videos"]));
        Assert.StartsWith("https://media.example.test/api/pub/media-export/", url);
        var grant = await db.productMediaExportGrants.SingleAsync();
        Assert.Equal(file.Id, grant.fileId);
        Assert.Equal(connection.Id, grant.connectionId);
    }

    [Fact]
    public async Task OzonSingleExport_UsesHostPreparedPermanentVideoGrantAndGalleryName()
    {
        var (db, product, connection, file, storage) = await CreateScenarioAsync("ozon");
        var adapter = CreateAdapter("ozon", new MediaCapabilities(
            CanImportVideos: true,
            CanExportVideos: true,
            UsesPermanentUrls: true), () => db.productMediaExportGrants.Count());
        var service = CreateProductSyncService(db, adapter, storage);

        var result = await service.ExportProductAsync(product.Id, connection.Id);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var proxy = Assert.IsAssignableFrom<FakeVideoExportAdapterProxy>((object)adapter);
        Assert.Equal(1, proxy.SingleExportGrantCount);
        Assert.StartsWith(
            "https://media.example.test/api/pub/media-export/",
            Assert.Single(Assert.IsType<List<string>>(proxy.SingleExportData!["videos"])));
        Assert.Equal(["clip.mp4"], Assert.IsType<List<string>>(proxy.SingleExportData["videoNames"]));
        Assert.Equal(file.Id, (await db.productMediaExportGrants.SingleAsync()).fileId);
    }

    [Fact]
    public async Task SingleExport_LogsSuccessfulAdapterMediaWarningAsOperationWarning()
    {
        var (db, product, connection, _, storage) = await CreateScenarioAsync("wildberries");
        const string warning = "Card updated but media upload failed: fake failure";
        var operationLogger = new RecordingOperationLogger();
        var adapter = CreateAdapter(
            "wildberries",
            new MediaCapabilities(CanExportVideos: true, UsesPermanentUrls: true, MaxVideos: 1),
            () => db.productMediaExportGrants.Count(),
            singleErrorMessage: warning);
        var service = CreateProductSyncService(db, adapter, storage, operationLogger: operationLogger);

        var result = await service.ExportProductAsync(
            product.Id,
            connection.Id,
            operationId: "operation-1",
            operationStoreId: connection.storeId);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains(operationLogger.Entries,
            entry => entry.Level == "warning" && entry.Message == warning);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task AdapterWithoutPermanentVideoCapability_DoesNotCreateGrantOrReceiveVideoUrls(bool canExport, bool permanent)
    {
        var (db, product, connection, _, storage) = await CreateScenarioAsync("fake");
        var adapter = CreateAdapter("fake", new MediaCapabilities(
            CanExportVideos: canExport,
            UsesPermanentUrls: permanent,
            MaxVideos: 6), () => db.productMediaExportGrants.Count());
        var service = CreateProductSyncService(db, adapter, storage, mediaGrants: null);

        var result = await service.ExportProductAsync(product.Id, connection.Id);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var adapterProxy = Assert.IsAssignableFrom<FakeVideoExportAdapterProxy>((object)adapter);
        Assert.Equal(0, adapterProxy.SingleExportGrantCount);
        Assert.DoesNotContain("videos", adapterProxy.SingleExportData!.Keys);
        Assert.Empty(await db.productMediaExportGrants.ToListAsync());
    }

    private static async Task<(DataContext Db, Product Product, MarketplaceConnection Connection, Models.File File, IStorageService Storage)> CreateScenarioAsync(
        string adapterKey)
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: [user]);
        var category = data.AddCategory(store.Id);
        var product = data.AddProduct(store.Id, categoryId: category.Id);
        var connection = data.AddConnection(store.Id, adapterKey: adapterKey);
        var mapping = data.AddMapping(connection.Id, category.Id);
        mapping.graphJson = GraphGenerator.BuildGraphJson(
        [
            new FieldSpec
            {
                externalFieldKey = "videos",
                externalFieldName = "Videos",
                externalFieldType = "multiselect",
                sourceType = FieldMappingSourceType.Static,
                staticValue = "https://untrusted.example.test/video.mp4"
            }
        ]);
        var file = new Models.File
        {
            Id = 41,
            productId = product.Id,
            authorUserId = user.Id,
            filePath = "stores/1/products/1/media/clip.mp4",
            displayName = "clip.mp4",
            contentType = "video/mp4",
            length = 6,
            fileType = FileType.Other,
            uploadDateTime = DateTime.UtcNow
        };
        db.files.Add(file);
        db.images.Add(new ImageReference { productId = product.Id, fileId = file.Id, index = 0 });
        await data.SaveAsync();
        return (db, product, connection, file, new MemoryStorage("abcdef"u8.ToArray()));
    }

    private static IMarketplaceAdapter CreateAdapter(
        string key,
        MediaCapabilities mediaCapabilities,
        Func<int> grantCount,
        string? singleErrorMessage = null)
    {
        var adapter = DispatchProxy.Create<IMarketplaceAdapter, FakeVideoExportAdapterProxy>();
        var proxy = (FakeVideoExportAdapterProxy)(object)adapter;
        proxy.AdapterKey = key;
        proxy.MediaCapabilities = mediaCapabilities;
        proxy.GetGrantCount = grantCount;
        proxy.SingleErrorMessage = singleErrorMessage;
        return adapter;
    }

    private static ProductSyncService CreateProductSyncService(
        DataContext db,
        IMarketplaceAdapter adapter,
        IStorageService storage,
        ProductMediaExportGrantService? mediaGrants = null,
        IOperationLogger? operationLogger = null)
    {
        var mappingServices = new ServiceCollection()
            .AddSingleton(storage)
            .BuildServiceProvider();
        var mappingService = new MappingService(db, mappingServices);
        var quota = new QuotaService(TestDbFactory.CreateUnitOfWork(db), db);
        mediaGrants ??= new ProductMediaExportGrantService(
            db,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PUBLIC_BASE_URL"] = "https://media.example.test"
            }).Build(),
            new PublicOriginValidator(),
            storage);
        return new ProductSyncService(
            db,
            new SingleAdapterRegistry(adapter),
            mappingService,
            storage,
            NullLogger<ProductSyncService>.Instance,
            operationLogger ?? new NoopOperationLogger(),
            quota,
            mediaGrants);
    }

    public class FakeVideoExportAdapterProxy : DispatchProxy, IMarketplaceMediaCapabilities
    {
        public string AdapterKey { get; set; } = "yandex";
        public MediaCapabilities MediaCapabilities { get; set; } = new();
        public Func<int>? GetGrantCount { get; set; }
        public string? SingleErrorMessage { get; set; }
        public int SingleExportGrantCount { get; private set; }
        public int BulkExportGrantCount { get; private set; }
        public Dictionary<string, object>? SingleExportData { get; private set; }
        public List<BulkExportItem>? BulkExportItems { get; private set; }

        public MediaCapabilities GetMediaCapabilities() => MediaCapabilities;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                "get_Key" => AdapterKey,
                "get_DisplayName" => AdapterKey,
                "get_Capabilities" => Delegat.Services.Marketplace.MarketplaceCapabilities.ExportProduct,
                "GetFieldBasedGroupingConfig" => null,
                "ExportProductAsync" => ExportSingle(args),
                "ExportProductsBulkAsync" => ExportBulk(args),
                _ => throw new NotSupportedException($"Unexpected fake-adapter call: {targetMethod?.Name}")
            };
        }

        private Task<ExportResult> ExportSingle(object?[]? args)
        {
            SingleExportGrantCount = GetGrantCount?.Invoke() ?? 0;
            SingleExportData = Assert.IsType<Dictionary<string, object>>(args![1]);
            return Task.FromResult(new ExportResult(true, "fake-external-id", SingleErrorMessage));
        }

        private Task<BulkExportResult> ExportBulk(object?[]? args)
        {
            BulkExportGrantCount = GetGrantCount?.Invoke() ?? 0;
            BulkExportItems = Assert.IsType<List<BulkExportItem>>(args![1]);
            return Task.FromResult(new BulkExportResult(
                BulkExportItems.Count,
                0,
                BulkExportItems.Count,
                BulkExportItems.Select(item => new ExportItemResult(
                    item.LocalProductId,
                    false,
                    ErrorMessage: "Fake adapter test declined this item.")).ToList()));
        }
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

    private sealed class RecordingOperationLogger : IOperationLogger
    {
        public List<(string Level, string Message)> Entries { get; } = [];

        public Task StartAsync(string operationId, int storeId, int authorUserId, string type, string title,
            int? connectionId = null, string? connectionName = null,
            string? batchId = null, string? batchType = null, int? totalItems = null, int? authorApiKeyId = null)
            => Task.CompletedTask;

        public Task LogAsync(string operationId, int storeId, string level, string message)
        {
            Entries.Add((level, message));
            return Task.CompletedTask;
        }

        public Task ProgressAsync(string operationId, int storeId, int total, int completed, int failed)
            => Task.CompletedTask;

        public Task FinishAsync(string operationId, int storeId, string status, string? summary = null)
            => Task.CompletedTask;

        public Task UpdateStatusAsync(string operationId, int storeId, string status)
            => Task.CompletedTask;
    }

    private sealed class PublicOriginValidator : IPublicMediaExportBaseUrlValidator
    {
        public Task ValidateAsync(Uri baseUri, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class MemoryStorage(byte[] bytes) : IStorageService
    {
        public string Bucket => "tests";
        public Task<bool> UploadAsync(IFormFile file, string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> UploadAsync(Stream stream, string path, long contentLength, string contentType, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> UploadFromUriAsync(string uri, string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<FileUploadResult?> UploadFromUriWithMetadataAsync(string uri, string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> CopyAsync(string sourcePath, string destinationPath, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => Task.FromResult(true);
        public Task<Stream?> GetStreamAsync(string path, CancellationToken ct = default) => throw new NotImplementedException();
        public string GetFullPath(string path) => path;
        public async Task StreamToAsync(string path, Stream outputStream, CancellationToken ct = default)
            => await outputStream.WriteAsync(bytes, ct);
        public async Task StreamRangeToAsync(string path, long offset, long length, Stream outputStream, CancellationToken ct = default)
            => await outputStream.WriteAsync(bytes.AsMemory((int)offset, (int)length), ct);
        public Task<string> GetPresignedUrlAsync(string path, int expirySeconds = 3600) => throw new NotImplementedException();
        public Task<string> GetPublicPresignedUrlAsync(string path, int expirySeconds = 3600) => throw new NotImplementedException();
        public Task<List<string>> ListObjectsByPrefixAsync(string prefix, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
