using Delegat.Infrastructure.Auth;
using Delegat.Marketplace.SDK;

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

public sealed class PlatformToolDefinition
{
    public required string Id { get; init; }
    public required string Mode { get; init; }
    public required string Path { get; init; }
    public required string TitleKey { get; init; }
    public required string DescriptionKey { get; init; }
    public required string Icon { get; init; }
    public required IReadOnlyList<string> Permissions { get; init; }
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
    public IReadOnlyList<CredentialField> Params { get; init; } = [];
    public string? ResultKind { get; init; }
}

/// <summary>
/// First-party tools shipped in this instance (custom Vue pages).
/// </summary>
public static class PlatformTools
{
    public const string CatalogConnectId = "catalog-connect";
    public const string CatalogScrapeId = "catalog-scrape";

    public static readonly PlatformToolDefinition CatalogConnect = new()
    {
        Id = CatalogConnectId,
        Mode = ToolModes.Page,
        Path = "/tools/catalog-connect",
        TitleKey = "pages.toolsPage.catalogConnectTitle",
        DescriptionKey = "pages.toolsPage.catalogConnectDescription",
        Icon = "Cable",
        Permissions = [Permissions.MarketplaceManage],
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
    };

    public static IReadOnlyList<PlatformToolDefinition> All { get; } = [CatalogConnect, CatalogScrape];
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
        IReadOnlySet<string> connectedAdapterKeys)
    {
        var entries = new List<ToolCatalogEntry>();
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in platform)
        {
            if (!usedIds.Add(tool.Id))
                continue;
            var allowed = tool.Permissions.Any(userPermissions.Contains);
            if (!allowed)
                continue;
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
                Available = true,
                RequiresConnection = false,
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
                if (!permissions.Any(userPermissions.Contains))
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
                });
            }
        }

        return entries;
    }
}
