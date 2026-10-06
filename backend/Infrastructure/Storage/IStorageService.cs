namespace Delegat.Infrastructure.Storage;

/// <summary>
/// Result of file upload with metadata
/// </summary>
public record FileUploadResult(string ContentType, long Length);

/// <summary>
/// Interface for object storage operations
/// </summary>
public interface IStorageService
{
    /// <summary>
    /// Upload a file from IFormFile
    /// </summary>
    Task<bool> UploadAsync(IFormFile file, string path, CancellationToken ct = default);

    /// <summary>
    /// Upload from stream
    /// </summary>
    Task<bool> UploadAsync(Stream stream, string path, long contentLength, string contentType, CancellationToken ct = default);

    /// <summary>
    /// Store a request body while it is still arriving.
    /// <paramref name="contentLength"/> is set only when the part declares its size.
    /// </summary>
    Task<FileUploadResult?> UploadStreamAsync(
        Stream stream,
        string path,
        string contentType,
        long? contentLength,
        CancellationToken ct = default)
    {
        return UploadCore(this, stream, path, contentType, contentLength, ct);

        static async Task<FileUploadResult?> UploadCore(
            IStorageService storage,
            Stream stream,
            string path,
            string contentType,
            long? contentLength,
            CancellationToken ct)
        {
            var length = contentLength ?? 0;
            var type = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType;
            var ok = await storage.UploadAsync(stream, path, length, type, ct);
            return ok ? new FileUploadResult(type, length) : null;
        }
    }

    /// <summary>
    /// Upload from URI
    /// </summary>
    Task<bool> UploadFromUriAsync(string uri, string path, CancellationToken ct = default);

    /// <summary>
    /// Upload from URI and return metadata (content type, length)
    /// </summary>
    Task<FileUploadResult?> UploadFromUriWithMetadataAsync(string uri, string path, CancellationToken ct = default);

    /// <summary>
    /// Server-side copy of an object to a new path in the same bucket.
    /// Returns true on success. Both objects exist after a successful copy.
    /// </summary>
    Task<bool> CopyAsync(string sourcePath, string destinationPath, CancellationToken ct = default);

    /// <summary>
    /// Delete a file
    /// </summary>
    Task<bool> DeleteAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Check if file exists
    /// </summary>
    Task<bool> ExistsAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Download a file as a stream
    /// </summary>
    Task<Stream?> GetStreamAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Get the full public URL for a file
    /// </summary>
    string GetFullPath(string path);

    /// <summary>
    /// Stream a file directly to an output stream without buffering
    /// </summary>
    Task StreamToAsync(string path, Stream outputStream, CancellationToken ct = default);

    /// <summary>
    /// Generate a presigned (temporary) URL for a file (uses internal endpoint)
    /// </summary>
    Task<string> GetPresignedUrlAsync(string path, int expirySeconds = 3600);

    /// <summary>
    /// Generate a presigned (temporary) URL using the public endpoint (for external consumers like marketplaces)
    /// </summary>
    Task<string> GetPublicPresignedUrlAsync(string path, int expirySeconds = 3600);

    /// <summary>
    /// List all object keys under a given prefix
    /// </summary>
    Task<List<string>> ListObjectsByPrefixAsync(string prefix, CancellationToken ct = default);

    /// <summary>
    /// Get the bucket name
    /// </summary>
    string Bucket { get; }
}
