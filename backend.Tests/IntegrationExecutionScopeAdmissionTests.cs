using System.Data.Common;
using Delegat.Services.Integration;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Delegat.Tests;

public sealed class IntegrationExecutionScopeAdmissionTests
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static ServiceProvider BuildProvider(Func<DataContext> factory, TimeProvider clock) =>
        new ServiceCollection()
            .AddScoped(_ => factory())
            .AddSingleton(clock)
            .AddSingleton<IntegrationScopeAdmission>()
            .BuildServiceProvider();

    private static readonly TimeSpan LeaseTtl = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task RelatedScopes_DenialDoesNotConsumeOtherScopes()
    {
        var clock = new ManualClock();
        var (schemaDb, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = schemaDb;
        var dsn = TestDbFactory.SqliteDsn(schemaDb);
        await using var provider = BuildProvider(() => TestDbFactory.CreateSqliteContext(dsn), clock);
        var admission = provider.GetRequiredService<IntegrationScopeAdmission>();
        var a = new IntegrationScope("scope-a", TimeSpan.Zero);
        var b = new IntegrationScope("scope-b", TimeSpan.Zero);
        var c = new IntegrationScope("scope-c", TimeSpan.Zero);

        var bOnly = await admission.TryReserveAsync([b], LeaseTtl, CancellationToken.None);
        Assert.NotNull(bOnly.Lease);

        var denied = await admission.TryReserveAsync([a, b], LeaseTtl, CancellationToken.None);
        Assert.Null(denied.Lease);
        Assert.Equal(clock.Now.Add(LeaseTtl), denied.RetryAt);

        var ac = await admission.TryReserveAsync([a, c], LeaseTtl, CancellationToken.None);
        Assert.NotNull(ac.Lease);

        await using var verify = TestDbFactory.CreateSqliteContext(dsn);
        var rows = await verify.integrationScopeStates.AsNoTracking().ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal(64, r.scopeKey.Length));
        Assert.All(rows, r => Assert.DoesNotContain(new[] { "scope-a", "scope-b", "scope-c" }, raw => r.scopeKey == raw));
    }

    [Fact]
    public async Task Complete_ReleasesOwnerButKeepsSpacingAndLongCooldownAcrossRestart()
    {
        var clock = new ManualClock();
        var (schemaDb, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = schemaDb;
        var dsn = TestDbFactory.SqliteDsn(schemaDb);
        await using var provider1 = BuildProvider(() => TestDbFactory.CreateSqliteContext(dsn), clock);
        var admission1 = provider1.GetRequiredService<IntegrationScopeAdmission>();
        var t0 = clock.Now;
        var scopeA = new IntegrationScope("scope-a", TimeSpan.FromSeconds(20));

        var first = await admission1.TryReserveAsync([scopeA], LeaseTtl, CancellationToken.None);
        Assert.NotNull(first.Lease);
        await admission1.CompleteAsync(first.Lease!, t0.AddHours(3).AddTicks(1), CancellationToken.None);
        // A shorter replayed completion cannot shorten the durable cooldown.
        await admission1.CompleteAsync(first.Lease!, t0.AddMinutes(1), CancellationToken.None);

        // A new provider over the same DSN simulates a restart.
        await using var provider2 = BuildProvider(() => TestDbFactory.CreateSqliteContext(dsn), clock);
        var admission2 = provider2.GetRequiredService<IntegrationScopeAdmission>();

        var blocked = await admission2.TryReserveAsync([scopeA], LeaseTtl, CancellationToken.None);
        Assert.Null(blocked.Lease);
        Assert.Equal(t0.AddHours(3).AddTicks(1), blocked.RetryAt);

        clock.Now = t0.AddHours(3); // one tick before the cooldown ends
        var stillBlocked = await admission2.TryReserveAsync([scopeA], LeaseTtl, CancellationToken.None);
        Assert.Null(stillBlocked.Lease);

        clock.Now = t0.AddHours(3).AddTicks(1);
        var reserved = await admission2.TryReserveAsync([scopeA], LeaseTtl, CancellationToken.None);
        Assert.NotNull(reserved.Lease);

        var scopeB = new IntegrationScope("scope-b", TimeSpan.FromSeconds(20));
        var start = clock.Now;
        var b1 = await admission2.TryReserveAsync([scopeB], LeaseTtl, CancellationToken.None);
        Assert.NotNull(b1.Lease);
        await admission2.CompleteAsync(b1.Lease!, null, CancellationToken.None);
        var bBlocked = await admission2.TryReserveAsync([scopeB], LeaseTtl, CancellationToken.None);
        Assert.Null(bBlocked.Lease);
        Assert.Equal(start.AddSeconds(20), bBlocked.RetryAt);
        clock.Now = start.AddSeconds(20);
        var b2 = await admission2.TryReserveAsync([scopeB], LeaseTtl, CancellationToken.None);
        Assert.NotNull(b2.Lease);
    }

    [Fact]
    public async Task ExpiredOwner_CompletionCannotReleaseNewOwner()
    {
        var clock = new ManualClock();
        var (schemaDb, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = schemaDb;
        var dsn = TestDbFactory.SqliteDsn(schemaDb);
        await using var provider = BuildProvider(() => TestDbFactory.CreateSqliteContext(dsn), clock);
        var admission = provider.GetRequiredService<IntegrationScopeAdmission>();
        var a = new IntegrationScope("scope-a", TimeSpan.Zero);
        var t0 = clock.Now;

        var first = await admission.TryReserveAsync([a], LeaseTtl, CancellationToken.None);
        Assert.NotNull(first.Lease);

        clock.Now = t0.AddMinutes(2);
        var second = await admission.TryReserveAsync([a], LeaseTtl, CancellationToken.None);
        Assert.NotNull(second.Lease);
        Assert.NotEqual(first.Lease!.Owner, second.Lease!.Owner);

        // The expired owner's late completion may extend the cooldown but cannot release the new lease.
        await admission.CompleteAsync(first.Lease!, t0.AddMinutes(3), CancellationToken.None);
        var blockedByLease = await admission.TryReserveAsync([a], LeaseTtl, CancellationToken.None);
        Assert.Null(blockedByLease.Lease);
        Assert.Equal(t0.AddMinutes(4), blockedByLease.RetryAt);

        await admission.CompleteAsync(second.Lease!, null, CancellationToken.None);
        var blockedByCooldown = await admission.TryReserveAsync([a], LeaseTtl, CancellationToken.None);
        Assert.Null(blockedByCooldown.Lease);
        Assert.Equal(t0.AddMinutes(3), blockedByCooldown.RetryAt);

        clock.Now = t0.AddMinutes(3);
        var reserved = await admission.TryReserveAsync([a], LeaseTtl, CancellationToken.None);
        Assert.NotNull(reserved.Lease);
    }

    [Fact]
    public async Task ReservationCommitFails_RollsBackAllScopes()
    {
        var clock = new ManualClock();
        var (schemaDb, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = schemaDb;
        var dsn = TestDbFactory.SqliteDsn(schemaDb);
        var failOnce = new FailBeforeCommitInterceptor();
        await using var provider = BuildProvider(() => TestDbFactory.CreateSqliteContext(dsn, interceptors: [failOnce]), clock);
        var admission = provider.GetRequiredService<IntegrationScopeAdmission>();
        var a = new IntegrationScope("scope-a", TimeSpan.Zero);
        var b = new IntegrationScope("scope-b", TimeSpan.Zero);

        await Assert.ThrowsAsync<InvalidOperationException>(() => admission.TryReserveAsync([a, b], LeaseTtl, CancellationToken.None));
        Assert.Empty(await schemaDb.integrationScopeStates.AsNoTracking().ToListAsync());
        var second = await admission.TryReserveAsync([a, b], LeaseTtl, CancellationToken.None);
        Assert.NotNull(second.Lease);
        Assert.Equal(2, await schemaDb.integrationScopeStates.CountAsync());
    }

    private sealed class FailBeforeCommitInterceptor : DbTransactionInterceptor
    {
        private int _armed = 1;
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            Interlocked.Exchange(ref _armed, 0) == 1
                ? ValueTask.FromException<InterceptionResult>(new InvalidOperationException("simulated failed commit"))
                : ValueTask.FromResult(result);
    }

    private sealed class FailOnceCommitInterceptor(Func<Task>? afterCommit = null) : DbTransactionInterceptor
    {
        private int _armed = 1;
        public override async Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _armed, 0) != 1)
                return;
            if (afterCommit is not null)
                await afterCommit();
            throw new InvalidOperationException("simulated lost commit acknowledgement");
        }
    }

    [Fact]
    public async Task CommitAcknowledgementLost_ReusesCommittedReservation()
    {
        var clock = new ManualClock();
        var (schemaDb, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = schemaDb;
        var dsn = TestDbFactory.SqliteDsn(schemaDb);
        var failOnce = new FailOnceCommitInterceptor();
        // The interceptor attaches only to the service-owned contexts, never to EnsureCreated.
        await using var provider = BuildProvider(
            () => TestDbFactory.CreateSqliteContext(dsn, retryingStrategy: true, interceptors: [failOnce]),
            clock);
        var admission = provider.GetRequiredService<IntegrationScopeAdmission>();
        var a = new IntegrationScope("scope-a", TimeSpan.FromSeconds(20));
        var b = new IntegrationScope("scope-b", TimeSpan.Zero);

        var result = await admission.TryReserveAsync([a, b], LeaseTtl, CancellationToken.None);
        Assert.NotNull(result.Lease);

        await using var verify = TestDbFactory.CreateSqliteContext(dsn);
        var rows = await verify.integrationScopeStates.AsNoTracking().ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(1, r.revision));
        Assert.All(rows, r => Assert.Equal(result.Lease!.Owner, r.leaseOwner));
    }

    [Fact]
    public async Task CommitAcknowledgementLost_DefersForCooldownRecordedAfterCommit()
    {
        var clock = new ManualClock();
        var (schemaDb, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = schemaDb;
        var dsn = TestDbFactory.SqliteDsn(schemaDb);
        await using var otherProvider = BuildProvider(() => TestDbFactory.CreateSqliteContext(dsn), clock);
        var other = otherProvider.GetRequiredService<IntegrationScopeAdmission>();
        var a = new IntegrationScope("scope-a", TimeSpan.Zero);
        var b = new IntegrationScope("scope-b", TimeSpan.Zero);
        var previous = await other.TryReserveAsync([a], LeaseTtl, CancellationToken.None);
        Assert.NotNull(previous.Lease);
        clock.Now = previous.Lease!.ExpiresAt;
        var cooldown = clock.Now.AddSeconds(30); // Shorter than the unpublished replacement lease.
        var failOnce = new FailOnceCommitInterceptor(
            () => other.CompleteAsync(previous.Lease!, cooldown, CancellationToken.None));
        await using var provider = BuildProvider(
            () => TestDbFactory.CreateSqliteContext(dsn, retryingStrategy: true, interceptors: [failOnce]),
            clock);
        var admission = provider.GetRequiredService<IntegrationScopeAdmission>();

        var result = await admission.TryReserveAsync([a, b], LeaseTtl, CancellationToken.None);

        Assert.Null(result.Lease);
        Assert.Equal(cooldown, result.RetryAt);
        var unrelatedToCooldown = await other.TryReserveAsync([b], LeaseTtl, CancellationToken.None);
        Assert.NotNull(unrelatedToCooldown.Lease);
        await other.CompleteAsync(unrelatedToCooldown.Lease!, null, CancellationToken.None);
        clock.Now = cooldown;
        Assert.NotNull((await other.TryReserveAsync([a, b], LeaseTtl, CancellationToken.None)).Lease);
    }

    [LocalMySqlFact]
    public async Task ConcurrentMySqlClaimers_CannotBothOwnSameScope()
    {
        var clock = new ManualClock();
        await using var mysql = await LocalMySqlDatabase.CreateAsync();
        await using var provider = BuildProvider(() => mysql.CreateContext(), clock);
        var admission = provider.GetRequiredService<IntegrationScopeAdmission>();
        var a = new IntegrationScope("scope-a", TimeSpan.Zero);
        var b = new IntegrationScope("scope-b", TimeSpan.Zero);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = Task.Run(async () =>
        {
            await gate.Task;
            return await admission.TryReserveAsync([a, b], LeaseTtl, CancellationToken.None);
        });
        var second = Task.Run(async () =>
        {
            await gate.Task;
            return await admission.TryReserveAsync([b, a], LeaseTtl, CancellationToken.None);
        });
        gate.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, results.Count(r => r.Lease is not null));
        var winner = results.Single(r => r.Lease is not null);
        var loser = results.Single(r => r.Lease is null);
        Assert.Equal(winner.Lease!.ExpiresAt, loser.RetryAt);

        await admission.CompleteAsync(winner.Lease!, null, CancellationToken.None);
        var again = await admission.TryReserveAsync([a, b], LeaseTtl, CancellationToken.None);
        Assert.NotNull(again.Lease);

        await using var verify = mysql.CreateContext();
        var rows = await verify.integrationScopeStates.AsNoTracking().ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(again.Lease!.Owner, r.leaseOwner));
    }
}
