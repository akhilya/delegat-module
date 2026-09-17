using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace Delegat.Services.Ai.Agent;

/// <summary>
/// Denylist for <c>platform_request</c>: the assistant uses the same cabinet JSON API
/// as the SPA. Secrets and a few irreversible actions are denied; everything else is allowed.
/// </summary>
public static class PlatformRequestPolicy
{
    public const string SurfaceDescription = """
        Call the same cabinet JSON API the SPA uses (prefix /api is optional). Permissions,
        store membership, and quotas are those of the signed-in user. GET/HEAD are reads.
        POST/PUT/PATCH/DELETE are writes (propose_plan first).

        Lists are paged: startIndex, pageSize (not a fake stats endpoint). Search products with searchQuery.

        Catalog: GET/POST /products  GET/PATCH/DELETE /products/{id}  POST /products/{id}/restore
          GET /products/trash  GET /products/trash/count  POST /products/bulk
          PATCH /products/{id}/group { groupId } — move the card (null removes). Also
          POST /productgroups/{id}/products { productIds }  DELETE /productgroups/{id}/products/{productId}
          POST /products/{id}/files { attachmentIds, folderId? } — staged chat files onto a product
          GET /products/{id}/files  PUT /products/{id}/files is multipart (Agent HTTP, not JSON)
          GET/POST /categories  GET/PATCH /categories/{id}
          GET/POST /categories/{id}/attributes  PATCH /categories/{id}/attributes/{attributeId}
          GET/POST/PATCH /store-attributes  GET/POST /productgroups
        PATCH /products/{id} attributeValues replaces the list (same as the product form). GET first.

        Orders: GET /marketplace/orders?tab=  GET /marketplace/connections/{id}/orders
          GET …/orders/{orderId}  POST …/orders/{orderId}/status  GET …/orders/{orderId}/label
          shipments, boxes, promotions, prices on that connection (same as the order sheet)

        Marketplace (no secrets): GET /marketplace/connections  GET …/connections/{id}
          GET …/capabilities  GET …/limits  GET …/mappings
          GET/PATCH …/mappings/{mappingId}/graph
          GET …/external-products  GET …/products/import-preview
          POST …/products/import-selected  POST …/products/import-unlinked
          POST …/products/import-all (legacy; prefer import-unlinked or import-selected)
          POST …/products/export-batch { productIds } — local catalog → this shop (Operations)
          POST …/products/export-batch { allMatching: true, skipWithoutImages?: true } — whole catalog (skip cards with no gallery photos)
          POST …/products/export-bulk { productIds } — same write, waits in-request (small sets only)
          GET /marketplace/export-batches/{batchId}/status
          POST …/match/preview  POST …/match/apply  POST …/match/repair-mappings
          GET /marketplace/catalog-connect/jobs  GET /marketplace/adapters
          POST /tools/{id}/run  catalog-scrape search/apply
        Do not POST/PATCH connection credentials. Do not DELETE a store.
        Do not tell the user to click a cabinet button for a write you can POST.
        Link existing local cards to another marketplace: POST match/preview (optional
        operationId only; host picks fromLocal/fromRemote). Poll jobs. Then POST match/apply
        { items: [{ productId, externalProductId, externalCategoryId?, externalCategoryName? }] }
        copying category fields from the preview row when present. If apply returns
        mappingsCreated 0, POST match/repair-mappings (creates missing category mappings
        and copies characteristics). HTTP 200 with mappingsCreated 0 still means no mapping —
        do not claim characteristics imported. There is no GET …/connections/{id}/graph.
        Further POSTs queue while another Catalog Connect job is running (HTTP 200 pending).
        Do not GET import-preview to link. Do not invent extra catalog-connect URLs
        or OpenAPI.

        Notes, folders, barcodes, operations, storage lists, and reference catalogs use the
        same /api paths as the cabinet. Prefer small pageSize.
        """;

    private static readonly Regex PermanentDelete = new(
        @"/(bulk-)?permanent(-delete)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ConnectionResource = new(
        @"^/api/marketplace/connections(/[0-9]+)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ConnectionTest = new(
        @"^/api/marketplace/connections/[0-9]+/test$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] DeniedPrefixes =
    [
        "/api/v1",
        "/api/mcp",
        "/mcp",
        "/api/agent",
        "/api/ai",
        "/api/auth",
        "/api/users",
        "/api/roles",
        "/api/plugins",
        "/api/api-keys",
        "/api/invitation",
        "/api/invitations",
        "/swagger",
        "/hubs",
        "/api/health",
    ];

    public static bool IsMutating(string method) =>
        !HttpMethods.IsGet(method)
        && !HttpMethods.IsHead(method)
        && !HttpMethods.IsOptions(method);

    public static (string Path, Dictionary<string, string> Query) SplitPathAndQuery(string path)
    {
        var p = (path ?? "").Trim();
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var qIdx = p.IndexOf('?');
        if (qIdx >= 0)
        {
            ParseQuery(p[(qIdx + 1)..], query);
            p = p[..qIdx];
        }

        if (!p.StartsWith('/'))
            p = "/" + p;
        if (p.Length > 1)
            p = p.TrimEnd('/');
        if (!p.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) &&
            !p.Equals("/api", StringComparison.OrdinalIgnoreCase))
            p = "/api" + p;
        return (p, query);
    }

    public static string NormalizePath(string path) => SplitPathAndQuery(path).Path;

    /// <summary>Null when the route may run; otherwise an error code.</summary>
    public static string? DenyReason(string method, string path)
    {
        var apiPath = SplitPathAndQuery(path).Path;
        foreach (var prefix in DeniedPrefixes)
        {
            if (apiPath.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || apiPath.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
                return "route_denied";
        }

        if (apiPath.Equals("/api/settings/database/dump", StringComparison.OrdinalIgnoreCase)
            || apiPath.Equals("/api/settings/quotas/trim", StringComparison.OrdinalIgnoreCase)
            || apiPath.StartsWith("/api/settings/logging", StringComparison.OrdinalIgnoreCase)
            || apiPath.StartsWith("/api/settings/public-api", StringComparison.OrdinalIgnoreCase))
            return "route_denied";

        if (PermanentDelete.IsMatch(apiPath))
            return "route_denied";

        if (HttpMethods.IsDelete(method)
            && Regex.IsMatch(apiPath, @"^/api/stores/[0-9]+$", RegexOptions.IgnoreCase))
            return "route_denied";

        if (IsConnectionSecretWrite(method, apiPath))
            return "route_denied";

        return null;
    }

    private static bool IsConnectionSecretWrite(string method, string apiPath)
    {
        if (!IsMutating(method))
            return false;
        if (ConnectionTest.IsMatch(apiPath))
            return true;
        if (!ConnectionResource.IsMatch(apiPath))
            return false;
        // Create / update credentials. DELETE a connection is allowed (same as the SPA).
        return HttpMethods.IsPost(method) || HttpMethods.IsPatch(method) || HttpMethods.IsPut(method);
    }

    public static bool IsAllowed(string method, string path) => DenyReason(method, path) == null;

    public static object OpenApiSurface() => new
    {
        description = SurfaceDescription,
        deny = new[]
        {
            "/api/v1", "/api/mcp", "/mcp", "/api/agent", "/api/ai", "/api/auth",
            "/api/users", "/api/roles", "/api/plugins", "/api/api-keys",
            "/api/invitation", "/swagger", "/hubs",
            "POST/PATCH marketplace connection credentials and /test",
            "GET/POST /api/settings/database/dump", "POST /api/settings/quotas/trim",
            "DELETE /api/stores/{id}", "permanent product delete",
        },
    };

    private static void ParseQuery(string raw, Dictionary<string, string> into)
    {
        foreach (var part in raw.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            string key;
            string value;
            if (eq < 0)
            {
                key = Uri.UnescapeDataString(part);
                value = "";
            }
            else
            {
                key = Uri.UnescapeDataString(part[..eq]);
                value = Uri.UnescapeDataString(part[(eq + 1)..]);
            }

            if (key.Length > 0)
                into[key] = value;
        }
    }
}
