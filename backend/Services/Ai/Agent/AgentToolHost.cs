using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Delegat.Infrastructure.Auth;
using Delegat.Models.Ai;
using Delegat.Services.Docs;

namespace Delegat.Services.Ai.Agent;

public sealed class AgentToolHost
{
    private static readonly Regex SafePathRx = new(
        @"^/[a-zA-Z0-9\-/._]*(\?[A-Za-z0-9._~%=&+,-]*)?$",
        RegexOptions.Compiled);

    private readonly CatalogSearchService _catalog;
    private readonly SqlReadonlyService _sql;
    private readonly KnowledgeIndexService _knowledge;
    private readonly AiMemoryService _memory;
    private readonly AgentPlatformGateway _platform;
    private readonly PythonSandboxClient _python;
    private readonly IPermissionService _permissions;
    private readonly DataContext _db;
    private readonly CabinetActionCatalog _actions;
    private readonly ILogger<AgentToolHost> _logger;
    private readonly IMcpToolGateway? _mcp;
    private readonly IPublicDocsClient? _publicDocs;

    public AgentToolHost(
        CatalogSearchService catalog,
        SqlReadonlyService sql,
        KnowledgeIndexService knowledge,
        AgentPlatformGateway platform,
        PythonSandboxClient python,
        IPermissionService permissions,
        DataContext db,
        ILogger<AgentToolHost> logger,
        IMcpToolGateway? mcp = null,
        IPublicDocsClient? publicDocs = null,
        CabinetActionCatalog? actions = null,
        AiMemoryService? memory = null)
    {
        _catalog = catalog;
        _sql = sql;
        _knowledge = knowledge;
        _memory = memory ?? new AiMemoryService(db);
        _platform = platform;
        _python = python;
        _permissions = permissions;
        _db = db;
        _logger = logger;
        _mcp = mcp;
        _publicDocs = publicDocs;
        _actions = actions ?? new CabinetActionCatalog();
    }

    public async Task<(object result, string summary)> DispatchAsync(
        string toolName,
        string inputJson,
        int storeId,
        int userId,
        CancellationToken ct,
        bool allowConnectedMcp = false)
    {
        var perms = new HashSet<string>(
            await _permissions.GetEffectivePermissionsAsync(userId, ct),
            StringComparer.OrdinalIgnoreCase);

        if (!AgentAccess.CanUseAgent(perms))
            return (new { error = "ai.use required" }, "ai.use required");

        if (AgentAccess.IsReadOnly(perms)
            && (string.Equals(toolName, "memory_save", StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolName, "memory_delete", StringComparison.OrdinalIgnoreCase)))
            return (new { error = "read_only" }, "This account is read-only");

        try
        {
            object result;
            string summary;
            if (McpToolNames.TryParse(toolName, out _, out _))
            {
                if (!allowConnectedMcp)
                    return (new { error = "connected_tool_not_available" }, "Connected tools are not available on this surface");
                if (!perms.Contains(Permissions.AiUse, StringComparer.OrdinalIgnoreCase))
                    return (new { error = "ai.use required for connected tools" }, "ai.use required for connected tools");
                if (_mcp == null)
                    (result, summary) = ((object)new { error = $"Unknown tool: {toolName}" }, $"Unknown tool: {toolName}");
                else
                    (result, summary) = await _mcp.CallAsync(toolName, inputJson, ct);
            }
            else
            {
                (result, summary) = toolName switch
                {
                    "search_knowledge" => await SearchKnowledgeAsync(inputJson, ct),
                    "memory_search" => await MemorySearchAsync(inputJson, storeId, userId, ct),
                    "memory_save" => await MemorySaveAsync(inputJson, storeId, userId, ct),
                    "memory_delete" => await MemoryDeleteAsync(inputJson, storeId, userId, ct),
                    "get_doc" => await GetDocAsync(inputJson, ct),
                    "search_catalog" => await SearchCatalogAsync(inputJson, storeId, ct),
                    "search_actions" => SearchActions(inputJson),
                    "sql_readonly" => await SqlAsync(inputJson, storeId, perms, ct),
                    "run_python" => await PythonAsync(inputJson, storeId, userId, perms, ct),
                    "platform_request" => await PlatformAsync(inputJson, storeId, userId, perms, ct),
                    "match_files" => await MatchFilesAsync(inputJson, storeId, ct),
                    "navigate_to" => NavigateTo(inputJson),
                    "ask_form" => AskForm(inputJson),
                    _ => ((object)new { error = $"Unknown tool: {toolName}" }, $"Unknown tool: {toolName}"),
                };
            }

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
        var take = doc.RootElement.TryGetProperty("limit", out var le) && le.TryGetInt32(out var n) ? n : 12;
        var hits = await _knowledge.SearchAsync(q, take, ct);
        return (new { items = hits.Select(h => new { h.SourcePath, h.Title, snippet = h.Body.Length > 900 ? h.Body[..900] : h.Body }) },
            $"{hits.Count} knowledge hits");
    }

    private async Task<(object, string)> MemorySearchAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var query = doc.RootElement.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "";
        var items = await _memory.SearchAsync(storeId, userId, query, ct);
        return (new { items = items.Select(m => new { m.Id, m.category, m.key, m.value, m.updatedAt }) },
            $"{items.Count} memories");
    }

    private async Task<(object, string)> MemorySaveAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var root = doc.RootElement;
        var category = root.TryGetProperty("category", out var c) ? c.GetString() ?? "general" : "general";
        var key = root.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "";
        var value = root.TryGetProperty("value", out var v) ? v.GetString() ?? "" : "";
        var saved = await _memory.SaveAsync(storeId, userId, category, key, value, ct);
        return (new { saved = true, saved.Id, saved.category, saved.key, saved.value }, $"saved memory {saved.Id}");
    }

    private async Task<(object, string)> MemoryDeleteAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var id = doc.RootElement.TryGetProperty("id", out var i) && i.TryGetInt32(out var parsed) ? parsed : 0;
        var deleted = await _memory.DeleteAsync(storeId, userId, id, ct);
        return (new { deleted }, deleted ? $"deleted memory {id}" : "memory not found");
    }

    private (object, string) SearchActions(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var q = doc.RootElement.TryGetProperty("query", out var qe) ? qe.GetString() ?? "" : "";
        var take = doc.RootElement.TryGetProperty("limit", out var le) && le.TryGetInt32(out var n) ? n : 8;
        var hits = _actions.Search(q, take);
        return (
            new
            {
                hits = hits.Select(h => new { method = h.Method, path = h.Path, write = h.Write, note = h.Note }),
            },
            $"{hits.Count} actions");
    }

    private async Task<(object, string)> GetDocAsync(string json, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var slug = doc.RootElement.TryGetProperty("slug", out var se) ? se.GetString() ?? "" : "";
        var locale = doc.RootElement.TryGetProperty("locale", out var le) ? le.GetString() ?? "ru" : "ru";
        if (string.IsNullOrWhiteSpace(slug))
            return (new { error = "docs_not_found" }, "docs_not_found");

        if (_publicDocs != null && !KnowledgeIndexService.IsInstanceDocPath(slug))
        {
            try
            {
                var page = await _publicDocs.GetPageAsync(slug, locale, ct);
                var body = page.Markdown.Length > PublicDocsClient.MaxBodyChars
                    ? page.Markdown[..PublicDocsClient.MaxBodyChars]
                    : page.Markdown;
                return (new { slug = page.Slug, locale = page.Locale, title = page.Title, markdown = body },
                    string.IsNullOrEmpty(page.Slug) ? "doc hub" : $"doc {page.Slug}");
            }
            catch (DocsClientException)
            {
                // Fall through to the indexed instance corpus.
            }
        }

        var indexed = await _knowledge.ReadSourceAsync(slug, locale, ct);
        if (indexed != null)
        {
            return (new { slug = indexed.SourcePath, locale, title = indexed.Title, markdown = indexed.Body },
                $"doc {indexed.SourcePath}");
        }

        if (_publicDocs == null)
            return (new { error = "docs_not_configured" }, "docs_not_configured");
        return (new { error = "docs_not_found" }, "docs_not_found");
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
            if (actions.Count >= 6) return;
            var prompt = (rawPrompt ?? "").Trim();
            if (prompt.Length > 200) prompt = prompt[..200];
            var url = (rawUrl ?? "").Trim();
            if (url.Length > 0 && !url.StartsWith('/')) url = "/" + url;
            var safeUrl = url.Length is > 0 and <= 180 && SafePathRx.IsMatch(url) ? url : "";
            // One action per button: opening a page never also sends a hidden follow-up.
            if (safeUrl.Length > 0) prompt = "";
            if (safeUrl.Length == 0 && prompt.Length == 0) return;
            var label = (rawLabel ?? "").Trim();
            if (label.Length == 0) label = safeUrl.Length > 0 ? safeUrl : prompt;
            if (label.Length > 80) label = label[..80];
            var row = new Dictionary<string, string>
            {
                ["label"] = label,
                ["kind"] = safeUrl.Length > 0 ? "open" : "continue",
            };
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

    private static readonly Regex AdapterKeyRx = new(@"^[a-zA-Z0-9._-]{1,64}$", RegexOptions.Compiled);
    private static readonly Regex FieldKeyRx = new(@"^[a-zA-Z][a-zA-Z0-9_]{0,39}$", RegexOptions.Compiled);

    internal static (object, string) AskForm(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var root = doc.RootElement;
        var kind = (root.TryGetProperty("kind", out var kindEl) ? kindEl.GetString() : "")
            ?.Trim().ToLowerInvariant() ?? "";
        var title = Clip(root, "title", 80);
        var submitLabel = Clip(root, "submitLabel", 40);

        if (kind is "connection" or "connect")
        {
            var adapterKey = (root.TryGetProperty("adapterKey", out var ak) ? ak.GetString() : "")?.Trim() ?? "";
            if (!AdapterKeyRx.IsMatch(adapterKey))
                return (new { error = "adapter_required", form = false }, "Need an adapter");
            var name = Clip(root, "name", 80);
            return (new
            {
                form = true,
                kind = "connection",
                adapterKey,
                name,
                title,
                submitLabel,
            }, "Form");
        }

        if (kind is "questions" or "question")
        {
            var questions = ParseQuestionList(root);
            if (questions.Count == 0)
                return (new { error = "questions_required", form = false }, "Need questions");
            return (new
            {
                form = true,
                kind = "questions",
                title,
                submitLabel,
                followUp = Clip(root, "followUp", 400),
                questions,
            }, "Form");
        }

        if (kind != "fields")
            return (new { error = "unknown_form", form = false }, "Unknown form");

        var fields = new List<object>();
        if (root.TryGetProperty("fields", out var fieldsEl) && fieldsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in fieldsEl.EnumerateArray())
            {
                if (fields.Count >= 8) break;
                var key = (el.TryGetProperty("key", out var k) ? k.GetString() : "")?.Trim() ?? "";
                if (!FieldKeyRx.IsMatch(key)) continue;
                var type = (el.TryGetProperty("type", out var t) ? t.GetString() : "text")
                    ?.Trim().ToLowerInvariant() ?? "text";
                if (type is "password" or "secret") continue;
                if (el.TryGetProperty("secret", out var secretEl)
                    && secretEl.ValueKind == JsonValueKind.True)
                    continue;
                if (el.TryGetProperty("isSecret", out var isSecretEl)
                    && isSecretEl.ValueKind == JsonValueKind.True)
                    continue;
                if (type != "select") type = "text";
                var label = (el.TryGetProperty("label", out var l) ? l.GetString() : "")?.Trim() ?? "";
                if (label.Length == 0) label = key;
                if (label.Length > 80) label = label[..80];
                var required = true;
                if (el.TryGetProperty("required", out var reqEl) && reqEl.ValueKind == JsonValueKind.False)
                    required = false;
                var placeholder = ClipEl(el, "placeholder", 80);
                List<object>? options = null;
                if (type == "select" && el.TryGetProperty("options", out var optEl)
                    && optEl.ValueKind == JsonValueKind.Array)
                {
                    options = [];
                    foreach (var opt in optEl.EnumerateArray())
                    {
                        if (options.Count >= 20) break;
                        var value = (opt.TryGetProperty("value", out var v) ? v.GetString() : "")?.Trim() ?? "";
                        if (value.Length == 0) continue;
                        var optLabel = (opt.TryGetProperty("label", out var ol) ? ol.GetString() : "")?.Trim() ?? value;
                        options.Add(new { value = value.Length > 80 ? value[..80] : value, label = optLabel.Length > 80 ? optLabel[..80] : optLabel });
                    }
                    if (options.Count == 0) continue;
                }

                fields.Add(new
                {
                    key,
                    label,
                    type,
                    required,
                    placeholder,
                    options,
                });
            }
        }

        if (fields.Count == 0)
            return (new { error = "fields_required", form = false }, "Need fields");

        var followUp = Clip(root, "followUp", 400);
        return (new
        {
            form = true,
            kind = "fields",
            title,
            submitLabel,
            followUp,
            fields,
        }, "Form");
    }

    private static List<object> ParseQuestionList(JsonElement root)
    {
        JsonElement listEl;
        if (root.TryGetProperty("questions", out var questionsEl) && questionsEl.ValueKind == JsonValueKind.Array)
            listEl = questionsEl;
        else if (root.TryGetProperty("fields", out var fieldsEl) && fieldsEl.ValueKind == JsonValueKind.Array)
            listEl = fieldsEl;
        else
            return [];

        var questions = new List<object>();
        foreach (var el in listEl.EnumerateArray())
        {
            if (questions.Count >= 5) break;
            var parsed = ParseQuestion(el);
            if (parsed != null) questions.Add(parsed);
        }
        return questions;
    }

    private static object? ParseQuestion(JsonElement el)
    {
        var key = (el.TryGetProperty("key", out var k) ? k.GetString() : null)
            ?? (el.TryGetProperty("name", out var n) ? n.GetString() : "")
            ?? "";
        key = key.Trim();
        if (!FieldKeyRx.IsMatch(key)) return null;
        if (IsSecretFlag(el)) return null;

        var rawType = (el.TryGetProperty("type", out var t) ? t.GetString() : "")
            ?.Trim().ToLowerInvariant() ?? "";
        if (rawType is "password" or "secret") return null;

        var choices = ParseChoices(el);
        var type = rawType switch
        {
            "multiple" or "multi" or "checkbox" => "multiple",
            "text" or "freeform" or "input" or "textarea" => "text",
            "select" or "choice" or "single" or "radio" => "single",
            _ => choices.Count > 0 ? "single" : "text",
        };

        var allowOther = el.TryGetProperty("allowOther", out var otherEl)
            && otherEl.ValueKind == JsonValueKind.True;
        if (type == "text")
        {
            choices = [];
            allowOther = false;
        }
        else if (choices.Count == 0 && !allowOther)
        {
            return null;
        }

        var title = ClipEl(el.TryGetProperty("title", out var titleEl) ? titleEl : default, "", 80);
        if (title.Length == 0) title = ClipEl(el.TryGetProperty("label", out var labelEl) ? labelEl : default, "", 80);
        if (title.Length == 0) title = key;
        var required = true;
        if (el.TryGetProperty("required", out var reqEl) && reqEl.ValueKind == JsonValueKind.False)
            required = false;

        return new
        {
            key,
            title,
            description = ClipEl(el.TryGetProperty("description", out var descEl) ? descEl : default, "", 160),
            type,
            required,
            placeholder = ClipEl(el.TryGetProperty("placeholder", out var phEl) ? phEl : default, "", 80),
            allowOther,
            choices,
        };
    }

    private static bool IsSecretFlag(JsonElement el)
    {
        if (el.TryGetProperty("secret", out var secretEl) && secretEl.ValueKind == JsonValueKind.True)
            return true;
        if (el.TryGetProperty("isSecret", out var isSecretEl) && isSecretEl.ValueKind == JsonValueKind.True)
            return true;
        return false;
    }

    private static List<object> ParseChoices(JsonElement el)
    {
        JsonElement optEl;
        if (el.TryGetProperty("choices", out var choicesEl) && choicesEl.ValueKind == JsonValueKind.Array)
            optEl = choicesEl;
        else if (el.TryGetProperty("options", out var optionsEl) && optionsEl.ValueKind == JsonValueKind.Array)
            optEl = optionsEl;
        else
            return [];

        var choices = new List<object>();
        foreach (var opt in optEl.EnumerateArray())
        {
            if (choices.Count >= 8) break;
            var value = (opt.TryGetProperty("value", out var v) ? v.GetString() : "")?.Trim() ?? "";
            if (value.Length == 0) continue;
            if (value.Length > 80) value = value[..80];
            var label = (opt.TryGetProperty("label", out var ol) ? ol.GetString() : "")?.Trim() ?? value;
            if (label.Length == 0) label = value;
            if (label.Length > 80) label = label[..80];
            var description = ClipEl(opt.TryGetProperty("description", out var d) ? d : default, "", 120);
            choices.Add(new { value, label, description });
        }
        return choices;
    }

    private static string Clip(JsonElement root, string name, int max)
    {
        if (!root.TryGetProperty(name, out var el)) return "";
        return ClipEl(el, name, max);
    }

    private static string ClipEl(JsonElement el, string _, int max)
    {
        var text = el.ValueKind == JsonValueKind.String ? (el.GetString() ?? "").Trim() : "";
        return text.Length > max ? text[..max] : text;
    }

    public static string HashScript(string script)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(script ?? ""));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
