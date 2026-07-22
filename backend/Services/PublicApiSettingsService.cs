using Delegat.Infrastructure.Configuration;
using Delegat.Models;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services;

public sealed record PublicApiSettingsDto(
    bool publicApiEnabled,
    DateTime updatedAt);

public sealed class UpdatePublicApiSettingsRequest
{
    public bool? publicApiEnabled { get; set; }
}

public interface IPublicApiSettingsService
{
    bool IsPublicApiEnabled { get; }

    /// <summary>Load or seed the singleton row and warm the in-memory cache.</summary>
    Task EnsureInitializedAsync(CancellationToken ct = default);

    Task<PublicApiSettingsDto> GetAsync(CancellationToken ct = default);

    Task<PublicApiSettingsDto> UpdateAsync(UpdatePublicApiSettingsRequest request, CancellationToken ct = default);
}

/// <summary>
/// Cached Public API enable toggle. Hot paths (middleware) read the cache only.
/// </summary>
public sealed class PublicApiSettingsService : IPublicApiSettingsService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILoggingSettingsService _loggingSettings;
    private readonly ILogger<PublicApiSettingsService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private volatile PublicApiSettingsDto _cache = new(
        publicApiEnabled: true,
        updatedAt: DateTime.UnixEpoch);

    private volatile bool _initialized;

    public PublicApiSettingsService(
        IServiceScopeFactory scopeFactory,
        ILoggingSettingsService loggingSettings,
        ILogger<PublicApiSettingsService> logger)
    {
        _scopeFactory = scopeFactory;
        _loggingSettings = loggingSettings;
        _logger = logger;
    }

    public bool IsPublicApiEnabled => _cache.publicApiEnabled;

    public async Task EnsureInitializedAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        await GetAsync(ct);
    }

    public async Task<PublicApiSettingsDto> GetAsync(CancellationToken ct = default)
    {
        if (_initialized) return _cache;

        await _gate.WaitAsync(ct);
        try
        {
            if (_initialized) return _cache;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();
            var row = await db.instancePublicApiSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == InstancePublicApiSettings.SingletonId, ct);

            if (row is null)
            {
                row = new InstancePublicApiSettings
                {
                    Id = InstancePublicApiSettings.SingletonId,
                    publicApiEnabled = FeatureFlags.IsPublicApiEnabled(),
                    updatedAt = DateTime.UtcNow,
                };
                db.instancePublicApiSettings.Add(row);
                await db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "Seeded instance Public API settings from env (enabled={Enabled})",
                    row.publicApiEnabled);
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

    public async Task<PublicApiSettingsDto> UpdateAsync(
        UpdatePublicApiSettingsRequest request,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();
            var row = await db.instancePublicApiSettings
                .FirstOrDefaultAsync(x => x.Id == InstancePublicApiSettings.SingletonId, ct);

            if (row is null)
            {
                row = new InstancePublicApiSettings
                {
                    Id = InstancePublicApiSettings.SingletonId,
                    publicApiEnabled = FeatureFlags.IsPublicApiEnabled(),
                };
                db.instancePublicApiSettings.Add(row);
            }

            if (request.publicApiEnabled is bool enabled)
                row.publicApiEnabled = enabled;

            row.updatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            _cache = ToDto(row);
            _initialized = true;

            if (!row.publicApiEnabled)
            {
                await _loggingSettings.UpdateAsync(
                    new UpdateLoggingSettingsRequest { logApiRequestsEnabled = false },
                    ct);
            }

            return _cache;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static PublicApiSettingsDto ToDto(InstancePublicApiSettings row) => new(
        row.publicApiEnabled,
        row.updatedAt);
}
