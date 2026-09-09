using Delegat.Services.Ai.Agent;

namespace Delegat.Tests;

public class PlatformRequestPolicyTests
{
    [Fact]
    public void CabinetCatalogRoutes_AreAllowed()
    {
        Assert.True(PlatformRequestPolicy.IsAllowed("GET", "/products"));
        Assert.True(PlatformRequestPolicy.IsAllowed("GET", "/api/products?categoryId=5"));
        Assert.True(PlatformRequestPolicy.IsAllowed("POST", "/products"));
        Assert.True(PlatformRequestPolicy.IsAllowed("PATCH", "/products/42"));
        Assert.True(PlatformRequestPolicy.IsAllowed("DELETE", "/products/42"));
        Assert.True(PlatformRequestPolicy.IsAllowed("POST", "/products/42/restore"));
        Assert.True(PlatformRequestPolicy.IsAllowed("POST", "/products/12/files"));
        Assert.True(PlatformRequestPolicy.IsAllowed("GET", "/categories/5/attributes"));
        Assert.True(PlatformRequestPolicy.IsAllowed("POST", "/categories/5/attributes"));
        Assert.True(PlatformRequestPolicy.IsAllowed("GET", "/store-attributes"));
        Assert.True(PlatformRequestPolicy.IsAllowed("GET", "/productgroups"));
        Assert.True(PlatformRequestPolicy.IsAllowed("GET", "/marketplace/orders?tab=pack"));
        Assert.True(PlatformRequestPolicy.IsAllowed("GET", "/marketplace/connections/3/mappings"));
        Assert.True(PlatformRequestPolicy.IsAllowed("POST", "/marketplace/connections/3/orders/abc/status"));
        Assert.True(PlatformRequestPolicy.IsAllowed("POST", "/tools/export-all-prices/run"));
        Assert.True(PlatformRequestPolicy.IsAllowed("GET", "/settings/branding"));
        Assert.True(PlatformRequestPolicy.IsAllowed("PATCH", "/settings/branding"));
        Assert.True(PlatformRequestPolicy.IsAllowed("DELETE", "/marketplace/connections/3"));
        Assert.True(PlatformRequestPolicy.IsAllowed(
            "POST", "/marketplace/connections/1/products/import-unlinked"));
        Assert.True(PlatformRequestPolicy.IsAllowed(
            "POST", "/marketplace/connections/1/products/import-selected"));
        Assert.True(PlatformRequestPolicy.IsAllowed(
            "GET", "/marketplace/connections/1/products/import-preview"));
        Assert.True(PlatformRequestPolicy.IsAllowed(
            "GET", "/marketplace/connections/1/external-products"));
        Assert.True(PlatformRequestPolicy.IsAllowed(
            "POST", "/marketplace/connections/1/products/import-all"));
        Assert.True(PlatformRequestPolicy.IsAllowed("GET", "/marketplace/catalog-connect/jobs"));
        Assert.True(PlatformRequestPolicy.IsAllowed(
            "POST", "/marketplace/connections/1/match/preview"));
        Assert.True(PlatformRequestPolicy.IsAllowed(
            "POST", "/marketplace/connections/1/match/apply"));
        Assert.True(PlatformRequestPolicy.IsAllowed(
            "POST", "/marketplace/connections/1/match/repair-mappings"));
        Assert.True(PlatformRequestPolicy.IsAllowed("GET", "/products/trash/count"));
    }

    [Fact]
    public void SecretsAndDangerousRoutes_AreDenied()
    {
        Assert.Equal("route_denied", PlatformRequestPolicy.DenyReason("GET", "/users"));
        Assert.Equal("route_denied", PlatformRequestPolicy.DenyReason("POST", "/plugins/upload"));
        Assert.Equal("route_denied", PlatformRequestPolicy.DenyReason("DELETE", "/ai/models/1"));
        Assert.Equal("route_denied", PlatformRequestPolicy.DenyReason("GET", "/api/auth/me"));
        Assert.Equal("route_denied", PlatformRequestPolicy.DenyReason("GET", "/api/v1/stores/1/products"));
        Assert.Equal("route_denied", PlatformRequestPolicy.DenyReason("POST", "/mcp"));
        Assert.Equal("route_denied", PlatformRequestPolicy.DenyReason("POST", "/agent/stores/1/platform"));
        Assert.Equal("route_denied", PlatformRequestPolicy.DenyReason("GET", "/settings/database/dump"));
        Assert.Equal("route_denied", PlatformRequestPolicy.DenyReason("POST", "/settings/quotas/trim"));
        Assert.Equal("route_denied", PlatformRequestPolicy.DenyReason("DELETE", "/stores/1"));
        Assert.Equal("route_denied", PlatformRequestPolicy.DenyReason("DELETE", "/products/12/permanent"));
        Assert.Equal("route_denied", PlatformRequestPolicy.DenyReason("POST", "/products/bulk-permanent-delete"));
        Assert.Equal("route_denied", PlatformRequestPolicy.DenyReason("POST", "/marketplace/connections"));
        Assert.Equal("route_denied", PlatformRequestPolicy.DenyReason("PATCH", "/marketplace/connections/3"));
        Assert.Equal("route_denied", PlatformRequestPolicy.DenyReason("POST", "/marketplace/connections/3/test"));
        Assert.Equal("route_denied", PlatformRequestPolicy.DenyReason("GET", "/api-keys"));
    }

    [Fact]
    public void NormalizePath_AddsApiPrefixAndSplitsQuery()
    {
        var (path, query) = PlatformRequestPolicy.SplitPathAndQuery("/products?categoryId=5");
        Assert.Equal("/api/products", path);
        Assert.Equal("5", query["categoryId"]);

        Assert.Equal("/api/products/12", PlatformRequestPolicy.NormalizePath("/api/products/12/"));
        Assert.False(PlatformRequestPolicy.IsMutating("GET"));
        Assert.True(PlatformRequestPolicy.IsMutating("PATCH"));
    }

    [Fact]
    public void SurfaceDescription_UsesRealCabinetPaths()
    {
        Assert.Contains("POST /products/{id}/files", PlatformRequestPolicy.SurfaceDescription, StringComparison.Ordinal);
        Assert.Contains("attachmentIds", PlatformRequestPolicy.SurfaceDescription, StringComparison.Ordinal);
        Assert.Contains("searchQuery", PlatformRequestPolicy.SurfaceDescription, StringComparison.Ordinal);
        Assert.Contains("/productgroups", PlatformRequestPolicy.SurfaceDescription, StringComparison.Ordinal);
        Assert.Contains("/marketplace/orders", PlatformRequestPolicy.SurfaceDescription, StringComparison.Ordinal);
        Assert.DoesNotContain("/stats", PlatformRequestPolicy.SurfaceDescription, StringComparison.Ordinal);
        Assert.DoesNotContain("GET /groups", PlatformRequestPolicy.SurfaceDescription, StringComparison.Ordinal);
        Assert.Contains("import-unlinked", PlatformRequestPolicy.SurfaceDescription, StringComparison.Ordinal);
        Assert.Contains("import-selected", PlatformRequestPolicy.SurfaceDescription, StringComparison.Ordinal);
        Assert.Contains("import-preview", PlatformRequestPolicy.SurfaceDescription, StringComparison.Ordinal);
        Assert.Contains("/marketplace/catalog-connect/jobs", PlatformRequestPolicy.SurfaceDescription, StringComparison.Ordinal);
        Assert.Contains("match/preview", PlatformRequestPolicy.SurfaceDescription, StringComparison.Ordinal);
        Assert.Contains("match/apply", PlatformRequestPolicy.SurfaceDescription, StringComparison.Ordinal);
        Assert.Contains("repair-mappings", PlatformRequestPolicy.SurfaceDescription, StringComparison.Ordinal);
        Assert.Contains("mappings/{mappingId}/graph", PlatformRequestPolicy.SurfaceDescription, StringComparison.Ordinal);
        Assert.DoesNotContain("GET …/graph", PlatformRequestPolicy.SurfaceDescription, StringComparison.Ordinal);
        Assert.Contains("/products/trash/count", PlatformRequestPolicy.SurfaceDescription, StringComparison.Ordinal);
        Assert.Contains("Do not tell the user to click", PlatformRequestPolicy.SurfaceDescription, StringComparison.Ordinal);
    }
}
