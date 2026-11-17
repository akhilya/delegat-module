using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Delegat;
using Delegat.Infrastructure.Auth;
using Delegat.Models.Marketplace;
using Delegat.Infrastructure.Storage;
using Delegat.Services.Ai.Agent;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests.Ai;

/// <summary>
/// Client-side artifact protocol: health-based candidate selection, the single
/// POST, and byte-verified ZIP ingest. The sidecar itself is a stub handler.
/// </summary>
[Collection("sandbox-env")]
public class PythonGeneratedArtifactTests
{
    private sealed class EnvScope : IDisposable
    {
        private readonly Dictionary<string, string?> _previous = new();

        public EnvScope Set(string name, string? value)
        {
            if (!_previous.ContainsKey(name))
                _previous[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
            return this;
        }

        public void Dispose()
        {
            foreach (var (name, value) in _previous)
                Environment.SetEnvironmentVariable(name, value);
        }
    }

    private static EnvScope SandboxEnv(params (string Url, string? Stack)[] _) =>
        new EnvScope()
            .Set("AI_ENABLED", "true")
            .Set("SANDBOX_URL", "http://fake-sandbox:8080")
            .Set("SANDBOX_SECRET", "test-secret")
            .Set("STACK_ID", null)
            .Set("INSTANCE_ID", null)
            .Set("MYSQL_SERVER", null)
            .Set("MINIO_ENDPOINT", null);

    private static PythonSandboxClient Client(
        DataContext db, StubHttpHandler handler, GeneratedArtifactService? artifacts = null)
    {
        return new PythonSandboxClient(
            db,
            new StubFactory(handler),
            NullLogger<PythonSandboxClient>.Instance,
            new HttpContextAccessor(),
            new FakeJwt(),
            artifacts: artifacts);
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

    private static JsonElement ResultJson(object result) =>
        JsonSerializer.SerializeToElement(result);

    private static HttpResponseMessage Health(int protocol = 1) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { ok = true, artifactProtocol = protocol }),
                Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage Zip(byte[] zip) =>
        new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(zip)
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip") },
            },
        };

    private static byte[] BuildZip(
        IReadOnlyList<(string FileName, string ContentType, byte[] Bytes)> outputs,
        string stdout = "done",
        Func<int, string>? shaOverride = null,
        string? manifestError = null)
    {
        using var mem = new MemoryStream();
        using (var zip = new ZipArchive(mem, ZipArchiveMode.Create, leaveOpen: true))
        {
            var metas = new List<object>();
            for (var i = 0; i < outputs.Count; i++)
            {
                var entry = zip.CreateEntry($"outputs/{i}", CompressionLevel.NoCompression);
                using var writer = entry.Open();
                writer.Write(outputs[i].Bytes);
                var sha = shaOverride?.Invoke(i)
                          ?? Convert.ToHexString(SHA256.HashData(outputs[i].Bytes)).ToLowerInvariant();
                metas.Add(new
                {
                    index = i,
                    fileName = outputs[i].FileName,
                    contentType = outputs[i].ContentType,
                    length = outputs[i].Bytes.Length,
                    sha256 = sha,
                });
            }
            var manifest = JsonSerializer.Serialize(new
            {
                protocol = 1,
                result = new { stdout, stderr = "", exitCode = 0, error = manifestError },
                outputs = metas,
            });
            var manifestEntry = zip.CreateEntry("manifest.json", CompressionLevel.NoCompression);
            using var ms = manifestEntry.Open();
            ms.Write(Encoding.UTF8.GetBytes(manifest));
        }
        return mem.ToArray();
    }

    [Fact]
    public async Task Outputs_OldSidecar_FailsBeforeAnyPost()
    {
        using var env = SandboxEnv();
        await using var db = TestDbFactory.CreateDb();
        var handler = new StubHttpHandler
        {
            Responder = req => req.RequestUri!.AbsolutePath == "/health"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"ok":true}""", Encoding.UTF8, "application/json"),
                }
                : throw new InvalidOperationException("POST must never run"),
        };
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();
        var client = Client(db, handler, Artifacts(db, new MemoryStorage()));

        var (result, _) = await client.RunAsync(store.Id, user.Id, "print(1)",
            [new GeneratedOutputSpec("r.csv", "text/csv")], default);

        Assert.Equal("ai_artifact_protocol_unavailable",
            ResultJson(result).GetProperty("error").GetString());
        Assert.DoesNotContain(handler.Requests, r => r.Method == "POST");
    }

    [Fact]
    public async Task Outputs_ZipRoundTrip_StagesMetadataOnly()
    {
        using var env = SandboxEnv();
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var csv = Encoding.UTF8.GetBytes("a,b\n1,2\n");
        var bin = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        var storage = new MemoryStorage();
        var zip = BuildZip(
            [("r.csv", "text/csv", csv), ("b.bin", "application/octet-stream", bin)],
            stdout: "report ready");
        var handler = new StubHttpHandler
        {
            Responder = req => req.RequestUri!.AbsolutePath switch
            {
                "/health" => Health(),
                "/run-artifacts" => Zip(zip),
                _ => throw new InvalidOperationException("unexpected request"),
            },
        };
        var client = Client(db, handler, Artifacts(db, storage));

        var (result, _) = await client.RunAsync(store.Id, user.Id, "code",
            [
                new GeneratedOutputSpec("r.csv", "text/csv"),
                new GeneratedOutputSpec("b.bin", "application/octet-stream"),
            ], default);

        var json = ResultJson(result);
        Assert.Equal(0, json.GetProperty("exitCode").GetInt32());
        Assert.Equal("report ready", json.GetProperty("stdout").GetString());
        var artifacts = json.GetProperty("artifacts");
        Assert.Equal(2, artifacts.GetArrayLength());
        Assert.Equal("r.csv", artifacts[0].GetProperty("FileName").GetString());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(csv)).ToLowerInvariant(),
            artifacts[0].GetProperty("Sha256").GetString());
        Assert.Equal(csv.Length, artifacts[0].GetProperty("Length").GetInt64());
        foreach (var item in artifacts.EnumerateArray())
        {
            var artifactId = item.GetProperty("ArtifactId").GetString()!;
            Assert.Equal(
                GeneratedFileContract.TemporaryDownloadUrl(artifactId),
                item.GetProperty("DownloadUrl").GetString());
        }

        // No bytes, base64, storage keys, or paths in the model-facing payload.
        var serialized = json.ToString();
        Assert.DoesNotContain("storageKey", serialized);
        Assert.DoesNotContain("__ai-generated", serialized);
        Assert.DoesNotContain(Convert.ToBase64String(csv), serialized);

        Assert.Equal(2, await db.aiGeneratedArtifacts.CountAsync());
        Assert.All(await db.aiGeneratedArtifacts.ToListAsync(), a => Assert.Equal("ready", a.state));
        Assert.True(storage.StoredObjects.Keys.All(k => k.StartsWith("__ai-generated/")));

        // Exactly one health probe and one POST: no retried execution.
        Assert.Single(handler.Requests, r => r.Method == "GET");
        Assert.Single(handler.Requests, r => r.Method == "POST");

        // The audited run row exists before the artifacts bind to it.
        var run = await db.aiSandboxRuns.SingleAsync();
        Assert.Equal(0, run.exitCode);
        Assert.All(await db.aiGeneratedArtifacts.ToListAsync(),
            a => Assert.Equal(run.Id, a.sandboxRunId));
    }

    [Fact]
    public async Task Outputs_TamperedManifestHash_RejectedBeforeReady()
    {
        using var env = SandboxEnv();
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();

        var csv = Encoding.UTF8.GetBytes("data");
        var zip = BuildZip([("r.csv", "text/csv", csv)], shaOverride: _ => new string('0', 64));
        var handler = new StubHttpHandler
        {
            Responder = req => req.RequestUri!.AbsolutePath switch
            {
                "/health" => Health(),
                "/run-artifacts" => Zip(zip),
                _ => throw new InvalidOperationException("unexpected request"),
            },
        };
        var client = Client(db, handler, Artifacts(db, new MemoryStorage()));

        var (result, _) = await client.RunAsync(store.Id, user.Id, "code",
            [new GeneratedOutputSpec("r.csv", "text/csv")], default);

        Assert.Equal("ai_artifact_capture_unknown",
            ResultJson(result).GetProperty("error").GetString());
        Assert.Empty(await db.aiGeneratedArtifacts.ToListAsync());
    }

    [Fact]
    public async Task Outputs_DroppedPost_NeverRetriesSecondCandidate()
    {
        using var env = new EnvScope()
            .Set("AI_ENABLED", "true")
            .Set("SANDBOX_URL", "http://fake-one:8080")
            .Set("SANDBOX_SECRET", "test-secret")
            .Set("STACK_ID", "abcdef0123456789")
            .Set("INSTANCE_ID", null)
            .Set("MYSQL_SERVER", null)
            .Set("MINIO_ENDPOINT", null);
        await using var db = TestDbFactory.CreateDb();
        var handler = new StubHttpHandler
        {
            Responder = req =>
            {
                if (req.Method == HttpMethod.Get)
                    return Health();
                // The response is lost mid-flight: the script may have run.
                throw new HttpRequestException("connection dropped");
            },
        };
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();
        var client = Client(db, handler, Artifacts(db, new MemoryStorage()));

        var (result, _) = await client.RunAsync(store.Id, user.Id, "code",
            [new GeneratedOutputSpec("r.csv", "text/csv")], default);

        Assert.Equal("ai_artifact_capture_unknown",
            ResultJson(result).GetProperty("error").GetString());
        // One POST total across every candidate host: side effects never rerun.
        Assert.Single(handler.Requests, r => r.Method == "POST");
        Assert.All(handler.Requests.Where(r => r.Method == "GET"),
            r => Assert.StartsWith("http://fake-one:8080", r.Url));
    }

    [Fact]
    public async Task Outputs_NoSecret_CaptureNotConfigured()
    {
        using var env = SandboxEnv().Set("SANDBOX_SECRET", "");
        await using var db = TestDbFactory.CreateDb();
        var handler = new StubHttpHandler
        {
            Responder = _ => throw new InvalidOperationException("no request expected"),
        };
        var client = Client(db, handler, Artifacts(db, new MemoryStorage()));

        var (result, _) = await client.RunAsync(1, 1, "code",
            [new GeneratedOutputSpec("r.csv", "text/csv")], default);

        Assert.Equal("ai_artifact_capture_not_configured",
            ResultJson(result).GetProperty("error").GetString());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PlainRun_StillJsonCompatible()
    {
        using var env = SandboxEnv();
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();
        var handler = new StubHttpHandler
        {
            Responder = req => req.RequestUri!.AbsolutePath switch
            {
                "/health" => Health(),
                "/run" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"exitCode":0,"stdout":"hi","stderr":"","error":null}""",
                        Encoding.UTF8, "application/json"),
                },
                _ => throw new InvalidOperationException("unexpected request"),
            },
        };
        var client = Client(db, handler);

        var (result, _) = await client.RunAsync(store.Id, user.Id, "print('hi')", default);

        var json = ResultJson(result);
        Assert.Equal("hi", json.GetProperty("stdout").GetString());
        Assert.Equal(0, json.GetProperty("exitCode").GetInt32());
        Assert.Single(handler.Requests, r => r.Method == "POST");
        Assert.EndsWith("/run", handler.Requests.Single(r => r.Method == "POST").Url);
    }

    [Fact]
    public async Task Outputs_NoCapturePermission_RefusedBeforeAnyRequest()
    {
        using var env = SandboxEnv();
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();
        var handler = new StubHttpHandler
        {
            Responder = _ => throw new InvalidOperationException("no request expected"),
        };
        var limited = Artifacts(db, new MemoryStorage(),
            new StaticPermissions(Permissions.FilesManage));
        var client = Client(db, handler, limited);

        var (result, _) = await client.RunAsync(store.Id, user.Id, "code",
            [new GeneratedOutputSpec("r.csv", "text/csv")], default);

        Assert.Equal("forbidden", ResultJson(result).GetProperty("error").GetString());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Outputs_ManifestErrorMarked_NotStagedAsSuccess()
    {
        using var env = SandboxEnv();
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();
        var zip = BuildZip([("r.csv", "text/csv", [1])], manifestError: "script exploded");
        var handler = new StubHttpHandler
        {
            Responder = req => req.RequestUri!.AbsolutePath switch
            {
                "/health" => Health(),
                "/run-artifacts" => Zip(zip),
                _ => throw new InvalidOperationException("unexpected request"),
            },
        };
        var client = Client(db, handler, Artifacts(db, new MemoryStorage()));

        var (result, _) = await client.RunAsync(store.Id, user.Id, "code",
            [new GeneratedOutputSpec("r.csv", "text/csv")], default);

        var json = ResultJson(result);
        Assert.Equal("script exploded", json.GetProperty("error").GetString());
        Assert.Equal("generated_artifact", json.GetProperty("errorClass").GetString());
        Assert.False(json.GetProperty("retryable").GetBoolean());
        Assert.True(json.GetProperty("outcomeUnknown").GetBoolean());
        Assert.Empty(await db.aiGeneratedArtifacts.ToListAsync());
    }

    [Fact]
    public async Task Outputs_NonSuccessZipStatus_IsCaptureFailed()
    {
        using var env = SandboxEnv();
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();
        var zip = BuildZip([("r.csv", "text/csv", [1])]);
        var handler = new StubHttpHandler
        {
            Responder = req => req.RequestUri!.AbsolutePath switch
            {
                "/health" => Health(),
                "/run-artifacts" => new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new ByteArrayContent(zip)
                    {
                        Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip") },
                    },
                },
                _ => throw new InvalidOperationException("unexpected request"),
            },
        };
        var client = Client(db, handler, Artifacts(db, new MemoryStorage()));

        var (result, _) = await client.RunAsync(store.Id, user.Id, "code",
            [new GeneratedOutputSpec("r.csv", "text/csv")], default);

        var json = ResultJson(result);
        Assert.Equal("ai_artifact_capture_failed", json.GetProperty("error").GetString());
        Assert.Equal("generated_artifact", json.GetProperty("errorClass").GetString());
        Assert.False(json.GetProperty("retryable").GetBoolean());
        Assert.True(json.GetProperty("outcomeUnknown").GetBoolean());
        Assert.Empty(await db.aiGeneratedArtifacts.ToListAsync());
    }

    [Fact]
    public async Task Outputs_ManifestSecrets_RedactedBeforeAuditAndResult()
    {
        using var env = SandboxEnv();
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        const string secret = "shop-secret-value-12345";
        db.marketplaceConnections.Add(new MarketplaceConnection
        {
            storeId = store.Id,
            name = "s",
            adapterKey = "avito",
            isActive = true,
            credentialsJson = $"{{\"clientSecret\":\"{secret}\"}}",
        });
        await data.SaveAsync();

        var zip = BuildZip([("r.csv", "text/csv", [1])],
            stdout: "token=" + secret + " tail");
        var handler = new StubHttpHandler
        {
            Responder = req => req.RequestUri!.AbsolutePath switch
            {
                "/health" => Health(),
                "/run-artifacts" => Zip(zip),
                _ => throw new InvalidOperationException("unexpected request"),
            },
        };
        var client = Client(db, handler, Artifacts(db, new MemoryStorage()));

        var (result, _) = await client.RunAsync(store.Id, user.Id, "code",
            [new GeneratedOutputSpec("r.csv", "text/csv")], default);

        var json = ResultJson(result);
        Assert.DoesNotContain(secret, json.ToString());
        Assert.DoesNotContain(secret, json.GetProperty("stdout").GetString()!);
        var run = await db.aiSandboxRuns.SingleAsync();
        Assert.DoesNotContain(secret, run.stdoutPreview ?? "");
        Assert.Single(await db.aiGeneratedArtifacts.ToListAsync()); // capture still staged
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        public List<(string Method, string Url)> Requests { get; } = [];
        public Func<HttpRequestMessage, HttpResponseMessage>? Responder { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method.Method, request.RequestUri!.ToString()));
            return Task.FromResult(
                Responder?.Invoke(request)
                ?? throw new HttpRequestException("no responder"));
        }
    }

    private sealed class StubFactory(StubHttpHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
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
