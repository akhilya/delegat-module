using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Configuration;
using Delegat.Infrastructure.Security;
using Delegat.Models.Ai;
using Delegat.Services.Marketplace;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services.Ai.Agent;

public sealed class PythonSandboxClient
{
    private readonly DataContext _db;
    private readonly IHttpClientFactory _http;
    private readonly IHttpContextAccessor _httpContext;
    private readonly IJwtService _jwt;
    private readonly ILogger<PythonSandboxClient> _logger;
    private readonly EncryptionService? _encryption;
    private readonly IMarketplaceAdapterRegistry? _adapters;
    private readonly GeneratedArtifactService? _artifacts;

    public PythonSandboxClient(
        DataContext db,
        IHttpClientFactory http,
        ILogger<PythonSandboxClient> logger,
        IHttpContextAccessor httpContext,
        IJwtService jwt,
        EncryptionService? encryption = null,
        IMarketplaceAdapterRegistry? adapters = null,
        GeneratedArtifactService? artifacts = null)
    {
        _db = db;
        _http = http;
        _logger = logger;
        _httpContext = httpContext;
        _jwt = jwt;
        _encryption = encryption;
        _adapters = adapters;
        _artifacts = artifacts;
    }

    /// <summary>
    /// Sidecar is on only while AI is on. Address is an explicit <c>SANDBOX_URL</c>
    /// (local compose) or the Control Plane container name <c>d-{stack}-sandbox</c>
    /// from <c>STACK_ID</c> / <c>INSTANCE_ID</c> / <c>mysql-{stack}</c>. Alias DNS is not used.
    /// </summary>
    public static bool IsConfigured() => FeatureFlags.IsAiEnabled() && CandidateUrls().Count > 0;

    /// <summary>
    /// Capture additionally requires the configured secret: /run-artifacts fails
    /// closed without it, so declaring outputs then is never viable.
    /// </summary>
    public static bool ArtifactCaptureConfigured() =>
        IsConfigured() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SANDBOX_SECRET"));

    internal static IReadOnlyList<string> CandidateUrls()
    {
        if (!FeatureFlags.IsAiEnabled())
            return [];

        var list = new List<string>();
        AddUrl(list, Environment.GetEnvironmentVariable("SANDBOX_URL"));

        var stackId = ResolveStackId();
        if (string.IsNullOrEmpty(stackId))
            return list;

        // Older stacks use the full instance hex as the container name. Current
        // Control Plane stacks use the first 12. Both are container_name, not aliases.
        AddUrl(list, $"http://d-{stackId}-sandbox:8080");
        if (stackId.Length > 12)
            AddUrl(list, $"http://d-{stackId[..12]}-sandbox:8080");
        return list;
    }

    internal static string? ResolveStackId()
    {
        var stack = Environment.GetEnvironmentVariable("STACK_ID")?.Trim();
        if (!string.IsNullOrEmpty(stack))
            return stack.ToLowerInvariant();

        var instanceId = Environment.GetEnvironmentVariable("INSTANCE_ID")?.Trim();
        if (!string.IsNullOrEmpty(instanceId))
        {
            // Control Plane writes inst_{guid:N}. That suffix is the full Docker stack id
            // (container d-{id}-sandbox), not the first 12 characters of "inst" + hex.
            var inst = Regex.Match(instanceId, @"^inst_([a-fA-F0-9]{8,})$");
            if (inst.Success)
                return inst.Groups[1].Value.ToLowerInvariant();

            if (Guid.TryParse(instanceId, out var guid))
                return guid.ToString("N")[..12];

            var cleaned = Regex.Replace(instanceId.ToLowerInvariant(), @"[^a-z0-9]+", "");
            if (cleaned.Length >= 8)
                return cleaned.Length <= 12 ? cleaned : cleaned[..12];
        }

        return StackIdFromServiceHost(Environment.GetEnvironmentVariable("MYSQL_SERVER"))
            ?? StackIdFromServiceHost(Environment.GetEnvironmentVariable("MINIO_ENDPOINT"));
    }

    /// <summary>
    /// Control Plane sets <c>MYSQL_SERVER=mysql-{stack}</c> and <c>MINIO_ENDPOINT=minio-{stack}:9000</c>
    /// even when <c>STACK_ID</c> is not copied into the API process.
    /// </summary>
    internal static string? StackIdFromServiceHost(string? hostPort)
    {
        if (string.IsNullOrWhiteSpace(hostPort))
            return null;
        var host = hostPort.Trim().Split(':')[0];
        var match = Regex.Match(host, @"^(?:mysql|minio)-([a-z0-9]{8,})$", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    internal static string ResolveAgentBaseUrl()
    {
        var explicitUrl = Environment.GetEnvironmentVariable("SANDBOX_AGENT_BASE_URL")?.Trim();
        var stackId = ResolveStackId();
        if (!string.IsNullOrEmpty(explicitUrl))
        {
            // An alias such as api-{stack} is not required. The API container name is enough.
            if (!string.IsNullOrEmpty(stackId)
                && explicitUrl.Contains($"api-{stackId}", StringComparison.OrdinalIgnoreCase)
                && !explicitUrl.Contains($"d-{stackId}-api", StringComparison.OrdinalIgnoreCase))
                return $"http://d-{stackId}-api:5000";
            return explicitUrl;
        }

        if (!string.IsNullOrEmpty(stackId))
            return $"http://d-{stackId}-api:5000";

        var publicUrl = Environment.GetEnvironmentVariable("PUBLIC_BASE_URL")?.Trim();
        if (!string.IsNullOrEmpty(publicUrl))
            return publicUrl;

        return "http://127.0.0.1:5000";
    }

    public Task<(object result, string summary)> RunAsync(
        int storeId, int userId, string script, CancellationToken ct)
        => RunAsync(storeId, userId, script, null, ct);

    /// <summary>
    /// Runs the script against the first healthy sidecar. Exactly one POST is sent:
    /// read-only /health selects the candidate, and no error after dispatch ever
    /// retries the script on another host. With <paramref name="outputs"/>, the
    /// sidecar must advertise artifactProtocol 1 and returns a bounded ZIP that is
    /// byte-verified before anything is staged; the model only ever sees metadata.
    /// </summary>
    public async Task<(object result, string summary)> RunAsync(
        int storeId,
        int userId,
        string script,
        IReadOnlyList<GeneratedOutputSpec>? outputs,
        CancellationToken ct)
    {
        var urls = CandidateUrls();
        if (urls.Count == 0)
            return (new { error = "sandbox_not_configured" }, "Python sandbox is not configured");

        if (string.IsNullOrWhiteSpace(script))
            return (new { error = "code is required" }, "code is required");
        if (script.Length > 80_000)
            return (new { error = "script too large" }, "script too large");

        var capture = outputs is { Count: > 0 };
        var secret = Environment.GetEnvironmentVariable("SANDBOX_SECRET") ?? "";
        if (capture && (_artifacts == null || secret.Length == 0))
            return (GeneratedFileContract.ErrorResult("ai_artifact_capture_not_configured"),
                "Generated-file capture is not configured");

        // Capture preflight runs before any POST or credential preparation: a
        // script the actor could not stage must never execute.
        if (capture)
        {
            try
            {
                await _artifacts!.ValidateCaptureAsync(storeId, userId, ct);
            }
            catch (GeneratedArtifactException ex)
            {
                return (GeneratedFileContract.ErrorResult(ex),
                    "Generated output capture rejected");
            }
        }

        var baseUrl = ResolveAgentBaseUrl();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(script))).ToLowerInvariant();
        var started = DateTime.UtcNow;
        var token = await ResolveSandboxTokenAsync(userId, ct);
        var brokerRollout = FeatureFlags.IsMarketplaceBrokerSandboxEnabled(storeId);
        var shopsJson = await SandboxShops.ForStoreAsync(
            _db, _encryption, storeId, ct, brokerRollout, _adapters);
        var shopSecrets = SandboxShops.SecretValues(shopsJson);

        // Read-only health probes select the single execution candidate; an old
        // sidecar that cannot capture fails before any POST, never after running.
        var selected = await SelectCandidateAsync(urls, capture, ct);
        if (selected == null)
        {
            if (capture)
                return (GeneratedFileContract.ErrorResult("ai_artifact_protocol_unavailable"),
                    "No sandbox advertises artifact capture");
            return (new { error = "sandbox unreachable" }, "sandbox error");
        }

        try
        {
            var client = _http.CreateClient(nameof(PythonSandboxClient));
            using var req = new HttpRequestMessage(
                HttpMethod.Post, selected.TrimEnd('/') + (capture ? "/run-artifacts" : "/run"));
            if (secret.Length > 0)
                req.Headers.TryAddWithoutValidation("X-Sandbox-Secret", secret);
            req.Content = CreateRunBody(script, storeId, baseUrl, token, shopsJson, outputs);
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

            if (capture && IsZip(resp))
                return await IngestArtifactResponseAsync(
                    resp, storeId, userId, script, hash, started, outputs!, shopSecrets, ct);

            var payload = await resp.Content.ReadFromJsonAsync<SandboxRunResponse>(
                             JsonOptions, ct)
                          ?? new SandboxRunResponse();
            payload.Stdout = SandboxShops.Redact(payload.Stdout, shopSecrets);
            payload.Stderr = SandboxShops.Redact(payload.Stderr, shopSecrets);
            payload.Error = SandboxShops.Redact(payload.Error, shopSecrets);
            var runId = await AuditRunAsync(storeId, userId, hash, payload, started, ct);

            if (!resp.IsSuccessStatusCode)
            {
                if (capture)
                    return (GeneratedFileContract.ErrorResult(
                            "ai_artifact_capture_failed", outcomeUnknown: true),
                        "Generated output capture failed");
                return (new { error = payload.Error ?? resp.ReasonPhrase, stdout = payload.Stdout, stderr = payload.Stderr },
                    "sandbox HTTP " + (int)resp.StatusCode);
            }

            if (capture)
            {
                // A real script/capture failure comes back as plain JSON; a
                // success-shaped payload without the ZIP is an unknown capture.
                // The script was already submitted, so the failure shape still
                // carries the generated-artifact class and unknown outcome.
                if (payload.ExitCode != 0 || !string.IsNullOrWhiteSpace(payload.Error))
                {
                    return (new
                    {
                        stdout = payload.Stdout,
                        stderr = payload.Stderr,
                        exitCode = payload.ExitCode,
                        error = payload.Error ?? $"python_exit_{payload.ExitCode}",
                        errorClass = "generated_artifact",
                        retryable = false,
                        outcomeUnknown = true,
                    }, "Generated output capture failed");
                }
                return (GeneratedFileContract.ErrorResult(
                        "ai_artifact_capture_unknown", outcomeUnknown: true),
                    "Generated output capture failed");
            }

            var failed = payload.ExitCode != 0 || !string.IsNullOrWhiteSpace(payload.Error);
            var error = failed
                ? payload.Error ?? $"python_exit_{payload.ExitCode}"
                : null;
            var result = new
            {
                stdout = payload.Stdout,
                stderr = payload.Stderr,
                exitCode = payload.ExitCode,
                error,
            };
            var summary = failed
                ? $"Python run failed (exit {payload.ExitCode})"
                : "Python run completed (exit 0)";
            return (result, summary);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sandbox run failed");
            // The POST may or may not have executed; never retry it elsewhere.
            if (capture)
                return (GeneratedFileContract.ErrorResult(
                        "ai_artifact_capture_unknown", outcomeUnknown: true),
                    "sandbox error");
            return (new { error = ex.Message }, "sandbox error");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static bool IsZip(HttpResponseMessage resp) =>
        resp.Content.Headers.ContentType?.MediaType?
            .Equals("application/zip", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Probes each trusted candidate's /health with a short linked timeout and
    /// returns the first compatible one. Capture requires artifactProtocol 1.
    /// </summary>
    private async Task<string?> SelectCandidateAsync(
        IReadOnlyList<string> urls, bool requireArtifacts, CancellationToken ct)
    {
        var anyHealthy = false;
        foreach (var url in urls)
        {
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linked.CancelAfter(TimeSpan.FromSeconds(3));
                var client = _http.CreateClient(nameof(PythonSandboxClient));
                using var resp = await client.GetAsync(
                    url.TrimEnd('/') + "/health", linked.Token);
                if (!resp.IsSuccessStatusCode)
                    continue;
                var health = await resp.Content.ReadFromJsonAsync<SandboxHealth>(JsonOptions, linked.Token);
                if (health?.Ok != true)
                    continue;
                anyHealthy = true;
                if (!requireArtifacts || health.ArtifactProtocol == GeneratedFileContract.ProtocolVersion)
                    return url;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Sandbox host {Url} health probe failed", url);
            }
        }
        _logger.LogWarning(
            "No compatible sandbox candidate (healthy={Healthy}, requireArtifacts={Require})",
            anyHealthy, requireArtifacts);
        return null;
    }

    private async Task<int> AuditRunAsync(
        int storeId, int userId, string hash, SandboxRunResponse payload,
        DateTime started, CancellationToken ct)
    {
        var run = new AiSandboxRun
        {
            userId = userId,
            storeId = storeId,
            scriptHash = hash,
            exitCode = payload.ExitCode,
            durationMs = (int)(DateTime.UtcNow - started).TotalMilliseconds,
            stdoutPreview = Trim(payload.Stdout),
            createdAt = DateTime.UtcNow,
        };
        _db.aiSandboxRuns.Add(run);
        await _db.SaveChangesAsync(ct);
        return run.Id;
    }

    /// <summary>
    /// Bounds the archive into a private temp file, verifies the manifest and every
    /// declared output byte-for-byte, and only then stages the batch. A malformed
    /// archive after execution is an unknown capture - the script already ran.
    /// </summary>
    private async Task<(object result, string summary)> IngestArtifactResponseAsync(
        HttpResponseMessage resp,
        int storeId,
        int userId,
        string script,
        string hash,
        DateTime started,
        IReadOnlyList<GeneratedOutputSpec> outputs,
        IReadOnlyList<string> shopSecrets,
        CancellationToken ct)
    {
        var archivePath = Path.Combine(Path.GetTempPath(), $"ai-artifacts-{Guid.NewGuid():N}.zip");
        var spools = new List<FileStream>();
        try
        {
            // A ZIP is only accepted for a successful response; a non-2xx ZIP is
            // an unknown capture, not a verified batch.
            if (!resp.IsSuccessStatusCode)
                return (GeneratedFileContract.ErrorResult("ai_artifact_capture_failed", outcomeUnknown: true),
                    "Generated output capture failed");
            if (resp.Content.Headers.ContentLength is > GeneratedFileContract.MaxArchiveBytes)
                return CaptureUnknown("archive exceeds limit");
            await using (var archiveFile = new FileStream(
                             archivePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                             81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose))
            {
                SetPrivateFileMode(archivePath);
                await using (var body = await resp.Content.ReadAsStreamAsync(ct))
                {
                    var bytes = await CopyBoundedAsync(
                        body, archiveFile, GeneratedFileContract.MaxArchiveBytes, ct);
                    if (bytes is null)
                        return CaptureUnknown("archive exceeds limit");
                }
                archiveFile.Position = 0;

                using var zip = new ZipArchive(archiveFile, ZipArchiveMode.Read, leaveOpen: false);
                var (manifest, entries) = await ReadManifestAsync(zip, outputs, ct);
                // Shop secrets are stripped BEFORE audit and before anything
                // returns to the model, exactly like the plain /run path.
                var payload = manifest.Result!;
                payload.Stdout = SandboxShops.Redact(payload.Stdout, shopSecrets);
                payload.Stderr = SandboxShops.Redact(payload.Stderr, shopSecrets);
                payload.Error = SandboxShops.Redact(payload.Error, shopSecrets);
                var runId = await AuditRunAsync(storeId, userId, hash, payload, started, ct);

                // An error-marked or nonzero result is an honest script failure:
                // it is audited and returned, never staged as a success. The
                // script was submitted, so the side-effect outcome is unknown.
                if (payload.ExitCode != 0 || !string.IsNullOrWhiteSpace(payload.Error))
                {
                    return (new
                    {
                        stdout = payload.Stdout,
                        stderr = payload.Stderr,
                        exitCode = payload.ExitCode,
                        error = payload.Error ?? $"python_exit_{payload.ExitCode}",
                        errorClass = "generated_artifact",
                        retryable = false,
                        outcomeUnknown = true,
                    }, "Generated output capture failed");
                }

                var verified = new List<VerifiedGeneratedOutput>();
                long batchTotal = 0;
                for (var i = 0; i < outputs.Count; i++)
                {
                    var meta = manifest.Outputs![i];
                    var entry = entries[$"outputs/{i}"];
                    if (entry.Length != meta.Length)
                        return CaptureUnknown("entry size mismatch");
                    var spoolPath = Path.Combine(Path.GetTempPath(), $"ai-output-{Guid.NewGuid():N}.tmp");
                    var spool = new FileStream(
                        spoolPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                        81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
                    spools.Add(spool);
                    SetPrivateFileMode(spoolPath);
                    await using (var src = entry.Open())
                    {
                        var spooled = await CopyHashingBoundedAsync(
                            src, spool, GeneratedFileContract.MaxFileBytes + 1, ct);
                        if (spooled is null)
                            return CaptureUnknown("output exceeds limit");
                        if (spooled.Value.Bytes != meta.Length
                            || !string.Equals(spooled.Value.Sha256, meta.Sha256!, StringComparison.Ordinal))
                            return CaptureUnknown("output hash mismatch");
                    }
                    batchTotal += meta.Length;
                    if (batchTotal > GeneratedFileContract.MaxBatchBytes)
                        return CaptureUnknown("batch exceeds limit");
                    spool.Position = 0;
                    verified.Add(new VerifiedGeneratedOutput(outputs[i], meta.Length, meta.Sha256!, spool));
                }

                var staged = await _artifacts!.StageCapturedAsync(
                    storeId, userId, runId, verified, ct);

                var result = new
                {
                    stdout = payload.Stdout,
                    stderr = payload.Stderr,
                    exitCode = payload.ExitCode,
                    error = (string?)null,
                    artifacts = staged,
                };
                return (result, "Python run completed with captured artifacts");
            }
        }
        catch (GeneratedArtifactException ex)
        {
            return (GeneratedFileContract.ErrorResult(ex),
                "Generated output capture failed");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Generated artifact archive ingest failed");
            return CaptureUnknown("ingest failed");
        }
        finally
        {
            foreach (var spool in spools)
                await spool.DisposeAsync();
        }

        (object, string) CaptureUnknown(string reason) =>
            (GeneratedFileContract.ErrorResult("ai_artifact_capture_unknown", outcomeUnknown: true),
                "Generated output capture failed");
    }

    /// <summary>
    /// Validates the archive entry table and manifest against the declared
    /// outputs before any output bytes are read.
    /// </summary>
    private static async Task<(ArtifactManifest Manifest, Dictionary<string, ZipArchiveEntry> Entries)>
        ReadManifestAsync(ZipArchive zip, IReadOnlyList<GeneratedOutputSpec> outputs, CancellationToken ct)
    {
        if (zip.Entries.Count != outputs.Count + 1)
            throw new GeneratedArtifactException("ai_artifact_capture_failed");
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries)
        {
            if (entry.Name.Length == 0 || entry.FullName.EndsWith('/'))
                throw new GeneratedArtifactException("ai_artifact_capture_failed");
            if (!entries.TryAdd(entry.FullName, entry))
                throw new GeneratedArtifactException("ai_artifact_capture_failed");
        }
        for (var i = 0; i < outputs.Count; i++)
        {
            if (!entries.ContainsKey($"outputs/{i}"))
                throw new GeneratedArtifactException("ai_artifact_capture_failed");
        }
        if (!entries.TryGetValue("manifest.json", out var manifestEntry)
            || manifestEntry.Length > GeneratedFileContract.MaxManifestBytes)
            throw new GeneratedArtifactException("ai_artifact_capture_failed");

        await using var stream = manifestEntry.Open();
        var mem = new MemoryStream();
        var copied = await CopyBoundedAsync(stream, mem, GeneratedFileContract.MaxManifestBytes, ct);
        if (copied is null)
            throw new GeneratedArtifactException("ai_artifact_capture_failed");
        mem.Position = 0;
        ArtifactManifest? manifest;
        try
        {
            manifest = await JsonSerializer.DeserializeAsync<ArtifactManifest>(mem, JsonOptions, ct);
        }
        catch (JsonException)
        {
            manifest = null;
        }
        if (manifest == null
            || manifest.Protocol != GeneratedFileContract.ProtocolVersion
            || manifest.Result == null
            || manifest.Outputs == null
            || manifest.Outputs.Count != outputs.Count)
            throw new GeneratedArtifactException("ai_artifact_capture_failed");
        for (var i = 0; i < outputs.Count; i++)
        {
            var meta = manifest.Outputs[i];
            var spec = outputs[i];
            if (meta == null
                || meta.Index != i
                || !string.Equals(meta.FileName, spec.FileName, StringComparison.Ordinal)
                || !string.Equals(meta.ContentType, spec.ContentType, StringComparison.Ordinal)
                || meta.Length < 0 || meta.Length > GeneratedFileContract.MaxFileBytes
                || !GeneratedFileContract.IsValidSha256(meta.Sha256))
                throw new GeneratedArtifactException("ai_artifact_capture_failed");
        }
        return (manifest, entries);
    }

    private static async Task<long?> CopyBoundedAsync(
        Stream input, Stream output, long maxBytes, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(
                buffer.AsMemory(0, (int)Math.Min(buffer.Length, maxBytes - total + 1)), ct);
            if (read == 0)
                return total;
            total += read;
            if (total > maxBytes)
                return null;
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }

    private static async Task<(long Bytes, string Sha256)?> CopyHashingBoundedAsync(
        Stream input, Stream output, long maxBytes, CancellationToken ct)
    {
        using var sha = SHA256.Create();
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(
                buffer.AsMemory(0, (int)Math.Min(buffer.Length, maxBytes - total)), ct);
            if (read == 0)
                break;
            sha.TransformBlock(buffer, 0, read, null, 0);
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            total += read;
            if (total >= maxBytes)
                return null;
        }
        sha.TransformFinalBlock([], 0, 0);
        return (total, Convert.ToHexString(sha.Hash!).ToLowerInvariant());
    }

    private static void SetPrivateFileMode(string path)
    {
        if (OperatingSystem.IsWindows())
            return;
        try
        {
            System.IO.File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch { /* best effort; the file is still process-private */ }
    }

    internal static ByteArrayContent CreateRunBody(
        string script,
        int storeId,
        string baseUrl,
        string token,
        string shopsJson = "[]",
        IReadOnlyList<GeneratedOutputSpec>? outputs = null)
    {
        var shops = ParseShops(shopsJson);
        string json;
        if (outputs is { Count: > 0 })
        {
            json = JsonSerializer.Serialize(new
            {
                code = script,
                storeId,
                baseUrl,
                token,
                shops,
                artifactProtocol = GeneratedFileContract.ProtocolVersion,
                outputs = outputs.Select(o => new { fileName = o.FileName, contentType = o.ContentType }),
            });
        }
        else
        {
            json = JsonSerializer.Serialize(new { code = script, storeId, baseUrl, token, shops });
        }
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(json));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        return content;
    }

    private async Task<string> ResolveSandboxTokenAsync(int userId, CancellationToken ct)
    {
        var auth = _httpContext.HttpContext?.Request.Headers.Authorization.ToString() ?? "";
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && auth.Length > 8)
            return auth;

        var userName = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.UserName)
            .FirstOrDefaultAsync(ct) ?? userId.ToString();
        return "Bearer " + _jwt.GenerateToken(userId, userName);
    }

    private static void AddUrl(List<string> list, string? raw)
    {
        var url = raw?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(url))
            return;
        if (list.Exists(u => string.Equals(u, url, StringComparison.OrdinalIgnoreCase)))
            return;
        list.Add(url);
    }

    private static JsonElement ParseShops(string shopsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(shopsJson) ? "[]" : shopsJson);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var doc = JsonDocument.Parse("[]");
            return doc.RootElement.Clone();
        }
    }

    private static string? Trim(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return text.Length <= 4000 ? text : text[..4000];
    }

    private sealed class SandboxRunResponse
    {
        public int ExitCode { get; set; }
        public string? Stdout { get; set; }
        public string? Stderr { get; set; }
        public string? Error { get; set; }
    }

    private sealed class SandboxHealth
    {
        public bool Ok { get; set; }
        public int ArtifactProtocol { get; set; }
    }

    private sealed class ArtifactManifest
    {
        public int Protocol { get; set; }
        public SandboxRunResponse? Result { get; set; }
        public List<ArtifactOutputMeta>? Outputs { get; set; }
    }

    private sealed class ArtifactOutputMeta
    {
        public int Index { get; set; }
        public string? FileName { get; set; }
        public string? ContentType { get; set; }
        public long Length { get; set; }
        public string? Sha256 { get; set; }
    }
}
