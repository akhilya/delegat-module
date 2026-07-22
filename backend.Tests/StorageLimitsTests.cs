using Delegat.Infrastructure.Configuration;
using Delegat.Infrastructure.Middleware;

namespace Delegat.Tests;

public class StorageLimitsTests
{
    private static IDisposable SetEnv(string name, string? value)
    {
        var previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
        return new EnvRestore(name, previous);
    }

    private sealed class EnvRestore(string name, string? previous) : IDisposable
    {
        public void Dispose() => Environment.SetEnvironmentVariable(name, previous);
    }

    [Fact]
    public void GetMaxArchiveBytes_DefaultsAndOverrides()
    {
        using (SetEnv("STORAGE_MAX_ARCHIVE_BYTES", null))
            Assert.Equal(StorageLimits.DefaultMaxArchiveBytes, StorageLimits.GetMaxArchiveBytes());

        using (SetEnv("STORAGE_MAX_ARCHIVE_BYTES", "12345"))
            Assert.Equal(12345, StorageLimits.GetMaxArchiveBytes());

        using (SetEnv("STORAGE_MAX_ARCHIVE_BYTES", "0"))
            Assert.Equal(StorageLimits.DefaultMaxArchiveBytes, StorageLimits.GetMaxArchiveBytes());

        using (SetEnv("STORAGE_MAX_ARCHIVE_BYTES", "nope"))
            Assert.Equal(StorageLimits.DefaultMaxArchiveBytes, StorageLimits.GetMaxArchiveBytes());
    }

    [Fact]
    public void GetMaxArchiveFileCount_DefaultsAndOverrides()
    {
        using (SetEnv("STORAGE_MAX_ARCHIVE_FILE_COUNT", null))
            Assert.Equal(StorageLimits.DefaultMaxArchiveFileCount, StorageLimits.GetMaxArchiveFileCount());

        using (SetEnv("STORAGE_MAX_ARCHIVE_FILE_COUNT", "42"))
            Assert.Equal(42, StorageLimits.GetMaxArchiveFileCount());

        using (SetEnv("STORAGE_MAX_ARCHIVE_FILE_COUNT", "-1"))
            Assert.Equal(StorageLimits.DefaultMaxArchiveFileCount, StorageLimits.GetMaxArchiveFileCount());
    }

    [Theory]
    [InlineData(null, 24)]
    [InlineData("0", 24)]
    [InlineData("nope", 24)]
    [InlineData("12", 12)]
    [InlineData("168", 168)]
    [InlineData("999", 168)] // clamped to 7 days
    public void GetArtifactTtl_ParsesAndClampsHours(string? raw, int expectedHours)
    {
        using var _ = SetEnv("STORAGE_ARCHIVE_ARTIFACT_TTL_HOURS", raw);
        Assert.Equal(TimeSpan.FromHours(expectedHours), StorageLimits.GetArtifactTtl());
    }

    [Fact]
    public void EnsureWithinArchiveLimits_AllowsAtLimit()
    {
        using var files = SetEnv("STORAGE_MAX_ARCHIVE_FILE_COUNT", "10");
        using var bytes = SetEnv("STORAGE_MAX_ARCHIVE_BYTES", "1000");

        StorageLimits.EnsureWithinArchiveLimits(10, 1000);
    }

    [Theory]
    [InlineData(11, 100)]
    [InlineData(5, 1001)]
    public void EnsureWithinArchiveLimits_ThrowsWhenExceeded(int fileCount, long totalBytes)
    {
        using var files = SetEnv("STORAGE_MAX_ARCHIVE_FILE_COUNT", "10");
        using var bytes = SetEnv("STORAGE_MAX_ARCHIVE_BYTES", "1000");

        var ex = Assert.Throws<ApiException>(() =>
            StorageLimits.EnsureWithinArchiveLimits(fileCount, totalBytes));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("archive_too_large", ex.Code);
    }
}
