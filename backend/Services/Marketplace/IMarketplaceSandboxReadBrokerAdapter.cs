using System.Text.Json;
using Delegat.Marketplace.SDK;
using Delegat.Models.Marketplace;

namespace Delegat.Services.Marketplace;

/// <summary>Host-side bridge to the optional read-broker capability of a loaded adapter plugin.</summary>
public interface IMarketplaceSandboxReadBrokerAdapter
{
    bool SupportsReadBroker => false;
    bool SupportsGetReads => false;

    IReadOnlyList<MarketplaceSandboxReadOperationDescriptor> GetReadOperations() => [];

    Task<MarketplaceSandboxReadResponse?> ReadAsync(
        MarketplaceConnection connection,
        string hostKey,
        string path,
        CancellationToken ct);

    /// <summary>Dispatch an adapter-defined operation; null means this plugin does not opt in.</summary>
    Task<MarketplaceSandboxReadResponse?> ReadOperationAsync(
        MarketplaceConnection connection,
        string operation,
        JsonElement arguments,
        CancellationToken ct) => Task.FromResult<MarketplaceSandboxReadResponse?>(null);
}
