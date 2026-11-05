using System.Runtime.CompilerServices;
using System.Text.Json;
using Delegat;
using Delegat.Models.Ai;
using Delegat.Services.Ai;
using Delegat.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class AiLiveTurnHubTests
{
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
        turn.Publish(AiSseTurnRecorder.Format(SseEventType.Text, new { delta = "a" }));

        var seen = new List<string>();
        var sub = Consume(turn, seen);
        await Task.Delay(30);
        turn.Publish(AiSseTurnRecorder.Format(SseEventType.Text, new { delta = "b" }));
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
        Assert.True(hub.TryStart("s", 1, 1, null, HangAfterText));
        await WaitUntil(() => hub.Find("s", 1, 1)?.SnapshotChunks().Count > 0);
        Assert.True(hub.Stop("s", 1, 1));
        await WaitUntil(() => !hub.IsGenerating("s", 1, 1));
        var assistant = db.aiConversationMessages.Single(m => m.role == "assistant");
        using var json = JsonDocument.Parse(assistant.toolCallsJson!);
        Assert.True(json.RootElement.GetProperty("planConfirmed").GetBoolean());
        Assert.False(json.RootElement.GetProperty("planExecuted").GetBoolean());
        var retry = await AiConversationTurnStore.ConsumePendingPlanDetailedAsync(db, "s", "p1", 1, 1, CancellationToken.None);
        Assert.Null(retry.Steps);
        Assert.Equal("already_processed", retry.RejectionReason);
    }

    [Fact]
    public async Task Stop_PersistsPartialAssistant()
    {
        using var hub = CreateHub(out var db);
        Assert.True(hub.TryStart("s", 1, 1, User("hello"), HangAfterText));
        await WaitUntil(() => hub.Find("s", 1, 1)?.SnapshotChunks().Count > 0);
        Assert.True(hub.Stop("s", 1, 1));
        await WaitUntil(() => !hub.IsGenerating("s", 1, 1));

        var messages = db.aiConversationMessages.OrderBy(m => m.Id).ToList();
        Assert.Contains(messages, m => m.role == "user" && m.content == "hello");
        Assert.Contains(messages, m => m.role == "assistant" && m.content.Contains("partial"));
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
            """{"_ui":"assistantTurn","tools":[],"timeline":[{"type":"text","text":"Ready."}],"plan":{"summary":"Upload"}}""";
        await AiConversationTurnStore.UpsertAssistantAsync(
            db, "s", 1, 1,
            new AiSseTurnSnapshot("Ready.", planJson, "m"),
            CancellationToken.None);

        Assert.True(handle.TryStart("s", 1, 1, null, ImmediateDone));
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

    private static async IAsyncEnumerable<string> Hang(
        IServiceProvider _,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Delay(Timeout.Infinite, ct);
        yield break;
    }

    private static async IAsyncEnumerable<string> HangAfterText(
        IServiceProvider _,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return AiSseTurnRecorder.Format(SseEventType.Text, new { delta = "partial" });
        await Task.Delay(Timeout.Infinite, ct);
    }

    private static async IAsyncEnumerable<string> ImmediateDone(
        IServiceProvider _,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return AiSseTurnRecorder.Format(SseEventType.Text, new { delta = "done-text" });
        yield return AiSseTurnRecorder.Format(SseEventType.Done, new { modelName = "x", ok = true });
        await Task.CompletedTask;
    }

    private static async Task Consume(AiLiveTurn turn, List<string> seen)
    {
        await foreach (var chunk in turn.Subscribe())
            seen.Add(chunk);
    }

    private static LiveHubHandle CreateHub(out DataContext db)
    {
        db = TestDbFactory.CreateDb();
        var services = new ServiceCollection();
        services.AddSingleton(db);
        var sp = services.BuildServiceProvider();
        var hub = new AiLiveTurnHub(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AiLiveTurnHub>.Instance,
            TimeSpan.FromMilliseconds(50));
        return new LiveHubHandle(hub, sp);
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
            Func<IServiceProvider, CancellationToken, IAsyncEnumerable<string>> p) =>
            Hub.TryStart(s, store, user, m, p);
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
