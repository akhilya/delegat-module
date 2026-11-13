using Microsoft.EntityFrameworkCore;

namespace Delegat.Services.Marketplace.Graph;

public enum MappingGraphSaveStatus
{
    Saved,
    NotFound,
    Conflict,
}

public readonly record struct MappingGraphSaveResult(MappingGraphSaveStatus Status, string GraphVersion);

/// <summary>
/// Whole-graph saves from the mapping editor. With an expected version the check and the write are
/// one conditional UPDATE, so two overlapping saves of the same version cannot both succeed.
/// </summary>
public static class MappingGraphStore
{
    public static async Task<MappingGraphSaveResult> SaveAsync(
        DataContext db,
        int connectionId,
        int mappingId,
        string? graphJson,
        string? expectedGraphVersion,
        CancellationToken ct = default)
    {
        var current = await ReadAsync(db, connectionId, mappingId, ct);
        if (current == null)
            return new(MappingGraphSaveStatus.NotFound, MappingGraphVersion.Empty);

        if (!MappingGraphVersion.Matches(current.Value.GraphJson, expectedGraphVersion))
            return new(MappingGraphSaveStatus.Conflict, MappingGraphVersion.Compute(current.Value.GraphJson));

        var written = string.IsNullOrEmpty(expectedGraphVersion)
            ? await TryWriteAsync(db, connectionId, mappingId, graphJson, original: null, ct)
            : await TryWriteAsync(db, connectionId, mappingId, graphJson, current, ct);
        if (written)
            return new(MappingGraphSaveStatus.Saved, MappingGraphVersion.Compute(graphJson));

        var latest = await ReadAsync(db, connectionId, mappingId, ct);
        return latest == null
            ? new(MappingGraphSaveStatus.NotFound, MappingGraphVersion.Empty)
            : new(MappingGraphSaveStatus.Conflict, MappingGraphVersion.Compute(latest.Value.GraphJson));
    }

    public readonly record struct StoredGraph(string? GraphJson, DateTime? GraphUpdatedAt);

    public static async Task<StoredGraph?> ReadAsync(
        DataContext db, int connectionId, int mappingId, CancellationToken ct = default)
    {
        var rows = await db.categoryMappings
            .AsNoTracking()
            .Where(m => m.Id == mappingId && m.connectionId == connectionId)
            .Select(m => new StoredGraph(m.graphJson, m.graphUpdatedAt))
            .Take(1)
            .ToListAsync(ct);
        return rows.Count == 0 ? null : rows[0];
    }

    /// <summary>
    /// Writes the graph. When <paramref name="original"/> is set, the row is updated only if its
    /// graph and save timestamp still equal that snapshot; returns false when another write won.
    /// </summary>
    public static async Task<bool> TryWriteAsync(
        DataContext db,
        int connectionId,
        int mappingId,
        string? graphJson,
        StoredGraph? original,
        CancellationToken ct = default)
    {
        var query = db.categoryMappings.Where(m => m.Id == mappingId && m.connectionId == connectionId);
        if (original is { } snapshot)
        {
            var originalJson = snapshot.GraphJson;
            var originalUpdatedAt = snapshot.GraphUpdatedAt;
            query = query.Where(m => m.graphJson == originalJson && m.graphUpdatedAt == originalUpdatedAt);
        }

        // ExecuteUpdate bypasses DataContext.SaveChanges, so stamp graphUpdatedAt here.
        var now = DateTime.UtcNow;
        var rows = await query.ExecuteUpdateAsync(s => s
            .SetProperty(m => m.graphJson, graphJson)
            .SetProperty(m => m.graphUpdatedAt, now), ct);
        return rows > 0;
    }
}
