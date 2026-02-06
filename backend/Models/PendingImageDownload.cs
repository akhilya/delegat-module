using PrintO.Models.Products;

namespace PrintO.Models;

/// <summary>
/// Status of an image download operation.
/// </summary>
public enum ImageDownloadStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Failed = 3
}

/// <summary>
/// Represents a queued background image download task.
/// </summary>
public class PendingImageDownload
{
    /// <summary>
    /// Unique record identifier.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Product identifier the image is being downloaded for.
    /// </summary>
    public int productId { get; set; }

    /// <summary>
    /// Navigation property to the associated product.
    /// </summary>
    public Product? product { get; set; }

    /// <summary>
    /// Store identifier.
    /// </summary>
    public int storeId { get; set; }

    /// <summary>
    /// Identifier of the user who initiated the import.
    /// </summary>
    public int authorUserId { get; set; }

    /// <summary>
    /// Source URL of the image to download.
    /// </summary>
    public string imageUrl { get; set; } = string.Empty;

    /// <summary>
    /// Order index of the image (for sorting).
    /// </summary>
    public int imageIndex { get; set; }

    /// <summary>
    /// Current download status.
    /// </summary>
    public ImageDownloadStatus status { get; set; } = ImageDownloadStatus.Pending;

    /// <summary>
    /// Number of download attempts made.
    /// </summary>
    public int retryCount { get; set; } = 0;

    /// <summary>
    /// Last error message if the download failed.
    /// </summary>
    public string? lastError { get; set; }

    /// <summary>
    /// Timestamp when the record was created.
    /// </summary>
    public DateTime createdAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp of the last download attempt.
    /// </summary>
    public DateTime? lastAttemptAt { get; set; }

    /// <summary>
    /// Timestamp when the download completed successfully.
    /// </summary>
    public DateTime? completedAt { get; set; }

    /// <summary>
    /// Maximum number of retry attempts allowed.
    /// </summary>
    public const int MAX_RETRIES = 5;
}
