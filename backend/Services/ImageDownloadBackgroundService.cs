using Microsoft.EntityFrameworkCore;
using PrintO.Enums;
using PrintO.Infrastructure.Storage;
using PrintO.Models;

namespace PrintO.Services;

/// <summary>
/// Background service that processes the pending image download queue.
/// Downloads images from external URLs and stores them in MinIO.
/// </summary>
public class ImageDownloadBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ImageDownloadBackgroundService> _logger;

    // Settings
    private const int BATCH_SIZE = 5;              // Images to process per batch
    private const int DELAY_BETWEEN_IMAGES_MS = 500;  // Delay between images (ms)
    private const int POLL_INTERVAL_MS = 5000;     // Queue poll interval (ms)
    private const int ERROR_DELAY_MS = 30000;      // Delay after error (ms)

    /// <summary>
    /// Initializes a new instance of <see cref="ImageDownloadBackgroundService"/>.
    /// </summary>
    /// <param name="scopeFactory">Factory for creating DI scopes.</param>
    /// <param name="logger">Logger instance.</param>
    public ImageDownloadBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<ImageDownloadBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Main execution loop that polls for pending images and processes them in batches.
    /// </summary>
    /// <param name="stoppingToken">Token to signal graceful shutdown.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ImageDownloadBackgroundService запущен");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingImagesAsync(stoppingToken);
                await Task.Delay(POLL_INTERVAL_MS, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Нормальная остановка
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в ImageDownloadBackgroundService");
                await Task.Delay(ERROR_DELAY_MS, stoppingToken);
            }
        }

        _logger.LogInformation("ImageDownloadBackgroundService остановлен");
    }

    private async Task ProcessPendingImagesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DataContext>();
        var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();

        // Получаем пачку pending изображений
        var pendingImages = await context.pendingImageDownloads
            .Where(p => p.status == ImageDownloadStatus.Pending ||
                       (p.status == ImageDownloadStatus.Failed && p.retryCount < PendingImageDownload.MAX_RETRIES))
            .OrderBy(p => p.createdAt)
            .ThenBy(p => p.imageIndex)
            .Take(BATCH_SIZE)
            .ToListAsync(ct);

        if (pendingImages.Count == 0)
            return;

        _logger.LogInformation("Обрабатываю {Count} изображений из очереди", pendingImages.Count);

        foreach (var pending in pendingImages)
        {
            if (ct.IsCancellationRequested)
                break;

            await ProcessSingleImageAsync(context, storageService, pending, ct);

            // Задержка между изображениями чтобы не перегружать CDN
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

        try
        {
            // Генерируем путь для файла
            var fileId = Guid.NewGuid().ToString("N");
            var extension = GetExtensionFromUrl(pending.imageUrl) ?? ".jpg";
            var filePath = $"stores/{pending.storeId}/products/{pending.productId}/images/{fileId}{extension}";

            // Скачиваем и загружаем в хранилище
            var uploadResult = await storageService.UploadFromUriWithMetadataAsync(pending.imageUrl, filePath, ct);

            if (uploadResult == null)
            {
                pending.status = ImageDownloadStatus.Failed;
                pending.lastError = "Не удалось скачать изображение";
                await context.SaveChangesAsync(ct);

                _logger.LogWarning(
                    "Не удалось скачать изображение {ImageUrl} для товара {ProductId} (попытка {Retry})",
                    pending.imageUrl, pending.productId, pending.retryCount);
                return;
            }

            // Создаём запись File
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

            // Проверяем, есть ли уже изображение с таким индексом
            var existingImage = await context.images
                .FirstOrDefaultAsync(i => i.productId == pending.productId && i.index == pending.imageIndex, ct);

            if (existingImage == null)
            {
                // Создаём ImageReference
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
                // Обновляем существующий
                existingImage.fileId = file.Id;
            }

            // Помечаем как завершённый
            pending.status = ImageDownloadStatus.Completed;
            pending.completedAt = DateTime.UtcNow;
            pending.lastError = null;

            await context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Загружено изображение {Index} для товара {ProductId}: {ImageUrl}",
                pending.imageIndex, pending.productId, pending.imageUrl);
        }
        catch (Exception ex)
        {
            pending.status = ImageDownloadStatus.Failed;
            pending.lastError = ex.Message;
            await context.SaveChangesAsync(CancellationToken.None); // Сохраняем даже если отмена

            _logger.LogError(ex,
                "Ошибка загрузки изображения {ImageUrl} для товара {ProductId}",
                pending.imageUrl, pending.productId);
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
