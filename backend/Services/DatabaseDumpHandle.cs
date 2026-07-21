namespace Delegat.Services;

/// <summary>
/// Owns a temporary SQL dump file until disposed (after the HTTP response finishes).
/// </summary>
public sealed class DatabaseDumpHandle : IAsyncDisposable, IDisposable
{
    private readonly string _filePath;
    private readonly Action _onDispose;
    private int _disposed;

    public DatabaseDumpHandle(string filePath, string fileName, Action onDispose)
    {
        _filePath = filePath;
        FileName = fileName;
        _onDispose = onDispose;
    }

    public string FilePath => _filePath;
    public string FileName { get; }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return ValueTask.CompletedTask;

        try
        {
            if (File.Exists(_filePath))
                File.Delete(_filePath);
        }
        catch
        {
            // Best-effort cleanup
        }

        try
        {
            _onDispose();
        }
        catch
        {
            // Lock release must not throw from dispose
        }

        return ValueTask.CompletedTask;
    }
}
