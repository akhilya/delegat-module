using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Delegat.Services.PlatformLink;

public static class PlatformLinkHmac
{
    public const string HeaderInstanceId = "X-Instance-Id";
    public const string HeaderTimestamp = "X-Timestamp";
    public const string HeaderSignature = "X-Signature";

    public static string Sha256Hex(ReadOnlySpan<byte> data)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(data, hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string Sha256Hex(string text) =>
        Sha256Hex(Encoding.UTF8.GetBytes(text));

    public static string BuildCanonical(string method, string pathAndQuery, long unixSeconds, string bodySha256Hex) =>
        $"{method.ToUpperInvariant()}\n{pathAndQuery}\n{unixSeconds.ToString(CultureInfo.InvariantCulture)}\n{bodySha256Hex}";

    public static string Sign(string secret, string canonical)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var data = Encoding.UTF8.GetBytes(canonical);
        var hash = HMACSHA256.HashData(key, data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool TimestampsWithinSkew(long unixSeconds, TimeSpan skew, DateTimeOffset? now = null)
    {
        var n = now ?? DateTimeOffset.UtcNow;
        var delta = Math.Abs(n.ToUnixTimeSeconds() - unixSeconds);
        return delta <= (long)skew.TotalSeconds;
    }
}
