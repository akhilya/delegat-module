using Delegat.Infrastructure.Auth;
using Delegat.Marketplace.SDK;
using Delegat.Services.Marketplace;
using CredentialField = Delegat.Marketplace.SDK.CredentialField;
using PlatformCredentialField = Delegat.Services.Marketplace.CredentialField;
using HostCaps = Delegat.Services.Marketplace.MarketplaceCapabilities;

namespace Delegat.Services.Tools;

public static class ToolModes
{
    public const string Page = "page";
    public const string Job = "job";
}

public static class ToolSources
{
    public const string Platform = "platform";
    public const string Adapter = "adapter";
}

public static class ToolGroups
{
    public const string Catalog = "catalog";
    public const string Prices = "prices";
    public const string Promotions = "promotions";
    public const string Api = "api";
    public const string Adapters = "adapters";
}

public static class ToolStages
{
    public const string Stable = "stable";
    public const string Experimental = "experimental";
    public const string Beta = "beta";
    public const string Deprecated = "deprecated";
}

public sealed class PlatformToolDefinition
{
    public required string Id { get; init; }
    public required string Mode { get; init; }
    public required string Path { get; init; }
    public required string TitleKey { get; init; }
    public required string DescriptionKey { get; init; }
    public required string Icon { get; init; }
    public required IReadOnlyList<string> Permissions { get; init; }
    public bool RequiresConnection { get; init; }
    public HostCaps? RequiredCapability { get; init; }
    public string? RequiredCapabilityName { get; init; }
    public bool QuickRun { get; init; }
    public bool Destructive { get; init; }
    public string Group { get; init; } = ToolGroups.Catalog;
    public string Stage { get; init; } = ToolStages.Stable;
    public IReadOnlyList<PlatformCredentialField> Params { get; init; } = [];
}

public sealed class ToolCatalogEntry
{
    public required string Id { get; init; }
    public required string Source { get; init; }
    public string? AdapterKey { get; init; }
    public required string Mode { get; init; }
    public required string Path { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? TitleKey { get; init; }
    public string? DescriptionKey { get; init; }
    public string? Icon { get; init; }
    public required IReadOnlyList<string> Permissions { get; init; }
    public bool Available { get; init; }
    public string? UnavailableReason { get; init; }
    public bool RequiresConnection { get; init; }
    public string? RequiredCapability { get; init; }
    public bool QuickRun { get; init; }
    public bool Destructive { get; init; }
    public string? Group { get; init; }
    public string Stage { get; init; } = ToolStages.Stable;
    public IReadOnlyList<CredentialField> Params { get; init; } = [];
    public string? ResultKind { get; init; }
}

/// <summary>
/// First-party tools shipped in this instance (custom Vue pages and platform jobs).
/// </summary>
public static class PlatformTools
{
    public const string CatalogConnectId = "catalog-connect";
    public const string CatalogScrapeId = "catalog-scrape";
    public const string PublicApiTryId = "public-api-try";
    public const string ExportAllPricesId = "export-all-prices";
    public const string ClearPromotionProductsId = "clear-promotion-products";
    public const string ExportPricesId = "export-prices";
    public const string ExportStocksId = "export-stocks";
    public const string ImportPricesId = "import-prices";
    public const string ImportProductsId = "import-products";
    public const string VerifyProductLinksId = "verify-product-links";
    public const string CheckExportReadinessId = "check-export-readiness";
    public const string ComparePricesId = "compare-prices";

    private static PlatformToolDefinition MarketplaceAction(
        string id, string titleKey, string descriptionKey, HostCaps capability,
        string icon, string group = ToolGroups.Prices, bool destructive = false,
        IReadOnlyList<PlatformCredentialField>? parameters = null) => new()
    {
        Id = id,
        Mode = ToolModes.Job,
        Path = $"/tools/{id}",
        TitleKey = titleKey,
        DescriptionKey = descriptionKey,
        Icon = icon,
        Permissions = [Permissions.MarketplaceManage],
        RequiresConnection = true,
        RequiredCapability = capability,
        RequiredCapabilityName = capability.ToString(),
        QuickRun = true,
        Destructive = destructive,
        Group = group,
        Params = parameters ?? [],
    };

    public static readonly PlatformToolDefinition ExportPrices = MarketplaceAction(
        ExportPricesId, "pages.toolsPage.exportPricesTitle", "pages.toolsPage.exportPricesDescription",
        HostCaps.ExportPrices, "DollarSign", destructive: true);
    public static readonly PlatformToolDefinition ExportStocks = MarketplaceAction(
        ExportStocksId, "pages.toolsPage.exportStocksTitle", "pages.toolsPage.exportStocksDescription",
        HostCaps.ExportStocks, "Boxes", destructive: true);
    public static readonly PlatformToolDefinition ImportPrices = MarketplaceAction(
        ImportPricesId, "pages.toolsPage.importPricesTitle", "pages.toolsPage.importPricesDescription",
        HostCaps.ImportPrices, "Download", destructive: true);
    public static readonly PlatformToolDefinition ImportProducts = MarketplaceAction(
        ImportProductsId, "pages.toolsPage.importProductsTitle", "pages.toolsPage.importProductsDescription",
        HostCaps.ImportAllProducts, "PackagePlus", group: ToolGroups.Catalog, destructive: true,
        parameters:
        [
            new PlatformCredentialField { Key = "maxProducts", DisplayName = "Maximum products per run", Type = "number", IsRequired = true, DefaultValue = "50", Description = "Between 1 and 200." },
            new PlatformCredentialField { Key = "targetCategoryId", DisplayName = "Local category ID", Type = "number", IsRequired = false, Description = "Optional. Imported products remain uncategorized when empty." },
        ]);
    public static readonly PlatformToolDefinition VerifyProductLinks = MarketplaceAction(
        VerifyProductLinksId, "pages.toolsPage.verifyProductLinksTitle", "pages.toolsPage.verifyProductLinksDescription",
        HostCaps.ImportProduct, "Link", group: ToolGroups.Catalog, destructive: true);
    public static readonly PlatformToolDefinition CheckExportReadiness = MarketplaceAction(
        CheckExportReadinessId, "pages.toolsPage.checkExportReadinessTitle", "pages.toolsPage.checkExportReadinessDescription",
        HostCaps.ExportProduct, "ClipboardCheck", group: ToolGroups.Catalog);
    public static readonly PlatformToolDefinition ComparePrices = MarketplaceAction(
        ComparePricesId, "pages.toolsPage.comparePricesTitle", "pages.toolsPage.comparePricesDescription",
        HostCaps.ImportPrices, "Scale");

    public static readonly PlatformToolDefinition CatalogConnect = new()
    {
        Id = CatalogConnectId,
        Mode = ToolModes.Page,
        Path = "/tools/catalog-connect",
        TitleKey = "pages.toolsPage.catalogConnectTitle",
        DescriptionKey = "pages.toolsPage.catalogConnectDescription",
        Icon = "Cable",
        Permissions = [Permissions.MarketplaceManage],
        Group = ToolGroups.Catalog,
    };

    public static readonly PlatformToolDefinition CatalogScrape = new()
    {
        Id = CatalogScrapeId,
        Mode = ToolModes.Page,
        Path = "/tools/catalog-scrape",
        TitleKey = "pages.toolsPage.catalogScrapeTitle",
        DescriptionKey = "pages.toolsPage.catalogScrapeDescription",
        Icon = "Search",
        Permissions = [Permissions.MarketplaceManage],
        Group = ToolGroups.Catalog,
        Stage = ToolStages.Experimental,
    };

    public static readonly PlatformToolDefinition PublicApiTry = new()
    {
        Id = PublicApiTryId,
        Mode = ToolModes.Page,
        Path = "/tools/public-api-try",
        TitleKey = "pages.toolsPage.publicApiTryTitle",
        DescriptionKey = "pages.toolsPage.publicApiTryDescription",
        Icon = "Play",
        Permissions = [Permissions.ApiAccess],
        Group = ToolGroups.Api,
    };

    public static readonly PlatformToolDefinition ExportAllPrices = new()
    {
        Id = ExportAllPricesId,
        Mode = ToolModes.Job,
        Path = "/tools/export-all-prices",
        TitleKey = "pages.toolsPage.exportAllPricesTitle",
        DescriptionKey = "pages.toolsPage.exportAllPricesDescription",
        Icon = "DollarSign",
        Permissions = [Permissions.MarketplaceManage],
        RequiresConnection = true,
        RequiredCapability = HostCaps.ExportPrices,
        RequiredCapabilityName = nameof(HostCaps.ExportPrices),
        QuickRun = true,
        Group = ToolGroups.Prices,
        Stage = ToolStages.Experimental,
    };

    public static readonly PlatformToolDefinition ClearPromotionProducts = new()
    {
        Id = ClearPromotionProductsId,
        Mode = ToolModes.Job,
        Path = "/tools/clear-promotion-products",
        TitleKey = "pages.toolsPage.clearPromotionProductsTitle",
        DescriptionKey = "pages.toolsPage.clearPromotionProductsDescription",
        Icon = "Tag",
        Permissions = [Permissions.MarketplaceManage],
        RequiresConnection = true,
        RequiredCapability = HostCaps.GetPromotions | HostCaps.UpdatePromotions,
        RequiredCapabilityName = nameof(HostCaps.UpdatePromotions),
        QuickRun = true,
        Destructive = true,
        Group = ToolGroups.Promotions,
        Stage = ToolStages.Experimental,
    };

    public static IReadOnlyList<PlatformToolDefinition> All { get; } =
        [CatalogConnect, CatalogScrape, ExportAllPrices, ExportPrices, ExportStocks, ImportPrices,
            ImportProducts, VerifyProductLinks, CheckExportReadiness, ComparePrices,
            ClearPromotionProducts, PublicApiTry];

    public static HostCaps? RequiredCapabilityFor(string id)
    {
        if (string.Equals(id, ExportAllPricesId, StringComparison.OrdinalIgnoreCase))
            return HostCaps.ExportPrices;
        if (string.Equals(id, ClearPromotionProductsId, StringComparison.OrdinalIgnoreCase))
            return HostCaps.GetPromotions | HostCaps.UpdatePromotions;
        if (MarketplaceActionIds.Contains(id, StringComparer.OrdinalIgnoreCase))
            return MarketplaceActionExecutor.RequiredCapability(id);
        return null;
    }

    public static readonly string[] MarketplaceActionIds =
        [ExportPricesId, ExportStocksId, ImportPricesId, ImportProductsId,
            VerifyProductLinksId, CheckExportReadinessId, ComparePricesId];

    public static bool IsMarketplaceAction(string id)
        => MarketplaceActionIds.Contains(id, StringComparer.OrdinalIgnoreCase);

    public static bool IsPlatformJob(string id)
        => string.Equals(id, ExportAllPricesId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(id, ClearPromotionProductsId, StringComparison.OrdinalIgnoreCase)
            || IsMarketplaceAction(id);
}

/// <summary>
/// Pure merge of platform tools and adapter-declared job tools.
/// </summary>
public static class ToolCatalogComposer
{
    public static string AdapterToolId(string adapterKey, string localId)
        => $"{adapterKey}.{localId}";

    public static bool TryParseAdapterToolId(string id, out string adapterKey, out string localId)
    {
        adapterKey = "";
        localId = "";
        var dot = id.IndexOf('.');
        if (dot <= 0 || dot >= id.Length - 1)
            return false;
        adapterKey = id[..dot];
        localId = id[(dot + 1)..];
        return IsSafeLocalId(localId);
    }

    public static bool IsSafeLocalId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64)
            return false;
        foreach (var ch in id)
        {
            var ok = (ch >= 'a' && ch <= 'z')
                || (ch >= '0' && ch <= '9')
                || ch == '-';
            if (!ok) return false;
        }

        return true;
    }

    public static IReadOnlyList<ToolCatalogEntry> Compose(
        IReadOnlyList<PlatformToolDefinition> platform,
        IReadOnlyList<(string AdapterKey, IReadOnlyList<AdapterToolDescriptor> Tools)> adapters,
        IReadOnlySet<string> userPermissions,
        IReadOnlySet<string> connectedAdapterKeys,
        IReadOnlyDictionary<string, HostCaps>? activeAdapterCapabilities = null)
    {
        var entries = new List<ToolCatalogEntry>();
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in platform)
        {
            if (!usedIds.Add(tool.Id))
                continue;
            var allowed = Permissions.UserMayReadAny(userPermissions, tool.Permissions);
            if (!allowed)
                continue;

            var requiresConnection = tool.RequiresConnection || tool.RequiredCapability != null;
            var available = true;
            string? unavailable = null;
            if (tool.RequiredCapability is { } cap)
            {
                var has = activeAdapterCapabilities != null
                    && activeAdapterCapabilities.Values.Any(c => c.HasFlag(cap));
                if (!has)
                {
                    available = false;
                    unavailable = "connection_required";
                }
            }

            entries.Add(new ToolCatalogEntry
            {
                Id = tool.Id,
                Source = ToolSources.Platform,
                Mode = tool.Mode,
                Path = tool.Path,
                TitleKey = tool.TitleKey,
                DescriptionKey = tool.DescriptionKey,
                Icon = tool.Icon,
                Permissions = tool.Permissions,
                Available = available,
                UnavailableReason = unavailable,
                RequiresConnection = requiresConnection,
                RequiredCapability = tool.RequiredCapabilityName,
                QuickRun = tool.QuickRun,
                Destructive = tool.Destructive,
                Group = tool.Group,
                Stage = string.IsNullOrWhiteSpace(tool.Stage) ? ToolStages.Stable : tool.Stage,
                Params = tool.Params.Select(field => new CredentialField
                {
                    Key = field.Key,
                    DisplayName = field.DisplayName,
                    Description = field.Description,
                    Type = field.Type,
                    IsRequired = field.IsRequired,
                    IsSecret = field.IsSecret,
                    DefaultValue = field.DefaultValue,
                    Placeholder = field.Placeholder,
                }).ToList(),
                ResultKind = tool.Mode == ToolModes.Job ? "none" : null,
            });
        }

        foreach (var (adapterKey, tools) in adapters)
        {
            if (string.IsNullOrWhiteSpace(adapterKey))
                continue;
            foreach (var tool in tools)
            {
                if (!IsSafeLocalId(tool.Id))
                    continue;
                var id = AdapterToolId(adapterKey, tool.Id);
                if (!usedIds.Add(id))
                    continue;

                var permissions = new[] { Permissions.MarketplaceManage };
                if (!Permissions.UserMayReadAny(userPermissions, permissions))
                    continue;

                var needsConnection = tool.RequiresConnection;
                var hasConnection = connectedAdapterKeys.Contains(adapterKey);
                string? unavailable = null;
                var available = true;
                if (needsConnection && !hasConnection)
                {
                    available = false;
                    unavailable = "connection_required";
                }

                entries.Add(new ToolCatalogEntry
                {
                    Id = id,
                    Source = ToolSources.Adapter,
                    AdapterKey = adapterKey,
                    Mode = ToolModes.Job,
                    Path = $"/tools/{id}",
                    Title = string.IsNullOrWhiteSpace(tool.Title) ? id : tool.Title,
                    Description = tool.Description,
                    Permissions = permissions,
                    Available = available,
                    UnavailableReason = unavailable,
                    RequiresConnection = needsConnection,
                    Params = tool.Params ?? [],
                    ResultKind = tool.ResultKind.ToString().ToLowerInvariant(),
                    Group = ToolGroups.Adapters,
                    Stage = ToolStages.Stable,
                });
            }
        }

        return entries;
    }

    /// <summary>
    /// Hide Public API try when the instance toggle is off (same as API keys).
    /// </summary>
    public static IReadOnlyList<ToolCatalogEntry> ApplyInstanceFlags(
        IReadOnlyList<ToolCatalogEntry> entries,
        bool publicApiEnabled)
    {
        if (publicApiEnabled)
            return entries;
        return entries.Where(e => e.Id != PlatformTools.PublicApiTryId).ToList();
    }
}
