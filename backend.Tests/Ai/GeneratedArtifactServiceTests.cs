using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Delegat;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Delegat.Models.Ai;
using Delegat.Services;
using Delegat.Services.Ai.Agent;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests.Ai;

[Collection("EnvVars")]
public class GeneratedArtifactServiceTests
{
    private static GeneratedArtifactService NewService(
        DataContext db, IStorageService storage, TimeProvider? time = null,
        IPermissionService? permissions = null) =>
        new(
            db,
            storage,
            new QuotaService(TestDbFactory.CreateUnitOfWork(db), db),
            new AgentSessionResolver(
                new FakeCurrentUserService(),
                permissions ?? new GrantAllPermissions(),
                db,
                new HttpContextAccessor()),
            NullLogger<GeneratedArtifactService>.Instance,
            time);

    private static VerifiedGeneratedOutput Out(
        string name, string contentType, byte[] bytes) =>
        new(new GeneratedOutputSpec(name, contentType),
            bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            new MemoryStream(bytes));

    private static GeneratedFileImportRequest ImportRequest(
        GeneratedArtifactMetadata meta, string destination = "store",
        int? productId = null, int? folderId = null) =>
        new(meta.ArtifactId, meta.Sha256, meta.Length, meta.ExpiresAtUtc,
            destination, productId, folderId);

    private static byte[] Sha(byte[] bytes) => SHA256.HashData(bytes);

    [Fact]
    public async Task Stage_WholeBatchReady_ExactMetadataAndScopedKeys()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var run = new AiSandboxRun { storeId = store.Id, userId = user.Id, scriptHash = "h", exitCode = 0, createdAt = DateTime.UtcNow };
        db.aiSandboxRuns.Add(run);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var csv = Encoding.UTF8.GetBytes("a,b\n1,2\n");
        var bin = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        var svc = NewService(db, storage);

        var metas = await svc.StageCapturedAsync(store.Id, user.Id, run.Id,
            [Out("r.csv", "text/csv", csv), Out("b.bin", "application/octet-stream", bin)], default);

        Assert.Equal(2, metas.Count);
        Assert.Matches("^[0-9a-f]{32}$", metas[0].ArtifactId);
        Assert.Equal("r.csv", metas[0].FileName);
        Assert.Equal(csv.Length, metas[0].Length);
        Assert.Equal(Convert.ToHexString(Sha(csv)).ToLowerInvariant(), metas[0].Sha256);
        Assert.True(metas[0].ExpiresAtUtc > DateTime.UtcNow.AddHours(23));
        Assert.Equal(0, metas[0].ExpiresAtUtc.Ticks % 10); // datetime(6) precision

        foreach (var meta in metas)
        {
            var key = $"__ai-generated/{store.Id}/{user.Id}/{meta.ArtifactId}/{meta.FileName}";
            Assert.True(storage.StoredObjects.ContainsKey(key));
        }
        Assert.Equal(csv, storage.StoredObjects[$"__ai-generated/{store.Id}/{user.Id}/{metas[0].ArtifactId}/r.csv"]);
        Assert.Equal(bin, storage.StoredObjects[$"__ai-generated/{store.Id}/{user.Id}/{metas[1].ArtifactId}/b.bin"]);

        var rows = await db.aiGeneratedArtifacts.ToListAsync();
        Assert.All(rows, r => Assert.Equal("ready", r.state));

        var quota = new QuotaService(TestDbFactory.CreateUnitOfWork(db), db);
        var usage = await quota.GetStoreFileQuotaAsync(store.Id);
        Assert.Equal(csv.Length + bin.Length, usage.StorageBytes.Used);
        Assert.Equal(0, usage.Files.Used); // staged bytes are not files
    }

    [Fact]
    public async Task Stage_UploadFailure_NoUsableHandles()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var storage = new MemoryStorage { UploadSucceeds = false };
        var svc = NewService(db, storage);
        var ex = await Assert.ThrowsAsync<GeneratedArtifactException>(() =>
            svc.StageCapturedAsync(store.Id, user.Id, 1,
                [Out("r.csv", "text/csv", [1, 2])], default));
        Assert.Equal("ai_artifact_capture_failed", ex.Code);
        Assert.Empty(await db.aiGeneratedArtifacts.ToListAsync());
    }

    [Fact]
    public async Task Import_StoreDestination_TypedRowPrivateKeyIdempotentReplay()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var svc = NewService(db, storage);
        var bytes = Encoding.UTF8.GetBytes("report");
        var metas = await svc.StageCapturedAsync(store.Id, user.Id, 1,
            [Out("r.csv", "text/csv", bytes)], default);
        var request = ImportRequest(metas[0]);

        var result = await svc.ImportAsync(request, store.Id, user.Id, default);

        var file = await db.storeFiles.SingleAsync();
        Assert.Equal(file.Id, result.FileId);
        Assert.Equal("r.csv", file.displayName);
        Assert.Equal(Enums.FileType.Other, file.fileType);
        Assert.Equal(bytes, storage.StoredObjects[file.filePath]);
        Assert.StartsWith($"{store.Id}/store/", file.filePath);
        Assert.Equal($"/api/store-storage/files/{file.Id}/download", result.DownloadUrl);
        Assert.Equal(1, result.WrittenCount);
        Assert.False(result.Idempotent);
        Assert.Equal(2, storage.UploadCount); // stage + destination

        var receipt = await db.aiGeneratedArtifactImports.SingleAsync();
        Assert.Equal("completed", receipt.state);
        Assert.Equal(file.Id, receipt.fileId);
        Assert.Equal("store", receipt.destination);
        Assert.Equal(file.filePath, receipt.destinationKey);

        // Exact replay resolves the same receipt and file; nothing is rewritten.
        var replay = await svc.ImportAsync(request, store.Id, user.Id, default);
        Assert.True(replay.Idempotent);
        Assert.Equal(0, replay.WrittenCount);
        Assert.Equal(file.Id, replay.FileId);
        Assert.Equal(2, storage.UploadCount);
        Assert.Single(await db.storeFiles.ToListAsync());
    }

    [Fact]
    public async Task Import_ProductDestination_TypedProductFile()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var product = data.AddProduct(store.Id);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var svc = NewService(db, storage);
        var bytes = new byte[] { 9, 9, 9 };
        var metas = await svc.StageCapturedAsync(store.Id, user.Id, 1,
            [Out("b.bin", "application/octet-stream", bytes)], default);
        var request = ImportRequest(metas[0], "product", productId: product.Id);

        var result = await svc.ImportAsync(request, store.Id, user.Id, default);

        var file = await db.files.SingleAsync();
        Assert.Equal(file.Id, result.FileId);
        Assert.Equal(product.Id, file.productId);
        Assert.Equal(Enums.FileType.Other, file.fileType);
        Assert.StartsWith($"{store.Id}/{product.Id}/", file.filePath);
        Assert.Equal($"/api/files/{file.Id}/download", result.DownloadUrl);
        Assert.Empty(await db.storeFiles.ToListAsync());

        var receipt = await db.aiGeneratedArtifactImports.SingleAsync();
        Assert.Equal("product", receipt.destination);
        Assert.Equal(product.Id, receipt.productId);
    }

    [Fact]
    public async Task Import_SecondDestination_HasOwnFenceAndFile()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var folder = new StoreFolder { name = "gen", storeId = store.Id };
        db.storeFolders.Add(folder);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var svc = NewService(db, storage);
        var metas = await svc.StageCapturedAsync(store.Id, user.Id, 1,
            [Out("r.csv", "text/csv", [1])], default);

        var root = await svc.ImportAsync(ImportRequest(metas[0]), store.Id, user.Id, default);
        var into = await svc.ImportAsync(
            ImportRequest(metas[0], folderId: folder.Id), store.Id, user.Id, default);

        Assert.NotEqual(root.FileId, into.FileId);
        Assert.Equal(2, await db.storeFiles.CountAsync());
        Assert.Equal(2, await db.aiGeneratedArtifactImports.CountAsync());
        Assert.Equal(folder.Id, (await db.storeFiles.OrderByDescending(f => f.Id).FirstAsync()).folderId);
    }

    [Fact]
    public async Task Import_ForeignHandle_MetadataNeverLeaks()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var other = data.AddUser();
        var store = data.AddStore(members: user);
        var otherStore = data.AddStore(members: other);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var svc = NewService(db, storage);
        var metas = await svc.StageCapturedAsync(store.Id, user.Id, 1,
            [Out("r.csv", "text/csv", [1])], default);

        var wrongStore = await Assert.ThrowsAsync<GeneratedArtifactException>(() =>
            svc.ImportAsync(ImportRequest(metas[0]), otherStore.Id, other.Id, default));
        Assert.Equal("ai_artifact_not_found", wrongStore.Code);

        var wrongUser = await Assert.ThrowsAsync<GeneratedArtifactException>(() =>
            svc.ImportAsync(ImportRequest(metas[0]), store.Id, other.Id, default));
        // The foreign user is not a member of this store: forbidden, not found.
        Assert.Equal("forbidden", wrongUser.Code);
    }

    [Fact]
    public async Task Import_TamperedMetadata_RejectedBeforeWrite()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var svc = NewService(db, storage);
        var metas = await svc.StageCapturedAsync(store.Id, user.Id, 1,
            [Out("r.csv", "text/csv", [1])], default);

        var badSha = ImportRequest(metas[0]) with { Sha256 = new string('0', 64) };
        var badLen = ImportRequest(metas[0]) with { Length = metas[0].Length + 1 };
        var badExpiry = ImportRequest(metas[0]) with { ExpiresAtUtc = metas[0].ExpiresAtUtc.AddMinutes(1) };

        Assert.Equal("ai_artifact_mismatch",
            (await Assert.ThrowsAsync<GeneratedArtifactException>(
                () => svc.ImportAsync(badSha, store.Id, user.Id, default))).Code);
        Assert.Equal("ai_artifact_mismatch",
            (await Assert.ThrowsAsync<GeneratedArtifactException>(
                () => svc.ImportAsync(badLen, store.Id, user.Id, default))).Code);
        Assert.Equal("ai_artifact_mismatch",
            (await Assert.ThrowsAsync<GeneratedArtifactException>(
                () => svc.ImportAsync(badExpiry, store.Id, user.Id, default))).Code);
        Assert.Equal(1, storage.UploadCount); // only the stage upload
        Assert.Empty(await db.aiGeneratedArtifactImports.ToListAsync());
    }

    [Fact]
    public async Task Import_ExpiredArtifact_Rejected()
    {
        var clock = new Clock();
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var svc = NewService(db, storage, clock);
        var metas = await svc.StageCapturedAsync(store.Id, user.Id, 1,
            [Out("r.csv", "text/csv", [1])], default);

        clock.UtcNow = metas[0].ExpiresAtUtc.AddSeconds(1);
        var ex = await Assert.ThrowsAsync<GeneratedArtifactException>(() =>
            svc.ImportAsync(ImportRequest(metas[0]), store.Id, user.Id, default));
        Assert.Equal("ai_artifact_expired", ex.Code);
    }

    [Fact]
    public async Task Import_CorruptedStaging_KnownFailureAllowsNewConfirmedRetry()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var svc = NewService(db, storage);
        var bytes = new byte[] { 7, 7 };
        var metas = await svc.StageCapturedAsync(store.Id, user.Id, 1,
            [Out("r.csv", "text/csv", bytes)], default);
        var request = ImportRequest(metas[0]);

        // Simulate staged bytes being replaced underneath the artifact.
        var stagedKey = $"__ai-generated/{store.Id}/{user.Id}/{metas[0].ArtifactId}/r.csv";
        storage.StoredObjects[stagedKey] = [0, 0];

        var ex = await Assert.ThrowsAsync<GeneratedArtifactException>(() =>
            svc.ImportAsync(request, store.Id, user.Id, default));
        Assert.Equal("ai_artifact_mismatch", ex.Code);
        // The newly claimed receipt was released after cleanup: a NEW confirmed
        // plan may retry (the consumed plan itself is never replayed).
        Assert.Empty(await db.aiGeneratedArtifactImports.ToListAsync());

        storage.StoredObjects[stagedKey] = bytes;
        var result = await svc.ImportAsync(request, store.Id, user.Id, default);
        Assert.Equal(1, result.WrittenCount);
    }

    [Fact]
    public async Task Import_AmbiguousFinalSave_RetainsFenceAndBlob_NoReplay()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<DataContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(new FailOnStoreFileAdd())
            .Options;
        await using var db = new DataContext(options);
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var svc = NewService(db, storage);
        var bytes = new byte[] { 3 };
        var metas = await svc.StageCapturedAsync(store.Id, user.Id, 1,
            [Out("r.csv", "text/csv", bytes)], default);
        var request = ImportRequest(metas[0]);

        var ex = await Assert.ThrowsAsync<GeneratedArtifactException>(() =>
            svc.ImportAsync(request, store.Id, user.Id, default));
        Assert.Equal("ai_artifact_import_unknown", ex.Code);
        Assert.True(ex.OutcomeUnknown);

        // Destination blob and durable fence stay for reconciliation; the tracked
        // file entry was detached so an audit SaveChanges cannot replay the write.
        await db.SaveChangesAsync();
        Assert.Empty(await db.storeFiles.ToListAsync());
        var receipt = await db.aiGeneratedArtifactImports.SingleAsync();
        Assert.Equal("started", receipt.state);
        Assert.True(storage.StoredObjects.ContainsKey(receipt.destinationKey));

        var uploadsBefore = storage.UploadCount;
        var second = await Assert.ThrowsAsync<GeneratedArtifactException>(() =>
            svc.ImportAsync(request, store.Id, user.Id, default));
        Assert.Equal("ai_artifact_import_unknown", second.Code);
        Assert.Equal(uploadsBefore, storage.UploadCount); // zero I/O on a started fence
        Assert.Empty(await db.storeFiles.ToListAsync());
    }

    [Fact]
    public async Task Import_CompletedReceipt_AcrossContexts_ReturnsSameFile()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var db1 = TestDbFactory.CreateDb(dbName);
        await using var db2 = TestDbFactory.CreateDb(dbName);
        var data = new TestDataBuilder(db1);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var bytes = new byte[] { 5 };
        var metas = await NewService(db1, storage).StageCapturedAsync(store.Id, user.Id, 1,
            [Out("r.csv", "text/csv", bytes)], default);
        var request = ImportRequest(metas[0]);

        var first = await NewService(db1, storage).ImportAsync(request, store.Id, user.Id, default);
        var second = await NewService(db2, storage).ImportAsync(request, store.Id, user.Id, default);

        Assert.False(first.Idempotent);
        Assert.True(second.Idempotent);
        Assert.Equal(first.FileId, second.FileId);
        Assert.Equal(0, second.WrittenCount);
    }

    [Fact]
    public async Task Import_WithoutFilesManage_Forbidden()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var svc = NewService(db, storage);
        var metas = await svc.StageCapturedAsync(store.Id, user.Id, 1,
            [Out("r.csv", "text/csv", [1])], default);

        var limited = new StaticPermissions(Permissions.AiUse);
        var limitedSvc = NewService(db, storage, permissions: limited);
        var ex = await Assert.ThrowsAsync<GeneratedArtifactException>(() =>
            limitedSvc.ImportAsync(ImportRequest(metas[0]), store.Id, user.Id, default));
        Assert.Equal("forbidden", ex.Code);
    }

    [Fact]
    public async Task WorkspaceViewPlusFilesManage_WithoutAiUse_ForbiddenBeforeAnyWork()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var svc = NewService(db, storage);
        var metas = await svc.StageCapturedAsync(store.Id, user.Id, 1,
            [Out("r.csv", "text/csv", [1])], default);

        // WorkspaceView grants agent access and FilesManage is present, but the
        // importer/stager also require explicit ai.use — refuse before any
        // receipt claim, upload, or new reservation.
        var viewOnly = NewService(db, storage,
            permissions: new StaticPermissions(Permissions.WorkspaceView, Permissions.FilesManage));

        var uploadsBefore = storage.UploadCount;
        Assert.Equal("forbidden", (await Assert.ThrowsAsync<GeneratedArtifactException>(
            () => viewOnly.ImportAsync(ImportRequest(metas[0]), store.Id, user.Id, default))).Code);
        Assert.Empty(await db.aiGeneratedArtifactImports.ToListAsync());
        Assert.Equal(uploadsBefore, storage.UploadCount);

        var artifactsBefore = await db.aiGeneratedArtifacts.CountAsync();
        Assert.Equal("forbidden", (await Assert.ThrowsAsync<GeneratedArtifactException>(
            () => viewOnly.StageCapturedAsync(store.Id, user.Id, 2,
                [Out("x.csv", "text/csv", [2])], default))).Code);
        Assert.Equal(artifactsBefore, await db.aiGeneratedArtifacts.CountAsync());
    }

    [Fact]
    public async Task ValidateImport_ReturnsServerVerifiedApproval()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var folder = new StoreFolder { name = "gen", storeId = store.Id };
        db.storeFolders.Add(folder);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var svc = NewService(db, storage);
        var metas = await svc.StageCapturedAsync(store.Id, user.Id, 1,
            [Out("отчёт.csv", "text/csv", [1])], default);
        var request = ImportRequest(metas[0], folderId: folder.Id);

        var approval = await svc.ValidateImportAsync(request, store.Id, user.Id, 0, default);

        Assert.Equal(0, approval.Step);
        Assert.Equal(metas[0].ArtifactId, approval.ArtifactId);
        Assert.Equal("отчёт.csv", approval.FileName);
        Assert.Equal(metas[0].Sha256, approval.Sha256);
        Assert.Equal(metas[0].ExpiresAtUtc, approval.ExpiresAtUtc);
        Assert.Equal(store.Id, approval.StoreId);
        Assert.Equal(folder.Id, approval.FolderId);
    }

    [Fact]
    public async Task PurgeExpired_DeletesOnlyGeneratedKeys_KeepsReceiptAndDestination()
    {
        var clock = new Clock();
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var svc = NewService(db, storage, clock);
        var bytes = new byte[] { 1 };
        var metas = await svc.StageCapturedAsync(store.Id, user.Id, 1,
            [Out("r.csv", "text/csv", bytes)], default);
        var imported = await svc.ImportAsync(ImportRequest(metas[0]), store.Id, user.Id, default);
        var stagedKey = $"__ai-generated/{store.Id}/{user.Id}/{metas[0].ArtifactId}/r.csv";
        var destKey = (await db.aiGeneratedArtifactImports.SingleAsync()).destinationKey;

        clock.UtcNow = metas[0].ExpiresAtUtc.AddHours(1);
        Assert.Equal(1, await svc.PurgeExpiredAsync(default));

        Assert.False(storage.StoredObjects.ContainsKey(stagedKey));
        Assert.True(storage.StoredObjects.ContainsKey(destKey)); // imported copy stays
        Assert.Empty(await db.aiGeneratedArtifacts.ToListAsync());
        var receipt = await db.aiGeneratedArtifactImports.SingleAsync();
        Assert.Equal("completed", receipt.state); // receipts are never purged
    }

    [Fact]
    public async Task Stage_WriteThenFalse_DeletesAttemptedKeyAndRows()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var storage = new MemoryStorage { Behavior = MemoryStorage.UploadBehavior.WriteThenFalse };
        var ex = await Assert.ThrowsAsync<GeneratedArtifactException>(() =>
            NewService(db, storage).StageCapturedAsync(store.Id, user.Id, 1,
                [Out("r.csv", "text/csv", [1, 2])], default));
        Assert.Equal("ai_artifact_capture_failed", ex.Code);
        Assert.Empty(storage.StoredObjects); // attempted write was cleaned
        Assert.Empty(await db.aiGeneratedArtifacts.ToListAsync());
    }

    [Fact]
    public async Task Stage_WriteThenThrowAndDeleteFails_RetainsRows_PurgeAfterExpiryRemovesStagedOnly()
    {
        var clock = new Clock();
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var storage = new MemoryStorage
        {
            Behavior = MemoryStorage.UploadBehavior.WriteThenThrow,
            DeleteOverride = () => Task.FromResult(false),
        };
        var svc = NewService(db, storage, clock);
        await Assert.ThrowsAsync<GeneratedArtifactException>(() =>
            svc.StageCapturedAsync(store.Id, user.Id, 1, [Out("r.csv", "text/csv", [1, 2])], default));

        // Unproven delete => rows stay so bytes remain accountable for retry cleanup.
        var row = await db.aiGeneratedArtifacts.SingleAsync();
        Assert.Equal("capturing", row.state);
        Assert.True(storage.StoredObjects.ContainsKey(row.storageKey));
        await db.SaveChangesAsync(); // audit save must not publish pending removals
        Assert.Single(await db.aiGeneratedArtifacts.ToListAsync());

        // Expired retained rows are retried by the sweeper; now deletion succeeds.
        storage.Behavior = MemoryStorage.UploadBehavior.Ok;
        storage.DeleteOverride = null;
        clock.UtcNow = DateTime.UtcNow.AddDays(2);
        Assert.Equal(1, await svc.PurgeExpiredAsync(default));
        Assert.Empty(await db.aiGeneratedArtifacts.ToListAsync());
        Assert.Empty(storage.StoredObjects);
    }

    [Fact]
    public async Task Import_WriteThenFalse_DeletesAttemptedBlobAndReleasesClaim()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var svc = NewService(db, storage);
        var metas = await svc.StageCapturedAsync(store.Id, user.Id, 1,
            [Out("r.csv", "text/csv", [3])], default);
        var request = ImportRequest(metas[0]);

        storage.Behavior = MemoryStorage.UploadBehavior.WriteThenFalse;
        var ex = await Assert.ThrowsAsync<GeneratedArtifactException>(() =>
            svc.ImportAsync(request, store.Id, user.Id, default));
        Assert.Equal("ai_artifact_storage_failed", ex.Code);
        Assert.False(ex.OutcomeUnknown);
        // Attempted destination bytes were cleaned and the claim released.
        Assert.Single(storage.StoredObjects); // only the staged artifact remains
        Assert.Empty(await db.aiGeneratedArtifactImports.ToListAsync());

        storage.Behavior = MemoryStorage.UploadBehavior.Ok;
        var result = await svc.ImportAsync(request, store.Id, user.Id, default);
        Assert.Equal(1, result.WrittenCount);
    }

    [Fact]
    public async Task Import_WriteThenThrowAndDeleteFails_RetainsFenceAndBlob_ZeroIoOnFence()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var svc = NewService(db, storage);
        var metas = await svc.StageCapturedAsync(store.Id, user.Id, 1,
            [Out("r.csv", "text/csv", [4])], default);
        var request = ImportRequest(metas[0]);

        storage.Behavior = MemoryStorage.UploadBehavior.WriteThenThrow;
        storage.DeleteOverride = () => Task.FromResult(false);
        var ex = await Assert.ThrowsAsync<GeneratedArtifactException>(() =>
            svc.ImportAsync(request, store.Id, user.Id, default));
        Assert.Equal("ai_artifact_import_unknown", ex.Code);
        Assert.True(ex.OutcomeUnknown);

        var receipt = await db.aiGeneratedArtifactImports.SingleAsync();
        Assert.Equal("started", receipt.state);
        Assert.True(storage.StoredObjects.ContainsKey(receipt.destinationKey));
        await db.SaveChangesAsync(); // audit save must not publish pending receipt removal
        Assert.Single(await db.aiGeneratedArtifactImports.ToListAsync());

        // The retained fence blocks any retry with zero storage I/O.
        var uploads = storage.UploadCount;
        storage.Behavior = MemoryStorage.UploadBehavior.Ok;
        storage.DeleteOverride = null;
        var second = await Assert.ThrowsAsync<GeneratedArtifactException>(() =>
            svc.ImportAsync(request, store.Id, user.Id, default));
        Assert.Equal("ai_artifact_import_unknown", second.Code);
        Assert.Equal(uploads, storage.UploadCount);
        Assert.Empty(await db.storeFiles.ToListAsync());
    }

    [Fact]
    public async Task Import_UnknownFence_PurgeNeverDeletesDestinationKey()
    {
        var clock = new Clock();
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var svc = NewService(db, storage, clock);
        var metas = await svc.StageCapturedAsync(store.Id, user.Id, 1,
            [Out("r.csv", "text/csv", [5])], default);
        storage.Behavior = MemoryStorage.UploadBehavior.WriteThenThrow;
        storage.DeleteOverride = () => Task.FromResult(false);
        await Assert.ThrowsAsync<GeneratedArtifactException>(() =>
            svc.ImportAsync(ImportRequest(metas[0]), store.Id, user.Id, default));
        var destKey = (await db.aiGeneratedArtifactImports.SingleAsync()).destinationKey;

        storage.Behavior = MemoryStorage.UploadBehavior.Ok;
        storage.DeleteOverride = null;
        clock.UtcNow = DateTime.UtcNow.AddDays(2);
        await svc.PurgeExpiredAsync(default);
        Assert.True(storage.StoredObjects.ContainsKey(destKey)); // fences/destinations never purged
        Assert.Single(await db.aiGeneratedArtifactImports.ToListAsync());
    }

    [Fact]
    public async Task Import_CancelAtFinalSave_DetachesOwned_NoAuditReplay()
    {
        var options = new DbContextOptionsBuilder<DataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(new CancelOnStoreFileAdd())
            .Options;
        await using var db = new DataContext(options);
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var svc = NewService(db, storage);
        var metas = await svc.StageCapturedAsync(store.Id, user.Id, 1,
            [Out("r.csv", "text/csv", [6])], default);

        // Cancellation after the final save attempt: fence + blob stay, tracked
        // file/receipt are detached so a later audit save cannot mint either.
        var ex = await Assert.ThrowsAsync<GeneratedArtifactException>(() =>
            svc.ImportAsync(ImportRequest(metas[0]), store.Id, user.Id, default));
        Assert.Equal("ai_artifact_import_unknown", ex.Code);
        Assert.True(ex.OutcomeUnknown);

        await db.SaveChangesAsync(CancellationToken.None);
        Assert.Empty(await db.storeFiles.ToListAsync());
        var receipt = await db.aiGeneratedArtifactImports.SingleAsync();
        Assert.Equal("started", receipt.state); // not "completed" from a rolled-back save
        Assert.True(storage.StoredObjects.ContainsKey(receipt.destinationKey));
    }

    [Fact]
    public async Task Import_Sqlite_ConcurrentClaims_FenceBlocksSecond_IoOnce()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using (db)
        {
            var data = new TestDataBuilder(db);
            var user = data.AddUser();
            var store = data.AddStore(members: user);
            await data.SaveAsync();

            var storage = new MemoryStorage();
            var gate = new TaskCompletionSource();
            var entered = new TaskCompletionSource();
            storage.BeforeDestinationUpload = async () =>
            {
                entered.TrySetResult();
                await gate.Task;
            };
            var svc1 = NewService(db, storage);
            var metas = await svc1.StageCapturedAsync(store.Id, user.Id, 1,
                [Out("r.csv", "text/csv", [7])], default);
            var request = ImportRequest(metas[0]);

            // Context A claims the receipt and pauses mid-write.
            var first = svc1.ImportAsync(request, store.Id, user.Id, default);
            await entered.Task;

            // Context B (separate context, same SQLite DSN) hits the committed
            // started fence: import_unknown, zero additional destination I/O.
            await using var db2 = TestDbFactory.CreateSqliteContext(TestDbFactory.SqliteDsn(db));
            var svc2 = NewService(db2, storage);
            storage.BeforeDestinationUpload = null;
            var uploadsBefore = storage.DestinationUploadCount;
            var second = await Assert.ThrowsAsync<GeneratedArtifactException>(() =>
                svc2.ImportAsync(request, store.Id, user.Id, default));
            Assert.Equal("ai_artifact_import_unknown", second.Code);
            Assert.Equal(uploadsBefore, storage.DestinationUploadCount);

            gate.SetResult();
            var done = await first;
            Assert.Equal(1, storage.DestinationUploadCount);
            Assert.Equal(1, await db2.storeFiles.CountAsync());

            // A third context sees the completed receipt: idempotent, no I/O.
            await using var db3 = TestDbFactory.CreateSqliteContext(TestDbFactory.SqliteDsn(db));
            var third = await NewService(db3, storage)
                .ImportAsync(request, store.Id, user.Id, default);
            Assert.True(third.Idempotent);
            Assert.Equal(done.FileId, third.FileId);
            Assert.Equal(1, storage.DestinationUploadCount);
        }
    }

    [Fact]
    public async Task Import_Sqlite_RetryingStrategy_UserTransactionsWork_AndSingleAttempt()
    {
        var counter = new CountStoreFileSaves();
        var (db, keeper) = TestDbFactory.CreateSqliteDb(
            retryingStrategy: true, interceptors: [counter]);
        await using var _ = keeper;
        await using (db)
        {
            var data = new TestDataBuilder(db);
            var user = data.AddUser();
            var store = data.AddStore(members: user);
            await data.SaveAsync();

            // With EnableRetryOnFailure configured, manual transactions inside
            // the single-attempt wrapper still work (no
            // ExecutionStrategyExistingTransaction-style failure).
            var storage = new MemoryStorage();
            var svc = NewService(db, storage);
            var metas = await svc.StageCapturedAsync(store.Id, user.Id, 1,
                [Out("r.csv", "text/csv", [8])], default);
            var result = await svc.ImportAsync(ImportRequest(metas[0]), store.Id, user.Id, default);
            Assert.Equal(1, result.WrittenCount);
            Assert.Equal(1, await db.storeFiles.CountAsync());
            var fileSaves = counter.FileAddSaves;
            Assert.True(fileSaves >= 1);

            // A started fence short-circuits with zero further file saves.
            var receipt = await db.aiGeneratedArtifactImports.SingleAsync();
            receipt.state = "started";
            receipt.fileId = null;
            await db.SaveChangesAsync();
            await Assert.ThrowsAsync<GeneratedArtifactException>(() =>
                svc.ImportAsync(ImportRequest(metas[0]), store.Id, user.Id, default));
            Assert.Equal(fileSaves, counter.FileAddSaves);
        }
    }

    [Fact]
    public async Task ValidateImport_CompletedReceipt_NotRejectedByQuota()
    {
        var previous = Environment.GetEnvironmentVariable("MAX_FILES");
        Environment.SetEnvironmentVariable("MAX_FILES", "1");
        try
        {
            await using var db = TestDbFactory.CreateDb();
            var data = new TestDataBuilder(db);
            var user = data.AddUser();
            var store = data.AddStore(members: user);
            await data.SaveAsync();

            var storage = new MemoryStorage();
            var svc = NewService(db, storage);
            var metas = await svc.StageCapturedAsync(store.Id, user.Id, 1,
                [Out("r.csv", "text/csv", [9])], default);
            var request = ImportRequest(metas[0]);
            var done = await svc.ImportAsync(request, store.Id, user.Id, default);

            // At the MAX_FILES cap the already-completed import stays approvable.
            var approval = await svc.ValidateImportAsync(request, store.Id, user.Id, 0, default);
            Assert.Equal(metas[0].ArtifactId, approval.ArtifactId);
            var replay = await svc.ImportAsync(request, store.Id, user.Id, default);
            Assert.Equal(0, replay.WrittenCount);
            Assert.Equal(done.FileId, replay.FileId);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MAX_FILES", previous);
        }
    }

    [Fact]
    public async Task ValidateImport_StartedFence_UnknownNotQuota()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var svc = NewService(db, storage);
        var metas = await svc.StageCapturedAsync(store.Id, user.Id, 1,
            [Out("r.csv", "text/csv", [10])], default);
        var request = ImportRequest(metas[0]);
        var artifact = await db.aiGeneratedArtifacts.SingleAsync();
        var binding = ComputeBinding(request, artifact, store.Id, user.Id);
        db.aiGeneratedArtifactImports.Add(new AiGeneratedArtifactImport
        {
            Id = binding, storeId = store.Id, userId = user.Id,
            artifactId = metas[0].ArtifactId, length = metas[0].Length,
            destination = "store", destinationKey = "k", state = "started",
            createdAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<GeneratedArtifactException>(() =>
            svc.ValidateImportAsync(request, store.Id, user.Id, 0, default));
        Assert.Equal("ai_artifact_import_unknown", ex.Code);
        Assert.True(ex.OutcomeUnknown);
    }

    private static string ComputeBinding(
        GeneratedFileImportRequest request, AiGeneratedArtifact artifact, int storeId, int userId)
    {
        var method = typeof(GeneratedArtifactService).GetMethod(
            "ComputeBindingId", System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Static)!;
        return (string)method.Invoke(null, [request, artifact, storeId, userId])!;
    }

    [Fact]
    public async Task ValidateCapture_PermissionMatrix()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var outsider = data.AddUser();
        await data.SaveAsync();
        var storage = new MemoryStorage();

        // files.manage without ai.use: still refused.
        var noAiUse = NewService(db, storage,
            permissions: new StaticPermissions(Permissions.FilesManage));
        Assert.Equal("forbidden", (await Assert.ThrowsAsync<GeneratedArtifactException>(
            () => noAiUse.ValidateCaptureAsync(store.Id, user.Id, default))).Code);

        // ai.use without files.manage: refused.
        var noManage = NewService(db, storage,
            permissions: new StaticPermissions(Permissions.AiUse));
        Assert.Equal("forbidden", (await Assert.ThrowsAsync<GeneratedArtifactException>(
            () => noManage.ValidateCaptureAsync(store.Id, user.Id, default))).Code);

        // Both permissions but not a member of this store: refused.
        var nonMember = NewService(db, storage);
        Assert.Equal("forbidden", (await Assert.ThrowsAsync<GeneratedArtifactException>(
            () => nonMember.ValidateCaptureAsync(store.Id, outsider.Id, default))).Code);

        // Full grant: passes.
        await NewService(db, storage).ValidateCaptureAsync(store.Id, user.Id, default);
    }

    [Fact]
    public async Task Import_FolderFromForeignStore_Rejected()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var other = data.AddStore(members: user);
        var foreignFolder = new StoreFolder { name = "f", storeId = other.Id };
        db.storeFolders.Add(foreignFolder);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var svc = NewService(db, storage);
        var metas = await svc.StageCapturedAsync(store.Id, user.Id, 1,
            [Out("r.csv", "text/csv", [11])], default);
        var request = ImportRequest(metas[0], folderId: foreignFolder.Id);
        Assert.Equal("ai_artifact_invalid_request", (await Assert.ThrowsAsync<GeneratedArtifactException>(
            () => svc.ImportAsync(request, store.Id, user.Id, default))).Code);
    }

    [Fact]
    public async Task Import_ProductFolderFromOtherProduct_Rejected()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var productA = data.AddProduct(store.Id);
        var productB = data.AddProduct(store.Id);
        var folderOfA = new ProductFolder { name = "f", productId = productA.Id };
        db.productFolders.Add(folderOfA);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var svc = NewService(db, storage);
        var metas = await svc.StageCapturedAsync(store.Id, user.Id, 1,
            [Out("r.csv", "text/csv", [3])], default);
        var request = ImportRequest(
            metas[0], "product", productId: productB.Id, folderId: folderOfA.Id);

        Assert.Equal("ai_artifact_invalid_request", (await Assert.ThrowsAsync<GeneratedArtifactException>(
            () => svc.ImportAsync(request, store.Id, user.Id, default))).Code);
        Assert.Empty(await db.aiGeneratedArtifactImports.ToListAsync());
        Assert.Equal(0, storage.DestinationUploadCount);
        Assert.Empty(await db.files.ToListAsync());
    }

    [Fact]
    public async Task Import_TrashedProduct_Rejected()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var product = data.AddProduct(store.Id, deletedAt: DateTime.UtcNow);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var svc = NewService(db, storage);
        var metas = await svc.StageCapturedAsync(store.Id, user.Id, 1,
            [Out("r.csv", "text/csv", [4])], default);
        var request = ImportRequest(metas[0], "product", productId: product.Id);

        Assert.Equal("ai_artifact_invalid_request", (await Assert.ThrowsAsync<GeneratedArtifactException>(
            () => svc.ImportAsync(request, store.Id, user.Id, default))).Code);
        Assert.Empty(await db.aiGeneratedArtifactImports.ToListAsync());
        Assert.Equal(0, storage.DestinationUploadCount);
        Assert.Empty(await db.files.ToListAsync());
    }

    [Fact]
    public async Task Import_PermissionRevokedDuringUpload_RejectedAndReleased()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var permissions = new StaticPermissions(Permissions.AiUse, Permissions.FilesManage);
        var storage = new MemoryStorage();
        var svc = NewService(db, storage, permissions: permissions);
        var metas = await svc.StageCapturedAsync(store.Id, user.Id, 1,
            [Out("r.csv", "text/csv", [5])], default);

        // files.manage is revoked while the owned destination object is being
        // written: the final-transaction recheck refuses, the blob is cleaned
        // and the claim released so a fresh confirmed plan can retry.
        storage.BeforeDestinationUpload = () =>
        {
            permissions.Current = [Permissions.AiUse];
            return Task.CompletedTask;
        };

        var ex = await Assert.ThrowsAsync<GeneratedArtifactException>(() =>
            svc.ImportAsync(ImportRequest(metas[0]), store.Id, user.Id, default));
        Assert.Equal("forbidden", ex.Code);
        Assert.Equal(1, storage.DestinationUploadCount);
        Assert.Empty(await db.aiGeneratedArtifactImports.ToListAsync());
        Assert.Empty(await db.storeFiles.ToListAsync());
        Assert.All(storage.StoredObjects.Keys,
            key => Assert.StartsWith(GeneratedArtifactService.StoragePrefix, key));
    }

    [Fact]
    public async Task Import_Sqlite_CancelAtReceiptCompletedSave_UnknownRetainsFenceAndBlob()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb(
            interceptors: [new CancelOnReceiptCompleted()]);
        await using var _ = keeper;
        await using (db)
        {
            var data = new TestDataBuilder(db);
            var user = data.AddUser();
            var store = data.AddStore(members: user);
            await data.SaveAsync();

            var storage = new MemoryStorage();
            var svc = NewService(db, storage);
            var metas = await svc.StageCapturedAsync(store.Id, user.Id, 1,
                [Out("r.csv", "text/csv", [9])], default);

            var ex = await Assert.ThrowsAsync<GeneratedArtifactException>(() =>
                svc.ImportAsync(ImportRequest(metas[0]), store.Id, user.Id, default));
            Assert.Equal("ai_artifact_import_unknown", ex.Code);
            Assert.True(ex.OutcomeUnknown);

            // An unrelated audit save on the service context cannot replay the
            // detached pending file or the half-completed receipt.
            await db.SaveChangesAsync(CancellationToken.None);
            await using var fresh = TestDbFactory.CreateSqliteContext(TestDbFactory.SqliteDsn(db));
            Assert.Empty(await fresh.storeFiles.ToListAsync());
            var receipt = await fresh.aiGeneratedArtifactImports.SingleAsync();
            Assert.Equal("started", receipt.state);
            Assert.Null(receipt.fileId);
            Assert.True(storage.StoredObjects.ContainsKey(receipt.destinationKey));
        }
    }

    [Fact]
    public async Task Import_Sqlite_CancelAtCommit_UnknownRetainsFenceAndBlob()
    {
        var cancel = new CancelOnCommit();
        var (db, keeper) = TestDbFactory.CreateSqliteDb(
            interceptors: [cancel]);
        await using var _ = keeper;
        await using (db)
        {
            var data = new TestDataBuilder(db);
            var user = data.AddUser();
            var store = data.AddStore(members: user);
            await data.SaveAsync();

            var storage = new MemoryStorage();
            var svc = NewService(db, storage);
            var metas = await svc.StageCapturedAsync(store.Id, user.Id, 1,
                [Out("r.csv", "text/csv", [10])], default);

            cancel.Armed = true;
            var ex = await Assert.ThrowsAsync<GeneratedArtifactException>(() =>
                svc.ImportAsync(ImportRequest(metas[0]), store.Id, user.Id, default));
            Assert.Equal("ai_artifact_import_unknown", ex.Code);
            Assert.True(ex.OutcomeUnknown);

            await db.SaveChangesAsync(CancellationToken.None);
            await using var fresh = TestDbFactory.CreateSqliteContext(TestDbFactory.SqliteDsn(db));
            Assert.Empty(await fresh.storeFiles.ToListAsync());
            var receipt = await fresh.aiGeneratedArtifactImports.SingleAsync();
            Assert.Equal("started", receipt.state);
            Assert.Null(receipt.fileId);
            Assert.True(storage.StoredObjects.ContainsKey(receipt.destinationKey));
        }
    }

    private sealed class CancelOnReceiptCompleted : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<AiGeneratedArtifactImport>()
                    .Any(e => e.State == EntityState.Modified && e.Entity.state == "completed") == true)
                throw new OperationCanceledException("simulated cancel at receipt completion");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class CancelOnCommit : DbTransactionInterceptor
    {
        public bool Armed { get; set; }
        public int Commits { get; private set; }

        // Arms on demand: the import commits the claim first, then the final
        // file+receipt transaction; the second armed commit is the one cancelled.
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && ++Commits == 2)
                throw new OperationCanceledException("simulated cancel at commit");
            return base.TransactionCommittingAsync(
                transaction, eventData, result, cancellationToken);
        }
    }

    private sealed class CancelOnStoreFileAdd : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<StoreFile>()
                    .Any(e => e.State == EntityState.Added) == true)
                throw new OperationCanceledException("simulated cancel at file save");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class CountStoreFileSaves : SaveChangesInterceptor
    {
        public int FileAddSaves { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<StoreFile>()
                    .Any(e => e.State == EntityState.Added) == true)
                FileAddSaves++;
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class StaticPermissions(params string[] permissions) : IPermissionService
    {
        public string[] Current { get; set; } = permissions;
        public Task EnsureRolesSeededAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task MigrateLegacyAccessAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> GetEffectivePermissionsAsync(int userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(Current);
        public Task<IReadOnlyList<string>> GetRoleNamesAsync(int userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
        public Task<UserAccessDto> GetUserAccessAsync(int userId, CancellationToken ct = default) =>
            Task.FromResult(new UserAccessDto());
        public Task SetUserAccessAsync(int userId, IReadOnlyList<string> roleNames, IReadOnlyList<string> permissionGrants, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task EnsureOwnerAccessAsync(User user, CancellationToken ct = default) => Task.CompletedTask;
        public Task HydrateClaimsAsync(System.Security.Claims.ClaimsIdentity identity, int userId, CancellationToken ct = default) =>
            Task.CompletedTask;
        public void InvalidateUserAccess(int userId) { }
    }

    private sealed class FailOnStoreFileAdd : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<StoreFile>()
                    .Any(e => e.State == EntityState.Added) == true)
                throw new InvalidOperationException("injected ambiguous commit failure");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class MemoryStorage : IStorageService
    {
        public enum UploadBehavior { Ok, FalseBeforeWrite, WriteThenFalse, WriteThenThrow }

        public bool UploadSucceeds { get; set; } = true;
        public UploadBehavior Behavior { get; set; } = UploadBehavior.Ok;
        public Func<Task<bool>>? DeleteOverride { get; set; }
        public Func<Task>? BeforeDestinationUpload { get; set; }
        public int UploadCount { get; private set; }
        public int DestinationUploadCount { get; private set; }
        public string Bucket => "test";
        public Dictionary<string, byte[]> StoredObjects { get; } = new(StringComparer.Ordinal);

        public Task<bool> UploadAsync(IFormFile file, string path, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public async Task<bool> UploadAsync(
            Stream stream, string path, long contentLength, string contentType, CancellationToken ct = default)
        {
            UploadCount++;
            var destination = !path.StartsWith(
                GeneratedArtifactService.StoragePrefix, StringComparison.Ordinal);
            if (destination)
            {
                DestinationUploadCount++;
                if (BeforeDestinationUpload != null)
                    await BeforeDestinationUpload();
            }
            if (!UploadSucceeds || Behavior == UploadBehavior.FalseBeforeWrite)
                return false;
            using var output = new MemoryStream();
            await stream.CopyToAsync(output, ct);
            StoredObjects[path] = output.ToArray();
            if (Behavior == UploadBehavior.WriteThenFalse)
                return false;
            if (Behavior == UploadBehavior.WriteThenThrow)
                throw new InvalidOperationException("upload dropped after write");
            return true;
        }
        public Task<bool> UploadFromUriAsync(string uri, string path, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<FileUploadResult?> UploadFromUriWithMetadataAsync(
            string uri, string path, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<bool> CopyAsync(string sourcePath, string destinationPath, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public async Task<bool> DeleteAsync(string path, CancellationToken ct = default)
        {
            if (DeleteOverride != null)
            {
                var result = await DeleteOverride();
                if (result)
                    StoredObjects.Remove(path);
                return result;
            }
            StoredObjects.Remove(path);
            return true;
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
}
