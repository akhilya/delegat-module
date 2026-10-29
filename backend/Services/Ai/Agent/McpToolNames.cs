using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Delegat.Services.Ai.Agent;

public static class McpToolNames
{
    public const int MaxLength = 64;
    private const int HashHexLength = 16;
    private static readonly Regex ParseRx = new(@"^mcp([1-9]\d*)_(.+)$", RegexOptions.Compiled);

    public static string ForLlm(int serverId, string remoteName)
    {
        var sanitized = Sanitize(remoteName);
        if (sanitized.Length == 0) sanitized = "tool";
        var prefix = $"mcp{serverId}_";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(remoteName)))
            [..HashHexLength].ToLowerInvariant();
        var suffix = "_" + hash;
        var budget = MaxLength - prefix.Length - suffix.Length;
        if (budget < 1) return prefix[..MaxLength];
        if (sanitized.Length > budget) sanitized = sanitized[..budget];
        return prefix + sanitized + suffix;
    }

    public static bool TryParse(string? llmName, out int serverId, out string toolKey)
    {
        serverId = 0;
        toolKey = "";
        if (string.IsNullOrEmpty(llmName)) return false;
        var match = ParseRx.Match(llmName);
        if (!match.Success) return false;
        serverId = int.Parse(match.Groups[1].Value);
        toolKey = match.Groups[2].Value;
        return true;
    }

    private static string Sanitize(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '_' or '-') sb.Append(c);
            else if (char.IsWhiteSpace(c) || c is '.' or '/') sb.Append('_');
        }
        return sb.ToString().Trim('_');
    }
}
