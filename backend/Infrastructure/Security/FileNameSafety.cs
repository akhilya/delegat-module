namespace Delegat.Infrastructure.Security;

/// <summary>
/// Sanitizes user-supplied filenames for storage object keys.
/// </summary>
public static class FileNameSafety
{
    public static string Sanitize(string? fileName, string fallback = "file")
    {
        var name = Path.GetFileName(fileName?.Replace('\\', '/') ?? "");
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
            name = fallback;

        // Strip control chars and path separators that might survive GetFileName on some platforms
        var cleaned = new string(name.Where(c =>
            !char.IsControl(c) && c != '/' && c != '\\' && c != '\0').ToArray());

        if (string.IsNullOrWhiteSpace(cleaned))
            cleaned = fallback;

        // Cap length to avoid oversized object keys
        return cleaned.Length > 200 ? cleaned[..200] : cleaned;
    }
}
