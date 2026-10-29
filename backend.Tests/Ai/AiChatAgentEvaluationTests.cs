using System.Runtime.CompilerServices;
using System.Text.Json;
using Delegat;
using Delegat.Infrastructure.Security;
using Delegat.Models.Ai;
using Delegat.Services;
using Delegat.Services.Ai;
using Delegat.Services.Ai.Agent;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

/// <summary>
/// Deterministic, provider-free checks of the complete model/tool/model chat loop.
/// These focus on tool traces and write gating rather than judging free-form prose.
/// </summary>
[Collection("AiPlanSigningKey")]
public class AiChatAgentEvaluationTests
{
    [Fact]
    public async Task ReadToolResult_IsIncludedInNextModelRequestBeforeFinalAnswer()
    {
        await using var fixture = await CreateFixtureAsync(
            ToolCall("search_catalog", "{\"query\":\"SKU-42\",\"limit\":5}", "read-1"),
            Text("The product is Ceramic Mug."));

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("Find SKU-42")], fixture.StoreId, fixture.UserId,
            sessionId: "eval-read", locale: "en"));

        Assert.Equal(2, fixture.Client.Requests.Count);
        Assert.Contains("search_catalog", fixture.Client.Requests[0].ToolNames);
        Assert.Contains("tool_use", fixture.Client.Requests[1].Messages[1].Content, StringComparison.Ordinal);
        Assert.Contains("tool_result", fixture.Client.Requests[1].Messages[2].Content, StringComparison.Ordinal);
        Assert.Contains("SKU-42", fixture.Client.Requests[1].Messages[2].Content, StringComparison.Ordinal);
        Assert.Contains("Ceramic Mug", AiSseTurnRecorder.Fold(chunks).Content, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"done\"", string.Concat(chunks), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingFacts_AreReadBeforeStandalonePlan_AndPlanDoesNotExecuteWrite()
    {
        using var signingKey = new TemporaryEnvironmentVariable(
            "ENCRYPTION_KEY",
            Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()));
        await using var fixture = await CreateFixtureAsync(
            ToolCall("search_catalog", "{\"query\":\"SKU-MISSING\",\"limit\":5}", "read-1"),
            ToolCall("propose_plan", """
                {"summary":"Update the missing product","steps":[{"description":"Update product","tool":"platform_request","args":{"method":"POST","path":"/products/42","body":{"name":"New name"}}}]}
                """, "plan-1"));

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("Update SKU-MISSING")], fixture.StoreId, fixture.UserId,
            sessionId: "eval-plan", locale: "en"));
        var serialized = string.Concat(chunks);

        Assert.Equal(2, fixture.Client.Requests.Count);
        Assert.Contains("tool_result", fixture.Client.Requests[1].Messages[2].Content, StringComparison.Ordinal);
        Assert.Contains("propose_plan", fixture.Client.Requests[1].ToolNames);
        Assert.Contains("\"type\":\"plan\"", serialized, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Invoker.Calls);
    }

    [Fact]
    public async Task UnconfirmedWrite_IsRejectedAndNeverReachesCabinetInvoker()
    {
        await using var fixture = await CreateFixtureAsync(
            ToolCall("platform_request", "{\"method\":\"POST\",\"path\":\"/products\",\"body\":{\"name\":\"New\"}}", "write-1"),
            Text("I need your confirmation before making that change."));

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("Create a product named New")], fixture.StoreId, fixture.UserId,
            sessionId: "eval-write-gate", locale: "en"));

        Assert.Equal(0, fixture.Invoker.Calls);
        Assert.Equal(2, fixture.Client.Requests.Count);
        Assert.Contains("Not run. Cabinet writes only run after the user confirms",
            fixture.Client.Requests[1].Messages[2].Content, StringComparison.Ordinal);
        Assert.Contains("confirmation", AiSseTurnRecorder.Fold(chunks).Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CabinetGet_RunsImmediatelyAndReturnsItsResultToTheModel()
    {
        await using var fixture = await CreateFixtureAsync(
            ToolCall("platform_request", "{\"method\":\"GET\",\"path\":\"/products?pageSize=5\"}", "get-1"),
            Text("The current page contains five products."));

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("Show me the first five products")], fixture.StoreId, fixture.UserId,
            sessionId: "eval-get", locale: "en"));

        Assert.Equal(1, fixture.Invoker.Calls);
        Assert.Contains("tool_result", fixture.Client.Requests[1].Messages[2].Content, StringComparison.Ordinal);
        Assert.Contains("five products", AiSseTurnRecorder.Fold(chunks).Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CatalogSearch_CannotReturnAnotherStoresProduct()
    {
        await using var fixture = await CreateFixtureAsync(
            ToolCall("search_catalog", "{\"query\":\"PRIVATE-OTHER-STORE\",\"limit\":5}", "isolated-search"),
            Text("I could not find that product in this store."));

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("Find PRIVATE-OTHER-STORE")], fixture.StoreId, fixture.UserId,
            sessionId: "eval-store-isolation", locale: "en"));

        var toolResult = fixture.Client.Requests[1].Messages[2].Content;
        Assert.DoesNotContain("Private Product", toolResult, StringComparison.Ordinal);
        Assert.Contains("0 products", string.Concat(chunks), StringComparison.Ordinal);
        Assert.Contains("could not find", AiSseTurnRecorder.Fold(chunks).Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAndWriteInSameBatch_RunsReadButDefersWriteForConfirmation()
    {
        await using var fixture = await CreateFixtureAsync(
            ToolBatch(
                ("search_catalog", "{\"query\":\"SKU-42\",\"limit\":5}", "mixed-read"),
                ("platform_request", "{\"method\":\"POST\",\"path\":\"/products\",\"body\":{\"name\":\"Unsafe\"}}", "mixed-write")),
            Text("I found the current product. That change is waiting for your confirmation."));

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("Find SKU-42 and create one more product")], fixture.StoreId, fixture.UserId,
            sessionId: "eval-read-write-batch", locale: "en"));

        Assert.Equal(0, fixture.Invoker.Calls);
        Assert.Contains("SKU-42", fixture.Client.Requests[1].Messages[2].Content, StringComparison.Ordinal);
        Assert.Contains(AgentChatTurnGate.WriteNeedsPlan,
            fixture.Client.Requests[1].Messages[2].Content, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"search_catalog\",\"status\":\"done\"", string.Concat(chunks), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchActionsThenGet_UsesDiscoveredCabinetRoute()
    {
        await using var fixture = await CreateFixtureAsync(
            ToolCall("search_actions", "{\"query\":\"list products\",\"limit\":5}", "discover-products"),
            ToolCall("platform_request", "{\"method\":\"GET\",\"path\":\"/products?pageSize=5\"}", "read-products"),
            Text("I checked the product list."));

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("Find the cabinet route for listing products and check the first five")],
            fixture.StoreId, fixture.UserId, sessionId: "eval-discover-route", locale: "en"));

        Assert.Equal(1, fixture.Invoker.Calls);
        Assert.Equal(3, fixture.Client.Requests.Count);
        Assert.Contains("search_actions", fixture.Client.Requests[0].ToolNames);
        Assert.Contains("/products", fixture.Client.Requests[1].Messages[2].Content, StringComparison.Ordinal);
        Assert.Contains("tool_result", fixture.Client.Requests[2].Messages[2].Content, StringComparison.Ordinal);
        Assert.Contains("checked the product list", AiSseTurnRecorder.Fold(chunks).Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MappingOnlyPlan_DoesNotInventAnExportStep()
    {
        using var signingKey = new TemporaryEnvironmentVariable(
            "ENCRYPTION_KEY",
            Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()));
        await using var fixture = await CreateFixtureAsync(
            ToolCall("propose_plan", "{\"summary\":\"Connect the product title\",\"steps\":[{\"description\":\"Connect the title field\",\"tool\":\"platform_request\",\"args\":{\"method\":\"POST\",\"path\":\"/marketplace/connections/3/mappings/9/graph/connect\",\"body\":{\"adapterFieldKey\":\"title\"}}}]}", "mapping-only-plan"));

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("Connect the title field; do not export yet")], fixture.StoreId, fixture.UserId,
            sessionId: "eval-mapping-only", locale: "en"));

        Assert.Equal(0, fixture.Invoker.Calls);
        Assert.Single(fixture.Client.Requests);
        var serialized = string.Concat(chunks);
        Assert.Contains("graph/connect", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("export-batch", serialized, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"plan\"", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlanInMixedBatch_DefersEverySiblingTool()
    {
        using var signingKey = new TemporaryEnvironmentVariable(
            "ENCRYPTION_KEY",
            Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()));
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
            sessionId: "eval-mixed-batch", locale: "en"));

        Assert.Equal(0, fixture.Invoker.Calls);
        Assert.Equal(0, mcp.Calls);
        Assert.Contains("\"type\":\"plan\"", string.Concat(chunks), StringComparison.Ordinal);
        Assert.Single(fixture.Client.Requests);
    }

    [Fact]
    public async Task MultiplePlansInOneBatch_PresentsOnlyTheFirstValidPlan()
    {
        using var signingKey = new TemporaryEnvironmentVariable(
            "ENCRYPTION_KEY",
            Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()));
        await using var fixture = await CreateFixtureAsync(
            ToolBatch(
                ("propose_plan", "{\"summary\":\"First proposed action\",\"steps\":[{\"description\":\"Do the first action\",\"tool\":\"platform_request\",\"args\":{\"method\":\"POST\",\"path\":\"/products\",\"body\":{\"name\":\"First\"}}}]}", "first-plan"),
                ("propose_plan", "{\"summary\":\"Second proposed action\",\"steps\":[{\"description\":\"Do the second action\",\"tool\":\"platform_request\",\"args\":{\"method\":\"POST\",\"path\":\"/products\",\"body\":{\"name\":\"Second\"}}}]}", "second-plan")));

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("Create two products")], fixture.StoreId, fixture.UserId,
            sessionId: "eval-multiple-plans", locale: "en"));

        var plan = Assert.Single(chunks,
            chunk => chunk.Contains("\"type\":\"plan\"", StringComparison.Ordinal));
        Assert.Contains("First proposed action", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("Second proposed action", string.Concat(chunks), StringComparison.Ordinal);
        Assert.Single(fixture.Client.Requests);
        Assert.Equal(0, fixture.Invoker.Calls);
    }

    [Fact]
    public async Task KnownInputWrite_ProposesPlanWithoutUnneededReads()
    {
        using var signingKey = new TemporaryEnvironmentVariable(
            "ENCRYPTION_KEY",
            Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()));
        await using var fixture = await CreateFixtureAsync(
            ToolCall("propose_plan", "{\"summary\":\"Create Ceramic Mug\",\"steps\":[{\"description\":\"Create Ceramic Mug\",\"tool\":\"platform_request\",\"args\":{\"method\":\"POST\",\"path\":\"/products\",\"body\":{\"name\":\"Ceramic Mug\",\"sku\":\"SKU-NEW\"}}}]}", "known-write-plan"));

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("Create SKU-NEW named Ceramic Mug")], fixture.StoreId, fixture.UserId,
            sessionId: "eval-known-write", locale: "en"));

        Assert.Single(fixture.Client.Requests);
        Assert.Equal(0, fixture.Invoker.Calls);
        Assert.Contains("\"type\":\"plan\"", string.Concat(chunks), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("POST", "/products", "{\"name\":\"Unsafe\"}")]
    [InlineData("PATCH", "/products/42", "{\"name\":\"Unsafe\"}")]
    [InlineData("DELETE", "/products/42", "{}")]
    [InlineData("POST", "/products/bulk", "{\"updates\":{\"price\":10}}")]
    [InlineData("POST", "/marketplace/connections/1/products/export-batch", "{\"where\":\"@inStock\"}")]
    [InlineData("POST", "/tools/catalog-connect/import-unlinked", "{\"connectionId\":1}")]
    [InlineData("DELETE", "/marketplace/connections/1", "{}")]
    public async Task UnconfirmedCatalogAndMarketplaceMutations_NeverReachInvoker(
        string method, string path, string body)
    {
        using var bodyDocument = JsonDocument.Parse(body);
        var inputJson = JsonSerializer.Serialize(new { method, path, body = bodyDocument.RootElement });
        await using var fixture = await CreateFixtureAsync(
            ToolCall("platform_request", inputJson, "blocked-mutation"),
            Text("That operation is waiting for your confirmation."));

        await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage($"Please {method} {path}")], fixture.StoreId, fixture.UserId,
            sessionId: "eval-blocked-mutation", locale: "en"));

        Assert.Equal(0, fixture.Invoker.Calls);
        Assert.Contains(AgentChatTurnGate.WriteNeedsPlan,
            fixture.Client.Requests[1].Messages[2].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfirmedPlan_ExecutesThePersistedStepOnce()
    {
        using var signingKey = new TemporaryEnvironmentVariable(
            "ENCRYPTION_KEY",
            Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()));
        const string sessionId = "eval-confirmed-plan";
        const string userText = "Create a product named Approved";
        await using var fixture = await CreateFixtureAsync(
            ToolCall("propose_plan", "{\"summary\":\"Create Approved\",\"steps\":[{\"description\":\"Create the Approved product\",\"tool\":\"platform_request\",\"args\":{\"method\":\"POST\",\"path\":\"/products\",\"body\":{\"name\":\"Approved\"}}}]}", "confirmed-plan"));

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage(userText)], fixture.StoreId, fixture.UserId,
            sessionId: sessionId, locale: "en"));
        var snapshot = AiSseTurnRecorder.Fold(chunks);
        var planPayload = Assert.Single(chunks,
            chunk => chunk.Contains("\"type\":\"plan\"", StringComparison.Ordinal));
        using var planDocument = JsonDocument.Parse(planPayload[(planPayload.IndexOf("data:", StringComparison.Ordinal) + 5)..].Trim());
        var proposalId = planDocument.RootElement.GetProperty("data").GetProperty("proposalId").GetString()!;

        await AiConversationTurnStore.AppendUserIfNeededAsync(
            fixture.Db, sessionId, fixture.StoreId, fixture.UserId, UserMessage(userText), default);
        await AiConversationTurnStore.UpsertAssistantAsync(
            fixture.Db, sessionId, fixture.StoreId, fixture.UserId, snapshot, default);

        Assert.Equal(0, fixture.Invoker.Calls);
        var confirmed = await fixture.Chat.ConsumeConfirmedPlanAsync(
            sessionId, proposalId, fixture.StoreId, fixture.UserId, default);
        Assert.NotNull(confirmed);
        Assert.Null(await fixture.Chat.ConsumeConfirmedPlanAsync(
            sessionId, proposalId, fixture.StoreId, fixture.UserId, default));

        var execution = await Collect(fixture.Chat.ExecutePlanStreamAsync(
            confirmed!.ToList(), fixture.StoreId, fixture.UserId, proposalId, default));

        Assert.Equal(1, fixture.Invoker.Calls);
        Assert.Contains("\"ok\":true", string.Concat(execution), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectedMcpCall_IsRejectedUntilAStandalonePlanIsConfirmed()
    {
        var mcp = new EmptyMcpGateway(new AiTool
        {
            name = "mcp3_update_record",
            description = "Update a connected record",
            input_schema = new AiToolInputSchema { type = "object" },
        });
        await using var fixture = await CreateFixtureAsync(
            mcp,
            ToolCall("mcp3_update_record", "{\"id\":42}", "mcp-write"),
            Text("I need your confirmation before using the connected service."));

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("Update the connected record")], fixture.StoreId, fixture.UserId,
            sessionId: "eval-mcp-gate", locale: "en"));

        Assert.Equal(0, mcp.Calls);
        Assert.Contains("mcp3_update_record", fixture.Client.Requests[0].ToolNames);
        Assert.Contains(AgentChatTurnGate.ExternalCapabilityNeedsPlan,
            fixture.Client.Requests[1].Messages[2].Content, StringComparison.Ordinal);
        Assert.Contains("confirmation", AiSseTurnRecorder.Fold(chunks).Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PythonCall_IsRejectedBeforeSandboxDispatch()
    {
        await using var fixture = await CreateFixtureAsync(
            ToolCall("run_python", "{\"code\":\"print('read data')\"}", "python-read"),
            Text("I need your confirmation before running that code."));

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("Run this harmless calculation")], fixture.StoreId, fixture.UserId,
            sessionId: "eval-python-gate", locale: "en"));

        Assert.Contains(AgentChatTurnGate.ExternalCapabilityNeedsPlan,
            fixture.Client.Requests[1].Messages[2].Content, StringComparison.Ordinal);
        Assert.DoesNotContain("sandbox_not_configured", string.Concat(chunks), StringComparison.Ordinal);
        Assert.Contains("confirmation", AiSseTurnRecorder.Fold(chunks).Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ToolFailure_IsReturnedToModelAndDoesNotTerminateTurn()
    {
        await using var fixture = await CreateFixtureAsync(
            ToolCall("search_catalog", "{", "bad-read"),
            Text("The lookup failed, so I could not verify the product."));

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("Find product SKU-ERR")], fixture.StoreId, fixture.UserId,
            sessionId: "eval-error", locale: "en"));

        Assert.Equal(2, fixture.Client.Requests.Count);
        Assert.Contains("error", fixture.Client.Requests[1].Messages[2].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lookup failed", AiSseTurnRecorder.Fold(chunks).Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"type\":\"error\"", string.Concat(chunks), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RepeatedReadCall_IsMeasuredWithoutLoggingItsArguments()
    {
        await using var fixture = await CreateFixtureAsync(
            ToolCall("search_catalog", "{\"query\":\"PRIVATE-SKU-9\",\"limit\":5}", "read-1"),
            ToolCall("search_catalog", "{ \"query\": \"PRIVATE-SKU-9\", \"limit\": 5 }", "read-2"),
            Text("I already checked that product; no matching item was found."));

        await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("Find PRIVATE-SKU-9")], fixture.StoreId, fixture.UserId,
            sessionId: "eval-repeat", locale: "en"));

        var telemetry = Assert.Single(fixture.Logger.Messages, m => m.Contains(
            "AI chat turn telemetry", StringComparison.Ordinal));
        Assert.Contains("ToolRounds=2", telemetry, StringComparison.Ordinal);
        Assert.Contains("RepeatedModelToolCalls=1", telemetry, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE-SKU-9", telemetry, StringComparison.Ordinal);
    }

    private static Task<ChatFixture> CreateFixtureAsync(params IReadOnlyList<AiStreamEvent>[] responses) =>
        CreateFixtureAsync(new EmptyMcpGateway(), responses);

    private static async Task<ChatFixture> CreateFixtureAsync(
        EmptyMcpGateway mcp,
        params IReadOnlyList<AiStreamEvent>[] responses)
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser("agent-eval");
        var store = data.AddStore("Eval store", "RUB", "UTC", user);
        data.AddProduct(store.Id, name: "Ceramic Mug", sku: "SKU-42");
        var otherStore = data.AddStore("Other eval store", "RUB", "UTC", user);
        data.AddProduct(otherStore.Id, name: "Private Product", sku: "PRIVATE-OTHER-STORE");
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
        var client = new ScriptedAiStreamingClient(responses);
        var uow = TestDbFactory.CreateUnitOfWork(db);
        var quota = new QuotaService(uow, db);
        var uploads = new AgentFileUploadService(db, uow, new FakeStorage(), quota);
        var python = new PythonSandboxClient(
            db, new ThrowingHttpFactory(), NullLogger<PythonSandboxClient>.Instance,
            new HttpContextAccessor(), new FakeJwt());
        var toolHost = new AgentToolHost(
            new CatalogSearchService(db),
            new SqlReadonlyService(db),
            new KnowledgeIndexService(db, new FakeStorage(), NullLogger<KnowledgeIndexService>.Instance),
            new AgentPlatformGateway(uploads, invoker),
            python,
            new GrantAllPermissions(),
            db,
            NullLogger<AgentToolHost>.Instance,
            mcp);
        var encryptionKey = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
        var modelService = new AiModelConfigService(
            db,
            new AiApiClient(new HttpClient(), NullLogger<AiApiClient>.Instance),
            new EncryptionService(encryptionKey));
        var logger = new CapturingLogger<AiChatService>();
        var chat = new AiChatService(
            modelService, client, db, toolHost, new HttpContextAccessor(), quota,
            logger, mcp);

        return new ChatFixture(db, chat, client, invoker, mcp, logger, user.Id, store.Id);
    }

    private static ChatMessage UserMessage(string text)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(text));
        return new ChatMessage { role = "user", content = document.RootElement.Clone() };
    }

    private static IReadOnlyList<AiStreamEvent> Text(string text) =>
    [
        new()
        {
            type = "content_block_delta",
            delta = new AiStreamDelta { type = "text_delta", text = text },
        },
        new() { type = "message_stop" },
    ];

    private static IReadOnlyList<AiStreamEvent> ToolCall(string name, string inputJson, string id) =>
    [
        new()
        {
            type = "content_block_start",
            content_block = new AiContentBlock { type = "tool_use", name = name, id = id },
        },
        new()
        {
            type = "content_block_delta",
            delta = new AiStreamDelta { type = "input_json_delta", partial_json = inputJson },
        },
        new() { type = "content_block_stop" },
        new() { type = "message_stop" },
    ];

    private static IReadOnlyList<AiStreamEvent> ToolBatch(params (string Name, string InputJson, string Id)[] calls)
    {
        var events = new List<AiStreamEvent>();
        foreach (var (name, inputJson, id) in calls)
        {
            events.Add(new AiStreamEvent
            {
                type = "content_block_start",
                content_block = new AiContentBlock { type = "tool_use", name = name, id = id },
            });
            events.Add(new AiStreamEvent
            {
                type = "content_block_delta",
                delta = new AiStreamDelta { type = "input_json_delta", partial_json = inputJson },
            });
            events.Add(new AiStreamEvent { type = "content_block_stop" });
        }
        events.Add(new AiStreamEvent { type = "message_stop" });
        return events;
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
        ScriptedAiStreamingClient client,
        RecordingInvoker invoker,
        EmptyMcpGateway mcp,
        CapturingLogger<AiChatService> logger,
        int userId,
        int storeId) : IAsyncDisposable
    {
        public DataContext Db { get; } = db;
        public AiChatService Chat { get; } = chat;
        public ScriptedAiStreamingClient Client { get; } = client;
        public RecordingInvoker Invoker { get; } = invoker;
        public EmptyMcpGateway Mcp { get; } = mcp;
        public CapturingLogger<AiChatService> Logger { get; } = logger;
        public int UserId { get; } = userId;
        public int StoreId { get; } = storeId;

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed record CapturedMessage(string Role, string Content);

    private sealed record CapturedRequest(
        string? System,
        IReadOnlyList<CapturedMessage> Messages,
        IReadOnlyList<string> ToolNames);

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
