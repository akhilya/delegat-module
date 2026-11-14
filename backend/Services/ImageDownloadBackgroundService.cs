using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.StaticFiles;
using Delegat.Enums;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Storage;
using Delegat.Models;

namespace Delegat.Services;

/// <summary>
/// Background service that processes the pending image download queue.
/// Downloads images from external URLs and stores them in MinIO.
/// </summary>
public class ImageDownloadBackgroundService : BackgroundService
{
    private const long MaxImportedVideoBytes = 500L * 1024 * 1024;
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
        var quotaService = scope.ServiceProvider.GetRequiredService<QuotaService>();

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
        {
            // Pre-created empty "-img" ops stay running until the first image is queued.
            // Once the parent import is terminal and the queue is idle, close them.
            var closed = await FinishIdleImageDownloadOpsAsync(context, _operationLogger, ct);
            if (closed > 0)
                _logger.LogInformation("Closed {Count} idle image-download operation(s)", closed);
            return;
        }

        _logger.LogInformation("Processing {Count} images from the queue", pendingImages.Count);

        foreach (var pending in pendingImages)
        {
            if (ct.IsCancellationRequested)
                break;

            if (pending.batchId != null)
            {
                if (await OperationLifecycle.IsStopRequestedAsync(context, pending.batchId, ct))
                {
                    var cancelled = await QueryBatchRowsToCancel(context, pending.batchId)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ImageDownloadStatus.Cancelled), ct);
                    _logger.LogInformation(
                        "Operation {BatchId} was stopped; marked {Count} queue row(s) as Cancelled",
                        pending.batchId, cancelled);
                    await _operationLogger.FinishAsync(pending.batchId, pending.storeId, "cancelled", "Stopped");
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

            await ProcessSingleImageAsync(context, storageService, quotaService, pending, ct);

            // Report progress for batched images
            if (pending.batchId != null)
            {
                var counts = await context.pendingImageDownloads.AsNoTracking()
                    .Where(p => p.batchId == pending.batchId)
                    .GroupBy(p => new { p.status, p.retryCount })
                    .Select(g => new { g.Key.status, g.Key.retryCount, Count = g.Count() })
                    .ToListAsync(ct);
                var total = counts.Sum(g => g.Count);
                var completed = counts.Where(g => g.status == ImageDownloadStatus.Completed).Sum(g => g.Count);
                var failed = counts.Where(g => g.status == ImageDownloadStatus.Failed).Sum(g => g.Count);
                var cancelled = counts.Where(g => g.status == ImageDownloadStatus.Cancelled).Sum(g => g.Count);
                await _operationLogger.ProgressAsync(pending.batchId, pending.storeId, total, completed, failed);

                var hasActive = counts.Any(p =>
                    p.status == ImageDownloadStatus.Pending
                    || p.status == ImageDownloadStatus.InProgress
                    || (p.status == ImageDownloadStatus.Failed
                        && p.retryCount < PendingImageDownload.MAX_RETRIES));

                if (!hasActive)
                {
                    var status = failed == 0
                        ? (completed == 0 ? (cancelled > 0 ? "cancelled" : "completed") : "completed")
                        : (completed == 0 ? "failed" : "partial");
                    var summary = cancelled > 0
                        ? $"Downloaded: {completed} succeeded, {failed} failed, {cancelled} cancelled"
                        : $"Downloaded: {completed} succeeded, {failed} failed";
                    await _operationLogger.FinishAsync(pending.batchId, pending.storeId, status, summary);
                }
            }

            if (!ct.IsCancellationRequested
                && (pending.batchId == null
                    || !await OperationLifecycle.IsStopRequestedAsync(context, pending.batchId, ct)))
                await Task.Delay(DELAY_BETWEEN_IMAGES_MS, ct);
        }
    }

    internal async Task ProcessSingleImageAsync(
        DataContext context,
        IStorageService storageService,
        QuotaService quotaService,
        PendingImageDownload pending,
        CancellationToken ct)
    {
        if (pending.skipGallery)
        {
            var target = await ResolveFileAttributeTargetAsync(context, pending, ct);
            if (target == null
                || (!target.AllowMultipleFiles
                    && await HasTargetFilesAsync(context, pending.productId, target, ct)))
            {
                pending.status = ImageDownloadStatus.Completed;
                pending.completedAt = DateTime.UtcNow;
                pending.lastError = null;
                await context.SaveChangesAsync(ct);
                return;
            }
        }

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

        var shortUrl = SafeUrlForLog(pending.imageUrl);
        var isVideo = pending.mediaKind == PendingMediaKind.Video;
        string? uploadedFilePath = null;
        Models.File? importedFile = null;
        ImageReference? galleryReference = null;
        ProductAttributeValue? productAttributeValue = null;
        StoreProductAttributeValue? storeProductAttributeValue = null;
        ProductAttributeValue? newProductAttributeValue = null;
        StoreProductAttributeValue? newStoreProductAttributeValue = null;
        ProductAttributeFile? productAttributeFile = null;
        StoreProductAttributeFile? storeProductAttributeFile = null;
        string? previousProductAttributeValue = null;
        string? previousStoreAttributeValue = null;

        try
        {
            try
            {
                await quotaService.EnsureFileQuotaAsync(pending.storeId);
            }
            catch (ApiException ex) when (ex.Code is "quota_files_reached")
            {
                await FailWithoutRetryAsync(context, pending, ex.Message, Log);
                return;
            }

            var fileId = Guid.NewGuid().ToString("N");
            var urlExtension = isVideo ? null : GetExtensionFromUrl(pending.imageUrl);
            var extension = isVideo
                ? GetVideoExtension(pending.mediaFileName)
                : urlExtension ?? ".jpg";
            var mediaFolder = isVideo ? "media" : "images";
            var filePath = $"stores/{pending.storeId}/products/{pending.productId}/{mediaFolder}/{fileId}{extension}";
            uploadedFilePath = filePath;

            var uploadResult = isVideo
                ? await storageService.UploadVideoFromUriWithMetadataAsync(
                    pending.imageUrl, filePath, MaxImportedVideoBytes, ct)
                : await storageService.UploadFromUriWithMetadataAsync(pending.imageUrl, filePath, ct);

            if (uploadResult == null)
            {
                await storageService.DeleteAsync(filePath, CancellationToken.None);
                pending.status = ImageDownloadStatus.Failed;
                pending.lastError = isVideo ? "Failed to download a supported video file" : "Failed to download image";
                await context.SaveChangesAsync(ct);

                _logger.LogWarning(
                    "Failed to download media {MediaUrl} for product {ProductId} (attempt {Retry})",
                    shortUrl, pending.productId, pending.retryCount);

                await Log("warning", $"Failed to download: {shortUrl} (attempt {pending.retryCount})");
                return;
            }

            try
            {
                await quotaService.EnsureStorageQuotaAsync(pending.storeId, uploadResult.Length);
            }
            catch (ApiException ex) when (ex.Code is "quota_storage_reached")
            {
                await storageService.DeleteAsync(filePath, ct);
                await FailWithoutRetryAsync(context, pending, ex.Message, Log);
                return;
            }

            var target = pending.targetAttributeId.HasValue || pending.targetStoreAttributeId.HasValue
                ? await ResolveFileAttributeTargetAsync(context, pending, ct)
                : null;
            var canAttachToAttribute = target != null;
            if (target != null)
            {
                if (target.IsStoreAttribute)
                {
                    storeProductAttributeValue = await context.storeProductAttributeValues
                        .Include(value => value.files)
                        .FirstOrDefaultAsync(value =>
                            value.productId == pending.productId
                            && value.storeAttributeId == target.AttributeId, ct);
                    canAttachToAttribute = target.AllowMultipleFiles
                        || storeProductAttributeValue == null
                        || storeProductAttributeValue.files.Count == 0;
                }
                else
                {
                    productAttributeValue = await context.productAttributeValues
                        .Include(value => value.files)
                        .FirstOrDefaultAsync(value =>
                            value.productId == pending.productId
                            && value.attributeId == target.AttributeId, ct);
                    canAttachToAttribute = target.AllowMultipleFiles
                        || productAttributeValue == null
                        || productAttributeValue.files.Count == 0;
                }
            }

            if (pending.skipGallery && !canAttachToAttribute)
            {
                await storageService.DeleteAsync(filePath, CancellationToken.None);
                uploadedFilePath = null;
                pending.status = ImageDownloadStatus.Completed;
                pending.completedAt = DateTime.UtcNow;
                pending.lastError = null;
                await context.SaveChangesAsync(ct);
                return;
            }

            var file = new Models.File();
            file.AddFill(new Models.File.AddForm(
                filePath,
                pending.authorUserId,
                pending.productId,
                uploadResult.ContentType,
                uploadResult.Length,
                pending.imageUrl
            ));
            var isExtensionlessNonImage = !isVideo
                && urlExtension == null
                && !uploadResult.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
            file.fileType = isVideo || isExtensionlessNonImage ? FileType.Other : FileType.Image;
            if (isVideo)
                file.displayName = pending.mediaFileName;
            else if (isExtensionlessNonImage)
            {
                var contentTypeExtension = new FileExtensionContentTypeProvider().Mappings
                    .FirstOrDefault(mapping => string.Equals(
                        mapping.Value,
                        uploadResult.ContentType,
                        StringComparison.OrdinalIgnoreCase))
                    .Key;
                if (!string.IsNullOrEmpty(contentTypeExtension))
                    file.displayName = $"{fileId}{contentTypeExtension}";
            }

            importedFile = file;
            context.files.Add(file);

            if (!pending.skipGallery)
            {
                var occupied = await context.images
                    .AnyAsync(i => i.productId == pending.productId && i.index == pending.imageIndex, ct);
                var index = pending.imageIndex;
                if (occupied)
                {
                    var maxIndex = await context.images
                        .Where(i => i.productId == pending.productId)
                        .Select(i => (int?)i.index)
                        .MaxAsync(ct) ?? -1;
                    index = maxIndex + 1;
                }

                galleryReference = new ImageReference
                {
                    file = file,
                    productId = pending.productId,
                    index = index
                };
                context.images.Add(galleryReference);
            }

            if (canAttachToAttribute && target != null)
            {
                if (target.IsStoreAttribute)
                {
                    if (storeProductAttributeValue == null)
                    {
                        storeProductAttributeValue = new StoreProductAttributeValue
                        {
                            productId = pending.productId,
                            storeAttributeId = target.AttributeId,
                            value = string.Empty
                        };
                        newStoreProductAttributeValue = storeProductAttributeValue;
                        context.storeProductAttributeValues.Add(storeProductAttributeValue);
                    }
                    else
                    {
                        previousStoreAttributeValue = storeProductAttributeValue.value;
                        storeProductAttributeValue.value = string.Empty;
                    }

                    storeProductAttributeFile = new StoreProductAttributeFile
                    {
                        attributeValue = storeProductAttributeValue,
                        file = file,
                        index = storeProductAttributeValue.files.Count == 0
                            ? 0
                            : storeProductAttributeValue.files.Max(link => link.index) + 1
                    };
                    storeProductAttributeValue.files.Add(storeProductAttributeFile);
                    context.storeProductAttributeFiles.Add(storeProductAttributeFile);
                }
                else
                {
                    if (productAttributeValue == null)
                    {
                        productAttributeValue = new ProductAttributeValue
                        {
                            productId = pending.productId,
                            attributeId = target.AttributeId,
                            value = string.Empty
                        };
                        newProductAttributeValue = productAttributeValue;
                        context.productAttributeValues.Add(productAttributeValue);
                    }
                    else
                    {
                        previousProductAttributeValue = productAttributeValue.value;
                        productAttributeValue.value = string.Empty;
                    }

                    productAttributeFile = new ProductAttributeFile
                    {
                        attributeValue = productAttributeValue,
                        file = file,
                        index = productAttributeValue.files.Count == 0
                            ? 0
                            : productAttributeValue.files.Max(link => link.index) + 1
                    };
                    productAttributeValue.files.Add(productAttributeFile);
                    context.productAttributeFiles.Add(productAttributeFile);
                }
            }

            pending.status = ImageDownloadStatus.Completed;
            pending.completedAt = DateTime.UtcNow;
            pending.lastError = null;

            await context.SaveChangesAsync(ct);

            _logger.LogDebug(
                "Downloaded media {Index} for product {ProductId}: {MediaUrl}",
                pending.imageIndex, pending.productId, shortUrl);
        }
        catch (Exception ex)
        {
            if (galleryReference != null)
                context.images.Remove(galleryReference);
            if (productAttributeFile != null)
            {
                productAttributeValue?.files.Remove(productAttributeFile);
                context.productAttributeFiles.Remove(productAttributeFile);
            }
            if (storeProductAttributeFile != null)
            {
                storeProductAttributeValue?.files.Remove(storeProductAttributeFile);
                context.storeProductAttributeFiles.Remove(storeProductAttributeFile);
            }
            if (newProductAttributeValue != null)
                context.productAttributeValues.Remove(newProductAttributeValue);
            else if (productAttributeValue != null && previousProductAttributeValue != null)
                productAttributeValue.value = previousProductAttributeValue;
            if (newStoreProductAttributeValue != null)
                context.storeProductAttributeValues.Remove(newStoreProductAttributeValue);
            else if (storeProductAttributeValue != null && previousStoreAttributeValue != null)
                storeProductAttributeValue.value = previousStoreAttributeValue;
            if (importedFile != null)
                context.files.Remove(importedFile);
            if (uploadedFilePath != null)
            {
                try
                {
                    await storageService.DeleteAsync(uploadedFilePath, CancellationToken.None);
                }
                catch (Exception cleanupException)
                {
                    _logger.LogError(cleanupException,
                        "Failed to clean imported media object {Path} after attachment failure",
                        uploadedFilePath);
                }
            }

            // Host shutdown cancellation is not a media failure. Keep the queue row
            // recoverable as InProgress; startup recovery will return it to Pending.
            if (ex is OperationCanceledException && ct.IsCancellationRequested)
                throw;

            pending.status = ImageDownloadStatus.Failed;
            pending.lastError = ex.Message;
            await context.SaveChangesAsync(CancellationToken.None);

            _logger.LogError(ex,
                "Error downloading media {MediaUrl} for product {ProductId}",
                shortUrl, pending.productId);

            await Log("error", $"Error downloading image #{pending.imageIndex}: {ex.Message}");
        }
    }

    private static async Task FailWithoutRetryAsync(
        DataContext context,
        PendingImageDownload pending,
        string error,
        Func<string, string, Task> log)
    {
        pending.status = ImageDownloadStatus.Failed;
        pending.lastError = error;
        pending.retryCount = PendingImageDownload.MAX_RETRIES;
        await context.SaveChangesAsync();
        await log("error", error);
    }

    private sealed record FileAttributeTarget(
        int AttributeId,
        bool IsStoreAttribute,
        bool AllowMultipleFiles);

    private static async Task<FileAttributeTarget?> ResolveFileAttributeTargetAsync(
        DataContext context,
        PendingImageDownload pending,
        CancellationToken ct)
    {
        if (pending.targetAttributeId.HasValue == pending.targetStoreAttributeId.HasValue)
            return null;

        var product = await context.products.AsNoTracking()
            .Where(item => item.Id == pending.productId)
            .Select(item => new { item.categoryId, item.storeId })
            .FirstOrDefaultAsync(ct);
        if (product == null || product.storeId != pending.storeId)
            return null;

        if (pending.targetAttributeId is int categoryAttributeId)
        {
            if (!product.categoryId.HasValue)
                return null;

            var attribute = await context.categoryAttributes.AsNoTracking()
                .Where(item => item.Id == categoryAttributeId)
                .Select(item => new { item.categoryId, item.type, item.allowMultipleFiles })
                .FirstOrDefaultAsync(ct);
            if (attribute == null
                || attribute.type != AttributeType.File
                || attribute.categoryId != product.categoryId.Value)
                return null;

            return new FileAttributeTarget(
                categoryAttributeId,
                IsStoreAttribute: false,
                attribute.allowMultipleFiles);
        }

        if (pending.targetStoreAttributeId is int storeAttributeId)
        {
            var attribute = await context.storeAttributes.AsNoTracking()
                .Where(item => item.Id == storeAttributeId)
                .Select(item => new { item.storeId, item.type, item.allowMultipleFiles })
                .FirstOrDefaultAsync(ct);
            if (attribute == null
                || attribute.type != AttributeType.File
                || attribute.storeId != product.storeId)
                return null;

            return new FileAttributeTarget(
                storeAttributeId,
                IsStoreAttribute: true,
                attribute.allowMultipleFiles);
        }

        return null;
    }

    private static Task<bool> HasTargetFilesAsync(
        DataContext context,
        int productId,
        FileAttributeTarget target,
        CancellationToken ct)
        => target.IsStoreAttribute
            ? context.storeProductAttributeValues.AnyAsync(value =>
                value.productId == productId
                && value.storeAttributeId == target.AttributeId
                && value.files.Any(), ct)
            : context.productAttributeValues.AnyAsync(value =>
                value.productId == productId
                && value.attributeId == target.AttributeId
                && value.files.Any(), ct);

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

    private static string GetVideoExtension(string? fileName)
    {
        var extension = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        return extension is ".mp4" or ".webm" or ".avi" ? extension : ".video";
    }

    private static string SafeUrlForLog(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "external media URL";
        return $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath}";
    }

    /// <summary>
    /// Non-terminal image rows for a cancelled batch (must be marked Cancelled or they are re-polled forever).
    /// </summary>
    internal static IQueryable<PendingImageDownload> QueryBatchRowsToCancel(DataContext context, string batchId) =>
        context.pendingImageDownloads.Where(p =>
            p.batchId == batchId &&
            (p.status == ImageDownloadStatus.Pending
             || p.status == ImageDownloadStatus.InProgress
             || p.status == ImageDownloadStatus.Failed));

    /// <summary>
    /// Completes running/paused image-download ops that have no active queue work once the parent
    /// import is terminal (or missing). Fixes pre-created empty <c>{batchId}-img</c> ops that would
    /// otherwise stay <c>running</c> until the 24h safety net.
    /// </summary>
    /// <param name="db">EF data context.</param>
    /// <param name="operationLogger">Writes finish/log events for the operations panel.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="onlyBatchId">When set, only consider this image-download operation id.</param>
    /// <returns>Number of operations closed.</returns>
    internal static async Task<int> FinishIdleImageDownloadOpsAsync(
        DataContext db,
        IOperationLogger operationLogger,
        CancellationToken ct,
        string? onlyBatchId = null)
    {
        var query = db.operations.AsNoTracking()
            .Where(o => (o.status == "running" || o.status == "paused")
                        && (o.batchType == "image-download" || o.type == "image-download"));

        if (!string.IsNullOrEmpty(onlyBatchId))
            query = query.Where(o => o.operationId == onlyBatchId);

        var ops = await query
            .Select(o => new { o.operationId, o.storeId, o.batchId })
            .Take(200)
            .ToListAsync(ct);

        var finished = 0;
        foreach (var op in ops)
        {
            var batchKey = op.batchId ?? op.operationId;

            string? parentStatus = null;
            if (op.operationId.EndsWith("-img", StringComparison.Ordinal))
            {
                var parentId = op.operationId[..^4];
                parentStatus = await db.operations.AsNoTracking()
                    .Where(o => o.operationId == parentId)
                    .Select(o => o.status)
                    .FirstOrDefaultAsync(ct);

                // Parent import still running — images may still be queued; leave the child open.
                if (parentStatus == OperationLifecycle.Running)
                    continue;
            }

            var rows = await db.pendingImageDownloads.AsNoTracking()
                .Where(p => p.batchId == batchKey)
                .GroupBy(p => new { p.status, p.retryCount })
                .Select(g => new { g.Key.status, g.Key.retryCount, Count = g.Count() })
                .ToListAsync(ct);

            var hasActive = rows.Any(p =>
                p.status == ImageDownloadStatus.Pending
                || p.status == ImageDownloadStatus.InProgress
                || (p.status == ImageDownloadStatus.Failed
                    && p.retryCount < PendingImageDownload.MAX_RETRIES));
            if (hasActive)
                continue;

            var completed = rows.Where(p => p.status == ImageDownloadStatus.Completed).Sum(p => p.Count);
            var failed = rows.Where(p => p.status == ImageDownloadStatus.Failed).Sum(p => p.Count);
            var cancelled = rows.Where(p => p.status == ImageDownloadStatus.Cancelled).Sum(p => p.Count);

            string status;
            string summary;
            if (rows.Count == 0)
            {
                status = parentStatus is "cancelled" or "paused" ? "cancelled" : "completed";
                summary = parentStatus == "cancelled"
                    ? "Cancelled (no images queued)"
                    : "No images to download";
            }
            else if (completed == 0 && failed == 0 && cancelled > 0)
            {
                status = "cancelled";
                summary = $"Cancelled. Downloaded: 0, errors: 0, cancelled: {cancelled}";
            }
            else if (parentStatus == "cancelled" && completed == 0)
            {
                status = "cancelled";
                summary = $"Cancelled. Downloaded: {completed}, errors: {failed}, cancelled: {cancelled}";
            }
            else
            {
                status = failed == 0
                    ? "completed"
                    : (completed == 0 ? "failed" : "partial");
                summary = cancelled > 0
                    ? $"Downloaded: {completed} succeeded, {failed} failed, {cancelled} cancelled"
                    : $"Downloaded: {completed} succeeded, {failed} failed";
            }

            await operationLogger.LogAsync(op.operationId, op.storeId, "info", summary);
            await operationLogger.FinishAsync(op.operationId, op.storeId, status, summary);
            finished++;
        }

        return finished;
    }
}
