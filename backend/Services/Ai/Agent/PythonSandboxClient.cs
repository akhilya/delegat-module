using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Delegat.Infrastructure.Auth;
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

    public PythonSandboxClient(
        DataContext db,
        IHttpClientFactory http,
        ILogger<PythonSandboxClient> logger,
        IHttpContextAccessor httpContext,
        IJwtService jwt)
    {
        _db = db;
        _http = http;
        _logger = logger;
        _httpContext = httpContext;
        _jwt = jwt;
    }

    public static bool IsConfigured() =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SANDBOX_URL"));

    public async Task<(object result, string summary)> RunAsync(
        int storeId, int userId, string script, CancellationToken ct)
    {
        var url = Environment.GetEnvironmentVariable("SANDBOX_URL")?.Trim();
        if (string.IsNullOrEmpty(url))
            return (new { error = "sandbox_not_configured" }, "Python sandbox is not configured (SANDBOX_URL)");

        if (string.IsNullOrWhiteSpace(script))
            return (new { error = "code is required" }, "code is required");
        if (script.Length > 80_000)
            return (new { error = "script too large" }, "script too large");

        var secret = Environment.GetEnvironmentVariable("SANDBOX_SECRET") ?? "";
        var baseUrl = Environment.GetEnvironmentVariable("SANDBOX_AGENT_BASE_URL")
                      ?? Environment.GetEnvironmentVariable("PUBLIC_BASE_URL")
                      ?? "http://127.0.0.1:5000";

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(script))).ToLowerInvariant();
        var started = DateTime.UtcNow;
        var token = await ResolveSandboxTokenAsync(userId, ct);

        try
        {
            var client = _http.CreateClient(nameof(PythonSandboxClient));
            using var req = new HttpRequestMessage(HttpMethod.Post, url.TrimEnd('/') + "/run");
            if (secret.Length > 0)
                req.Headers.TryAddWithoutValidation("X-Sandbox-Secret", secret);
            req.Content = CreateRunBody(script, storeId, baseUrl, token);
            using var resp = await client.SendAsync(req, ct);
            var payload = await resp.Content.ReadFromJsonAsync<SandboxRunResponse>(
                             new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct)
                          ?? new SandboxRunResponse();
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sandbox run failed");
            return (new { error = ex.Message }, "sandbox error");
        }
    }

    internal static ByteArrayContent CreateRunBody(string script, int storeId, string baseUrl, string token)
    {
        var json = JsonSerializer.Serialize(new { code = script, storeId, baseUrl, token });
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
