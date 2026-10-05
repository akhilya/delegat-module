using System.Text.Json.Serialization;
using Delegat.Models;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services;

/// <summary>
/// Operation logs are read from the newest line backward, then shown oldest-first.
/// </summary>
public static class OperationLogQuery
{
    public const int MaxTake = 500;

    public static async Task<List<OperationLogLine>> LatestAsync(
        IQueryable<OperationLogEntry> source,
        int skip,
        int take,
        CancellationToken ct)
    {
        if (skip < 0) skip = 0;
        take = Math.Clamp(take, 1, MaxTake);

        var rows = await source
            .OrderByDescending(e => e.timestamp)
            .ThenByDescending(e => e.Id)
            .Skip(skip)
            .Take(take)
            .Select(e => new OperationLogLine(e.level, e.message, e.timestamp))
            .ToListAsync(ct);

        rows.Reverse();
        return rows;
    }
}

public readonly record struct OperationLogLine(
    [property: JsonPropertyName("level")] string Level,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("timestamp")] DateTime Timestamp);
