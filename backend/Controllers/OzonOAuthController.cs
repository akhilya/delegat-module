using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Configuration;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Security;
using Delegat.Models.Marketplace;
using Delegat.Services;
using Delegat.Services.Marketplace;
using Delegat.Services.PlatformLink;

namespace Delegat.Controllers;

/// <summary>
/// Ozon App Store OAuth flow on the instance. The authorize redirect lands on the
/// Control Plane (single registered redirect_uri); the CP bounces the browser
/// here with a one-time ticket that is redeemed over the Platform Link channel.
/// </summary>
[ApiController]
[JwtAuthorize]
public class OzonOAuthController : ControllerBase
{
    private readonly IPlatformLinkClient _platformLink;
    private readonly PlatformLinkOptions _platformLinkOptions;
    private readonly ICurrentUserService _currentUserService;
    private readonly DataContext _db;
    private readonly EncryptionService? _encryption;
    private readonly ILogger<OzonOAuthController> _logger;

    public OzonOAuthController(
        IPlatformLinkClient platformLink,
        PlatformLinkOptions platformLinkOptions,
        ICurrentUserService currentUserService,
        DataContext db,
        ILogger<OzonOAuthController> logger,
        EncryptionService? encryption = null)
    {
        _platformLink = platformLink;
        _platformLinkOptions = platformLinkOptions;
        _currentUserService = currentUserService;
        _db = db;
        _logger = logger;
        _encryption = encryption;
    }

    /// <summary>
    /// Returns the Control Plane URL the SPA should navigate to in order to
    /// start the Ozon consent screen.
    /// </summary>
    [HttpGet]
    [Route("/marketplace/ozon/oauth/start")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> Start()
    {
        if (!_platformLink.IsEnabled || string.IsNullOrEmpty(_platformLinkOptions.InstanceId))
            throw ApiException.BadRequest(code: "platform_link_not_configured");

        var storeId = await _currentUserService.GetSelectedStoreIdAsync();
        if (storeId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var url = $"{_platformLinkOptions.ControlPlaneUrl}/api/ozon/oauth/start" +
                  $"?instance={Uri.EscapeDataString(_platformLinkOptions.InstanceId)}" +
                  $"&store={storeId.Value}";

        return Ok(new { url });
    }

    /// <summary>
    /// Landing URL after the Ozon consent screen (redirected here by the CP
    /// callback). Redeems the ticket, upserts the ozon connection credentials
    /// and returns the browser to the marketplace page.
    /// </summary>
    [HttpGet]
    [Route("/marketplace/ozon/oauth/complete")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> Complete(
        [FromQuery] string? ticket,
        [FromQuery] string? error,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(error))
            return Redirect($"/adapter?ozonError={Uri.EscapeDataString(error)}");
        if (string.IsNullOrWhiteSpace(ticket))
            return Redirect("/adapter?ozonError=missing_ticket");

        var grant = await _platformLink.ClaimOzonGrantAsync(ticket, ct);
        if (grant is null || string.IsNullOrEmpty(grant.AccessToken))
            return Redirect("/adapter?ozonError=claim_failed");

        var storeId = await _currentUserService.GetSelectedStoreIdAsync() ?? grant.StoreHint;
        if (storeId is null || !await _db.stores.AnyAsync(s => s.Id == storeId.Value, ct))
            return Redirect("/adapter?ozonError=store_not_found");

        var credentials = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["authType"] = "oauth",
            ["accessToken"] = grant.AccessToken,
            ["refreshToken"] = grant.RefreshToken ?? "",
            ["accessTokenExpiresAt"] = grant.AccessTokenExpiresAt.ToString("O"),
            ["sellerId"] = grant.SellerId ?? "",
            ["scope"] = grant.Scope ?? "",
        };
        var credsJson = _encryption is { } enc
            ? enc.Encrypt(JsonSerializer.Serialize(credentials))
            : JsonSerializer.Serialize(credentials);

        var connection = await FindOauthConnectionAsync(storeId.Value, ct);
        if (connection is null)
        {
            connection = new MarketplaceConnection();
            connection.AddFill(new MarketplaceConnection.AddForm
            {
                name = string.IsNullOrEmpty(grant.SellerId) ? "Ozon" : $"Ozon · {grant.SellerId}",
                adapterKey = "ozon",
                credentialsJson = credsJson,
                settingsJson = null,
                isActive = true,
                storeId = storeId.Value,
            });
            _db.marketplaceConnections.Add(connection);
        }
        else
        {
            connection.credentialsJson = credsJson;
            connection.isActive = true;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Ozon OAuth grant claimed for store {StoreId}, connection {ConnectionId}",
            storeId.Value, connection.Id);

        return Redirect($"/adapter/{connection.Id}?ozonConnected=1");
    }

    /// <summary>
    /// Existing ozon connection on this store whose credentials carry
    /// <c>authType: oauth</c>; encrypted values are decrypted one by one.
    /// </summary>
    private async Task<MarketplaceConnection?> FindOauthConnectionAsync(int storeId, CancellationToken ct)
    {
        var candidates = await _db.marketplaceConnections
            .Where(c => c.storeId == storeId && c.adapterKey == "ozon" && c.credentialsJson != null)
            .ToListAsync(ct);

        foreach (var candidate in candidates)
        {
            var plain = ConnectionCredentialsHelper.DecryptCredentialsJson(candidate.credentialsJson, _encryption);
            var creds = ConnectionCredentialsHelper.ParseCredentialsObject(plain);
            if (creds.TryGetValue("authType", out var authType)
                && string.Equals(authType, "oauth", StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }
}
