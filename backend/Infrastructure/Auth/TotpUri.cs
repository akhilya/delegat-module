using System.Text;

namespace Delegat.Infrastructure.Auth;

/// <summary>Builds Google Authenticator / Bitwarden otpauth URIs for TOTP enrollment.</summary>
public static class TotpUri
{
    public const string Issuer = "Delegat";

    public static string Build(string userName, string secret, string? issuer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        var compact = CompactSecret(secret);
        var issuerName = string.IsNullOrWhiteSpace(issuer) ? Issuer : issuer.Trim();
        var label = $"{Uri.EscapeDataString(issuerName)}:{Uri.EscapeDataString(userName.Trim())}";
        var issuerQuery = Uri.EscapeDataString(issuerName);
        return $"otpauth://totp/{label}?secret={compact}&issuer={issuerQuery}&digits=6&period=30";
    }

    public static string FormatSecret(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        var compact = CompactSecret(secret);
        var blocks = (compact.Length + 3) / 4;
        var sb = new StringBuilder(compact.Length + blocks);
        for (var i = 0; i < compact.Length; i += 4)
        {
            if (i > 0)
                sb.Append(' ');
            sb.Append(compact.AsSpan(i, Math.Min(4, compact.Length - i)));
        }

        return sb.ToString();
    }

    public static string NormalizeCode(string? code) =>
        (code ?? "").Replace(" ", "", StringComparison.Ordinal).Trim();

    private static string CompactSecret(string secret) =>
        secret.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
}
