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
    private const string TxCookieName = "ozon_oauth_tx";
    private const string TxCookiePath = "/api/marketplace/ozon/oauth";
    private static readonly TimeSpan TxLifetime = TimeSpan.FromMinutes(15);

    private readonly IPlatformLinkClient _platformLink;
    private readonly PlatformLinkOptions _platformLinkOptions;
    private readonly ICurrentUserService _currentUserService;
    private readonly QuotaService _quota;
    private readonly DataContext _db;
    private readonly EncryptionService? _encryption;
    private readonly ILogger<OzonOAuthController> _logger;

    public OzonOAuthController(
        IPlatformLinkClient platformLink,
        PlatformLinkOptions platformLinkOptions,
        ICurrentUserService currentUserService,
        QuotaService quota,
        DataContext db,
        ILogger<OzonOAuthController> logger,
        EncryptionService? encryption = null)
    {
        _platformLink = platformLink;
        _platformLinkOptions = platformLinkOptions;
        _currentUserService = currentUserService;
        _quota = quota;
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

        // Browser-bound transaction: Complete only accepts a ticket when this
        // cookie is present — it proves the browser actually started the flow
        // here, and binds the completion to the store the user picked.
        var nonce = OzonOAuthTransaction.CreateNonce();
        Response.Cookies.Append(TxCookieName, $"{storeId}.{nonce}", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = TxLifetime,
            Path = TxCookiePath,
        });

        var url = $"{_platformLinkOptions.ControlPlaneUrl}/api/ozon/oauth/start" +
                  $"?instance={Uri.EscapeDataString(_platformLinkOptions.InstanceId)}" +
                  $"&store={storeId}" +
                  $"&nonce={Uri.EscapeDataString(nonce)}";

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
        [FromQuery] string? nonce,
        [FromQuery] string? error,
        CancellationToken ct)
    {
        // GET is downgraded to workspace.view by the permission handler, but this
        // endpoint writes credentials — enforce the manage claim explicitly
        // (same bypass as PermissionAuthorizationHandler: admins / store creators).
        var canManage = User.HasClaim(Permissions.ClaimType, Permissions.MarketplaceManage)
            || User.HasClaim("is_admin", "true")
            || User.HasClaim(Permissions.ClaimType, Permissions.StoresCreate);
        if (!canManage)
            return Redirect("/adapter?ozonError=forbidden");

        if (!string.IsNullOrWhiteSpace(error))
            return Redirect($"/adapter?ozonError={Uri.EscapeDataString(error)}");
        if (string.IsNullOrWhiteSpace(ticket))
            return Redirect("/adapter?ozonError=missing_ticket");

        // A ticket is only redeemed in a browser that ran Start on this instance
        // (transaction cookie is Lax, so the top-level CP redirect keeps it).
        if (!Request.Cookies.TryGetValue(TxCookieName, out var tx)
            || !OzonOAuthTransaction.TryParseCookie(tx, nonce, out var txStoreId))
        {
            return Redirect("/adapter?ozonError=missing_oauth_transaction");
        }
        Response.Cookies.Delete(TxCookieName, new CookieOptions { Path = TxCookiePath });

        // The bound store must be the selected one — the permission handler
        // checked membership against selected_store_id, so accepting a forged
        // tx cookie pointing at another store would write credentials there.
        // GetSelectedStoreIdAsync also re-verifies membership, closing the gap
        // when selected_store_id itself is forged to a non-member store.
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (txStoreId != selectedStoreId)
            return Redirect("/adapter?ozonError=store_mismatch");

        var grant = await _platformLink.ClaimOzonGrantAsync(ticket, ct);
        if (grant is null || string.IsNullOrEmpty(grant.AccessToken))
            return Redirect("/adapter?ozonError=claim_failed");

        var storeId = txStoreId;
        if (!await _db.stores.AnyAsync(s => s.Id == storeId, ct))
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

        var connection = await FindOauthConnectionAsync(storeId, grant.SellerId, ct);
        if (connection is null)
        {
            try
            {
                await _quota.EnsureConnectionQuotaAsync(storeId);
            }
            catch (ApiException)
            {
                return Redirect("/adapter?ozonError=quota_connections_reached");
            }

            connection = new MarketplaceConnection();
            connection.AddFill(new MarketplaceConnection.AddForm
            {
                name = string.IsNullOrEmpty(grant.SellerId) ? "Ozon" : $"Ozon · {grant.SellerId}",
                adapterKey = "ozon",
                credentialsJson = credsJson,
                settingsJson = null,
                isActive = true,
                storeId = storeId,
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
            storeId, connection.Id);

        return Redirect($"/adapter/{connection.Id}?ozonConnected=1");
    }

    /// <summary>
    /// Existing ozon connection on this store whose credentials carry
    /// <c>authType: oauth</c> and — when the grant names one — the same seller.
    /// A different seller must not overwrite the first connection (its mappings
    /// and product links belong to that seller).
    /// </summary>
    private async Task<MarketplaceConnection?> FindOauthConnectionAsync(
        int storeId, string? sellerId, CancellationToken ct)
    {
        var candidates = await _db.marketplaceConnections
            .Where(c => c.storeId == storeId && c.adapterKey == "ozon" && c.credentialsJson != null)
            .ToListAsync(ct);

        foreach (var candidate in candidates)
        {
            var plain = ConnectionCredentialsHelper.DecryptCredentialsJson(candidate.credentialsJson, _encryption);
            var creds = ConnectionCredentialsHelper.ParseCredentialsObject(plain);
            if (!creds.TryGetValue("authType", out var authType)
                || !string.Equals(authType, "oauth", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            creds.TryGetValue("sellerId", out var existingSeller);
            if (!string.IsNullOrEmpty(sellerId)
                && !string.Equals(existingSeller, sellerId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return candidate;
        }

        return null;
    }
}
