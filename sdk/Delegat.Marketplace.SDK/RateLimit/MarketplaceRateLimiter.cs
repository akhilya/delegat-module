using System.Collections.Concurrent;
using System.Diagnostics;

namespace Delegat.Marketplace.SDK.RateLimit;

/// <summary>
/// Sliding-window rate limiter. Thread-safe, lightweight.
/// Enforces a maximum number of requests per second.
/// </summary>
public class MarketplaceRateLimiter
{
    private readonly int _maxRequestsPerSecond;
    private readonly ConcurrentQueue<long> _timestamps = new();
    private long _totalRequests;

    public MarketplaceRateLimiter(int maxRequestsPerSecond)
    {
        _maxRequestsPerSecond = maxRequestsPerSecond > 0
            ? maxRequestsPerSecond
            : throw new ArgumentOutOfRangeException(nameof(maxRequestsPerSecond));
    }

    /// <summary>
    /// Wait until a request slot is available, then record the request.
    /// </summary>
    public async Task AcquireAsync(CancellationToken ct = default)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var now = Stopwatch.GetTimestamp();
            var windowStart = now - Stopwatch.Frequency; // 1 second ago

            // Purge old timestamps outside the window
            while (_timestamps.TryPeek(out var oldest) && oldest < windowStart)
            {
                _timestamps.TryDequeue(out _);
            }

            if (_timestamps.Count < _maxRequestsPerSecond)
            {
                _timestamps.Enqueue(now);
                Interlocked.Increment(ref _totalRequests);
                return;
            }

            // At limit — wait a bit and retry
            await Task.Delay(20, ct);
        }
    }

    /// <summary>
    /// Get the current number of requests in the sliding 1-second window.
    /// </summary>
    public int GetCurrentWindowCount()
    {
        var windowStart = Stopwatch.GetTimestamp() - Stopwatch.Frequency;

        // Purge stale entries
        while (_timestamps.TryPeek(out var oldest) && oldest < windowStart)
        {
            _timestamps.TryDequeue(out _);
        }

        return _timestamps.Count;
    }

    /// <summary>
    /// Total requests since this limiter was created.
    /// </summary>
    public long TotalRequests => Interlocked.Read(ref _totalRequests);

    public int MaxRequestsPerSecond => _maxRequestsPerSecond;
}
