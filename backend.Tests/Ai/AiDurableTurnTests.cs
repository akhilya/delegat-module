using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.CompilerServices;
using Delegat.Models.Ai;
using Delegat.Services.Ai;
using Delegat.Services.Ai.Agent;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Delegat.Tests;

[Collection("AiPlanSigningKey")]
public class AiDurableTurnTests
{
    [Fact]
    public async Task Admission_CommitsApprovalAndExactStepsTogether_AndRejectsAnotherExecution()
    {
        await using var fixture = await Fixture.CreateAsync();
        var token = await SeedPlan(fixture.Db, "approval");
        var store = new AiDurableTurnStore(fixture.Db);
        var job = await store.EnqueueAsync("approval", 1, 2, "execution", "legacy",
            Request(token), null, default);
        await using var other = fixture.Context();
        var saved = await other.aiTurnJobs.SingleAsync();
        Assert.Equal(job.runId, saved.runId);
        Assert.Equal("pending", saved.status);
        Assert.Equal("approval", saved.activeSessionId);
        var request = JsonSerializer.Deserialize<AiDurableTurnRequest>(saved.requestJson, AiDurableTurnStore.Json)!;
        Assert.Equal(token, request.ProposalId);
        Assert.Equal("run_python", Assert.Single(request.Steps).Tool);
        Assert.Equal("print('once')", request.Steps[0].Args!.Value.GetProperty("code").GetString());
        var planRow = await other.aiConversationMessages.SingleAsync(m => m.role == "assistant");
        Assert.True(JsonNode.Parse(planRow.toolCallsJson!)!["planConfirmed"]!.GetValue<bool>());
        Assert.Equal(99, JsonNode.Parse(planRow.toolCallsJson!)!["plan"]!["affectedCount"]!.GetValue<int>());
        var error = await Assert.ThrowsAsync<AiTurnAdmissionException>(() =>
            new AiDurableTurnStore(other).EnqueueAsync("approval", 1, 2, "execution", "legacy",
                Request(token), null, default));
        Assert.Equal("turn_running", error.Reason);
        Assert.Single(await other.aiTurnJobs.ToListAsync());
        Assert.Single(await other.aiAgentAudits.ToListAsync());
    }

    [Fact]
    public async Task Admission_RollsBackTheConsumedNonce_WhenTheJobPayloadCannotCommit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var token = await SeedPlan(fixture.Db, "rollback");
        await using var failing = TestDbFactory.CreateSqliteContext(fixture.ConnectionString,
            interceptors: [new FailPreparedJob()]);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AiDurableTurnStore(failing).EnqueueAsync("rollback", 1, 2, "execution", "legacy",
                Request(token), null, default));
        await using var inspect = fixture.Context();
        Assert.Empty(await inspect.aiTurnJobs.ToListAsync());
        Assert.Empty(await inspect.aiAgentAudits.ToListAsync());
        var row = await inspect.aiConversationMessages.SingleAsync(m => m.role == "assistant");
        Assert.Null(JsonNode.Parse(row.toolCallsJson!)!["planConfirmed"]);
        var retry = await new AiDurableTurnStore(inspect).EnqueueAsync(
            "rollback", 1, 2, "execution", "legacy", Request(token), null, default);
        Assert.Equal("pending", retry.status);
    }

    [Fact]
    public async Task BusyChat_AttachesToTheSameJob_AcrossFreshContexts()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.EnqueueChat("same");
        await using var other = fixture.Context();
        var attached = await new AiDurableTurnStore(other).EnqueueAsync(
            "same", 1, 2, "chat", "legacy", Request(), User("Different second input"), default);
        Assert.Equal(first.runId, attached.runId);
        Assert.Single(await other.aiTurnJobs.ToListAsync());
        Assert.Single(await other.aiConversationMessages.Where(m => m.role == "user").ToListAsync());
        Assert.Null(await new AiDurableTurnStore(other).FindLatestAsync("same", 1, 999, default));
        Assert.Null(await new AiDurableTurnStore(other).FindLatestAsync("same", 999, 2, default));
    }

    [Fact]
    public async Task Lease_RejectsAnotherOwnerAndAllLateWritesAfterTakeover()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.EnqueueChat("lease");
        var first = (await new AiDurableTurnStore(fixture.Db).ClaimAsync(default))!;
        await using var other = fixture.Context();
        var store = new AiDurableTurnStore(other);
        Assert.Null(await store.ClaimAsync(default));
        await other.aiTurnJobs.Where(j => j.Id == first.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.leaseExpiresAtUtc, DateTime.UtcNow.AddMinutes(-2)));
        var second = (await store.ClaimAsync(default))!;
        Assert.NotEqual(first.leaseOwner, second.leaseOwner);
        Assert.Equal(2, second.attempts);
        Assert.False(await new AiDurableTurnStore(fixture.Db).RenewAsync(first.Id, first.leaseOwner!, default));
        await Assert.ThrowsAsync<AiTurnLeaseLostException>(() =>
            new AiDurableTurnStore(fixture.Db).CompleteAsync(first, "completed", default));
        Assert.Equal(second.leaseOwner, (await store.OwnedAsync(second.Id, second.leaseOwner!, default)).leaseOwner);
    }

    [Fact]
    public async Task ConcurrentClaimers_WithTheSameStaleCandidate_GetExactlyOneLease()
    {
        await using var fixture = await Fixture.CreateAsync();
        var candidate = await fixture.EnqueueChat("race");
        var now = DateTime.UtcNow;
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await using var db = fixture.Context();
            return await new AiDurableTurnStore(db).TryClaimCandidateAsync(candidate, now, default);
        })));
        var winner = Assert.Single(results, j => j != null)!;
        await using var inspect = fixture.Context();
        var persisted = await inspect.aiTurnJobs.SingleAsync();
        Assert.Equal(winner.leaseOwner, persisted.leaseOwner);
        Assert.Equal(1, persisted.attempts);
    }

    [Fact]
    public async Task AHeartbeatAfterSelection_PreventsAStaleExpiredLeaseTakeover()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.EnqueueChat("renew-race");
        var store = new AiDurableTurnStore(fixture.Db);
        var old = (await store.ClaimAsync(default))!;
        var now = DateTime.UtcNow;
        await fixture.Db.aiTurnJobs.Where(j => j.Id == old.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.leaseExpiresAtUtc, now.AddSeconds(-1)));
        await using var observer = fixture.Context();
        var stale = await observer.aiTurnJobs.AsNoTracking().SingleAsync();
        // Simulate renewal landing after a second process's SELECT, including clock skew.
        await fixture.Db.aiTurnJobs.Where(j => j.Id == old.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.leaseExpiresAtUtc, now.AddMinutes(1)));
        Assert.Null(await new AiDurableTurnStore(observer).TryClaimCandidateAsync(stale, now, default));
        var current = await observer.aiTurnJobs.AsNoTracking().SingleAsync();
        Assert.Equal(old.leaseOwner, current.leaseOwner);
        Assert.Equal(1, current.attempts);
    }

    [Fact]
    public async Task Journal_ReplaysTheExactCommittedCursor_AfterAllServiceInstancesAreRecreated()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.EnqueueChat("cursor");
        var store = new AiDurableTurnStore(fixture.Db);
        var lease = (await store.ClaimAsync(default))!;
        var binding = await store.BeginPhaseAsync(lease, "chat", null, null, default);
        var start = AiTurnEvent.Create(SseEventType.MessageStart, new { }) with
        {
            RunId = lease.runId, MessageId = binding.MessageId, DbId = binding.DbId,
            Phase = "chat", Sequence = 1,
        };
        lease = await store.AppendAsync(lease, start, binding, AiSseTurnRecorder.FoldEvents([start]), default);
        var text = AiTurnEvent.Create(SseEventType.Text, new { delta = "A durable reply" }) with
        {
            RunId = lease.runId, MessageId = binding.MessageId, DbId = binding.DbId,
            Phase = "chat", Sequence = 2,
        };
        lease = await store.AppendAsync(lease, text, binding, null, default);
        var done = AiTurnEvent.Create(SseEventType.Done, new { }) with
        {
            RunId = lease.runId, MessageId = binding.MessageId, DbId = binding.DbId,
            Phase = "chat", Sequence = 3,
        };
        lease = await store.AppendAsync(lease, done, binding,
            AiSseTurnRecorder.FoldEvents([start, text, done]), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.AppendAsync(lease, done, binding, null, default));
        Assert.Equal(3, (await store.ReadEventsAsync(lease.Id, default)).Count);
        await store.CompleteAsync(lease, "completed", default);
        await using var reloaded = fixture.Context();
        var restoredStore = new AiDurableTurnStore(reloaded);
        var restored = (await restoredStore.FindLatestAsync("cursor", 1, 2, default))!;
        var chunks = new List<string>();
        await foreach (var chunk in restoredStore.SubscribeAsync(restored, lease.runId, 1, default))
            chunks.Add(chunk);
        Assert.Equal(new[] { text.ToSse(), done.ToSse() }, chunks);
        var row = await reloaded.aiConversationMessages.SingleAsync(m => m.role == "assistant");
        Assert.Equal("A durable reply", row.content);
        Assert.Equal(3, JsonNode.Parse(row.toolCallsJson!)!["lastEventSequence"]!.GetValue<int>());
        Assert.False(await restoredStore.RequestStopAsync("cursor", 1, 2, default));
    }

    [Fact]
    public async Task WorkerTransactions_AreSingleAttempt_EvenWithARetryingProvider()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.EnqueueChat("provider-retries");
        var lease = (await new AiDurableTurnStore(fixture.Db).ClaimAsync(default))!;
        await using var retrying = TestDbFactory.CreateSqliteContext(fixture.ConnectionString, retryingStrategy: true);
        var store = new AiDurableTurnStore(retrying);
        var binding = await store.BeginPhaseAsync(lease, "chat", null, null, default);
        var done = AiTurnEvent.Create(SseEventType.Done, new { }) with
        {
            RunId = lease.runId, MessageId = binding.MessageId, DbId = binding.DbId,
            Phase = "chat", Sequence = 1,
        };
        lease = await store.AppendAsync(lease, done, binding, AiSseTurnRecorder.FoldEvents([done]), default);
        await store.CompleteAsync(lease, "completed", default);
        await using var inspect = fixture.Context();
        Assert.Equal("completed", (await inspect.aiTurnJobs.SingleAsync()).status);
        Assert.Single(await inspect.aiTurnJournalEvents.ToListAsync());
    }

    [Fact]
    public async Task Stop_IsScopedDurable_AndPreventsTheNextDispatchBarrier()
    {
        await using var fixture = await Fixture.CreateAsync();
        var token = await SeedPlan(fixture.Db, "cancel");
        var store = new AiDurableTurnStore(fixture.Db);
        await store.EnqueueAsync("cancel", 1, 2, "execution", "legacy", Request(token), null, default);
        var lease = (await store.ClaimAsync(default))!;
        var binding = await store.BeginPhaseAsync(lease, "execution", token, null, default);
        await using var other = fixture.Context();
        var remote = new AiDurableTurnStore(other);
        Assert.False(await remote.RequestStopAsync("cancel", 999, 2, default));
        Assert.True(await remote.RequestStopAsync("cancel", 1, 2, default));
        var running = Running(lease, binding, 1);
        await Assert.ThrowsAsync<AiTurnLeaseLostException>(() =>
            store.AppendAsync(lease, running, binding, null, default));
        Assert.Empty(await other.aiTurnJournalEvents.ToListAsync());
        Assert.Null((await remote.OwnedAsync(lease.Id, lease.leaseOwner!, default)).startedStep);
    }

    [Fact]
    public async Task StartedPython_RecoversAsUnknown_WithoutInvokingAnyProducer()
    {
        await using var fixture = await Fixture.CreateAsync();
        var token = await SeedPlan(fixture.Db, "in-flight");
        var store = new AiDurableTurnStore(fixture.Db);
        await store.EnqueueAsync("in-flight", 1, 2, "execution", "legacy", Request(token), null, default);
        var lease = (await store.ClaimAsync(default))!;
        var binding = await store.BeginPhaseAsync(lease, "execution", token, null, default);
        var running = Running(lease, binding, 1);
        await store.AppendAsync(lease, running, binding, AiSseTurnRecorder.FoldEvents([running]), default);
        await fixture.Db.aiTurnJobs.Where(j => j.Id == lease.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.leaseExpiresAtUtc, DateTime.UtcNow.AddMinutes(-2)));
        await using var restarted = fixture.Context();
        var recovery = (await new AiDurableTurnStore(restarted).ClaimAsync(default))!;
        var log = new TestLog();
        // No producer services are registered. A mistaken entry into a producer logs a warning
        // and fails this assertion, rather than passing just because a fake call counter is zero.
        using var services = fixture.Services();
        var worker = new AiDurableTurnWorker(services.GetRequiredService<IServiceScopeFactory>(), log);
        await worker.RunClaimedAsync(recovery, default);
        Assert.Empty(log.Warnings);
        await using var inspect = fixture.Context();
        var job = await inspect.aiTurnJobs.SingleAsync();
        Assert.Equal("interrupted", job.status);
        Assert.Null(job.activeSessionId);
        Assert.Equal(0, job.startedStep);
        var events = await new AiDurableTurnStore(inspect).ReadEventsAsync(job.Id, default);
        Assert.Single(events, e => e.Type == SseEventType.Tool);
        var outcome = Assert.Single(events, e => e.Type == SseEventType.ExecutionStatus);
        Assert.True(outcome.Data.GetProperty("outcomeUnknown").GetBoolean());
        Assert.Equal(0, outcome.Data.GetProperty("succeeded").GetInt32());
        Assert.Equal(99, JsonNode.Parse((await inspect.aiConversationMessages
            .SingleAsync(m => m.role == "assistant")).toolCallsJson!)!["plan"]!["affectedCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task AReclaimedChatStream_IsInterrupted_NotReissuedToTheModel()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.EnqueueChat("old-stream");
        var store = new AiDurableTurnStore(fixture.Db);
        var lease = (await store.ClaimAsync(default))!;
        await store.BeginPhaseAsync(lease, "chat", null, null, default);
        await store.ReleaseAsync(lease, default);
        var recovered = (await store.ClaimAsync(default))!;
        var log = new TestLog();
        using var services = fixture.Services();
        await new AiDurableTurnWorker(services.GetRequiredService<IServiceScopeFactory>(), log)
            .RunClaimedAsync(recovered, default);
        Assert.Empty(log.Warnings);
        await using var inspect = fixture.Context();
        Assert.Equal("interrupted", (await inspect.aiTurnJobs.SingleAsync()).status);
        Assert.Contains(await inspect.aiTurnJournalEvents.ToListAsync(),
            e => e.eventJson.Contains("interrupted", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RestartAfterACommittedStep_ContinuesTheNextStep_WithoutRepeatingTheEffect()
    {
        await using var fixture = await Fixture.CreateAsync();
        var token = await SeedPlan(fixture.Db, "prefix", 2);
        var store = new AiDurableTurnStore(fixture.Db);
        await store.EnqueueAsync("prefix", 1, 2, "execution", "legacy", Request(token), null, default);
        var first = (await store.ClaimAsync(default))!;
        var effects = new List<int>();
        using var shutdown = new CancellationTokenSource();
        var initial = new ScriptedProducer((job, resume, ct) =>
            Execution(effects, resume.Steps.Count, 2, shutdown, false, false, token, ct));
        var log = new TestLog();
        using (var services = fixture.Services(initial))
            await new AiDurableTurnWorker(services.GetRequiredService<IServiceScopeFactory>(), log)
                .RunClaimedAsync(first, shutdown.Token);
        Assert.Empty(log.Warnings);
        Assert.Equal(new[] { 0 }, effects);
        await using var reboot = fixture.Context();
        var saved = await reboot.aiTurnJobs.SingleAsync();
        Assert.Equal("running", saved.status);
        Assert.Null(saved.startedStep);
        Assert.False(saved.cancelRequested);
        Assert.Single(JsonSerializer.Deserialize<List<AiExecutionStepCheckpoint>>(
            saved.checkpointsJson, AiDurableTurnStore.Json)!);
        var second = (await new AiDurableTurnStore(reboot).ClaimAsync(default))!;
        var resumed = new ScriptedProducer((job, resume, ct) =>
        {
            Assert.Equal("legacy", job.engine);
            Assert.Single(resume.Steps);
            return Execution(effects, resume.Steps.Count, 2, null, false, false, token, ct);
        });
        using (var services = fixture.Services(resumed))
            await new AiDurableTurnWorker(services.GetRequiredService<IServiceScopeFactory>(), log)
                .RunClaimedAsync(second, default);
        Assert.Empty(log.Warnings);
        Assert.Equal(new[] { 0, 1 }, effects);
        Assert.Equal(1, initial.Calls);
        Assert.Equal(1, resumed.Calls);
        await using var inspect = fixture.Context();
        Assert.Equal("completed", (await inspect.aiTurnJobs.SingleAsync()).status);
        var row = await inspect.aiConversationMessages.SingleAsync(m => m.role == "assistant");
        Assert.Equal(2, JsonNode.Parse(row.toolCallsJson!)!["tools"]!.AsArray().Count);
        var events = await new AiDurableTurnStore(inspect).ReadEventsAsync(second.Id, default);
        Assert.Equal(new[] { 0, 1 }, events.Where(e => e.Type == SseEventType.Tool
            && e.Data.GetProperty("status").GetString() == "running")
            .Select(e => e.Data.GetProperty("stepIndex").GetInt32()));
        Assert.Equal(Enumerable.Range(1, events.Count).Select(i => (long?)i), events.Select(e => e.Sequence));
    }

    [Fact]
    public async Task RestartAfterAnExternalEffectButBeforeItsResult_NeverInvokesTheProducerAgain()
    {
        await using var fixture = await Fixture.CreateAsync();
        var token = await SeedPlan(fixture.Db, "ambiguous-effect");
        var store = new AiDurableTurnStore(fixture.Db);
        await store.EnqueueAsync("ambiguous-effect", 1, 2, "execution", "legacy", Request(token), null, default);
        var first = (await store.ClaimAsync(default))!;
        var effects = new List<int>();
        using var shutdown = new CancellationTokenSource();
        var producer = new ScriptedProducer((_, resume, ct) =>
            Execution(effects, resume.Steps.Count, 1, shutdown, true, false, token, ct));
        var log = new TestLog();
        using (var services = fixture.Services(producer))
            await new AiDurableTurnWorker(services.GetRequiredService<IServiceScopeFactory>(), log)
                .RunClaimedAsync(first, shutdown.Token);
        Assert.Equal(new[] { 0 }, effects);
        await using var reboot = fixture.Context();
        var second = (await new AiDurableTurnStore(reboot).ClaimAsync(default))!;
        using (var services = fixture.Services(producer))
            await new AiDurableTurnWorker(services.GetRequiredService<IServiceScopeFactory>(), log)
                .RunClaimedAsync(second, default);
        Assert.Empty(log.Warnings);
        Assert.Equal(1, producer.Calls);
        Assert.Equal(new[] { 0 }, effects);
        await using var inspect = fixture.Context();
        Assert.Equal("interrupted", (await inspect.aiTurnJobs.SingleAsync()).status);
    }

    [Fact]
    public async Task InterruptedReview_DoesNotReopenOrRewriteTheCompletedExecution()
    {
        await using var fixture = await Fixture.CreateAsync();
        var token = await SeedPlan(fixture.Db, "review-crash");
        var store = new AiDurableTurnStore(fixture.Db);
        await store.EnqueueAsync("review-crash", 1, 2, "execution", "legacy", Request(token), null, default);
        var first = (await store.ClaimAsync(default))!;
        var effects = new List<int>();
        using var shutdown = new CancellationTokenSource();
        var producer = new ScriptedProducer((_, resume, ct) =>
            Execution(effects, resume.Steps.Count, 1, shutdown, false, true, token, ct));
        var log = new TestLog();
        using (var services = fixture.Services(producer))
            await new AiDurableTurnWorker(services.GetRequiredService<IServiceScopeFactory>(), log)
                .RunClaimedAsync(first, shutdown.Token);
        await using var reboot = fixture.Context();
        var execution = await reboot.aiConversationMessages.AsNoTracking()
            .OrderBy(m => m.Id).FirstAsync(m => m.role == "assistant");
        var original = execution.toolCallsJson;
        Assert.True(AiConversationTurnStore.HasExecutedPlan(original));
        var second = (await new AiDurableTurnStore(reboot).ClaimAsync(default))!;
        using (var services = fixture.Services(producer))
            await new AiDurableTurnWorker(services.GetRequiredService<IServiceScopeFactory>(), log)
                .RunClaimedAsync(second, default);
        Assert.Empty(log.Warnings);
        Assert.Equal(1, producer.Calls);
        await using var inspect = fixture.Context();
        var rows = await inspect.aiConversationMessages.AsNoTracking()
            .Where(m => m.role == "assistant").OrderBy(m => m.Id).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(original, rows[0].toolCallsJson);
        Assert.Contains("Partial review", rows[1].content);
        Assert.Contains("\"executionProposalId\"", rows[1].toolCallsJson);
        Assert.DoesNotContain("\"planConfirmed\"", rows[1].toolCallsJson);
        Assert.Equal("interrupted", (await inspect.aiTurnJobs.SingleAsync()).status);
    }

    private static async IAsyncEnumerable<AiTurnEvent> Execution(
        List<int> effects, int first, int total, CancellationTokenSource? shutdown,
        bool crashBeforeResult, bool review, string proposal,
        [EnumeratorCancellation] CancellationToken ct)
    {
        for (var index = first; index < total; index++)
        {
            yield return AiTurnEvent.Create(SseEventType.Tool, new
            {
                id = $"step-{index}", name = "run_python", status = "running",
                input = """{"code":"print('once')"}""", stepIndex = index,
            });
            effects.Add(index);
            if (crashBeforeResult)
            {
                shutdown!.Cancel();
                ct.ThrowIfCancellationRequested();
            }
            var outcome = JsonSerializer.SerializeToElement(new
            {
                step = index + 1, tool = "run_python", description = "Compute once", status = "completed",
                summary = "done", result = new { exitCode = 0, stdout = "once" },
                counts = AgentResultCounts.Unknown,
            }, AiDurableTurnStore.Json);
            yield return AiTurnEvent.Create(SseEventType.Tool, new
            {
                id = $"step-{index}", name = "run_python", status = "done", summary = "done",
                input = """{"code":"print('once')"}""", result = """{"exitCode":0,"stdout":"once"}""",
                counts = AgentResultCounts.Unknown, stepIndex = index, executionOutcome = outcome,
            });
            if (shutdown != null && !review)
            {
                shutdown.Cancel();
                ct.ThrowIfCancellationRequested();
            }
        }
        yield return AiTurnEvent.Create(SseEventType.ExecutionStatus, new
        {
            status = "completed", succeeded = total, total, queued = 0, failed = 0, skipped = 0,
            outcomeUnknown = false, summary = "Completed",
        });
        yield return AiTurnEvent.Create(SseEventType.Done, new { ok = true, failedCount = 0, cancelled = false });
        if (review)
        {
            yield return AiTurnEvent.Create(SseEventType.AssistantStart, new { executionProposalId = proposal });
            yield return AiTurnEvent.Create(SseEventType.Text, new { delta = "Partial review" });
            shutdown!.Cancel();
            ct.ThrowIfCancellationRequested();
        }
        await Task.CompletedTask;
    }

    private sealed class ScriptedProducer(
        Func<AiTurnJob, AiExecutionResume, CancellationToken, IAsyncEnumerable<AiTurnEvent>> run)
        : IAiDurableTurnProducer
    {
        public int Calls { get; private set; }
        public IAsyncEnumerable<AiTurnEvent> RunAsync(
            AiTurnJob job, AiDurableTurnRequest request, AiExecutionResume resume, CancellationToken ct)
        {
            Calls++;
            return run(job, resume, ct);
        }
    }

    private static AiTurnEvent Running(AiTurnJob job,
        AiConversationTurnStore.AiAssistantTurnBinding binding, long sequence) =>
        AiTurnEvent.Create(SseEventType.Tool, new
        {
            id = "once", name = "run_python", status = "running",
            input = """{"code":"print('once')"}""", stepIndex = 0,
        }) with
        {
            RunId = job.runId, MessageId = binding.MessageId, DbId = binding.DbId,
            Phase = "execution", ProposalId = binding.ProposalId, Sequence = sequence,
        };

    private static AiDurableTurnRequest Request(string? proposal = null) =>
        new([], null, null, "en", proposal, []);

    private static ChatMessage User(string text) =>
        new() { role = "user", content = JsonSerializer.SerializeToElement(text) };

    private static async Task<string> SeedPlan(DataContext db, string session, int count = 1)
    {
        var conversation = await AiConversationTurnStore.EnsureConversationAsync(db, session, 1, 2, default);
        var steps = JsonNode.Parse(
            """[{"tool":"run_python","description":"Compute once","args":{"code":"print('once')"}}]""")!.AsArray();
        while (steps.Count < count) steps.Add(steps[0]!.DeepClone());
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(15);
        var hash = AiConversationTurnStore.ComputeStepsHash(steps);
        var (token, nonce) = AiPlanProposalToken.Mint(session, 1, 2, hash, expires);
        db.aiConversationMessages.Add(new AiConversationMessage
        {
            conversationId = conversation.Id, role = "assistant", content = "Confirm",
            toolCallsJson = new JsonObject
            {
                ["_ui"] = "assistantTurn", ["tools"] = new JsonArray(), ["timeline"] = new JsonArray(),
                ["plan"] = new JsonObject
                {
                    ["proposalId"] = token, ["proposalNonce"] = nonce, ["stepsHash"] = hash,
                    ["createdAtUtc"] = now, ["expiresAtUtc"] = expires, ["steps"] = steps,
                    ["affectedCount"] = 99,
                },
            }.ToJsonString(),
        });
        await db.SaveChangesAsync();
        return token;
    }

    private sealed class FailPreparedJob : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<AiTurnJob>()
                .Any(e => e.Entity.requestJson != "{}"))
                throw new InvalidOperationException("injected final admission save failure");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class TestLog : ILogger<AiDurableTurnWorker>
    {
        public List<string> Warnings { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning) Warnings.Add(formatter(state, exception));
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly Microsoft.Data.Sqlite.SqliteConnection _keeper;
        private readonly string? _oldKey;
        public DataContext Db { get; }
        public string ConnectionString => _keeper.ConnectionString;

        private Fixture()
        {
            _oldKey = Environment.GetEnvironmentVariable("ENCRYPTION_KEY");
            Environment.SetEnvironmentVariable("ENCRYPTION_KEY",
                Convert.ToBase64String(Enumerable.Range(1, 32).Select(x => (byte)x).ToArray()));
            (Db, _keeper) = TestDbFactory.CreateSqliteDb();
        }

        public static Task<Fixture> CreateAsync() => Task.FromResult(new Fixture());
        public DataContext Context() => TestDbFactory.CreateSqliteContext(ConnectionString);
        public Task<AiTurnJob> EnqueueChat(string session) =>
            new AiDurableTurnStore(Db).EnqueueAsync(session, 1, 2, "chat", "legacy",
                Request(), User("First input"), default);
        public ServiceProvider Services(IAiDurableTurnProducer? producer = null)
        {
            var services = new ServiceCollection();
            services.AddScoped(_ => Context());
            services.AddScoped<AiDurableTurnStore>();
            if (producer != null) services.AddSingleton(producer);
            return services.BuildServiceProvider();
        }
        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _keeper.DisposeAsync();
            Environment.SetEnvironmentVariable("ENCRYPTION_KEY", _oldKey);
        }
    }
}
