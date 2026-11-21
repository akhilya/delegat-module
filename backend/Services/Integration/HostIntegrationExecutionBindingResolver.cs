using System.Security.Cryptography;
using System.Text;
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
/// verifies the persisted non-secret seller/environment binding fingerprint, and
/// constructs the approved <see cref="IntegrationHostRouteBinding"/>.
/// </summary>
internal sealed class HostIntegrationExecutionBindingResolver(
    DataContext db,
    PluginManager plugins,
    EncryptionService? encryption = null,
    IMediaBlobReader? mediaBlobReader = null) : IIntegrationExecutionBindingResolver
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

        // Verify that the connection's seller/environment identity has not been replaced since admission.
        var expectedFingerprint = ExtractBindingFingerprint(command.inputJson);
        if (!string.IsNullOrWhiteSpace(expectedFingerprint))
        {
            var currentFingerprint = ComputeBindingFingerprint(connection, creds, routeBinding);
            if (!string.Equals(expectedFingerprint, currentFingerprint, StringComparison.Ordinal))
                return null;
        }

        var connCtx = new ConnectionContext
        {
            ConnectionId = connection.Id,
            StoreId = connection.storeId,
            AdapterKey = connection.adapterKey,
            Credentials = creds,
            Settings = settings,
            MediaBlobReader = mediaBlobReader
        };

        return new IntegrationExecutionBinding(provider, connCtx, routeBinding);
    }

    internal static string ComputeBindingFingerprint(
        MarketplaceConnection connection,
        IReadOnlyDictionary<string, string> creds,
        IntegrationHostRouteBinding routeBinding)
    {
        // Use verified non-secret seller identity when available; otherwise fall back to a
        // SHA-256 hash of credential identity fields so replacing seller credentials on an
        // existing connection ID cannot silently retarget queued commands.
        var sellerIdentity = routeBinding.VerifiedSellerId;
        if (string.IsNullOrWhiteSpace(sellerIdentity) || sellerIdentity.StartsWith("conn:", StringComparison.Ordinal))
        {
            var identitySource = $"{routeBinding.Provider}|{routeBinding.Environment}|{routeBinding.AllowedOrigin.GetLeftPart(UriPartial.Authority)}|{FirstNonEmpty(creds, "apiKey", "clientSecret", "accessToken") ?? ""}";
            sellerIdentity = "cred:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identitySource))).ToLowerInvariant();
        }

        var canonical = $"{routeBinding.Provider}|{routeBinding.Environment}|{routeBinding.AllowedOrigin.GetLeftPart(UriPartial.Authority)}|{sellerIdentity}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    internal static string? ExtractBindingFingerprint(string? inputJson)
    {
        if (string.IsNullOrWhiteSpace(inputJson))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(inputJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("bindingFingerprint", out var el) &&
                el.ValueKind == JsonValueKind.String)
            {
                return el.GetString();
            }
        }
        catch (JsonException) { }
        return null;
    }

    internal static bool TryBuildRouteBinding(
        MarketplaceConnection connection,
        IReadOnlyDictionary<string, string> creds,
        out IntegrationHostRouteBinding? binding)
    {
        binding = null;
        var key = (connection.adapterKey ?? "").Trim().ToLowerInvariant();
        var env = ResolveEnvironment(key, creds);

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
                var host = env == IntegrationProviderEnvironment.Sandbox
                    ? new Uri("https://marketplace-api-sandbox.wildberries.ru")
                    : new Uri("https://marketplace-api.wildberries.ru");
                var sid = TryExtractWildberriesSellerId(FirstNonEmpty(creds, "apiKey", "token"));
                binding = new IntegrationHostRouteBinding(
                    IntegrationProviderKind.Wildberries, env, host,
                    VerifiedSellerId: string.IsNullOrWhiteSpace(sid) ? $"conn:{connection.Id}" : sid);
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
                if (!TryParseHttpsUri(rawUrl, out var uri))
                    return false;
                binding = new IntegrationHostRouteBinding(
                    IntegrationProviderKind.OpenCart, env, uri!,
                    VerifiedSellerId: $"conn:{connection.Id}");
                return true;
            }
            case "woocommerce":
            {
                var rawUrl = FirstNonEmpty(creds, "siteUrl", "apiBaseUrl");
                if (!TryParseHttpsUri(rawUrl, out var uri))
                    return false;
                binding = new IntegrationHostRouteBinding(
                    IntegrationProviderKind.WooCommerce, env, uri!,
                    VerifiedSellerId: $"conn:{connection.Id}");
                return true;
            }
            case "medusa":
            {
                var rawUrl = FirstNonEmpty(creds, "apiBaseUrl", "siteUrl");
                if (!TryParseHttpsUri(rawUrl, out var uri))
                    return false;
                binding = new IntegrationHostRouteBinding(
                    IntegrationProviderKind.Medusa, env, uri!,
                    VerifiedSellerId: $"conn:{connection.Id}");
                return true;
            }
            default:
                return false;
        }
    }

    internal static IntegrationProviderEnvironment ResolveEnvironment(
        string normalizedAdapterKey,
        IReadOnlyDictionary<string, string> creds)
    {
        var explicitEnv = FirstNonEmpty(creds, "environment", "env")?.ToLowerInvariant();
        if (explicitEnv == "sandbox")
            return IntegrationProviderEnvironment.Sandbox;
        if (explicitEnv == "production" || explicitEnv == "prod")
            return IntegrationProviderEnvironment.Production;

        var sandboxFlag = FirstNonEmpty(creds, "sandbox")?.ToLowerInvariant();
        if (sandboxFlag is "true" or "1" or "sandbox")
            return IntegrationProviderEnvironment.Sandbox;
        if (sandboxFlag is "false" or "0" or "production")
            return IntegrationProviderEnvironment.Production;

        // Wildberries supports environment="auto" (or omitted) where sandbox tokens carry "t":true or "s":1 in JWT payload.
        if (normalizedAdapterKey == "wildberries" &&
            IsWildberriesSandboxToken(FirstNonEmpty(creds, "apiKey", "token")))
        {
            return IntegrationProviderEnvironment.Sandbox;
        }

        return IntegrationProviderEnvironment.Production;
    }

    private static bool TryParseHttpsUri(string? rawUrl, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var parsed))
            return false;
        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;
        uri = parsed;
        return true;
    }

    private static bool IsWildberriesSandboxToken(string? token)
    {
        if (!TryDecodeJwtPayload(token, out var payload))
            return false;
        using (payload)
        {
            var root = payload!.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;
            if (root.TryGetProperty("t", out var tEl) && tEl.ValueKind == JsonValueKind.True)
                return true;
            if (root.TryGetProperty("sandbox", out var sbEl) && sbEl.ValueKind == JsonValueKind.True)
                return true;
        }
        return false;
    }

    private static string? TryExtractWildberriesSellerId(string? token)
    {
        if (!TryDecodeJwtPayload(token, out var payload))
            return null;
        using (payload)
        {
            var root = payload!.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("sid", out var sidEl) &&
                sidEl.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(sidEl.GetString()))
            {
                return sidEl.GetString()!.Trim();
            }
        }
        return null;
    }

    private static bool TryDecodeJwtPayload(string? token, out JsonDocument? doc)
    {
        doc = null;
        if (string.IsNullOrWhiteSpace(token))
            return false;
        var parts = token.Trim().Split('.');
        if (parts.Length < 2)
            return false;
        try
        {
            var padded = parts[1].Replace('-', '+').Replace('_', '/');
            var mod = padded.Length % 4;
            if (mod == 2) padded += "==";
            else if (mod == 3) padded += "=";
            else if (mod != 0) return false;
            var bytes = Convert.FromBase64String(padded);
            doc = JsonDocument.Parse(bytes);
            return true;
        }
        catch
        {
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
