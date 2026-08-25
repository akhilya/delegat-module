using Delegat.Infrastructure.Auth;
using Delegat.Marketplace.SDK;
using Delegat.Services.Tools;

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

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Id == PlatformTools.CatalogConnectId);
        var scrape = Assert.Single(entries, e => e.Id == PlatformTools.CatalogScrapeId);
        Assert.Equal(ToolSources.Platform, scrape.Source);
        Assert.Equal(ToolModes.Page, scrape.Mode);
        Assert.Equal("/tools/catalog-scrape", scrape.Path);
        Assert.True(scrape.Available);
        Assert.DoesNotContain(entries, e => e.Id == PlatformTools.PublicApiTryId);
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

        Assert.Equal(3, entries.Count);
        var job = entries.Single(e => e.Source == ToolSources.Adapter);
        Assert.Equal("wildberries.duplicate-cards", job.Id);
        Assert.Equal("/tools/wildberries.duplicate-cards", job.Path);
        Assert.Equal(ToolModes.Job, job.Mode);
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

        Assert.Equal(4, entries.Count);
        Assert.Contains(entries, e => e.Id == PlatformTools.CatalogConnectId && e.Source == ToolSources.Platform);
        Assert.Contains(entries, e => e.Id == PlatformTools.CatalogScrapeId && e.Source == ToolSources.Platform);
        Assert.Contains(entries, e => e.Id == "wildberries.catalog-connect");
        Assert.Contains(entries, e => e.Id == "wildberries.ok-tool");
        Assert.DoesNotContain(entries, e => e.Id.Contains("Bad", StringComparison.Ordinal));
    }

    [Fact]
    public void TryParseAdapterToolId_RejectsPlatformIds()
    {
        Assert.False(ToolCatalogComposer.TryParseAdapterToolId("catalog-connect", out _, out _));
        Assert.False(ToolCatalogComposer.TryParseAdapterToolId("catalog-scrape", out _, out _));
        Assert.False(ToolCatalogComposer.TryParseAdapterToolId("public-api-try", out _, out _));
        Assert.True(ToolCatalogComposer.TryParseAdapterToolId("wb.duplicate-cards", out var key, out var local));
        Assert.Equal("wb", key);
        Assert.Equal("duplicate-cards", local);
    }
}
