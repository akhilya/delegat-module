using Delegat.Models;
using Delegat.Models.Integration;
using Delegat.Services;
using Delegat.Services.Integration;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public sealed class IntegrationExecutionP2Tests
{
    private static IntegrationAdmitRequest AdmitRequest(
        string kind = "export-product",
        int storeId = 1,
        int? connectionId = 7,
        string? requestKey = "req-1",
        string inputJson = """{"productId":42}""",
        IReadOnlyList<IntegrationStageSeed>? stages = null) =>
        new(kind, storeId, AuthorUserId: 10, AuthorApiKeyId: null, connectionId,
            requestKey, inputJson, Title: "Test op",
            stages ?? [new IntegrationStageSeed("item-1", "create", "create", 0, """{"sku":"X"}""")]);

    private static IntegrationCoordinator CreateCoordinator(DataContext db) =>
        new(db, new NoopOperationLogger(), NullLogger<IntegrationCoordinator>.Instance);

    private static IntegrationExecutionWorker CreateWorker(
        DataContext db, IIntegrationStageDispatcher dispatcher) =>
        new(BuildScopeFactory(db, dispatcher), NullLogger<IntegrationExecutionWorker>.Instance);

    private static IServiceScopeFactory BuildScopeFactory(
        DataContext db, IIntegrationStageDispatcher dispatcher) =>
        new ServiceCollection()
            .AddSingleton(db)
            .AddSingleton(dispatcher)
            .AddSingleton<IOperationLogger>(new NoopOperationLogger())
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

    // ---------- Admission / dedupe / conflict (A02) ----------

    [Fact]
    public async Task Admit_CreatesCommandStagesAndOperationAtomically()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        var coordinator = CreateCoordinator(db);

        var result = await coordinator.AdmitAsync(AdmitRequest(), v1Enabled: true, CancellationToken.None);

        var command = await db.integrationCommands.SingleAsync();
        Assert.Equal(result.CommandId, command.commandId);
        Assert.Equal("admitted", command.state);
        Assert.Equal(IntegrationCoordinator.ComputeHash("""{"productId":42}"""), command.requestHash);

        var stage = await db.integrationStages.SingleAsync();
        Assert.Equal(command.Id, stage.commandId);
        Assert.Equal("ready", stage.state);

        var op = await db.operations.SingleAsync();
        Assert.Equal(result.OperationId, op.operationId);
        Assert.Equal(IntegrationCoordinator.OperationType, op.type);
        Assert.Equal("running", op.status);
    }

    [Fact]
    public async Task Admit_SameKeySameBody_ReturnsExistingCommand()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        var coordinator = CreateCoordinator(db);

        var first = await coordinator.AdmitAsync(AdmitRequest(), v1Enabled: true, CancellationToken.None);
        var second = await coordinator.AdmitAsync(AdmitRequest(), v1Enabled: true, CancellationToken.None);

        Assert.True(second.IsDuplicate);
        Assert.Equal(first.CommandId, second.CommandId);
        Assert.Equal(1, await db.integrationCommands.CountAsync());
        Assert.Equal(1, await db.integrationStages.CountAsync());
        Assert.Equal(1, await db.operations.CountAsync());
    }

    [Fact]
    public async Task Admit_SameKeyDifferentBody_ConflictsBeforeAnyEffect()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        var coordinator = CreateCoordinator(db);

        await coordinator.AdmitAsync(AdmitRequest(), v1Enabled: true, CancellationToken.None);
        await Assert.ThrowsAsync<IntegrationRequestConflictException>(() =>
            coordinator.AdmitAsync(
                AdmitRequest(inputJson: """{"productId":43}"""),
                v1Enabled: true, CancellationToken.None));

        Assert.Equal(1, await db.integrationCommands.CountAsync());
        Assert.Equal(1, await db.integrationStages.CountAsync());
    }

    [Fact]
    public async Task Admit_SameKeyDifferentScope_IsIndependentCommand()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        var coordinator = CreateCoordinator(db);

        var first = await coordinator.AdmitAsync(AdmitRequest(), v1Enabled: true, CancellationToken.None);
        var second = await coordinator.AdmitAsync(
            AdmitRequest(kind: "price-sync", inputJson: """{"productId":99}"""),
            v1Enabled: true, CancellationToken.None);

        Assert.False(second.IsDuplicate);
        Assert.NotEqual(first.CommandId, second.CommandId);
        Assert.Equal(2, await db.integrationCommands.CountAsync());
    }

    [Fact]
    public async Task Admit_V1Disabled_ThrowsAndWritesNothing()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        var coordinator = CreateCoordinator(db);

        await Assert.ThrowsAsync<IntegrationExecutionDisabledException>(() =>
            coordinator.AdmitAsync(AdmitRequest(), v1Enabled: false, CancellationToken.None));

        Assert.Equal(0, await db.integrationCommands.CountAsync());
        Assert.Equal(0, await db.operations.CountAsync());
    }

    // ---------- Attempt-before-send + lost response (A04/A11) ----------

    [Fact]
    public async Task Worker_PersistsAttemptBeforePhysicalDispatch()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        var coordinator = CreateCoordinator(db);
        await coordinator.AdmitAsync(AdmitRequest(), v1Enabled: true, CancellationToken.None);

        var dispatcher = new FakeDispatcher
        {
            DispatchBehavior = async (db2, req) =>
            {
                // Attempt evidence must be committed before the provider call.
                var attemptExists = await db2.integrationAttempts
                    .AnyAsync(a => a.requestKey == req.AttemptRequestKey);
                Assert.True(attemptExists, "attempt row must exist before dispatch");
                return new IntegrationDispatchResult(IntegrationDispatchOutcome.Applied);
            },
            Db = db
        };

        await CreateWorker(db, dispatcher).ProcessDueStagesAsync(CancellationToken.None);

        Assert.Equal(1, dispatcher.DispatchCalls);
        var attempt = await db.integrationAttempts.SingleAsync();
        Assert.Equal("applied", attempt.outcome);
        Assert.Equal(IntegrationEffectCertainty.Applied, attempt.effectCertainty);
        Assert.NotNull(attempt.completedAt);

        var stage = await db.integrationStages.SingleAsync();
        Assert.Equal("applied", stage.state);
        var command = await db.integrationCommands.SingleAsync();
        Assert.Equal("completed", command.state);
        var op = await db.operations.SingleAsync();
        Assert.Equal("completed", op.status);
    }

    [Fact]
    public async Task Worker_LostResponse_RecoversViaConfirmNotRedispatch()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        var coordinator = CreateCoordinator(db);
        await coordinator.AdmitAsync(AdmitRequest(), v1Enabled: true, CancellationToken.None);

        // Simulate crash: attempt committed, stage inflight, no recorded outcome.
        var stage = await db.integrationStages.SingleAsync();
        stage.state = IntegrationStageState.InFlight;
        stage.attemptCount = 1;
        var command = await db.integrationCommands.SingleAsync();
        db.integrationAttempts.Add(new IntegrationAttempt
        {
            commandId = command.Id,
            stageId = stage.Id,
            stageKey = stage.stageKey,
            dispatchId = $"{command.commandId}:{stage.stageKey}",
            attemptNumber = 1,
            requestKey = "lost-key",
            requestHash = "h",
            createdAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var dispatcher = new FakeDispatcher
        {
            ConfirmResult = new IntegrationDispatchResult(
                IntegrationDispatchOutcome.Applied,
                ReceiptJson: """{"externalId":"r-1"}""")
        };

        await CreateWorker(db, dispatcher).ProcessDueStagesAsync(CancellationToken.None);

        Assert.Equal(0, dispatcher.DispatchCalls);
        Assert.Equal(1, dispatcher.ConfirmCalls);
        Assert.Equal("applied", (await db.integrationStages.SingleAsync()).state);
        Assert.Equal("""{"externalId":"r-1"}""",
            (await db.integrationStages.SingleAsync()).receiptJson);
    }

    [Fact]
    public async Task Worker_RejectedOutcome_RetriesThenTerminatesBeforeEffect()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        var coordinator = CreateCoordinator(db);
        await coordinator.AdmitAsync(AdmitRequest(), v1Enabled: true, CancellationToken.None);

        var dispatcher = new FakeDispatcher
        {
            DispatchResult = new IntegrationDispatchResult(
                IntegrationDispatchOutcome.Rejected, Retryable: true)
        };
        var worker = CreateWorker(db, dispatcher);

        await worker.ProcessDueStagesAsync(CancellationToken.None);
        var stage = await db.integrationStages.SingleAsync();
        Assert.Equal("ready", stage.state); // retryable before-effect: requeued with backoff
        Assert.NotNull(stage.nextCheckAt);

        // Retry budget exhausted -> terminal, no false success.
        dispatcher.DispatchResult = new IntegrationDispatchResult(
            IntegrationDispatchOutcome.Rejected, Retryable: false);
        stage.nextCheckAt = null;
        await db.SaveChangesAsync();
        await worker.ProcessDueStagesAsync(CancellationToken.None);

        stage = await db.integrationStages.SingleAsync();
        Assert.Equal("rejected", stage.state);
        var command = await db.integrationCommands.SingleAsync();
        Assert.Equal("failed", command.state);
        Assert.Equal("failed", (await db.operations.SingleAsync()).status);
        Assert.Equal(IntegrationEffectCertainty.None,
            (await db.integrationAttempts.OrderByDescending(a => a.Id).FirstAsync()).effectCertainty);
    }

    // ---------- Ordering / ownership (A08) ----------

    [Fact]
    public async Task Worker_DependencyBlocksSecondStageUntilFirstApplied()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        var coordinator = CreateCoordinator(db);
        await coordinator.AdmitAsync(
            AdmitRequest(stages:
            [
                new IntegrationStageSeed("item-1", "create", "create", 0),
                new IntegrationStageSeed("item-1", "price", "update", 1, DependsOn: ["create"])
            ]),
            v1Enabled: true, CancellationToken.None);

        var dispatcher = new FakeDispatcher
        {
            // "create" is accepted but not yet applied: dependency unsatisfied.
            // "price" applies immediately once dispatched.
            DispatchSelector = req => req.StageKey == "create"
                ? new IntegrationDispatchResult(IntegrationDispatchOutcome.Accepted)
                : new IntegrationDispatchResult(IntegrationDispatchOutcome.Applied),
            ConfirmResult = new IntegrationDispatchResult(IntegrationDispatchOutcome.Applied)
        };
        var worker = CreateWorker(db, dispatcher);

        await worker.ProcessDueStagesAsync(CancellationToken.None);
        Assert.Equal(1, dispatcher.DispatchCalls); // only "create" dispatched

        var stages = await db.integrationStages.OrderBy(s => s.position).ToListAsync();
        Assert.Equal("accepted", stages[0].state);
        Assert.Equal("ready", stages[1].state); // blocked, not sent out of order

        // The provider wait elapses; "create" becomes due for confirmation.
        stages[0].nextCheckAt = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();

        await worker.ProcessDueStagesAsync(CancellationToken.None);
        Assert.Equal(1, dispatcher.ConfirmCalls);
        Assert.Equal(2, dispatcher.DispatchCalls); // "price" continues in order
        Assert.Equal("applied", (await db.integrationStages.OrderBy(s => s.position).LastAsync()).state);
        Assert.Equal("completed", (await db.integrationCommands.SingleAsync()).state);
    }

    [Fact]
    public async Task Worker_FailedDependency_CancelsDependentBeforeSend()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        var coordinator = CreateCoordinator(db);
        await coordinator.AdmitAsync(
            AdmitRequest(stages:
            [
                new IntegrationStageSeed("item-1", "create", "create", 0),
                new IntegrationStageSeed("item-1", "price", "update", 1, DependsOn: ["create"])
            ]),
            v1Enabled: true, CancellationToken.None);

        var dispatcher = new FakeDispatcher
        {
            DispatchResult = new IntegrationDispatchResult(
                IntegrationDispatchOutcome.Rejected, Retryable: false)
        };
        var worker = CreateWorker(db, dispatcher);

        await worker.ProcessDueStagesAsync(CancellationToken.None);
        await worker.ProcessDueStagesAsync(CancellationToken.None);

        Assert.Equal(1, dispatcher.DispatchCalls); // dependent stage never sent
        var stages = await db.integrationStages.OrderBy(s => s.position).ToListAsync();
        Assert.Equal("rejected", stages[0].state);
        Assert.Equal("cancelled", stages[1].state);
        Assert.Equal("failed", (await db.integrationCommands.SingleAsync()).state);
    }

    [Fact]
    public async Task Worker_ConcurrentClaims_OnlyOneWorkerDispatches()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        var dsn = TestDbFactory.SqliteDsn(db);
        var coordinator = CreateCoordinator(db);
        await coordinator.AdmitAsync(AdmitRequest(), v1Enabled: true, CancellationToken.None);

        var dispatcherA = new FakeDispatcher
        {
            DispatchResult = new IntegrationDispatchResult(IntegrationDispatchOutcome.Applied)
        };
        var dispatcherB = new FakeDispatcher
        {
            DispatchResult = new IntegrationDispatchResult(IntegrationDispatchOutcome.Applied)
        };

        // Two contexts over the same shared SQLite DSN; both workers poll at once.
        await using var dbB = TestDbFactory.CreateSqliteContext(dsn);
        var workerA = CreateWorker(db, dispatcherA);
        var workerB = CreateWorker(dbB, dispatcherB);

        await Task.WhenAll(
            workerA.ProcessDueStagesAsync(CancellationToken.None),
            workerB.ProcessDueStagesAsync(CancellationToken.None));

        Assert.Equal(1, dispatcherA.DispatchCalls + dispatcherB.DispatchCalls);
        Assert.Equal(1, await db.integrationAttempts.CountAsync());
        Assert.Equal("applied", (await db.integrationStages.SingleAsync()).state);
    }

    // ---------- Stop semantics (A07) ----------

    [Fact]
    public async Task Stop_BeforeSend_CancelsPendingStagesWithoutDispatch()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        var coordinator = CreateCoordinator(db);
        var admitted = await coordinator.AdmitAsync(AdmitRequest(), v1Enabled: true, CancellationToken.None);

        Assert.True(await coordinator.RequestStopByOperationIdAsync(admitted.OperationId, CancellationToken.None));

        var dispatcher = new FakeDispatcher();
        await CreateWorker(db, dispatcher).ProcessDueStagesAsync(CancellationToken.None);

        Assert.Equal(0, dispatcher.DispatchCalls);
        var stage = await db.integrationStages.SingleAsync();
        Assert.Equal("cancelled", stage.state);
        var command = await db.integrationCommands.SingleAsync();
        Assert.Equal("cancelled", command.state);
        Assert.Equal("cancelled", (await db.operations.SingleAsync()).status);
    }

    [Fact]
    public async Task Stop_WhileAccepted_StillConfirmsInFlightEffect()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        var coordinator = CreateCoordinator(db);
        var admitted = await coordinator.AdmitAsync(AdmitRequest(), v1Enabled: true, CancellationToken.None);

        var dispatcher = new FakeDispatcher
        {
            DispatchResult = new IntegrationDispatchResult(IntegrationDispatchOutcome.Accepted),
            ConfirmResult = new IntegrationDispatchResult(
                IntegrationDispatchOutcome.Applied, ReceiptJson: """{"externalId":"r-9"}""")
        };
        var worker = CreateWorker(db, dispatcher);

        await worker.ProcessDueStagesAsync(CancellationToken.None);
        var acceptedStage = await db.integrationStages.SingleAsync();
        Assert.Equal("accepted", acceptedStage.state);

        // The provider wait elapses; the stage is due for confirmation.
        acceptedStage.nextCheckAt = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();

        await coordinator.RequestStopByOperationIdAsync(admitted.OperationId, CancellationToken.None);
        await worker.ProcessDueStagesAsync(CancellationToken.None);

        // Stop must not claim remote rollback: the accepted effect is confirmed.
        Assert.Equal(1, dispatcher.ConfirmCalls);
        var stage = await db.integrationStages.SingleAsync();
        Assert.Equal("applied", stage.state);
        var command = await db.integrationCommands.SingleAsync();
        Assert.Equal("partial", command.state); // stopped after partial application
    }

    // ---------- Unknown outcome / guarded deletion (A05/A09) ----------

    [Fact]
    public async Task Worker_UnknownOutcome_TerminatesCommandNotAsSuccess()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        var coordinator = CreateCoordinator(db);
        var admitted = await coordinator.AdmitAsync(AdmitRequest(), v1Enabled: true, CancellationToken.None);

        var dispatcher = new FakeDispatcher
        {
            DispatchResult = new IntegrationDispatchResult(
                IntegrationDispatchOutcome.Unknown, ResponseSummary: "timeout")
        };
        await CreateWorker(db, dispatcher).ProcessDueStagesAsync(CancellationToken.None);

        var stage = await db.integrationStages.SingleAsync();
        Assert.Equal("unknown", stage.state);
        var command = await db.integrationCommands.SingleAsync();
        Assert.Equal("unknown", command.state);
        var op = await db.operations.SingleAsync();
        Assert.Equal("failed", op.status); // honest terminal, never silent success

        // Evidence retained: unresolved-projection guard sees it as terminal,
        // but the attempt stays for reconciliation.
        var attempt = await db.integrationAttempts.SingleAsync();
        Assert.Equal("unknown", attempt.outcome);
        Assert.Equal(IntegrationEffectCertainty.Possible, attempt.effectCertainty);
        Assert.False(await coordinator.IsUnresolvedCommandOperationAsync(
            admitted.OperationId, CancellationToken.None));
    }

    [Fact]
    public async Task GuardedDeletion_UnresolvedCommandsReportedCorrectly()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        var coordinator = CreateCoordinator(db);

        var live = await coordinator.AdmitAsync(
            AdmitRequest(requestKey: "live"), v1Enabled: true, CancellationToken.None);
        var done = await coordinator.AdmitAsync(
            AdmitRequest(requestKey: "done", kind: "price-sync", inputJson: """{"p":2}"""),
            v1Enabled: true, CancellationToken.None);
        await db.integrationCommands
            .Where(c => c.commandId == done.CommandId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.state, IntegrationCommandState.Completed));

        var blocked = await coordinator.GetUnresolvedOperationIdsAsync(
            [live.OperationId, done.OperationId, "unrelated"], CancellationToken.None);

        Assert.Single(blocked);
        Assert.Contains(live.OperationId, blocked);
    }

    private sealed class FakeDispatcher : IIntegrationStageDispatcher
    {
        public DataContext? Db { get; set; }
        public IntegrationDispatchResult DispatchResult { get; set; } =
            new(IntegrationDispatchOutcome.Applied);
        public IntegrationDispatchResult ConfirmResult { get; set; } =
            new(IntegrationDispatchOutcome.Applied);
        public Func<DataContext, DispatchStageRequest, Task<IntegrationDispatchResult>>? DispatchBehavior { get; set; }
        public Func<DispatchStageRequest, IntegrationDispatchResult>? DispatchSelector { get; set; }
        public int DispatchCalls { get; private set; }
        public int ConfirmCalls { get; private set; }

        public Task<IntegrationDispatchResult> DispatchAsync(
            DispatchStageRequest request, CancellationToken ct)
        {
            DispatchCalls++;
            if (DispatchSelector != null)
                return Task.FromResult(DispatchSelector(request));
            return DispatchBehavior != null && Db != null
                ? DispatchBehavior(Db, request)
                : Task.FromResult(DispatchResult);
        }

        public Task<IntegrationDispatchResult> ConfirmAsync(
            DispatchStageRequest request, CancellationToken ct)
        {
            ConfirmCalls++;
            return Task.FromResult(ConfirmResult);
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
