namespace PrintO.Infrastructure.Storage;

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
    /// Upload from URI
    /// </summary>
    Task<bool> UploadFromUriAsync(string uri, string path, CancellationToken ct = default);

    /// <summary>
    /// Upload from URI and return metadata (content type, length)
    /// </summary>
    Task<FileUploadResult?> UploadFromUriWithMetadataAsync(string uri, string path, CancellationToken ct = default);

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
    /// Generate a presigned (temporary) URL for a file
    /// </summary>
    Task<string> GetPresignedUrlAsync(string path, int expirySeconds = 3600);

    /// <summary>
    /// Get the bucket name
    /// </summary>
    string Bucket { get; }
}
