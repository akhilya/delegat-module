using System.Text.Json;
using Delegat.Infrastructure.Security;
using Delegat.Marketplace.SDK;
using Delegat.Models.Marketplace;
using Delegat.Services.Marketplace;
using Delegat.Services.Plugins;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services.Integration;

/// <summary>
/// Production <see cref="IIntegrationExecutionBindingResolver"/> that resolves
/// a command's active store-scoped <see cref="MarketplaceConnection"/>, decrypts
/// its credentials, locates the matching <see cref="IMarketplaceExecutionProvider"/>,
/// and constructs the approved <see cref="IntegrationHostRouteBinding"/>.
/// </summary>
internal sealed class HostIntegrationExecutionBindingResolver(
    DataContext db,
    PluginManager plugins,
    EncryptionService? encryption = null) : IIntegrationExecutionBindingResolver
{
    public async Task<IntegrationExecutionBinding?> ResolveAsync(DispatchStageRequest request, CancellationToken ct)
    {
        var command = await db.integrationCommands
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CommandId, ct);
        if (command?.connectionId is not int connectionId)
            return null;

        var connection = await db.marketplaceConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == connectionId && c.storeId == command.storeId && c.isActive, ct);
        if (connection is null)
            return null;

        if (plugins.GetPlugin(connection.adapterKey) is not IMarketplaceExecutionProvider provider)
            return null;

        var plain = ConnectionCredentialsHelper.DecryptCredentialsJson(connection.credentialsJson, encryption);
        var creds = ConnectionCredentialsHelper.ParseCredentialsObject(plain);
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(connection.settingsJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(connection.settingsJson);
                if (parsed != null)
                {
                    foreach (var (k, v) in parsed)
                        settings[k] = v.ToString();
                }
            }
            catch (JsonException) { }
        }

        if (!TryBuildRouteBinding(connection, creds, out var routeBinding) || routeBinding is null)
            return null;

        var connCtx = new ConnectionContext
        {
            ConnectionId = connection.Id,
            StoreId = connection.storeId,
            AdapterKey = connection.adapterKey,
            Credentials = creds,
            Settings = settings
        };

        return new IntegrationExecutionBinding(provider, connCtx, routeBinding);
    }

    internal static bool TryBuildRouteBinding(
        MarketplaceConnection connection,
        IReadOnlyDictionary<string, string> creds,
        out IntegrationHostRouteBinding? binding)
    {
        binding = null;
        var key = (connection.adapterKey ?? "").Trim().ToLowerInvariant();
        var isSandbox = creds.TryGetValue("sandbox", out var sb) &&
            string.Equals(sb?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        var env = isSandbox
            ? IntegrationProviderEnvironment.Sandbox
            : IntegrationProviderEnvironment.Production;

        switch (key)
        {
            case "ozon":
            {
                creds.TryGetValue("clientId", out var clientId);
                binding = new IntegrationHostRouteBinding(
                    IntegrationProviderKind.Ozon, env, new Uri("https://api-seller.ozon.ru"),
                    VerifiedSellerId: string.IsNullOrWhiteSpace(clientId) ? $"conn:{connection.Id}" : clientId.Trim());
                return true;
            }
            case "wildberries":
            {
                var host = isSandbox
                    ? new Uri("https://marketplace-api-sandbox.wildberries.ru")
                    : new Uri("https://marketplace-api.wildberries.ru");
                binding = new IntegrationHostRouteBinding(
                    IntegrationProviderKind.Wildberries, env, host,
                    VerifiedSellerId: $"conn:{connection.Id}");
                return true;
            }
            case "yandex" or "yandex-market":
            {
                creds.TryGetValue("businessId", out var businessId);
                binding = new IntegrationHostRouteBinding(
                    IntegrationProviderKind.YandexMarket, env, new Uri("https://api.partner.market.yandex.ru"),
                    VerifiedSellerId: string.IsNullOrWhiteSpace(businessId) ? $"conn:{connection.Id}" : businessId.Trim());
                return true;
            }
            case "avito":
            {
                creds.TryGetValue("clientId", out var clientId);
                binding = new IntegrationHostRouteBinding(
                    IntegrationProviderKind.Avito, env, new Uri("https://api.avito.ru"),
                    VerifiedSellerId: string.IsNullOrWhiteSpace(clientId) ? $"conn:{connection.Id}" : clientId.Trim());
                return true;
            }
            case "opencart":
            {
                var rawUrl = FirstNonEmpty(creds, "apiBaseUrl", "siteUrl");
                if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
                    return false;
                binding = new IntegrationHostRouteBinding(
                    IntegrationProviderKind.OpenCart, env, uri,
                    VerifiedSellerId: $"conn:{connection.Id}");
                return true;
            }
            case "woocommerce":
            {
                var rawUrl = FirstNonEmpty(creds, "siteUrl", "apiBaseUrl");
                if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
                    return false;
                binding = new IntegrationHostRouteBinding(
                    IntegrationProviderKind.WooCommerce, env, uri,
                    VerifiedSellerId: $"conn:{connection.Id}");
                return true;
            }
            case "medusa":
            {
                var rawUrl = FirstNonEmpty(creds, "apiBaseUrl", "siteUrl");
                if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
                    return false;
                binding = new IntegrationHostRouteBinding(
                    IntegrationProviderKind.Medusa, env, uri,
                    VerifiedSellerId: $"conn:{connection.Id}");
                return true;
            }
            default:
                return false;
        }
    }

    private static string? FirstNonEmpty(IReadOnlyDictionary<string, string> creds, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (creds.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }
        return null;
    }
}
