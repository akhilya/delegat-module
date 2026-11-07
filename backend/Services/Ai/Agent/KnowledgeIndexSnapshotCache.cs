using Delegat.Models.Ai;
using System.Text.Json;

namespace Delegat.Services.Ai.Agent;

/// <summary>One bounded, process-local snapshot of this instance's global documentation vectors.</summary>
public sealed class KnowledgeIndexSnapshotCache(TimeProvider? timeProvider = null) : IDisposable
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Snapshot? _snapshot;
    private long _generation;

    internal sealed record Snapshot(
        string ModelId,
        long Generation,
        DateTimeOffset ExpiresAt,
        IReadOnlyList<KnowledgeChunk> Chunks,
        IReadOnlyList<float[]?> Vectors);

    internal async Task<Snapshot> GetAsync(
        string modelId,
        Func<CancellationToken, Task<List<AiKnowledgeChunk>>> load,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot = Volatile.Read(ref _snapshot);
        if (IsCurrent(snapshot, modelId)) return snapshot!;

        await _gate.WaitAsync(ct);
        try
        {
            snapshot = Volatile.Read(ref _snapshot);
            if (IsCurrent(snapshot, modelId)) return snapshot!;
            var generation = Volatile.Read(ref _generation);
            var rows = await load(ct);
            ct.ThrowIfCancellationRequested();
            snapshot = new Snapshot(modelId, generation, _clock.GetUtcNow().AddMinutes(1),
                rows.Select(c => new KnowledgeChunk(c.sourcePath, c.title, c.body)).ToArray(),
                rows.Select(c => ReadVector(c, modelId)).ToArray());
            // A reindex/update may finish while the rows are being loaded. Such a
            // snapshot can finish this read, but must not be reused by later reads.
            if (generation == Volatile.Read(ref _generation))
                Volatile.Write(ref _snapshot, snapshot);
            return snapshot;
        }
        finally { _gate.Release(); }
    }

    public void Invalidate()
    {
        Interlocked.Increment(ref _generation);
        Volatile.Write(ref _snapshot, null);
    }

    private bool IsCurrent(Snapshot? snapshot, string modelId) =>
        snapshot != null && snapshot.ModelId == modelId
        && snapshot.Generation == Volatile.Read(ref _generation)
        && snapshot.ExpiresAt > _clock.GetUtcNow();

    private static float[]? ReadVector(AiKnowledgeChunk chunk, string modelId)
    {
        if (chunk.embeddingModel != modelId || chunk.embeddingJson == null) return null;
        try
        {
            var vector = JsonSerializer.Deserialize<float[]>(chunk.embeddingJson);
            return vector is { Length: > 0 and <= 4096 } && vector.All(float.IsFinite)
                ? vector : null;
        }
        catch (JsonException) { return null; }
    }

    public void Dispose() => _gate.Dispose();
}
