namespace Delegat.Models.Integration;

/// <summary>Durable outbound admission state. Scope identities are opaque digests, not credentials.</summary>
public sealed class IntegrationScopeState
{
    public string scopeKey { get; set; } = null!;
    public long revision { get; set; }
    public string? leaseOwner { get; set; }
    // UTC ticks preserve cooldown precision across database providers.
    public long leaseUntilTicks { get; set; }
    public long nextStartTicks { get; set; }
    public long cooldownUntilTicks { get; set; }
}
