using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Security;
using Delegat.Marketplace.SDK;
using Delegat.Models.Marketplace;
using Delegat.Services;
using Delegat.Infrastructure.Middleware;

namespace Delegat.Services.Marketplace;

public interface IAdapterHttpLogQueryService
{
    Task<(List<AdapterHttpLogSummaryDto> Items, int Total, int ErrorCount)> QueryAsync(
        int storeId,
        AdapterHttpLogQuery query,
        CancellationToken ct = default);

    Task<AdapterHttpLogDetailDto?> GetDetailAsync(int storeId, int id, CancellationToken ct = default);

    /// <summary>Returns the full stored request or response body (not UI-preview truncated).</summary>
    Task<string?> GetStoredBodyAsync(int storeId, int id, bool response, CancellationToken ct = default);
}

public sealed class AdapterHttpLogQuery
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int? ConnectionId { get; set; }
    public string? AdapterKey { get; set; }
    public string? Method { get; set; }
    public int? StatusCode { get; set; }
    public string? StatusClass { get; set; } // 2xx, 4xx, 5xx
    public string? Q { get; set; }
    public string? SortBy { get; set; }
    public string? SortDir { get; set; }
    public int Limit { get; set; } = 50;
    public int Offset { get; set; }
}

public record AdapterHttpLogSummaryDto(
    int Id,
    DateTime CreatedAt,
    int ConnectionId,
    string AdapterKey,
    string Method,
    string Path,
    string Url,
    int StatusCode,
    int DurationMs,
    string? ErrorMessage);

public record AdapterHttpLogDetailDto(
    int Id,
    DateTime CreatedAt,
    int StoreId,
    int ConnectionId,
    string AdapterKey,
    string Method,
    string Url,
    string Path,
    int StatusCode,
    int DurationMs,
    string? RequestHeadersJson,
    string? RequestBody,
    bool RequestBodyTruncated,
    int RequestBodyLength,
    string? ResponseHeadersJson,
    string? ResponseBody,
    bool ResponseBodyTruncated,
    int ResponseBodyLength,
    string? ErrorMessage,
    string? OperationId,
    bool DetailsAvailable = true);

/// <summary>
/// Queues adapter HTTP logs and persists them asynchronously. Also implements SDK sink.
/// </summary>
public sealed class AdapterHttpLogService : IAdapterHttpLogger, IAdapterHttpLogQueryService, IAsyncDisposable
{
    /// <summary>Preview size returned in detail responses (full body via download endpoint).</summary>
    public const int MaxPreviewBodyChars = 16 * 1024;

    /// <summary>Hard cap for bodies written to the database.</summary>
    public const int MaxStoredBodyChars = 64 * 1024;
    public const int MaxStoredHeadersChars = 16 * 1024;

    /// <summary>Backward-compatible alias for <see cref="MaxStoredBodyChars"/>.</summary>
    public const int MaxBodyChars = MaxStoredBodyChars;

    public const string TruncationMarker = AdapterHttpLogHelper.TruncationMarker;

    public const int DefaultRetentionDays = 7;
    public const int DefaultDetailRetentionHours = 24;
    public const int MaxDetailedRowsSafety = 5_000;
    public const int MaxRowsSafety = 200_000;
    private const int CleanupDeleteBatchSize = 500;

    private static readonly HashSet<string> SensitiveHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Api-Key", "Authorization", "Client-Secret", "X-Api-Key", "Cookie",
        "X-Client-Secret", "Proxy-Authorization", "Set-Cookie", "Client-Id",
        "X-Delegat-Api-Key", "Delegat-Api-Key"
    };

    private static readonly Regex SensitiveJsonValue = new(
        @"""(?i:api[_-]?key|password|secret|token|authorization)""\s*:\s*""[^""]*""",
        RegexOptions.Compiled);

    private readonly Channel<AdapterHttpLogEntry> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AdapterHttpLogService> _logger;
    private readonly ILoggingSettingsService _loggingSettings;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;
    private int _disposed;

    public AdapterHttpLogService(
        IServiceScopeFactory scopeFactory,
        ILogger<AdapterHttpLogService> logger,
        ILoggingSettingsService loggingSettings)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _loggingSettings = loggingSettings;
        _channel = Channel.CreateBounded<AdapterHttpLogEntry>(new BoundedChannelOptions(2000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _worker = Task.Run(ProcessQueueAsync);
    }

    public void Log(AdapterHttpLogEntry entry)
    {
        if (!_loggingSettings.IsAdapterHttpLoggingEnabled)
            return;

        try
        {
            var sanitized = Sanitize(entry);
            _channel.Writer.TryWrite(sanitized);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to enqueue adapter HTTP log");
        }
    }

    public async Task<(List<AdapterHttpLogSummaryDto> Items, int Total, int ErrorCount)> QueryAsync(
        int storeId,
        AdapterHttpLogQuery query,
        CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();

        var q = db.adapterHttpLogs.AsNoTracking().Where(l => l.storeId == storeId);
        q = ApplyFilters(q, query);

        // Group counts by status in one covered index scan. Separate Count queries
        // caused two full scans of large per-store log tables on every page load.
        var statusCounts = await q
            .GroupBy(l => l.statusCode)
            .Select(g => new { StatusCode = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var total = statusCounts.Sum(x => x.Count);
        var errorCount = statusCounts
            .Where(x => x.StatusCode >= 400 || x.StatusCode == 0)
            .Sum(x => x.Count);

        var limit = Math.Clamp(query.Limit, 1, 200);
        var offset = Math.Max(0, query.Offset);

        var items = await ApplySort(q, query.SortBy, query.SortDir)
            .Skip(offset)
            .Take(limit)
            .Select(l => new AdapterHttpLogSummaryDto(
                l.Id,
                l.createdAt,
                l.connectionId,
                l.adapterKey,
                l.method,
                l.path,
                l.url,
                l.statusCode,
                l.durationMs,
                l.errorMessage))
            .ToListAsync(ct);

        return (items, total, errorCount);
    }

    private static IOrderedQueryable<AdapterHttpLog> ApplySort(
        IQueryable<AdapterHttpLog> q,
        string? sortBy,
        string? sortDir)
    {
        var asc = string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
        IOrderedQueryable<AdapterHttpLog> ordered = (sortBy?.Trim().ToLowerInvariant()) switch
        {
            "method" => asc ? q.OrderBy(l => l.method) : q.OrderByDescending(l => l.method),
            "path" => asc ? q.OrderBy(l => l.path) : q.OrderByDescending(l => l.path),
            "adapterkey" or "adapter" => asc
                ? q.OrderBy(l => l.adapterKey)
                : q.OrderByDescending(l => l.adapterKey),
            "durationms" or "duration" => asc
                ? q.OrderBy(l => l.durationMs)
                : q.OrderByDescending(l => l.durationMs),
            "statuscode" or "status" or "code" => asc
                ? q.OrderBy(l => l.statusCode)
                : q.OrderByDescending(l => l.statusCode),
            _ => asc
                ? q.OrderBy(l => l.createdAt)
                : q.OrderByDescending(l => l.createdAt),
        };
        // Stable tie-break so Skip/Take pages stay deterministic.
        return ordered.ThenByDescending(l => l.Id);
    }

    public async Task<AdapterHttpLogDetailDto?> GetDetailAsync(int storeId, int id, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();

        var cutoff = DateTime.UtcNow.AddHours(-GetDetailRetentionHours());
        return await ProjectDetails(db.adapterHttpLogs.AsNoTracking()
            .Where(l => l.Id == id && l.storeId == storeId), cutoff).FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// SQL-translatable bounded projection; the caller must scope the input query to its store.
    /// Expired payloads are hidden even before cleanup runs.
    /// </summary>
    public static IQueryable<AdapterHttpLogDetailDto> ProjectDetails(IQueryable<AdapterHttpLog> query, DateTime cutoff) =>
        query.Select(l => new AdapterHttpLogDetailDto(
            l.Id,
            l.createdAt,
            l.storeId,
            l.connectionId,
            l.adapterKey,
            l.method,
            l.url,
            l.path,
            l.statusCode,
            l.durationMs,
            l.createdAt >= cutoff && l.requestHeadersJson != null
                ? l.requestHeadersJson.Length > MaxStoredHeadersChars ? l.requestHeadersJson.Substring(0, MaxStoredHeadersChars) : l.requestHeadersJson : null,
            l.createdAt >= cutoff && l.requestBody != null
                ? l.requestBody.Length > MaxPreviewBodyChars ? l.requestBody.Substring(0, MaxPreviewBodyChars) + TruncationMarker : l.requestBody : null,
            l.createdAt >= cutoff && l.requestBody != null &&
                (l.requestBody.Length > MaxPreviewBodyChars || l.requestBody.EndsWith(TruncationMarker)),
            l.createdAt >= cutoff && l.requestBody != null ? l.requestBody.Length : 0,
            l.createdAt >= cutoff && l.responseHeadersJson != null
                ? l.responseHeadersJson.Length > MaxStoredHeadersChars ? l.responseHeadersJson.Substring(0, MaxStoredHeadersChars) : l.responseHeadersJson : null,
            l.createdAt >= cutoff && l.responseBody != null
                ? l.responseBody.Length > MaxPreviewBodyChars ? l.responseBody.Substring(0, MaxPreviewBodyChars) + TruncationMarker : l.responseBody : null,
            l.createdAt >= cutoff && l.responseBody != null &&
                (l.responseBody.Length > MaxPreviewBodyChars || l.responseBody.EndsWith(TruncationMarker)),
            l.createdAt >= cutoff && l.responseBody != null ? l.responseBody.Length : 0,
            l.errorMessage,
            l.operationId,
            l.createdAt >= cutoff && (l.requestBody != null || l.responseBody != null ||
                l.requestHeadersJson != null || l.responseHeadersJson != null)));

    public async Task<string?> GetStoredBodyAsync(int storeId, int id, bool response, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();

        var cutoff = DateTime.UtcNow.AddHours(-GetDetailRetentionHours());
        var l = await db.adapterHttpLogs.AsNoTracking()
            .Where(x => x.Id == id && x.storeId == storeId)
            .Select(x => new
            {
                Available = x.createdAt >= cutoff && (x.requestBody != null || x.responseBody != null ||
                    x.requestHeadersJson != null || x.responseHeadersJson != null),
                Body = x.createdAt < cutoff ? null : response ? x.responseBody : x.requestBody
            })
            .FirstOrDefaultAsync(ct);
        if (l == null) return null;
        if (!l.Available)
            throw ApiException.NotFound(code: "log_details_unavailable");
        return l.Body ?? "";
    }

    public async Task CleanupAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();

        var retentionDays = GetRetentionDays();

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var oldDeleted = await DeleteOldestAdapterLogsAsync(db, cutoff, 100_000, ct);
        if (oldDeleted > 0)
            _logger.LogInformation("Adapter HTTP log cleanup removed {Count} rows older than {Days}d", oldDeleted, retentionDays);

        var total = await db.adapterHttpLogs.CountAsync(ct);
        if (total > MaxRowsSafety)
        {
            var excess = Math.Min(total - MaxRowsSafety, 100_000);
            var deleted = await DeleteOldestAdapterLogsAsync(db, null, excess, ct);
            if (deleted > 0)
                _logger.LogInformation("Adapter HTTP log safety cap removed {Count} oldest rows", deleted);
        }

        var detailCutoff = DateTime.UtcNow.AddHours(-GetDetailRetentionHours());
        var compacted = await ClearDetailsAsync(db, WithDetails(db.adapterHttpLogs)
            .Where(l => l.createdAt < detailCutoff), 100_000, ct);

        // Also bound storage during high-volume imports within the retention window.
        var boundary = await WithDetails(db.adapterHttpLogs).AsNoTracking()
            .OrderByDescending(l => l.createdAt).ThenByDescending(l => l.Id)
            .Skip(MaxDetailedRowsSafety)
            .Select(l => new { l.createdAt, l.Id }).FirstOrDefaultAsync(ct);
        if (boundary != null)
            compacted += await ClearDetailsAsync(db, WithDetails(db.adapterHttpLogs).Where(l =>
                l.createdAt < boundary.createdAt || l.createdAt == boundary.createdAt && l.Id <= boundary.Id), 100_000, ct);
        if (compacted > 0)
            _logger.LogInformation("Adapter HTTP log cleanup cleared payloads from {Count} rows; summaries retained", compacted);
    }

    private static int GetRetentionDays() =>
        int.TryParse(Environment.GetEnvironmentVariable("ADAPTER_HTTP_LOG_RETENTION_DAYS"), out var days)
            ? Math.Clamp(days, 1, 30) : DefaultRetentionDays;

    private static int GetDetailRetentionHours() =>
        Math.Min(GetRetentionDays() * 24,
            int.TryParse(Environment.GetEnvironmentVariable("ADAPTER_HTTP_LOG_DETAIL_RETENTION_HOURS"), out var hours)
                ? Math.Clamp(hours, 1, 168) : DefaultDetailRetentionHours);

    private static IQueryable<AdapterHttpLog> WithDetails(IQueryable<AdapterHttpLog> query) =>
        query.Where(l => l.requestBody != null || l.responseBody != null ||
            l.requestHeadersJson != null || l.responseHeadersJson != null);

    private static async Task<int> ClearDetailsAsync(DataContext db, IQueryable<AdapterHttpLog> query,
        int maxRows, CancellationToken ct)
    {
        var cleared = 0;
        DateTime? lastCreatedAt = null;
        var lastId = 0;
        while (cleared < maxRows)
        {
            var batchQuery = query.AsNoTracking();
            // Compacted summaries remain in the table. Advance past them instead of
            // rescanning every previously cleared row on each cleanup batch.
            if (lastCreatedAt.HasValue)
                batchQuery = batchQuery.Where(l => l.createdAt > lastCreatedAt.Value ||
                    l.createdAt == lastCreatedAt.Value && l.Id > lastId);
            var batch = await batchQuery.OrderBy(l => l.createdAt).ThenBy(l => l.Id)
                .Select(l => new { l.Id, l.createdAt })
                .Take(Math.Min(CleanupDeleteBatchSize, maxRows - cleared)).ToListAsync(ct);
            if (batch.Count == 0) break;
            lastCreatedAt = batch[^1].createdAt;
            lastId = batch[^1].Id;
            var ids = batch.Select(l => l.Id).ToList();
            var rows = db.adapterHttpLogs.Where(l => ids.Contains(l.Id));
            if (db.Database.IsRelational())
                cleared += await rows.ExecuteUpdateAsync(setters => setters
                    .SetProperty(l => l.requestBody, (string?)null)
                    .SetProperty(l => l.responseBody, (string?)null)
                    .SetProperty(l => l.requestHeadersJson, (string?)null)
                    .SetProperty(l => l.responseHeadersJson, (string?)null), ct);
            else
            {
                var entries = await rows.ToListAsync(ct);
                foreach (var entry in entries)
                {
                    entry.requestBody = entry.responseBody = null;
                    entry.requestHeadersJson = entry.responseHeadersJson = null;
                }
                cleared += entries.Count;
                await db.SaveChangesAsync(ct);
            }
        }
        return cleared;
    }

    private static async Task<int> DeleteOldestAdapterLogsAsync(
        DataContext db, DateTime? before, int maxRows, CancellationToken ct)
    {
        var deleted = 0;
        while (deleted < maxRows)
        {
            var idsQuery = db.adapterHttpLogs.AsNoTracking();
            if (before.HasValue)
                idsQuery = idsQuery.Where(l => l.createdAt < before.Value);
            var ids = await idsQuery.OrderBy(l => l.createdAt).ThenBy(l => l.Id)
                .Select(l => l.Id).Take(Math.Min(CleanupDeleteBatchSize, maxRows - deleted)).ToListAsync(ct);
            if (ids.Count == 0) break;
            if (db.Database.IsRelational())
                deleted += await db.adapterHttpLogs.Where(l => ids.Contains(l.Id)).ExecuteDeleteAsync(ct);
            else
            {
                var rows = await db.adapterHttpLogs.Where(l => ids.Contains(l.Id)).ToListAsync(ct);
                db.adapterHttpLogs.RemoveRange(rows);
                deleted += rows.Count;
                await db.SaveChangesAsync(ct);
            }
        }
        return deleted;
    }

    /// <summary>
    /// Redacts secrets and caps length for DB persistence (downloadable up to this size).
    /// </summary>
    public static string TruncateBody(string? body)
    {
        if (string.IsNullOrEmpty(body)) return body ?? "";
        var redacted = MediaExportGrantSecrets.Redact(RedactBody(body));
        if (redacted.Length <= MaxStoredBodyChars) return redacted;
        return redacted[..MaxStoredBodyChars] + TruncationMarker;
    }

    /// <summary>
    /// Builds a short UI preview; full stored body is available via download.
    /// </summary>
    public static (string Preview, bool Truncated, int FullLength) PreviewBody(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return ("", false, 0);

        var fullLength = body.Length;
        var alreadyMarked = body.EndsWith(TruncationMarker, StringComparison.Ordinal);
        if (body.Length <= MaxPreviewBodyChars)
            return (body, alreadyMarked, fullLength);

        return (body[..MaxPreviewBodyChars] + TruncationMarker, true, fullLength);
    }

    public static string RedactHeadersJson(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in headers)
        {
            var value = string.Join(", ", h.Value);
            dict[h.Key] = SensitiveHeaderNames.Contains(h.Key) ? "***" : value;
        }
        return JsonSerializer.Serialize(dict);
    }

    public static string RedactBody(string body)
    {
        string redacted;
        try
        {
            redacted = SensitiveJsonValue.Replace(body, m =>
            {
                var colon = m.Value.IndexOf(':');
                if (colon < 0) return m.Value;
                var keyPart = m.Value[..colon];
                return $"{keyPart}: \"***\"";
            });
        }
        catch
        {
            redacted = body;
        }
        return MediaExportGrantSecrets.Redact(redacted);
    }

    private static AdapterHttpLogEntry Sanitize(AdapterHttpLogEntry e) => new()
    {
        StoreId = e.StoreId,
        ConnectionId = e.ConnectionId,
        AdapterKey = e.AdapterKey ?? "",
        Method = e.Method ?? "",
        Url = Truncate(MediaExportGrantSecrets.Redact(e.Url), 2000),
        Path = Truncate(MediaExportGrantSecrets.Redact(e.Path), 1000),
        StatusCode = e.StatusCode,
        DurationMs = e.DurationMs,
        RequestHeadersJson = Truncate(MediaExportGrantSecrets.Redact(e.RequestHeadersJson), MaxStoredHeadersChars),
        RequestBody = TruncateBody(e.RequestBody),
        ResponseHeadersJson = Truncate(MediaExportGrantSecrets.Redact(e.ResponseHeadersJson), MaxStoredHeadersChars),
        ResponseBody = TruncateBody(e.ResponseBody),
        ErrorMessage = Truncate(MediaExportGrantSecrets.Redact(e.ErrorMessage), 2000),
        OperationId = Truncate(e.OperationId, 64)
    };

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        return s.Length <= max ? s : s[..max];
    }

    private static IQueryable<AdapterHttpLog> ApplyFilters(IQueryable<AdapterHttpLog> q, AdapterHttpLogQuery query)
    {
        if (query.From.HasValue)
            q = q.Where(l => l.createdAt >= query.From.Value);
        if (query.To.HasValue)
            q = q.Where(l => l.createdAt <= query.To.Value);
        if (query.ConnectionId.HasValue)
            q = q.Where(l => l.connectionId == query.ConnectionId.Value);
        if (!string.IsNullOrWhiteSpace(query.AdapterKey))
            q = q.Where(l => l.adapterKey == query.AdapterKey);
        if (!string.IsNullOrWhiteSpace(query.Method))
            q = q.Where(l => l.method == query.Method.ToUpperInvariant());
        if (query.StatusCode.HasValue)
            q = q.Where(l => l.statusCode == query.StatusCode.Value);
        if (!string.IsNullOrWhiteSpace(query.StatusClass))
        {
            q = query.StatusClass.ToLowerInvariant() switch
            {
                "2xx" => q.Where(l => l.statusCode >= 200 && l.statusCode < 300),
                "4xx" => q.Where(l => l.statusCode >= 400 && l.statusCode < 500),
                "5xx" => q.Where(l => l.statusCode >= 500 && l.statusCode < 600),
                "error" => q.Where(l => l.statusCode >= 400 || l.statusCode == 0),
                _ => q
            };
        }
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            q = q.Where(l => l.path.Contains(term) || l.url.Contains(term));
        }
        return q;
    }

    private async Task ProcessQueueAsync()
    {
        var batch = new List<AdapterHttpLogEntry>(32);
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                batch.Clear();
                try
                {
                    var first = await _channel.Reader.ReadAsync(_cts.Token);
                    batch.Add(first);
                    while (batch.Count < 32 && _channel.Reader.TryRead(out var next))
                        batch.Add(next);

                    await PersistBatchAsync(batch);
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Adapter HTTP log writer failed");
                    await Task.Delay(1000, CancellationToken.None);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Adapter HTTP log worker stopped");
        }
    }

    private async Task PersistBatchAsync(List<AdapterHttpLogEntry> batch)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();
        foreach (var e in batch)
        {
            db.adapterHttpLogs.Add(new AdapterHttpLog
            {
                createdAt = DateTime.UtcNow,
                storeId = e.StoreId,
                connectionId = e.ConnectionId,
                adapterKey = e.AdapterKey,
                method = e.Method,
                url = e.Url,
                path = e.Path,
                statusCode = e.StatusCode,
                durationMs = e.DurationMs,
                requestHeadersJson = e.RequestHeadersJson,
                requestBody = e.RequestBody,
                responseHeadersJson = e.ResponseHeadersJson,
                responseBody = e.ResponseBody,
                errorMessage = e.ErrorMessage,
                operationId = e.OperationId
            });
        }
        await db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        _channel.Writer.TryComplete();
        try { await _worker.ConfigureAwait(false); } catch { /* ignore */ }
        _cts.Dispose();
    }
}
