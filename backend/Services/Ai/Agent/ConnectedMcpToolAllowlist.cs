using Delegat.Services.Ai;

namespace Delegat.Services.Ai.Agent;

public static class ConnectedMcpToolAllowlist
{
    public const string EnvironmentVariable = "AI_CONNECTED_MCP_TOOL_ALLOWLIST";

    /// <summary>
    /// Entries are exact remote tool names scoped to a database MCP server id:
    /// <c>2:search_docs;2:fetch_document</c>. Wildcards are intentionally unsupported.
    /// </summary>
    public static HashSet<(int ServerId, string RemoteTool)> Parse(string? raw)
    {
        var entries = new HashSet<(int ServerId, string RemoteTool)>();
        if (string.IsNullOrWhiteSpace(raw))
            return entries;

        foreach (var entry in raw.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var value = entry.Trim();
            var separator = value.IndexOf(':');
            if (separator <= 0
                || separator == value.Length - 1
                || !int.TryParse(value[..separator], out var serverId)
                || serverId <= 0)
                continue;

            var remoteTool = value[(separator + 1)..].Trim();
            if (remoteTool.Length == 0 || remoteTool == "*")
                continue;

            entries.Add((serverId, remoteTool));
        }

        return entries;
    }

    public static bool IsAllowed(string? raw, int serverId, string? remoteTool) =>
        serverId > 0
        && !string.IsNullOrWhiteSpace(remoteTool)
        && Parse(raw).Contains((serverId, remoteTool));

    public static bool IsAllowed(int serverId, string? remoteTool) =>
        IsAllowed(Environment.GetEnvironmentVariable(EnvironmentVariable), serverId, remoteTool);

    public static IReadOnlyList<AiTool> FilterTools(
        string? raw,
        int serverId,
        IEnumerable<AiTool> tools) =>
        tools.Where(tool => IsAllowed(raw, serverId, tool.McpRemoteName)).ToList();
}
