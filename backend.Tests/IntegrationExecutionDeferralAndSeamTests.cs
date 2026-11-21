using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using Delegat.Marketplace.SDK;
using Delegat.Models;
using Delegat.Models.Integration;
using Delegat.Services;
using Delegat.Services.Integration;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public sealed class IntegrationExecutionDeferralAndSeamTests
{
    private const string Payload = """{"sku":"deferred-1"}""";
    private static readonly IntegrationScope SharedScope = new("fixture-provider-seller", TimeSpan.Zero);

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            send(request, ct);
    }

    private sealed class DelegateDispatcher(
        Func<DispatchStageRequest, CancellationToken, Task<IntegrationDispatchResult>>? onDispatch = null,
        Func<DispatchStageRequest, CancellationToken, Task<IntegrationDispatchResult>>? onConfirm = null)
        : IIntegrationStageDispatcher
    {
        public int DispatchCalls { get; private set; }
        public int ConfirmCalls { get; private set; }

        public Task<IntegrationDispatchResult> DispatchAsync(DispatchStageRequest request, CancellationToken ct)
        {
            DispatchCalls++;
            return onDispatch is not null
                ? onDispatch(request, ct)
                : Task.FromResult(new IntegrationDispatchResult(IntegrationDispatchOutcome.Applied));
        }

        public Task<IntegrationDispatchResult> ConfirmAsync(DispatchStageRequest request, CancellationToken ct)
        {
            ConfirmCalls++;
            return onConfirm is not null
                ? onConfirm(request, ct)
                : Task.FromResult(new IntegrationDispatchResult(IntegrationDispatchOutcome.Applied));
        }
    }

    private static IntegrationOutboundPolicy? ResolveFixture(IntegrationTransportRequest request) =>
        request.Url == new Uri("https://provider.test/items") && request.Method is "POST" or "GET"
            ? new(request.Method == "POST", [SharedScope], 2048)
            : null;

    private static IntegrationTransportRequest FixtureRequest(bool effectful = true) =>
        new(effectful ? "POST" : "GET", new Uri("https://provider.test/items"),
            new Dictionary<string, string>(), effectful ? Payload : null,
            new IntegrationRequestPolicy("untrusted-hint", IsEffectful: effectful));

    private static (ServiceProvider Provider, IntegrationExecutionWorker Worker) BuildStack(
        Func<DataContext> createContext,
        TimeProvider clock,
        IIntegrationStageDispatcher dispatcher,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? httpSend = null)
    {
        var services = new ServiceCollection()
            .AddScoped(_ => createContext())
            .AddSingleton(clock)
            .AddSingleton<IOperationLogger, NoopOperationLogger>()
            .AddSingleton(dispatcher)
            .AddSingleton<IntegrationScopeAdmission>()
            .AddSingleton<IntegrationHttpTransport>();
        services.AddHttpClient(IntegrationHttpTransport.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(httpSend ?? ((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }))));
        var provider = services.BuildServiceProvider();
        var worker = new IntegrationExecutionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<IntegrationExecutionWorker>.Instance,
            clock);
        return (provider, worker);
    }

    private static IntegrationAdmitRequest AdmitSingle(
        string requestKey = "req-1",
        string itemKey = "item-1",
        int storeId = 1,
        int? connectionId = 7,
        IReadOnlyList<IntegrationStageSeed>? stages = null) =>
        new("export-product", storeId, AuthorUserId: 10, AuthorApiKeyId: null, connectionId,
            requestKey, """{"productId":1}""", "Test op",
            stages ?? [new IntegrationStageSeed(itemKey, "create", "create", 0, Payload)]);

    [Fact]
    public async Task Worker_DeferredBeforeSend_PersistsWaitAndResumesFirstDispatchAtExactRetryAt()
    {
        var clock = new ManualClock();
        var (schemaDb, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = schemaDb;
        var dsn = TestDbFactory.SqliteDsn(schemaDb);

        var httpCalls = 0;
        IntegrationHttpTransport? transport = null;
        var dispatcher = new DelegateDispatcher(
            onDispatch: async (req, ct) =>
            {
                var bound = transport!.Bind(req.TransportAttempt!, ResolveFixture);
                var resp = await bound.SendAsync(FixtureRequest(effectful: true), ct);
                return resp.StatusCode == 200
                    ? new IntegrationDispatchResult(IntegrationDispatchOutcome.Applied, ResponseSummary: "ok")
                    : new IntegrationDispatchResult(IntegrationDispatchOutcome.Unknown);
            },
            onConfirm: (_, _) => throw new InvalidOperationException("Must not enter ConfirmAsync for unsent deferral."));

        await using var built = BuildStack(() => TestDbFactory.CreateSqliteContext(dsn), clock, dispatcher,
            (_, _) =>
            {
                httpCalls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
            }).Provider;
        transport = built.GetRequiredService<IntegrationHttpTransport>();
        var admission = built.GetRequiredService<IntegrationScopeAdmission>();
        var worker = new IntegrationExecutionWorker(
            built.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<IntegrationExecutionWorker>.Instance,
            clock);

        // Put the scope into a 3-hour provider pause before admitting the command.
        var retryAt = clock.Now.AddHours(3);
        var prior = await admission.TryReserveAsync([SharedScope], TimeSpan.FromMinutes(2), CancellationToken.None);
        Assert.NotNull(prior.Lease);
        await admission.CompleteAsync(prior.Lease!, retryAt, CancellationToken.None);

        await using (var admitDb = TestDbFactory.CreateSqliteContext(dsn))
        {
            var coordinator = new IntegrationCoordinator(admitDb, new NoopOperationLogger(), NullLogger<IntegrationCoordinator>.Instance);
            await coordinator.AdmitAsync(AdmitSingle(), v1Enabled: true, CancellationToken.None);
        }

        // First poll: admission denies before any send authorization.
        await worker.ProcessDueStagesAsync(CancellationToken.None);

        Assert.Equal(1, dispatcher.DispatchCalls);
        Assert.Equal(0, dispatcher.ConfirmCalls);
        Assert.Equal(0, httpCalls);

        await using (var verify = TestDbFactory.CreateSqliteContext(dsn))
        {
            var attempt = await verify.integrationAttempts.AsNoTracking().SingleAsync();
            Assert.Equal(IntegrationAttemptOutcome.DeferredBeforeSend, attempt.outcome);
            Assert.Equal(IntegrationEffectCertainty.None, attempt.effectCertainty);
            Assert.Null(attempt.sendAuthorizedAt);
            Assert.NotNull(attempt.completedAt);
            Assert.Equal("transport_admission_deferred", attempt.responseSummary);

            var stage = await verify.integrationStages.AsNoTracking().SingleAsync();
            Assert.Equal(IntegrationStageState.Ready, stage.state);
            Assert.Equal(retryAt.UtcDateTime, stage.nextCheckAt);
            Assert.Null(stage.claimedBy);
            Assert.Null(stage.claimedUntil);

            var cmd = await verify.integrationCommands.AsNoTracking().SingleAsync();
            Assert.Equal(IntegrationCommandState.Executing, cmd.state);
            var op = await verify.operations.AsNoTracking().SingleAsync();
            Assert.Equal(OperationLifecycle.Running, op.status);
            Assert.Equal(0, op.completedItems);
            Assert.Equal(0, op.failedItems);
        }

        // One tick before RetryAt: still not eligible, zero HTTP.
        clock.Now = retryAt.AddTicks(-1);
        await worker.ProcessDueStagesAsync(CancellationToken.None);
        Assert.Equal(1, dispatcher.DispatchCalls);
        Assert.Equal(0, httpCalls);

        // At exact RetryAt: resumes first dispatch, authorizes send, completes command and projection.
        clock.Now = retryAt;
        await worker.ProcessDueStagesAsync(CancellationToken.None);

        Assert.Equal(2, dispatcher.DispatchCalls);
        Assert.Equal(0, dispatcher.ConfirmCalls);
        Assert.Equal(1, httpCalls);

        await using (var verify = TestDbFactory.CreateSqliteContext(dsn))
        {
            var attempts = await verify.integrationAttempts.AsNoTracking().OrderBy(a => a.attemptNumber).ToListAsync();
            Assert.Equal(2, attempts.Count);
            Assert.Null(attempts[0].sendAuthorizedAt);
            Assert.Equal(IntegrationAttemptOutcome.DeferredBeforeSend, attempts[0].outcome);
            Assert.Equal(retryAt.UtcDateTime, attempts[1].sendAuthorizedAt);
            Assert.Equal(IntegrationAttemptOutcome.Applied, attempts[1].outcome);

            var stage = await verify.integrationStages.AsNoTracking().SingleAsync();
            Assert.Equal(IntegrationStageState.Applied, stage.state);
            Assert.Equal(IntegrationCommandState.Completed, (await verify.integrationCommands.AsNoTracking().SingleAsync()).state);
            var op = await verify.operations.AsNoTracking().SingleAsync();
            Assert.Equal(OperationLifecycle.Completed, op.status);
            Assert.Equal(1, op.completedItems);
            Assert.Equal(0, op.failedItems);
        }
    }

    [Fact]
    public async Task Worker_RepeatedAdmissionDeferral_DoesNotExhaustSendRetryBudget()
    {
        var clock = new ManualClock();
        var (schemaDb, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = schemaDb;
        var dsn = TestDbFactory.SqliteDsn(schemaDb);

        var callIndex = 0;
        var dispatcher = new DelegateDispatcher(onDispatch: (_, _) =>
        {
            callIndex++;
            if (callIndex <= 5)
                throw new IntegrationTransportBlockedException("transport_admission_deferred", clock.Now.AddMinutes(1));
            if (callIndex <= 7)
                return Task.FromResult(new IntegrationDispatchResult(IntegrationDispatchOutcome.Rejected, Retryable: true, ResponseSummary: "temp_reject"));
            return Task.FromResult(new IntegrationDispatchResult(IntegrationDispatchOutcome.Applied, ResponseSummary: "applied"));
        });

        var (provider, worker) = BuildStack(() => TestDbFactory.CreateSqliteContext(dsn), clock, dispatcher);
        await using var _p = provider;

        await using (var admitDb = TestDbFactory.CreateSqliteContext(dsn))
        {
            var coordinator = new IntegrationCoordinator(admitDb, new NoopOperationLogger(), NullLogger<IntegrationCoordinator>.Instance);
            await coordinator.AdmitAsync(AdmitSingle(), v1Enabled: true, CancellationToken.None);
        }

        // 5 admission deferrals + 2 retryable rejections + 1 final applied = 8 polls total.
        for (var i = 0; i < 8; i++)
        {
            await worker.ProcessDueStagesAsync(CancellationToken.None);
            clock.Now = clock.Now.AddMinutes(5);
        }

        await using var verify = TestDbFactory.CreateSqliteContext(dsn);
        var stage = await verify.integrationStages.AsNoTracking().SingleAsync();
        Assert.Equal(IntegrationStageState.Applied, stage.state);
        Assert.Equal(8, stage.attemptCount);
        var attempts = await verify.integrationAttempts.AsNoTracking().OrderBy(a => a.attemptNumber).ToListAsync();
        Assert.Equal(5, attempts.Count(a => a.outcome == IntegrationAttemptOutcome.DeferredBeforeSend));
        Assert.Equal(2, attempts.Count(a => a.outcome == IntegrationAttemptOutcome.Rejected));
        Assert.Equal(1, attempts.Count(a => a.outcome == IntegrationAttemptOutcome.Applied));
        Assert.Equal(IntegrationCommandState.Completed, (await verify.integrationCommands.AsNoTracking().SingleAsync()).state);
    }

    [Fact]
    public async Task Worker_AdmissionDeferralDuringConfirmation_StaysOnConfirmationAndPreservesReceipt()
    {
        var clock = new ManualClock();
        var (schemaDb, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = schemaDb;
        var dsn = TestDbFactory.SqliteDsn(schemaDb);

        const string receipt = """{"kind":"task","value":"ozon-task-99"}""";
        var confirmAttempt = 0;
        var dispatcher = new DelegateDispatcher(
            onDispatch: (_, _) => Task.FromResult(new IntegrationDispatchResult(
                IntegrationDispatchOutcome.Accepted, ReceiptJson: receipt, NextCheckAt: clock.Now.AddSeconds(15).UtcDateTime)),
            onConfirm: (req, _) =>
            {
                Assert.Equal(receipt, req.ReceiptJson);
                confirmAttempt++;
                if (confirmAttempt == 1)
                    throw new IntegrationTransportBlockedException("transport_admission_deferred", clock.Now.AddMinutes(10));
                return Task.FromResult(new IntegrationDispatchResult(IntegrationDispatchOutcome.Applied));
            });

        var (provider, worker) = BuildStack(() => TestDbFactory.CreateSqliteContext(dsn), clock, dispatcher);
        await using var _p = provider;

        await using (var admitDb = TestDbFactory.CreateSqliteContext(dsn))
        {
            var coordinator = new IntegrationCoordinator(admitDb, new NoopOperationLogger(), NullLogger<IntegrationCoordinator>.Instance);
            await coordinator.AdmitAsync(AdmitSingle(), v1Enabled: true, CancellationToken.None);
        }

        // Dispatch -> Accepted with receipt.
        await worker.ProcessDueStagesAsync(CancellationToken.None);
        Assert.Equal(1, dispatcher.DispatchCalls);

        // First confirmation -> admission deferred.
        clock.Now = clock.Now.AddSeconds(15);
        var expectedConfirmRetry = clock.Now.AddMinutes(10);
        await worker.ProcessDueStagesAsync(CancellationToken.None);
        Assert.Equal(1, dispatcher.DispatchCalls);
        Assert.Equal(1, dispatcher.ConfirmCalls);

        await using (var verify = TestDbFactory.CreateSqliteContext(dsn))
        {
            var stage = await verify.integrationStages.AsNoTracking().SingleAsync();
            Assert.Equal(IntegrationStageState.Accepted, stage.state);
            Assert.Equal(receipt, stage.receiptJson);
            Assert.Equal(expectedConfirmRetry.UtcDateTime, stage.nextCheckAt);
        }

        // At RetryAt: stays on ConfirmAsync (never re-enters DispatchAsync) and finishes Applied.
        clock.Now = expectedConfirmRetry;
        await worker.ProcessDueStagesAsync(CancellationToken.None);
        Assert.Equal(1, dispatcher.DispatchCalls);
        Assert.Equal(2, dispatcher.ConfirmCalls);

        await using (var verify = TestDbFactory.CreateSqliteContext(dsn))
        {
            var stage = await verify.integrationStages.AsNoTracking().SingleAsync();
            Assert.Equal(IntegrationStageState.Applied, stage.state);
            Assert.Equal(receipt, stage.receiptJson);
        }
    }

    [Fact]
    public async Task Worker_AdmissionDeferralAfterSendAuthorized_NeverReentersFirstDispatch()
    {
        var clock = new ManualClock();
        var (schemaDb, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = schemaDb;
        var dsn = TestDbFactory.SqliteDsn(schemaDb);

        IntegrationHttpTransport? transport = null;
        var httpCalls = 0;
        var dispatcher = new DelegateDispatcher(
            onDispatch: async (req, ct) =>
            {
                var bound = transport!.Bind(req.TransportAttempt!, ResolveFixture);
                // First effectful send succeeds and commits sendAuthorizedAt, with 429 cooldown for follow-up.
                await bound.SendAsync(FixtureRequest(effectful: true), ct);
                // A second send on the same attempt hits transport_admission_deferred.
                await bound.SendAsync(FixtureRequest(effectful: false), ct);
                return new IntegrationDispatchResult(IntegrationDispatchOutcome.Applied);
            },
            onConfirm: (_, _) => Task.FromResult(new IntegrationDispatchResult(IntegrationDispatchOutcome.Applied)));

        var (provider, worker) = BuildStack(() => TestDbFactory.CreateSqliteContext(dsn), clock, dispatcher,
            (_, _) =>
            {
                httpCalls++;
                var resp = new HttpResponseMessage((HttpStatusCode)429) { Content = new StringContent("rate_limited") };
                resp.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(5));
                return Task.FromResult(resp);
            });
        await using var _p = provider;
        transport = provider.GetRequiredService<IntegrationHttpTransport>();

        await using (var admitDb = TestDbFactory.CreateSqliteContext(dsn))
        {
            var coordinator = new IntegrationCoordinator(admitDb, new NoopOperationLogger(), NullLogger<IntegrationCoordinator>.Instance);
            await coordinator.AdmitAsync(AdmitSingle(), v1Enabled: true, CancellationToken.None);
        }

        var expectedRetryAt = clock.Now.AddMinutes(5);
        await worker.ProcessDueStagesAsync(CancellationToken.None);

        Assert.Equal(1, httpCalls);
        Assert.Equal(1, dispatcher.DispatchCalls);
        Assert.Equal(0, dispatcher.ConfirmCalls);

        await using (var verify = TestDbFactory.CreateSqliteContext(dsn))
        {
            var attempt = await verify.integrationAttempts.AsNoTracking().SingleAsync();
            Assert.NotNull(attempt.sendAuthorizedAt);
            Assert.Equal(IntegrationAttemptOutcome.Pending, attempt.outcome);
            Assert.Equal(IntegrationEffectCertainty.Possible, attempt.effectCertainty);

            // Must NOT return to Ready; must transition to Deferred so next poll enters ConfirmAsync.
            var stage = await verify.integrationStages.AsNoTracking().SingleAsync();
            Assert.Equal(IntegrationStageState.Deferred, stage.state);
            Assert.Equal(expectedRetryAt.UtcDateTime, stage.nextCheckAt);
        }

        clock.Now = expectedRetryAt;
        await worker.ProcessDueStagesAsync(CancellationToken.None);
        Assert.Equal(1, dispatcher.DispatchCalls);
        Assert.Equal(1, dispatcher.ConfirmCalls);
    }

    [Fact]
    public async Task Worker_StopWhileWaitingForAdmissionDeferral_CancelsBeforeResumedWrite()
    {
        var clock = new ManualClock();
        var (schemaDb, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = schemaDb;
        var dsn = TestDbFactory.SqliteDsn(schemaDb);

        var dispatcher = new DelegateDispatcher(
            onDispatch: (_, _) => throw new IntegrationTransportBlockedException("transport_admission_deferred", clock.Now.AddMinutes(30)));

        var (provider, worker) = BuildStack(() => TestDbFactory.CreateSqliteContext(dsn), clock, dispatcher);
        await using var _p = provider;

        string opId;
        await using (var admitDb = TestDbFactory.CreateSqliteContext(dsn))
        {
            var coordinator = new IntegrationCoordinator(admitDb, new NoopOperationLogger(), NullLogger<IntegrationCoordinator>.Instance);
            opId = (await coordinator.AdmitAsync(AdmitSingle(), v1Enabled: true, CancellationToken.None)).OperationId;
        }

        await worker.ProcessDueStagesAsync(CancellationToken.None);
        Assert.Equal(1, dispatcher.DispatchCalls);

        // User stops the command while it is waiting on the admission cooldown.
        await using (var stopDb = TestDbFactory.CreateSqliteContext(dsn))
        {
            var coordinator = new IntegrationCoordinator(stopDb, new NoopOperationLogger(), NullLogger<IntegrationCoordinator>.Instance);
            Assert.True(await coordinator.RequestStopByOperationIdAsync(opId, CancellationToken.None));
        }

        clock.Now = clock.Now.AddMinutes(30);
        await worker.ProcessDueStagesAsync(CancellationToken.None);

        // Resumed write is cancelled before dispatch; DispatchAsync is not called a second time.
        Assert.Equal(1, dispatcher.DispatchCalls);
        await using var verify = TestDbFactory.CreateSqliteContext(dsn);
        Assert.Equal(IntegrationStageState.CancelledBeforeEffect, (await verify.integrationStages.AsNoTracking().SingleAsync()).state);
        Assert.Equal(IntegrationCommandState.Cancelled, (await verify.integrationCommands.AsNoTracking().SingleAsync()).state);
    }

    [Fact]
    public async Task Worker_SlowPollingBatch_UsesFreshClockAndLeasePerStage()
    {
        var clock = new ManualClock();
        var (schemaDb, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = schemaDb;
        var dsn = TestDbFactory.SqliteDsn(schemaDb);

        var t0 = clock.Now;
        DateTimeOffset? secondClaimExpires = null;
        var dispatcher = new DelegateDispatcher(onDispatch: (req, _) =>
        {
            if (req.ItemKey == "item-1")
                clock.Now = t0.AddSeconds(90); // Stage 1 takes 90 seconds.
            else
                secondClaimExpires = req.TransportAttempt!.ClaimExpiresAt;
            return Task.FromResult(new IntegrationDispatchResult(IntegrationDispatchOutcome.Applied));
        });

        var (provider, worker) = BuildStack(() => TestDbFactory.CreateSqliteContext(dsn), clock, dispatcher);
        await using var _p = provider;

        await using (var admitDb = TestDbFactory.CreateSqliteContext(dsn))
        {
            var coordinator = new IntegrationCoordinator(admitDb, new NoopOperationLogger(), NullLogger<IntegrationCoordinator>.Instance);
            await coordinator.AdmitAsync(AdmitSingle(stages:
            [
                new IntegrationStageSeed("item-1", "create", "create", 0, Payload),
                new IntegrationStageSeed("item-2", "create", "create", 0, Payload)
            ]), v1Enabled: true, CancellationToken.None);
        }

        await worker.ProcessDueStagesAsync(CancellationToken.None);

        // Stage 2 must receive a fresh 2-minute lease from t0+90s, not from batch start t0.
        Assert.Equal(t0.AddSeconds(90).AddMinutes(2), secondClaimExpires);
    }

    [Fact]
    public async Task Worker_NonDeferralTransportBlock_TerminatesAsUnknownNotReady()
    {
        var clock = new ManualClock();
        var (schemaDb, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = schemaDb;
        var dsn = TestDbFactory.SqliteDsn(schemaDb);

        var dispatcher = new DelegateDispatcher(
            onDispatch: (_, _) => throw new IntegrationTransportBlockedException("transport_attempt_or_scope_not_current"));

        var (provider, worker) = BuildStack(() => TestDbFactory.CreateSqliteContext(dsn), clock, dispatcher);
        await using var _p = provider;

        await using (var admitDb = TestDbFactory.CreateSqliteContext(dsn))
        {
            var coordinator = new IntegrationCoordinator(admitDb, new NoopOperationLogger(), NullLogger<IntegrationCoordinator>.Instance);
            await coordinator.AdmitAsync(AdmitSingle(), v1Enabled: true, CancellationToken.None);
        }

        await worker.ProcessDueStagesAsync(CancellationToken.None);

        await using var verify = TestDbFactory.CreateSqliteContext(dsn);
        Assert.Equal(IntegrationStageState.Unknown, (await verify.integrationStages.AsNoTracking().SingleAsync()).state);
        Assert.Equal(IntegrationCommandState.Unknown, (await verify.integrationCommands.AsNoTracking().SingleAsync()).state);
    }

    [Fact]
    public async Task Worker_StaleClaimant_CannotReleaseSuccessorsDependencyWaitClaim()
    {
        var clock = new ManualClock();
        var (schemaDb, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = schemaDb;
        var dsn = TestDbFactory.SqliteDsn(schemaDb);

        await using (var admitDb = TestDbFactory.CreateSqliteContext(dsn))
        {
            var coordinator = new IntegrationCoordinator(admitDb, new NoopOperationLogger(), NullLogger<IntegrationCoordinator>.Instance);
            await coordinator.AdmitAsync(AdmitSingle(stages:
            [
                // Stage A is Accepted (not yet satisfying Stage B's dependency).
                new IntegrationStageSeed("item-1", "stage-a", "create", 0, Payload),
                new IntegrationStageSeed("item-1", "stage-b", "price", 1, Payload, DependsOn: ["stage-a"])
            ]), v1Enabled: true, CancellationToken.None);
            await admitDb.integrationStages.Where(s => s.stageKey == "stage-a")
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.state, IntegrationStageState.Accepted)
                    .SetProperty(x => x.nextCheckAt, clock.Now.AddMinutes(10).UtcDateTime));
        }

        var stageBId = await schemaDb.integrationStages.AsNoTracking()
            .Where(s => s.stageKey == "stage-b").Select(s => s.Id).SingleAsync();

        // Intercept right before ReleaseClaimAsync's UPDATE to simulate a rival taking over the claim.
        var hijacked = false;
        var interceptor = new InterceptBeforeRelease(async ct =>
        {
            if (hijacked) return;
            hijacked = true;
            await using var rivalDb = TestDbFactory.CreateSqliteContext(dsn);
            await rivalDb.integrationStages.Where(s => s.Id == stageBId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.claimVersion, x => x.claimVersion + 1)
                    .SetProperty(x => x.claimedBy, "worker-successor")
                    .SetProperty(x => x.claimedUntil, clock.Now.AddMinutes(5).UtcDateTime), ct);
        });

        var dispatcher = new DelegateDispatcher();
        var (provider, worker) = BuildStack(
            () => TestDbFactory.CreateSqliteContext(dsn, interceptors: [interceptor]),
            clock, dispatcher);
        await using var _p = provider;

        await worker.ProcessDueStagesAsync(CancellationToken.None);

        Assert.True(hijacked);
        await using var verify = TestDbFactory.CreateSqliteContext(dsn);
        var stageB = await verify.integrationStages.AsNoTracking().SingleAsync(s => s.Id == stageBId);
        Assert.Equal("worker-successor", stageB.claimedBy);
        Assert.Equal(2, stageB.claimVersion);
        Assert.Equal(clock.Now.AddMinutes(5).UtcDateTime, stageB.claimedUntil);
    }

    [Fact]
    public async Task Worker_StaleClaimant_CannotAlterSuccessorsAttemptState()
    {
        var clock = new ManualClock();
        var (schemaDb, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = schemaDb;
        var dsn = TestDbFactory.SqliteDsn(schemaDb);

        await using (var admitDb = TestDbFactory.CreateSqliteContext(dsn))
        {
            var coordinator = new IntegrationCoordinator(admitDb, new NoopOperationLogger(), NullLogger<IntegrationCoordinator>.Instance);
            await coordinator.AdmitAsync(AdmitSingle(), v1Enabled: true, CancellationToken.None);
        }

        var stageId = await schemaDb.integrationStages.AsNoTracking().Select(s => s.Id).SingleAsync();

        // Intercept right before CommitAttemptBeforeSendAsync's UPDATE to simulate lease takeover.
        var hijacked = false;
        var interceptor = new InterceptBeforeInFlight(async ct =>
        {
            if (hijacked) return;
            hijacked = true;
            await using var rivalDb = TestDbFactory.CreateSqliteContext(dsn);
            await rivalDb.integrationStages.Where(s => s.Id == stageId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.claimVersion, x => x.claimVersion + 1)
                    .SetProperty(x => x.claimedBy, "worker-successor")
                    .SetProperty(x => x.attemptCount, 1)
                    .SetProperty(x => x.state, IntegrationStageState.InFlight), ct);
        });

        var dispatcher = new DelegateDispatcher();
        var (provider, worker) = BuildStack(
            () => TestDbFactory.CreateSqliteContext(dsn, interceptors: [interceptor]),
            clock, dispatcher);
        await using var _p = provider;

        await worker.ProcessDueStagesAsync(CancellationToken.None);

        Assert.True(hijacked);
        Assert.Equal(0, dispatcher.DispatchCalls);
        await using var verify = TestDbFactory.CreateSqliteContext(dsn);
        Assert.Empty(await verify.integrationAttempts.AsNoTracking().ToListAsync());
        var stage = await verify.integrationStages.AsNoTracking().SingleAsync();
        Assert.Equal("worker-successor", stage.claimedBy);
        Assert.Equal(2, stage.claimVersion);
    }

    [Fact]
    public async Task Worker_PriorSameEntityWrite_BlocksSuccessorWhileUnresolvedOrUnknown()
    {
        var clock = new ManualClock();
        var (schemaDb, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = schemaDb;
        var dsn = TestDbFactory.SqliteDsn(schemaDb);

        var dispatchedCommands = new List<int>();
        var confirmOutcome = IntegrationDispatchOutcome.Accepted;
        var dispatcher = new DelegateDispatcher(
            onDispatch: (req, _) =>
            {
                dispatchedCommands.Add(req.CommandId);
                return Task.FromResult(new IntegrationDispatchResult(
                    IntegrationDispatchOutcome.Accepted, NextCheckAt: clock.Now.AddMinutes(1).UtcDateTime));
            },
            onConfirm: (_, _) => Task.FromResult(new IntegrationDispatchResult(
                confirmOutcome, NextCheckAt: clock.Now.AddMinutes(1).UtcDateTime)));

        var (provider, worker) = BuildStack(() => TestDbFactory.CreateSqliteContext(dsn), clock, dispatcher);
        await using var _p = provider;

        int cmd1Id, cmd2Id;
        await using (var admitDb = TestDbFactory.CreateSqliteContext(dsn))
        {
            var coordinator = new IntegrationCoordinator(admitDb, new NoopOperationLogger(), NullLogger<IntegrationCoordinator>.Instance);
            var r1 = await coordinator.AdmitAsync(AdmitSingle(requestKey: "cmd-1", itemKey: "product-42"), v1Enabled: true, CancellationToken.None);
            var r2 = await coordinator.AdmitAsync(AdmitSingle(requestKey: "cmd-2", itemKey: "product-42"), v1Enabled: true, CancellationToken.None);
            cmd1Id = await admitDb.integrationCommands.Where(c => c.commandId == r1.CommandId).Select(c => c.Id).SingleAsync();
            cmd2Id = await admitDb.integrationCommands.Where(c => c.commandId == r2.CommandId).Select(c => c.Id).SingleAsync();
        }

        // Poll 1: cmd-1 dispatches and stays Accepted; cmd-2 must NOT dispatch while cmd-1 is unresolved.
        await worker.ProcessDueStagesAsync(CancellationToken.None);
        Assert.Equal([cmd1Id], dispatchedCommands);

        await using (var verify = TestDbFactory.CreateSqliteContext(dsn))
        {
            var stage2 = await verify.integrationStages.AsNoTracking().SingleAsync(s => s.commandId == cmd2Id);
            Assert.Equal(IntegrationStageState.Ready, stage2.state);
            Assert.NotNull(stage2.nextCheckAt);
        }

        // Now cmd-1 confirmation returns Unknown -> cmd-2 must be blocked/cancelled before any send!
        confirmOutcome = IntegrationDispatchOutcome.Unknown;
        clock.Now = clock.Now.AddMinutes(2);
        await worker.ProcessDueStagesAsync(CancellationToken.None);

        Assert.Equal([cmd1Id], dispatchedCommands);
        await using (var verify = TestDbFactory.CreateSqliteContext(dsn))
        {
            var stage1 = await verify.integrationStages.AsNoTracking().SingleAsync(s => s.commandId == cmd1Id);
            var stage2 = await verify.integrationStages.AsNoTracking().SingleAsync(s => s.commandId == cmd2Id);
            Assert.Equal(IntegrationStageState.Unknown, stage1.state);
            Assert.Equal(IntegrationStageState.CancelledBeforeEffect, stage2.state);
            Assert.Contains("prior_entity_effect_unknown", stage2.issuesJson);
        }
    }

    private sealed class InterceptBeforeRelease(Func<CancellationToken, Task> onBefore) : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE \"integrationStages\"", StringComparison.Ordinal) &&
                command.CommandText.Contains("\"nextCheckAt\"", StringComparison.Ordinal))
                await onBefore(cancellationToken);
            return result;
        }
    }

    private sealed class InterceptBeforeInFlight(Func<CancellationToken, Task> onBefore) : DbTransactionInterceptor
    {
        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            await onBefore(cancellationToken);
            return result;
        }
    }

    private sealed class NoopOperationLogger : IOperationLogger
    {
        public Task StartAsync(string operationId, int storeId, int authorUserId, string type, string title,
            int? connectionId = null, string? connectionName = null,
            string? batchId = null, string? batchType = null, int? totalItems = null, int? authorApiKeyId = null)
            => Task.CompletedTask;
        public Task LogAsync(string operationId, int storeId, string level, string message) => Task.CompletedTask;
        public Task ProgressAsync(string operationId, int storeId, int total, int completed, int failed) => Task.CompletedTask;
        public Task FinishAsync(string operationId, int storeId, string status, string? summary = null) => Task.CompletedTask;
        public Task UpdateStatusAsync(string operationId, int storeId, string status) => Task.CompletedTask;
    }
}
