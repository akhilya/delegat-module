using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Delegat;
using Delegat.Infrastructure.Storage;
using Delegat.Infrastructure.Security;
using Delegat.Models.Ai;
using Delegat.Services;
using Delegat.Services.Ai;
using Delegat.Services.Ai.Agent;
using Delegat.Services.Ai.Agent.Maf;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ChatMessage = Delegat.Services.Ai.ChatMessage;
using FrameworkChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Delegat.Tests;

/// <summary>
/// M3 parity checks: the Microsoft Agent Framework engine (Ai:AgentRuntime=maf) must
/// reproduce the legacy StreamChatAsync contract - batch-level plan gating, synthetic
/// deferred results, attachment ordering, review mode, nudges, runaway protection, and
/// the SSE stream shape. Fixtures mirror AiChatAgentEvaluationTests; the model is
/// driven through a scripted Microsoft.Extensions.AI IChatClient.
/// </summary>
[Collection("AiPlanSigningKey")]
public class MafEngineParityTests
{
    [Fact]
    public async Task PlanInMixedBatch_DefersEverySiblingTool_AndStopsAtOneProviderRequest()
    {
        using var signingKey = SigningKey();
        var mcp = new EmptyMcpGateway(new AiTool
        {
            name = "mcp3_update_record",
            description = "Update a connected record",
            input_schema = new AiToolInputSchema { type = "object" },
        });
        await using var fixture = await CreateFixtureAsync(
            mcp,
            ToolBatch(
                ("platform_request", "{\"method\":\"POST\",\"path\":\"/products\",\"body\":{\"name\":\"New\"}}", "write-sibling"),
                ("mcp3_update_record", "{\"id\":42}", "mcp-sibling"),
                ("propose_plan", "{\"summary\":\"Update the record\",\"steps\":[{\"description\":\"Update the connected record\",\"tool\":\"mcp3_update_record\",\"args\":{\"id\":42}}]}", "plan-sibling")));

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("Update the cabinet product and connected record")], fixture.StoreId, fixture.UserId,
            sessionId: "maf-mixed-batch", locale: "en"));

        Assert.Equal(0, fixture.Invoker.Calls);
        Assert.Equal(0, mcp.Calls);
        Assert.Contains("\"type\":\"plan\"", string.Concat(chunks), StringComparison.Ordinal);
        // After a presented plan the turn ends without querying the model again.
        Assert.Single(fixture.Client.Requests);
    }

    [Fact]
    public async Task MultiplePlansInOneBatch_PresentsOnlyTheFirstValidPlan()
    {
        using var signingKey = SigningKey();
        await using var fixture = await CreateFixtureAsync(
            ToolBatch(
                ("propose_plan", "{\"summary\":\"First proposed action\",\"steps\":[{\"description\":\"Do the first action\",\"tool\":\"platform_request\",\"args\":{\"method\":\"POST\",\"path\":\"/products\",\"body\":{\"name\":\"First\"}}}]}", "first-plan"),
                ("propose_plan", "{\"summary\":\"Second proposed action\",\"steps\":[{\"description\":\"Do the second action\",\"tool\":\"platform_request\",\"args\":{\"method\":\"POST\",\"path\":\"/products\",\"body\":{\"name\":\"Second\"}}}]}", "second-plan")));

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("Create two products")], fixture.StoreId, fixture.UserId,
            sessionId: "maf-multiple-plans", locale: "en"));

        var plan = Assert.Single(chunks,
            chunk => chunk.Contains("\"type\":\"plan\"", StringComparison.Ordinal));
        Assert.Contains("First proposed action", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("Second proposed action", string.Concat(chunks), StringComparison.Ordinal);
        Assert.Single(fixture.Client.Requests);
        Assert.Equal(0, fixture.Invoker.Calls);
    }

    [Fact]
    public async Task AttachmentReadInMixedBatch_CompletesBeforePlanIsPresented()
    {
        using var signingKey = SigningKey();
        const string sessionId = "maf-attachment-plan";
        const string csv = "SKU,Status\nSKU-42,Inactive";
        await using var fixture = await CreateFixtureWithAttachmentAsync(
            sessionId,
            "status.csv",
            Encoding.UTF8.GetBytes(csv),
            ToolBatch(
                ("read_chat_attachment", "{\"attachmentId\":71}", "read-attachment"),
                ("propose_plan", "{\"summary\":\"Update SKU-42\",\"steps\":[{\"description\":\"Update SKU-42 status\",\"tool\":\"platform_request\",\"args\":{\"method\":\"PATCH\",\"path\":\"/products/42\",\"body\":{\"status\":\"active\"}}}]}", "premature-plan")),
            ToolCall("propose_plan", "{\"summary\":\"Update SKU-42 after checking the attachment\",\"steps\":[{\"description\":\"Update SKU-42 status\",\"tool\":\"platform_request\",\"args\":{\"method\":\"PATCH\",\"path\":\"/products/42\",\"body\":{\"status\":\"active\"}}}]}", "validated-plan"));

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("Use the attached status file to update SKU-42")], fixture.StoreId, fixture.UserId,
            sessionId: sessionId, locale: "en"));
        var serialized = string.Concat(chunks);

        Assert.Equal(2, fixture.Client.Requests.Count);
        var followUpRequest = string.Concat(fixture.Client.Requests[1].Messages.Select(message => message.Content));
        Assert.Contains("SKU-42", followUpRequest, StringComparison.Ordinal);
        Assert.Contains("Inactive", followUpRequest, StringComparison.Ordinal);
        var readCompletedAt = serialized.IndexOf("\"name\":\"read_chat_attachment\",\"status\":\"done\"", StringComparison.Ordinal);
        var planPresentedAt = serialized.IndexOf("\"type\":\"plan\"", StringComparison.Ordinal);
        Assert.True(readCompletedAt >= 0, "The attachment read should complete in the turn.");
        Assert.True(planPresentedAt > readCompletedAt, "The plan must not be presented before the attachment read result.");
        Assert.Contains("Update SKU-42 after checking the attachment", serialized, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Invoker.Calls);
    }

    [Fact]
    public async Task TextOnlyAnswerWithUnfulfilledIntent_IsNudgedOnce()
    {
        await using var fixture = await CreateFixtureAsync(
            Text("I will check the catalog for that product."),
            Text("The product costs 100."));

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("How much is SKU-42?")], fixture.StoreId, fixture.UserId,
            sessionId: "maf-intent-nudge", locale: "en"));

        Assert.Equal(2, fixture.Client.Requests.Count);
        var nudge = fixture.Client.Requests[1].Messages.Last();
        Assert.Equal("user", nudge.Role);
        Assert.Contains("Continue in this turn", nudge.Content, StringComparison.Ordinal);
        Assert.Contains("costs 100", AiSseTurnRecorder.Fold(chunks).Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SilentTurn_IsNudgedOnce_ThenFallsBack()
    {
        await using var fixture = await CreateFixtureAsync(
            Text(""),
            Text(""));

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("Say something")], fixture.StoreId, fixture.UserId,
            sessionId: "maf-silent", locale: "en"));

        Assert.Equal(2, fixture.Client.Requests.Count);
        var nudge = fixture.Client.Requests[1].Messages.Last();
        Assert.Equal("user", nudge.Role);
        Assert.Contains("You stopped with no reply", nudge.Content, StringComparison.Ordinal);
        Assert.Contains("I stopped without an answer", AiSseTurnRecorder.Fold(chunks).Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnconfirmedWrite_GetsWriteNeedsPlan_AndNeverReachesInvoker()
    {
        await using var fixture = await CreateFixtureAsync(
            ToolCall("platform_request", "{\"method\":\"POST\",\"path\":\"/products\",\"body\":{\"name\":\"New\"}}", "write-1"),
            Text("I need your confirmation before making that change."));

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("Create a product named New")], fixture.StoreId, fixture.UserId,
            sessionId: "maf-write-gate", locale: "en"));

        Assert.Equal(0, fixture.Invoker.Calls);
        Assert.Equal(2, fixture.Client.Requests.Count);
        Assert.Contains("Not run. Cabinet writes only run after the user confirms",
            LastToolResult(fixture.Client.Requests[1]), StringComparison.Ordinal);
        Assert.Contains("confirmation", AiSseTurnRecorder.Fold(chunks).Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReviewMode_AdvertisesOnlyProposePlan_AndNonPlanCallsGetNotRun()
    {
        await using var fixture = await CreateFixtureAsync(
            ToolCall("run_python", "{\"code\":\"print(1)\"}", "python-read"),
            Text("The execution already finished; results are summarized above."));

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("Review the actual results")], fixture.StoreId, fixture.UserId,
            sessionId: "maf-review", locale: "en", planResultReview: true));

        Assert.Equal(["propose_plan"], fixture.Client.Requests[0].ToolNames);
        Assert.Equal(2, fixture.Client.Requests.Count);
        Assert.Contains("Not run. Execution is already finished",
            LastToolResult(fixture.Client.Requests[1]), StringComparison.Ordinal);
        Assert.Equal("The execution already finished; results are summarized above.",
            AiSseTurnRecorder.Fold(chunks).Content);
    }

    [Fact]
    public async Task UnknownTool_GetsErrorResult_AndTurnCompletes()
    {
        await using var fixture = await CreateFixtureAsync(
            ToolCall("totally_unknown_tool", "{}", "unknown-1"),
            Text("I could not use that tool, so here is the answer."));

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("Do the thing")], fixture.StoreId, fixture.UserId,
            sessionId: "maf-unknown-tool", locale: "en"));

        Assert.Equal(2, fixture.Client.Requests.Count);
        Assert.Contains("Unknown tool: totally_unknown_tool",
            LastToolResult(fixture.Client.Requests[1]), StringComparison.Ordinal);
        Assert.Contains("\"type\":\"done\"", string.Concat(chunks), StringComparison.Ordinal);
        Assert.Contains("here is the answer", AiSseTurnRecorder.Fold(chunks).Content,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadToolResult_IsIncludedInNextModelRequestBeforeFinalAnswer()
    {
        await using var fixture = await CreateFixtureAsync(
            ToolCall("search_catalog", "{\"query\":\"SKU-42\",\"limit\":5}", "read-1"),
            Text("The product is Ceramic Mug."));

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("Find SKU-42")], fixture.StoreId, fixture.UserId,
            sessionId: "maf-read", locale: "en"));

        Assert.Equal(2, fixture.Client.Requests.Count);
        Assert.Contains("search_catalog", fixture.Client.Requests[0].ToolNames);
        var toolResult = LastToolResult(fixture.Client.Requests[1]);
        Assert.Contains("SKU-42", toolResult, StringComparison.Ordinal);
        Assert.Contains("Ceramic Mug", AiSseTurnRecorder.Fold(chunks).Content, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"done\"", string.Concat(chunks), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LegacyFlag_UsesExistingLoop_AndNeverTouchesTheMafClient()
    {
        var legacyClient = new ScriptedAiStreamingClient(
        [
            new AiStreamEvent
            {
                type = "content_block_delta",
                delta = new AiStreamDelta { type = "text_delta", text = "legacy answer" },
            },
            new AiStreamEvent { type = "message_stop" },
        ]);
        await using var fixture = await CreateFixtureCoreAsync(
            new EmptyMcpGateway(), null, null, null, [], legacyClient, runtime: "legacy");

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("hello")], fixture.StoreId, fixture.UserId,
            sessionId: "maf-flag-legacy", locale: "en"));

        Assert.Equal("legacy answer", AiSseTurnRecorder.Fold(chunks).Content);
        Assert.Single(legacyClient.Requests);
        Assert.Empty(fixture.Client.Requests);
        Assert.Equal(0, fixture.Factory.Calls);
    }

    [Fact]
    public async Task ProviderErrorMidTurn_EmitsInterruptedError_AndCompletesTurn()
    {
        await using var fixture = await CreateFixtureAsync(
            ToolCall("search_catalog", "{\"query\":\"SKU-42\",\"limit\":5}", "read-1"),
            new HttpRequestException("provider boom"));

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("Find SKU-42")], fixture.StoreId, fixture.UserId,
            sessionId: "maf-provider-error", locale: "en"));

        var serialized = string.Concat(chunks);
        Assert.Equal(2, fixture.Client.Requests.Count);
        Assert.Contains("\"type\":\"error\"", serialized, StringComparison.Ordinal);
        Assert.Contains(AgentToolResult.TurnInterruptedMessage.Replace("\"", "\\u0022"),
            serialized, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"done\"", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderErrorOnFirstRequest_EmitsProviderErrorMessage()
    {
        await using var fixture = await CreateFixtureAsync(
            new HttpRequestException("provider boom"));

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("hello")], fixture.StoreId, fixture.UserId,
            sessionId: "maf-provider-error-first", locale: "en"));

        var serialized = string.Concat(chunks);
        Assert.Single(fixture.Client.Requests);
        Assert.Contains("\"type\":\"error\"", serialized, StringComparison.Ordinal);
        Assert.Contains(AgentToolResult.ProviderErrorMessage.Replace("\"", "\\u0022"),
            serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunawayToolRounds_EndsLikeLegacyLoopExit()
    {
        // The model keeps calling a read tool; after RunawayToolRounds the gate goes
        // speak-only, strips the calls from the batch, and the turn closes cleanly -
        // the same end state as the legacy for-loop exhausting its iteration budget.
        await using var fixture = await CreateFixtureAsync(
            Enumerable.Range(0, AgentChatTurnGate.RunawayToolRounds + 5)
                .Select(i => ToolCall("search_catalog", "{\"query\":\"loop\",\"limit\":1}", $"loop-{i}"))
                .Cast<object>()
                .ToArray());

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("keep searching")], fixture.StoreId, fixture.UserId,
            sessionId: "maf-runaway", locale: "en"));

        // 40 tool rounds + one speak-only request whose calls the gate strips.
        Assert.Equal(AgentChatTurnGate.RunawayToolRounds + 1, fixture.Client.Requests.Count);
        Assert.Empty(fixture.Client.Requests[^1].ToolNames);
        Assert.Contains("\"type\":\"done\"", string.Concat(chunks), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TelemetryLine_CarriesMafEngineLabel_AndProviderUsage()
    {
        await using var fixture = await CreateFixtureAsync(
            new ChatResponse(new FrameworkChatMessage(ChatRole.Assistant, "answer"))
            {
                FinishReason = ChatFinishReason.Stop,
                Usage = new UsageDetails { InputTokenCount = 11, OutputTokenCount = 7 },
            });

        await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("hi")], fixture.StoreId, fixture.UserId,
            sessionId: "maf-telemetry", locale: "en"));

        var telemetry = Assert.Single(fixture.Logger.Messages,
            m => m.Contains("AI chat turn telemetry", StringComparison.Ordinal));
        Assert.Contains("Engine=maf", telemetry, StringComparison.Ordinal);
        Assert.Contains("InputTokens=11", telemetry, StringComparison.Ordinal);
        Assert.Contains("OutputTokens=7", telemetry, StringComparison.Ordinal);
    }

    private static string LastToolResult(CapturedRequest request) =>
        request.Messages.Last(m => m.Role == "tool").Content;

    private static TemporaryEnvironmentVariable SigningKey() =>
        new("ENCRYPTION_KEY",
            Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()));

    private static Task<ChatFixture> CreateFixtureAsync(params object[] responses) =>
        CreateFixtureCoreAsync(new EmptyMcpGateway(), null, null, null, responses);

    private static Task<ChatFixture> CreateFixtureAsync(EmptyMcpGateway mcp, params object[] responses) =>
        CreateFixtureCoreAsync(mcp, null, null, null, responses);

    private static Task<ChatFixture> CreateFixtureWithAttachmentAsync(
        string sessionId, string fileName, byte[] attachmentBytes, params object[] responses) =>
        CreateFixtureCoreAsync(new EmptyMcpGateway(), sessionId, fileName, attachmentBytes, responses);

    private static async Task<ChatFixture> CreateFixtureCoreAsync(
        EmptyMcpGateway mcp,
        string? attachmentSessionId,
        string? attachmentFileName,
        byte[]? attachmentBytes,
        object[] responses,
        ScriptedAiStreamingClient? legacyClient = null,
        string runtime = "maf")
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser("maf-parity");
        var store = data.AddStore("Maf parity store", "RUB", "UTC", user);
        data.AddProduct(store.Id, name: "Ceramic Mug", sku: "SKU-42");
        if (attachmentSessionId != null && attachmentFileName != null && attachmentBytes != null)
        {
            db.aiChatAttachments.Add(new AiChatAttachment
            {
                Id = 71,
                sessionId = attachmentSessionId,
                storeId = store.Id,
                userId = user.Id,
                fileName = attachmentFileName,
                contentType = "text/csv",
                length = attachmentBytes.Length,
                storageKey = "staged/" + attachmentFileName,
                createdAt = DateTime.UtcNow,
            });
        }
        db.aiModelConfigs.Add(new AiModelConfig
        {
            name = "scripted",
            apiUrl = "https://api.example.test",
            apiKeyEncrypted = "test-key",
            modelId = "scripted-model",
            isDefault = true,
            isEnabled = true,
            provider = AiModelProvider.Anthropic,
            maxTokens = 1024,
        });
        await db.SaveChangesAsync();

        var invoker = new RecordingInvoker();
        var mafClient = new ScriptedChatClient(responses);
        var factory = new ScriptedRuntimeClientFactory(mafClient);
        legacyClient ??= new ScriptedAiStreamingClient();
        var uow = TestDbFactory.CreateUnitOfWork(db);
        var quota = new QuotaService(uow, db);
        var uploads = new AgentFileUploadService(db, uow, new FakeStorage(), quota);
        IStorageService storage = attachmentBytes == null ? new FakeStorage() : new AttachmentStorage(attachmentBytes);
        var python = new PythonSandboxClient(
            db, new ThrowingHttpFactory(), NullLogger<PythonSandboxClient>.Instance,
            new HttpContextAccessor(), new FakeJwt());
        var toolHost = new AgentToolHost(
            new CatalogSearchService(db),
            new SqlReadonlyService(db),
            new KnowledgeIndexService(db, storage, NullLogger<KnowledgeIndexService>.Instance),
            new AgentPlatformGateway(uploads, invoker, capabilities: null),
            python,
            new GrantAllPermissions(),
            db,
            NullLogger<AgentToolHost>.Instance,
            mcp,
            chatAttachments: attachmentBytes == null ? null : new ChatAttachmentReadService(db, storage));
        var encryptionKey = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
        var modelService = new AiModelConfigService(
            db,
            new AiApiClient(new HttpClient(), NullLogger<AiApiClient>.Instance),
            new EncryptionService(encryptionKey));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ai:AgentRuntime"] = runtime })
            .Build();
        var engine = new MafChatEngine(factory, toolHost, configuration, NullLogger<MafChatEngine>.Instance);
        var logger = new CapturingLogger<AiChatService>();
        var chat = new AiChatService(
            modelService, legacyClient, db, toolHost, new HttpContextAccessor(), quota,
            logger, mcp, engine);

        return new ChatFixture(db, chat, mafClient, factory, invoker, mcp, logger, user.Id, store.Id);
    }

    private static ChatMessage UserMessage(string text)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(text));
        return new ChatMessage { role = "user", content = document.RootElement.Clone() };
    }

    private static ChatResponse Text(string text) =>
        new(new FrameworkChatMessage(ChatRole.Assistant, text)) { FinishReason = ChatFinishReason.Stop };

    private static ChatResponse ToolCall(string name, string inputJson, string id) =>
        ToolBatch((name, inputJson, id));

    private static ChatResponse ToolBatch(params (string Name, string InputJson, string Id)[] calls) =>
        new(new FrameworkChatMessage(ChatRole.Assistant,
            calls.Select(c => (AIContent)new FunctionCallContent(c.Id, c.Name, ParseArgs(c.InputJson))).ToList()))
        { FinishReason = ChatFinishReason.ToolCalls };

    private static IDictionary<string, object?> ParseArgs(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json)
                ?? new Dictionary<string, object?>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, object?>();
        }
    }

    private static async Task<List<string>> Collect(IAsyncEnumerable<string> stream)
    {
        var chunks = new List<string>();
        await foreach (var chunk in stream)
            chunks.Add(chunk);
        return chunks;
    }

    private sealed class ChatFixture(
        DataContext db,
        AiChatService chat,
        ScriptedChatClient client,
        ScriptedRuntimeClientFactory factory,
        RecordingInvoker invoker,
        EmptyMcpGateway mcp,
        CapturingLogger<AiChatService> logger,
        int userId,
        int storeId) : IAsyncDisposable
    {
        public DataContext Db { get; } = db;
        public AiChatService Chat { get; } = chat;
        public ScriptedChatClient Client { get; } = client;
        public ScriptedRuntimeClientFactory Factory { get; } = factory;
        public RecordingInvoker Invoker { get; } = invoker;
        public EmptyMcpGateway Mcp { get; } = mcp;
        public CapturingLogger<AiChatService> Logger { get; } = logger;
        public int UserId { get; } = userId;
        public int StoreId { get; } = storeId;

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    /// <summary>Scripted Microsoft.Extensions.AI provider; records every request it receives.
    /// Queue items are <see cref="ChatResponse"/> to answer or <see cref="Exception"/> to throw.</summary>
    private sealed class ScriptedChatClient(params object[] responses) : IChatClient
    {
        private readonly Queue<object> _responses = new(responses);
        public List<CapturedRequest> Requests { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<FrameworkChatMessage> messages, ChatOptions? options, CancellationToken ct) =>
            Task.FromResult(Next(messages, options));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<FrameworkChatMessage> messages, ChatOptions? options,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var response = Next(messages, options);
            foreach (var update in response.ToChatResponseUpdates())
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }

        private ChatResponse Next(IEnumerable<FrameworkChatMessage> messages, ChatOptions? options)
        {
            if (_responses.Count == 0)
                throw new InvalidOperationException("No scripted model response remains.");
            Requests.Add(new CapturedRequest(
                options?.Instructions,
                messages.Select(m => new CapturedMessage(m.Role.Value, SerializeContents(m))).ToList(),
                options?.Tools?.Select(t => t.Name).ToList() ?? []));
            return _responses.Dequeue() switch
            {
                Exception error => throw error,
                ChatResponse response => response,
                var other => throw new InvalidOperationException(
                    $"Unsupported scripted item {other?.GetType().Name}."),
            };
        }

        private static string SerializeContents(FrameworkChatMessage message) =>
            JsonSerializer.Serialize(message.Contents.Select(content => content switch
            {
                TextContent text => (object)new { type = "text", text = text.Text },
                FunctionCallContent call => new { type = "tool_use", id = call.CallId, name = call.Name, input = call.Arguments },
                FunctionResultContent result => new { type = "tool_result", tool_use_id = result.CallId, content = result.Result },
                _ => new { type = content.GetType().Name },
            }).ToList());
    }

    private sealed class ScriptedRuntimeClientFactory(IChatClient client) : IAiRuntimeClientFactory
    {
        public int Calls { get; private set; }

        public IChatClient CreateChatClient(AiModelConfig model)
        {
            Calls++;
            return client;
        }
    }

    /// <summary>Legacy-loop scripted provider; used only by the flag=legacy check.</summary>
    private sealed class ScriptedAiStreamingClient(params IReadOnlyList<AiStreamEvent>[] responses)
        : IAiApiClient
    {
        private readonly Queue<IReadOnlyList<AiStreamEvent>> _responses = new(responses);
        public List<CapturedRequest> Requests { get; } = [];

        public Task<AiResponse> SendAsync(AiModelConfig model, AiRequest request, CancellationToken ct) =>
            throw new NotSupportedException("The chat evaluation harness only scripts streaming responses.");

        public async IAsyncEnumerable<AiStreamEvent> StreamAsync(
            AiModelConfig model,
            AiRequest request,
            [EnumeratorCancellation] CancellationToken ct)
        {
            if (_responses.Count == 0)
                throw new InvalidOperationException("No scripted model response remains.");

            Requests.Add(new CapturedRequest(
                request.system,
                request.messages.Select(m => new CapturedMessage(
                    m.role, JsonSerializer.Serialize(m.content))).ToList(),
                request.tools?.Select(t => t.name).ToList() ?? []));

            foreach (var responseEvent in _responses.Dequeue())
            {
                ct.ThrowIfCancellationRequested();
                yield return responseEvent;
                await Task.Yield();
            }
        }
    }

    private sealed record CapturedMessage(string Role, string Content);

    private sealed record CapturedRequest(
        string? System,
        IReadOnlyList<CapturedMessage> Messages,
        IReadOnlyList<string> ToolNames);

    private sealed class EmptyMcpGateway(params AiTool[] tools) : IMcpToolGateway
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<AiTool>> ListToolsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AiTool>>(tools);

        public Task<(object result, string summary)> CallAsync(string llmName, string inputJson, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<(object, string)>((new { error = "unexpected_mcp_call" }, "unexpected MCP call"));
        }
    }

    private sealed class RecordingInvoker : ICabinetRequestInvoker
    {
        public int Calls { get; private set; }
        public string? LastPath { get; private set; }
        public JsonElement? LastBody { get; private set; }

        public Task<(object result, string summary)> InvokeAsync(
            int storeId,
            int userId,
            string method,
            string apiPath,
            IReadOnlyDictionary<string, string> query,
            JsonElement? body,
            CancellationToken ct)
        {
            Calls++;
            LastPath = apiPath;
            LastBody = body?.Clone();
            return Task.FromResult<(object, string)>((new { ok = true }, "called"));
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    private sealed class AttachmentStorage(byte[] bytes) : IStorageService
    {
        public string Bucket => "test";
        public Task<bool> UploadAsync(IFormFile file, string path, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> UploadAsync(Stream stream, string path, long contentLength, string contentType, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> UploadFromUriAsync(string uri, string path, CancellationToken ct = default) => Task.FromResult(true);
        public Task<FileUploadResult?> UploadFromUriWithMetadataAsync(string uri, string path, CancellationToken ct = default) => Task.FromResult<FileUploadResult?>(null);
        public Task<bool> CopyAsync(string sourcePath, string destinationPath, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> DeleteAsync(string path, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => Task.FromResult(true);
        public Task<Stream?> GetStreamAsync(string path, CancellationToken ct = default) => Task.FromResult<Stream?>(new MemoryStream(bytes, writable: false));
        public string GetFullPath(string path) => path;
        public Task StreamToAsync(string path, Stream outputStream, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> GetPresignedUrlAsync(string path, int expirySeconds = 3600) => Task.FromResult(path);
        public Task<string> GetPublicPresignedUrlAsync(string path, int expirySeconds = 3600) => Task.FromResult(path);
        public Task<List<string>> ListObjectsByPrefixAsync(string prefix, CancellationToken ct = default) => Task.FromResult(new List<string>());
    }

    private sealed class TemporaryEnvironmentVariable : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        public TemporaryEnvironmentVariable(string name, string value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
    }
}
