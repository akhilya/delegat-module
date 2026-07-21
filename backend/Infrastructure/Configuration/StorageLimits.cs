using Delegat.Infrastructure.Middleware;

namespace Delegat.Infrastructure.Configuration;

/// <summary>
/// Limits for folder archive jobs. Overridable via environment variables.
/// </summary>
public static class StorageLimits
{
    public const long DefaultMaxArchiveBytes = 10L * 1024 * 1024 * 1024; // 10 GiB
    public const int DefaultMaxArchiveFileCount = 50_000;
    public const int DefaultArtifactTtlHours = 24;

    public static long GetMaxArchiveBytes()
    {
        var raw = Environment.GetEnvironmentVariable("STORAGE_MAX_ARCHIVE_BYTES");
        if (long.TryParse(raw, out var bytes) && bytes > 0)
            return bytes;
        return DefaultMaxArchiveBytes;
    }

    public static int GetMaxArchiveFileCount()
    {
        var raw = Environment.GetEnvironmentVariable("STORAGE_MAX_ARCHIVE_FILE_COUNT");
        if (int.TryParse(raw, out var count) && count > 0)
            return count;
        return DefaultMaxArchiveFileCount;
    }

    public static TimeSpan GetArtifactTtl()
    {
        var raw = Environment.GetEnvironmentVariable("STORAGE_ARCHIVE_ARTIFACT_TTL_HOURS");
        if (int.TryParse(raw, out var hours) && hours > 0)
            return TimeSpan.FromHours(Math.Clamp(hours, 1, 168));
        return TimeSpan.FromHours(DefaultArtifactTtlHours);
    }

    public static void EnsureWithinArchiveLimits(int fileCount, long totalBytes)
    {
        var maxFiles = GetMaxArchiveFileCount();
        var maxBytes = GetMaxArchiveBytes();
        if (fileCount > maxFiles || totalBytes > maxBytes)
        {
            throw ApiException.BadRequest(
                $"Archive is too large ({fileCount} files, {totalBytes} bytes). Limits: {maxFiles} files or {maxBytes} bytes.",
                new { fileCount, totalBytes, maxFileCount = maxFiles, maxBytes },
                code: "archive_too_large");
        }
    }
}
