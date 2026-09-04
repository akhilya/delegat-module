using Delegat.Services.Marketplace;

namespace Delegat.Tests.Fakes;

public sealed class SingleAdapterRegistry(IMarketplaceAdapter adapter) : IMarketplaceAdapterRegistry
{
    public IReadOnlyList<IMarketplaceAdapter> GetAll() => [adapter];

    public IMarketplaceAdapter? GetByKey(string key) =>
        key == adapter.Key ? adapter : null;

    public bool Exists(string key) => key == adapter.Key;

    public IReadOnlyList<MarketplaceInfo> GetAvailableMarketplaces() => [];
}
