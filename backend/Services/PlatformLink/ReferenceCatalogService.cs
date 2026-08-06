using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Configuration;
using Delegat.Models;
using Delegat.Services.Plugins;

namespace Delegat.Services.PlatformLink;

public interface IReferenceCatalogService
{
    Task<IReadOnlyList<object>> GetCurrenciesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<object>> GetTimezonesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<object>> GetPluginsAsync(CancellationToken ct = default);
    Task<ReferenceCatalogStatusDto> GetStatusAsync(CancellationToken ct = default);
    Task SyncFromControlPlaneAsync(CancellationToken ct = default);
}

public sealed class ReferenceCatalogService : IReferenceCatalogService
{
    private readonly DataContext _db;
    private readonly IPlatformLinkClient _client;
    private readonly PlatformLinkOptions _options;
    private readonly PluginManager _pluginManager;
    private readonly ILogger<ReferenceCatalogService> _logger;

    public ReferenceCatalogService(
        DataContext db,
        IPlatformLinkClient client,
        PlatformLinkOptions options,
        PluginManager pluginManager,
        ILogger<ReferenceCatalogService> logger)
    {
        _db = db;
        _client = client;
        _options = options;
        _pluginManager = pluginManager;
        _logger = logger;
    }

    public async Task<IReadOnlyList<object>> GetCurrenciesAsync(CancellationToken ct = default)
    {
        var rows = await _db.referenceCurrencies
            .AsNoTracking()
            .OrderBy(c => c.code)
            .ToListAsync(ct);

        return rows.Select(c => (object)new
        {
            code = c.code,
            label = c.label,
            symbol = c.symbol,
        }).ToList();
    }

    public async Task<IReadOnlyList<object>> GetTimezonesAsync(CancellationToken ct = default)
    {
        var rows = await _db.referenceTimezones
            .AsNoTracking()
            .OrderBy(t => t.id)
            .ToListAsync(ct);

        return rows.Select(t => (object)new
        {
            id = t.id,
            value = t.id,
            label = t.label,
        }).ToList();
    }

    public async Task<IReadOnlyList<object>> GetPluginsAsync(CancellationToken ct = default)
    {
        var rows = await _db.referencePlugins
            .AsNoTracking()
            .OrderBy(p => p.displayName)
            .ToListAsync(ct);

        var installed = _pluginManager.GetAllLoadedPlugins()
            .ToDictionary(p => p.Key, p => p.Version, StringComparer.OrdinalIgnoreCase);

        return rows.Select(p =>
        {
            installed.TryGetValue(p.key, out var installedVersion);
            return (object)new
            {
                key = p.key,
                displayName = p.displayName,
                latestVersion = p.latestVersion,
                minPlatformVersion = p.minPlatformVersion,
                artifactUrl = p.artifactUrl,
                changelog = p.changelog,
                installed = installedVersion != null,
                installedVersion,
            };
        }).ToList();
    }

    public async Task<ReferenceCatalogStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        var currenciesCount = await _db.referenceCurrencies.CountAsync(ct);
        var timezonesCount = await _db.referenceTimezones.CountAsync(ct);
        var pluginsCount = await _db.referencePlugins.CountAsync(ct);

        DateTime? lastSyncedAt = null;
        var stamps = new List<DateTime>();
        if (currenciesCount > 0)
            stamps.Add(await _db.referenceCurrencies.MaxAsync(x => x.syncedAt, ct));
        if (timezonesCount > 0)
            stamps.Add(await _db.referenceTimezones.MaxAsync(x => x.syncedAt, ct));
        if (pluginsCount > 0)
            stamps.Add(await _db.referencePlugins.MaxAsync(x => x.syncedAt, ct));
        if (stamps.Count > 0)
            lastSyncedAt = stamps.Max();

        return new ReferenceCatalogStatusDto
        {
            Linked = _options.IsEnabled,
            LastSyncedAt = lastSyncedAt,
            CurrenciesCount = currenciesCount,
            TimezonesCount = timezonesCount,
            PluginsCount = pluginsCount,
            Ready = currenciesCount > 0 && timezonesCount > 0,
        };
    }

    public async Task SyncFromControlPlaneAsync(CancellationToken ct = default)
    {
        if (!_client.IsEnabled)
        {
            _logger.LogDebug("Platform Link disabled; skipping reference catalog sync");
            return;
        }

        var now = DateTime.UtcNow;

        try
        {
            var currencies = await _client.GetCurrenciesAsync(ct);
            await UpsertCurrenciesAsync(currencies, now, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to sync reference currencies from Control Plane");
        }

        try
        {
            var timezones = await _client.GetTimezonesAsync(ct);
            await UpsertTimezonesAsync(timezones, now, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to sync reference timezones from Control Plane");
        }

        // CP no longer publishes a plugin catalog — drop any stale local rows.
        var stalePlugins = await _db.referencePlugins.ToListAsync(ct);
        if (stalePlugins.Count > 0)
        {
            _db.referencePlugins.RemoveRange(stalePlugins);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Cleared {Count} stale reference plugin rows", stalePlugins.Count);
        }
    }

    private async Task UpsertCurrenciesAsync(
        IReadOnlyList<PlatformCurrencyDto> items,
        DateTime now,
        CancellationToken ct)
    {
        var existing = await _db.referenceCurrencies.ToDictionaryAsync(c => c.code, StringComparer.OrdinalIgnoreCase, ct);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var code = item.Code?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(code) || code.Length > ReferenceCurrency.CODE_MAX_LENGTH)
                continue;
            seen.Add(code);

            if (existing.TryGetValue(code, out var row))
            {
                row.label = Truncate(item.Label, ReferenceCurrency.LABEL_MAX_LENGTH) ?? code;
                row.symbol = Truncate(item.Symbol, ReferenceCurrency.SYMBOL_MAX_LENGTH);
                row.syncedAt = now;
            }
            else
            {
                _db.referenceCurrencies.Add(new ReferenceCurrency
                {
                    code = code,
                    label = Truncate(item.Label, ReferenceCurrency.LABEL_MAX_LENGTH) ?? code,
                    symbol = Truncate(item.Symbol, ReferenceCurrency.SYMBOL_MAX_LENGTH),
                    syncedAt = now,
                });
            }
        }

        var stale = existing.Values.Where(r => !seen.Contains(r.code)).ToList();
        if (stale.Count > 0)
            _db.referenceCurrencies.RemoveRange(stale);

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Synced {Count} reference currencies from Control Plane", seen.Count);
    }

    private async Task UpsertTimezonesAsync(
        IReadOnlyList<PlatformTimezoneDto> items,
        DateTime now,
        CancellationToken ct)
    {
        var existing = await _db.referenceTimezones.ToDictionaryAsync(t => t.id, StringComparer.Ordinal, ct);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            var id = item.Id?.Trim();
            if (string.IsNullOrEmpty(id) || id.Length > ReferenceTimezone.ID_MAX_LENGTH)
                continue;
            seen.Add(id);

            if (existing.TryGetValue(id, out var row))
            {
                row.label = Truncate(item.Label, ReferenceTimezone.LABEL_MAX_LENGTH) ?? id;
                row.syncedAt = now;
            }
            else
            {
                _db.referenceTimezones.Add(new ReferenceTimezone
                {
                    id = id,
                    label = Truncate(item.Label, ReferenceTimezone.LABEL_MAX_LENGTH) ?? id,
                    syncedAt = now,
                });
            }
        }

        var stale = existing.Values.Where(r => !seen.Contains(r.id)).ToList();
        if (stale.Count > 0)
            _db.referenceTimezones.RemoveRange(stale);

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Synced {Count} reference timezones from Control Plane", seen.Count);
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        return v.Length <= max ? v : v[..max];
    }
}
