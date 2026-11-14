using Delegat.Enums;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Delegat.Services;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

[Collection("EnvVars")]
public class ImageDownloadAttachTests
{
    [Fact]
    public async Task Worker_DownloadsTypedVideoIntoMixedGalleryAsNonImageFile()
    {
        var (worker, db, storage, pending, _) = await SeedPendingAsync();
        pending.mediaKind = PendingMediaKind.Video;
        pending.mediaFileName = "demo.mp4";
        await db.SaveChangesAsync();

        await worker.ProcessSingleImageAsync(db, storage, Quota(db), pending, CancellationToken.None);

        Assert.Equal(1, storage.VideoUploadCalls);
        Assert.Equal(ImageDownloadStatus.Completed, pending.status);
        var file = await db.files.SingleAsync(f => f.contentType == "video/mp4");
        Assert.Equal(FileType.Other, file.fileType);
        Assert.Equal("demo.mp4", file.displayName);
        var reference = await db.images.SingleAsync(i => i.fileId == file.Id);
        Assert.Equal(pending.imageIndex, reference.index);
    }

    [Fact]
    public async Task Worker_ClassifiesExtensionlessDocumentByContentType()
    {
        var (worker, db, storage, pending, _) = await SeedPendingAsync();
        pending.imageUrl = "https://8.8.8.8/download?file=invoice";
        pending.mediaKind = PendingMediaKind.Image;
        storage.UploadContentType = "application/pdf";
        await db.SaveChangesAsync();

        await worker.ProcessSingleImageAsync(db, storage, Quota(db), pending, CancellationToken.None);

        var file = await db.files.SingleAsync(item => item.sourceUrl == pending.imageUrl);
        Assert.Equal(FileType.Other, file.fileType);
        Assert.Equal("application/pdf", file.contentType);
        Assert.NotNull(file.displayName);
        Assert.EndsWith(".pdf", file.displayName);
        Assert.EndsWith(".jpg", file.filePath);
        Assert.NotNull(storage.LastUploadPath);
        Assert.EndsWith(".jpg", storage.LastUploadPath);
    }

    [Fact]
    public async Task Worker_AttachesCategoryFileToGalleryDownload()
    {
        var seeded = await SeedAttributePendingAsync(skipGallery: false);

        await seeded.Worker.ProcessSingleImageAsync(
            seeded.Db, seeded.Storage, Quota(seeded.Db), seeded.Pending, CancellationToken.None);

        Assert.Equal(ImageDownloadStatus.Completed, seeded.Pending.status);
        var value = await seeded.Db.productAttributeValues
            .Include(item => item.files)
            .SingleAsync(item => item.productId == seeded.ProductId);
        Assert.Equal(string.Empty, value.value);
        var attachment = Assert.Single(value.files);
        var file = await seeded.Db.files.SingleAsync(item => item.Id == attachment.fileId);
        Assert.Equal(seeded.ProductId, file.productId);
        Assert.Equal(seeded.Pending.imageUrl, file.sourceUrl);
        var reference = Assert.Single(await seeded.Db.images.ToListAsync());
        Assert.Equal(file.Id, reference.fileId);
    }

    [Fact]
    public async Task Worker_AttachesSkipGalleryDownloadWithoutCreatingImageReference()
    {
        var seeded = await SeedAttributePendingAsync(skipGallery: true);

        await seeded.Worker.ProcessSingleImageAsync(
            seeded.Db, seeded.Storage, Quota(seeded.Db), seeded.Pending, CancellationToken.None);

        Assert.Equal(ImageDownloadStatus.Completed, seeded.Pending.status);
        var value = await seeded.Db.productAttributeValues
            .Include(item => item.files)
            .SingleAsync(item => item.productId == seeded.ProductId);
        Assert.Equal(string.Empty, value.value);
        Assert.Single(value.files);
        Assert.Empty(await seeded.Db.images.ToListAsync());
    }

    [Fact]
    public async Task Worker_AttachesSkipGalleryDownloadToStoreFileAttribute()
    {
        var seeded = await SeedAttributePendingAsync(skipGallery: true, storeAttribute: true);

        await seeded.Worker.ProcessSingleImageAsync(
            seeded.Db, seeded.Storage, Quota(seeded.Db), seeded.Pending, CancellationToken.None);

        Assert.Equal(ImageDownloadStatus.Completed, seeded.Pending.status);
        var value = await seeded.Db.storeProductAttributeValues
            .Include(item => item.files)
            .SingleAsync(item => item.productId == seeded.ProductId);
        Assert.Equal(string.Empty, value.value);
        Assert.Single(value.files);
        Assert.Empty(await seeded.Db.images.ToListAsync());
    }

    [Fact]
    public async Task Worker_CleansSkipGalleryUploadWhenSingleFileTargetFillsDuringDownload()
    {
        var seeded = await SeedAttributePendingAsync(skipGallery: true);
        seeded.Storage.AfterUpload = () =>
        {
            var existingFile = new Models.File
            {
                productId = seeded.ProductId,
                authorUserId = seeded.UserId,
                filePath = "stores/1/products/1/media/managed.mp4",
                contentType = "video/mp4",
                length = 20,
                fileType = FileType.Other,
                uploadDateTime = DateTime.UtcNow,
                sourceUrl = "https://8.8.8.8/managed.mp4"
            };
            var value = new ProductAttributeValue
            {
                productId = seeded.ProductId,
                attributeId = seeded.AttributeId,
                value = string.Empty
            };
            value.files.Add(new ProductAttributeFile { file = existingFile, index = 0 });
            seeded.Db.files.Add(existingFile);
            seeded.Db.productAttributeValues.Add(value);
            seeded.Db.SaveChanges();
        };

        await seeded.Worker.ProcessSingleImageAsync(
            seeded.Db, seeded.Storage, Quota(seeded.Db), seeded.Pending, CancellationToken.None);

        Assert.Equal(1, seeded.Storage.UploadCalls);
        Assert.True(seeded.Storage.DeleteCalled);
        Assert.Equal(ImageDownloadStatus.Completed, seeded.Pending.status);
        Assert.DoesNotContain(
            await seeded.Db.files.ToListAsync(),
            file => file.sourceUrl == seeded.Pending.imageUrl);
        Assert.Empty(await seeded.Db.images.ToListAsync());
        var value = await seeded.Db.productAttributeValues
            .Include(item => item.files)
            .SingleAsync(item => item.productId == seeded.ProductId);
        Assert.Single(value.files);
    }

    [Fact]
    public async Task Worker_CancellationCleansVideoObjectAndLeavesQueueRowRecoverable()
    {
        var (worker, db, storage, pending, _) = await SeedPendingAsync();
        pending.mediaKind = PendingMediaKind.Video;
        pending.mediaFileName = "demo.mp4";
        storage.CancelVideoUpload = true;
        using var cancellation = new CancellationTokenSource();
        storage.CancellationSource = cancellation;
        await db.SaveChangesAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            worker.ProcessSingleImageAsync(db, storage, Quota(db), pending, cancellation.Token));

        Assert.Equal(ImageDownloadStatus.InProgress, pending.status);
        Assert.Empty(await db.files.Where(file => file.contentType == "video/mp4").ToListAsync());
        Assert.Empty(await db.images.Where(image => image.fileId != 1).ToListAsync());
        Assert.True(storage.DeleteCalled);
    }

    [Fact]
    public async Task Worker_DoesNotReplaceOccupiedGallerySlot()
    {
        var (worker, db, storage, pending, existing) = await SeedPendingAsync();
        db.images.Add(new ImageReference { fileId = existing.Id, productId = pending.productId, index = 0 });
        await db.SaveChangesAsync();

        await worker.ProcessSingleImageAsync(db, storage, Quota(db), pending, CancellationToken.None);

        var refs = await db.images.OrderBy(i => i.index).ToListAsync();
        Assert.Equal(2, refs.Count);
        Assert.Equal(existing.Id, refs[0].fileId);
        Assert.Equal(0, refs[0].index);
        Assert.NotEqual(existing.Id, refs[1].fileId);
        Assert.Equal(1, refs[1].index);
        Assert.Equal(ImageDownloadStatus.Completed, pending.status);
        Assert.Equal("https://cdn.example.test/new.jpg", (await db.files.SingleAsync(f => f.Id == refs[1].fileId)).sourceUrl);
    }

    [Fact]
    public async Task Worker_RedownloadsAlreadyImportedSourceUrl_AppendsGallery()
    {
        var (worker, db, storage, pending, existing) = await SeedPendingAsync(
            pendingUrl: "https://cdn.example.test/kept.jpg?b=2",
            existingSourceUrl: "https://cdn.example.test/kept.jpg?a=1");
        db.images.Add(new ImageReference { fileId = existing.Id, productId = pending.productId, index = 0 });
        await db.SaveChangesAsync();

        await worker.ProcessSingleImageAsync(db, storage, Quota(db), pending, CancellationToken.None);

        Assert.Equal(1, storage.UploadCalls);
        Assert.Equal(ImageDownloadStatus.Completed, pending.status);
        Assert.Equal(2, await db.files.CountAsync());
        var refs = await db.images.OrderBy(i => i.index).ToListAsync();
        Assert.Equal(2, refs.Count);
        Assert.Equal(existing.Id, refs[0].fileId);
        Assert.NotEqual(existing.Id, refs[1].fileId);
        Assert.Equal(1, refs[1].index);
    }

    [Fact]
    public async Task Worker_FileQuotaFailure_DoesNotRetry()
    {
        using var _ = SetEnv("MAX_FILES", "1");
        using var _s = SetEnv("MAX_STORAGE_GB", null);
        var (worker, db, storage, pending, _) = await SeedPendingAsync();

        await worker.ProcessSingleImageAsync(db, storage, Quota(db), pending, CancellationToken.None);

        Assert.Equal(0, storage.UploadCalls);
        Assert.Equal(ImageDownloadStatus.Failed, pending.status);
        Assert.Equal(PendingImageDownload.MAX_RETRIES, pending.retryCount);
        Assert.Contains("MAX_FILES", pending.lastError);
    }

    [Fact]
    public async Task Worker_StorageQuotaFailure_DeletesUploadAndDoesNotRetry()
    {
        using var _ = SetEnv("MAX_STORAGE_GB", "0.000000000000001");
        using var _f = SetEnv("MAX_FILES", null);
        var (worker, db, storage, pending, existing) = await SeedPendingAsync();
        existing.length = 1;
        await db.SaveChangesAsync();

        await worker.ProcessSingleImageAsync(db, storage, Quota(db), pending, CancellationToken.None);

        Assert.Equal(1, storage.UploadCalls);
        Assert.True(storage.DeleteCalled);
        Assert.Equal(ImageDownloadStatus.Failed, pending.status);
        Assert.Equal(PendingImageDownload.MAX_RETRIES, pending.retryCount);
        Assert.Equal(1, await db.files.CountAsync());
    }

    [Fact]
    public async Task Worker_DatabaseFailureAfterDownload_CleansObjectAndDoesNotAttachFile()
    {
        var interceptor = new FailNextFileSaveInterceptor();
        var options = new DbContextOptionsBuilder<DataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(interceptor)
            .Options;
        var db = new DataContext(options);
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore();
        var product = data.AddProduct(store.Id);
        var existing = new Models.File
        {
            Id = 1,
            productId = product.Id,
            authorUserId = user.Id,
            filePath = "stores/1/products/1/images/old.jpg",
            contentType = "image/jpeg",
            length = 10,
            fileType = FileType.Image,
            uploadDateTime = DateTime.UtcNow,
            sourceUrl = "https://cdn.example.test/old.jpg"
        };
        db.files.Add(existing);
        var pending = new PendingImageDownload
        {
            productId = product.Id,
            storeId = store.Id,
            authorUserId = user.Id,
            imageUrl = "https://cdn.example.test/new.jpg",
            imageIndex = 0,
            status = ImageDownloadStatus.Pending,
            createdAt = DateTime.UtcNow
        };
        db.pendingImageDownloads.Add(pending);
        await db.SaveChangesAsync();
        interceptor.FailNextFileSave = true;

        var worker = new ImageDownloadBackgroundService(
            scopeFactory: null!,
            NullLogger<ImageDownloadBackgroundService>.Instance,
            new NoopOperationLogger());
        var storage = new FakeStorage();
        await worker.ProcessSingleImageAsync(db, storage, Quota(db), pending, CancellationToken.None);

        Assert.Equal(ImageDownloadStatus.Failed, pending.status);
        Assert.Single(await db.files.ToListAsync());
        Assert.Empty(await db.images.ToListAsync());
        Assert.True(storage.DeleteCalled);
    }

    private static async Task<(
        ImageDownloadBackgroundService worker,
        DataContext db,
        FakeStorage storage,
        PendingImageDownload pending,
        Models.File existing)> SeedPendingAsync(
        string pendingUrl = "https://cdn.example.test/new.jpg",
        string existingSourceUrl = "https://cdn.example.test/old.jpg")
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore();
        var product = data.AddProduct(store.Id);
        var existing = new Models.File
        {
            Id = 1,
            productId = product.Id,
            authorUserId = user.Id,
            filePath = "stores/1/products/1/images/old.jpg",
            contentType = "image/jpeg",
            length = 10,
            fileType = FileType.Image,
            uploadDateTime = DateTime.UtcNow,
            sourceUrl = existingSourceUrl
        };
        db.files.Add(existing);
        var pending = new PendingImageDownload
        {
            productId = product.Id,
            storeId = store.Id,
            authorUserId = user.Id,
            imageUrl = pendingUrl,
            imageIndex = 0,
            status = ImageDownloadStatus.Pending,
            createdAt = DateTime.UtcNow
        };
        db.pendingImageDownloads.Add(pending);
        await db.SaveChangesAsync();

        var worker = new ImageDownloadBackgroundService(
            scopeFactory: null!,
            NullLogger<ImageDownloadBackgroundService>.Instance,
            new NoopOperationLogger());
        return (worker, db, new FakeStorage(), pending, existing);
    }

    private static async Task<(
        ImageDownloadBackgroundService Worker,
        DataContext Db,
        FakeStorage Storage,
        PendingImageDownload Pending,
        int AttributeId,
        int ProductId,
        int UserId)> SeedAttributePendingAsync(
        bool skipGallery,
        bool storeAttribute = false)
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore();
        var category = data.AddCategory(store.Id, "Target category");
        var product = data.AddProduct(store.Id, "Target product", "ART-1", category.Id);
        var attribute = storeAttribute
            ? (object)data.AddStoreAttribute(store.Id, "Target file", AttributeType.File)
            : data.AddCategoryAttribute(category.Id, "Target file", AttributeType.File);
        var attributeId = attribute switch
        {
            CategoryAttribute categoryAttribute => categoryAttribute.Id,
            StoreAttribute storeAttributeDefinition => storeAttributeDefinition.Id,
            _ => throw new InvalidOperationException("Unsupported test attribute")
        };
        var pending = new PendingImageDownload
        {
            productId = product.Id,
            storeId = store.Id,
            authorUserId = user.Id,
            imageUrl = "https://8.8.8.8/target.jpg",
            mediaKind = PendingMediaKind.Image,
            imageIndex = 0,
            status = ImageDownloadStatus.Pending,
            targetAttributeId = storeAttribute ? null : attributeId,
            targetStoreAttributeId = storeAttribute ? attributeId : null,
            skipGallery = skipGallery
        };
        db.pendingImageDownloads.Add(pending);
        await db.SaveChangesAsync();

        var worker = new ImageDownloadBackgroundService(
            scopeFactory: null!,
            NullLogger<ImageDownloadBackgroundService>.Instance,
            new NoopOperationLogger());
        return (worker, db, new FakeStorage(), pending, attributeId, product.Id, user.Id);
    }

    private static QuotaService Quota(DataContext db) =>
        new(TestDbFactory.CreateUnitOfWork(db), db);

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

    private sealed class FakeStorage : IStorageService
    {
        public int UploadCalls { get; private set; }
        public int VideoUploadCalls { get; private set; }
        public bool DeleteCalled { get; private set; }
        public bool CancelVideoUpload { get; set; }
        public CancellationTokenSource? CancellationSource { get; set; }
        public Action? AfterUpload { get; set; }
        public string UploadContentType { get; set; } = "image/jpeg";
        public string? LastUploadPath { get; private set; }
        public string Bucket => "test";

        public Task<FileUploadResult?> UploadFromUriWithMetadataAsync(string uri, string path, CancellationToken ct = default)
        {
            UploadCalls++;
            LastUploadPath = path;
            AfterUpload?.Invoke();
            return Task.FromResult<FileUploadResult?>(new FileUploadResult(UploadContentType, 1024));
        }

        public Task<FileUploadResult?> UploadVideoFromUriWithMetadataAsync(
            string uri, string path, long maxBytes, CancellationToken ct = default)
        {
            VideoUploadCalls++;
            AfterUpload?.Invoke();
            Assert.True(maxBytes > 0);
            if (CancelVideoUpload)
            {
                CancellationSource?.Cancel();
                throw new OperationCanceledException(ct);
            }
            return Task.FromResult<FileUploadResult?>(new FileUploadResult("video/mp4", 2048));
        }

        public Task<bool> DeleteAsync(string path, CancellationToken ct = default)
        {
            DeleteCalled = true;
            return Task.FromResult(true);
        }

        public Task<bool> UploadAsync(IFormFile file, string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> UploadAsync(Stream stream, string path, long contentLength, string contentType, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> UploadFromUriAsync(string uri, string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> CopyAsync(string sourcePath, string destinationPath, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Stream?> GetStreamAsync(string path, CancellationToken ct = default) => throw new NotImplementedException();
        public string GetFullPath(string path) => path;
        public Task StreamToAsync(string path, Stream outputStream, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string> GetPresignedUrlAsync(string path, int expirySeconds = 3600) => throw new NotImplementedException();
        public Task<string> GetPublicPresignedUrlAsync(string path, int expirySeconds = 3600) => throw new NotImplementedException();
        public Task<List<string>> ListObjectsByPrefixAsync(string prefix, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FailNextFileSaveInterceptor : SaveChangesInterceptor
    {
        public bool FailNextFileSave { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (FailNextFileSave
                && eventData.Context?.ChangeTracker.Entries<Models.File>()
                    .Any(entry => entry.State == EntityState.Added) == true)
            {
                FailNextFileSave = false;
                throw new InvalidOperationException("Injected file row persistence failure");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
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
}
