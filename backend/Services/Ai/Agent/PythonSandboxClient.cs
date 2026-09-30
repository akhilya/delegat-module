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

    public PythonSandboxClient(
        DataContext db,
        IHttpClientFactory http,
        ILogger<PythonSandboxClient> logger,
        IHttpContextAccessor httpContext,
        IJwtService jwt,
        EncryptionService? encryption = null)
    {
        _db = db;
        _http = http;
        _logger = logger;
        _httpContext = httpContext;
        _jwt = jwt;
        _encryption = encryption;
    }

    /// <summary>
    /// Sidecar is on only while AI is on. Address is an explicit <c>SANDBOX_URL</c>
    /// (local compose) or the Control Plane container name <c>d-{stack}-sandbox</c>
    /// from <c>STACK_ID</c> / <c>INSTANCE_ID</c> / <c>mysql-{stack}</c>. Alias DNS is not used.
    /// </summary>
    public static bool IsConfigured() => FeatureFlags.IsAiEnabled() && CandidateUrls().Count > 0;

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

    public async Task<(object result, string summary)> RunAsync(
        int storeId, int userId, string script, CancellationToken ct)
    {
        var urls = CandidateUrls();
        if (urls.Count == 0)
            return (new { error = "sandbox_not_configured" }, "Python sandbox is not configured");

        if (string.IsNullOrWhiteSpace(script))
            return (new { error = "code is required" }, "code is required");
        if (script.Length > 80_000)
            return (new { error = "script too large" }, "script too large");

        var secret = Environment.GetEnvironmentVariable("SANDBOX_SECRET") ?? "";
        var baseUrl = ResolveAgentBaseUrl();

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(script))).ToLowerInvariant();
        var started = DateTime.UtcNow;
        var token = await ResolveSandboxTokenAsync(userId, ct);
        var shopsJson = await SandboxShops.ForStoreAsync(_db, _encryption, storeId, ct);
        var shopSecrets = SandboxShops.SecretValues(shopsJson);

        Exception? lastConnect = null;
        for (var i = 0; i < urls.Count; i++)
        {
            var url = urls[i];
            try
            {
                var client = _http.CreateClient(nameof(PythonSandboxClient));
                using var req = new HttpRequestMessage(HttpMethod.Post, url.TrimEnd('/') + "/run");
                if (secret.Length > 0)
                    req.Headers.TryAddWithoutValidation("X-Sandbox-Secret", secret);
                req.Content = CreateRunBody(script, storeId, baseUrl, token, shopsJson);
                using var resp = await client.SendAsync(req, ct);
                var payload = await resp.Content.ReadFromJsonAsync<SandboxRunResponse>(
                                 new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct)
                              ?? new SandboxRunResponse();
                payload.Stdout = SandboxShops.Redact(payload.Stdout, shopSecrets);
                payload.Stderr = SandboxShops.Redact(payload.Stderr, shopSecrets);
                payload.Error = SandboxShops.Redact(payload.Error, shopSecrets);
                var duration = (int)(DateTime.UtcNow - started).TotalMilliseconds;
                _db.aiSandboxRuns.Add(new AiSandboxRun
                {
                    userId = userId,
                    storeId = storeId,
                    scriptHash = hash,
                    exitCode = payload.ExitCode,
                    durationMs = duration,
                    stdoutPreview = Trim(payload.Stdout),
                    createdAt = DateTime.UtcNow,
                });
                await _db.SaveChangesAsync(ct);

                if (!resp.IsSuccessStatusCode)
                    return (new { error = payload.Error ?? resp.ReasonPhrase, stdout = payload.Stdout, stderr = payload.Stderr },
                        "sandbox HTTP " + (int)resp.StatusCode);

                return (new { stdout = payload.Stdout, stderr = payload.Stderr, exitCode = payload.ExitCode },
                    $"exit {payload.ExitCode}");
            }
            catch (HttpRequestException ex) when (i < urls.Count - 1)
            {
                lastConnect = ex;
                _logger.LogDebug(ex, "Sandbox host {Url} unreachable, trying next", url);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Sandbox run failed");
                return (new { error = ex.Message }, "sandbox error");
            }
        }

        _logger.LogWarning(lastConnect, "Sandbox run failed");
        return (new { error = lastConnect?.Message ?? "sandbox unreachable" }, "sandbox error");
    }

    internal static ByteArrayContent CreateRunBody(
        string script, int storeId, string baseUrl, string token, string shopsJson = "[]")
    {
        var shops = ParseShops(shopsJson);
        var json = JsonSerializer.Serialize(new { code = script, storeId, baseUrl, token, shops });
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
}
