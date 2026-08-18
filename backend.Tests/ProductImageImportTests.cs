using Delegat.Enums;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Products;
using Delegat.Services;
using Delegat.Services.Marketplace;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class ProductImageImportTests
{
    [Fact]
    public async Task Preview_MarksImportedQueuedAndNew()
    {
        var (svc, db, product, user) = Create();
        db.files.Add(FileRow(product.Id, user.Id, "https://cdn.example.test/kept.jpg?x=1"));
        db.pendingImageDownloads.Add(new PendingImageDownload
        {
            productId = product.Id,
            storeId = product.storeId,
            authorUserId = user.Id,
            imageUrl = "https://cdn.example.test/queued.jpg?y=1",
            status = ImageDownloadStatus.Pending,
            createdAt = DateTime.UtcNow
        });
        db.images.Add(new ImageReference { fileId = 1, productId = product.Id, index = 0 });
        await db.SaveChangesAsync();

        var preview = await svc.PreviewImportImagesAsync(product.Id,
        [
            "https://cdn.example.test/kept.jpg?x=2",
            "https://cdn.example.test/queued.jpg?y=2",
            "https://cdn.example.test/new.jpg"
        ]);

        Assert.Equal(1, preview.LocalGalleryCount);
        Assert.Equal("alreadyImported", preview.Images[0].Status);
        Assert.Equal("queued", preview.Images[1].Status);
        Assert.Equal("new", preview.Images[2].Status);
    }

    [Fact]
    public async Task Preview_MarksInFlightAlreadyImportedAsQueued()
    {
        var (svc, db, product, user) = Create();
        db.files.Add(FileRow(product.Id, user.Id, "https://cdn.example.test/kept.jpg?x=1"));
        db.pendingImageDownloads.Add(new PendingImageDownload
        {
            productId = product.Id,
            storeId = product.storeId,
            authorUserId = user.Id,
            imageUrl = "https://cdn.example.test/kept.jpg?x=2",
            status = ImageDownloadStatus.Pending,
            createdAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var preview = await svc.PreviewImportImagesAsync(product.Id,
        [
            "https://cdn.example.test/kept.jpg?x=3"
        ]);

        Assert.Equal("queued", preview.Images[0].Status);
    }

    [Fact]
    public async Task Queue_RejectsUrlNotOnAllowlist()
    {
        var (svc, _, product, user) = Create();

        var ex = await Assert.ThrowsAsync<ApiException>(() => svc.QueueImportImagesAsync(
            product.Id, product.storeId, user.Id,
            selectedUrls: ["https://evil.test/x.jpg"],
            allowlist: ["https://cdn.example.test/ok.jpg"],
            batchId: "img-1"));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("image_url_not_from_adapter", ex.Code);
    }

    [Fact]
    public async Task Queue_AppendsAlreadyImportedAndNewAfterExistingGallery()
    {
        var (svc, db, product, user) = Create();
        db.files.Add(FileRow(product.Id, user.Id, "https://cdn.example.test/old.jpg"));
        db.images.Add(new ImageReference { fileId = 1, productId = product.Id, index = 4 });
        await db.SaveChangesAsync();

        var result = await svc.QueueImportImagesAsync(
            product.Id, product.storeId, user.Id,
            selectedUrls:
            [
                "https://cdn.example.test/old.jpg?sig=stale",
                "https://cdn.example.test/new-a.jpg?sig=1",
                "https://cdn.example.test/new-b.jpg"
            ],
            allowlist:
            [
                "https://cdn.example.test/old.jpg?sig=fresh",
                "https://cdn.example.test/new-a.jpg?sig=2",
                "https://cdn.example.test/new-b.jpg"
            ],
            batchId: "img-1");

        Assert.Equal(3, result.QueuedCount);
        Assert.Equal(0, result.SkippedCount);

        var queued = await db.pendingImageDownloads.OrderBy(p => p.imageIndex).ToListAsync();
        Assert.Equal(3, queued.Count);
        Assert.Equal(5, queued[0].imageIndex);
        Assert.Equal(6, queued[1].imageIndex);
        Assert.Equal(7, queued[2].imageIndex);
        Assert.Equal("https://cdn.example.test/old.jpg?sig=fresh", queued[0].imageUrl);
        Assert.Equal("https://cdn.example.test/new-a.jpg?sig=2", queued[1].imageUrl);
        Assert.Equal("https://cdn.example.test/new-b.jpg", queued[2].imageUrl);
    }

    [Fact]
    public async Task Queue_SkipsUrlsAlreadyInFlight()
    {
        var (svc, db, product, user) = Create();
        db.pendingImageDownloads.Add(new PendingImageDownload
        {
            productId = product.Id,
            storeId = product.storeId,
            authorUserId = user.Id,
            imageUrl = "https://cdn.example.test/queued.jpg?y=1",
            imageIndex = 0,
            status = ImageDownloadStatus.Pending,
            createdAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await svc.QueueImportImagesAsync(
            product.Id, product.storeId, user.Id,
            selectedUrls: ["https://cdn.example.test/queued.jpg?y=2"],
            allowlist: ["https://cdn.example.test/queued.jpg?y=2"],
            batchId: "img-1");

        Assert.Equal(0, result.QueuedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(1, await db.pendingImageDownloads.CountAsync());
    }

    private static (
        ProductSyncService svc,
        DataContext db,
        Product product,
        User user) Create()
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore();
        var product = data.AddProduct(store.Id);
        db.SaveChanges();

        var quota = new QuotaService(TestDbFactory.CreateUnitOfWork(db), db);
        var svc = new ProductSyncService(
            db,
            adapterRegistry: null!,
            mappingService: null!,
            storageService: null!,
            NullLogger<ProductSyncService>.Instance,
            new NoopOperationLogger(),
            quota);
        return (svc, db, product, user);
    }

    private static Models.File FileRow(int productId, int userId, string sourceUrl) => new()
    {
        Id = 1,
        productId = productId,
        authorUserId = userId,
        filePath = "stores/1/products/1/images/a.jpg",
        contentType = "image/jpeg",
        length = 10,
        fileType = FileType.Image,
        uploadDateTime = DateTime.UtcNow,
        sourceUrl = sourceUrl
    };

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
