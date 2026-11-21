using Delegat.Models.Integration;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace Delegat.Services.Integration;

internal enum IntegrationScopeWindowKind
{
    None = 0,
    TokenBucket = 1,
    FixedWindow = 2,
    CalendarReset = 3
}

// Keys come from host policy, never from a connection ID or credential substitute.
// MinimumInterval spaces reservations; WindowKind/Allowance/Burst/MaxConcurrency model
// audited provider quota, concurrency, burst, and calendar-reset rules.
internal sealed record IntegrationScope(
    string Key,
    TimeSpan MinimumInterval,
    int MaxConcurrency = 1,
    IntegrationScopeWindowKind WindowKind = IntegrationScopeWindowKind.None,
    long? Allowance = null,
    TimeSpan? Period = null,
    long Burst = 0,
    TimeSpan? RefillInterval = null,
    long ReservedCost = 1,
    string? ResetTimeZoneId = null,
    int ResetHourOfDay = 0);

internal sealed record IntegrationReservedScope(
    string RawKey,
    string HashedKey,
    int MaxConcurrency,
    long ReservedCost,
    long WindowStartTicks,
    IntegrationScopeWindowKind WindowKind);

internal sealed record IntegrationScopeLease(string Owner, IReadOnlyList<string> ScopeKeys, DateTimeOffset ExpiresAt)
{
    internal IReadOnlyDictionary<string, IntegrationReservedScope> Details { get; init; } =
        new Dictionary<string, IntegrationReservedScope>(StringComparer.Ordinal);
}

internal sealed record IntegrationScopeSettlement(
    long? ActualCost = null,
    DateTimeOffset? CooldownUntil = null,
    long? ObservedLimit = null,
    long? ObservedRemaining = null,
    DateTimeOffset? ObservedResetAt = null);

internal sealed record IntegrationScopeAdmissionResult(IntegrationScopeLease? Lease, DateTimeOffset? RetryAt);

internal sealed record IntegrationScopeSnapshot(
    string RawKey,
    string HashedKey,
    int ActiveLeases,
    DateTimeOffset? CooldownUntil,
    DateTimeOffset? NextStartAt,
    long UsedUnits,
    DateTimeOffset? WindowEndAt,
    long? ObservedLimit,
    long? ObservedRemaining);

/// <summary>Atomically reserves related outbound scopes and persists waits, budgets, and cooldowns without holding a database transaction across I/O.</summary>
internal sealed class IntegrationScopeAdmission(IServiceScopeFactory scopeFactory, TimeProvider clock)
{
    private const int MaxContentionAttempts = 5;

    internal static string HashScopeKey(string rawKey) =>
        IntegrationCoordinator.ComputeHash("integration-scope:v1:" + rawKey);

    internal static string FormatActiveLeaseMarker(string owner) => $"|{owner}:";

    internal async Task<IntegrationScopeAdmissionResult> TryReserveAsync(
        IReadOnlyList<IntegrationScope> scopes, TimeSpan leaseLifetime, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        if (scopes.Count == 0 || scopes.Any(s =>
                string.IsNullOrWhiteSpace(s.Key)
                || s.MinimumInterval < TimeSpan.Zero
                || s.MaxConcurrency < 0
                || s.ReservedCost < 0
                || (s.WindowKind == IntegrationScopeWindowKind.TokenBucket && (s.Burst <= 0 || s.RefillInterval is not { Ticks: > 0 }))
                || (s.WindowKind == IntegrationScopeWindowKind.FixedWindow && s.Period is not { Ticks: > 0 })
                || (s.WindowKind == IntegrationScopeWindowKind.CalendarReset && (string.IsNullOrWhiteSpace(s.ResetTimeZoneId) || s.ResetHourOfDay is < 0 or > 23))))
            throw new ArgumentException("At least one valid scope with valid concurrency, spacing, and window parameters is required.", nameof(scopes));
        if (leaseLifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseLifetime));

        var ordered = scopes
            .Select(s => (HashedKey: HashScopeKey(s.Key), Scope: s))
            .OrderBy(s => s.HashedKey, StringComparer.Ordinal)
            .ToArray();
        var keys = ordered.Select(s => s.HashedKey).ToArray();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length)
            throw new ArgumentException("Scope keys must be unique within a reservation.", nameof(scopes));

        var owner = Guid.NewGuid().ToString("N");
        var expiresAt = clock.GetUtcNow() + leaseLifetime;
        var expires = expiresAt.UtcTicks;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();
        var states = db.integrationScopeStates;
        var strategy = db.Database.CreateExecutionStrategy();

        for (var i = 0; i < MaxContentionAttempts; i++)
        {
            var result = await strategy.ExecuteAsync<IntegrationScopeAdmissionResult?>(async token =>
            {
                var now = clock.GetUtcNow();
                if (now >= expiresAt)
                    return new(null, now);
                var nowTicks = now.UtcTicks;

                await using var tx = await db.Database.BeginTransactionAsync(token);
                var rows = await states.AsNoTracking()
                    .Where(s => keys.Contains(s.scopeKey))
                    .ToDictionaryAsync(s => s.scopeKey, token);

                // Replay recovery after a lost commit acknowledgement: if every concurrency-leased
                // scope already carries our owner/expiry, our earlier transaction committed.
                var leasedItems = ordered.Where(x => x.Scope.MaxConcurrency > 0).ToArray();
                if (leasedItems.Length > 0 && rows.Count == keys.Length &&
                    leasedItems.All(item => OwnsLease(rows[item.HashedKey], item.Scope.MaxConcurrency, owner, expires)))
                {
                    // Our commit landed, but a late provider response can have extended a cooldown since then.
                    if (!rows.Values.Any(s => s.cooldownUntilTicks > nowTicks))
                    {
                        var recoveredDetails = ordered.ToDictionary(
                            x => x.HashedKey,
                            x => new IntegrationReservedScope(
                                x.Scope.Key, x.HashedKey, x.Scope.MaxConcurrency, x.Scope.ReservedCost,
                                rows[x.HashedKey].windowStartTicks, x.Scope.WindowKind),
                            StringComparer.Ordinal);
                        return new(new IntegrationScopeLease(owner, Array.AsReadOnly(keys), expiresAt) { Details = recoveredDetails }, null);
                    }

                    var blockedUntil = rows.Values.Max(s => Math.Max(s.nextStartTicks, s.cooldownUntilTicks));
                    // No lease was handed out; free all of our leases without refunding their spacing.
                    foreach (var item in leasedItems)
                        await ReleaseOwnedLeaseAsync(states, rows[item.HashedKey], item.Scope.MaxConcurrency, owner, nowTicks, token);

                    await tx.CommitAsync(token);
                    return new(null, new DateTimeOffset(blockedUntil, TimeSpan.Zero));
                }

                // Evaluate all scopes before mutating any row so a denial consumes nothing.
                var plans = new (string HashedKey, IntegrationScope Scope, IntegrationScopeState? Row,
                    string? NewLeaseOwner, long NewLeaseUntilTicks, string? NewActiveLeases,
                    long NewNextStartTicks, long NewWindowStartTicks, long NewWindowEndTicks, long NewUsedUnits)[ordered.Length];
                long maxBlockedTicks = 0;

                for (var idx = 0; idx < ordered.Length; idx++)
                {
                    var (hashedKey, item) = ordered[idx];
                    rows.TryGetValue(hashedKey, out var row);

                    if (row is not null)
                    {
                        if (row.cooldownUntilTicks > nowTicks)
                            maxBlockedTicks = Math.Max(maxBlockedTicks, row.cooldownUntilTicks);
                        if (row.nextStartTicks > nowTicks)
                            maxBlockedTicks = Math.Max(maxBlockedTicks, row.nextStartTicks);
                    }

                    string? newLeaseOwner = row?.leaseOwner;
                    long newLeaseUntil = row?.leaseUntilTicks ?? 0;
                    string? newActiveLeases = row?.activeLeases;

                    if (item.MaxConcurrency == 1)
                    {
                        if (row is not null && row.leaseUntilTicks > nowTicks)
                            maxBlockedTicks = Math.Max(maxBlockedTicks, row.leaseUntilTicks);
                        newLeaseOwner = owner;
                        newLeaseUntil = expires;
                        newActiveLeases = null;
                    }
                    else if (item.MaxConcurrency > 1)
                    {
                        var active = ParseActiveLeases(row?.activeLeases, nowTicks);
                        if (active.Count >= item.MaxConcurrency)
                            maxBlockedTicks = Math.Max(maxBlockedTicks, active.Min(a => a.ExpiresTicks));
                        active.Add((owner, expires));
                        newLeaseOwner = null;
                        newLeaseUntil = 0;
                        newActiveLeases = FormatActiveLeases(active);
                    }

                    var (windowBlockedTicks, newWindowStart, newWindowEnd, newUsed) =
                        EvaluateBudget(item, row, now);
                    if (windowBlockedTicks > nowTicks)
                        maxBlockedTicks = Math.Max(maxBlockedTicks, windowBlockedTicks);

                    var newNextStart = Math.Max(row?.nextStartTicks ?? 0, (now + item.MinimumInterval).UtcTicks);
                    plans[idx] = (hashedKey, item, row, newLeaseOwner, newLeaseUntil, newActiveLeases,
                        newNextStart, newWindowStart, newWindowEnd, newUsed);
                }

                if (maxBlockedTicks > nowTicks)
                    return new(null, new DateTimeOffset(maxBlockedTicks, TimeSpan.Zero));

                var details = new Dictionary<string, IntegrationReservedScope>(ordered.Length, StringComparer.Ordinal);
                foreach (var p in plans)
                {
                    details[p.HashedKey] = new IntegrationReservedScope(
                        p.Scope.Key, p.HashedKey, p.Scope.MaxConcurrency, p.Scope.ReservedCost,
                        p.NewWindowStartTicks, p.Scope.WindowKind);

                    if (p.Row is not null)
                    {
                        var changed = await states.Where(s => s.scopeKey == p.HashedKey && s.revision == p.Row.revision)
                            .ExecuteUpdateAsync(s => s
                                .SetProperty(x => x.revision, x => x.revision + 1)
                                .SetProperty(x => x.leaseOwner, p.NewLeaseOwner)
                                .SetProperty(x => x.leaseUntilTicks, p.NewLeaseUntilTicks)
                                .SetProperty(x => x.activeLeases, p.NewActiveLeases)
                                .SetProperty(x => x.nextStartTicks, p.NewNextStartTicks)
                                .SetProperty(x => x.windowStartTicks, p.NewWindowStartTicks)
                                .SetProperty(x => x.windowEndTicks, p.NewWindowEndTicks)
                                .SetProperty(x => x.usedUnits, p.NewUsedUnits), token);
                        if (changed != 1)
                            return null; // Dispose rolls back the other scopes before retrying.
                    }
                    else
                    {
                        var entry = states.Add(new IntegrationScopeState
                        {
                            scopeKey = p.HashedKey,
                            leaseOwner = p.NewLeaseOwner,
                            leaseUntilTicks = p.NewLeaseUntilTicks,
                            activeLeases = p.NewActiveLeases,
                            nextStartTicks = p.NewNextStartTicks,
                            windowStartTicks = p.NewWindowStartTicks,
                            windowEndTicks = p.NewWindowEndTicks,
                            usedUnits = p.NewUsedUnits,
                            revision = 1
                        });
                        try
                        {
                            await db.SaveChangesAsync(token);
                        }
                        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry })
                        {
                            return null; // Concurrent initialization of this primary key.
                        }
                        finally
                        {
                            entry.State = EntityState.Detached;
                        }
                    }
                }

                await tx.CommitAsync(token);
                return new(new IntegrationScopeLease(owner, Array.AsReadOnly(keys), expiresAt) { Details = details }, null);
            }, ct);

            if (result is not null)
            {
                var now = clock.GetUtcNow();
                return result.Lease is not null && now >= expiresAt ? new(null, now) : result;
            }
        }

        return new(null, clock.GetUtcNow().AddSeconds(1));
    }

    // Reserve/complete do not authorize mutation by themselves. The transport must fence sends
    // against the durable attempt, observe ExpiresAt, and bound the request to the remaining lease.
    internal Task CompleteAsync(IntegrationScopeLease lease, DateTimeOffset? cooldownUntil, CancellationToken ct) =>
        CompleteAsync(lease, settlements: null, globalCooldownUntil: cooldownUntil, ct);

    internal async Task CompleteAsync(
        IntegrationScopeLease lease,
        IReadOnlyDictionary<string, IntegrationScopeSettlement>? settlements,
        DateTimeOffset? globalCooldownUntil,
        CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();
        var states = db.integrationScopeStates;
        var globalCooldownTicks = globalCooldownUntil?.UtcTicks ?? 0;

        await db.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            var nowTicks = clock.GetUtcNow().UtcTicks;
            foreach (var key in lease.ScopeKeys)
            {
                lease.Details.TryGetValue(key, out var detail);
                IntegrationScopeSettlement? settlement = null;
                if (settlements is not null)
                {
                    if (!settlements.TryGetValue(key, out settlement) && detail is not null)
                        settlements.TryGetValue(detail.RawKey, out settlement);
                }

                var targetCooldown = Math.Max(globalCooldownTicks, settlement?.CooldownUntil?.UtcTicks ?? 0);
                var maxConcurrency = detail?.MaxConcurrency ?? 1;

                for (var attempt = 0; attempt < MaxContentionAttempts; attempt++)
                {
                    await using var tx = await db.Database.BeginTransactionAsync(token);
                    var row = await states.AsNoTracking().FirstOrDefaultAsync(s => s.scopeKey == key, token);
                    if (row is null)
                    {
                        await tx.CommitAsync(token);
                        break;
                    }

                    var newLeaseOwner = row.leaseOwner;
                    var newLeaseUntil = row.leaseUntilTicks;
                    var newActiveLeases = row.activeLeases;
                    var changed = false;

                    // A late response can extend cooldown or settle cost, but can never release a successor's lease.
                    if (maxConcurrency == 1 && row.leaseOwner == lease.Owner)
                    {
                        newLeaseOwner = null;
                        newLeaseUntil = 0;
                        changed = true;
                    }
                    else if (maxConcurrency > 1 && row.activeLeases is not null &&
                             row.activeLeases.Contains(FormatActiveLeaseMarker(lease.Owner), StringComparison.Ordinal))
                    {
                        var active = ParseActiveLeases(row.activeLeases, nowTicks)
                            .Where(a => !string.Equals(a.Owner, lease.Owner, StringComparison.Ordinal))
                            .ToList();
                        newActiveLeases = FormatActiveLeases(active);
                        changed = true;
                    }

                    var newCooldown = row.cooldownUntilTicks;
                    if (targetCooldown > newCooldown)
                    {
                        newCooldown = targetCooldown;
                        changed = true;
                    }

                    var newWindowStart = row.windowStartTicks;
                    var newWindowEnd = row.windowEndTicks;
                    var newUsed = row.usedUnits;

                    if (settlement?.ActualCost is { } actualCost && detail is not null &&
                        detail.WindowKind != IntegrationScopeWindowKind.None)
                    {
                        var delta = actualCost - detail.ReservedCost;
                        if (delta != 0)
                        {
                            // Apply settlement only if still in the same window (or token bucket).
                            if (detail.WindowKind == IntegrationScopeWindowKind.TokenBucket ||
                                row.windowStartTicks == detail.WindowStartTicks)
                            {
                                newUsed = Math.Max(0, row.usedUnits + delta);
                                if (detail.WindowKind == IntegrationScopeWindowKind.TokenBucket &&
                                    newWindowStart == 0 && newUsed > 0)
                                    newWindowStart = nowTicks;
                                changed = true;
                            }
                        }
                    }

                    var newObsLimit = row.observedLimit;
                    var newObsRem = row.observedRemaining;
                    var newObsAt = row.observedAtTicks;
                    if (settlement?.ObservedLimit is not null || settlement?.ObservedRemaining is not null)
                    {
                        if (settlement.ObservedLimit is not null)
                            newObsLimit = settlement.ObservedLimit;
                        if (settlement.ObservedRemaining is not null)
                            newObsRem = settlement.ObservedRemaining;
                        newObsAt = Math.Max(row.observedAtTicks, nowTicks);
                        changed = true;
                    }
                    if (settlement?.ObservedResetAt is { } obsReset && obsReset.UtcTicks > newWindowEnd)
                    {
                        newWindowEnd = obsReset.UtcTicks;
                        changed = true;
                    }

                    if (!changed)
                    {
                        await tx.CommitAsync(token);
                        break;
                    }

                    var updated = await states.Where(s => s.scopeKey == key && s.revision == row.revision)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(x => x.revision, x => x.revision + 1)
                            .SetProperty(x => x.leaseOwner, newLeaseOwner)
                            .SetProperty(x => x.leaseUntilTicks, newLeaseUntil)
                            .SetProperty(x => x.activeLeases, newActiveLeases)
                            .SetProperty(x => x.cooldownUntilTicks, newCooldown)
                            .SetProperty(x => x.windowStartTicks, newWindowStart)
                            .SetProperty(x => x.windowEndTicks, newWindowEnd)
                            .SetProperty(x => x.usedUnits, newUsed)
                            .SetProperty(x => x.observedLimit, newObsLimit)
                            .SetProperty(x => x.observedRemaining, newObsRem)
                            .SetProperty(x => x.observedAtTicks, newObsAt), token);
                    if (updated == 1)
                    {
                        await tx.CommitAsync(token);
                        break;
                    }
                }
            }
        }, ct);
    }

    /// <summary>Records externally observed dynamic quota values (e.g. Ozon /v4/product/info/limit).</summary>
    internal async Task RecordObservedQuotaAsync(
        string rawScopeKey,
        long? observedLimit,
        long? observedRemaining,
        DateTimeOffset? resetAt,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawScopeKey);
        var hashedKey = HashScopeKey(rawScopeKey);
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();
        var states = db.integrationScopeStates;

        await db.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            var nowTicks = clock.GetUtcNow().UtcTicks;
            var resetTicks = resetAt?.UtcTicks ?? 0;

            for (var attempt = 0; attempt < MaxContentionAttempts; attempt++)
            {
                await using var tx = await db.Database.BeginTransactionAsync(token);
                var row = await states.AsNoTracking().FirstOrDefaultAsync(s => s.scopeKey == hashedKey, token);
                if (row is not null)
                {
                    var newEnd = resetTicks > row.windowEndTicks ? resetTicks : row.windowEndTicks;
                    var newStart = row.windowStartTicks == 0 ? nowTicks : row.windowStartTicks;
                    var updated = await states.Where(s => s.scopeKey == hashedKey && s.revision == row.revision)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(x => x.revision, x => x.revision + 1)
                            .SetProperty(x => x.windowStartTicks, newStart)
                            .SetProperty(x => x.windowEndTicks, newEnd)
                            .SetProperty(x => x.observedLimit, observedLimit ?? row.observedLimit)
                            .SetProperty(x => x.observedRemaining, observedRemaining ?? row.observedRemaining)
                            .SetProperty(x => x.observedAtTicks, nowTicks), token);
                    if (updated == 1)
                    {
                        await tx.CommitAsync(token);
                        return;
                    }
                }
                else
                {
                    var entry = states.Add(new IntegrationScopeState
                    {
                        scopeKey = hashedKey,
                        revision = 1,
                        windowStartTicks = nowTicks,
                        windowEndTicks = resetTicks,
                        observedLimit = observedLimit,
                        observedRemaining = observedRemaining,
                        observedAtTicks = nowTicks
                    });
                    try
                    {
                        await db.SaveChangesAsync(token);
                        await tx.CommitAsync(token);
                        return;
                    }
                    catch (DbUpdateException ex) when (ex.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry })
                    {
                        // Concurrent insert; retry update.
                    }
                    finally
                    {
                        entry.State = EntityState.Detached;
                    }
                }
            }
        }, ct);
    }

    private static (long BlockedUntilTicks, long WindowStartTicks, long WindowEndTicks, long UsedUnits) EvaluateBudget(
        IntegrationScope item, IntegrationScopeState? row, DateTimeOffset now)
    {
        var nowTicks = now.UtcTicks;
        switch (item.WindowKind)
        {
            case IntegrationScopeWindowKind.TokenBucket:
            {
                var refillTicks = item.RefillInterval!.Value.Ticks;
                long windowStart;
                long used;
                if (row is null || row.windowStartTicks == 0)
                {
                    windowStart = nowTicks;
                    used = 0;
                }
                else
                {
                    var elapsed = Math.Max(0, nowTicks - row.windowStartTicks);
                    var refilled = elapsed / refillTicks;
                    used = Math.Max(0, row.usedUnits - refilled);
                    windowStart = used == 0 ? nowTicks : row.windowStartTicks + refilled * refillTicks;
                }

                if (used + item.ReservedCost > item.Burst)
                {
                    var deficit = used + item.ReservedCost - item.Burst;
                    var blockedUntil = windowStart + deficit * refillTicks;
                    return (blockedUntil, windowStart, 0, used);
                }
                return (0, windowStart, 0, used + item.ReservedCost);
            }

            case IntegrationScopeWindowKind.FixedWindow:
            case IntegrationScopeWindowKind.CalendarReset:
            {
                long windowStart;
                long windowEnd;
                long used;
                if (row is null || row.windowEndTicks <= nowTicks)
                {
                    windowStart = nowTicks;
                    windowEnd = item.WindowKind == IntegrationScopeWindowKind.FixedWindow
                        ? nowTicks + item.Period!.Value.Ticks
                        : ComputeNextCalendarResetTicks(now, item.ResetTimeZoneId!, item.ResetHourOfDay);
                    used = 0;
                }
                else
                {
                    windowStart = row.windowStartTicks;
                    windowEnd = row.windowEndTicks;
                    used = row.usedUnits;
                }

                var obsFresh = row is not null && row.observedAtTicks >= windowStart;
                long? obsLimit = obsFresh ? row!.observedLimit : null;
                long? obsRem = obsFresh ? row!.observedRemaining : null;
                long? effectiveLimit = item.Allowance.HasValue && obsLimit.HasValue
                    ? Math.Min(item.Allowance.Value, obsLimit.Value)
                    : item.Allowance ?? obsLimit;

                if (obsRem is <= 0)
                    return (windowEnd, windowStart, windowEnd, used);
                if (effectiveLimit.HasValue && used + item.ReservedCost > effectiveLimit.Value)
                    return (windowEnd, windowStart, windowEnd, used);

                return (0, windowStart, windowEnd, used + item.ReservedCost);
            }

            default:
                return (0, row?.windowStartTicks ?? 0, row?.windowEndTicks ?? 0, row?.usedUnits ?? 0);
        }
    }

    internal static long ComputeNextCalendarResetTicks(DateTimeOffset now, string timeZoneId, int resetHourOfDay)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(now, tz);
        var todayReset = DateTime.SpecifyKind(localNow.Date.AddHours(resetHourOfDay), DateTimeKind.Unspecified);
        var nextResetLocal = localNow.DateTime >= todayReset ? todayReset.AddDays(1) : todayReset;
        var nextResetUtc = TimeZoneInfo.ConvertTimeToUtc(nextResetLocal, tz);
        return new DateTimeOffset(nextResetUtc, TimeSpan.Zero).UtcTicks;
    }

    private static bool OwnsLease(IntegrationScopeState row, int maxConcurrency, string owner, long expires) =>
        maxConcurrency == 1
            ? row.leaseOwner == owner && row.leaseUntilTicks == expires
            : row.activeLeases is not null && row.activeLeases.Contains($"|{owner}:{expires}|", StringComparison.Ordinal);

    private static Task<int> ReleaseOwnedLeaseAsync(
        DbSet<IntegrationScopeState> states, IntegrationScopeState row, int maxConcurrency, string owner, long nowTicks, CancellationToken ct)
    {
        if (maxConcurrency == 1)
        {
            return states.Where(s => s.scopeKey == row.scopeKey && s.leaseOwner == owner)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.revision, x => x.revision + 1)
                    .SetProperty(x => x.leaseUntilTicks, 0L)
                    .SetProperty(x => x.leaseOwner, (string?)null), ct);
        }

        var remaining = ParseActiveLeases(row.activeLeases, nowTicks)
            .Where(a => !string.Equals(a.Owner, owner, StringComparison.Ordinal))
            .ToList();
        return states.Where(s => s.scopeKey == row.scopeKey && s.revision == row.revision)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.revision, x => x.revision + 1)
                .SetProperty(x => x.activeLeases, FormatActiveLeases(remaining)), ct);
    }

    internal static List<(string Owner, long ExpiresTicks)> ParseActiveLeases(string? raw, long nowTicks)
    {
        var list = new List<(string Owner, long ExpiresTicks)>();
        if (string.IsNullOrEmpty(raw))
            return list;
        foreach (var segment in raw.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = segment.IndexOf(':');
            if (colon <= 0 || colon >= segment.Length - 1)
                continue;
            if (long.TryParse(segment.AsSpan(colon + 1), out var exp) && exp > nowTicks)
                list.Add((segment[..colon], exp));
        }
        return list;
    }

    private static string? FormatActiveLeases(List<(string Owner, long ExpiresTicks)> leases) =>
        leases.Count == 0
            ? null
            : "|" + string.Join("|", leases.Select(l => $"{l.Owner}:{l.ExpiresTicks}")) + "|";

    /// <summary>Reads current durable admission state for diagnostics and /limits observation.</summary>
    internal async Task<IReadOnlyList<IntegrationScopeSnapshot>> GetSnapshotsAsync(
        IReadOnlyList<string> rawScopeKeys, CancellationToken ct)
    {
        if (rawScopeKeys.Count == 0)
            return [];
        var map = rawScopeKeys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(HashScopeKey, k => k, StringComparer.Ordinal);
        if (map.Count == 0)
            return [];

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();
        var nowTicks = clock.GetUtcNow().UtcTicks;
        var hashedKeys = map.Keys.ToArray();
        var rows = await db.integrationScopeStates.AsNoTracking()
            .Where(s => hashedKeys.Contains(s.scopeKey))
            .ToListAsync(ct);

        return rows.Select(r =>
        {
            var activeCount = r.activeLeases is not null
                ? ParseActiveLeases(r.activeLeases, nowTicks).Count
                : (r.leaseOwner is not null && r.leaseUntilTicks > nowTicks ? 1 : 0);
            return new IntegrationScopeSnapshot(
                RawKey: map[r.scopeKey],
                HashedKey: r.scopeKey,
                ActiveLeases: activeCount,
                CooldownUntil: r.cooldownUntilTicks > nowTicks ? new DateTimeOffset(r.cooldownUntilTicks, TimeSpan.Zero) : null,
                NextStartAt: r.nextStartTicks > nowTicks ? new DateTimeOffset(r.nextStartTicks, TimeSpan.Zero) : null,
                UsedUnits: r.usedUnits,
                WindowEndAt: r.windowEndTicks > nowTicks ? new DateTimeOffset(r.windowEndTicks, TimeSpan.Zero) : null,
                ObservedLimit: r.observedLimit,
                ObservedRemaining: r.observedRemaining);
        }).ToList();
    }
}
