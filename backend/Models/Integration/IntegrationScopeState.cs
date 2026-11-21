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

    // Durable window/token-bucket/quota accounting (request, item, byte, or weighted error units).
    public long windowStartTicks { get; set; }
    public long windowEndTicks { get; set; }
    public long usedUnits { get; set; }

    // Externally observed provider quota (e.g. Ozon dynamic daily limits, Yandex/Avito headers).
    public long? observedLimit { get; set; }
    public long? observedRemaining { get; set; }
    public long observedAtTicks { get; set; }

    // Delimited active leases (|owner:expiresTicks|...) for scopes with MaxConcurrency > 1.
    public string? activeLeases { get; set; }
}
