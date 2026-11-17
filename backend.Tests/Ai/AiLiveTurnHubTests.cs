using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Delegat;
using Delegat.Models.Ai;
using Delegat.Services.Ai;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

[Collection("AiPlanSigningKey")]
public class AiLiveTurnHubTests
{
    [Fact]
    public void Recorder_DiscardsUnexecutedResultTextWhenAPlanIsProposed()
    {
        var snapshot = AiSseTurnRecorder.Fold([
            AiSseTurnRecorder.Format(SseEventType.Text, new { delta = "Invented calculation table" }),
            AiSseTurnRecorder.Format(SseEventType.Plan, new { proposalId = "pending", summary = "Compute results", discardDraftText = true }),
            AiSseTurnRecorder.Format(SseEventType.Done, new { }),
        ]);
        Assert.Equal("", snapshot.Content);
        Assert.DoesNotContain("Invented", snapshot.ToolCallsJson);
        Assert.Contains("pending", snapshot.ToolCallsJson);
    }

    [Fact]
    public async Task PlanFollowup_PersistsFailedOutcomeAndNewProposalAsSeparateMessages()
    {
        using var hub = CreateHub(out var db);
        await SeedConfirmedPlan(db);
        Assert.True(hub.TryStart("s", 1, 1, null, FailedPlanWithRecovery, "original"));
        await WaitUntil(() => !hub.IsGenerating("s", 1, 1));
        var rows = db.aiConversationMessages.Where(m => m.role == "assistant").OrderBy(m => m.Id).ToList();
        Assert.Equal(2, rows.Count);
        using var failed = JsonDocument.Parse(rows[0].toolCallsJson!);
        using var recovery = JsonDocument.Parse(rows[1].toolCallsJson!);
        Assert.Equal("original", failed.RootElement.GetProperty("plan").GetProperty("proposalId").GetString());
        Assert.True(failed.RootElement.GetProperty("planFailed").GetBoolean());
        Assert.True(failed.RootElement.GetProperty("planExecuted").GetBoolean());
        Assert.Equal("replacement", recovery.RootElement.GetProperty("plan").GetProperty("proposalId").GetString());
        Assert.Equal("original", recovery.RootElement.GetProperty("executionProposalId").GetString());
        Assert.False(recovery.RootElement.TryGetProperty("planConfirmed", out _));
    }

    [Fact]
    public async Task StopDuringReview_DoesNotStopOrReopenTheAlreadyCompletedPlan()
    {
        using var hub = CreateHub(out var db);
        await SeedConfirmedPlan(db);
        Assert.True(hub.TryStart("s", 1, 1, null, ReviewAfterCompletedPlan, "original"));
        var turn = hub.Find("s", 1, 1)!;
        await WaitUntil(() => turn.SnapshotChunks().Any(c => c.Contains("Analyzing result")));
        Assert.True(hub.Stop("s", 1, 1));
        await WaitUntil(() => !hub.IsGenerating("s", 1, 1));
        var rows = db.aiConversationMessages.Where(m => m.role == "assistant").OrderBy(m => m.Id).ToList();
        Assert.Equal(2, rows.Count);
        Assert.True(AiConversationTurnStore.HasExecutedPlan(rows[0].toolCallsJson));
        Assert.False(AiConversationTurnStore.HasStoppedTurn(rows[0].toolCallsJson));
        Assert.True(AiConversationTurnStore.HasStoppedTurn(rows[1].toolCallsJson));
    }

    private static async Task SeedConfirmedPlan(DataContext db)
    {
        await AiConversationTurnStore.AppendUserIfNeededAsync(db, "s", 1, 1, User("Calculate"), default);
        await AiConversationTurnStore.UpsertAssistantAsync(db, "s", 1, 1,
            new("", """{"_ui":"assistantTurn","plan":{"proposalId":"original","summary":"Calculate"},"planConfirmed":true,"tools":[],"timeline":[]}""", null), default);
    }

    private static async IAsyncEnumerable<AiTurnEvent> FailedPlanWithRecovery(IServiceProvider _, [EnumeratorCancellation] CancellationToken ct)
    {
        yield return AiTurnEvent.Create(SseEventType.Done, new { ok = false });
        yield return AiTurnEvent.Create(SseEventType.AssistantStart, new { executionProposalId = "original" });
        yield return AiTurnEvent.Create(SseEventType.Plan, new { proposalId = "replacement", summary = "Correct calculation", discardDraftText = true });
        yield return AiTurnEvent.Create(SseEventType.Done, new { });
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<AiTurnEvent> ReviewAfterCompletedPlan(IServiceProvider _, [EnumeratorCancellation] CancellationToken ct)
    {
        yield return AiTurnEvent.Create(SseEventType.Done, new { ok = true });
        yield return AiTurnEvent.Create(SseEventType.AssistantStart, new { executionProposalId = "original" });
        yield return AiTurnEvent.Create(SseEventType.Text, new { delta = "Analyzing result" });
        await Task.Delay(Timeout.Infinite, ct);
    }

    [Theory]
    [InlineData("", false, false)]
    [InlineData("done", false, false)]
    [InlineData("error", false, false)]
    [InlineData("success", true, false)]
    [InlineData("failure", true, true)]
    public async Task PlanOutcome_RequiresExplicitTerminalOk(string ending, bool completed, bool failed)
    {
        using var db = TestDbFactory.CreateDb();
        await AiConversationTurnStore.AppendUserIfNeededAsync(db, "outcome", 1, 1, User("prepare"), CancellationToken.None);
        const string proposal = """{"_ui":"assistantTurn","plan":{"proposalId":"p1","summary":"Update"},"planConfirmed":true,"tools":[],"timeline":[{"type":"text","text":"Ready."}]}""";
        await AiConversationTurnStore.UpsertAssistantAsync(db, "outcome", 1, 1, new("Ready.", proposal, null), CancellationToken.None);
        var chunks = new List<string> {
            AiSseTurnRecorder.Format(SseEventType.Tool, new { id = "t1", name = "platform_request", status = "running", input = "{}" }),
        };
        if (ending == "done") chunks.Add(AiSseTurnRecorder.Format(SseEventType.Done, new { }));
        if (ending == "error") chunks.Add(AiSseTurnRecorder.Format(SseEventType.Error, new { code = "internal_error" }));
        if (ending is "success" or "failure") chunks.Add(AiSseTurnRecorder.Format(SseEventType.Done, new { ok = ending == "success" }));
        await AiConversationTurnStore.UpsertAssistantAsync(db, "outcome", 1, 1, AiSseTurnRecorder.Fold(chunks), CancellationToken.None);
        var stored = db.aiConversationMessages.Single(m => m.role == "assistant");
        using var json = JsonDocument.Parse(stored.toolCallsJson!);
        Assert.True(json.RootElement.GetProperty("planConfirmed").GetBoolean());
        Assert.Equal(completed, json.RootElement.GetProperty("planExecuted").GetBoolean());
        Assert.Equal(failed, json.RootElement.TryGetProperty("planFailed", out var flag) && flag.GetBoolean());
        if (ending == "error") Assert.Equal("internal_error", json.RootElement.GetProperty("requestError").GetProperty("code").GetString());
    }

    [Fact]
    public void Recorder_FoldsTextToolsAndPlan()
    {
        var chunks = new[]
        {
            AiSseTurnRecorder.Format(SseEventType.Text, new { delta = "Looking. " }),
            AiSseTurnRecorder.Format(SseEventType.Text, new { delta = "Found 2." }),
            AiSseTurnRecorder.Format(SseEventType.Tool, new { id = "t1", name = "search_catalog", status = "running", input = "{}" }),
            AiSseTurnRecorder.Format(SseEventType.Tool, new { id = "t1", name = "search_catalog", status = "done", summary = "2 products", result = """{"n":2}""" }),
            AiSseTurnRecorder.Format(SseEventType.Plan, new { summary = "Upload photos", steps = new[] { new { description = "Match files" } } }),
            AiSseTurnRecorder.Format(SseEventType.Done, new { modelName = "claude" }),
        };

        var snap = AiSseTurnRecorder.Fold(chunks);
        Assert.Equal("Looking. Found 2.", snap.Content);
        Assert.Equal("claude", snap.ModelName);
        Assert.Contains("assistantTurn", snap.ToolCallsJson);
        Assert.Contains("Upload photos", snap.ToolCallsJson);
        Assert.Contains("search_catalog", snap.ToolCallsJson);
        Assert.DoesNotContain("planFailed", snap.ToolCallsJson);
    }

    [Fact]
    public void Recorder_ApiErrorAfterRunningTool_DoesNotMarkPlanFailed()
    {
        var chunks = new[]
        {
            AiSseTurnRecorder.Format(SseEventType.Tool, new
            {
                id = "t1",
                name = "platform_request",
                status = "running",
                input = """{"method":"POST","path":"/marketplace/connections/1/products/import-unlinked"}""",
            }),
            AiSseTurnRecorder.Format(SseEventType.Error, new
            {
                message = "Could not finish this reply. If a catalog action already started, check Operations.",
            }),
            AiSseTurnRecorder.Format(SseEventType.Done, new { }),
        };

        var snap = AiSseTurnRecorder.Fold(chunks);
        Assert.DoesNotContain("planFailed", snap.ToolCallsJson);
        Assert.Contains("interrupted", snap.ToolCallsJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("check Operations", snap.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Recorder_PersistsHostedAiErrorCodeForLocalizedDisplay()
    {
        var snapshot = AiSseTurnRecorder.Fold(new[]
        {
            AiSseTurnRecorder.Format(SseEventType.Error, new
            {
                code = "hosted_ai_quota_exceeded",
                status = 429,
            }),
            AiSseTurnRecorder.Format(SseEventType.Done, new { }),
        });

        Assert.Equal(string.Empty, snapshot.Content);
        Assert.Contains("hosted_ai_quota_exceeded", snapshot.ToolCallsJson);
        Assert.Contains("429", snapshot.ToolCallsJson);
    }

    [Fact]
    public void Recorder_ExecutePlanHttpError_SetsPlanFailedWithoutClaimingSuccess()
    {
        var result = """{"error":"product_limit_reached","status":409,"title":"Product limit reached"}""";
        var chunks = new[]
        {
            AiSseTurnRecorder.Format(SseEventType.Tool, new { id = "s1", name = "platform_request", status = "running", input = "{}" }),
            AiSseTurnRecorder.Format(SseEventType.Tool, new { id = "s1", name = "platform_request", status = "done", summary = "Product limit reached — nothing was created.", result }),
            AiSseTurnRecorder.Format(SseEventType.Text, new { delta = "Nothing was changed (0 of 1 steps succeeded)." }),
            AiSseTurnRecorder.Format(SseEventType.Done, new { ok = false, failedCount = 1 }),
        };

        var snap = AiSseTurnRecorder.Fold(chunks);
        Assert.Contains("planFailed", snap.ToolCallsJson);
        Assert.Contains("Nothing was changed", snap.Content);
        Assert.DoesNotContain("Plan executed", snap.Content);
    }

    [Fact]
    public async Task Subscribe_ReplaysThenTails()
    {
        var turn = new AiLiveTurn("s", 1, 1);
        turn.Publish(AiTurnEvent.Create(SseEventType.Text, new { delta = "a" }));

        var seen = new List<string>();
        var sub = Consume(turn, seen);
        await Task.Delay(30);
        turn.Publish(AiTurnEvent.Create(SseEventType.Text, new { delta = "b" }));
        turn.Complete();
        await sub;

        Assert.Equal(2, seen.Count);
        Assert.Contains("\"delta\":\"a\"", seen[0]);
        Assert.Contains("\"delta\":\"b\"", seen[1]);
    }

    [Fact]
    public void Find_RejectsOtherOwner()
    {
        using var hub = CreateHub(out _);
        Assert.True(hub.TryStart("s", 1, 1, User("hi"), Hang));
        Assert.NotNull(hub.Find("s", 1, 1));
        Assert.Null(hub.Find("s", 1, 99));
        Assert.Null(hub.Find("s", 2, 1));
        hub.Stop("s", 1, 1);
    }

    [Fact]
    public void TryStart_DoesNotReplaceRunningTurn()
    {
        using var hub = CreateHub(out _);
        Assert.True(hub.TryStart("s", 1, 1, User("one"), Hang));
        Assert.False(hub.TryStart("s", 1, 1, User("two"), Hang));
        Assert.True(hub.IsGenerating("s", 1, 1));
        hub.Stop("s", 1, 1);
    }

    [Fact]
    public async Task TryStart_ReplacesCompletedTurn()
    {
        using var hub = CreateHub(out _);
        Assert.True(hub.TryStart("s", 1, 1, User("one"), ImmediateDone));
        await WaitUntil(() => !hub.IsGenerating("s", 1, 1));
        Assert.True(hub.TryStart("s", 1, 1, User("two"), Hang));
        Assert.True(hub.IsGenerating("s", 1, 1));
        hub.Stop("s", 1, 1);
    }

    [Fact]
    public async Task Stop_AcceptedPlanPersistsUnknownOutcomeAndCannotBeConsumedAgain()
    {
        using var hub = CreateHub(out var db);
        await AiConversationTurnStore.AppendUserIfNeededAsync(db, "s", 1, 1, User("prepare"), CancellationToken.None);
        const string proposal = """{"_ui":"assistantTurn","plan":{"proposalId":"p1","summary":"Update"},"planConfirmed":true,"tools":[],"timeline":[{"type":"text","text":"Ready."}]}""";
        await AiConversationTurnStore.UpsertAssistantAsync(db, "s", 1, 1, new("Ready.", proposal, null), CancellationToken.None);
        Assert.True(hub.TryStart("s", 1, 1, null, HangAfterText, "p1"));
        await WaitUntil(() => hub.Find("s", 1, 1)?.SnapshotChunks().Any(c => c.Contains("partial")) == true);
        Assert.True(hub.Stop("s", 1, 1));
        await WaitUntil(() => !hub.IsGenerating("s", 1, 1));
        var assistant = db.aiConversationMessages.Single(m => m.role == "assistant");
        using var json = JsonDocument.Parse(assistant.toolCallsJson!);
        Assert.True(json.RootElement.GetProperty("planConfirmed").GetBoolean());
        Assert.False(json.RootElement.GetProperty("planExecuted").GetBoolean());
        Assert.True(json.RootElement.GetProperty("turnStopped").GetBoolean());
        var retry = await AiConversationTurnStore.ConsumePendingPlanDetailedAsync(db, "s", "p1", 1, 1, CancellationToken.None);
        Assert.Null(retry.Steps);
        Assert.Equal("already_processed", retry.RejectionReason);
    }

    [Fact]
    public async Task StoppedUnconfirmedProposalCannotBeConsumed()
    {
        using var db = TestDbFactory.CreateDb();
        await AiConversationTurnStore.AppendUserIfNeededAsync(db, "stopped-proposal", 1, 1, User("prepare"), CancellationToken.None);
        const string proposal = """{"_ui":"assistantTurn","plan":{"proposalId":"p1","summary":"Update"},"turnStopped":true,"tools":[],"timeline":[]}""";
        await AiConversationTurnStore.UpsertAssistantAsync(db, "stopped-proposal", 1, 1, new("", proposal, null), CancellationToken.None);
        var result = await AiConversationTurnStore.ConsumePendingPlanDetailedAsync(db, "stopped-proposal", "p1", 1, 1, CancellationToken.None);
        Assert.Null(result.Steps);
        Assert.Equal("plan_cancelled", result.RejectionReason);
    }

    [Fact]
    public async Task Stop_PersistsPartialAssistant()
    {
        using var hub = CreateHub(out var db);
        Assert.True(hub.TryStart("s", 1, 1, User("hello"), HangAfterText));
        await WaitUntil(() => hub.Find("s", 1, 1)?.SnapshotChunks().Any(c => c.Contains("partial")) == true);
        Assert.True(hub.Stop("s", 1, 1));
        await WaitUntil(() => !hub.IsGenerating("s", 1, 1));

        var messages = db.aiConversationMessages.OrderBy(m => m.Id).ToList();
        Assert.Contains(messages, m => m.role == "user" && m.content == "hello");
        Assert.Contains(messages, m => m.role == "assistant" && m.content.Contains("partial"));
        var assistant = messages.Single(m => m.role == "assistant");
        using var json = JsonDocument.Parse(assistant.toolCallsJson!);
        Assert.True(json.RootElement.GetProperty("turnStopped").GetBoolean());
        Assert.Contains(hub.Find("s", 1, 1)!.SnapshotChunks(), chunk => chunk.Contains("\"type\":\"stopped\""));
    }

    [Fact]
    public async Task Stop_BeforeFirstTokenPersistsAnEmptyStoppedReply()
    {
        using var hub = CreateHub(out var db);
        Assert.True(hub.TryStart("s", 1, 1, User("hello"), Hang));
        Assert.True(hub.Stop("s", 1, 1));
        await WaitUntil(() => !hub.IsGenerating("s", 1, 1));
        var assistant = db.aiConversationMessages.Single(m => m.role == "assistant");
        Assert.Equal("", assistant.content);
        Assert.True(AiConversationTurnStore.HasStoppedTurn(assistant.toolCallsJson));
    }

    [Fact]
    public async Task DisconnectingSubscriberDoesNotStopTheProducer()
    {
        using var hub = CreateHub(out _);
        Assert.True(hub.TryStart("s", 1, 1, User("hello"), HangAfterText));
        await WaitUntil(() => hub.Find("s", 1, 1)?.SnapshotChunks().Any(c => c.Contains("partial")) == true);
        var turn = hub.Find("s", 1, 1)!;
        using var subscription = new CancellationTokenSource();
        await using (var reader = turn.Subscribe(subscription.Token).GetAsyncEnumerator())
        {
            Assert.True(await reader.MoveNextAsync());
            subscription.Cancel();
        }
        Assert.True(hub.IsGenerating("s", 1, 1));
        Assert.False(turn.StopRequested);
        await using var resumed = turn.Subscribe().GetAsyncEnumerator();
        var sawPartial = false;
        for (var i = 0; i < 8 && !sawPartial; i++)
        {
            var move = resumed.MoveNextAsync().AsTask();
            if (!await move.WaitAsync(TimeSpan.FromSeconds(5)) || !await move)
                break;
            sawPartial |= resumed.Current.Contains("partial");
        }
        Assert.True(sawPartial);
        hub.Stop("s", 1, 1);
    }

    [Fact]
    public void Recorder_CancelledPlanKeepsStoppedToolsInsteadOfReportingSuccess()
    {
        var snapshot = AiSseTurnRecorder.Fold([
            AiSseTurnRecorder.Format(SseEventType.Tool, new { id = "t1", name = "run_python", status = "running" }),
            AiSseTurnRecorder.Format(SseEventType.Done, new { ok = false, cancelled = true }),
        ]);
        using var json = JsonDocument.Parse(snapshot.ToolCallsJson!);
        Assert.True(snapshot.TurnEnded);
        Assert.True(json.RootElement.GetProperty("turnStopped").GetBoolean());
        Assert.False(json.RootElement.TryGetProperty("planExecuted", out _));
        Assert.Equal("stopped", json.RootElement.GetProperty("tools")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task CompletedTurn_PersistsAssistantAndStopsGenerating()
    {
        using var hub = CreateHub(out var db);
        Assert.True(hub.TryStart("s", 1, 1, User("hi"), ImmediateDone));
        await WaitUntil(() => !hub.IsGenerating("s", 1, 1));

        var assistant = db.aiConversationMessages.Single(m => m.role == "assistant");
        Assert.Equal("done-text", assistant.content);
        Assert.False(hub.IsGenerating("s", 1, 1));
    }

    [Fact]
    public void Stop_WrongOwner_IsNoop()
    {
        using var hub = CreateHub(out _);
        Assert.True(hub.TryStart("s", 1, 1, User("hi"), Hang));
        Assert.False(hub.Stop("s", 1, 99));
        Assert.True(hub.IsGenerating("s", 1, 1));
        hub.Stop("s", 1, 1);
    }

    [Fact]
    public async Task TurnStore_UpdatesLastAssistantInsteadOfDuplicating()
    {
        using var db = TestDbFactory.CreateDb();
        var user = User("q");
        await AiConversationTurnStore.AppendUserIfNeededAsync(db, "s", 1, 1, user, CancellationToken.None);
        await AiConversationTurnStore.AppendUserIfNeededAsync(db, "s", 1, 1, user, CancellationToken.None);
        Assert.Equal(1, db.aiConversationMessages.Count(m => m.role == "user"));

        await AiConversationTurnStore.UpsertAssistantAsync(
            db, "s", 1, 1, new AiSseTurnSnapshot("one", null, "m"), CancellationToken.None);
        await AiConversationTurnStore.UpsertAssistantAsync(
            db, "s", 1, 1, new AiSseTurnSnapshot("two", null, "m"), CancellationToken.None);

        var assistants = db.aiConversationMessages.Where(m => m.role == "assistant").ToList();
        Assert.Single(assistants);
        Assert.Equal("two", assistants[0].content);
    }

    [Fact]
    public async Task TurnStore_SkipsConfirmedProceedUserLine()
    {
        using var db = TestDbFactory.CreateDb();
        var confirm = User(AiConversationTurnStore.ConfirmedPlanUserText);
        await AiConversationTurnStore.AppendUserIfNeededAsync(
            db, "s", 1, 1, confirm, CancellationToken.None);
        Assert.Empty(db.aiConversationMessages);
    }

    [Fact]
    public async Task TurnStore_MergesExecutePlanIntoExistingPlanAssistant()
    {
        using var db = TestDbFactory.CreateDb();
        await AiConversationTurnStore.AppendUserIfNeededAsync(
            db, "s", 1, 1, User("please upload"), CancellationToken.None);
        const string planJson =
            """{"_ui":"assistantTurn","tools":[],"timeline":[{"type":"text","text":"Ready."}],"plan":{"summary":"Upload photos"}}""";
        await AiConversationTurnStore.UpsertAssistantAsync(
            db, "s", 1, 1,
            new AiSseTurnSnapshot("Ready.", planJson, "m"),
            CancellationToken.None);

        const string execJson =
            """{"_ui":"assistantTurn","planExecuted":true,"tools":[{"id":"t1","name":"platform_request"}],"timeline":[{"type":"tool","id":"t1"},{"type":"text","text":"Plan executed."}]}""";
        await AiConversationTurnStore.UpsertAssistantAsync(
            db, "s", 1, 1,
            new AiSseTurnSnapshot("Plan executed.", execJson, "m"),
            CancellationToken.None);

        var assistants = db.aiConversationMessages.Where(m => m.role == "assistant").ToList();
        Assert.Single(assistants);
        Assert.Contains("Ready.", assistants[0].content);
        Assert.Contains("Plan executed.", assistants[0].content);
        Assert.Contains("Upload photos", assistants[0].toolCallsJson);
        Assert.Contains("planExecuted", assistants[0].toolCallsJson);
        Assert.Contains("platform_request", assistants[0].toolCallsJson);
        Assert.DoesNotContain("planFailed", assistants[0].toolCallsJson);
        Assert.DoesNotContain(
            db.aiConversationMessages,
            m => m.role == "user" && m.content == AiConversationTurnStore.ConfirmedPlanUserText);
    }

    [Fact]
    public async Task TurnStore_MergesFailedExecutePlan_SetsPlanFailed()
    {
        using var db = TestDbFactory.CreateDb();
        await AiConversationTurnStore.AppendUserIfNeededAsync(
            db, "s", 1, 1, User("create a product"), CancellationToken.None);
        const string planJson =
            """{"_ui":"assistantTurn","tools":[],"timeline":[{"type":"text","text":"Ready."}],"plan":{"summary":"Create a test product"}}""";
        await AiConversationTurnStore.UpsertAssistantAsync(
            db, "s", 1, 1,
            new AiSseTurnSnapshot("Ready.", planJson, "m"),
            CancellationToken.None);

        const string execJson =
            """{"_ui":"assistantTurn","planExecuted":true,"tools":[{"id":"t1","name":"platform_request","summary":"Product limit reached — nothing was created.","result":{"error":"product_limit_reached","status":409}}],"timeline":[{"type":"tool","id":"t1"}],"planFailed":true}""";
        await AiConversationTurnStore.UpsertAssistantAsync(
            db, "s", 1, 1,
            new AiSseTurnSnapshot("Nothing was changed (0 of 1 steps succeeded).", execJson, "m"),
            CancellationToken.None);

        var assistant = db.aiConversationMessages.Single(m => m.role == "assistant");
        Assert.Contains("planExecuted", assistant.toolCallsJson);
        Assert.Contains("planFailed", assistant.toolCallsJson);
        Assert.Contains("Nothing was changed", assistant.content);
        Assert.DoesNotContain("Plan executed", assistant.content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TurnStore_DoesNotDuplicateAfterPlanAlreadyExecuted()
    {
        using var db = TestDbFactory.CreateDb();
        await AiConversationTurnStore.AppendUserIfNeededAsync(
            db, "s", 1, 1, User("please"), CancellationToken.None);
        const string planJson =
            """{"_ui":"assistantTurn","tools":[],"timeline":[{"type":"text","text":"Ready."}],"plan":{"summary":"Do it"}}""";
        await AiConversationTurnStore.UpsertAssistantAsync(
            db, "s", 1, 1,
            new AiSseTurnSnapshot("Ready.", planJson, "m"),
            CancellationToken.None);
        const string execJson =
            """{"_ui":"assistantTurn","planExecuted":true,"tools":[],"timeline":[{"type":"text","text":"Plan executed."}]}""";
        await AiConversationTurnStore.UpsertAssistantAsync(
            db, "s", 1, 1,
            new AiSseTurnSnapshot("Plan executed.", execJson, "m"),
            CancellationToken.None);
        await AiConversationTurnStore.UpsertAssistantAsync(
            db, "s", 1, 1,
            new AiSseTurnSnapshot("again", execJson, "m"),
            CancellationToken.None);

        var assistant = db.aiConversationMessages.Single(m => m.role == "assistant");
        Assert.Equal("Ready.\n\nPlan executed.", assistant.content);
        Assert.DoesNotContain("again", assistant.content);
    }

    [Fact]
    public async Task ExecutePlanTurn_MergesWithoutConfirmUser()
    {
        using var handle = CreateHub(out var db);
        await AiConversationTurnStore.AppendUserIfNeededAsync(
            db, "s", 1, 1, User("please"), CancellationToken.None);
        const string planJson =
            """{"_ui":"assistantTurn","tools":[],"timeline":[{"type":"text","text":"Ready."}],"plan":{"summary":"Upload","proposalId":"p1"},"planConfirmed":true}""";
        await AiConversationTurnStore.UpsertAssistantAsync(
            db, "s", 1, 1,
            new AiSseTurnSnapshot("Ready.", planJson, "m"),
            CancellationToken.None);

        Assert.True(handle.TryStart("s", 1, 1, null, ImmediateDone, "p1"));
        await WaitUntil(() => !handle.IsGenerating("s", 1, 1));

        var messages = db.aiConversationMessages.OrderBy(m => m.Id).ToList();
        Assert.DoesNotContain(
            messages,
            m => m.role == "user" && m.content == AiConversationTurnStore.ConfirmedPlanUserText);
        Assert.Single(messages, m => m.role == "assistant");
        var assistant = messages.Single(m => m.role == "assistant");
        Assert.Contains("Ready.", assistant.content);
        Assert.Contains("done-text", assistant.content);
        Assert.Contains("planExecuted", assistant.toolCallsJson!);
        Assert.Contains("Upload", assistant.toolCallsJson);
    }

    private static ChatMessage User(string text) => new()
    {
        role = "user",
        content = JsonSerializer.SerializeToElement(text),
    };

    private static async IAsyncEnumerable<AiTurnEvent> Hang(
        IServiceProvider _,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Delay(Timeout.Infinite, ct);
        yield break;
    }

    private static async IAsyncEnumerable<AiTurnEvent> HangAfterText(
        IServiceProvider _,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return AiTurnEvent.Create(SseEventType.Text, new { delta = "partial" });
        await Task.Delay(Timeout.Infinite, ct);
    }

    private static async IAsyncEnumerable<AiTurnEvent> ImmediateDone(
        IServiceProvider _,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return AiTurnEvent.Create(SseEventType.Text, new { delta = "done-text" });
        yield return AiTurnEvent.Create(SseEventType.Done, new { modelName = "x", ok = true });
        await Task.CompletedTask;
    }

    [Fact]
    public async Task HeldReservation_BlocksSecondReserve_WithoutConsumingPendingPlan()
    {
        // Admission reserves the session slot BEFORE the signed plan is
        // consumed: a second reservation is refused while the pending token
        // stays consumable, and a released reservation reopens the slot.
        var originalKey = Environment.GetEnvironmentVariable("ENCRYPTION_KEY");
        Environment.SetEnvironmentVariable(
            "ENCRYPTION_KEY",
            Convert.ToBase64String(Enumerable.Range(1, 32).Select(x => (byte)x).ToArray()));
        try
        {
            using var hub = CreateHub(out var db);
            var conversation = await AiConversationTurnStore.EnsureConversationAsync(
                db, "s", 1, 1, CancellationToken.None);
            var steps = System.Text.Json.Nodes.JsonNode.Parse(
                """[{"tool":"platform_request","description":"Update item","args":{"method":"PATCH","path":"/products/7","body":{"name":"new"}}}]""")!
                .AsArray();
            var createdAt = DateTime.UtcNow;
            var expiresAt = createdAt.AddMinutes(15);
            var stepsHash = AiConversationTurnStore.ComputeStepsHash(steps);
            var (proposalToken, proposalNonce) = AiPlanProposalToken.Mint(
                "s", 1, 1, stepsHash, expiresAt);
            db.aiConversationMessages.Add(new AiConversationMessage
            {
                conversationId = conversation.Id,
                role = "assistant",
                content = "Review this plan",
                toolCallsJson = new System.Text.Json.Nodes.JsonObject
                {
                    ["_ui"] = "assistantTurn",
                    ["tools"] = new System.Text.Json.Nodes.JsonArray(),
                    ["timeline"] = new System.Text.Json.Nodes.JsonArray(),
                    ["plan"] = new System.Text.Json.Nodes.JsonObject
                    {
                        ["proposalId"] = proposalToken,
                        ["proposalNonce"] = proposalNonce,
                        ["stepsHash"] = stepsHash,
                        ["createdAtUtc"] = createdAt,
                        ["expiresAtUtc"] = expiresAt,
                        ["steps"] = steps.DeepClone(),
                    },
                }.ToJsonString(),
                createdAt = createdAt,
            });
            await db.SaveChangesAsync();

            var reserved = hub.Hub.TryReserve("s", 1, 1, proposalToken);
            Assert.NotNull(reserved);
            Assert.Equal(proposalToken, reserved.ExecutionProposalId);
            // A second admission for the same session is refused while the
            // reservation is held - and nothing has consumed the plan yet.
            Assert.Null(hub.Hub.TryReserve("s", 1, 1, proposalToken));
            var attempt = await AiConversationTurnStore.ConsumePendingPlanDetailedAsync(
                db, "s", proposalToken, 1, 1, CancellationToken.None);
            Assert.NotNull(attempt.Steps);

            hub.Hub.ReleaseReservation(reserved);
            Assert.True(hub.TryStart("s", 1, 1, User("retry"), Hang));
            hub.Stop("s", 1, 1);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ENCRYPTION_KEY", originalKey);
        }
    }

    [Fact]
    public async Task StartReserved_Twice_LaunchesTheProducerOnce()
    {
        using var hub = CreateHub(out _);
        var reserved = hub.Hub.TryReserve("s", 1, 1)!;
        var launches = 0;
        IAsyncEnumerable<AiTurnEvent> Producer(IServiceProvider sp, CancellationToken ct)
        {
            Interlocked.Increment(ref launches);
            return Hang(sp, ct);
        }
        hub.Hub.StartReserved(reserved, User("hi"), Producer);
        hub.Hub.StartReserved(reserved, User("hi"), Producer);
        await WaitUntil(() => reserved.SnapshotChunks().Count > 0);
        hub.Stop("s", 1, 1);
        await WaitUntil(() => !hub.IsGenerating("s", 1, 1));
        Assert.Equal(1, launches);
    }

    [Fact]
    public async Task Publish_ConcurrentWritersAndComplete_DeliverEachEventOnceInOrder()
    {
        var turn = new AiLiveTurn("s", 1, 1);
        turn.BindMessage("m1", 1, "chat");
        var subscribers = Enumerable.Range(0, 3)
            .Select(_ =>
            {
                var list = new List<string>();
                return (list, task: Consume(turn, list));
            })
            .ToList();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writers = Enumerable.Range(0, 4)
            .Select(w => Task.Run(async () =>
            {
                await gate.Task;
                for (var i = 0; i < 25; i++)
                    turn.Publish(AiTurnEvent.Create(
                        SseEventType.Text, new { delta = $"w{w}-{i}" }));
            }))
            .ToList();
        gate.SetResult();
        await Task.WhenAll(writers);
        turn.Complete();
        await Task.WhenAll(subscribers.Select(s => s.task));

        foreach (var (list, _) in subscribers)
        {
            Assert.Equal(100, list.Count);
            var sequences = list.Select(line =>
                JsonDocument.Parse(line["data: ".Length..].TrimEnd())
                    .RootElement.GetProperty("sequence").GetInt64());
            Assert.Equal(Enumerable.Range(1, 100).Select(i => (long)i), sequences);
        }
    }

    [Fact]
    public async Task Subscribe_RunCursor_TailsOnlyNewerEventsForTheSameRun()
    {
        var turn = new AiLiveTurn("s", 1, 1);
        turn.BindMessage("m1", 1, "chat");
        turn.Publish(AiTurnEvent.Create(SseEventType.Text, new { delta = "one" }));
        turn.Publish(AiTurnEvent.Create(SseEventType.Text, new { delta = "two" }));
        turn.Publish(AiTurnEvent.Create(SseEventType.Text, new { delta = "three" }));
        turn.Complete();

        var tail = new List<string>();
        await foreach (var chunk in turn.Subscribe(default, turn.RunId, 2))
            tail.Add(chunk);
        Assert.Single(tail);
        Assert.Contains("\"delta\":\"three\"", tail[0]);

        var mismatched = new List<string>();
        await foreach (var chunk in turn.Subscribe(default, "other-run", 2))
            mismatched.Add(chunk);
        Assert.Equal(3, mismatched.Count);

        var outOfRange = new List<string>();
        await foreach (var chunk in turn.Subscribe(default, turn.RunId, 99))
            outOfRange.Add(chunk);
        Assert.Equal(3, outOfRange.Count);
    }

    [Fact]
    public void TryStop_AfterComplete_DoesNotRewriteTerminalOutcome()
    {
        var turn = new AiLiveTurn("s", 1, 1);
        turn.BindMessage("m1", 1, "chat");
        turn.Publish(AiTurnEvent.Create(SseEventType.Done, new { ok = true }));
        turn.Complete();
        Assert.False(turn.TryStop());
        var last = turn.SnapshotEvents().Last();
        Assert.Equal(SseEventType.Done, last.Type);
        Assert.DoesNotContain(turn.SnapshotChunks(), c => c.Contains("\"stopped\""));
    }

    [Fact]
    public async Task PersistAssistantTurn_RepeatedSameRun_IsIdempotent()
    {
        await using var db = TestDbFactory.CreateDb();
        var binding = await AiConversationTurnStore.BeginAssistantTurnAsync(
            db, "s", 1, 1, "run-1", "chat", null, null, CancellationToken.None);
        var snapshot = AiSseTurnRecorder.FoldEvents([
            new AiTurnEvent(SseEventType.Text, JsonSerializer.SerializeToElement(new { delta = "hello" }))
            { RunId = "run-1", MessageId = binding.MessageId, Sequence = 1, Phase = "chat" },
            new AiTurnEvent(SseEventType.Done, JsonSerializer.SerializeToElement(new { }))
            { RunId = "run-1", MessageId = binding.MessageId, Sequence = 2, Phase = "chat" },
        ]);
        await AiConversationTurnStore.PersistAssistantTurnAsync(
            db, "s", 1, 1, binding, snapshot, CancellationToken.None);
        await AiConversationTurnStore.PersistAssistantTurnAsync(
            db, "s", 1, 1, binding, snapshot, CancellationToken.None);
        var row = db.aiConversationMessages.Single(m => m.role == "assistant");
        Assert.Equal("hello", row.content);
        using var json = JsonDocument.Parse(row.toolCallsJson!);
        Assert.Single(
            json.RootElement.GetProperty("timeline").EnumerateArray(),
            i => i.GetProperty("type").GetString() == "text");
    }

    [Fact]
    public async Task BeginExecution_BindsTheExactConfirmedProposal_NotTheLastAssistantRow()
    {
        await using var db = TestDbFactory.CreateDb();
        var conversation = await AiConversationTurnStore.EnsureConversationAsync(
            db, "s", 1, 1, CancellationToken.None);
        var planRow = new AiConversationMessage
        {
            conversationId = conversation.Id,
            role = "assistant",
            content = "Ready.",
            toolCallsJson = """
                {"_ui":"assistantTurn","messageId":"srv-plan","plan":{"proposalId":"p1","summary":"Update"},"planConfirmed":true,"tools":[],"timeline":[{"type":"text","text":"Ready."}]}
                """,
            createdAt = DateTime.UtcNow,
        };
        var laterRow = new AiConversationMessage
        {
            conversationId = conversation.Id,
            role = "assistant",
            content = "A later reply",
            toolCallsJson = """
                {"_ui":"assistantTurn","tools":[],"timeline":[{"type":"text","text":"A later reply"}]}
                """,
            createdAt = DateTime.UtcNow.AddMilliseconds(1),
        };
        db.aiConversationMessages.AddRange(planRow, laterRow);
        await db.SaveChangesAsync();

        var binding = await AiConversationTurnStore.BeginAssistantTurnAsync(
            db, "s", 1, 1, "run-exec", "execution", "p1", null, CancellationToken.None);
        Assert.Equal(planRow.Id, binding.DbId);
        Assert.Equal("srv-plan", binding.MessageId);
        var envelope = System.Text.Json.Nodes.JsonNode.Parse(planRow.toolCallsJson!)!;
        Assert.Equal("srv-plan", envelope["planOwnerMessageId"]!.GetValue<string>());
        Assert.Equal(1, envelope["planExecStart"]!.GetValue<int>());
        Assert.Equal("running", envelope["messageState"]!.GetValue<string>());
        // The unrelated later row is untouched.
        Assert.Equal("A later reply", laterRow.content);
    }

    [Fact]
    public async Task CanonicalExecutionRow_IsBoundBeforeTheFirstTurnEvent()
    {
        // The gated producer cannot yield until released: a committed
        // execution-bound row while it is blocked proves binding precedes the
        // first engine event. A second context on the same InMemory store
        // avoids racing the producer's context.
        var name = Guid.NewGuid().ToString();
        await using var db = TestDbFactory.CreateDb(name);
        await using var reader = TestDbFactory.CreateDb(name);
        using var handle = CreateHub(db);
        await SeedConfirmedPlan(db);
        var gate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        async IAsyncEnumerable<AiTurnEvent> Gated(
            IServiceProvider _,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await gate.Task;
            yield return AiTurnEvent.Create(SseEventType.Done, new { ok = true });
        }

        Assert.True(handle.TryStart("s", 1, 1, null, Gated, "original"));

        await WaitUntil(() =>
            reader.aiConversationMessages.Any(m =>
                m.role == "assistant"
                && m.toolCallsJson != null
                && m.toolCallsJson.Contains("\"phase\":\"execution\"")));
        var bound = reader.aiConversationMessages
            .Single(m => m.toolCallsJson!.Contains("\"phase\":\"execution\""));
        var envelope = JsonNode.Parse(bound.toolCallsJson!)!;
        Assert.Equal("running", envelope["messageState"]!.GetValue<string>());
        Assert.Equal(
            envelope["messageId"]!.GetValue<string>(),
            envelope["planOwnerMessageId"]!.GetValue<string>());
        Assert.True(envelope["planExecStart"]!.GetValue<int>() >= 0);
        // At most the binding marker exists: no producer event was emitted yet.
        var events = handle.Find("s", 1, 1)!.SnapshotEvents();
        Assert.True(events.Count <= 1);

        gate.SetResult();
        await WaitUntil(() => !handle.IsGenerating("s", 1, 1));
        reader.ChangeTracker.Clear();
        var done = JsonNode.Parse(
            reader.aiConversationMessages.Single(m => m.Id == bound.Id).toolCallsJson!)!;
        Assert.Equal("completed", done["messageState"]!.GetValue<string>());
    }

    [Fact]
    public async Task BeginReview_PreservesParentLinkAndAssignsOwnIdentity()
    {
        await using var db = TestDbFactory.CreateDb();
        var conversation = await AiConversationTurnStore.EnsureConversationAsync(
            db, "s", 1, 1, CancellationToken.None);
        var parent = new AiConversationMessage
        {
            conversationId = conversation.Id,
            role = "assistant",
            content = "Done.",
            toolCallsJson = """
                {"_ui":"assistantTurn","messageId":"srv-a","runId":"run-1","plan":{"proposalId":"A"},"planOwnerMessageId":"srv-a","planConfirmed":true,"planExecuted":true,"tools":[],"timeline":[]}
                """,
            createdAt = DateTime.UtcNow,
        };
        db.aiConversationMessages.Add(parent);
        await db.SaveChangesAsync();

        var binding = await AiConversationTurnStore.BeginAssistantTurnAsync(
            db, "s", 1, 1, "run-2", "review", null, "A", CancellationToken.None);
        Assert.NotEqual(parent.Id, binding.DbId);
        Assert.NotEqual("srv-a", binding.MessageId);
        var review = db.aiConversationMessages.Single(m => m.Id == binding.DbId);
        var envelope = System.Text.Json.Nodes.JsonNode.Parse(review.toolCallsJson!)!;
        Assert.Equal("A", envelope["executionProposalId"]!.GetValue<string>());
        Assert.Equal(binding.MessageId, envelope["messageId"]!.GetValue<string>());
        Assert.Equal("running", envelope["messageState"]!.GetValue<string>());
        // The review's own identity is not the parent's plan ownership.
        Assert.Null(envelope["planOwnerMessageId"]);
        Assert.Null(envelope["plan"]);
        var parentEnvelope = System.Text.Json.Nodes.JsonNode.Parse(parent.toolCallsJson!)!;
        Assert.Equal("srv-a", parentEnvelope["messageId"]!.GetValue<string>());
        Assert.True(parentEnvelope["planExecuted"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Subscribe_MissingRunCursor_ReplaysTheWholeCurrentRun()
    {
        // A cursor without its runId proves nothing: the full current run is
        // replayed rather than silently truncating a foreign run.
        var turn = new AiLiveTurn("s", 1, 1);
        turn.BindMessage("m1", 1, "chat");
        turn.Publish(AiTurnEvent.Create(SseEventType.Text, new { delta = "one" }));
        turn.Publish(AiTurnEvent.Create(SseEventType.Text, new { delta = "two" }));
        turn.Complete();

        var chunks = new List<string>();
        await foreach (var chunk in turn.Subscribe(default, null, 1))
            chunks.Add(chunk);
        Assert.Equal(2, chunks.Count);
    }

    [Fact]
    public async Task Publish_RacingComplete_DeliversExactlyTheAcceptedPrefixInOrder()
    {
        var turn = new AiLiveTurn("s", 1, 1);
        turn.BindMessage("m1", 1, "chat");
        var subscribers = Enumerable.Range(0, 3)
            .Select(_ =>
            {
                var list = new List<string>();
                return (list, task: Consume(turn, list));
            })
            .ToList();
        // At least one event lands before the barrier so every subscriber is
        // guaranteed a nonempty prefix.
        turn.Publish(AiTurnEvent.Create(SseEventType.Text, new { delta = "first" }));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writers = Enumerable.Range(0, 4)
            .Select(w => Task.Run(async () =>
            {
                await gate.Task;
                for (var i = 0; i < 25; i++)
                    turn.Publish(AiTurnEvent.Create(
                        SseEventType.Text, new { delta = $"w{w}-{i}" }));
            }))
            .ToList();
        var completer = Task.Run(async () =>
        {
            await gate.Task;
            turn.Complete();
        });
        gate.SetResult();
        await Task.WhenAll(writers.Concat([completer]));
        await Task.WhenAll(subscribers.Select(s => s.task));

        // Whatever Publish accepted before Complete is delivered exactly once
        // and in sequence - no subscriber can observe extra or reordered events.
        var accepted = turn.SnapshotEvents();
        foreach (var (list, _) in subscribers)
        {
            var sequences = list.Select(line =>
                JsonDocument.Parse(line["data: ".Length..].TrimEnd())
                    .RootElement.GetProperty("sequence").GetInt64());
            Assert.Equal(accepted.Select(e => e.Sequence!.Value), sequences);
        }
    }

    [Fact]
    public async Task PersistExecution_PreservesPlanOwnershipAndReviewParent()
    {
        // Recovery B owns its own plan but keeps the review parent A: an
        // execution merge must not drop either.
        await using var db = TestDbFactory.CreateDb();
        var conversation = await AiConversationTurnStore.EnsureConversationAsync(
            db, "s", 1, 1, CancellationToken.None);
        var review = new AiConversationMessage
        {
            conversationId = conversation.Id,
            role = "assistant",
            content = "Corrected.",
            toolCallsJson = """
                {"_ui":"assistantTurn","messageId":"srv-b","runId":"run-review","phase":"review","executionProposalId":"A","plan":{"proposalId":"B","summary":"Fix"},"planOwnerMessageId":"srv-b","planConfirmed":true,"planConfirmedBy":1,"planConfirmedAtUtc":"2026-01-01T00:00:00Z","planConfirmationId":"B","tools":[],"timeline":[{"type":"text","text":"Corrected."}]}
                """,
            createdAt = DateTime.UtcNow,
        };
        db.aiConversationMessages.Add(review);
        await db.SaveChangesAsync();

        var binding = await AiConversationTurnStore.BeginAssistantTurnAsync(
            db, "s", 1, 1, "run-exec", "execution", "B", null, CancellationToken.None);
        Assert.Equal(review.Id, binding.DbId);
        var snapshot = AiSseTurnRecorder.FoldEvents([
            new AiTurnEvent(SseEventType.Tool, JsonSerializer.SerializeToElement(
                new { id = "t1", name = "platform_request", status = "done", summary = "ok" }))
            { RunId = "run-exec", MessageId = "srv-b", Sequence = 1, Phase = "execution" },
            new AiTurnEvent(SseEventType.ExecutionStatus, JsonSerializer.SerializeToElement(
                new { status = "completed", succeeded = 1, total = 1, summary = "1 step completed." }))
            { RunId = "run-exec", MessageId = "srv-b", Sequence = 2, Phase = "execution" },
            new AiTurnEvent(SseEventType.Done, JsonSerializer.SerializeToElement(new { ok = true }))
            { RunId = "run-exec", MessageId = "srv-b", Sequence = 3, Phase = "execution" },
        ]);
        await AiConversationTurnStore.PersistAssistantTurnAsync(
            db, "s", 1, 1, binding, snapshot, CancellationToken.None);

        db.ChangeTracker.Clear();
        var row = await db.aiConversationMessages.SingleAsync(m => m.Id == review.Id);
        var envelope = JsonNode.Parse(row.toolCallsJson!)!;
        Assert.Equal("srv-b", envelope["planOwnerMessageId"]!.GetValue<string>());
        Assert.Equal("A", envelope["executionProposalId"]!.GetValue<string>());
        Assert.Equal("srv-b", envelope["messageId"]!.GetValue<string>());
        Assert.Equal("run-exec", envelope["runId"]!.GetValue<string>());
        Assert.Equal(1, envelope["planExecStart"]!.GetValue<int>());
        Assert.Equal(1, envelope["planConfirmedBy"]!.GetValue<int>());
        Assert.True(envelope["planExecuted"]!.GetValue<bool>());
        Assert.Equal("completed", envelope["messageState"]!.GetValue<string>());
    }

    [Fact]
    public async Task PersistExecution_WithoutMergeablePlan_ThrowsInsteadOfRewriting()
    {
        // An execution binding whose row lost its plan must fail loudly rather
        // than replace the signed envelope with a planless snapshot.
        await using var db = TestDbFactory.CreateDb();
        var binding = await AiConversationTurnStore.BeginAssistantTurnAsync(
            db, "s", 1, 1, "run-1", "chat", null, null, CancellationToken.None);
        var execBinding = binding with { Phase = "execution" };
        var snapshot = AiSseTurnRecorder.FoldEvents([
            new AiTurnEvent(SseEventType.Text, JsonSerializer.SerializeToElement(new { delta = "x" }))
            { RunId = "run-1", MessageId = binding.MessageId, Sequence = 1, Phase = "execution" },
        ]);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AiConversationTurnStore.PersistAssistantTurnAsync(
                db, "s", 1, 1, execBinding, snapshot, CancellationToken.None));
    }

    [Fact]
    public async Task PersistExecution_TerminalRow_IsNotReopenedByLaterSegmentSnapshot()
    {
        // A later segment snapshot carrying a higher sequence for the same run
        // (e.g. a review-admission failure marker) must never reopen an already
        // terminal execution row.
        await using var db = TestDbFactory.CreateDb();
        var conversation = await AiConversationTurnStore.EnsureConversationAsync(
            db, "s", 1, 1, CancellationToken.None);
        var planRow = new AiConversationMessage
        {
            conversationId = conversation.Id,
            role = "assistant",
            content = "Ready.",
            toolCallsJson = """
                {"_ui":"assistantTurn","messageId":"srv-a","plan":{"proposalId":"A","summary":"Update"},"planConfirmed":true,"tools":[],"timeline":[{"type":"text","text":"Ready."}]}
                """,
            createdAt = DateTime.UtcNow,
        };
        db.aiConversationMessages.Add(planRow);
        await db.SaveChangesAsync();
        var binding = await AiConversationTurnStore.BeginAssistantTurnAsync(
            db, "s", 1, 1, "run-exec", "execution", "A", null, CancellationToken.None);
        var terminal = AiSseTurnRecorder.FoldEvents([
            new AiTurnEvent(SseEventType.Tool, JsonSerializer.SerializeToElement(
                new { id = "t1", name = "platform_request", status = "done", summary = "ok" }))
            { RunId = "run-exec", MessageId = "srv-a", Sequence = 1, Phase = "execution" },
            new AiTurnEvent(SseEventType.Done, JsonSerializer.SerializeToElement(new { ok = true }))
            { RunId = "run-exec", MessageId = "srv-a", Sequence = 2, Phase = "execution" },
        ]);
        await AiConversationTurnStore.PersistAssistantTurnAsync(
            db, "s", 1, 1, binding, terminal, CancellationToken.None);

        var errorTail = AiSseTurnRecorder.FoldEvents([
            new AiTurnEvent(SseEventType.Tool, JsonSerializer.SerializeToElement(
                new { id = "t1", name = "platform_request", status = "done", summary = "ok" }))
            { RunId = "run-exec", MessageId = "srv-a", Sequence = 1, Phase = "execution" },
            new AiTurnEvent(SseEventType.Done, JsonSerializer.SerializeToElement(new { ok = true }))
            { RunId = "run-exec", MessageId = "srv-a", Sequence = 2, Phase = "execution" },
            new AiTurnEvent(SseEventType.Error, JsonSerializer.SerializeToElement(
                new { message = "interrupted", code = "internal_error" }))
            { RunId = "run-exec", MessageId = "srv-a", Sequence = 7, Phase = "execution" },
            new AiTurnEvent(SseEventType.Done, JsonSerializer.SerializeToElement(new { interrupted = true }))
            { RunId = "run-exec", MessageId = "srv-a", Sequence = 8, Phase = "execution" },
        ]);
        await AiConversationTurnStore.PersistAssistantTurnAsync(
            db, "s", 1, 1, binding, errorTail, CancellationToken.None);

        db.ChangeTracker.Clear();
        var row = await db.aiConversationMessages.SingleAsync(m => m.Id == planRow.Id);
        var envelope = JsonNode.Parse(row.toolCallsJson!)!;
        Assert.Equal("completed", envelope["messageState"]!.GetValue<string>());
        Assert.True(envelope["planExecuted"]!.GetValue<bool>());
        Assert.Null(envelope["requestError"]);
        // Tools are never appended twice by the reopened snapshot.
        Assert.Single(envelope["tools"]!.AsArray());
        Assert.Equal("t1", envelope["tools"]![0]!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task BeginExecution_AmbiguousConfirmedTargets_ThrowsInsteadOfGuessing()
    {
        // Two confirmed unexecuted rows claiming the same proposal is corrupt
        // state: refuse rather than silently pick the latest.
        await using var db = TestDbFactory.CreateDb();
        var conversation = await AiConversationTurnStore.EnsureConversationAsync(
            db, "s", 1, 1, CancellationToken.None);
        foreach (var id in new[] { "srv-1", "srv-2" })
        {
            db.aiConversationMessages.Add(new AiConversationMessage
            {
                conversationId = conversation.Id,
                role = "assistant",
                content = "Ready.",
                toolCallsJson = $$"""
                    {"_ui":"assistantTurn","messageId":"{{id}}","plan":{"proposalId":"A","summary":"Update"},"planConfirmed":true,"tools":[],"timeline":[]}
                    """,
                createdAt = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AiConversationTurnStore.BeginAssistantTurnAsync(
                db, "s", 1, 1, "run-exec", "execution", "A", null, CancellationToken.None));
    }

    [Fact]
    public async Task BeginExecution_IgnoresMalformedHistoricalPlans()
    {
        // An unrelated historical row with a malformed plan must not throw and
        // must not become the execution target.
        await using var db = TestDbFactory.CreateDb();
        var conversation = await AiConversationTurnStore.EnsureConversationAsync(
            db, "s", 1, 1, CancellationToken.None);
        db.aiConversationMessages.Add(new AiConversationMessage
        {
            conversationId = conversation.Id,
            role = "assistant",
            content = "corrupt",
            toolCallsJson = """
                {"_ui":"assistantTurn","plan":{"proposalId":42},"planConfirmed":"yes","tools":[],"timeline":[]}
                """,
            createdAt = DateTime.UtcNow,
        });
        var target = new AiConversationMessage
        {
            conversationId = conversation.Id,
            role = "assistant",
            content = "Ready.",
            toolCallsJson = """
                {"_ui":"assistantTurn","plan":{"proposalId":"A","summary":"Update"},"planConfirmed":true,"tools":[],"timeline":[]}
                """,
            createdAt = DateTime.UtcNow.AddMilliseconds(1),
        };
        db.aiConversationMessages.Add(target);
        await db.SaveChangesAsync();
        var binding = await AiConversationTurnStore.BeginAssistantTurnAsync(
            db, "s", 1, 1, "run-exec", "execution", "A", null, CancellationToken.None);
        Assert.Equal(target.Id, binding.DbId);
    }

    [Fact]
    public async Task ReviewBeginFailure_NeverWritesBackIntoTerminalExecution()
    {
        // Inject a failure at the review-row insert AFTER the completed
        // execution was already persisted: the error tail must land on an
        // ephemeral review identity, never reopen or rewrite A.
        var options = new DbContextOptionsBuilder<DataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(new FailOnReviewRowInsert())
            .Options;
        await using var db = new DataContext(options);
        using var handle = CreateHub(db);
        await SeedConfirmedPlan(db);
        var advancedPastBoundary = false;
        Assert.True(handle.TryStart("s", 1, 1, null, GuardedProducer, "original"));
        await WaitUntil(() => !handle.IsGenerating("s", 1, 1));

        var rows = db.aiConversationMessages
            .Where(m => m.role == "assistant")
            .OrderBy(m => m.Id)
            .ToList();
        // The failed review row was never inserted.
        Assert.Single(rows);
        var envelope = JsonNode.Parse(rows[0].toolCallsJson!)!;
        Assert.Equal("completed", envelope["messageState"]!.GetValue<string>());
        Assert.True(envelope["planExecuted"]!.GetValue<bool>());
        Assert.Null(envelope["requestError"]);
        var aMessageId = envelope["messageId"]!.GetValue<string>();

        var events = handle.Find("s", 1, 1)!.SnapshotEvents();
        var reviewStart = events.Last(e => e.Type == SseEventType.AssistantStart);
        Assert.NotEqual(aMessageId, reviewStart.MessageId);
        Assert.Equal("review", reviewStart.Phase);
        Assert.Null(reviewStart.DbId);
        // The error tail is stamped on the ephemeral review identity, not on A.
        Assert.Equal(reviewStart.MessageId, events.Last(e => e.Type == SseEventType.Error).MessageId);
        // A terminal review-admission failure stops the producer: no text or
        // recovery plan can be emitted after the terminal error, and the
        // producer is never advanced past the boundary.
        var boundary = events.ToList().FindIndex(e => ReferenceEquals(e, reviewStart));
        Assert.DoesNotContain(
            events.Skip(boundary + 1),
            e => e.Type is SseEventType.Text or SseEventType.Plan);
        Assert.False(advancedPastBoundary);

        async IAsyncEnumerable<AiTurnEvent> GuardedProducer(
            IServiceProvider _,
            [EnumeratorCancellation] CancellationToken ct)
        {
            yield return AiTurnEvent.Create(SseEventType.Done, new { ok = true });
            yield return AiTurnEvent.Create(
                SseEventType.AssistantStart, new { executionProposalId = "original" });
            // Reaching past the boundary means the consumer failed to stop.
            advancedPastBoundary = true;
            yield return AiTurnEvent.Create(SseEventType.Text, new { delta = "review text" });
            yield return AiTurnEvent.Create(SseEventType.Plan,
                new { proposalId = "postmortem", summary = "late recovery" });
            yield return AiTurnEvent.Create(SseEventType.Done, new { });
            await Task.CompletedTask;
        }
    }

    private sealed class FailOnReviewRowInsert : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var injecting = eventData.Context!.ChangeTracker
                .Entries<AiConversationMessage>()
                .Any(e => e.State == EntityState.Added
                    && e.Entity.toolCallsJson?.Contains("\"phase\":\"review\"") == true);
            if (injecting)
                throw new InvalidOperationException("injected review-begin failure");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private static async Task Consume(AiLiveTurn turn, List<string> seen)
    {
        await foreach (var chunk in turn.Subscribe())
            seen.Add(chunk);
    }

    private static LiveHubHandle CreateHub(DataContext db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        var sp = services.BuildServiceProvider();
        var hub = new AiLiveTurnHub(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AiLiveTurnHub>.Instance,
            TimeSpan.FromMilliseconds(50));
        return new LiveHubHandle(hub, sp);
    }

    private static LiveHubHandle CreateHub(out DataContext db)
    {
        db = TestDbFactory.CreateDb();
        return CreateHub(db);
    }

    private static async Task WaitUntil(Func<bool> pred)
    {
        var start = DateTime.UtcNow;
        while (!pred())
        {
            if (DateTime.UtcNow - start > TimeSpan.FromSeconds(3))
                throw new TimeoutException("condition was not met");
            await Task.Delay(15);
        }
    }

    private sealed class LiveHubHandle : IDisposable
    {
        private readonly ServiceProvider _sp;
        public LiveHubHandle(AiLiveTurnHub hub, ServiceProvider sp)
        {
            Hub = hub;
            _sp = sp;
        }

        public AiLiveTurnHub Hub { get; }
        public static implicit operator AiLiveTurnHub(LiveHubHandle h) => h.Hub;
        public bool TryStart(string s, int store, int user, ChatMessage? m,
            Func<IServiceProvider, CancellationToken, IAsyncEnumerable<AiTurnEvent>> p,
            string? executionProposalId = null) =>
            Hub.TryStart(s, store, user, m, p, executionProposalId);
        public bool Stop(string s, int store, int user) => Hub.Stop(s, store, user);
        public bool IsGenerating(string s, int store, int user) => Hub.IsGenerating(s, store, user);
        public AiLiveTurn? Find(string s, int store, int user) => Hub.Find(s, store, user);
        public void Dispose()
        {
            Hub.Stop("s", 1, 1);
            var start = DateTime.UtcNow;
            while (Hub.IsGenerating("s", 1, 1) && DateTime.UtcNow - start < TimeSpan.FromSeconds(2))
                Thread.Sleep(15);
            _sp.Dispose();
        }
    }
}
