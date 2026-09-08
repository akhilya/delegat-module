using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Delegat.Infrastructure.Auth;
using Delegat.Models.Ai;

namespace Delegat.Services.Ai.Agent;

public sealed class AgentToolHost
{
    private static readonly Regex SafePathRx = new(@"^/[a-zA-Z0-9\-/._]*$", RegexOptions.Compiled);

    private readonly CatalogSearchService _catalog;
    private readonly SqlReadonlyService _sql;
    private readonly KnowledgeIndexService _knowledge;
    private readonly AgentPlatformGateway _platform;
    private readonly PythonSandboxClient _python;
    private readonly IPermissionService _permissions;
    private readonly DataContext _db;
    private readonly ILogger<AgentToolHost> _logger;

    public AgentToolHost(
        CatalogSearchService catalog,
        SqlReadonlyService sql,
        KnowledgeIndexService knowledge,
        AgentPlatformGateway platform,
        PythonSandboxClient python,
        IPermissionService permissions,
        DataContext db,
        ILogger<AgentToolHost> logger)
    {
        _catalog = catalog;
        _sql = sql;
        _knowledge = knowledge;
        _platform = platform;
        _python = python;
        _permissions = permissions;
        _db = db;
        _logger = logger;
    }

    public async Task<(object result, string summary)> DispatchAsync(
        string toolName, string inputJson, int storeId, int userId, CancellationToken ct)
    {
        var perms = new HashSet<string>(
            await _permissions.GetEffectivePermissionsAsync(userId, ct),
            StringComparer.OrdinalIgnoreCase);

        if (!AgentAccess.CanUseAgent(perms))
            return (new { error = "ai.use required" }, "ai.use required");

        try
        {
            var (result, summary) = toolName switch
            {
                "search_knowledge" => await SearchKnowledgeAsync(inputJson, ct),
                "search_catalog" => await SearchCatalogAsync(inputJson, storeId, ct),
                "sql_readonly" => await SqlAsync(inputJson, storeId, perms, ct),
                "run_python" => await PythonAsync(inputJson, storeId, userId, perms, ct),
                "platform_request" => await PlatformAsync(inputJson, storeId, userId, perms, ct),
                "match_files" => await MatchFilesAsync(inputJson, storeId, ct),
                "navigate_to" => NavigateTo(inputJson),
                _ => ((object)new { error = $"Unknown tool: {toolName}" }, $"Unknown tool: {toolName}"),
            };

            _db.aiAgentAudits.Add(new AiAgentAudit
            {
                userId = userId,
                storeId = storeId,
                tool = toolName.Length > 64 ? toolName[..64] : toolName,
                summary = summary.Length > 500 ? summary[..500] : summary,
                createdAt = DateTime.UtcNow,
            });
            try { await _db.SaveChangesAsync(ct); }
            catch (Exception ex) { _logger.LogDebug(ex, "Agent audit persist failed"); }

            return (result, summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent tool {Tool} failed", toolName);
            return (new { error = ex.Message }, $"Error: {ex.Message}");
        }
    }

    private async Task<(object, string)> SearchKnowledgeAsync(string json, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var q = doc.RootElement.TryGetProperty("query", out var qe) ? qe.GetString() ?? "" : "";
        var take = doc.RootElement.TryGetProperty("limit", out var le) && le.TryGetInt32(out var n) ? n : 8;
        var hits = await _knowledge.SearchAsync(q, take, ct);
        return (new { items = hits.Select(h => new { h.SourcePath, h.Title, snippet = h.Body.Length > 600 ? h.Body[..600] : h.Body }) },
            $"{hits.Count} knowledge hits");
    }

    private Task<(object, string)> SearchCatalogAsync(string json, int storeId, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var q = doc.RootElement.TryGetProperty("query", out var qe) ? qe.GetString() : "";
        var take = doc.RootElement.TryGetProperty("limit", out var le) && le.TryGetInt32(out var n) ? n : 50;
        return _catalog.SearchAsync(storeId, q, take, ct);
    }

    private Task<(object, string)> SqlAsync(string json, int storeId, IReadOnlySet<string> perms, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var sql = doc.RootElement.TryGetProperty("sql", out var s) ? s.GetString() ?? "" : "";
        return _sql.QueryAsync(storeId, sql, perms, ct);
    }

    private async Task<(object, string)> PythonAsync(
        string json, int storeId, int userId, IReadOnlySet<string> perms, CancellationToken ct)
    {
        if (!AgentAccess.CanRunPython(perms))
            return (new { error = "python_forbidden" }, "Python is not allowed for this account");
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var script = doc.RootElement.TryGetProperty("code", out var c) ? c.GetString()
            ?? (doc.RootElement.TryGetProperty("script", out var s) ? s.GetString() : "") : "";
        return await _python.RunAsync(storeId, userId, script ?? "", ct);
    }

    private async Task<(object, string)> PlatformAsync(
        string json, int storeId, int userId, IReadOnlySet<string> perms, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var method = doc.RootElement.TryGetProperty("method", out var m) ? m.GetString() ?? "GET" : "GET";
        var path = doc.RootElement.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
        JsonElement? body = doc.RootElement.TryGetProperty("body", out var b) ? b.Clone() : null;
        return await _platform.ExecuteAsync(storeId, userId, method, path, body, perms, ct);
    }

    private async Task<(object, string)> MatchFilesAsync(string json, int storeId, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        if (!doc.RootElement.TryGetProperty("files", out var filesEl) || filesEl.ValueKind != JsonValueKind.Array)
            return (new { error = "files array required" }, "files required");

        var inputs = new List<ProductFileMatchInput>();
        foreach (var f in filesEl.EnumerateArray())
        {
            var rel = f.TryGetProperty("relativePath", out var rp) ? rp.GetString() ?? "" : "";
            var name = f.TryGetProperty("fileName", out var fn) ? fn.GetString() ?? Path.GetFileName(rel) : Path.GetFileName(rel);
            if (rel.Length == 0) continue;
            inputs.Add(new ProductFileMatchInput(rel, name));
        }

        var products = await _catalog.ListMatchProductsAsync(storeId, ct);
        var matches = ProductFileMatcher.Match(inputs, products);
        var hitCount = matches.Count(x => x.ProductId != null);
        return (new { matches }, $"{hitCount}/{matches.Count} matched");
    }

    internal static (object, string) NavigateTo(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var actions = new List<Dictionary<string, string>>();

        void TryAdd(string? rawUrl, string? rawLabel, string? rawPrompt)
        {
            if (actions.Count >= 4) return;
            var prompt = (rawPrompt ?? "").Trim();
            if (prompt.Length > 200) prompt = prompt[..200];
            var url = (rawUrl ?? "").Trim();
            if (url.Length > 0 && !url.StartsWith('/')) url = "/" + url;
            var safeUrl = url.Length is > 0 and <= 180 && SafePathRx.IsMatch(url) ? url : "";
            if (safeUrl.Length == 0 && prompt.Length == 0) return;
            var label = (rawLabel ?? "").Trim();
            if (label.Length == 0) label = safeUrl.Length > 0 ? safeUrl : prompt;
            if (label.Length > 80) label = label[..80];
            var row = new Dictionary<string, string> { ["label"] = label };
            if (safeUrl.Length > 0) row["url"] = safeUrl;
            if (prompt.Length > 0) row["prompt"] = prompt;
            actions.Add(row);
        }

        if (doc.RootElement.TryGetProperty("items", out var itemsEl)
            && itemsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in itemsEl.EnumerateArray())
            {
                TryAdd(
                    el.TryGetProperty("url", out var u) ? u.GetString() : "",
                    el.TryGetProperty("label", out var l) ? l.GetString() : "",
                    el.TryGetProperty("prompt", out var p) ? p.GetString() : "");
            }
        }
        else
        {
            TryAdd(
                doc.RootElement.TryGetProperty("url", out var u) ? u.GetString() : "",
                doc.RootElement.TryGetProperty("label", out var l) ? l.GetString() : "",
                doc.RootElement.TryGetProperty("prompt", out var p) ? p.GetString() : "");
        }

        var ok = actions.Count > 0;
        var firstUrl = ok && actions[0].TryGetValue("url", out var fu) ? fu : "";
        var firstLabel = ok ? actions[0]["label"] : "";
        return (new
        {
            navigate = ok,
            url = firstUrl,
            label = firstLabel,
            actions,
        }, ok ? "Navigate" : "Unsafe path");
    }

    public static string HashScript(string script)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(script ?? ""));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
