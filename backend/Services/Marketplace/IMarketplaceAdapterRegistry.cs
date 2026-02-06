namespace PrintO.Services.Marketplace;

/// <summary>
/// Marketplace adapter registry.
/// Allows discovering and retrieving adapters by key.
/// </summary>
public interface IMarketplaceAdapterRegistry
{
    /// <summary>
    /// Get all registered adapters
    /// </summary>
    IReadOnlyList<IMarketplaceAdapter> GetAll();

    /// <summary>
    /// Get an adapter by key
    /// </summary>
    /// <param name="key">Adapter key (e.g. "wildberries")</param>
    /// <returns>Adapter or null if not found</returns>
    IMarketplaceAdapter? GetByKey(string key);

    /// <summary>
    /// Check if an adapter exists
    /// </summary>
    bool Exists(string key);

    /// <summary>
    /// Get a list of available marketplaces for the UI
    /// </summary>
    IReadOnlyList<MarketplaceInfo> GetAvailableMarketplaces();
}

/// <summary>
/// Marketplace information for display in the UI
/// </summary>
public class MarketplaceInfo
{
    /// <summary>
    /// Adapter key
    /// </summary>
    public string Key { get; set; } = null!;

    /// <summary>
    /// Display name
    /// </summary>
    public string DisplayName { get; set; } = null!;

    /// <summary>
    /// Description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Logo URL
    /// </summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Supported capabilities
    /// </summary>
    public MarketplaceCapabilities Capabilities { get; set; }

    /// <summary>
    /// Credentials field schema (for dynamic UI form)
    /// </summary>
    public CredentialsSchema? CredentialsSchema { get; set; }
}
