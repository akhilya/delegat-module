namespace Delegat.Services;

public interface IDatabaseDumpService
{
    /// <summary>
    /// Creates a MySQL logical dump file. Caller must dispose the handle
    /// (deletes the temp file and releases the dump lock).
    /// </summary>
    Task<DatabaseDumpHandle> CreateDumpAsync(CancellationToken cancellationToken = default);
}
