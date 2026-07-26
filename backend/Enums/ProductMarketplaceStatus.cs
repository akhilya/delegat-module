namespace Delegat.Enums;

public enum ProductMarketplaceStatus
{
    NotSynced = 0,
    Synced = 1,
    SyncError = 2,
    Modified = 3,
    /// <summary>
    /// Local link points at an external product that no longer exists on the marketplace.
    /// </summary>
    RemoteMissing = 4
}
