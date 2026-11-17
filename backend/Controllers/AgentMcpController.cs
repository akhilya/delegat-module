using System.Text.Json;
using System.Text.Json.Serialization;
using Delegat.Infrastructure.Configuration;
using Delegat.Infrastructure.Middleware;
using Delegat.Services.Ai.Agent;
using Microsoft.AspNetCore.Mvc;

namespace Delegat.Controllers;

[ApiController]
[AgentAuthorize]
[Route("/mcp")]
public class AgentMcpController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly AgentSessionResolver _session;
    private readonly AgentToolHost _tools;
    private readonly KnowledgeIndexService _knowledge;
    private readonly CabinetActionCatalog _actions;

    public AgentMcpController(
        AgentSessionResolver session,
        AgentToolHost tools,
        KnowledgeIndexService knowledge,
        CabinetActionCatalog actions)
    {
        _session = session;
        _tools = tools;
        _knowledge = knowledge;
        _actions = actions;
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] JsonElement body, CancellationToken ct)
    {
        if (!FeatureFlags.IsAiEnabled())
            throw ApiException.Forbidden(code: "ai_disabled");

        var method = body.TryGetProperty("method", out var m) ? m.GetString() ?? "" : "";
        JsonElement id = body.TryGetProperty("id", out var idEl) ? idEl : default;

        if (method.StartsWith("notifications/", StringComparison.Ordinal))
            return NoContent();

        try
        {
            object result = method switch
            {
                "initialize" => new
                {
                    protocolVersion = "2025-03-26",
                    capabilities = new { tools = new { }, resources = new { } },
                    serverInfo = new { name = "delegat-module", version = Delegat.PlatformVersion.Current },
                },
                "ping" => new { },
                "tools/list" => await ListToolsAsync(ct),
                "resources/list" => await ListResourcesAsync(ct),
                "resources/read" => await ReadResourceAsync(body, ct),
                "tools/call" => await CallToolAsync(body, ct),
                _ => throw new InvalidOperationException($"Unknown method {method}"),
            };

            return Ok(new { jsonrpc = "2.0", id = UnwrapId(id), result });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { jsonrpc = "2.0", id = UnwrapId(id), error = new { code = -32000, message = ex.Message } });
        }
        catch (Exception ex)
        {
            return Ok(new { jsonrpc = "2.0", id = UnwrapId(id), error = new { code = -32603, message = ex.Message } });
        }
    }

    private async Task<object> CallToolAsync(JsonElement body, CancellationToken ct)
    {
        var s = await _session.ResolveAsync(null, ct);
        var p = body.GetProperty("params");
        var name = p.GetProperty("name").GetString() ?? "";
        var args = p.TryGetProperty("arguments", out var a) ? a.GetRawText() : "{}";
        var (result, summary) = await _tools.DispatchAsync(name, args, s.StoreId, s.UserId, ct);
        return new
        {
            content = new[]
            {
                new { type = "text", text = JsonSerializer.Serialize(new { summary, result }, JsonOpts) },
            },
        };
    }

    private async Task<object> ReadResourceAsync(JsonElement body, CancellationToken ct)
    {
        await _session.ResolveAsync(null, ct);
        var uri = body.GetProperty("params").GetProperty("uri").GetString() ?? "";
        if (uri == "delegat://openapi/operator.json")
        {
            var json = JsonSerializer.Serialize(PlatformRequestPolicy.OpenApiSurface(), JsonOpts);
            return new { contents = new[] { new { uri, mimeType = "application/json", text = json } } };
        }

        if (uri == "delegat://store/schema")
        {
            return new { contents = new[] { new { uri, mimeType = "text/plain", text = AgentSqlGuard.SchemaDescription } } };
        }

        if (uri.StartsWith("delegat://cabinet/actions", StringComparison.Ordinal))
        {
            var q = uri.Contains('?') ? uri[(uri.IndexOf('?') + 1)..] : "";
            if (q.StartsWith("q=", StringComparison.OrdinalIgnoreCase))
                q = Uri.UnescapeDataString(q[2..]);
            var hits = _actions.Search(q);
            var json = JsonSerializer.Serialize(
                hits.Select(h => new
                {
                    method = h.Method,
                    path = h.Path,
                    write = h.Write,
                    note = h.Note,
                    callableViaJson = h.CallableViaJson,
                    transport = h.CallableViaJson ? null : h.Transport,
                }),
                JsonOpts);
            return new { contents = new[] { new { uri, mimeType = "application/json", text = json } } };
        }

        if (uri.StartsWith("delegat://docs/", StringComparison.Ordinal))
        {
            var q = uri["delegat://docs/".Length..];
            var hits = await _knowledge.SearchAsync(q, 5, ct);
            var text = string.Join("\n\n", hits.Select(h => $"# {h.Title}\n{h.Body}"));
            return new { contents = new[] { new { uri, mimeType = "text/markdown", text } } };
        }

        throw new InvalidOperationException("Unknown resource");
    }

    private async Task<object> ListToolsAsync(CancellationToken ct)
    {
        var session = await _session.ResolveAsync(null, ct);
        var tools = AgentToolCatalog.Build()
            .Where(t => t.name is not "propose_plan" and not "read_chat_attachment");
        if (AgentAccess.IsReadOnly(session.Permissions))
            tools = tools.Where(t => t.name is not "run_python" and not "memory_save" and not "memory_delete");
        return new
        {
            tools = tools.Select(t => new
            {
                name = t.name,
                description = t.description,
                inputSchema = t.input_schema,
            }),
        };
    }

    private async Task<object> ListResourcesAsync(CancellationToken ct)
    {
        await _session.ResolveAsync(null, ct);
        return new
        {
            resources = new object[]
            {
                new { uri = "delegat://openapi/operator.json", name = "Cabinet API denylist", mimeType = "application/json" },
                new { uri = "delegat://store/schema", name = "SQL schema", mimeType = "text/plain" },
                new { uri = "delegat://cabinet/actions", name = "Cabinet actions", mimeType = "application/json" },
                new { uri = "delegat://docs/index", name = "Knowledge search", mimeType = "text/markdown" },
            },
        };
    }

    private static object? UnwrapId(JsonElement id)
    {
        if (id.ValueKind == JsonValueKind.Undefined || id.ValueKind == JsonValueKind.Null) return null;
        if (id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var n)) return n;
        if (id.ValueKind == JsonValueKind.String) return id.GetString();
        return id.ToString();
    }
}
