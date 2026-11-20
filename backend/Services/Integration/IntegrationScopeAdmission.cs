using Delegat.Models.Integration;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace Delegat.Services.Integration;

// Keys come from host policy, never from a connection ID or credential substitute.
// MinimumInterval spaces reservations, not physical sends; provider quota rules belong in the transport policy.
internal sealed record IntegrationScope(string Key, TimeSpan MinimumInterval);
internal sealed record IntegrationScopeLease(string Owner, IReadOnlyList<string> ScopeKeys, DateTimeOffset ExpiresAt);
internal sealed record IntegrationScopeAdmissionResult(IntegrationScopeLease? Lease, DateTimeOffset? RetryAt);

/// <summary>Atomically reserves related outbound scopes and persists waits without holding a database transaction across I/O.</summary>
internal sealed class IntegrationScopeAdmission(IServiceScopeFactory scopeFactory, TimeProvider clock)
{
    private const int MaxContentionAttempts = 3;

    internal async Task<IntegrationScopeAdmissionResult> TryReserveAsync(
        IReadOnlyList<IntegrationScope> scopes, TimeSpan leaseLifetime, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        if (scopes.Count == 0 || scopes.Any(s => string.IsNullOrWhiteSpace(s.Key) || s.MinimumInterval < TimeSpan.Zero))
            throw new ArgumentException("At least one valid scope with non-negative spacing is required.", nameof(scopes));
        if (leaseLifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseLifetime));

        var ordered = scopes.Select(s => (Key: IntegrationCoordinator.ComputeHash("integration-scope:v1:" + s.Key), s.MinimumInterval))
            .OrderBy(s => s.Key, StringComparer.Ordinal).ToArray();
        var keys = ordered.Select(s => s.Key).ToArray();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length)
            throw new ArgumentException("Scope keys must be unique within a reservation.", nameof(scopes));

        var lease = new IntegrationScopeLease(Guid.NewGuid().ToString("N"), Array.AsReadOnly(keys), clock.GetUtcNow() + leaseLifetime);
        var expires = lease.ExpiresAt.UtcTicks;
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();
        var states = db.integrationScopeStates;
        var strategy = db.Database.CreateExecutionStrategy();

        for (var i = 0; i < MaxContentionAttempts; i++)
        {
            var result = await strategy.ExecuteAsync<IntegrationScopeAdmissionResult?>(async token =>
            {
                var now = clock.GetUtcNow();
                if (now >= lease.ExpiresAt)
                    return new(null, now);

                await using var tx = await db.Database.BeginTransactionAsync(token);
                var rows = await states.AsNoTracking().Where(s => keys.Contains(s.scopeKey)).ToDictionaryAsync(s => s.scopeKey, token);
                if (rows.Count == keys.Length && rows.Values.All(s => s.leaseOwner == lease.Owner && s.leaseUntilTicks == expires))
                {
                    // Our commit landed, but a late provider response can have extended a cooldown since then.
                    if (!rows.Values.Any(s => s.cooldownUntilTicks > now.UtcTicks))
                        return new(lease, null);

                    var blockedUntil = rows.Values.Max(s => Math.Max(s.nextStartTicks, s.cooldownUntilTicks));
                    // No lease was handed out; free all of its scopes without refunding their spacing.
                    foreach (var key in keys)
                        await ReleaseScopeAsync(states.Where(s => s.scopeKey == key), lease.Owner, token);

                    await tx.CommitAsync(token);
                    return new(null, new DateTimeOffset(blockedUntil, TimeSpan.Zero));
                }

                var retryTicks = rows.Values.Select(s => Math.Max(s.leaseUntilTicks, Math.Max(s.nextStartTicks, s.cooldownUntilTicks)))
                    .DefaultIfEmpty().Max();
                if (retryTicks > now.UtcTicks)
                    return new(null, new DateTimeOffset(retryTicks, TimeSpan.Zero));

                foreach (var item in ordered)
                {
                    var nextStart = (now + item.MinimumInterval).UtcTicks;
                    if (rows.TryGetValue(item.Key, out var row))
                    {
                        var changed = await states.Where(s => s.scopeKey == item.Key && s.revision == row.revision)
                            .ExecuteUpdateAsync(s => s
                                .SetProperty(x => x.revision, x => x.revision + 1)
                                .SetProperty(x => x.leaseOwner, lease.Owner)
                                .SetProperty(x => x.leaseUntilTicks, expires)
                                .SetProperty(x => x.nextStartTicks, nextStart), token);
                        if (changed != 1)
                            return null; // Dispose rolls back the other scopes before retrying.
                    }
                    else
                    {
                        var entry = states.Add(new IntegrationScopeState
                        {
                            scopeKey = item.Key,
                            leaseOwner = lease.Owner,
                            leaseUntilTicks = expires,
                            nextStartTicks = nextStart,
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
                return new(lease, null);
            }, ct);

            if (result is not null)
            {
                var now = clock.GetUtcNow();
                return result.Lease is not null && now >= lease.ExpiresAt ? new(null, now) : result;
            }
        }

        return new(null, clock.GetUtcNow().AddSeconds(1));
    }

    // Reserve/complete do not authorize mutation by themselves. The transport must fence sends
    // against the durable attempt, observe ExpiresAt, and bound the request to the remaining lease.
    internal async Task CompleteAsync(IntegrationScopeLease lease, DateTimeOffset? cooldownUntil, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();
        var cooldown = cooldownUntil?.UtcTicks ?? 0;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(token);
            foreach (var key in lease.ScopeKeys)
            {
                var state = db.integrationScopeStates.Where(s => s.scopeKey == key);
                await ReleaseScopeAsync(state, lease.Owner, token);

                // A late response can extend cooldown but cannot release a successor's lease.
                if (cooldown > 0)
                {
                    await state.Where(s => s.cooldownUntilTicks < cooldown)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(x => x.revision, x => x.revision + 1)
                            .SetProperty(x => x.cooldownUntilTicks, cooldown), token);
                }
            }
            await tx.CommitAsync(token);
        }, ct);
    }

    private static Task<int> ReleaseScopeAsync(IQueryable<IntegrationScopeState> state, string owner, CancellationToken ct) =>
        state.Where(s => s.leaseOwner == owner)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.revision, x => x.revision + 1)
                .SetProperty(x => x.leaseUntilTicks, 0L)
                .SetProperty(x => x.leaseOwner, (string?)null), ct);
}
