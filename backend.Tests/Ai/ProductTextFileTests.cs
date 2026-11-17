using System.Text;
using System.Text.Json;
using Delegat;
using Delegat.Controllers;
using Delegat.Enums;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Delegat.Services;
using Delegat.Services.Ai.Agent;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ProductFile = Delegat.Models.File;

namespace Delegat.Tests;

[Collection("EnvVars")]
public class ProductTextFileTests
{
    private static ProductTextFileService NewService(DataContext db, IStorageService storage) =>
        new(db, storage, new QuotaService(TestDbFactory.CreateUnitOfWork(db), db));

    [Fact]
    public async Task FromText_ExactUtf8CsvBytes_DownloadableThroughProductEndpoint()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var product = data.AddProduct(store.Id);
        var folder = new ProductFolder { name = "docs", productId = product.Id };
        db.productFolders.Add(folder);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var service = NewService(db, storage);
        var csv = "name;price\nМыло «Невская»;12,50\nÅngström;7\n";

        var result = await service.CreateFromTextAsync(
            store.Id, user.Id, product.Id, "prices.csv", "text/csv", csv, folder.Id, default);

        var expected = Encoding.UTF8.GetBytes(csv);
        var key = Assert.Single(storage.StoredObjects);
        Assert.Equal(expected, key.Value);
        Assert.StartsWith($"{store.Id}/{product.Id}/", key.Key);
        Assert.EndsWith("/prices.csv", key.Key);
        Assert.Equal(product.Id, result.ProductId);
        Assert.Equal(expected.Length, result.Length);
        Assert.Equal("prices.csv", result.FileName);
        Assert.Equal("text/csv", result.ContentType);
        Assert.Equal(folder.Id, result.FolderId);
        Assert.Equal($"/api/files/{result.Id}/download", result.DownloadUrl);

        Assert.Single(db.files);
        Assert.Empty(db.storeFiles);
        Assert.Empty(db.productAttributeFiles);
        Assert.Empty(db.storeProductAttributeFiles);
        var entity = db.files.Single();
        Assert.Equal(FileType.Other, entity.fileType);
        Assert.False(entity.isPublic);
        Assert.Null(entity.publicToken);
        Assert.Null(entity.sourceUrl);

        // The existing authenticated product download route streams exactly those bytes.
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
    public async Task FromText_SameFileNameTwice_CreatesDistinctKeysAndRows()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var product = data.AddProduct(store.Id);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var service = NewService(db, storage);
        await service.CreateFromTextAsync(store.Id, user.Id, product.Id, "a.csv", "text/csv", "x", null, default);
        var second = await service.CreateFromTextAsync(
            store.Id, user.Id, product.Id, "a.csv", "text/csv", "x", null, default);

        Assert.Equal(2, storage.StoredObjects.Count);
        Assert.Equal(2, storage.StoredObjects.Keys.Distinct().Count());
        Assert.Equal(2, db.files.Count());
        Assert.NotEqual(db.files.First().Id, second.Id);
    }

    [Fact]
    public async Task FromText_PathLikeFileName_IsSanitizedForTheStorageKey()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var product = data.AddProduct(store.Id);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var result = await NewService(db, storage).CreateFromTextAsync(
            store.Id, user.Id, product.Id, "../../evil.csv", "text/csv", "x", null, default);

        Assert.Equal("evil.csv", result.FileName);
        var key = Assert.Single(storage.StoredObjects).Key;
        Assert.EndsWith("/evil.csv", key);
        Assert.DoesNotContain("..", key);
    }

    [Fact]
    public async Task FromText_MissingProduct_IsRejectedBeforeUpload()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var ex = await Assert.ThrowsAsync<ApiException>(() => NewService(db, storage)
            .CreateFromTextAsync(store.Id, user.Id, productId: 999, "a.csv", "text/csv", "x", null, default));

        Assert.Equal("product_not_found", ex.Code);
        Assert.Empty(storage.StoredObjects);
        Assert.Empty(db.files);
    }

    [Fact]
    public async Task FromText_DeletedProduct_IsRejectedBeforeUpload()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var product = data.AddProduct(store.Id, deletedAt: DateTime.UtcNow);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var ex = await Assert.ThrowsAsync<ApiException>(() => NewService(db, storage)
            .CreateFromTextAsync(store.Id, user.Id, product.Id, "a.csv", "text/csv", "x", null, default));

        Assert.Equal("product_not_found", ex.Code);
        Assert.Empty(storage.StoredObjects);
        Assert.Empty(db.files);
    }

    [Fact]
    public async Task FromText_ForeignStoreProduct_IsRejectedBeforeUpload()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var other = data.AddStore();
        var foreign = data.AddProduct(other.Id);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var ex = await Assert.ThrowsAsync<ApiException>(() => NewService(db, storage)
            .CreateFromTextAsync(store.Id, user.Id, foreign.Id, "a.csv", "text/csv", "x", null, default));

        Assert.Equal("product_not_found", ex.Code);
        Assert.Empty(storage.StoredObjects);
        Assert.Empty(db.files);
    }

    [Fact]
    public async Task FromText_FolderOfAnotherProduct_IsRejectedBeforeUpload()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var product = data.AddProduct(store.Id);
        var otherProduct = data.AddProduct(store.Id);
        var folder = new ProductFolder { name = "other", productId = otherProduct.Id };
        db.productFolders.Add(folder);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var ex = await Assert.ThrowsAsync<ApiException>(() => NewService(db, storage)
            .CreateFromTextAsync(store.Id, user.Id, product.Id, "a.csv", "text/csv", "x", folder.Id, default));

        Assert.Equal("folder_not_found", ex.Code);
        Assert.Empty(storage.StoredObjects);
        Assert.Empty(db.files);
    }

    [Fact]
    public async Task FromText_FolderOnForeignStoreProduct_IsRejectedBeforeUpload()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var other = data.AddStore();
        var product = data.AddProduct(store.Id);
        var foreignProduct = data.AddProduct(other.Id);
        var folder = new ProductFolder { name = "foreign", productId = foreignProduct.Id };
        db.productFolders.Add(folder);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var ex = await Assert.ThrowsAsync<ApiException>(() => NewService(db, storage)
            .CreateFromTextAsync(store.Id, user.Id, product.Id, "a.csv", "text/csv", "x", folder.Id, default));

        Assert.Equal("folder_not_found", ex.Code);
        Assert.Empty(storage.StoredObjects);
        Assert.Empty(db.files);
    }

    [Fact]
    public async Task FromText_StoreStorageFolderId_IsNotAProductFolder()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var product = data.AddProduct(store.Id);
        // A store-storage folder is a different table: its id must not pass product-folder validation.
        var storeFolder = new StoreFolder { name = "store", storeId = store.Id };
        db.storeFolders.Add(storeFolder);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var ex = await Assert.ThrowsAsync<ApiException>(() => NewService(db, storage)
            .CreateFromTextAsync(store.Id, user.Id, product.Id, "a.csv", "text/csv", "x", storeFolder.Id, default));

        Assert.Equal("folder_not_found", ex.Code);
        Assert.Empty(storage.StoredObjects);
        Assert.Empty(db.files);
    }

    [Theory]
    [InlineData("text/csv")]
    [InlineData("application/json")]
    [InlineData("Text/CSV; charset=utf-8")]
    public async Task FromText_AllowedContentTypes_AreStoredNormalized(string contentType)
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var product = data.AddProduct(store.Id);
        await data.SaveAsync();

        var result = await NewService(db, new MemoryStorage()).CreateFromTextAsync(
            store.Id, user.Id, product.Id, "data.txt", contentType, "payload", null, default);

        Assert.Equal(contentType.Split(';', 2)[0].Trim().ToLowerInvariant(), result.ContentType);
        Assert.Equal(result.ContentType, db.files.Single().contentType);
    }

    [Fact]
    public async Task FromText_NullContentType_DefaultsToPlainText()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var product = data.AddProduct(store.Id);
        await data.SaveAsync();

        var result = await NewService(db, new MemoryStorage()).CreateFromTextAsync(
            store.Id, user.Id, product.Id, "note.txt", null, "hello", null, default);

        Assert.Equal("text/plain", result.ContentType);
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("application/octet-stream")]
    [InlineData("text/html")]
    public async Task FromText_DisallowedContentType_RejectedBeforeUpload(string contentType)
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var product = data.AddProduct(store.Id);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var ex = await Assert.ThrowsAsync<ApiException>(() => NewService(db, storage)
            .CreateFromTextAsync(store.Id, user.Id, product.Id, "a.bin", contentType, "x", null, default));

        Assert.Equal("content_type_not_allowed", ex.Code);
        Assert.Empty(storage.StoredObjects);
        Assert.Empty(db.files);
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
        var product = data.AddProduct(store.Id);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var ex = await Assert.ThrowsAsync<ApiException>(() => NewService(db, storage)
            .CreateFromTextAsync(store.Id, user.Id, product.Id, fileName, null, "x", null, default));

        Assert.Equal("file_name_required", ex.Code);
        Assert.Empty(storage.StoredObjects);
    }

    [Fact]
    public async Task FromText_LimitsUtf8BytesNotChars()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var product = data.AddProduct(store.Id);
        await data.SaveAsync();
        var storage = new MemoryStorage();
        var service = NewService(db, storage);

        var exact = await service.CreateFromTextAsync(
            store.Id, user.Id, product.Id, "exact.txt", null,
            new string('a', ProductTextFileService.MaxTextBytes), null, default);
        Assert.Equal(ProductTextFileService.MaxTextBytes, exact.Length);

        // Same character count, three UTF-8 bytes per char: well over the cap.
        var ex = await Assert.ThrowsAsync<ApiException>(() => service.CreateFromTextAsync(
            store.Id, user.Id, product.Id, "big.txt", null,
            new string('€', ProductTextFileService.MaxTextBytes), null, default));
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
        var product = data.AddProduct(store.Id);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var result = await NewService(db, storage).CreateFromTextAsync(
            store.Id, user.Id, product.Id, "empty.csv", "text/csv", "", null, default);

        Assert.Equal(0, result.Length);
        Assert.Equal([], Assert.Single(storage.StoredObjects).Value);
        Assert.Equal(0, db.files.Single().length);
    }

    [Fact]
    public async Task FromText_NullText_IsRejected()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var product = data.AddProduct(store.Id);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var ex = await Assert.ThrowsAsync<ApiException>(() => NewService(db, storage)
            .CreateFromTextAsync(store.Id, user.Id, product.Id, "a.txt", null, null, null, default));

        Assert.Equal("text_required", ex.Code);
        Assert.Empty(storage.StoredObjects);
    }

    [Fact]
    public async Task FromText_FileQuotaRejection_UploadsNothing()
    {
        var previous = Environment.GetEnvironmentVariable("MAX_FILES");
        Environment.SetEnvironmentVariable("MAX_FILES", "1");
        try
        {
            await using var db = TestDbFactory.CreateDb();
            var data = new TestDataBuilder(db);
            var user = data.AddUser();
            var store = data.AddStore(members: user);
            var product = data.AddProduct(store.Id);
            // One existing store-storage file already consumes the shared file quota.
            var existing = new StoreFile { displayName = "report.csv" };
            existing.Fill($"{store.Id}/store/old/report.csv", user.Id, store.Id, "text/csv", 10);
            db.storeFiles.Add(existing);
            await data.SaveAsync();

            var storage = new MemoryStorage();
            var ex = await Assert.ThrowsAsync<ApiException>(() => NewService(db, storage)
                .CreateFromTextAsync(store.Id, user.Id, product.Id, "new.csv", "text/csv", "x", null, default));

            Assert.Equal("quota_files_reached", ex.Code);
            Assert.Empty(storage.StoredObjects);
            Assert.Empty(db.files);
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
            var product = data.AddProduct(store.Id);
            await data.SaveAsync();

            var storage = new MemoryStorage();
            var ex = await Assert.ThrowsAsync<ApiException>(() => NewService(db, storage)
                .CreateFromTextAsync(store.Id, user.Id, product.Id, "big.csv", "text/csv",
                    new string('x', 2048), null, default));

            Assert.Equal("quota_storage_reached", ex.Code);
            Assert.Empty(storage.StoredObjects);
            Assert.Empty(db.files);
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
        var product = data.AddProduct(store.Id);
        await data.SaveAsync();

        var storage = new MemoryStorage { UploadSucceeds = false };
        var ex = await Assert.ThrowsAsync<ApiException>(() => NewService(db, storage)
            .CreateFromTextAsync(store.Id, user.Id, product.Id, "a.csv", "text/csv", "x", null, default));

        Assert.Equal("file_upload_failed", ex.Code);
        Assert.Empty(db.files);
    }

    [Fact]
    public async Task FromText_DbSaveFailure_DeletesTheUploadedObject()
    {
        var interceptor = new FailNextFileSave();
        var options = new DbContextOptionsBuilder<DataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(interceptor)
            .Options;
        await using var db = new DataContext(options);
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var product = data.AddProduct(store.Id);
        await data.SaveAsync();
        interceptor.Armed = true;

        var storage = new MemoryStorage();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewService(db, storage).CreateFromTextAsync(
                store.Id, user.Id, product.Id, "a.csv", "text/csv", "x", null, default));

        Assert.Equal(FailNextFileSave.FailureMessage, ex.Message);
        var path = Assert.Single(storage.DeletedPaths);
        Assert.False(storage.StoredObjects.ContainsKey(path));
        var deleteToken = Assert.Single(storage.DeleteTokens);
        Assert.False(deleteToken.CanBeCanceled);
    }

    [Fact]
    public async Task FromText_DbSaveFailure_CleanupFailurePreservesOriginalError()
    {
        var interceptor = new FailNextFileSave();
        var options = new DbContextOptionsBuilder<DataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(interceptor)
            .Options;
        await using var db = new DataContext(options);
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var product = data.AddProduct(store.Id);
        await data.SaveAsync();
        interceptor.Armed = true;

        var storage = new MemoryStorage { DeleteThrows = true };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewService(db, storage).CreateFromTextAsync(
                store.Id, user.Id, product.Id, "a.csv", "text/csv", "x", null, default));

        // The caller sees the persistence failure, not the cleanup failure.
        Assert.Equal(FailNextFileSave.FailureMessage, ex.Message);
        Assert.Single(storage.DeletedPaths);
    }

    [Fact]
    public void FromTextPost_RequiresFilesManageOnTheSelectedStore()
    {
        var permission = Assert.Single(typeof(FileController)
            .GetMethod(nameof(FileController.CreateProductFileFromText))!
            .GetCustomAttributes(typeof(RequiresPermissionAttribute), true)
            .Cast<RequiresPermissionAttribute>());
        Assert.Equal(PermissionPolicyProvider.StorePolicyName(Permissions.FilesManage), permission.Policy);

        var sizeLimit = Assert.Single(typeof(FileController)
            .GetMethod(nameof(FileController.CreateProductFileFromText))!
            .GetCustomAttributes(typeof(RequestSizeLimitAttribute), true)
            .Cast<RequestSizeLimitAttribute>());
        var metadata = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata>(sizeLimit);
        Assert.Equal(ProductTextFileService.MaxRequestBytes, metadata.MaxRequestBodySize);
    }

    [Fact]
    public async Task FromText_ControllerPost_OmittedOptionals_DefaultToPlainTextNoFolder()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var product = data.AddProduct(store.Id);
        await data.SaveAsync();

        // The request record must tolerate a JSON body without the optional fields.
        var request = JsonSerializer.Deserialize<FileController.CreateFileFromTextRequest>(
            """{"fileName":"note.csv","text":"a,b"}""");
        Assert.NotNull(request);
        Assert.Null(request.contentType);
        Assert.Null(request.folderId);

        var storage = new MemoryStorage();
        var controller = NewController(db, storage, user, store);
        var action = await controller.CreateProductFileFromText(product.Id, request, default);

        var ok = Assert.IsType<OkObjectResult>(action);
        var result = Assert.IsType<ProductTextFileService.Result>(ok.Value);
        Assert.Equal(product.Id, result.ProductId);
        Assert.Equal("text/plain", result.ContentType);
        Assert.Null(result.FolderId);
        Assert.Equal($"/api/files/{result.Id}/download", result.DownloadUrl);
    }

    [Fact]
    public async Task FromText_ControllerPost_NoSelectedStore_IsRejected()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var product = data.AddProduct(store.Id);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var current = new FakeCurrentUserService { UserId = user.Id, SelectedStoreId = null };
        var controller = NewController(db, storage, current);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            controller.CreateProductFileFromText(product.Id,
                new FileController.CreateFileFromTextRequest("a.csv", null, "x", null), default));
        Assert.Equal("store_not_selected", ex.Code);
        Assert.Empty(storage.StoredObjects);
    }

    [Fact]
    public async Task FromText_ControllerPost_NoUser_IsUnauthorized()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var product = data.AddProduct(store.Id);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var current = new FakeCurrentUserService { UserId = null, SelectedStoreId = store.Id };
        var controller = NewController(db, storage, current);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            controller.CreateProductFileFromText(product.Id,
                new FileController.CreateFileFromTextRequest("a.csv", null, "x", null), default));
        Assert.Equal(401, ex.StatusCode);
        Assert.Empty(storage.StoredObjects);
    }

    [Fact]
    public void FromTextPost_IsAWrite_RequiresConfirmedPlanBeforeDispatch()
    {
        Assert.True(AgentChatTurnGate.RequiresPlanConfirmation(
            "platform_request",
            """{"method":"POST","path":"/products/5/files/from-text","body":{"fileName":"a.csv","text":"x"}}"""));
        Assert.False(AgentChatTurnGate.RequiresPlanConfirmation(
            "platform_request",
            """{"method":"GET","path":"/products/5/files"}"""));
        Assert.True(PlatformRequestPolicy.IsAllowed("POST", "/products/5/files/from-text"));
    }

    [Theory]
    [InlineData("сохрани csv файл в файлы товара")]
    [InlineData("save generated product csv file")]
    public void SearchActions_GeneratedProductFile_FindsTheJsonRoute(string query)
    {
        var hits = new CabinetActionCatalog().Search(query);

        var hit = Assert.Single(hits,
            h => h.Path.Equals("/products/{productId}/files/from-text", StringComparison.Ordinal));
        Assert.True(hit.Write);
        Assert.Contains("262144", hit.Note);
        Assert.Contains("downloadUrl", hit.Note);
        Assert.Contains("not store storage", hit.Note);
    }

    [Fact]
    public void CanonicalAudit_FromTextContracts_AreDistinctAndMatchTheDtos()
    {
        var errors = AssistantInstructionAudit.Check(
        [
            ("policy", PlatformRequestPolicy.SurfaceDescription, true),
            ("catalog", CabinetActionCatalog.InstructionText(), true),
        ]);
        Assert.True(errors.Count == 0, string.Join("\n", errors));

        // The two from-text routes resolve to their own canonical contract:
        // longest suffix keeps the store path on the store sentence.
        var store = AssistantWriteContract.Match("POST", "/api/store-storage/files/from-text");
        var product = AssistantWriteContract.Match("POST", "/api/products/5/files/from-text");
        Assert.NotNull(store);
        Assert.NotNull(product);
        Assert.Equal("store-storage/files/from-text", store.PathSuffix);
        Assert.Equal("files/from-text", product.PathSuffix);
        Assert.Contains("not store storage", product.Sentence);

        Assert.Contains("POST files/from-text", PlatformRequestPolicy.SurfaceDescription);
        var fields = AssistantBodySchema.AcceptedFields("POST", "/api/products/5/files/from-text");
        Assert.NotNull(fields);
        Assert.Contains("fileName", fields);
        Assert.Contains("text", fields);
    }

    private static FileController NewController(
        DataContext db, IStorageService storage, User user, Store store) =>
        NewController(db, storage,
            new FakeCurrentUserService { UserId = user.Id, SelectedStoreId = store.Id, User = user });

    private static FileController NewController(
        DataContext db, IStorageService storage, FakeCurrentUserService current)
    {
        var uow = TestDbFactory.CreateUnitOfWork(db);
        return new FileController(
            current,
            uow,
            storage,
            new ServiceCollection().BuildServiceProvider(),
            new ThumbnailService(storage, NullLogger<ThumbnailService>.Instance),
            new QuotaService(uow, db),
            db,
            NewService(db, storage));
    }

    private sealed class MemoryStorage : IStorageService
    {
        public bool UploadSucceeds { get; set; } = true;
        public bool DeleteThrows { get; set; }
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
            if (DeleteThrows)
                throw new InvalidOperationException("Injected delete failure");
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

    private sealed class FailNextFileSave : SaveChangesInterceptor
    {
        public const string FailureMessage = "Injected product-file save failure";
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed
                && eventData.Context?.ChangeTracker.Entries<ProductFile>()
                    .Any(e => e.State == EntityState.Added) == true)
            {
                Armed = false;
                throw new InvalidOperationException(FailureMessage);
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
