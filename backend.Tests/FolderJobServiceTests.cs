using Delegat.Enums;
using Delegat.Infrastructure.Configuration;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Delegat.Services;
using Delegat.Services.FolderJobs;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProductFile = Delegat.Models.File;

namespace Delegat.Tests;

public class FolderJobServiceTests
{
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

    private static (DataContext db, FolderJobService service, FakeStorage storage, FakeOperationLogger ops) Create()
    {
        var db = TestDbFactory.CreateDb();
        var storage = new FakeStorage();
        var ops = new FakeOperationLogger();
        var thumbs = new ThumbnailService(storage, NullLogger<ThumbnailService>.Instance);
        var service = new FolderJobService(
            db, storage, ops, thumbs, NullLogger<FolderJobService>.Instance);
        return (db, service, storage, ops);
    }

    [Fact]
    public async Task GetProductScopeStats_Root_CountsAllProductFiles()
    {
        var (db, service, _, _) = Create();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore();
        var product = data.AddProduct(store.Id);
        await data.SaveAsync();

        var folder = new ProductFolder { productId = product.Id, name = "docs" };
        db.productFolders.Add(folder);
        await db.SaveChangesAsync();

        db.files.AddRange(
            NewProductFile(product.Id, user.Id, length: 10, folderId: null),
            NewProductFile(product.Id, user.Id, length: 20, folderId: folder.Id));
        await db.SaveChangesAsync();

        var (count, bytes, name) = await service.GetProductScopeStatsAsync(product.Id, null, CancellationToken.None);

        Assert.Equal(2, count);
        Assert.Equal(30, bytes);
        Assert.Equal("files", name);
    }

    [Fact]
    public async Task GetProductScopeStats_Folder_IncludesDescendantsOnly()
    {
        var (db, service, _, _) = Create();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore();
        var product = data.AddProduct(store.Id);
        await data.SaveAsync();

        var parent = new ProductFolder { productId = product.Id, name = "parent" };
        db.productFolders.Add(parent);
        await db.SaveChangesAsync();
        var child = new ProductFolder { productId = product.Id, name = "child", parentFolderId = parent.Id };
        var other = new ProductFolder { productId = product.Id, name = "other" };
        db.productFolders.AddRange(child, other);
        await db.SaveChangesAsync();

        db.files.AddRange(
            NewProductFile(product.Id, user.Id, length: 5, folderId: parent.Id),
            NewProductFile(product.Id, user.Id, length: 7, folderId: child.Id),
            NewProductFile(product.Id, user.Id, length: 100, folderId: other.Id),
            NewProductFile(product.Id, user.Id, length: 50, folderId: null));
        await db.SaveChangesAsync();

        var (count, bytes, name) = await service.GetProductScopeStatsAsync(product.Id, parent.Id, CancellationToken.None);

        Assert.Equal(2, count);
        Assert.Equal(12, bytes);
        Assert.Equal("parent", name);
    }

    [Fact]
    public async Task GetProductScopeStats_MissingFolder_ThrowsNotFound()
    {
        var (db, service, _, _) = Create();
        var data = new TestDataBuilder(db);
        var product = data.AddProduct(data.AddStore().Id);
        await data.SaveAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            service.GetProductScopeStatsAsync(product.Id, 999, CancellationToken.None));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task GetStoreScopeStats_Folder_IncludesDescendantsOnly()
    {
        var (db, service, _, _) = Create();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore();
        await data.SaveAsync();

        var parent = new StoreFolder { storeId = store.Id, name = "assets" };
        db.storeFolders.Add(parent);
        await db.SaveChangesAsync();
        var child = new StoreFolder { storeId = store.Id, name = "nested", parentFolderId = parent.Id };
        db.storeFolders.Add(child);
        await db.SaveChangesAsync();

        db.storeFiles.AddRange(
            NewStoreFile(store.Id, user.Id, length: 3, folderId: parent.Id),
            NewStoreFile(store.Id, user.Id, length: 4, folderId: child.Id),
            NewStoreFile(store.Id, user.Id, length: 9, folderId: null));
        await db.SaveChangesAsync();

        var (count, bytes, name) = await service.GetStoreScopeStatsAsync(store.Id, parent.Id, CancellationToken.None);

        Assert.Equal(2, count);
        Assert.Equal(7, bytes);
        Assert.Equal("assets", name);
    }

    [Fact]
    public async Task EnqueueArchive_PersistsPendingJobAndStartsOperation()
    {
        var (db, service, _, ops) = Create();
        using var files = SetEnv("STORAGE_MAX_ARCHIVE_FILE_COUNT", "100");
        using var bytes = SetEnv("STORAGE_MAX_ARCHIVE_BYTES", "100000");

        var operationId = await service.EnqueueArchiveAsync(
            storeId: 1,
            authorUserId: 2,
            scope: FolderJobScope.Product,
            productId: 10,
            folderId: 20,
            folderName: "Photos",
            fileCount: 3,
            totalBytes: 300,
            CancellationToken.None);

        var job = Assert.Single(db.pendingFolderJobs);
        Assert.Equal(operationId, job.operationId);
        Assert.Equal(FolderJobType.Archive, job.jobType);
        Assert.Equal(FolderJobScope.Product, job.scope);
        Assert.Equal(10, job.productId);
        Assert.Equal(20, job.folderId);
        Assert.Equal(FolderJobStatus.Pending, job.status);
        Assert.Equal(3, job.totalItems);
        Assert.Equal("folder-archive", ops.LastStartType);
        Assert.Equal($"Archive: Photos", ops.LastStartTitle);
    }

    [Fact]
    public async Task EnqueueArchive_OverLimit_ThrowsWithoutPersisting()
    {
        var (db, service, _, _) = Create();
        using var files = SetEnv("STORAGE_MAX_ARCHIVE_FILE_COUNT", "2");
        using var bytes = SetEnv("STORAGE_MAX_ARCHIVE_BYTES", "1000");

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            service.EnqueueArchiveAsync(
                storeId: 1, authorUserId: 1, scope: FolderJobScope.Store,
                productId: null, folderId: null, folderName: "root",
                fileCount: 3, totalBytes: 10, CancellationToken.None));

        Assert.Equal("archive_too_large", ex.Code);
        Assert.Empty(db.pendingFolderJobs);
    }

    [Fact]
    public async Task EnqueueDelete_PersistsPendingJob()
    {
        var (db, service, _, ops) = Create();

        var operationId = await service.EnqueueDeleteAsync(
            storeId: 5,
            authorUserId: 6,
            scope: FolderJobScope.Store,
            productId: null,
            folderId: 7,
            folderName: "Trash",
            totalFileCount: 0,
            CancellationToken.None);

        var job = Assert.Single(db.pendingFolderJobs);
        Assert.Equal(operationId, job.operationId);
        Assert.Equal(FolderJobType.Delete, job.jobType);
        Assert.Equal(7, job.folderId);
        Assert.Equal(1, job.totalItems); // Math.Max(0, 1)
        Assert.Equal("folder-delete", ops.LastStartType);
    }

    [Fact]
    public async Task CleanupExpiredArtifacts_DeletesOldCompletedArchivesOnly()
    {
        var (db, service, storage, _) = Create();
        using var _ = SetEnv("STORAGE_ARCHIVE_ARTIFACT_TTL_HOURS", "1");

        db.pendingFolderJobs.AddRange(
            new PendingFolderJob
            {
                storeId = 1,
                authorUserId = 1,
                operationId = "old",
                jobType = FolderJobType.Archive,
                status = FolderJobStatus.Completed,
                resultObjectKey = "folder-artifacts/1/old.zip",
                completedAt = DateTime.UtcNow.AddHours(-3),
                createdAt = DateTime.UtcNow.AddHours(-4),
            },
            new PendingFolderJob
            {
                storeId = 1,
                authorUserId = 1,
                operationId = "fresh",
                jobType = FolderJobType.Archive,
                status = FolderJobStatus.Completed,
                resultObjectKey = "folder-artifacts/1/fresh.zip",
                completedAt = DateTime.UtcNow.AddMinutes(-10),
                createdAt = DateTime.UtcNow.AddMinutes(-20),
            },
            new PendingFolderJob
            {
                storeId = 1,
                authorUserId = 1,
                operationId = "delete-job",
                jobType = FolderJobType.Delete,
                status = FolderJobStatus.Completed,
                resultObjectKey = null,
                completedAt = DateTime.UtcNow.AddHours(-5),
                createdAt = DateTime.UtcNow.AddHours(-5),
            });
        await db.SaveChangesAsync();

        await service.CleanupExpiredArtifactsAsync(CancellationToken.None);

        var old = await db.pendingFolderJobs.SingleAsync(j => j.operationId == "old");
        var fresh = await db.pendingFolderJobs.SingleAsync(j => j.operationId == "fresh");
        Assert.Null(old.resultObjectKey);
        Assert.Null(old.resultBytes);
        Assert.Equal("folder-artifacts/1/fresh.zip", fresh.resultObjectKey);
        Assert.Contains("folder-artifacts/1/old.zip", storage.DeletedPaths);
        Assert.DoesNotContain("folder-artifacts/1/fresh.zip", storage.DeletedPaths);
    }

    [Theory]
    [InlineData("Photos", "Photos")]
    [InlineData("   ", "archive")]
    [InlineData("", "archive")]
    public void SanitizeFileName_PreservesValidOrFallsBack(string input, string expected)
    {
        Assert.Equal(expected, FolderJobService.SanitizeFileName(input));
    }

    [Fact]
    public void SanitizeFileName_ReplacesPlatformInvalidChars()
    {
        var invalid = Path.GetInvalidFileNameChars().Where(c => !char.IsWhiteSpace(c)).Take(3).ToArray();
        Assert.NotEmpty(invalid);

        var input = "name" + new string(invalid);
        var sanitized = FolderJobService.SanitizeFileName(input);

        Assert.StartsWith("name", sanitized);
        foreach (var c in invalid)
            Assert.DoesNotContain(c, sanitized);
    }

    private static ProductFile NewProductFile(int productId, int userId, long length, int? folderId) => new()
    {
        productId = productId,
        authorUserId = userId,
        folderId = folderId,
        filePath = $"products/{productId}/{Guid.NewGuid():N}.bin",
        contentType = "application/octet-stream",
        length = length,
        fileType = FileType.Other,
        uploadDateTime = DateTime.UtcNow,
    };

    private static StoreFile NewStoreFile(int storeId, int userId, long length, int? folderId) => new()
    {
        storeId = storeId,
        authorUserId = userId,
        folderId = folderId,
        filePath = $"stores/{storeId}/{Guid.NewGuid():N}.bin",
        contentType = "application/octet-stream",
        length = length,
        fileType = FileType.Other,
        uploadDateTime = DateTime.UtcNow,
    };

    private sealed class FakeOperationLogger : IOperationLogger
    {
        public string? LastStartType { get; private set; }
        public string? LastStartTitle { get; private set; }

        public Task StartAsync(string operationId, int storeId, int authorUserId, string type, string title,
            int? connectionId = null, string? connectionName = null,
            string? batchId = null, string? batchType = null, int? totalItems = null)
        {
            LastStartType = type;
            LastStartTitle = title;
            return Task.CompletedTask;
        }

        public Task LogAsync(string operationId, int storeId, string level, string message) => Task.CompletedTask;
        public Task ProgressAsync(string operationId, int storeId, int total, int completed, int failed) => Task.CompletedTask;
        public Task FinishAsync(string operationId, int storeId, string status, string? summary = null) => Task.CompletedTask;
        public Task UpdateStatusAsync(string operationId, int storeId, string status) => Task.CompletedTask;
    }

    private sealed class FakeStorage : IStorageService
    {
        public List<string> DeletedPaths { get; } = new();
        public string Bucket => "test";

        public Task<bool> DeleteAsync(string path, CancellationToken ct = default)
        {
            DeletedPaths.Add(path);
            return Task.FromResult(true);
        }

        public Task<bool> UploadAsync(IFormFile file, string path, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> UploadAsync(Stream stream, string path, long contentLength, string contentType, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> UploadFromUriAsync(string uri, string path, CancellationToken ct = default) => Task.FromResult(true);
        public Task<FileUploadResult?> UploadFromUriWithMetadataAsync(string uri, string path, CancellationToken ct = default) =>
            Task.FromResult<FileUploadResult?>(null);
        public Task<bool> CopyAsync(string sourcePath, string destinationPath, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => Task.FromResult(false);
        public Task<Stream?> GetStreamAsync(string path, CancellationToken ct = default) => Task.FromResult<Stream?>(null);
        public string GetFullPath(string path) => path;
        public Task StreamToAsync(string path, Stream outputStream, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> GetPresignedUrlAsync(string path, int expirySeconds = 3600) => Task.FromResult(path);
        public Task<string> GetPublicPresignedUrlAsync(string path, int expirySeconds = 3600) => Task.FromResult(path);
        public Task<List<string>> ListObjectsByPrefixAsync(string prefix, CancellationToken ct = default) =>
            Task.FromResult(new List<string>());
    }
}
