using Delegat.Models.Ai;
using Delegat.Services.Ai.Agent;

namespace Delegat.Tests;

public class KnowledgeIndexSnapshotCacheTests
{
    [Fact]
    public async Task ConcurrentReads_LoadOnceAndReuseParsedVectors()
    {
        using var cache = new KnowledgeIndexSnapshotCache();
        var release = new TaskCompletionSource<List<AiKnowledgeChunk>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        Task<List<AiKnowledgeChunk>> Load(CancellationToken _) { calls++; return release.Task; }
        var reads = Enumerable.Range(0, 8).Select(_ => cache.GetAsync("model", Load, default)).ToArray();

        release.SetResult(Rows());
        var snapshots = await Task.WhenAll(reads);

        Assert.Equal(1, calls);
        Assert.All(snapshots, snapshot => Assert.Same(snapshots[0], snapshot));
        Assert.Equal(new float[] { 1, 0 }, snapshots[0].Vectors[0]);
    }

    [Fact]
    public async Task ModelChangeInvalidationAndExpiry_RefreshTheSnapshot()
    {
        var clock = new Clock();
        using var cache = new KnowledgeIndexSnapshotCache(clock);
        var calls = 0;
        Task<List<AiKnowledgeChunk>> Load(CancellationToken _) { calls++; return Task.FromResult(Rows()); }

        var first = await cache.GetAsync("model", Load, default);
        Assert.Same(first, await cache.GetAsync("model", Load, default));
        var changedModel = await cache.GetAsync("other", Load, default);
        Assert.NotSame(first, changedModel);
        Assert.Null(changedModel.Vectors[0]);
        cache.Invalidate();
        var invalidated = await cache.GetAsync("other", Load, default);
        Assert.NotSame(changedModel, invalidated);
        clock.Now = clock.Now.AddMinutes(1);
        Assert.NotSame(invalidated, await cache.GetAsync("other", Load, default));
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task InvalidationDuringLoad_DoesNotPublishTheOldGeneration()
    {
        using var cache = new KnowledgeIndexSnapshotCache();
        var release = new TaskCompletionSource<List<AiKnowledgeChunk>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = cache.GetAsync("model", _ => release.Task, default);
        cache.Invalidate();
        release.SetResult(Rows());
        var old = await pending;

        var current = await cache.GetAsync("model", _ => Task.FromResult(Rows()), default);

        Assert.NotSame(old, current);
        Assert.True(current.Generation > old.Generation);
    }

    [Fact]
    public async Task CancelledWaiter_DoesNotCancelTheOwnerOrPoisonTheCache()
    {
        using var cache = new KnowledgeIndexSnapshotCache();
        var release = new TaskCompletionSource<List<AiKnowledgeChunk>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = cache.GetAsync("model", _ => release.Task, default);
        using var cancellation = new CancellationTokenSource();
        var waiter = cache.GetAsync("model", _ => throw new InvalidOperationException(), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        release.SetResult(Rows());
        var snapshot = await owner;

        Assert.Same(snapshot, await cache.GetAsync("model", _ => throw new InvalidOperationException(), default));
    }

    [Fact]
    public async Task FailedLoad_IsRetriedRatherThanCached()
    {
        using var cache = new KnowledgeIndexSnapshotCache();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cache.GetAsync("model", _ => throw new InvalidOperationException(), default));

        Assert.Single((await cache.GetAsync("model", _ => Task.FromResult(Rows()), default)).Chunks);
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("[]")]
    [InlineData("[1e1000]")]
    public async Task InvalidPersistedVector_IsNotUsed(string json)
    {
        using var cache = new KnowledgeIndexSnapshotCache();
        var rows = Rows();
        rows[0].embeddingJson = json;

        var snapshot = await cache.GetAsync("model", _ => Task.FromResult(rows), default);

        Assert.Single(snapshot.Chunks);
        Assert.Null(snapshot.Vectors[0]);
    }

    private static List<AiKnowledgeChunk> Rows() =>
    [
        new() { sourcePath = "docs/guide.md", title = "Guide", body = "Documented behavior.",
            embeddingModel = "model", embeddingJson = "[1,0]" },
    ];

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
