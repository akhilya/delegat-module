namespace PrintO.Infrastructure.Storage;

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
    /// Delete a file
    /// </summary>
    Task<bool> DeleteAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Check if file exists
    /// </summary>
    Task<bool> ExistsAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Get the full public URL for a file
    /// </summary>
    string GetFullPath(string path);

    /// <summary>
    /// Get the bucket name
    /// </summary>
    string Bucket { get; }
}
