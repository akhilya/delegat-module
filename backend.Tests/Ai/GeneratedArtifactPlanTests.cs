using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Delegat;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Delegat.Models.Ai;
using Delegat.Services.Ai;
using Delegat.Services.Ai.Agent;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests.Ai;

/// <summary>
/// Plan-level seams: the shared proposal validator, the chat-only dispatch flag,
/// and the catalog surface. Staging is real (InMemory DB + fake storage); the
/// sandbox itself is never contacted.
/// </summary>
[Collection("EnvVars")]
public class GeneratedArtifactPlanTests
{
    private static JsonElement Step(string tool, string argsJson)
    {
        using var doc = JsonDocument.Parse(
            $$"""{"tool":"{{tool}}","description":"do it","args":{{argsJson}}}""");
        return doc.RootElement.Clone();
    }

    private static GeneratedArtifactService Artifacts(
        DataContext db, IStorageService storage, IPermissionService? permissions = null) =>
        new(
            db,
            storage,
            new Delegat.Services.QuotaService(TestDbFactory.CreateUnitOfWork(db), db),
            new AgentSessionResolver(
                new FakeCurrentUserService(), permissions ?? new GrantAllPermissions(), db,
                new HttpContextAccessor()),
            NullLogger<GeneratedArtifactService>.Instance);

    private static AgentToolHost Host(DataContext db, IStorageService storage, GeneratedArtifactService? artifacts = null)
    {
        var catalog = new CatalogSearchService(db);
        var sql = new SqlReadonlyService(db);
        var knowledge = new KnowledgeIndexService(db, storage, NullLogger<KnowledgeIndexService>.Instance);
        var uow = TestDbFactory.CreateUnitOfWork(db);
        var uploads = new AgentFileUploadService(db, uow, storage, new Delegat.Services.QuotaService(uow, db));
        var platform = new AgentPlatformGateway(uploads, new RecordingInvoker());
        var python = new PythonSandboxClient(
            db, new ThrowingHttpFactory(), NullLogger<PythonSandboxClient>.Instance,
            new HttpContextAccessor(), new FakeJwt());
        return new AgentToolHost(
            catalog, sql, knowledge, platform, python,
            new GrantAllPermissions(), db, NullLogger<AgentToolHost>.Instance,
            artifacts: artifacts);
    }

    private static VerifiedGeneratedOutput Out(string name, byte[] bytes) =>
        new(new GeneratedOutputSpec(name, "text/csv"), bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            new MemoryStream(bytes));

    private static string ImportArgs(GeneratedArtifactMetadata meta, int? folderId = null) =>
        $$"""
        {"artifactId":"{{meta.ArtifactId}}","sha256":"{{meta.Sha256}}",
         "length":{{meta.Length}},"expiresAtUtc":"{{meta.ExpiresAtUtc:O}}",
         "destination":"store","productId":null,"folderId":{{(folderId?.ToString() ?? "null")}}}
        """;

    [Fact]
    public void Gate_RequiresPlanConfirmation_ForImportTool()
    {
        Assert.True(AgentChatTurnGate.RequiresPlanConfirmation(
            GeneratedFileContract.ImportToolName, "{}"));
    }

    [Fact]
    public void Catalog_ImportTool_OnlyOnChatSurface()
    {
        Assert.DoesNotContain(AgentToolCatalog.Build(), t => t.name == GeneratedFileContract.ImportToolName);
        var chat = AgentToolCatalog.Build(includeGeneratedArtifacts: true);
        var tool = Assert.Single(chat, t => t.name == GeneratedFileContract.ImportToolName);

        // The provider schema carries the exact nullable-integer shape.
        var schema = AiApiClient.ToolSchemaElement(tool.input_schema);
        Assert.Equal("object", schema.GetProperty("type").GetString());
        var productId = schema.GetProperty("properties").GetProperty("productId");
        Assert.Equal(JsonValueKind.Array, productId.GetProperty("type").ValueKind);
        Assert.Equal(
            new[] { "integer", "null" },
            productId.GetProperty("type").EnumerateArray().Select(e => e.GetString()!).ToArray());
        var required = schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("productId", required);
        Assert.Contains("folderId", required);
    }

    [Fact]
    public async Task Dispatch_ImportTool_WithoutSignedExecutorFlag_Refused()
    {
        await using var db = TestDbFactory.CreateDb();
        var storage = new MemoryStorage();
        var host = Host(db, storage, Artifacts(db, storage));

        var args = """
            {"artifactId":"00000000000000000000000000000000","sha256":"0000000000000000000000000000000000000000000000000000000000000000",
             "length":1,"expiresAtUtc":"2999-01-01T00:00:00Z","destination":"store","productId":null,"folderId":null}
            """;
        // Direct model-loop dispatch (no flag) and the external Agent surface both fail.
        var (result, _) = await host.DispatchAsync(
            GeneratedFileContract.ImportToolName, args, 1, 1, default);
        var json = JsonSerializer.Serialize(result);
        Assert.Contains("confirmed_plan_required", json);
    }

    [Fact]
    public async Task Dispatch_RunPythonOutputs_WithoutFlag_Refused()
    {
        await using var db = TestDbFactory.CreateDb();
        var host = Host(db, new MemoryStorage());
        var (result, _) = await host.DispatchAsync(
            "run_python",
            """{"code":"print(1)","outputs":[{"fileName":"r.csv","contentType":"text/csv"}]}""",
            1, 1, default);
        Assert.Contains("confirmed_plan_required", JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task Dispatch_RunPython_MalformedOutputs_InvalidRequest()
    {
        await using var db = TestDbFactory.CreateDb();
        var host = Host(db, new MemoryStorage(), Artifacts(db, new MemoryStorage()));
        var (result, _) = await host.DispatchAsync(
            "run_python",
            """{"code":"print(1)","outputs":[{"fileName":"../x"}]}""",
            1, 1, default, allowGeneratedArtifacts: true);
        Assert.Contains("ai_artifact_invalid_request", JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task ValidatePlan_GenerationAndImportCombo_Rejected()
    {
        await using var db = TestDbFactory.CreateDb();
        var host = Host(db, new MemoryStorage(), Artifacts(db, new MemoryStorage()));
        var steps = new List<JsonElement>
        {
            Step("run_python", """{"code":"print(1)"}"""),
            Step(GeneratedFileContract.ImportToolName,
                """{"artifactId":"00000000000000000000000000000000","sha256":"0000000000000000000000000000000000000000000000000000000000000000","length":1,"expiresAtUtc":"2999-01-01T00:00:00Z","destination":"store","productId":null,"folderId":null}"""),
        };
        var validation = await host.ValidatePlanAsync(steps, 1, 1, default);
        Assert.NotNull(validation.Rejection);
        Assert.Contains("ai_artifact_invalid_request", validation.Rejection);
        Assert.Empty(validation.GeneratedFileImports);
    }

    [Fact]
    public async Task ValidatePlan_InventedArtifact_Rejected()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();
        var storage = new MemoryStorage();
        var host = Host(db, storage, Artifacts(db, storage));

        var steps = new List<JsonElement>
        {
            Step(GeneratedFileContract.ImportToolName,
                """{"artifactId":"00000000000000000000000000000000","sha256":"0000000000000000000000000000000000000000000000000000000000000000","length":1,"expiresAtUtc":"2999-01-01T00:00:00Z","destination":"store","productId":null,"folderId":null}"""),
        };
        var validation = await host.ValidatePlanAsync(steps, store.Id, user.Id, default);
        Assert.NotNull(validation.Rejection);
        Assert.Contains("ai_artifact_not_found", validation.Rejection);
    }

    [Fact]
    public async Task ValidatePlan_MalformedImportArgs_Rejected()
    {
        await using var db = TestDbFactory.CreateDb();
        var host = Host(db, new MemoryStorage(), Artifacts(db, new MemoryStorage()));
        var steps = new List<JsonElement>
        {
            // Missing folderId entirely: every field must be explicit.
            Step(GeneratedFileContract.ImportToolName,
                """{"artifactId":"00000000000000000000000000000000","sha256":"0000000000000000000000000000000000000000000000000000000000000000","length":1,"expiresAtUtc":"2999-01-01T00:00:00Z","destination":"store","productId":null}"""),
        };
        var validation = await host.ValidatePlanAsync(steps, 1, 1, default);
        Assert.Contains("ai_artifact_invalid_request", validation.Rejection);
    }

    [Fact]
    public async Task ValidatePlan_RealArtifact_DerivesServerApprovals()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var folder = new StoreFolder { name = "gen", storeId = store.Id };
        db.storeFolders.Add(folder);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var artifacts = Artifacts(db, storage);
        var metas = await artifacts.StageCapturedAsync(store.Id, user.Id, 1,
            [Out("r.csv", [1, 2, 3])], default);

        var host = Host(db, storage, artifacts);
        var steps = new List<JsonElement>
        {
            Step(GeneratedFileContract.ImportToolName, ImportArgs(metas[0], folder.Id)),
        };
        var validation = await host.ValidatePlanAsync(steps, store.Id, user.Id, default);

        Assert.Null(validation.Rejection);
        var approval = Assert.Single(validation.GeneratedFileImports);
        Assert.Equal(0, approval.Step);
        Assert.Equal(metas[0].ArtifactId, approval.ArtifactId);
        Assert.Equal("r.csv", approval.FileName);
        Assert.Equal(metas[0].Sha256, approval.Sha256);
        Assert.Equal(folder.Id, approval.FolderId);
    }

    [Fact]
    public async Task Dispatch_ImportTool_SignedExecutorFlag_RunsImporter()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var artifacts = Artifacts(db, storage);
        var bytes = new byte[] { 4, 2 };
        var metas = await artifacts.StageCapturedAsync(store.Id, user.Id, 1,
            [Out("r.csv", bytes)], default);

        var host = Host(db, storage, artifacts);
        var (result, summary) = await host.DispatchAsync(
            GeneratedFileContract.ImportToolName, ImportArgs(metas[0]),
            store.Id, user.Id, default, allowGeneratedArtifacts: true);

        var json = JsonSerializer.Serialize(result);
        Assert.Contains("FileId", json);
        Assert.Contains("/api/store-storage/files/", json);
        Assert.Contains("Imported", summary);
        Assert.Single(await db.storeFiles.ToListAsync());
    }

    [Fact]
    public void Catalog_RunPythonOutputs_OnlyWhenCaptureConfigured()
    {
        var prevAi = Environment.GetEnvironmentVariable("AI_ENABLED");
        var prevUrl = Environment.GetEnvironmentVariable("SANDBOX_URL");
        var prevSecret = Environment.GetEnvironmentVariable("SANDBOX_SECRET");
        try
        {
            Environment.SetEnvironmentVariable("AI_ENABLED", "true");
            Environment.SetEnvironmentVariable("SANDBOX_URL", "http://sandbox:8080");
            Environment.SetEnvironmentVariable("SANDBOX_SECRET", null);
            var noSecret = AgentToolCatalog.Build(includeGeneratedArtifacts: true)
                .Single(t => t.name == "run_python");
            Assert.False(noSecret.input_schema.properties!.ContainsKey("outputs"));
            Assert.DoesNotContain("import_generated_file", noSecret.description);

            Environment.SetEnvironmentVariable("SANDBOX_SECRET", "s3cret");
            var withSecret = AgentToolCatalog.Build(includeGeneratedArtifacts: true)
                .Single(t => t.name == "run_python");
            Assert.True(withSecret.input_schema.properties!.ContainsKey("outputs"));
            Assert.Contains("client.output_path", withSecret.description);

            // The external MCP surface (default build) never advertises capture.
            var external = AgentToolCatalog.Build().Single(t => t.name == "run_python");
            Assert.False(external.input_schema.properties!.ContainsKey("outputs"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AI_ENABLED", prevAi);
            Environment.SetEnvironmentVariable("SANDBOX_URL", prevUrl);
            Environment.SetEnvironmentVariable("SANDBOX_SECRET", prevSecret);
        }
    }

    private sealed class RecordingInvoker : ICabinetRequestInvoker
    {
        public Task<(object result, string summary)> InvokeAsync(
            int storeId, int userId, string method, string apiPath,
            IReadOnlyDictionary<string, string> query, JsonElement? body, CancellationToken ct) =>
            Task.FromResult<(object, string)>((new { ok = true }, "ok"));
    }

    [Fact]
    public async Task ValidatePlan_MultiStepOutputsPlan_Rejected()
    {
        var prevAi = Environment.GetEnvironmentVariable("AI_ENABLED");
        var prevUrl = Environment.GetEnvironmentVariable("SANDBOX_URL");
        var prevSecret = Environment.GetEnvironmentVariable("SANDBOX_SECRET");
        try
        {
            Environment.SetEnvironmentVariable("AI_ENABLED", "true");
            Environment.SetEnvironmentVariable("SANDBOX_URL", "http://sandbox:8080");
            Environment.SetEnvironmentVariable("SANDBOX_SECRET", "s3cret");
            await using var db = TestDbFactory.CreateDb();
            var data = new TestDataBuilder(db);
            var user = data.AddUser();
            var store = data.AddStore(members: user);
            await data.SaveAsync();
            var host = Host(db, new MemoryStorage(), Artifacts(db, new MemoryStorage()));
            var steps = new[]
            {
                Step("run_python", """{"code":"1","outputs":[{"fileName":"r.csv","contentType":"text/csv"}]}"""),
                Step("run_python", """{"code":"2"}"""),
            };

            var validation = await host.ValidatePlanAsync(steps, store.Id, user.Id, default);
            Assert.NotNull(validation.Rejection);
            Assert.Contains("ai_artifact_invalid_request", validation.Rejection, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AI_ENABLED", prevAi);
            Environment.SetEnvironmentVariable("SANDBOX_URL", prevUrl);
            Environment.SetEnvironmentVariable("SANDBOX_SECRET", prevSecret);
        }
    }

    [Fact]
    public async Task ValidatePlan_Outputs_RequiresCapturePermissions()
    {
        var prevAi = Environment.GetEnvironmentVariable("AI_ENABLED");
        var prevUrl = Environment.GetEnvironmentVariable("SANDBOX_URL");
        var prevSecret = Environment.GetEnvironmentVariable("SANDBOX_SECRET");
        try
        {
            Environment.SetEnvironmentVariable("AI_ENABLED", "true");
            Environment.SetEnvironmentVariable("SANDBOX_URL", "http://sandbox:8080");
            Environment.SetEnvironmentVariable("SANDBOX_SECRET", "s3cret");
            await using var db = TestDbFactory.CreateDb();
            var data = new TestDataBuilder(db);
            var user = data.AddUser();
            var store = data.AddStore(members: user);
            await data.SaveAsync();

            // files.manage without ai.use still fails the capture preflight.
            var limited = Artifacts(db, new MemoryStorage(),
                new StaticPermissions(Permissions.FilesManage));
            var host = Host(db, new MemoryStorage(), limited);
            var steps = new[]
            {
                Step("run_python", """{"code":"1","outputs":[{"fileName":"r.csv","contentType":"text/csv"}]}"""),
            };

            var validation = await host.ValidatePlanAsync(steps, store.Id, user.Id, default);
            Assert.NotNull(validation.Rejection);
            Assert.Contains("forbidden", validation.Rejection, StringComparison.Ordinal);
            Assert.Empty(validation.GeneratedFileImports);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AI_ENABLED", prevAi);
            Environment.SetEnvironmentVariable("SANDBOX_URL", prevUrl);
            Environment.SetEnvironmentVariable("SANDBOX_SECRET", prevSecret);
        }
    }

    [Fact]
    public async Task ValidatePlan_StartedFence_RejectsAsUnknownNotQuota()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var storage = new MemoryStorage();
        var artifacts = Artifacts(db, storage);
        var metas = await artifacts.StageCapturedAsync(store.Id, user.Id, 1,
            [Out("r.csv", [1])], default);
        var artifact = await db.aiGeneratedArtifacts.SingleAsync();
        var request = new GeneratedFileImportRequest(
            metas[0].ArtifactId, metas[0].Sha256, metas[0].Length, metas[0].ExpiresAtUtc,
            "store", null, null);
        var binding = (string)typeof(GeneratedArtifactService)
            .GetMethod("ComputeBindingId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [request, artifact, store.Id, user.Id])!;
        db.aiGeneratedArtifactImports.Add(new AiGeneratedArtifactImport
        {
            Id = binding, storeId = store.Id, userId = user.Id,
            artifactId = metas[0].ArtifactId, length = metas[0].Length,
            destination = "store", destinationKey = "k", state = "started",
            createdAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var host = Host(db, storage, artifacts);
        var steps = new[]
        {
            Step("import_generated_file", ImportArgs(metas[0])),
        };
        var validation = await host.ValidatePlanAsync(steps, store.Id, user.Id, default);
        Assert.NotNull(validation.Rejection);
        Assert.Contains("ai_artifact_import_unknown", validation.Rejection, StringComparison.Ordinal);
        Assert.Contains("\"outcomeUnknown\":true", validation.Rejection, StringComparison.Ordinal);
    }

    [Fact]
    public void OutputFileName_Utf8Boundaries_Enforced()
    {
        const string emoji = "\U0001F600"; // 4 UTF-8 bytes, 2 UTF-16 code units
        var cases = new (string Name, bool Valid)[]
        {
            (new string('a', 180), true),
            (new string('a', 181), false),
            (new string('Ж', 125) + ".csv", true),   // 254 UTF-8 bytes
            (new string('Ж', 125) + "a.csv", true),  // 255 UTF-8 bytes
            (new string('Ж', 126) + ".csv", false),  // 256 UTF-8 bytes
            (new string('Ж', 130) + ".csv", false),  // 264 UTF-8 bytes
            (string.Concat(Enumerable.Repeat(emoji, 62)) + "abc.csv", true),   // 248+7 = 255
            (string.Concat(Enumerable.Repeat(emoji, 62)) + "abcX.csv", false), // 256
            (new string('a', 155) + string.Concat(Enumerable.Repeat(emoji, 25)), false), // 255 B but 205 UTF-16 units
            ("lone\uD800surrogate", false),
            ("a\u0085b.csv", false), // C1 control
        };
        foreach (var (name, valid) in cases)
        {
            Assert.True(
                GeneratedFileContract.IsValidOutputFileName(name) == valid,
                $"expected {(valid ? "valid" : "invalid")} for byteLength={Encoding.UTF8.GetByteCount(name)}");
        }
    }

    [Theory]
    [InlineData("""{"outputs":[{"fileName":"\uD800.csv"}]}""")]
    [InlineData("""{"outputs":[{"fileName":"\uDC00.csv"}]}""")]
    [InlineData("""{"outputs":[{"fileName":"a.csv","contentType":"text/\uD800"}]}""")]
    [InlineData("""{"outputs":[{"\uD800":"a.csv"}]}""")]
    public void ParseOutputs_UnmatchedJsonSurrogate_Rejected(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.False(GeneratedFileContract.TryParseOutputs(document.RootElement, out var outputs));
        Assert.Null(outputs);
    }

    [Fact]
    public async Task ValidatePlan_UnmatchedJsonSurrogate_InvalidRequest()
    {
        var prevAi = Environment.GetEnvironmentVariable("AI_ENABLED");
        var prevUrl = Environment.GetEnvironmentVariable("SANDBOX_URL");
        var prevSecret = Environment.GetEnvironmentVariable("SANDBOX_SECRET");
        try
        {
            Environment.SetEnvironmentVariable("AI_ENABLED", "true");
            Environment.SetEnvironmentVariable("SANDBOX_URL", "http://sandbox:8080");
            Environment.SetEnvironmentVariable("SANDBOX_SECRET", "s3cret");
            await using var db = TestDbFactory.CreateDb();
            var data = new TestDataBuilder(db);
            var user = data.AddUser();
            var store = data.AddStore(members: user);
            await data.SaveAsync();
            var host = Host(db, new MemoryStorage(), Artifacts(db, new MemoryStorage()));
            var validation = await host.ValidatePlanAsync(
                new[] { Step("run_python", """{"code":"print(1)","outputs":[{"fileName":"\uD800.csv"}]}""") },
                store.Id, user.Id, default);
            Assert.NotNull(validation.Rejection);
            Assert.Contains("ai_artifact_invalid_request", validation.Rejection, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AI_ENABLED", prevAi);
            Environment.SetEnvironmentVariable("SANDBOX_URL", prevUrl);
            Environment.SetEnvironmentVariable("SANDBOX_SECRET", prevSecret);
        }
    }

    [Theory]
    [InlineData(126)]
    [InlineData(130)]
    public async Task ValidatePlan_OutputsNameBeyondUtf8Cap_Rejected(int cyrillicCount)
    {
        var prevAi = Environment.GetEnvironmentVariable("AI_ENABLED");
        var prevUrl = Environment.GetEnvironmentVariable("SANDBOX_URL");
        var prevSecret = Environment.GetEnvironmentVariable("SANDBOX_SECRET");
        try
        {
            Environment.SetEnvironmentVariable("AI_ENABLED", "true");
            Environment.SetEnvironmentVariable("SANDBOX_URL", "http://sandbox:8080");
            Environment.SetEnvironmentVariable("SANDBOX_SECRET", "s3cret");
            await using var db = TestDbFactory.CreateDb();
            var data = new TestDataBuilder(db);
            var user = data.AddUser();
            var store = data.AddStore(members: user);
            await data.SaveAsync();
            var host = Host(db, new MemoryStorage(), Artifacts(db, new MemoryStorage()));
            var name = new string('Ж', cyrillicCount) + ".csv"; // 256/264 UTF-8 bytes
            var args = JsonSerializer.Serialize(new
            {
                code = "1",
                outputs = new[] { new { fileName = name, contentType = "text/csv" } },
            });

            var validation = await host.ValidatePlanAsync(
                new[] { Step("run_python", args) }, store.Id, user.Id, default);
            Assert.NotNull(validation.Rejection);
            Assert.Contains("ai_artifact_invalid_request", validation.Rejection, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AI_ENABLED", prevAi);
            Environment.SetEnvironmentVariable("SANDBOX_URL", prevUrl);
            Environment.SetEnvironmentVariable("SANDBOX_SECRET", prevSecret);
        }
    }

    private sealed class MemoryStorage : IStorageService
    {
        public string Bucket => "test";
        public Dictionary<string, byte[]> StoredObjects { get; } = new(StringComparer.Ordinal);
        public Task<bool> UploadAsync(IFormFile file, string path, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public async Task<bool> UploadAsync(Stream stream, string path, long contentLength, string contentType, CancellationToken ct = default)
        {
            using var output = new MemoryStream();
            await stream.CopyToAsync(output, ct);
            StoredObjects[path] = output.ToArray();
            return true;
        }
        public Task<bool> UploadFromUriAsync(string uri, string path, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<FileUploadResult?> UploadFromUriWithMetadataAsync(string uri, string path, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<bool> CopyAsync(string sourcePath, string destinationPath, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<bool> DeleteAsync(string path, CancellationToken ct = default)
        {
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
        public Task<string> GetPresignedUrlAsync(string path, int expirySeconds = 3600) => Task.FromResult(path);
        public Task<string> GetPublicPresignedUrlAsync(string path, int expirySeconds = 3600) => Task.FromResult(path);
        public Task<List<string>> ListObjectsByPrefixAsync(string prefix, CancellationToken ct = default) =>
            Task.FromResult(new List<string>());
    }
}
