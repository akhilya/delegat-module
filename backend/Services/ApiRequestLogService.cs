using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Delegat.Models;

namespace Delegat.Services;

public sealed class ApiRequestLogQuery
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int? ApiKeyId { get; set; }
    public string? Method { get; set; }
    public string? StatusClass { get; set; }
    public string? Q { get; set; }
    public int Limit { get; set; } = 50;
    public int Offset { get; set; }
}

public record ApiRequestLogSummaryDto(
    int Id,
    DateTime CreatedAt,
    int? ApiKeyId,
    string? KeyDisplayHint,
    string Method,
    string Path,
    string? QueryString,
    int StatusCode,
    int DurationMs,
    string? ErrorMessage);

public record ApiRequestLogDetailDto(
    int Id,
    DateTime CreatedAt,
    int? ApiKeyId,
    int? UserId,
    string? KeyDisplayHint,
    string Method,
    string Path,
    string? QueryString,
    int StatusCode,
    int DurationMs,
    string? RemoteIpAddress,
    string? UserAgent,
    string? RequestBody,
    string? ResponseBody,
    string? ErrorMessage);

public interface IApiRequestLogger
{
    void Log(ApiRequestLogEntry entry);
}

public interface IApiRequestLogQueryService
{
    Task<(List<ApiRequestLogSummaryDto> Items, int Total, int ErrorCount)> QueryAsync(
        int userId,
        ApiRequestLogQuery query,
        CancellationToken ct = default);

    Task<ApiRequestLogDetailDto?> GetDetailAsync(int userId, int id, CancellationToken ct = default);
}

public sealed class ApiRequestLogEntry
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? ApiKeyId { get; set; }
    public int? UserId { get; set; }
    public string? KeyDisplayHint { get; set; }
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public string? QueryString { get; set; }
    public int StatusCode { get; set; }
    public int DurationMs { get; set; }
    public string? RemoteIpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? RequestBody { get; set; }
    public string? ResponseBody { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Queues Public API request logs and persists them asynchronously.
/// </summary>
public sealed class ApiRequestLogService : IApiRequestLogger, IApiRequestLogQueryService, IAsyncDisposable
{
    public const int MaxBodyChars = 64 * 1024;
    public const int DefaultRetentionDays = 30;

    private static readonly Regex SensitiveJsonValue = new(
        @"""(?i:api[_-]?key|password|secret|token|authorization)""\s*:\s*""[^""]*""",
        RegexOptions.Compiled);

    private readonly Channel<ApiRequestLogEntry> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ApiRequestLogService> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;

    public ApiRequestLogService(IServiceScopeFactory scopeFactory, ILogger<ApiRequestLogService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _channel = Channel.CreateBounded<ApiRequestLogEntry>(new BoundedChannelOptions(2000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _worker = Task.Run(ProcessQueueAsync);
    }

    public void Log(ApiRequestLogEntry entry)
    {
        if (!Delegat.Infrastructure.Configuration.FeatureFlags.IsApiRequestLoggingEnabled())
            return;

        try
        {
            entry.RequestBody = Truncate(SanitizeBody(entry.RequestBody));
            entry.ResponseBody = Truncate(SanitizeBody(entry.ResponseBody));
            entry.ErrorMessage = Truncate(entry.ErrorMessage, 2000);
            entry.UserAgent = Truncate(entry.UserAgent, 512);
            entry.QueryString = Truncate(entry.QueryString, 2000);
            entry.Path = Truncate(entry.Path, 1000) ?? "";
            _channel.Writer.TryWrite(entry);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to enqueue API request log");
        }
    }

    public async Task<(List<ApiRequestLogSummaryDto> Items, int Total, int ErrorCount)> QueryAsync(
        int userId,
        ApiRequestLogQuery query,
        CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();

        var keyIds = await db.userApiKeys.AsNoTracking()
            .Where(k => k.userId == userId)
            .Select(k => k.Id)
            .ToListAsync(ct);

        var q = db.apiRequestLogs.AsNoTracking()
            .Where(l => l.userId == userId || (l.apiKeyId != null && keyIds.Contains(l.apiKeyId.Value)));

        if (query.From.HasValue)
            q = q.Where(l => l.createdAt >= query.From.Value);
        if (query.To.HasValue)
            q = q.Where(l => l.createdAt <= query.To.Value);
        if (query.ApiKeyId.HasValue)
            q = q.Where(l => l.apiKeyId == query.ApiKeyId.Value);
        if (!string.IsNullOrWhiteSpace(query.Method))
            q = q.Where(l => l.method == query.Method);
        if (!string.IsNullOrWhiteSpace(query.StatusClass))
        {
            q = query.StatusClass.ToLowerInvariant() switch
            {
                "2xx" => q.Where(l => l.statusCode >= 200 && l.statusCode < 300),
                "4xx" => q.Where(l => l.statusCode >= 400 && l.statusCode < 500),
                "5xx" => q.Where(l => l.statusCode >= 500),
                "error" => q.Where(l => l.statusCode >= 400 || l.errorMessage != null),
                _ => q
            };
        }
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            q = q.Where(l =>
                l.path.Contains(term) ||
                (l.queryString != null && l.queryString.Contains(term)) ||
                (l.keyDisplayHint != null && l.keyDisplayHint.Contains(term)) ||
                (l.errorMessage != null && l.errorMessage.Contains(term)));
        }

        var total = await q.CountAsync(ct);
        var errorCount = await q.CountAsync(l => l.statusCode >= 400 || l.errorMessage != null, ct);

        var limit = Math.Clamp(query.Limit, 1, 200);
        var offset = Math.Max(0, query.Offset);

        var items = await q
            .OrderByDescending(l => l.createdAt)
            .Skip(offset)
            .Take(limit)
            .Select(l => new ApiRequestLogSummaryDto(
                l.Id,
                l.createdAt,
                l.apiKeyId,
                l.keyDisplayHint,
                l.method,
                l.path,
                l.queryString,
                l.statusCode,
                l.durationMs,
                l.errorMessage))
            .ToListAsync(ct);

        return (items, total, errorCount);
    }

    public async Task<ApiRequestLogDetailDto?> GetDetailAsync(int userId, int id, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();

        var keyIds = await db.userApiKeys.AsNoTracking()
            .Where(k => k.userId == userId)
            .Select(k => k.Id)
            .ToListAsync(ct);

        var l = await db.apiRequestLogs.AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Id == id &&
                (x.userId == userId || (x.apiKeyId != null && keyIds.Contains(x.apiKeyId.Value))), ct);

        if (l == null) return null;

        return new ApiRequestLogDetailDto(
            l.Id,
            l.createdAt,
            l.apiKeyId,
            l.userId,
            l.keyDisplayHint,
            l.method,
            l.path,
            l.queryString,
            l.statusCode,
            l.durationMs,
            l.remoteIpAddress,
            l.userAgent,
            l.requestBody,
            l.responseBody,
            l.errorMessage);
    }

    private async Task ProcessQueueAsync()
    {
        var batch = new List<ApiRequestLogEntry>(32);
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
                    _logger.LogWarning(ex, "Failed to persist API request log batch");
                    await Task.Delay(1000, CancellationToken.None);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API request log worker stopped");
        }
    }

    private async Task PersistBatchAsync(List<ApiRequestLogEntry> batch)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();
        foreach (var entry in batch)
        {
            db.apiRequestLogs.Add(new ApiRequestLog
            {
                createdAt = entry.CreatedAt,
                apiKeyId = entry.ApiKeyId,
                userId = entry.UserId,
                keyDisplayHint = entry.KeyDisplayHint,
                method = entry.Method,
                path = entry.Path,
                queryString = entry.QueryString,
                statusCode = entry.StatusCode,
                durationMs = entry.DurationMs,
                remoteIpAddress = entry.RemoteIpAddress,
                userAgent = entry.UserAgent,
                requestBody = entry.RequestBody,
                responseBody = entry.ResponseBody,
                errorMessage = entry.ErrorMessage,
            });
        }
        await db.SaveChangesAsync(_cts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _channel.Writer.TryComplete();
        try { await _worker; } catch { /* ignore */ }
        _cts.Dispose();
    }

    private static string? SanitizeBody(string? body)
    {
        if (string.IsNullOrEmpty(body)) return body;
        return SensitiveJsonValue.Replace(body, m =>
        {
            var idx = m.Value.IndexOf(':');
            return idx < 0 ? "\"***\"" : m.Value[..(idx + 1)] + " \"***\"";
        });
    }

    private static string? Truncate(string? value, int max = MaxBodyChars)
    {
        if (value == null) return null;
        return value.Length <= max ? value : value[..max];
    }
}

public sealed class ApiRequestLogCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ApiRequestLogCleanupService> _logger;

    public ApiRequestLogCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<ApiRequestLogCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<DataContext>();
                var cutoff = DateTime.UtcNow.AddDays(-ApiRequestLogService.DefaultRetentionDays);
                var deleted = await db.apiRequestLogs
                    .Where(l => l.createdAt < cutoff)
                    .ExecuteDeleteAsync(stoppingToken);
                if (deleted > 0)
                    _logger.LogInformation("Deleted {Count} expired API request logs", deleted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "API request log cleanup failed");
            }

            await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
        }
    }
}
