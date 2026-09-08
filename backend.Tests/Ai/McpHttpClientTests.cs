using System.Net;
using System.Text;
using System.Text.Json;
using Delegat.Services.Ai.Agent;

namespace Delegat.Tests;

public class McpHttpClientTests
{
    [Fact]
    public async Task ListToolsAsync_InitializeThenListsAndMapsSchemas()
    {
        var handler = new ScriptedHandler(
            InitializeOk(),
            Accepted(),
            JsonRpc(3, """{"tools":[{"name":"get_weather","description":"Weather","inputSchema":{"type":"object","properties":{"location":{"type":"string"}},"required":["location"]}}]}"""));
        var client = new McpHttpClient(new HttpClient(handler) { BaseAddress = new Uri("http://unused/") });

        var tools = await client.ListToolsAsync(12, "https://mcp.example.test/mcp", "secret-token", default);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("initialize", MethodOf(handler.Bodies[0]));
        Assert.Equal("notifications/initialized", MethodOf(handler.Bodies[1]));
        Assert.Equal("tools/list", MethodOf(handler.Bodies[2]));
        Assert.Contains("Bearer secret-token", handler.Requests[0].Headers.Authorization?.ToString());
        Assert.Equal("mcp12_get_weather", tools[0].name);
        Assert.Equal("get_weather", tools[0].McpRemoteName);
        Assert.Equal("Weather", tools[0].description);
        Assert.Contains("location", tools[0].input_schema.required!);
    }

    [Fact]
    public async Task CallToolAsync_PostsRemoteNameNotLlmPrefix()
    {
        var handler = new ScriptedHandler(
            InitializeOk(),
            Accepted(),
            JsonRpc(3, """{"content":[{"type":"text","text":"72F"}],"isError":false}"""));
        var client = new McpHttpClient(new HttpClient(handler));

        var (result, summary) = await client.CallToolAsync(
            "https://mcp.example.test/mcp", null, "get_weather", """{"location":"NYC"}""", default);

        Assert.Equal("tools/call", MethodOf(handler.Bodies[2]));
        var body = handler.Bodies[2];
        Assert.Contains("\"name\":\"get_weather\"", body.Replace(" ", ""));
        Assert.DoesNotContain("mcp12_", body);
        Assert.Contains("72F", JsonSerializer.Serialize(result));
        Assert.Contains("get_weather", summary);
    }

    [Fact]
    public async Task CallMappedToolAsync_ResolvesSanitizedLlmNameToOriginalRemote()
    {
        var handler = new ScriptedHandler(
            InitializeOk(),
            Accepted(),
            JsonRpc(3, """{"tools":[{"name":"get weather!","inputSchema":{"type":"object"}}]}"""),
            JsonRpc(4, """{"content":[{"type":"text","text":"ok"}]}"""));
        var client = new McpHttpClient(new HttpClient(handler));

        var (_, summary) = await client.CallMappedToolAsync(
            12, "https://mcp.example.test/mcp", null, "mcp12_get_weather", "{}", default);

        Assert.Equal("tools/list", MethodOf(handler.Bodies[2]));
        Assert.Equal("tools/call", MethodOf(handler.Bodies[3]));
        Assert.Contains("\"name\":\"get weather!\"", handler.Bodies[3]);
        Assert.DoesNotContain("mcp12_", handler.Bodies[3]);
        Assert.Contains("get weather!", summary);
    }

    [Fact]
    public async Task ListToolsAsync_ReadsJsonRpcFromSseStream()
    {
        var sse = "event: message\ndata: {\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"protocolVersion\":\"2025-03-26\",\"capabilities\":{},\"serverInfo\":{\"name\":\"s\"}}}\n\n";
        var handler = new ScriptedHandler(
            Sse(sse),
            Accepted(),
            JsonRpc(3, """{"tools":[{"name":"ping","inputSchema":{"type":"object"}}]}"""));
        var client = new McpHttpClient(new HttpClient(handler));

        var tools = await client.ListToolsAsync(1, "https://mcp.example.test/mcp", null, default);

        Assert.Equal("mcp1_ping", tools[0].name);
    }

    private static string MethodOf(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("method").GetString()!;
    }

    private static HttpResponseMessage InitializeOk() =>
        JsonRpc(1, """{"protocolVersion":"2025-03-26","capabilities":{},"serverInfo":{"name":"demo"}}""");

    private static HttpResponseMessage Accepted() =>
        new(HttpStatusCode.Accepted);

    private static HttpResponseMessage JsonRpc(int id, string resultJson) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"jsonrpc":"2.0","id":{{id}},"result":{{resultJson}}}""",
                Encoding.UTF8,
                "application/json"),
        };

    private static HttpResponseMessage Sse(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
        };

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        public ScriptedHandler(params HttpResponseMessage[] responses) =>
            _responses = new Queue<HttpResponseMessage>(responses);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            if (_responses.Count == 0)
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            return _responses.Dequeue();
        }
    }
}
