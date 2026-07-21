using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Delegat.Infrastructure.Configuration;

namespace Delegat.Services.PlatformLink;

public interface IPlatformErrorReporter
{
    void ReportFireAndForget(HttpContext context, Exception exception, int statusCode);
}

/// <summary>
/// Samples and deduplicates 5xx reports to Control Plane (non-blocking).
/// </summary>
public sealed class PlatformErrorReporter : IPlatformErrorReporter
{
    private static readonly TimeSpan DedupWindow = TimeSpan.FromMinutes(5);
    private const double SampleRate = 1.0; // report all unique fingerprints within window

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PlatformLinkOptions _options;
    private readonly ILogger<PlatformErrorReporter> _logger;
    private readonly ConcurrentDictionary<string, DateTime> _recent = new();

    public PlatformErrorReporter(
        IServiceScopeFactory scopeFactory,
        PlatformLinkOptions options,
        ILogger<PlatformErrorReporter> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    public void ReportFireAndForget(HttpContext context, Exception exception, int statusCode)
    {
        if (!_options.IsEnabled || statusCode < 500)
            return;

        var path = context.Request.Path.Value;
        var method = context.Request.Method;
        var fingerprint = Fingerprint(exception, method, path);

        var now = DateTime.UtcNow;
        Prune(now);
        if (!_recent.TryAdd(fingerprint, now))
        {
            if (_recent.TryGetValue(fingerprint, out var last) && now - last < DedupWindow)
                return;
            _recent[fingerprint] = now;
        }

        if (SampleRate < 1.0 && Random.Shared.NextDouble() > SampleRate)
            return;

        var report = new PlatformErrorReportDto
        {
            InstanceId = _options.InstanceId ?? "",
            OccurredAt = now,
            StatusCode = statusCode,
            Path = Truncate(path, 500),
            Method = method,
            Message = Truncate(exception.Message, 1000),
            ExceptionType = exception.GetType().FullName,
            Fingerprint = fingerprint,
            PlatformVersion = GetPlatformVersion(),
        };

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var client = scope.ServiceProvider.GetRequiredService<IPlatformLinkClient>();
                await client.ReportErrorAsync(report);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to report error to Control Plane");
            }
        });
    }

    private void Prune(DateTime now)
    {
        foreach (var kv in _recent)
        {
            if (now - kv.Value > DedupWindow)
                _recent.TryRemove(kv.Key, out _);
        }
    }

    private static string Fingerprint(Exception ex, string? method, string? path)
    {
        var raw = $"{ex.GetType().FullName}|{method}|{path}|{ex.Message}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private static string? Truncate(string? v, int max) =>
        string.IsNullOrEmpty(v) ? v : (v.Length <= max ? v : v[..max]);

    private static string? GetPlatformVersion() =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString();
}
