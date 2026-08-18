using Delegat.Infrastructure;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Marketplace photo URL identity: ignore signed query strings and fragments.
/// </summary>
public static class ImageSourceUrl
{
    public const int MaxLength = Models.File.SourceUrlMaxLength;

    public static string? Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return trimmed;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return trimmed;

        var host = uri.Host.ToLowerInvariant();
        var hostPart = uri.IsDefaultPort ? host : $"{host}:{uri.Port}";
        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.Length == 0)
            path = "/";

        return $"{uri.Scheme.ToLowerInvariant()}://{hostPart}{path}";
    }

    public static bool EqualsNormalized(string? a, string? b)
    {
        var left = Normalize(a);
        var right = Normalize(b);
        if (left == null || right == null)
            return false;
        return string.Equals(left, right, StringComparison.Ordinal);
    }

    public static string? Clip(string? url) => StringClip.ClipOptional(url, MaxLength);
}
