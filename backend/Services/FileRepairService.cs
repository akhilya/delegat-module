using Microsoft.EntityFrameworkCore;
using PrintO.Infrastructure.Data;
using PrintO.Infrastructure.Storage;

namespace PrintO.Services;

/// <summary>
/// One-time startup service that repairs files broken by the old rename behavior.
/// Before the displayName fix, renaming a file changed filePath in DB but not the
/// actual MinIO object key — leaving the DB pointing to a non-existent key.
/// This service detects those mismatches and restores the correct filePath,
/// preserving the user's rename as displayName.
/// </summary>
public class FileRepairService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IStorageService _storage;
    private readonly ILogger<FileRepairService> _logger;

    public FileRepairService(
        IServiceScopeFactory scopeFactory,
        IStorageService storage,
        ILogger<FileRepairService> logger)
    {
        _scopeFactory = scopeFactory;
        _storage = storage;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await RepairAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FileRepairService failed");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private async Task RepairAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();

        var files = await db.files.AsNoTracking().Select(f => new { f.Id, f.filePath, f.displayName }).ToListAsync(ct);

        _logger.LogInformation("FileRepairService: checking {Count} files", files.Count);

        int repaired = 0, missing = 0, ambiguous = 0, ok = 0;
        const int batchSize = 50;
        var updates = new List<(int Id, string NewFilePath, string DisplayName)>();

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested) break;

            // If file exists at the recorded path, it's fine
            if (await _storage.ExistsAsync(file.filePath, ct))
            {
                ok++;
                continue;
            }

            // Extract the GUID directory prefix: everything up to and including the last '/'
            var lastSlash = file.filePath.LastIndexOf('/');
            if (lastSlash < 0)
            {
                _logger.LogWarning("FileRepairService: file {Id} has no directory in path '{Path}', skipping", file.Id, file.filePath);
                missing++;
                continue;
            }

            var prefix = file.filePath[..(lastSlash + 1)];
            List<string> actualKeys;
            try
            {
                actualKeys = await _storage.ListObjectsByPrefixAsync(prefix, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FileRepairService: failed to list prefix '{Prefix}' for file {Id}", prefix, file.Id);
                missing++;
                continue;
            }

            if (actualKeys.Count == 1)
            {
                var actualKey = actualKeys[0];
                // Preserve the user's renamed filename as displayName (if not already set)
                var userFileName = file.displayName ?? Path.GetFileName(file.filePath);
                updates.Add((file.Id, actualKey, userFileName));
                repaired++;
            }
            else if (actualKeys.Count == 0)
            {
                _logger.LogWarning("FileRepairService: file {Id} — no objects found at prefix '{Prefix}' (truly missing)", file.Id, prefix);
                missing++;
            }
            else
            {
                _logger.LogWarning("FileRepairService: file {Id} — {Count} objects at prefix '{Prefix}' (ambiguous, skipping)", file.Id, actualKeys.Count, prefix);
                ambiguous++;
            }

            // Flush updates in batches
            if (updates.Count >= batchSize)
            {
                await FlushUpdatesAsync(db, updates, ct);
                updates.Clear();
            }
        }

        // Flush remaining
        if (updates.Count > 0)
            await FlushUpdatesAsync(db, updates, ct);

        _logger.LogInformation(
            "FileRepairService complete: {Total} checked, {Ok} ok, {Repaired} repaired, {Missing} missing, {Ambiguous} ambiguous",
            files.Count, ok, repaired, missing, ambiguous);
    }

    private static async Task FlushUpdatesAsync(
        DataContext db,
        List<(int Id, string NewFilePath, string DisplayName)> updates,
        CancellationToken ct)
    {
        foreach (var (id, newFilePath, displayName) in updates)
        {
            await db.files
                .Where(f => f.Id == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(f => f.filePath, newFilePath)
                    .SetProperty(f => f.displayName, displayName), ct);
        }
    }
}
