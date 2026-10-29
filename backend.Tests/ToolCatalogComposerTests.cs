using Delegat.Infrastructure.Auth;
using Delegat.Marketplace.SDK;
using Delegat.Services.Tools;
using HostCaps = Delegat.Services.Marketplace.MarketplaceCapabilities;

namespace Delegat.Tests;

public class ToolCatalogComposerTests
{
    [Fact]
    public void Compose_IncludesPlatformPageTools_WhenUserCanManageMarketplace()
    {
        var entries = ToolCatalogComposer.Compose(
            PlatformTools.All,
            [],
            new HashSet<string> { Permissions.MarketplaceManage },
            new HashSet<string>());

        Assert.Equal(11, entries.Count);
        Assert.Contains(entries, e => e.Id == PlatformTools.CatalogConnectId && e.Stage == ToolStages.Stable);
        Assert.Contains(entries, e => e.Id == PlatformTools.ExportAllPricesId);
        Assert.Contains(entries, e => e.Id == PlatformTools.ClearPromotionProductsId);
        Assert.Contains(entries, e => e.Id == PlatformTools.ExportPricesId);
        Assert.Contains(entries, e => e.Id == PlatformTools.ExportStocksId);
        Assert.Contains(entries, e => e.Id == PlatformTools.ImportPricesId);
        Assert.Contains(entries, e => e.Id == PlatformTools.ImportProductsId);
        Assert.Contains(entries, e => e.Id == PlatformTools.VerifyProductLinksId);
        Assert.Contains(entries, e => e.Id == PlatformTools.CheckExportReadinessId);
        Assert.Contains(entries, e => e.Id == PlatformTools.ComparePricesId);
        var scrape = Assert.Single(entries, e => e.Id == PlatformTools.CatalogScrapeId);
        Assert.Equal(ToolSources.Platform, scrape.Source);
        Assert.Equal(ToolModes.Page, scrape.Mode);
        Assert.Equal("/tools/catalog-scrape", scrape.Path);
        Assert.Equal(ToolGroups.Catalog, scrape.Group);
        Assert.Equal(ToolStages.Experimental, scrape.Stage);
        Assert.True(scrape.Available);
        Assert.DoesNotContain(entries, e => e.Id == PlatformTools.PublicApiTryId);
    }

    [Fact]
    public void MarketplaceActions_AreConnectionBoundAndRegisteredAsPlatformJobs()
    {
        var entries = ToolCatalogComposer.Compose(
            PlatformTools.All,
            [],
            new HashSet<string> { Permissions.MarketplaceManage },
            new HashSet<string>());

        foreach (var id in PlatformTools.MarketplaceActionIds)
        {
            var entry = Assert.Single(entries, e => e.Id == id);
            Assert.Equal(ToolModes.Job, entry.Mode);
            Assert.True(entry.RequiresConnection);
            Assert.True(entry.QuickRun);
            Assert.True(PlatformTools.IsPlatformJob(id));
        }

        var import = Assert.Single(entries, e => e.Id == PlatformTools.ImportProductsId);
        Assert.Contains(import.Params, p => p.Key == "maxProducts" && p.IsRequired);
        Assert.Contains(import.Params, p => p.Key == "targetCategoryId" && !p.IsRequired);
    }

    [Fact]
    public void Compose_IncludesPublicApiTry_WhenUserHasApiAccess()
    {
        var entries = ToolCatalogComposer.Compose(
            PlatformTools.All,
            [],
            new HashSet<string> { Permissions.ApiAccess },
            new HashSet<string>());

        var tool = Assert.Single(entries);
        Assert.Equal(PlatformTools.PublicApiTryId, tool.Id);
        Assert.Equal(ToolSources.Platform, tool.Source);
        Assert.Equal(ToolModes.Page, tool.Mode);
        Assert.Equal("/tools/public-api-try", tool.Path);
        Assert.Equal("Play", tool.Icon);
        Assert.Equal(ToolGroups.Api, tool.Group);
        Assert.Equal(ToolStages.Stable, tool.Stage);
    }

    [Fact]
    public void ApplyInstanceFlags_HidesPublicApiTry_WhenPublicApiDisabled()
    {
        var entries = ToolCatalogComposer.Compose(
            PlatformTools.All,
            [],
            new HashSet<string> { Permissions.ApiAccess, Permissions.MarketplaceManage },
            new HashSet<string>());
        Assert.Contains(entries, e => e.Id == PlatformTools.PublicApiTryId);

        var hidden = ToolCatalogComposer.ApplyInstanceFlags(entries, publicApiEnabled: false);
        Assert.DoesNotContain(hidden, e => e.Id == PlatformTools.PublicApiTryId);
        Assert.Contains(hidden, e => e.Id == PlatformTools.CatalogConnectId);

        var shown = ToolCatalogComposer.ApplyInstanceFlags(entries, publicApiEnabled: true);
        Assert.Contains(shown, e => e.Id == PlatformTools.PublicApiTryId);
    }

    [Fact]
    public void Compose_IncludesBrowseableTools_WhenUserHasWorkspaceView()
    {
        var entries = ToolCatalogComposer.Compose(
            PlatformTools.All,
            [],
            new HashSet<string> { Permissions.WorkspaceView },
            new HashSet<string>());

        Assert.Contains(entries, e => e.Id == PlatformTools.CatalogConnectId);
        Assert.Contains(entries, e => e.Id == PlatformTools.CatalogScrapeId);
        Assert.Contains(entries, e => e.Id == PlatformTools.ExportAllPricesId);
        Assert.Contains(entries, e => e.Id == PlatformTools.ClearPromotionProductsId);
        Assert.Contains(entries, e => e.Id == PlatformTools.PublicApiTryId);
    }

    [Fact]
    public void Compose_HidesCatalogConnect_WithoutMarketplacePermission()
    {
        var entries = ToolCatalogComposer.Compose(
            PlatformTools.All,
            [],
            new HashSet<string> { Permissions.CatalogManage },
            new HashSet<string>());

        Assert.Empty(entries);
    }

    [Fact]
    public void Compose_PrefixesAdapterToolIds_AndMarksMissingConnection()
    {
        var adapters = new List<(string, IReadOnlyList<AdapterToolDescriptor>)>
        {
            ("wildberries", [
                new AdapterToolDescriptor
                {
                    Id = "duplicate-cards",
                    Title = "Find copies",
                    Description = "Search copied cards",
                    RequiresConnection = true,
                    ResultKind = AdapterToolResultKind.Table,
                },
            ]),
        };

        var entries = ToolCatalogComposer.Compose(
            PlatformTools.All,
            adapters,
            new HashSet<string> { Permissions.MarketplaceManage },
            new HashSet<string>());

        Assert.Equal(12, entries.Count);
        var job = entries.Single(e => e.Source == ToolSources.Adapter);
        Assert.Equal("wildberries.duplicate-cards", job.Id);
        Assert.Equal("/tools/wildberries.duplicate-cards", job.Path);
        Assert.Equal(ToolModes.Job, job.Mode);
        Assert.Equal(ToolStages.Stable, job.Stage);
        Assert.False(job.Available);
        Assert.Equal("connection_required", job.UnavailableReason);
    }

    [Fact]
    public void Compose_AdapterToolAvailable_WhenConnectionExists()
    {
        var adapters = new List<(string, IReadOnlyList<AdapterToolDescriptor>)>
        {
            ("ozon", [
                new AdapterToolDescriptor { Id = "scraper", Title = "Scraper" },
            ]),
        };

        var entries = ToolCatalogComposer.Compose(
            [],
            adapters,
            new HashSet<string> { Permissions.MarketplaceManage },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ozon" });

        var job = Assert.Single(entries);
        Assert.True(job.Available);
        Assert.Null(job.UnavailableReason);
    }

    [Fact]
    public void Compose_SkipsUnsafeAdapterToolIds_AndDoesNotOverridePlatform()
    {
        var adapters = new List<(string, IReadOnlyList<AdapterToolDescriptor>)>
        {
            ("wildberries", [
                new AdapterToolDescriptor { Id = "catalog-connect", Title = "Nope" },
                new AdapterToolDescriptor { Id = "Bad_Id", Title = "Nope" },
                new AdapterToolDescriptor { Id = "ok-tool", Title = "OK" },
            ]),
        };

        var entries = ToolCatalogComposer.Compose(
            PlatformTools.All,
            adapters,
            new HashSet<string> { Permissions.MarketplaceManage },
            new HashSet<string> { "wildberries" });

        Assert.Equal(13, entries.Count);
        Assert.Contains(entries, e => e.Id == PlatformTools.CatalogConnectId && e.Source == ToolSources.Platform);
        Assert.Contains(entries, e => e.Id == PlatformTools.CatalogScrapeId && e.Source == ToolSources.Platform);
        Assert.Contains(entries, e => e.Id == "wildberries.catalog-connect");
        Assert.Contains(entries, e => e.Id == "wildberries.ok-tool");
        Assert.Equal(ToolGroups.Adapters, Assert.Single(entries, e => e.Id == "wildberries.ok-tool").Group);
        Assert.DoesNotContain(entries, e => e.Id.Contains("Bad", StringComparison.Ordinal));
    }

    [Fact]
    public void TryParseAdapterToolId_RejectsPlatformIds()
    {
        Assert.False(ToolCatalogComposer.TryParseAdapterToolId("catalog-connect", out _, out _));
        Assert.False(ToolCatalogComposer.TryParseAdapterToolId("catalog-scrape", out _, out _));
        Assert.False(ToolCatalogComposer.TryParseAdapterToolId("public-api-try", out _, out _));
        Assert.False(ToolCatalogComposer.TryParseAdapterToolId("export-all-prices", out _, out _));
        Assert.False(ToolCatalogComposer.TryParseAdapterToolId("clear-promotion-products", out _, out _));
        Assert.True(ToolCatalogComposer.TryParseAdapterToolId("wb.duplicate-cards", out var key, out var local));
        Assert.Equal("wb", key);
        Assert.Equal("duplicate-cards", local);
    }

    [Fact]
    public void Compose_PlatformJobTools_AreUnavailableWithoutActiveCapability()
    {
        var entries = ToolCatalogComposer.Compose(
            PlatformTools.All,
            [],
            new HashSet<string> { Permissions.MarketplaceManage },
            new HashSet<string>());

        var prices = Assert.Single(entries, e => e.Id == PlatformTools.ExportAllPricesId);
        Assert.Equal(ToolModes.Job, prices.Mode);
        Assert.True(prices.QuickRun);
        Assert.Equal(ToolGroups.Prices, prices.Group);
        Assert.Equal(ToolStages.Experimental, prices.Stage);
        Assert.False(prices.Available);
        Assert.Equal("connection_required", prices.UnavailableReason);
        Assert.Equal("ExportPrices", prices.RequiredCapability);

        var promos = Assert.Single(entries, e => e.Id == PlatformTools.ClearPromotionProductsId);
        Assert.True(promos.Destructive);
        Assert.True(promos.QuickRun);
        Assert.Equal(ToolGroups.Promotions, promos.Group);
        Assert.Equal(ToolStages.Experimental, promos.Stage);
        Assert.False(promos.Available);
        Assert.Equal("UpdatePromotions", promos.RequiredCapability);
    }

    [Fact]
    public void Compose_PlatformJobTools_AvailableWhenActiveAdapterHasFlags()
    {
        var caps = new Dictionary<string, HostCaps>(StringComparer.OrdinalIgnoreCase)
        {
            ["ozon"] = HostCaps.ExportPrices
                | HostCaps.GetPromotions
                | HostCaps.UpdatePromotions
        };

        var entries = ToolCatalogComposer.Compose(
            PlatformTools.All,
            [],
            new HashSet<string> { Permissions.MarketplaceManage },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ozon" },
            caps);

        Assert.True(Assert.Single(entries, e => e.Id == PlatformTools.ExportAllPricesId).Available);
        Assert.True(Assert.Single(entries, e => e.Id == PlatformTools.ClearPromotionProductsId).Available);
    }

    [Fact]
    public void Compose_ClearPromotions_RequiresBothPromotionFlags()
    {
        var caps = new Dictionary<string, HostCaps>(StringComparer.OrdinalIgnoreCase)
        {
            ["ozon"] = HostCaps.GetPromotions
        };

        var entries = ToolCatalogComposer.Compose(
            PlatformTools.All,
            [],
            new HashSet<string> { Permissions.MarketplaceManage },
            new HashSet<string> { "ozon" },
            caps);

        Assert.False(Assert.Single(entries, e => e.Id == PlatformTools.ClearPromotionProductsId).Available);
        Assert.False(Assert.Single(entries, e => e.Id == PlatformTools.ExportAllPricesId).Available);
    }
}
