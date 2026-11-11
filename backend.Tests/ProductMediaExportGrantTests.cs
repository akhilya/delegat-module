using System.Text;
using Delegat.Controllers;
using Delegat.Enums;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Security;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Delegat.Services.Marketplace;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Delegat.Tests;

public sealed class ProductMediaExportGrantTests
{
    [Fact]
    public async Task ExportGrant_IsStableConnectionScopedRangeReadableAndRevocable()
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: [user]);
        var product = data.AddProduct(store.Id);
        var connection = data.AddConnection(store.Id, adapterKey: "yandex");
        var otherConnection = data.AddConnection(store.Id, adapterKey: "yandex");
        var file = new Models.File
        {
            Id = 31,
            productId = product.Id,
            authorUserId = user.Id,
            filePath = "stores/1/products/1/media/31.mp4",
            contentType = "video/mp4",
            length = 6,
            fileType = FileType.Other,
            uploadDateTime = DateTime.UtcNow
        };
        db.files.Add(file);
        await data.SaveAsync();

        var storage = new MemoryStorage(Encoding.UTF8.GetBytes("abcdef"));
        var grants = CreateService(db, storage);
        var first = Assert.Single(await grants.GetOrCreateUrlsAsync(connection.Id, product.Id, [file.Id]));
        var reused = Assert.Single(await grants.GetOrCreateUrlsAsync(connection.Id, product.Id, [file.Id]));
        var other = Assert.Single(await grants.GetOrCreateUrlsAsync(otherConnection.Id, product.Id, [file.Id]));

        Assert.Equal(first, reused);
        Assert.NotEqual(first, other);
        Assert.False(file.isPublic);
        var record = await db.productMediaExportGrants.SingleAsync(g => g.connectionId == connection.Id);
        Assert.Equal(64, record.token.Length);

        var controller = new PublicMediaExportController(db, storage);
        var response = NewHttpContext("GET", "bytes=1-3");
        controller.ControllerContext = new ControllerContext { HttpContext = response };
        await controller.Get(record.token, CancellationToken.None);
        Assert.Equal(StatusCodes.Status206PartialContent, response.Response.StatusCode);
        Assert.Equal("bcd", Encoding.UTF8.GetString(((MemoryStream)response.Response.Body).ToArray()));
        Assert.Equal("no-store, no-cache, must-revalidate", response.Response.Headers.CacheControl);

        Assert.Equal(1, await grants.RevokeForConnectionAsync(connection.Id));
        var revokedRequest = NewHttpContext("HEAD", null);
        controller.ControllerContext = new ControllerContext { HttpContext = revokedRequest };
        var notFound = await Assert.ThrowsAsync<ApiException>(
            () => controller.Get(record.token, CancellationToken.None));
        Assert.Equal("file_not_found", notFound.Code);

        var reissued = Assert.Single(await grants.GetOrCreateUrlsAsync(connection.Id, product.Id, [file.Id]));
        Assert.NotEqual(first, reissued);
        Assert.False((await db.productMediaExportGrants.SingleAsync(g => g.connectionId == connection.Id)).revokedAt.HasValue);
    }

    [Fact]
    public async Task ExportGrant_RequiresPublicHttpsOriginBeforePersistingAnything()
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: [user]);
        var product = data.AddProduct(store.Id);
        var connection = data.AddConnection(store.Id, adapterKey: "yandex");
        var file = new Models.File
        {
            Id = 32,
            productId = product.Id,
            authorUserId = user.Id,
            filePath = "video.mp4",
            contentType = "video/mp4",
            length = 6,
            fileType = FileType.Other,
            uploadDateTime = DateTime.UtcNow
        };
        db.files.Add(file);
        await data.SaveAsync();

        var service = CreateService(db, new MemoryStorage("abcdef"u8.ToArray()), publicBaseUrl: null);
        var error = await Assert.ThrowsAsync<ApiException>(
            () => service.GetOrCreateUrlsAsync(connection.Id, product.Id, [file.Id]));

        Assert.Equal("media_export_public_url_required", error.Code);
        Assert.Empty(await db.productMediaExportGrants.ToListAsync());
    }

    [Fact]
    public async Task ExportGrant_RejectsVideoCountAboveYandexLimitWithoutTruncating()
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: [user]);
        var product = data.AddProduct(store.Id);
        var connection = data.AddConnection(store.Id, adapterKey: "yandex");
        await data.SaveAsync();
        var storage = new MemoryStorage([]);
        var service = CreateService(db, storage);

        var error = await Assert.ThrowsAsync<ApiException>(() => service.GetOrCreateUrlsAsync(
            connectionId: connection.Id,
            productId: product.Id,
            requestedFileIds: Enumerable.Range(1, ProductMediaExportGrantService.YandexMaxVideos + 1).ToArray()));

        Assert.Equal("media_export_video_limit_exceeded", error.Code);
        Assert.Empty(await db.productMediaExportGrants.ToListAsync());
    }

    [Fact]
    public async Task ExportGrant_WildberriesAcceptsOnlySupportedVideoAndEnforcesSizeAndCount()
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: [user]);
        var product = data.AddProduct(store.Id);
        var connection = data.AddConnection(store.Id, adapterKey: "wildberries");
        var accepted = new Models.File
        {
            Id = 51,
            productId = product.Id,
            authorUserId = user.Id,
            filePath = "accepted.mp4",
            contentType = "video/mp4",
            length = 50L * 1024 * 1024,
            fileType = FileType.Other,
            uploadDateTime = DateTime.UtcNow
        };
        var unsupported = new Models.File
        {
            Id = 52,
            productId = product.Id,
            authorUserId = user.Id,
            filePath = "unsupported.webm",
            contentType = "video/webm",
            length = 10,
            fileType = FileType.Other,
            uploadDateTime = DateTime.UtcNow
        };
        var oversized = new Models.File
        {
            Id = 53,
            productId = product.Id,
            authorUserId = user.Id,
            filePath = "oversized.mp4",
            contentType = "video/mp4",
            length = ProductMediaExportGrantService.WildberriesMaxVideoBytes + 1,
            fileType = FileType.Other,
            uploadDateTime = DateTime.UtcNow
        };
        db.files.AddRange(accepted, unsupported, oversized);
        await data.SaveAsync();
        var service = CreateService(db, new MemoryStorage([]));

        var url = Assert.Single(await service.GetOrCreateUrlsAsync(connection.Id, product.Id, [accepted.Id]));
        Assert.StartsWith("https://media.example.test/api/pub/media-export/", url);
        Assert.EndsWith("/video.mp4", url);

        var typeError = await Assert.ThrowsAsync<ApiException>(
            () => service.GetOrCreateUrlsAsync(connection.Id, product.Id, [unsupported.Id]));
        Assert.Equal("media_export_video_type_not_supported", typeError.Code);

        var sizeError = await Assert.ThrowsAsync<ApiException>(
            () => service.GetOrCreateUrlsAsync(connection.Id, product.Id, [oversized.Id]));
        Assert.Equal("media_export_video_size_exceeded", sizeError.Code);

        var countError = await Assert.ThrowsAsync<ApiException>(
            () => service.GetOrCreateUrlsAsync(connection.Id, product.Id, [accepted.Id, unsupported.Id]));
        Assert.Equal("media_export_video_limit_exceeded", countError.Code);
        Assert.Single(await db.productMediaExportGrants.ToListAsync());
    }

    [Theory]
    [InlineData("https://localhost")]
    [InlineData("https://127.0.0.1")]
    [InlineData("https://192.168.1.20")]
    public async Task PublicOriginValidator_RejectsLocalAndPrivateOrigins(string origin)
    {
        var validator = new PublicMediaExportBaseUrlValidator();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => validator.ValidateAsync(new Uri(origin)));
    }

    [Theory]
    [InlineData("https://media.example.test/api/pub/media-export/0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", "https://media.example.test/api/pub/media-export/[redacted]")]
    [InlineData("/api/pub/media-export/0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF", "/api/pub/media-export/[redacted]")]
    public void GrantSecretRedaction_HidesTokenFromLogText(string source, string expected)
        => Assert.Equal(expected, MediaExportGrantSecrets.Redact(source));

    private static ProductMediaExportGrantService CreateService(
        DataContext db,
        IStorageService storage,
        string? publicBaseUrl = "https://media.example.test")
    {
        var values = new Dictionary<string, string?>();
        if (publicBaseUrl != null)
            values["PUBLIC_BASE_URL"] = publicBaseUrl;
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ProductMediaExportGrantService(db, config, new PublicOriginValidator(), storage);
    }

    private static DefaultHttpContext NewHttpContext(string method, string? range)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        if (range != null)
            context.Request.Headers.Range = range;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private sealed class PublicOriginValidator : IPublicMediaExportBaseUrlValidator
    {
        public Task ValidateAsync(Uri baseUri, CancellationToken ct = default)
        {
            Assert.Equal("https", baseUri.Scheme);
            return Task.CompletedTask;
        }
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
