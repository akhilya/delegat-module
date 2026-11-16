using System.Text;
using System.Text.Json;
using Delegat;
using Delegat.Controllers;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Delegat.Services;
using Delegat.Services.Ai.Agent;
using Delegat.Services.FolderJobs;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class StoreTextFileTests
{
    private static StoreTextFileService NewService(DataContext db, IStorageService storage) =>
        new(db, storage, new QuotaService(TestDbFactory.CreateUnitOfWork(db), db));

    [Fact]
    public async Task FromText_ExactUtf8CsvBytes_DownloadableThroughCurrentEndpoint()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var folder = new StoreFolder { name = "exports", storeId = store.Id };
        db.storeFolders.Add(folder);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var service = NewService(db, storage);
        var csv = "name;price\nМыло «Невская»;12,50\nÅngström;7\n";

        var result = await service.CreateFromTextAsync(
            store.Id, user.Id, "products.csv", "text/csv", csv, folder.Id, default);

        var expected = Encoding.UTF8.GetBytes(csv);
        var key = Assert.Single(storage.StoredObjects);
        Assert.Equal(expected, key.Value);
        Assert.StartsWith($"{store.Id}/store/", key.Key);
        Assert.EndsWith("/products.csv", key.Key);
        Assert.Equal(expected.Length, result.Length);
        Assert.Equal("products.csv", result.FileName);
        Assert.Equal("text/csv", result.ContentType);
        Assert.Equal(folder.Id, result.FolderId);
        Assert.Equal($"/api/store-storage/files/{result.Id}/download", result.DownloadUrl);

        // The existing authenticated download route streams exactly those bytes.
        var controller = NewController(db, storage, user, store);
        var body = new MemoryStream();
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        controller.Response.Body = body;
        await controller.DownloadFile(result.Id, default);
        Assert.Equal(expected, body.ToArray());
    }

    [Fact]
    public async Task Download_FileOfAnotherStore_IsNotFound()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var other = data.AddStore();
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var created = await NewService(db, storage).CreateFromTextAsync(
            other.Id, user.Id, "foreign.csv", "text/csv", "x", null, default);

        // Selected store is "store"; the row belongs to "other".
        var controller = NewController(db, storage, user, store);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        controller.Response.Body = new MemoryStream();

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => controller.DownloadFile(created.Id, default));
        Assert.Equal("file_not_found", ex.Code);
    }

    [Fact]
    public async Task FromText_FolderOfAnotherStore_IsRejectedBeforeUpload()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var other = data.AddStore();
        var folder = new StoreFolder { name = "foreign", storeId = other.Id };
        db.storeFolders.Add(folder);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var ex = await Assert.ThrowsAsync<ApiException>(() => NewService(db, storage)
            .CreateFromTextAsync(store.Id, user.Id, "a.csv", "text/csv", "x", folder.Id, default));

        Assert.Equal("folder_not_found", ex.Code);
        Assert.Empty(storage.StoredObjects);
        Assert.Empty(db.storeFiles);
    }

    [Fact]
    public void FromText_RequiresFilesManageOnTheSelectedStore()
    {
        var permission = Assert.Single(typeof(StoreStorageController)
            .GetMethod(nameof(StoreStorageController.CreateFileFromText))!
            .GetCustomAttributes(typeof(RequiresPermissionAttribute), true)
            .Cast<RequiresPermissionAttribute>());
        Assert.Equal(PermissionPolicyProvider.StorePolicyName(Permissions.FilesManage), permission.Policy);
    }

    [Theory]
    [InlineData("text/csv")]
    [InlineData("application/json")]
    [InlineData("TEXT/PLAIN")]
    public async Task FromText_AllowedContentTypes_AreStoredNormalized(string contentType)
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var result = await NewService(db, storage).CreateFromTextAsync(
            store.Id, user.Id, "data.txt", contentType, "payload", null, default);

        Assert.Equal(contentType.ToLowerInvariant(), result.ContentType);
        Assert.Equal(result.ContentType, db.storeFiles.Single().contentType);
    }

    [Fact]
    public async Task FromText_NullContentType_DefaultsToPlainText()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var result = await NewService(db, new MemoryStorage()).CreateFromTextAsync(
            store.Id, user.Id, "note.txt", null, "hello", null, default);

        Assert.Equal("text/plain", result.ContentType);
    }

    [Fact]
    public async Task FromText_ControllerPost_OmittedOptionals_DefaultToPlainTextNoFolder()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        // The request record must tolerate a JSON body without the optional fields.
        var request = JsonSerializer.Deserialize<StoreStorageController.CreateFileFromTextRequest>(
            """{"fileName":"note.csv","text":"a,b"}""");
        Assert.NotNull(request);
        Assert.Null(request.contentType);
        Assert.Null(request.folderId);

        var storage = new MemoryStorage();
        var controller = NewController(db, storage, user, store);
        var action = await controller.CreateFileFromText(request, default);

        var ok = Assert.IsType<OkObjectResult>(action);
        var result = Assert.IsType<StoreTextFileService.Result>(ok.Value);
        Assert.Equal("text/plain", result.ContentType);
        Assert.Null(result.FolderId);
        Assert.Equal($"/api/store-storage/files/{result.Id}/download", result.DownloadUrl);
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("application/octet-stream")]
    [InlineData("text/html")]
    [InlineData("text/csv,evil")]
    public async Task FromText_DisallowedContentType_RejectedBeforeUpload(string contentType)
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var ex = await Assert.ThrowsAsync<ApiException>(() => NewService(db, storage)
            .CreateFromTextAsync(store.Id, user.Id, "a.bin", contentType, "x", null, default));

        Assert.Equal("content_type_not_allowed", ex.Code);
        Assert.Empty(storage.StoredObjects);
        Assert.Empty(db.storeFiles);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task FromText_BlankFileName_RejectedBeforeUpload(string? fileName)
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var ex = await Assert.ThrowsAsync<ApiException>(() => NewService(db, storage)
            .CreateFromTextAsync(store.Id, user.Id, fileName, null, "x", null, default));

        Assert.Equal("file_name_required", ex.Code);
        Assert.Empty(storage.StoredObjects);
    }

    [Fact]
    public async Task FromText_PathLikeFileName_IsSanitizedForTheStorageKey()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var result = await NewService(db, storage).CreateFromTextAsync(
            store.Id, user.Id, "../../evil.csv", "text/csv", "x", null, default);

        Assert.Equal("evil.csv", result.FileName);
        var key = Assert.Single(storage.StoredObjects).Key;
        Assert.EndsWith("/evil.csv", key);
        Assert.DoesNotContain("..", key);
    }

    [Fact]
    public async Task FromText_LimitsUtf8BytesNotChars()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();
        var storage = new MemoryStorage();
        var service = NewService(db, storage);

        var exact = await service.CreateFromTextAsync(
            store.Id, user.Id, "exact.txt", null,
            new string('a', StoreTextFileService.MaxTextBytes), null, default);
        Assert.Equal(StoreTextFileService.MaxTextBytes, exact.Length);

        // Same character count, three UTF-8 bytes per char: well over the cap.
        var ex = await Assert.ThrowsAsync<ApiException>(() => service.CreateFromTextAsync(
            store.Id, user.Id, "big.txt", null,
            new string('€', StoreTextFileService.MaxTextBytes), null, default));
        Assert.Equal("text_too_large", ex.Code);
        Assert.Single(storage.StoredObjects); // only the accepted upload
    }

    [Fact]
    public async Task FromText_EmptyText_CreatesAnEmptyFile()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var result = await NewService(db, storage).CreateFromTextAsync(
            store.Id, user.Id, "empty.csv", "text/csv", "", null, default);

        Assert.Equal(0, result.Length);
        Assert.Equal([], Assert.Single(storage.StoredObjects).Value);
        Assert.Equal(0, db.storeFiles.Single().length);
    }

    [Fact]
    public async Task FromText_NullText_IsRejected()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var ex = await Assert.ThrowsAsync<ApiException>(() => NewService(db, storage)
            .CreateFromTextAsync(store.Id, user.Id, "a.txt", null, null, null, default));

        Assert.Equal("text_required", ex.Code);
        Assert.Empty(storage.StoredObjects);
    }

    [Fact]
    public async Task FromText_QuotaRejection_UploadsNothing()
    {
        var previous = Environment.GetEnvironmentVariable("MAX_FILES");
        Environment.SetEnvironmentVariable("MAX_FILES", "1");
        try
        {
            await using var db = TestDbFactory.CreateDb();
            var data = new TestDataBuilder(db);
            var user = data.AddUser();
            var store = data.AddStore(members: user);
            var existing = new StoreFile();
            existing.Fill($"{store.Id}/store/old/report.csv", user.Id, store.Id, "text/csv", 10);
            db.storeFiles.Add(existing);
            await data.SaveAsync();

            var storage = new MemoryStorage();
            var ex = await Assert.ThrowsAsync<ApiException>(() => NewService(db, storage)
                .CreateFromTextAsync(store.Id, user.Id, "new.csv", "text/csv", "x", null, default));

            Assert.Equal("quota_files_reached", ex.Code);
            Assert.Empty(storage.StoredObjects);
            Assert.Single(db.storeFiles);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MAX_FILES", previous);
        }
    }

    [Fact]
    public async Task FromText_StorageQuotaRejection_UploadsNothing()
    {
        var previous = Environment.GetEnvironmentVariable("MAX_STORAGE_GB");
        // Fractional cap resolves to ~1073 bytes, so any realistic payload exceeds it.
        Environment.SetEnvironmentVariable("MAX_STORAGE_GB", "0.000001");
        try
        {
            await using var db = TestDbFactory.CreateDb();
            var data = new TestDataBuilder(db);
            var user = data.AddUser();
            var store = data.AddStore(members: user);
            await data.SaveAsync();

            var storage = new MemoryStorage();
            var ex = await Assert.ThrowsAsync<ApiException>(() => NewService(db, storage)
                .CreateFromTextAsync(store.Id, user.Id, "big.csv", "text/csv",
                    new string('x', 2048), null, default));

            Assert.Equal("quota_storage_reached", ex.Code);
            Assert.Empty(storage.StoredObjects);
            Assert.Empty(db.storeFiles);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MAX_STORAGE_GB", previous);
        }
    }

    [Fact]
    public async Task FromText_StorageFailure_PersistsNoRow()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var storage = new MemoryStorage { UploadSucceeds = false };
        var ex = await Assert.ThrowsAsync<ApiException>(() => NewService(db, storage)
            .CreateFromTextAsync(store.Id, user.Id, "a.csv", "text/csv", "x", null, default));

        Assert.Equal("file_upload_failed", ex.Code);
        Assert.Empty(db.storeFiles);
    }

    [Fact]
    public async Task FromText_DbSaveFailure_DeletesTheUploadedObject()
    {
        var interceptor = new FailNextStoreFileSave();
        var options = new DbContextOptionsBuilder<DataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(interceptor)
            .Options;
        await using var db = new DataContext(options);
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();
        interceptor.Armed = true;

        var storage = new MemoryStorage();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewService(db, storage).CreateFromTextAsync(
                store.Id, user.Id, "a.csv", "text/csv", "x", null, default));

        Assert.Equal(FailNextStoreFileSave.FailureMessage, ex.Message);
        var path = Assert.Single(storage.DeletedPaths);
        Assert.False(storage.StoredObjects.ContainsKey(path));
        var deleteToken = Assert.Single(storage.DeleteTokens);
        Assert.False(deleteToken.CanBeCanceled);
    }

    [Fact]
    public async Task FromText_ResponseIsPrivate_NoPathOrPublicToken()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var result = await NewService(db, new MemoryStorage()).CreateFromTextAsync(
            store.Id, user.Id, "a.csv", "text/csv", "secret body", null, default);

        var json = JsonSerializer.SerializeToElement(result,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var keys = json.EnumerateObject().Select(p => p.Name).OrderBy(k => k).ToArray();
        Assert.Equal(["contentType", "downloadUrl", "fileName", "folderId", "id", "length"], keys);
        var raw = json.GetRawText();
        Assert.DoesNotContain("secret body", raw);
        Assert.DoesNotContain("filePath", raw);
        Assert.DoesNotContain("token", raw, StringComparison.OrdinalIgnoreCase);

        var entity = db.storeFiles.Single();
        Assert.False(entity.isPublic);
        Assert.Null(entity.publicToken);
    }

    [Fact]
    public void FromTextPost_IsAWrite_RequiresConfirmedPlanBeforeDispatch()
    {
        Assert.True(AgentChatTurnGate.RequiresPlanConfirmation(
            "platform_request",
            """{"method":"POST","path":"/store-storage/files/from-text","body":{"fileName":"a.csv","text":"x"}}"""));
        Assert.False(AgentChatTurnGate.RequiresPlanConfirmation(
            "platform_request",
            """{"method":"GET","path":"/store-storage/files"}"""));
        Assert.True(PlatformRequestPolicy.IsAllowed("POST", "/store-storage/files/from-text"));
    }

    [Theory]
    [InlineData("сохрани сгенерированный csv файл в хранилище")]
    [InlineData("save generated csv text file to storage download")]
    public void SearchActions_GeneratedFileSave_FindsTheJsonRoute(string query)
    {
        var hits = new CabinetActionCatalog().Search(query);

        Assert.Contains(hits,
            h => h.Path.Equals("/store-storage/files/from-text", StringComparison.Ordinal));
        var hit = hits.Single(
            h => h.Path.Equals("/store-storage/files/from-text", StringComparison.Ordinal));
        Assert.True(hit.Write);
        Assert.Contains("262144", hit.Note);
        Assert.Contains("downloadUrl", hit.Note);
    }

    [Fact]
    public void CanonicalAudit_FromTextContract_IsTaughtAndMatchesTheDto()
    {
        var errors = AssistantInstructionAudit.Check(
        [
            ("policy", PlatformRequestPolicy.SurfaceDescription, true),
            ("catalog", CabinetActionCatalog.InstructionText(), true),
        ]);
        Assert.True(errors.Count == 0, string.Join("\n", errors));
        Assert.Contains("POST store-storage/files/from-text",
            PlatformRequestPolicy.SurfaceDescription);
        var fields = AssistantBodySchema.AcceptedFields("POST", "/api/store-storage/files/from-text");
        Assert.NotNull(fields);
        Assert.Contains("fileName", fields);
        Assert.Contains("text", fields);
    }

    private static StoreStorageController NewController(
        DataContext db, IStorageService storage, User user, Store store)
    {
        var current = new FakeCurrentUserService { UserId = user.Id, SelectedStoreId = store.Id };
        var uow = TestDbFactory.CreateUnitOfWork(db);
        var thumbs = new ThumbnailService(storage, NullLogger<ThumbnailService>.Instance);
        var jobs = new FolderJobService(
            db, storage, new NoopOperationLogger(), thumbs, NullLogger<FolderJobService>.Instance);
        return new StoreStorageController(
            current, storage, db, thumbs, new QuotaService(uow, db), null!, jobs,
            NewService(db, storage));
    }

    private sealed class MemoryStorage : IStorageService
    {
        public bool UploadSucceeds { get; set; } = true;
        public string Bucket => "test";
        public Dictionary<string, byte[]> StoredObjects { get; } = new(StringComparer.Ordinal);
        public List<string> DeletedPaths { get; } = [];
        public List<CancellationToken> DeleteTokens { get; } = [];

        public Task<bool> UploadAsync(IFormFile file, string path, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public async Task<bool> UploadAsync(
            Stream stream, string path, long contentLength, string contentType, CancellationToken ct = default)
        {
            if (!UploadSucceeds)
                return false;
            using var output = new MemoryStream();
            await stream.CopyToAsync(output, ct);
            StoredObjects[path] = output.ToArray();
            return true;
        }
        public Task<bool> UploadFromUriAsync(string uri, string path, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<FileUploadResult?> UploadFromUriWithMetadataAsync(
            string uri, string path, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<bool> CopyAsync(string sourcePath, string destinationPath, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<bool> DeleteAsync(string path, CancellationToken ct = default)
        {
            DeletedPaths.Add(path);
            DeleteTokens.Add(ct);
            StoredObjects.Remove(path);
            return Task.FromResult(true);
        }
        public Task<bool> ExistsAsync(string path, CancellationToken ct = default) =>
            Task.FromResult(StoredObjects.ContainsKey(path));
        public Task<Stream?> GetStreamAsync(string path, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(
                StoredObjects.TryGetValue(path, out var bytes) ? new MemoryStream(bytes) : null);
        public string GetFullPath(string path) => path;
        public Task StreamToAsync(string path, Stream outputStream, CancellationToken ct = default) =>
            outputStream.WriteAsync(StoredObjects[path], 0, StoredObjects[path].Length, ct);
        public Task<string> GetPresignedUrlAsync(string path, int expirySeconds = 3600) =>
            Task.FromResult(path);
        public Task<string> GetPublicPresignedUrlAsync(string path, int expirySeconds = 3600) =>
            Task.FromResult(path);
        public Task<List<string>> ListObjectsByPrefixAsync(string prefix, CancellationToken ct = default) =>
            Task.FromResult(new List<string>());
    }

    private sealed class FailNextStoreFileSave : SaveChangesInterceptor
    {
        public const string FailureMessage = "Injected store-file save failure";
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed
                && eventData.Context?.ChangeTracker.Entries<StoreFile>()
                    .Any(e => e.State == EntityState.Added) == true)
            {
                Armed = false;
                throw new InvalidOperationException(FailureMessage);
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class NoopOperationLogger : IOperationLogger
    {
        public Task StartAsync(string operationId, int storeId, int authorUserId, string type, string title,
            int? connectionId = null, string? connectionName = null,
            string? batchId = null, string? batchType = null, int? totalItems = null, int? authorApiKeyId = null) =>
            Task.CompletedTask;
        public Task LogAsync(string operationId, int storeId, string level, string message) =>
            Task.CompletedTask;
        public Task ProgressAsync(string operationId, int storeId, int total, int completed, int failed) =>
            Task.CompletedTask;
        public Task FinishAsync(string operationId, int storeId, string status, string? summary = null) =>
            Task.CompletedTask;
        public Task UpdateStatusAsync(string operationId, int storeId, string status) =>
            Task.CompletedTask;
    }
}
