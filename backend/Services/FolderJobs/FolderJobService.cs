using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Configuration;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Storage;
using Delegat.Models;

namespace Delegat.Services.FolderJobs;

public sealed class FolderJobService
{
    private readonly DataContext _context;
    private readonly IStorageService _storage;
    private readonly IOperationLogger _operationLogger;
    private readonly ThumbnailService _thumbnailService;
    private readonly ILogger<FolderJobService> _logger;

    public FolderJobService(
        DataContext context,
        IStorageService storage,
        IOperationLogger operationLogger,
        ThumbnailService thumbnailService,
        ILogger<FolderJobService> logger)
    {
        _context = context;
        _storage = storage;
        _operationLogger = operationLogger;
        _thumbnailService = thumbnailService;
        _logger = logger;
    }

    public async Task<(int FileCount, long TotalBytes, string FolderName)> GetProductScopeStatsAsync(
        int productId, int? folderId, CancellationToken ct)
    {
        if (folderId.HasValue)
        {
            var folder = await _context.productFolders.AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == folderId.Value && f.productId == productId, ct)
                ?? throw ApiException.NotFound("Folder not found");

            var folderIds = new List<int> { folderId.Value };
            await CollectProductDescendantsAsync(folderId.Value, productId, folderIds, ct);

            var stats = await _context.files.AsNoTracking()
                .Where(f => f.productId == productId && f.folderId.HasValue && folderIds.Contains(f.folderId.Value))
                .GroupBy(_ => 1)
                .Select(g => new { Count = g.Count(), Size = g.Sum(x => x.length) })
                .FirstOrDefaultAsync(ct);

            return (stats?.Count ?? 0, stats?.Size ?? 0, folder.name);
        }

        var rootStats = await _context.files.AsNoTracking()
            .Where(f => f.productId == productId)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Size = g.Sum(x => x.length) })
            .FirstOrDefaultAsync(ct);

        return (rootStats?.Count ?? 0, rootStats?.Size ?? 0, "files");
    }

    public async Task<(int FileCount, long TotalBytes, string FolderName)> GetStoreScopeStatsAsync(
        int storeId, int? folderId, CancellationToken ct)
    {
        if (folderId.HasValue)
        {
            var folder = await _context.storeFolders.AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == folderId.Value && f.storeId == storeId, ct)
                ?? throw ApiException.NotFound("Folder not found");

            var folderIds = new List<int> { folderId.Value };
            await CollectStoreDescendantsAsync(folderId.Value, storeId, folderIds, ct);

            var stats = await _context.storeFiles.AsNoTracking()
                .Where(f => f.storeId == storeId && f.folderId.HasValue && folderIds.Contains(f.folderId.Value))
                .GroupBy(_ => 1)
                .Select(g => new { Count = g.Count(), Size = g.Sum(x => x.length) })
                .FirstOrDefaultAsync(ct);

            return (stats?.Count ?? 0, stats?.Size ?? 0, folder.name);
        }

        var rootStats = await _context.storeFiles.AsNoTracking()
            .Where(f => f.storeId == storeId)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Size = g.Sum(x => x.length) })
            .FirstOrDefaultAsync(ct);

        return (rootStats?.Count ?? 0, rootStats?.Size ?? 0, "store-storage");
    }

    public async Task<string> EnqueueArchiveAsync(
        int storeId,
        int authorUserId,
        FolderJobScope scope,
        int? productId,
        int? folderId,
        string folderName,
        int fileCount,
        long totalBytes,
        CancellationToken ct)
    {
        StorageLimits.EnsureWithinArchiveLimits(fileCount, totalBytes);

        var operationId = Guid.NewGuid().ToString("N");
        var title = $"Archive: {folderName}";

        _context.pendingFolderJobs.Add(new PendingFolderJob
        {
            storeId = storeId,
            authorUserId = authorUserId,
            operationId = operationId,
            jobType = FolderJobType.Archive,
            scope = scope,
            productId = productId,
            folderId = folderId,
            status = FolderJobStatus.Pending,
            totalItems = fileCount,
            createdAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync(ct);

        await _operationLogger.StartAsync(
            operationId, storeId, authorUserId,
            "folder-archive", title,
            batchId: operationId, batchType: "folder-job",
            totalItems: Math.Max(fileCount, 1));

        return operationId;
    }

    public async Task<string> EnqueueDeleteAsync(
        int storeId,
        int authorUserId,
        FolderJobScope scope,
        int? productId,
        int folderId,
        string folderName,
        int totalFileCount,
        CancellationToken ct)
    {
        var operationId = Guid.NewGuid().ToString("N");
        var title = $"Delete folder: {folderName}";

        _context.pendingFolderJobs.Add(new PendingFolderJob
        {
            storeId = storeId,
            authorUserId = authorUserId,
            operationId = operationId,
            jobType = FolderJobType.Delete,
            scope = scope,
            productId = productId,
            folderId = folderId,
            status = FolderJobStatus.Pending,
            totalItems = Math.Max(totalFileCount, 1),
            createdAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync(ct);

        await _operationLogger.StartAsync(
            operationId, storeId, authorUserId,
            "folder-delete", title,
            batchId: operationId, batchType: "folder-job",
            totalItems: Math.Max(totalFileCount, 1));

        return operationId;
    }

    public async Task ProcessJobAsync(PendingFolderJob job, CancellationToken ct)
    {
        job.status = FolderJobStatus.InProgress;
        job.lastAttemptAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        try
        {
            if (await IsOperationCancelledAsync(job.operationId, ct))
            {
                await MarkCancelledAsync(job, ct);
                return;
            }

            if (job.jobType == FolderJobType.Archive)
                await ProcessArchiveAsync(job, ct);
            else
                await ProcessDeleteAsync(job, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            await MarkCancelledAsync(job, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Folder job {JobId} failed", job.Id);
            job.status = FolderJobStatus.Failed;
            job.lastError = ex.Message;
            job.completedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
            await _operationLogger.LogAsync(job.operationId, job.storeId, "error", ex.Message);
            await _operationLogger.FinishAsync(job.operationId, job.storeId, "failed", ex.Message);
        }
    }

    public async Task CleanupExpiredArtifactsAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - StorageLimits.GetArtifactTtl();
        var expired = await _context.pendingFolderJobs
            .Where(j => j.jobType == FolderJobType.Archive
                        && j.status == FolderJobStatus.Completed
                        && j.resultObjectKey != null
                        && (j.completedAt ?? j.createdAt) < cutoff)
            .OrderBy(j => j.completedAt)
            .Take(50)
            .ToListAsync(ct);

        foreach (var job in expired)
        {
            try
            {
                if (!string.IsNullOrEmpty(job.resultObjectKey))
                    await _storage.DeleteAsync(job.resultObjectKey);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete archive artifact {Key}", job.resultObjectKey);
            }

            job.resultObjectKey = null;
            job.resultBytes = null;
        }

        if (expired.Count > 0)
            await _context.SaveChangesAsync(ct);
    }

    private async Task ProcessArchiveAsync(PendingFolderJob job, CancellationToken ct)
    {
        await _operationLogger.LogAsync(job.operationId, job.storeId, "info", "Building archive...");

        var tempPath = Path.Combine(Path.GetTempPath(), $"folder-archive-{job.operationId}.zip");
        try
        {
            string folderName;
            int processed;
            if (job.scope == FolderJobScope.Product)
            {
                if (job.productId == null)
                    throw new InvalidOperationException("productId required for product archive");
                (folderName, processed) = await WriteProductZipAsync(job, tempPath, ct);
            }
            else
            {
                (folderName, processed) = await WriteStoreZipAsync(job, tempPath, ct);
            }

            if (await IsOperationCancelledAsync(job.operationId, ct))
            {
                await MarkCancelledAsync(job, ct);
                return;
            }

            var fileName = $"{SanitizeFileName(folderName)}.zip";
            var objectKey = $"folder-artifacts/{job.storeId}/{job.operationId}/{fileName}";
            await using (var fs = System.IO.File.OpenRead(tempPath))
            {
                var ok = await _storage.UploadAsync(fs, objectKey, fs.Length, "application/zip", ct);
                if (!ok)
                    throw new InvalidOperationException("Failed to upload archive to storage");
            }

            job.resultObjectKey = objectKey;
            job.resultFileName = fileName;
            job.resultBytes = new FileInfo(tempPath).Length;
            job.processedItems = processed;
            job.status = FolderJobStatus.Completed;
            job.completedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);

            await _operationLogger.ProgressAsync(job.operationId, job.storeId, Math.Max(job.totalItems, 1), processed, 0);
            await _operationLogger.FinishAsync(
                job.operationId, job.storeId, "completed",
                $"Archive ready ({processed} files, {job.resultBytes} bytes)");
        }
        finally
        {
            try
            {
                if (System.IO.File.Exists(tempPath))
                    System.IO.File.Delete(tempPath);
            }
            catch { /* ignore */ }
        }
    }

    private async Task<(string FolderName, int Processed)> WriteProductZipAsync(
        PendingFolderJob job, string tempPath, CancellationToken ct)
    {
        var productId = job.productId!.Value;
        var folderId = job.folderId;
        string folderName;
        var allFolderIds = new List<int>();

        if (folderId.HasValue)
        {
            var folder = await _context.productFolders.AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == folderId.Value && f.productId == productId, ct)
                ?? throw ApiException.NotFound("Folder not found");
            folderName = folder.name;
            allFolderIds.Add(folderId.Value);
            await CollectProductDescendantsAsync(folderId.Value, productId, allFolderIds, ct);
        }
        else
        {
            folderName = "files";
        }

        var files = folderId.HasValue
            ? await _context.files.AsNoTracking()
                .Where(f => f.productId == productId && f.folderId.HasValue && allFolderIds.Contains(f.folderId.Value))
                .ToListAsync(ct)
            : await _context.files.AsNoTracking()
                .Where(f => f.productId == productId)
                .ToListAsync(ct);

        var allFolders = await _context.productFolders.AsNoTracking()
            .Where(f => f.productId == productId)
            .Select(f => new { f.Id, f.name, f.parentFolderId })
            .ToListAsync(ct);

        var processed = await WriteZipFileAsync(
            tempPath, folderId, allFolderIds, allFolders.Select(f => (f.Id, f.name, f.parentFolderId)).ToList(),
            files.Select(f => (f.folderId, f.filePath, f.displayName)).ToList(),
            job, ct);

        return (folderName, processed);
    }

    private async Task<(string FolderName, int Processed)> WriteStoreZipAsync(
        PendingFolderJob job, string tempPath, CancellationToken ct)
    {
        var storeId = job.storeId;
        var folderId = job.folderId;
        string folderName;
        var allFolderIds = new List<int>();

        if (folderId.HasValue)
        {
            var folder = await _context.storeFolders.AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == folderId.Value && f.storeId == storeId, ct)
                ?? throw ApiException.NotFound("Folder not found");
            folderName = folder.name;
            allFolderIds.Add(folderId.Value);
            await CollectStoreDescendantsAsync(folderId.Value, storeId, allFolderIds, ct);
        }
        else
        {
            folderName = "store-storage";
        }

        var files = folderId.HasValue
            ? await _context.storeFiles.AsNoTracking()
                .Where(f => f.storeId == storeId && f.folderId.HasValue && allFolderIds.Contains(f.folderId.Value))
                .ToListAsync(ct)
            : await _context.storeFiles.AsNoTracking()
                .Where(f => f.storeId == storeId)
                .ToListAsync(ct);

        var allFolders = await _context.storeFolders.AsNoTracking()
            .Where(f => f.storeId == storeId)
            .Select(f => new { f.Id, f.name, f.parentFolderId })
            .ToListAsync(ct);

        var processed = await WriteZipFileAsync(
            tempPath, folderId, allFolderIds, allFolders.Select(f => (f.Id, f.name, f.parentFolderId)).ToList(),
            files.Select(f => (f.folderId, f.filePath, f.displayName)).ToList(),
            job, ct);

        return (folderName, processed);
    }

    private async Task<int> WriteZipFileAsync(
        string tempPath,
        int? rootFolderId,
        List<int> allFolderIds,
        List<(int Id, string name, int? parentFolderId)> allFolders,
        List<(int? folderId, string filePath, string? displayName)> files,
        PendingFolderJob job,
        CancellationToken ct)
    {
        var folderById = allFolders.ToDictionary(f => f.Id);
        var folderPathCache = new Dictionary<int, string>();
        string BuildFolderPath(int fId)
        {
            if (folderPathCache.TryGetValue(fId, out var cached)) return cached;

            var parts = new List<string>();
            int? cur = fId;
            while (cur.HasValue && folderById.TryGetValue(cur.Value, out var f))
            {
                parts.Insert(0, f.name);
                cur = f.parentFolderId;
            }

            if (rootFolderId.HasValue)
            {
                var targetParts = new List<string>();
                int? tc = rootFolderId.Value;
                while (tc.HasValue && folderById.TryGetValue(tc.Value, out var tf))
                {
                    targetParts.Insert(0, tf.name);
                    tc = tf.parentFolderId;
                }

                parts = parts.Count > targetParts.Count
                    ? parts.Skip(targetParts.Count).ToList()
                    : new List<string>();
            }

            var path = string.Join("/", parts);
            folderPathCache[fId] = path;
            return path;
        }

        var processed = 0;
        var total = Math.Max(files.Count, 1);

        await using (var fs = System.IO.File.Create(tempPath))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
        {
            foreach (var fId in allFolderIds)
            {
                var hasFiles = files.Any(f => f.folderId == fId);
                if (!hasFiles)
                {
                    var folderPath = BuildFolderPath(fId);
                    if (!string.IsNullOrEmpty(folderPath))
                        archive.CreateEntry(folderPath + "/");
                }
            }

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                if (await IsOperationCancelledAsync(job.operationId, ct))
                    throw new OperationCanceledException("Folder archive cancelled");

                var fileName = file.displayName ?? Path.GetFileName(file.filePath);
                string entryName;
                if (file.folderId.HasValue)
                {
                    var folderPath = BuildFolderPath(file.folderId.Value);
                    entryName = string.IsNullOrEmpty(folderPath) ? fileName : $"{folderPath}/{fileName}";
                }
                else
                {
                    entryName = fileName;
                }

                try
                {
                    var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                    await using var entryStream = entry.Open();
                    await _storage.StreamToAsync(file.filePath, entryStream, ct);
                    processed++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Skipping file {Path} in archive", file.filePath);
                }

                if (processed % 10 == 0 || processed == files.Count)
                {
                    await _operationLogger.ProgressAsync(job.operationId, job.storeId, total, processed, 0);
                    job.processedItems = processed;
                    await _context.SaveChangesAsync(ct);
                }
            }
        }

        return processed;
    }

    private async Task ProcessDeleteAsync(PendingFolderJob job, CancellationToken ct)
    {
        if (job.folderId == null)
            throw new InvalidOperationException("folderId required for delete");

        await _operationLogger.LogAsync(job.operationId, job.storeId, "info", "Deleting folder contents...");

        if (job.scope == FolderJobScope.Product)
            await DeleteProductFolderRecursiveAsync(job, ct);
        else
            await DeleteStoreFolderRecursiveAsync(job, ct);

        if (await IsOperationCancelledAsync(job.operationId, ct))
        {
            await MarkCancelledAsync(job, ct);
            return;
        }

        job.status = FolderJobStatus.Completed;
        job.completedAt = DateTime.UtcNow;
        job.processedItems = job.totalItems;
        await _context.SaveChangesAsync(ct);

        await _operationLogger.ProgressAsync(job.operationId, job.storeId, Math.Max(job.totalItems, 1), job.totalItems, 0);
        await _operationLogger.FinishAsync(job.operationId, job.storeId, "completed", "Folder deleted");
    }

    private async Task DeleteProductFolderRecursiveAsync(PendingFolderJob job, CancellationToken ct)
    {
        var productId = job.productId ?? throw new InvalidOperationException("productId required");
        var folderId = job.folderId!.Value;
        var allFolderIds = new List<int> { folderId };
        await CollectProductDescendantsAsync(folderId, productId, allFolderIds, ct);

        var filesToDelete = await _context.files.AsNoTracking()
            .Where(f => f.folderId.HasValue && allFolderIds.Contains(f.folderId.Value))
            .ToListAsync(ct);

        var total = Math.Max(filesToDelete.Count, 1);
        var processed = 0;

        var fileIds = filesToDelete.Select(f => f.Id).ToList();
        if (fileIds.Count > 0)
            await _context.tags.Where(t => fileIds.Contains(t.fileId)).ExecuteDeleteAsync(ct);

        foreach (var file in filesToDelete)
        {
            ct.ThrowIfCancellationRequested();
            if (await IsOperationCancelledAsync(job.operationId, ct))
                throw new OperationCanceledException("Folder delete cancelled");

            try
            {
                await _thumbnailService.DeleteThumbnailsAsync(file.filePath);
                await _storage.DeleteAsync(file.filePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete storage object {Path}", file.filePath);
            }

            processed++;
            if (processed % 10 == 0 || processed == filesToDelete.Count)
            {
                await _operationLogger.ProgressAsync(job.operationId, job.storeId, total, processed, 0);
                job.processedItems = processed;
                await _context.SaveChangesAsync(ct);
            }
        }

        await _context.files
            .Where(f => f.folderId.HasValue && allFolderIds.Contains(f.folderId.Value))
            .ExecuteDeleteAsync(ct);

        allFolderIds.Reverse();
        foreach (var id in allFolderIds)
            await _context.productFolders.Where(f => f.Id == id).ExecuteDeleteAsync(ct);
    }

    private async Task DeleteStoreFolderRecursiveAsync(PendingFolderJob job, CancellationToken ct)
    {
        var storeId = job.storeId;
        var folderId = job.folderId!.Value;
        var allFolderIds = new List<int> { folderId };
        await CollectStoreDescendantsAsync(folderId, storeId, allFolderIds, ct);

        var filesToDelete = await _context.storeFiles.AsNoTracking()
            .Where(f => f.folderId.HasValue && allFolderIds.Contains(f.folderId.Value))
            .ToListAsync(ct);

        var total = Math.Max(filesToDelete.Count, 1);
        var processed = 0;

        var fileIds = filesToDelete.Select(f => f.Id).ToList();
        if (fileIds.Count > 0)
        {
            await _context.storeFileTags.Where(t => fileIds.Contains(t.fileId)).ExecuteDeleteAsync(ct);
            await _context.storeFileShareTokens.Where(t => fileIds.Contains(t.fileId)).ExecuteDeleteAsync(ct);
        }

        await _context.storeFolderShareTokens
            .Where(t => t.folderId.HasValue && allFolderIds.Contains(t.folderId.Value))
            .ExecuteDeleteAsync(ct);

        foreach (var file in filesToDelete)
        {
            ct.ThrowIfCancellationRequested();
            if (await IsOperationCancelledAsync(job.operationId, ct))
                throw new OperationCanceledException("Folder delete cancelled");

            try
            {
                await _thumbnailService.DeleteThumbnailsAsync(file.filePath);
                await _storage.DeleteAsync(file.filePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete storage object {Path}", file.filePath);
            }

            processed++;
            if (processed % 10 == 0 || processed == filesToDelete.Count)
            {
                await _operationLogger.ProgressAsync(job.operationId, job.storeId, total, processed, 0);
                job.processedItems = processed;
                await _context.SaveChangesAsync(ct);
            }
        }

        await _context.storeFiles
            .Where(f => f.folderId.HasValue && allFolderIds.Contains(f.folderId.Value))
            .ExecuteDeleteAsync(ct);

        allFolderIds.Reverse();
        foreach (var id in allFolderIds)
            await _context.storeFolders.Where(f => f.Id == id).ExecuteDeleteAsync(ct);
    }

    private async Task MarkCancelledAsync(PendingFolderJob job, CancellationToken ct)
    {
        job.status = FolderJobStatus.Cancelled;
        job.completedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        await _operationLogger.FinishAsync(job.operationId, job.storeId, "cancelled", "Cancelled");
    }

    private async Task<bool> IsOperationCancelledAsync(string operationId, CancellationToken ct)
    {
        var status = await _context.operations.AsNoTracking()
            .Where(o => o.operationId == operationId)
            .Select(o => o.status)
            .FirstOrDefaultAsync(ct);
        return status == "cancelled";
    }

    private async Task CollectProductDescendantsAsync(int parentId, int productId, List<int> result, CancellationToken ct)
    {
        var childIds = await _context.productFolders.AsNoTracking()
            .Where(f => f.parentFolderId == parentId && f.productId == productId)
            .Select(f => f.Id)
            .ToListAsync(ct);

        foreach (var childId in childIds)
        {
            result.Add(childId);
            await CollectProductDescendantsAsync(childId, productId, result, ct);
        }
    }

    private async Task CollectStoreDescendantsAsync(int parentId, int storeId, List<int> result, CancellationToken ct)
    {
        var childIds = await _context.storeFolders.AsNoTracking()
            .Where(f => f.parentFolderId == parentId && f.storeId == storeId)
            .Select(f => f.Id)
            .ToListAsync(ct);

        foreach (var childId in childIds)
        {
            result.Add(childId);
            await CollectStoreDescendantsAsync(childId, storeId, result, ct);
        }
    }

    /// <summary>Sanitize folder names used as ZIP download file names.</summary>
    internal static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "archive" : cleaned;
    }
}
