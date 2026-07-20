using Microsoft.EntityFrameworkCore;
using Delegat.Enums;
using Delegat.Infrastructure.Storage;
using Delegat.Models;

namespace Delegat.Services;

/// <summary>
/// Background service that processes the pending image download queue.
/// Downloads images from external URLs and stores them in MinIO.
/// </summary>
public class ImageDownloadBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ImageDownloadBackgroundService> _logger;
    private readonly IOperationLogger _operationLogger;

    // Track which image batch operations have been started
    private readonly HashSet<string> _startedImageOps = new();

    // Settings
    private const int BATCH_SIZE = 5;              // Images to process per batch
    private const int DELAY_BETWEEN_IMAGES_MS = 500;  // Delay between images (ms)
    private const int POLL_INTERVAL_MS = 5000;     // Queue poll interval (ms)
    private const int ERROR_DELAY_MS = 30000;      // Delay after error (ms)
    private static readonly TimeSpan InProgressTimeout = TimeSpan.FromMinutes(30);

    public ImageDownloadBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<ImageDownloadBackgroundService> logger,
        IOperationLogger operationLogger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _operationLogger = operationLogger;
    }

    /// <summary>
    /// Main execution loop that polls for pending images and processes them in batches.
    /// </summary>
    /// <param name="stoppingToken">Token to signal graceful shutdown.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ImageDownloadBackgroundService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingImagesAsync(stoppingToken);
                await Task.Delay(POLL_INTERVAL_MS, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ImageDownloadBackgroundService");
                await Task.Delay(ERROR_DELAY_MS, stoppingToken);
            }
        }

        _logger.LogInformation("ImageDownloadBackgroundService stopped");
    }

    private async Task ProcessPendingImagesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DataContext>();
        var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();

        var staleInProgressBefore = DateTime.UtcNow - InProgressTimeout;

        // Fetch a batch of pending images. Stale InProgress rows are retried after a
        // worker restart/crash; otherwise they would keep the operation running forever.
        var pendingImages = await context.pendingImageDownloads
            .Where(p => p.status == ImageDownloadStatus.Pending ||
                       (p.status == ImageDownloadStatus.Failed && p.retryCount < PendingImageDownload.MAX_RETRIES) ||
                       (p.status == ImageDownloadStatus.InProgress &&
                        (p.lastAttemptAt ?? p.createdAt) < staleInProgressBefore))
            .OrderBy(p => p.createdAt)
            .ThenBy(p => p.imageIndex)
            .Take(BATCH_SIZE)
            .ToListAsync(ct);

        if (pendingImages.Count == 0)
            return;

        _logger.LogInformation("Processing {Count} images from the queue", pendingImages.Count);

        foreach (var pending in pendingImages)
        {
            if (ct.IsCancellationRequested)
                break;

            // Check if operation was paused or cancelled
            if (pending.batchId != null)
            {
                var opStatus = await context.operations.AsNoTracking()
                    .Where(o => o.operationId == pending.batchId)
                    .Select(o => o.status)
                    .FirstOrDefaultAsync(ct);

                if (opStatus is "paused" or "cancelled")
                {
                    _logger.LogInformation("Operation {BatchId} is in status {Status}, skipping download", pending.batchId, opStatus);
                    break;
                }

                // Update total count on first encounter
                if (!_startedImageOps.Contains(pending.batchId))
                {
                    var totalImages = await context.pendingImageDownloads.AsNoTracking()
                        .CountAsync(p => p.batchId == pending.batchId, ct);

                    // Operation may already exist (created by controller) or not (legacy)
                    var existingOp = await context.operations
                        .FirstOrDefaultAsync(o => o.operationId == pending.batchId, ct);
                    if (existingOp != null)
                    {
                        existingOp.totalItems = totalImages;
                        existingOp.title = $"Downloading {totalImages} images";
                        await context.SaveChangesAsync(ct);
                    }
                    else
                    {
                        await _operationLogger.StartAsync(pending.batchId, pending.storeId, pending.authorUserId,
                            "image-download", $"Downloading {totalImages} images",
                            batchId: pending.batchId, batchType: "image-download", totalItems: totalImages);
                    }

                    await _operationLogger.LogAsync(pending.batchId, pending.storeId, "info",
                        $"Started downloading {totalImages} images");
                    _startedImageOps.Add(pending.batchId);
                }
            }

            await ProcessSingleImageAsync(context, storageService, pending, ct);

            // Report progress for batched images
            if (pending.batchId != null)
            {
                var batchItems = await context.pendingImageDownloads.AsNoTracking()
                    .Where(p => p.batchId == pending.batchId).ToListAsync(ct);
                var total = batchItems.Count;
                var completed = batchItems.Count(p => p.status == ImageDownloadStatus.Completed);
                var failed = batchItems.Count(p => p.status == ImageDownloadStatus.Failed);
                await _operationLogger.ProgressAsync(pending.batchId, pending.storeId, total, completed, failed);

                if (total == completed + failed)
                    await _operationLogger.FinishAsync(pending.batchId, pending.storeId,
                        failed == 0 ? "completed" : (completed == 0 ? "failed" : "partial"),
                        $"Downloaded: {completed} succeeded, {failed} failed");
            }

            // Delay between images to avoid overloading the CDN
            if (!ct.IsCancellationRequested)
                await Task.Delay(DELAY_BETWEEN_IMAGES_MS, ct);
        }
    }

    private async Task ProcessSingleImageAsync(
        DataContext context,
        IStorageService storageService,
        PendingImageDownload pending,
        CancellationToken ct)
    {
        pending.status = ImageDownloadStatus.InProgress;
        pending.lastAttemptAt = DateTime.UtcNow;
        pending.retryCount++;
        await context.SaveChangesAsync(ct);

        // Helper: log to operation panel (no-op for images without a batch)
        async Task Log(string level, string message)
        {
            if (pending.batchId != null)
                await _operationLogger.LogAsync(pending.batchId, pending.storeId, level, message);
        }

        var shortUrl = pending.imageUrl.Length > 60
            ? pending.imageUrl[..57] + "..."
            : pending.imageUrl;

        try
        {
            // Generate the file path
            var fileId = Guid.NewGuid().ToString("N");
            var extension = GetExtensionFromUrl(pending.imageUrl) ?? ".jpg";
            var filePath = $"stores/{pending.storeId}/products/{pending.productId}/images/{fileId}{extension}";

            // Download and upload to storage
            var uploadResult = await storageService.UploadFromUriWithMetadataAsync(pending.imageUrl, filePath, ct);

            if (uploadResult == null)
            {
                pending.status = ImageDownloadStatus.Failed;
                pending.lastError = "Failed to download image";
                await context.SaveChangesAsync(ct);

                _logger.LogWarning(
                    "Failed to download image {ImageUrl} for product {ProductId} (attempt {Retry})",
                    pending.imageUrl, pending.productId, pending.retryCount);

                await Log("warning", $"Failed to download: {shortUrl} (attempt {pending.retryCount})");
                return;
            }

            // Create the File record
            var file = new Models.File();
            file.AddFill(new Models.File.AddForm(
                filePath,
                pending.authorUserId,
                pending.productId,
                uploadResult.ContentType,
                uploadResult.Length
            ));
            file.fileType = FileType.Image;

            context.files.Add(file);
            await context.SaveChangesAsync(ct);

            // Check whether an image with this index already exists
            var existingImage = await context.images
                .FirstOrDefaultAsync(i => i.productId == pending.productId && i.index == pending.imageIndex, ct);

            if (existingImage == null)
            {
                // Create the ImageReference
                var imageRef = new ImageReference();
                imageRef.AddFill(new ImageReference.AddForm(
                    file.Id,
                    pending.productId,
                    pending.imageIndex
                ));

                context.images.Add(imageRef);
            }
            else
            {
                // Update the existing one
                existingImage.fileId = file.Id;
            }

            // Mark as completed
            pending.status = ImageDownloadStatus.Completed;
            pending.completedAt = DateTime.UtcNow;
            pending.lastError = null;

            await context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Downloaded image {Index} for product {ProductId}: {ImageUrl}",
                pending.imageIndex, pending.productId, pending.imageUrl);
        }
        catch (Exception ex)
        {
            pending.status = ImageDownloadStatus.Failed;
            pending.lastError = ex.Message;
            await context.SaveChangesAsync(CancellationToken.None);

            _logger.LogError(ex,
                "Error downloading image {ImageUrl} for product {ProductId}",
                pending.imageUrl, pending.productId);

            await Log("error", $"Error downloading image #{pending.imageIndex}: {ex.Message}");
        }
    }

    private static string? GetExtensionFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var path = uri.AbsolutePath;
            var ext = Path.GetExtension(path);
            return string.IsNullOrEmpty(ext) ? null : ext.ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }
}
