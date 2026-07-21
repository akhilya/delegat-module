namespace Delegat.Services.PlatformLink;

public sealed class ReferenceCatalogStatusDto
{
    public bool Linked { get; init; }
    public DateTime? LastSyncedAt { get; init; }
    public int CurrenciesCount { get; init; }
    public int TimezonesCount { get; init; }
    public int PluginsCount { get; init; }
    public bool Ready { get; init; }
}
