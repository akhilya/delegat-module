using System.Security.Cryptography;
using System.Text;

namespace Delegat.Services.Marketplace.Graph;

/// <summary>
/// Content version of a stored mapping graph. The editor sends it back on save so a graph changed
/// elsewhere (another tab, the assistant) is not silently overwritten.
/// </summary>
public static class MappingGraphVersion
{
    public const string Empty = "empty";

    public static string Compute(string? graphJson)
    {
        if (string.IsNullOrEmpty(graphJson))
            return Empty;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(graphJson));
        return Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }

    public static bool Matches(string? graphJson, string? expected) =>
        string.IsNullOrEmpty(expected)
        || string.Equals(Compute(graphJson), expected, StringComparison.OrdinalIgnoreCase);
}
