using System.Collections.Concurrent;
using System.Text.Json;
using Delegat.Infrastructure.Configuration;
using Delegat.Infrastructure.Security;
using Delegat.Services.Marketplace;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services.PlatformLink;

/// <summary>
/// Rotates Ozon App Store OAuth tokens before they expire. Ozon access tokens
/// live ~1h and refresh tokens are single-use, so the rotation goes through the
/// Control Plane proxy (which holds the app client secret) and the fresh pair
/// is persisted atomically back into the connection credentials.
/// </summary>
public sealed class OzonOAuthRefreshHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RefreshBefore = TimeSpan.FromMinutes(15);
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> Gates = new();

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PlatformLinkOptions _options;
    private readonly ILogger<OzonOAuthRefreshHostedService> _logger;

    public OzonOAuthRefreshHostedService(
        IServiceScopeFactory scopeFactory,
        PlatformLinkOptions options,
        ILogger<OzonOAuthRefreshHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.IsEnabled)
            return;

        try { await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshDueTokensAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ozon OAuth token refresh sweep failed");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task RefreshDueTokensAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();
        var encryption = scope.ServiceProvider.GetService<EncryptionService>();
        var platformLink = scope.ServiceProvider.GetRequiredService<IPlatformLinkClient>();

        var candidates = await db.marketplaceConnections
            .Where(c => c.adapterKey == "ozon" && c.credentialsJson != null)
            .ToListAsync(ct);

        foreach (var connection in candidates)
        {
            var creds = ReadOauthCredentials(connection, encryption);
            if (creds is null)
                continue;

            var refreshToken = creds.GetValueOrDefault("refreshToken");
            if (string.IsNullOrEmpty(refreshToken))
                continue;

            var expiresAt = DateTimeOffset.TryParse(
                creds.GetValueOrDefault("accessTokenExpiresAt"), out var parsed)
                ? parsed
                : DateTimeOffset.MinValue;

            if (expiresAt > DateTimeOffset.UtcNow + RefreshBefore)
                continue;

            var gate = Gates.GetOrAdd(connection.Id, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct);
            try
            {
                await RefreshConnectionAsync(db, platformLink, encryption, connection.Id, ct);
            }
            finally
            {
                gate.Release();
            }
        }
    }

    /// <summary>
    /// Refreshes one connection's token pair; called under the per-connection gate.
    /// A public <see cref="TryRefreshNowAsync"/> shares it so other paths (e.g. a
    /// 403 from Ozon) can force a rotation without waiting for the sweep.
    /// </summary>
    private async Task RefreshConnectionAsync(
        DataContext db,
        IPlatformLinkClient platformLink,
        EncryptionService? encryption,
        int connectionId,
        CancellationToken ct)
    {
        // Re-read under the lock: a concurrent refresh may have already rotated.
        var connection = await db.marketplaceConnections
            .FirstOrDefaultAsync(c => c.Id == connectionId, ct);
        if (connection is null)
            return;

        var creds = ReadOauthCredentials(connection, encryption);
        if (creds is null)
            return;

        var refreshToken = creds.GetValueOrDefault("refreshToken");
        if (string.IsNullOrEmpty(refreshToken))
            return;

        var expiresAt = DateTimeOffset.TryParse(
            creds.GetValueOrDefault("accessTokenExpiresAt"), out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
        if (expiresAt > DateTimeOffset.UtcNow + RefreshBefore)
            return;

        var result = await platformLink.RefreshOzonTokenAsync(refreshToken, ct);
        if (!result.Success)
        {
            _logger.LogWarning(
                "Ozon OAuth refresh failed for connection {ConnectionId}: {Error}",
                connectionId, result.Error);

            if (result.Error is "invalid_grant" or "invalid_refresh_token" or "invalid_client")
                FlagReconnectRequired(db, connection);

            await db.SaveChangesAsync(ct);
            return;
        }

        creds["accessToken"] = result.AccessToken!;
        creds["refreshToken"] = string.IsNullOrEmpty(result.RefreshToken)
            ? refreshToken
            : result.RefreshToken;
        creds["accessTokenExpiresAt"] = DateTimeOffset.UtcNow
            .AddSeconds(result.ExpiresIn > 0 ? result.ExpiresIn : 3600)
            .ToString("O");

        connection.credentialsJson = encryption is { } enc
            ? enc.Encrypt(JsonSerializer.Serialize(creds))
            : JsonSerializer.Serialize(creds);

        ClearReconnectFlag(db, connection);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Ozon OAuth tokens rotated for connection {ConnectionId}", connectionId);
    }

    /// <summary>
    /// Forces an immediate rotation for one connection (e.g. after Ozon answered 403).
    /// </summary>
    public async Task TryRefreshNowAsync(int connectionId, CancellationToken ct = default)
    {
        var gate = Gates.GetOrAdd(connectionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();
            var encryption = scope.ServiceProvider.GetService<EncryptionService>();
            var platformLink = scope.ServiceProvider.GetRequiredService<IPlatformLinkClient>();
            await RefreshConnectionAsync(db, platformLink, encryption, connectionId, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private static Dictionary<string, string>? ReadOauthCredentials(
        Models.Marketplace.MarketplaceConnection connection,
        EncryptionService? encryption)
    {
        var plain = ConnectionCredentialsHelper.DecryptCredentialsJson(
            connection.credentialsJson, encryption);
        var creds = ConnectionCredentialsHelper.ParseCredentialsObject(plain);

        return creds.TryGetValue("authType", out var authType)
            && string.Equals(authType, "oauth", StringComparison.OrdinalIgnoreCase)
            ? creds
            : null;
    }

    private static void FlagReconnectRequired(DataContext db, Models.Marketplace.MarketplaceConnection connection)
    {
        var settings = ParseSettings(connection.settingsJson);
        settings["oauthReconnectRequired"] = true;
        connection.settingsJson = JsonSerializer.Serialize(settings);
    }

    private static void ClearReconnectFlag(DataContext db, Models.Marketplace.MarketplaceConnection connection)
    {
        var settings = ParseSettings(connection.settingsJson);
        if (settings.Remove("oauthReconnectRequired"))
            connection.settingsJson = JsonSerializer.Serialize(settings);
    }

    private static Dictionary<string, object?> ParseSettings(string? settingsJson)
    {
        try
        {
            return string.IsNullOrWhiteSpace(settingsJson)
                ? new Dictionary<string, object?>()
                : JsonSerializer.Deserialize<Dictionary<string, object?>>(settingsJson)
                    ?? new Dictionary<string, object?>();
        }
        catch
        {
            return new Dictionary<string, object?>();
        }
    }
}
