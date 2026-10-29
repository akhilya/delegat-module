using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Delegat;

namespace Delegat.Services.Ai.Agent;

public sealed class McpHttpClient
{
    public const string ProtocolVersion = "2025-03-26";
    public const int MaxResponseBytes = 256_000;

    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<(int ServerId, string Url), ToolSessionSnapshot> _turnSessions = new();

    public McpHttpClient(HttpClient http) => _http = http;

    public async Task<List<AiTool>> ListToolsAsync(int serverId, string url, string? bearerToken, CancellationToken ct)
    {
        var snapshot = await GetToolSessionAsync(serverId, url, bearerToken, ct);
        return snapshot.Tools;
    }

    public async Task<(object result, string summary)> CallToolAsync(
        string url, string? bearerToken, string remoteName, string inputJson, CancellationToken ct)
    {
        var session = await HandshakeAsync(url, bearerToken, ct);
        return await CallOnSessionAsync(session, remoteName, inputJson, ct);
    }

    public async Task<(object result, string summary)> CallMappedToolAsync(
        int serverId, string url, string? bearerToken, string llmName, string inputJson, CancellationToken ct)
    {
        var snapshot = await GetToolSessionAsync(serverId, url, bearerToken, ct);
        if (!snapshot.RemoteNamesByLlmName.TryGetValue(llmName, out var remote))
            return (new { error = $"Unknown tool: {llmName}" }, $"Unknown tool: {llmName}");
        return await CallOnSessionAsync(snapshot.Session, remote, inputJson, ct);
    }

    private async Task<ToolSessionSnapshot> GetToolSessionAsync(
        int serverId, string url, string? bearerToken, CancellationToken ct)
    {
        var normalizedUrl = url.Trim();
        var key = (serverId, normalizedUrl);
        if (_turnSessions.TryGetValue(key, out var cached)
            && string.Equals(cached.Session.BearerToken, bearerToken, StringComparison.Ordinal))
            return cached;

        var session = await HandshakeAsync(normalizedUrl, bearerToken, ct);
        var listed = await RpcAsync(session, "tools/list", new JsonObject(), ct);
        var tools = new List<AiTool>();
        var remoteNamesByLlmName = new Dictionary<string, string>(StringComparer.Ordinal);
        if (listed.TryGetProperty("tools", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                if (tools.Count >= McpToolMapper.MaxToolsPerServer) break;
                var mapped = McpToolMapper.ToAiTool(serverId, item);
                if (mapped == null || !remoteNamesByLlmName.TryAdd(mapped.name, mapped.McpRemoteName!))
                    continue;
                tools.Add(mapped);
            }
        }

        var snapshot = new ToolSessionSnapshot(session, tools, remoteNamesByLlmName);
        _turnSessions[key] = snapshot;
        return snapshot;
    }

    private async Task<(object result, string summary)> CallOnSessionAsync(
        McpSession session, string remoteName, string inputJson, CancellationToken ct)
    {
        JsonNode args = new JsonObject();
        if (!string.IsNullOrWhiteSpace(inputJson))
        {
            try { args = JsonNode.Parse(inputJson) ?? new JsonObject(); }
            catch (JsonException) { args = new JsonObject(); }
        }
        var result = await RpcAsync(session, "tools/call", new JsonObject
        {
            ["name"] = remoteName,
            ["arguments"] = args,
        }, ct);
        var text = SummarizeCall(result);
        var isError = result.TryGetProperty("isError", out var err) && err.ValueKind == JsonValueKind.True;
        return (JsonSerializer.Deserialize<object>(result.GetRawText()) ?? new { }, isError ? $"{remoteName} failed" : $"{remoteName}: {text}");
    }

    private async Task<McpSession> HandshakeAsync(string url, string? bearerToken, CancellationToken ct)
    {
        var session = new McpSession(url.Trim(), bearerToken, ProtocolVersion, sessionId: null);
        var init = await RpcAsync(session, "initialize", new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject
            {
                ["name"] = "delegat-module",
                ["version"] = PlatformVersion.Current,
            },
        }, ct);
        var version = init.TryGetProperty("protocolVersion", out var v) ? v.GetString() : ProtocolVersion;
        if (!string.IsNullOrWhiteSpace(version))
            session.ProtocolVersion = version!;
        await NotifyAsync(session, "notifications/initialized", ct);
        return session;
    }

    private async Task NotifyAsync(McpSession session, string method, CancellationToken ct)
    {
        using var req = BuildRequest(session, new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = new JsonObject(),
        });
        using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        CaptureSession(res, session);
        if ((int)res.StatusCode >= 400)
            throw new InvalidOperationException($"MCP {method} failed ({(int)res.StatusCode})");
    }

    private async Task<JsonElement> RpcAsync(McpSession session, string method, JsonNode? @params, CancellationToken ct)
    {
        var id = session.NextId();
        using var req = BuildRequest(session, new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = @params ?? new JsonObject(),
        });
        using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        CaptureSession(res, session);
        if ((int)res.StatusCode >= 400)
        {
            var errBody = await ReadContentLimitedAsync(res.Content, ct);
            throw new InvalidOperationException(RpcErrorMessage(errBody) ?? $"MCP {method} failed ({(int)res.StatusCode})");
        }
        var payload = await ReadRpcPayloadAsync(res, ct);
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out var error))
        {
            var message = error.TryGetProperty("message", out var m) ? m.GetString() : "MCP error";
            throw new InvalidOperationException(message ?? "MCP error");
        }
        if (!root.TryGetProperty("result", out var result))
            throw new InvalidOperationException($"MCP {method} returned no result");
        return result.Clone();
    }

    private static HttpRequestMessage BuildRequest(McpSession session, JsonObject body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, session.Url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        req.Headers.TryAddWithoutValidation("MCP-Protocol-Version", session.ProtocolVersion);
        if (!string.IsNullOrWhiteSpace(session.SessionId))
            req.Headers.TryAddWithoutValidation("Mcp-Session-Id", session.SessionId);
        if (!string.IsNullOrWhiteSpace(session.BearerToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.BearerToken);
        return req;
    }

    private static void CaptureSession(HttpResponseMessage res, McpSession session)
    {
        if (res.Headers.TryGetValues("Mcp-Session-Id", out var values))
        {
            var id = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(id)) session.SessionId = id;
        }
    }

    private static async Task<string> ReadRpcPayloadAsync(HttpResponseMessage res, CancellationToken ct)
    {
        var media = res.Content.Headers.ContentType?.MediaType ?? "";
        var raw = await ReadContentLimitedAsync(res.Content, ct);
        if (media.Contains("event-stream", StringComparison.OrdinalIgnoreCase) || raw.Contains("data:"))
            return ParseSseJson(raw);
        return raw;
    }

    private static async Task<string> ReadContentLimitedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream(capacity: Math.Min(MaxResponseBytes, 16_384));
        var chunk = new byte[8_192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, ct);
            if (read == 0) break;
            if (buffer.Length + read > MaxResponseBytes)
                throw new InvalidOperationException($"MCP response exceeds the {MaxResponseBytes}-byte limit");
            buffer.Write(chunk, 0, read);
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    internal static string ParseSseJson(string raw)
    {
        foreach (var line in raw.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (!trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var data = trimmed[5..].Trim();
            if (data.Length == 0 || data == "[DONE]") continue;
            if (data.StartsWith('{')) return data;
        }
        throw new InvalidOperationException("MCP SSE stream had no JSON payload");
    }

    private static string? RpcErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var m))
                return m.GetString();
        }
        catch (JsonException) { }
        return null;
    }

    private static string SummarizeCall(JsonElement result)
    {
        if (result.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            var texts = content.EnumerateArray()
                .Select(x => x.TryGetProperty("text", out var t) ? t.GetString() : null)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
            if (texts.Count > 0)
            {
                var joined = string.Join(" ", texts);
                return joined.Length > 180 ? joined[..180] : joined;
            }
        }
        var raw = result.GetRawText();
        return raw.Length > 180 ? raw[..180] : raw;
    }

    private sealed class McpSession(string url, string? bearerToken, string protocolVersion, string? sessionId)
    {
        private int _id;
        public string Url { get; } = url;
        public string? BearerToken { get; } = bearerToken;
        public string ProtocolVersion { get; set; } = protocolVersion;
        public string? SessionId { get; set; } = sessionId;
        public int NextId() => Interlocked.Increment(ref _id);
    }

    private sealed record ToolSessionSnapshot(
        McpSession Session,
        List<AiTool> Tools,
        Dictionary<string, string> RemoteNamesByLlmName);
}
