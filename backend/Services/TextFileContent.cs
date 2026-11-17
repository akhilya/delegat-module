using System.Text;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Security;

namespace Delegat.Services;

/// <summary>
/// Shared validation for the bounded from-text payloads: a safe display name,
/// an allow-listed text MIME type, and the exact UTF-8 bytes to store.
/// </summary>
internal sealed record TextFileContent(string FileName, string ContentType, byte[] Bytes)
{
    /// <summary>Strict UTF-8 byte cap for one from-text file.</summary>
    public const int MaxTextBytes = 256 * 1024; // 262144

    /// <summary>Cap on the JSON request envelope (escaped text plus fields).</summary>
    public const long MaxRequestBytes = 2 * 1024 * 1024;

    public const string DefaultContentType = "text/plain";

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/plain",
        "text/csv",
        "application/json",
    };

    /// <summary>
    /// Validates file name, MIME type, and text; returns the bytes to store.
    /// Throws <see cref="ApiException"/> before any storage access.
    /// </summary>
    public static TextFileContent Create(string? fileName, string? contentType, string? text)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw ApiException.BadRequest(code: "file_name_required");
        if (text == null)
            throw ApiException.BadRequest(code: "text_required");

        var safeName = FileNameSafety.Sanitize(fileName);
        var type = NormalizeContentType(contentType)
            ?? throw ApiException.BadRequest(code: "content_type_not_allowed");

        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > MaxTextBytes)
            throw ApiException.BadRequest(code: "text_too_large");

        return new TextFileContent(safeName, type, bytes);
    }

    private static string? NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return DefaultContentType;

        var type = contentType.Split(';', 2)[0].Trim();
        return AllowedContentTypes.Contains(type) ? type.ToLowerInvariant() : null;
    }
}
