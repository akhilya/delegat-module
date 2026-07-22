using Delegat.Infrastructure.Configuration;
using Delegat.Models;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services;

public sealed record LoggingSettingsDto(
    bool logErrorsEnabled,
    bool logApiRequestsEnabled,
    bool logAdapterHttpEnabled,
    DateTime updatedAt);

public sealed class UpdateLoggingSettingsRequest
{
    public bool? logErrorsEnabled { get; set; }
    public bool? logApiRequestsEnabled { get; set; }
    public bool? logAdapterHttpEnabled { get; set; }
}

public interface ILoggingSettingsService
{
    bool IsErrorLoggingEnabled { get; }
    bool IsApiRequestLoggingEnabled { get; }
    bool IsAdapterHttpLoggingEnabled { get; }

    /// <summary>Load or seed the singleton row and warm the in-memory cache.</summary>
    Task EnsureInitializedAsync(CancellationToken ct = default);

    Task<LoggingSettingsDto> GetAsync(CancellationToken ct = default);

    Task<LoggingSettingsDto> UpdateAsync(UpdateLoggingSettingsRequest request, CancellationToken ct = default);
}

/// <summary>
/// Cached instance logging toggles. Hot paths (middleware / log writers) read the cache only.
/// </summary>
public sealed class LoggingSettingsService : ILoggingSettingsService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LoggingSettingsService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private volatile LoggingSettingsDto _cache = new(
        logErrorsEnabled: true,
        logApiRequestsEnabled: false,
        logAdapterHttpEnabled: false,
        updatedAt: DateTime.UnixEpoch);

    private volatile bool _initialized;

    public LoggingSettingsService(
        IServiceScopeFactory scopeFactory,
        ILogger<LoggingSettingsService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public bool IsErrorLoggingEnabled => _cache.logErrorsEnabled;
    public bool IsApiRequestLoggingEnabled => _cache.logApiRequestsEnabled;
    public bool IsAdapterHttpLoggingEnabled => _cache.logAdapterHttpEnabled;

    public async Task EnsureInitializedAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        await GetAsync(ct);
    }

    public async Task<LoggingSettingsDto> GetAsync(CancellationToken ct = default)
    {
        if (_initialized) return _cache;

        await _gate.WaitAsync(ct);
        try
        {
            if (_initialized) return _cache;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();
            var row = await db.instanceLoggingSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == InstanceLoggingSettings.SingletonId, ct);

            if (row is null)
            {
                row = new InstanceLoggingSettings
                {
                    Id = InstanceLoggingSettings.SingletonId,
                    logErrorsEnabled = FeatureFlags.IsErrorLoggingEnabled(),
                    logApiRequestsEnabled = FeatureFlags.IsApiRequestLoggingEnabled(),
                    logAdapterHttpEnabled = FeatureFlags.IsAdapterHttpLoggingEnabled(),
                    updatedAt = DateTime.UtcNow,
                };
                db.instanceLoggingSettings.Add(row);
                await db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "Seeded instance logging settings from env (errors={Errors}, api={Api}, adapterHttp={Adapter})",
                    row.logErrorsEnabled, row.logApiRequestsEnabled, row.logAdapterHttpEnabled);
            }

            _cache = ToDto(row);
            _initialized = true;
            return _cache;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<LoggingSettingsDto> UpdateAsync(
        UpdateLoggingSettingsRequest request,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();
            var row = await db.instanceLoggingSettings
                .FirstOrDefaultAsync(x => x.Id == InstanceLoggingSettings.SingletonId, ct);

            if (row is null)
            {
                row = new InstanceLoggingSettings
                {
                    Id = InstanceLoggingSettings.SingletonId,
                    logErrorsEnabled = FeatureFlags.IsErrorLoggingEnabled(),
                    logApiRequestsEnabled = FeatureFlags.IsApiRequestLoggingEnabled(),
                    logAdapterHttpEnabled = FeatureFlags.IsAdapterHttpLoggingEnabled(),
                };
                db.instanceLoggingSettings.Add(row);
            }

            if (request.logErrorsEnabled is bool errors)
                row.logErrorsEnabled = errors;
            if (request.logApiRequestsEnabled is bool api)
                row.logApiRequestsEnabled = api;
            if (request.logAdapterHttpEnabled is bool adapter)
                row.logAdapterHttpEnabled = adapter;

            row.updatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            _cache = ToDto(row);
            _initialized = true;
            return _cache;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static LoggingSettingsDto ToDto(InstanceLoggingSettings row) => new(
        row.logErrorsEnabled,
        row.logApiRequestsEnabled,
        row.logAdapterHttpEnabled,
        row.updatedAt);
}
