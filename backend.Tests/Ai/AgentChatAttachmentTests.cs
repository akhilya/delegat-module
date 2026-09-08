using System.Text;
using System.Text.Json;
using Delegat;
using Delegat.Infrastructure.Storage;
using Delegat.Models.Ai;
using Delegat.Services;
using Delegat.Services.Ai.Agent;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class AgentChatAttachmentTests
{
    [Fact]
    public void UserMessage_IsGenericNotProductUpload()
    {
        var msg = AgentChatAttachmentContext.UserMessage(
        [
            new AiChatAttachment
            {
                Id = 9,
                relativePath = "photos/SKU-HELLO.jpg",
                fileName = "SKU-HELLO.jpg",
                length = 120,
                contentType = "image/jpeg",
            },
        ]);
        Assert.Contains("already stored", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=9", msg, StringComparison.Ordinal);
        Assert.DoesNotContain("client_upload", msg, StringComparison.Ordinal);
        Assert.DoesNotContain("Match those names to products", msg, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeRelativePath_DropsParentSegments()
    {
        Assert.Null(AgentFileUploadService.SanitizeRelativePath("../secret.txt", "secret.txt"));
        Assert.Equal("photos/SKU-HELLO.jpg", AgentFileUploadService.SanitizeRelativePath("photos\\SKU-HELLO.jpg", "SKU-HELLO.jpg"));
    }

    [Fact]
    public async Task StageThenAssign_CopiesOntoProductAndDeletesStaging()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore();
        var product = data.AddProduct(store.Id, "Hello soap", sku: "SKU-HELLO");
        await data.SaveAsync();

        var storage = new RecordingStorage();
        var uow = TestDbFactory.CreateUnitOfWork(db);
        var uploads = new AgentFileUploadService(db, uow, storage, new QuotaService(uow, db));

        var form = Form("SKU-HELLO.jpg", "jpeg-bytes");
        var staged = await uploads.StageChatAttachmentsAsync(
            store.Id, user.Id, "sess-1", [form], ["photos/SKU-HELLO.jpg"], default);

        Assert.Single(staged);
        Assert.Equal("photos/SKU-HELLO.jpg", staged[0].RelativePath);
        Assert.Single(storage.Uploaded);
        Assert.StartsWith(AgentFileUploadService.ChatAttachPrefix, storage.Uploaded[0]);

        var assigned = await uploads.AssignChatAttachmentsToProductAsync(
            store.Id, user.Id, product.Id, [staged[0].Id], null, default);

        Assert.Single(assigned);
        Assert.Equal(1, db.files.Count(f => f.productId == product.Id));
        Assert.Empty(db.aiChatAttachments);
        Assert.Single(storage.Copies);
        Assert.StartsWith(AgentFileUploadService.ChatAttachPrefix, storage.Copies[0].Src);
        Assert.Contains($"/{product.Id}/", storage.Copies[0].Dest);
        Assert.Single(storage.Deleted);
        Assert.Equal(storage.Uploaded[0], storage.Deleted[0]);
    }

    [Fact]
    public async Task PlatformRequest_AssignsStagedAttachments()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore();
        var product = data.AddProduct(store.Id, "Hello soap", sku: "SKU-HELLO");
        await data.SaveAsync();

        var storage = new RecordingStorage();
        var uow = TestDbFactory.CreateUnitOfWork(db);
        var uploads = new AgentFileUploadService(db, uow, storage, new QuotaService(uow, db));
        var form = Form("SKU-HELLO.jpg", "jpeg-bytes");
        var staged = await uploads.StageChatAttachmentsAsync(
            store.Id, user.Id, "sess-2", [form], ["SKU-HELLO.jpg"], default);

        var host = CreateHost(db, storage);
        var body =
            "{\"method\":\"POST\",\"path\":\"/products/" + product.Id +
            "/files\",\"body\":{\"attachmentIds\":[" + staged[0].Id + "]}}";
        var (result, _) = await host.DispatchAsync("platform_request", body, store.Id, user.Id, default);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.Equal(1, json.RootElement.GetProperty("count").GetInt32());
        Assert.Equal(1, db.files.Count(f => f.productId == product.Id));
        Assert.Empty(db.aiChatAttachments);
    }

    [Fact]
    public async Task Assign_ExpiredAttachment_Fails()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore();
        var product = data.AddProduct(store.Id, "Hello soap", sku: "SKU-HELLO");
        await data.SaveAsync();

        var storage = new RecordingStorage();
        var uow = TestDbFactory.CreateUnitOfWork(db);
        var uploads = new AgentFileUploadService(db, uow, storage, new QuotaService(uow, db));
        var staged = await uploads.StageChatAttachmentsAsync(
            store.Id, user.Id, "sess-old", [Form("SKU-HELLO.jpg", "jpeg-bytes")], ["SKU-HELLO.jpg"], default);

        var row = db.aiChatAttachments.Single();
        row.createdAt = DateTime.UtcNow.AddHours(-25);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            uploads.AssignChatAttachmentsToProductAsync(
                store.Id, user.Id, product.Id, [staged[0].Id], null, default));
        Assert.Equal("attachment_expired", ex.Message);
        Assert.Empty(db.files.Where(f => f.productId == product.Id));
        Assert.Single(db.aiChatAttachments);
    }

    [Fact]
    public async Task PurgeExpired_DeletesRowsAndStorageKeys()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore();
        await data.SaveAsync();

        var storage = new RecordingStorage();
        var uow = TestDbFactory.CreateUnitOfWork(db);
        var uploads = new AgentFileUploadService(db, uow, storage, new QuotaService(uow, db));
        await uploads.StageChatAttachmentsAsync(
            store.Id, user.Id, "sess-purge", [Form("old.jpg", "bytes")], ["old.jpg"], default);
        var live = await uploads.StageChatAttachmentsAsync(
            store.Id, user.Id, "sess-live", [Form("new.jpg", "bytes")], ["new.jpg"], default);

        var expired = db.aiChatAttachments.First(a => a.fileName == "old.jpg");
        expired.createdAt = DateTime.UtcNow.AddHours(-25);
        await db.SaveChangesAsync();

        var n = await uploads.PurgeExpiredAsync(default);
        Assert.Equal(1, n);
        Assert.Single(db.aiChatAttachments);
        Assert.Equal(live[0].Id, db.aiChatAttachments.Single().Id);
        Assert.Contains(storage.Uploaded[0], storage.Deleted);
        Assert.DoesNotContain(storage.Uploaded[1], storage.Deleted);
    }

    [Fact]
    public async Task GetLiveOwned_IgnoresExpired()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore();
        await data.SaveAsync();

        var storage = new RecordingStorage();
        var uow = TestDbFactory.CreateUnitOfWork(db);
        var uploads = new AgentFileUploadService(db, uow, storage, new QuotaService(uow, db));
        var staged = await uploads.StageChatAttachmentsAsync(
            store.Id, user.Id, "sess-dl", [Form("a.jpg", "bytes")], ["a.jpg"], default);

        Assert.NotNull(await uploads.GetLiveOwnedAsync(store.Id, user.Id, staged[0].Id, default));

        var row = db.aiChatAttachments.Single();
        row.createdAt = DateTime.UtcNow.AddHours(-25);
        await db.SaveChangesAsync();

        Assert.Null(await uploads.GetLiveOwnedAsync(store.Id, user.Id, staged[0].Id, default));
    }

    private static AgentToolHost CreateHost(DataContext db, IStorageService storage)
    {
        var catalog = new CatalogSearchService(db);
        var sql = new SqlReadonlyService(db);
        var knowledge = new KnowledgeIndexService(db, storage, NullLogger<KnowledgeIndexService>.Instance);
        var uow = TestDbFactory.CreateUnitOfWork(db);
        var uploads = new AgentFileUploadService(db, uow, storage, new QuotaService(uow, db));
        var platform = new AgentPlatformGateway(uploads, new UnusedInvoker());
        var python = new PythonSandboxClient(
            db,
            new ThrowingHttpFactory(),
            NullLogger<PythonSandboxClient>.Instance,
            new HttpContextAccessor(),
            new FakeJwt());
        return new AgentToolHost(
            catalog, sql, knowledge, platform, python,
            new GrantAllPermissions(),
            db,
            NullLogger<AgentToolHost>.Instance);
    }

    private sealed class UnusedInvoker : ICabinetRequestInvoker
    {
        public Task<(object result, string summary)> InvokeAsync(
            int storeId,
            int userId,
            string method,
            string apiPath,
            IReadOnlyDictionary<string, string> query,
            JsonElement? body,
            CancellationToken ct) =>
            throw new InvalidOperationException("cabinet invoker should not run for staged attach");
    }

    private static FormFile Form(string name, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "files", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg",
        };
    }

    private sealed class RecordingStorage : IStorageService
    {
        public List<string> Uploaded { get; } = [];
        public List<(string Src, string Dest)> Copies { get; } = [];
        public List<string> Deleted { get; } = [];

        public string Bucket => "test";
        public Task<bool> UploadAsync(IFormFile file, string path, CancellationToken ct = default)
        {
            Uploaded.Add(path);
            return Task.FromResult(true);
        }
        public Task<bool> UploadAsync(Stream stream, string path, long contentLength, string contentType, CancellationToken ct = default)
        {
            Uploaded.Add(path);
            return Task.FromResult(true);
        }
        public Task<bool> UploadFromUriAsync(string uri, string path, CancellationToken ct = default) => Task.FromResult(true);
        public Task<FileUploadResult?> UploadFromUriWithMetadataAsync(string uri, string path, CancellationToken ct = default) =>
            Task.FromResult<FileUploadResult?>(new FileUploadResult("text/plain", 1));
        public Task<bool> CopyAsync(string sourcePath, string destinationPath, CancellationToken ct = default)
        {
            Copies.Add((sourcePath, destinationPath));
            return Task.FromResult(true);
        }
        public Task<bool> DeleteAsync(string path, CancellationToken ct = default)
        {
            Deleted.Add(path);
            return Task.FromResult(true);
        }
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
