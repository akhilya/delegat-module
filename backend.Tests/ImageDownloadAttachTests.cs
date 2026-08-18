using Delegat.Enums;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Delegat.Services;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

[Collection("EnvVars")]
public class ImageDownloadAttachTests
{
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
        public bool DeleteCalled { get; private set; }
        public string Bucket => "test";

        public Task<FileUploadResult?> UploadFromUriWithMetadataAsync(string uri, string path, CancellationToken ct = default)
        {
            UploadCalls++;
            return Task.FromResult<FileUploadResult?>(new FileUploadResult("image/jpeg", 1024));
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

    private sealed class NoopOperationLogger : IOperationLogger
    {
        public Task StartAsync(string operationId, int storeId, int authorUserId, string type, string title,
            int? connectionId = null, string? connectionName = null,
            string? batchId = null, string? batchType = null, int? totalItems = null)
            => Task.CompletedTask;
        public Task LogAsync(string operationId, int storeId, string level, string message) => Task.CompletedTask;
        public Task ProgressAsync(string operationId, int storeId, int total, int completed, int failed) => Task.CompletedTask;
        public Task FinishAsync(string operationId, int storeId, string status, string? summary = null) => Task.CompletedTask;
        public Task UpdateStatusAsync(string operationId, int storeId, string status) => Task.CompletedTask;
    }
}
