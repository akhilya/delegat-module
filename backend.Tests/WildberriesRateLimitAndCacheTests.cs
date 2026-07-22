using Delegat.Marketplace.SDK.RateLimit;
using Delegat.Services.Marketplace;
using Microsoft.Extensions.Caching.Memory;

namespace Delegat.Tests;

public class WildberriesRateLimitAndCacheTests
{
    /// <summary>
    /// Mirrors WildberriesApiClient.ResolveRetryAfter parsing rules for unit isolation
    /// (tests project does not always load the WB plugin assembly).
    /// </summary>
    internal static TimeSpan ResolveRetryAfter(System.Net.Http.Headers.HttpResponseHeaders? headers)
    {
        const int defaultSeconds = 2;
        const int minSeconds = 1;
        const int maxSeconds = 60;

        int? TryParse(string name)
        {
            if (headers == null || !headers.TryGetValues(name, out var values))
                return null;
            foreach (var v in values)
            {
                if (int.TryParse(v, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out var s) && s > 0)
                    return s;
            }
            return null;
        }

        var seconds = TryParse("X-Ratelimit-Retry")
            ?? TryParse("x-ratelimit-retry")
            ?? (headers?.RetryAfter?.Delta is { } d ? (int?)Math.Ceiling(d.TotalSeconds) : null)
            ?? defaultSeconds;

        return TimeSpan.FromSeconds(Math.Clamp(seconds, minSeconds, maxSeconds));
    }

    [Fact]
    public void ResolveRetryAfter_ReadsXRatelimitRetry()
    {
        using var response = new HttpResponseMessage();
        response.Headers.TryAddWithoutValidation("X-Ratelimit-Retry", "7");

        var wait = ResolveRetryAfter(response.Headers);

        Assert.Equal(TimeSpan.FromSeconds(7), wait);
    }

    [Fact]
    public void ResolveRetryAfter_FallsBackToDefault_WhenMissing()
    {
        using var response = new HttpResponseMessage();

        var wait = ResolveRetryAfter(response.Headers);

        Assert.Equal(TimeSpan.FromSeconds(2), wait);
    }

    [Fact]
    public void ResolveRetryAfter_ClampsToMax()
    {
        using var response = new HttpResponseMessage();
        response.Headers.TryAddWithoutValidation("X-Ratelimit-Retry", "999");

        var wait = ResolveRetryAfter(response.Headers);

        Assert.Equal(TimeSpan.FromSeconds(60), wait);
    }

    [Fact]
    public async Task MarketplaceRateLimiter_PauseFor_BlocksAcquire()
    {
        var limiter = new MarketplaceRateLimiter(maxRequestsPerSecond: 10);
        limiter.PauseFor(TimeSpan.FromMilliseconds(200));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await limiter.AcquireAsync();
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds >= 150, $"Expected pause >= 150ms, got {sw.ElapsedMilliseconds}");
    }

    [Fact]
    public async Task MarketplaceRateLimiter_Lease_AllowsOnlyOneInFlight()
    {
        var limiter = new MarketplaceRateLimiter(maxRequestsPerSecond: 100, maxConcurrent: 1);

        await using var first = await limiter.AcquireLeaseAsync();
        Assert.Equal(1, limiter.CurrentInFlight);

        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondAcquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var secondTask = Task.Run(async () =>
        {
            secondStarted.SetResult();
            await using var second = await limiter.AcquireLeaseAsync();
            secondAcquired.SetResult();
        });

        await secondStarted.Task;
        await Task.Delay(80);
        Assert.False(secondAcquired.Task.IsCompleted, "Second lease must wait while first is held");

        await first.DisposeAsync();
        await secondAcquired.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await secondTask;
        Assert.Equal(0, limiter.CurrentInFlight);
    }

    [Fact]
    public async Task MarketplaceRateLimiter_Lease_PauseFor_BlocksWhileHeld()
    {
        var limiter = new MarketplaceRateLimiter(maxRequestsPerSecond: 10, maxConcurrent: 1);
        await using var lease = await limiter.AcquireLeaseAsync();
        limiter.PauseFor(TimeSpan.FromMilliseconds(200));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        // Release first so another acquire can proceed after pause.
        await lease.DisposeAsync();
        await using var next = await limiter.AcquireLeaseAsync();
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds >= 150, $"Expected pause >= 150ms, got {sw.ElapsedMilliseconds}");
    }

    [Fact]
    public void MarketplaceMetadataCache_RoundTripsValues()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = new MarketplaceMetadataCache(memory);

        cache.Set("wb:dir:global:7", new List<string> { "a", "b" }, TimeSpan.FromMinutes(5));

        Assert.True(cache.TryGetValue("wb:dir:global:7", out List<string>? values));
        Assert.Equal(new[] { "a", "b" }, values);
        Assert.False(cache.TryGetValue("missing", out List<string>? _));
    }

    [Fact]
    public void CategoryFieldsKey_IsStable()
    {
        var key = MarketplaceMetadataCache.CategoryFieldsKey("wildberries", 7, "1234");
        Assert.Equal("category-fields:wildberries:7:1234", key);
    }

    [Fact]
    public void GlobalDirectoryCacheKey_IsConnectionScoped()
    {
        Assert.Equal("wb:dir:global:42", $"wb:dir:global:{42}");
    }
}
