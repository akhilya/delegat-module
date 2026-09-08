using Delegat.Services.Ai;

namespace Delegat.Services.Ai.Agent;

public interface IMcpToolGateway
{
    Task<IReadOnlyList<AiTool>> ListToolsAsync(CancellationToken ct);
    Task<(object result, string summary)> CallAsync(string llmName, string inputJson, CancellationToken ct);
}

public sealed class McpToolGateway : IMcpToolGateway
{
    private readonly AiMcpServerService _servers;
    private readonly McpHttpClient _http;
    private readonly ILogger<McpToolGateway> _logger;

    public McpToolGateway(AiMcpServerService servers, McpHttpClient http, ILogger<McpToolGateway> logger)
    {
        _servers = servers;
        _http = http;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AiTool>> ListToolsAsync(CancellationToken ct)
    {
        var rows = await _servers.GetEnabledAsync(ct);
        var tools = new List<AiTool>();
        foreach (var row in rows)
        {
            try
            {
                var listed = await _http.ListToolsAsync(row.Id, row.url, _servers.DecryptApiKey(row), ct);
                tools.AddRange(listed);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MCP server {Id} ({Name}) tools/list failed", row.Id, row.name);
            }
        }
        return tools;
    }

    public async Task<(object result, string summary)> CallAsync(string llmName, string inputJson, CancellationToken ct)
    {
        if (!McpToolNames.TryParse(llmName, out var serverId, out _))
            return (new { error = $"Unknown tool: {llmName}" }, $"Unknown tool: {llmName}");

        var row = await _servers.GetByIdAsync(serverId, ct);
        if (row == null || !row.isEnabled)
            return (new { error = "mcp_server_unavailable" }, "Connected tool is unavailable");

        try
        {
            return await _http.CallMappedToolAsync(row.Id, row.url, _servers.DecryptApiKey(row), llmName, inputJson, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP tool {Tool} on server {Id} failed", llmName, serverId);
            return (new { error = ex.Message }, $"{llmName} failed");
        }
    }
}
