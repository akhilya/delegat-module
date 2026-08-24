using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Delegat.Marketplace.SDK;
using Delegat.Models.Marketplace;
using Delegat.Services;

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
    string? OperationId);

/// <summary>
/// Queues adapter HTTP logs and persists them asynchronously. Also implements SDK sink.
/// </summary>
public sealed class AdapterHttpLogService : IAdapterHttpLogger, IAdapterHttpLogQueryService, IAsyncDisposable
{
    /// <summary>Preview size returned in detail responses (full body via download endpoint).</summary>
    public const int MaxPreviewBodyChars = AdapterHttpLogHelper.MaxPreviewBodyChars;

    /// <summary>Hard cap for bodies written to the database.</summary>
    public const int MaxStoredBodyChars = AdapterHttpLogHelper.MaxStoredBodyChars;

    /// <summary>Backward-compatible alias for <see cref="MaxStoredBodyChars"/>.</summary>
    public const int MaxBodyChars = MaxStoredBodyChars;

    public const string TruncationMarker = AdapterHttpLogHelper.TruncationMarker;

    public const int DefaultRetentionDays = 7;
    public const int MaxRowsSafety = 200_000;

    private static readonly HashSet<string> SensitiveHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Api-Key", "Authorization", "Client-Secret", "X-Api-Key", "Cookie",
        "X-Client-Secret", "Proxy-Authorization", "Set-Cookie"
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

        var total = await q.CountAsync(ct);
        var errorCount = await q.CountAsync(l => l.statusCode >= 400 || l.statusCode == 0, ct);

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

        var l = await db.adapterHttpLogs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.storeId == storeId, ct);
        if (l == null) return null;

        var (reqPreview, reqTruncated, reqLen) = PreviewBody(l.requestBody);
        var (resPreview, resTruncated, resLen) = PreviewBody(l.responseBody);

        return new AdapterHttpLogDetailDto(
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
            l.requestHeadersJson,
            reqPreview,
            reqTruncated,
            reqLen,
            l.responseHeadersJson,
            resPreview,
            resTruncated,
            resLen,
            l.errorMessage,
            l.operationId);
    }

    public async Task<string?> GetStoredBodyAsync(int storeId, int id, bool response, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();

        var l = await db.adapterHttpLogs.AsNoTracking()
            .Where(x => x.Id == id && x.storeId == storeId)
            .Select(x => new { x.requestBody, x.responseBody })
            .FirstOrDefaultAsync(ct);
        if (l == null) return null;
        return response ? l.responseBody : l.requestBody;
    }

    public async Task CleanupAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();

        var retentionDays = DefaultRetentionDays;
        var raw = Environment.GetEnvironmentVariable("ADAPTER_HTTP_LOG_RETENTION_DAYS");
        if (int.TryParse(raw, out var days))
            retentionDays = Math.Clamp(days, 1, 30);

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var old = await db.adapterHttpLogs
            .Where(l => l.createdAt < cutoff)
            .OrderBy(l => l.createdAt)
            .ThenBy(l => l.Id)
            .Take(5000)
            .ToListAsync(ct);
        if (old.Count > 0)
        {
            db.adapterHttpLogs.RemoveRange(old);
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Adapter HTTP log cleanup removed {Count} rows older than {Days}d", old.Count, retentionDays);
        }

        var total = await db.adapterHttpLogs.CountAsync(ct);
        if (total > MaxRowsSafety)
        {
            var excess = total - MaxRowsSafety;
            var oldest = await db.adapterHttpLogs
                .OrderBy(l => l.createdAt)
                .Take(Math.Min(excess, 5000))
                .ToListAsync(ct);
            if (oldest.Count > 0)
            {
                db.adapterHttpLogs.RemoveRange(oldest);
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("Adapter HTTP log safety cap removed {Count} oldest rows", oldest.Count);
            }
        }
    }

    /// <summary>
    /// Redacts secrets and caps length for DB persistence (downloadable up to this size).
    /// </summary>
    public static string TruncateBody(string? body)
    {
        if (string.IsNullOrEmpty(body)) return body ?? "";
        var redacted = RedactBody(body);
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
        try
        {
            return SensitiveJsonValue.Replace(body, m =>
            {
                var colon = m.Value.IndexOf(':');
                if (colon < 0) return m.Value;
                var keyPart = m.Value[..colon];
                return $"{keyPart}: \"***\"";
            });
        }
        catch
        {
            return body;
        }
    }

    private static AdapterHttpLogEntry Sanitize(AdapterHttpLogEntry e) => new()
    {
        StoreId = e.StoreId,
        ConnectionId = e.ConnectionId,
        AdapterKey = e.AdapterKey ?? "",
        Method = e.Method ?? "",
        Url = Truncate(e.Url, 2000),
        Path = Truncate(e.Path, 1000),
        StatusCode = e.StatusCode,
        DurationMs = e.DurationMs,
        RequestHeadersJson = e.RequestHeadersJson,
        RequestBody = TruncateBody(e.RequestBody),
        ResponseHeadersJson = e.ResponseHeadersJson,
        ResponseBody = TruncateBody(e.ResponseBody),
        ErrorMessage = Truncate(e.ErrorMessage, 2000),
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
