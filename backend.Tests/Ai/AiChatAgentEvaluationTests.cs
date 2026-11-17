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
    [Theory]
    [InlineData("ru", "перегенерируй штрихкоды выбранных товаров")]
    [InlineData("en", "regenerate barcodes for selected products")]
    public async Task NativeBarcodeRegeneration_SearchesThenProposesOneBulkWriteWithoutPython(string locale, string query)
    {
        using var key = new TemporaryEnvironmentVariable("ENCRYPTION_KEY", Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()));
        var input = JsonSerializer.Serialize(new { summary = "Regenerate selected product barcodes", affectedCount = 3, isDestructive = true,
            steps = new[] { new { tool = "platform_request", description = "Generate new barcodes for the selected products",
                args = new { method = "PATCH", path = "/products/bulk", body = new { productIds = new[] { 7, 12, 28 }, wait = true,
                    updates = new { barcode = "", generateBarcodes = true } } } } } });
        await using var fixture = await CreateFixtureAsync(
            ToolCall("search_actions", JsonSerializer.Serialize(new { query, limit = 1 }), "discover-native"),
            ToolCall("propose_plan", input, "bulk-proposal"));
        var user = UserMessage(query + " (productIds: 7, 12, 28)");
        const string session = "native-barcode-bulk";
        var chunks = await Collect(fixture.Chat.StreamChatAsync([user], fixture.StoreId, fixture.UserId, sessionId: session, locale: locale));
        Assert.Equal(2, fixture.Client.Requests.Count);
        Assert.Contains("/products/bulk", fixture.Client.Requests[1].Messages.Last().Content);
        Assert.Contains("generateBarcodes", fixture.Client.Requests[1].Messages.Last().Content);
        Assert.Equal(0, fixture.Invoker.Calls);
        var snapshot = AiSseTurnRecorder.Fold(chunks);
        using var document = JsonDocument.Parse(snapshot.ToolCallsJson!);
        var plan = document.RootElement.GetProperty("plan");
        Assert.Single(plan.GetProperty("steps").EnumerateArray());
        Assert.DoesNotContain("run_python", string.Concat(chunks));
        await AiConversationTurnStore.AppendUserIfNeededAsync(fixture.Db, session, fixture.StoreId, fixture.UserId, user, default);
        await AiConversationTurnStore.UpsertAssistantAsync(fixture.Db, session, fixture.StoreId, fixture.UserId, snapshot, default);
        var proposal = plan.GetProperty("proposalId").GetString()!;
        var steps = await fixture.Chat.ConsumeConfirmedPlanAsync(session, proposal, fixture.StoreId, fixture.UserId, default);
        await Collect(fixture.Chat.ExecutePlanStreamAsync(steps!.ToList(), fixture.StoreId, fixture.UserId, proposal));
        Assert.Equal(1, fixture.Invoker.Calls);
        Assert.Equal("/api/products/bulk", fixture.Invoker.LastPath);
        Assert.Equal(new[] { 7, 12, 28 }, fixture.Invoker.LastBody!.Value.GetProperty("productIds").EnumerateArray().Select(id => id.GetInt32()));
        Assert.True(fixture.Invoker.LastBody.Value.GetProperty("updates").GetProperty("generateBarcodes").GetBoolean());
    }

    [Fact]
    public async Task RepeatedCommonUpdates_AreRejectedBeforeSigningAndReproposedAsOneBulkStep()
    {
        using var key = new TemporaryEnvironmentVariable("ENCRYPTION_KEY", Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()));
        var repeated = JsonSerializer.Serialize(new { summary = "Update selected stock", steps = new[] { 7, 12 }.Select(id => new {
            tool = "platform_request", description = "Update stock", args = new { method = "PATCH", path = $"/products/{id}", body = new { storageQuantity = 5 } },
        }) });
        const string bulk = """{"summary":"Update selected stock","steps":[{"tool":"platform_request","description":"Update stock","args":{"method":"PATCH","path":"/products/bulk","body":{"productIds":[7,12],"updates":{"storageQuantity":5}}}}]}""";
        await using var fixture = await CreateFixtureAsync(ToolCall("propose_plan", repeated, "inefficient"), ToolCall("propose_plan", bulk, "efficient"));
        var chunks = await Collect(fixture.Chat.StreamChatAsync([UserMessage("Set stock to 5 on the selected products")], fixture.StoreId, fixture.UserId, sessionId: "bulk-efficiency", locale: "en"));
        Assert.Equal(2, fixture.Client.Requests.Count);
        Assert.Contains("Plan not presented", fixture.Client.Requests[1].Messages.Last().Content);
        Assert.Contains("exactly the same productIds", fixture.Client.Requests[1].Messages.Last().Content);
        var presented = Assert.Single(chunks, c => c.Contains("\"type\":\"plan\"", StringComparison.Ordinal));
        Assert.Contains("/products/bulk", presented);
        Assert.Equal(0, fixture.Invoker.Calls);
    }

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

    [Theory]
    [InlineData("en", "What is the price of SKU-42?", "100")]
    [InlineData("ru", "Сколько стоит SKU-42?", "100")]
    public async Task ExactSkuPrice_RequiresGroundedFieldReadAfterIdentitySearch(
        string locale, string question, string expectedPrice)
    {
        await using var fixture = await CreateFixtureAsync(
            ToolCall("search_catalog", "{\"query\":\"SKU-42\",\"limit\":5}", "identity"),
            ToolCall("sql_readonly", "{\"sql\":\"SELECT id, sku, price FROM products WHERE sku = 'SKU-42' LIMIT 1\"}", "price"),
            Text(expectedPrice));

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage(question)], fixture.StoreId, fixture.UserId,
            sessionId: "eval-price-" + locale, locale: locale));

        Assert.Equal(3, fixture.Client.Requests.Count);
        var identityResult = fixture.Client.Requests[1].Messages.Last().Content;
        Assert.Contains("SKU-42", identityResult, StringComparison.Ordinal);
        Assert.DoesNotContain("\"price\"", identityResult, StringComparison.OrdinalIgnoreCase);
        var fieldResult = fixture.Client.Requests[2].Messages.Last().Content;
        Assert.Contains("\"price\"", fieldResult, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expectedPrice, fieldResult, StringComparison.Ordinal);
        Assert.Equal(expectedPrice, AiSseTurnRecorder.Fold(chunks).Content.Trim());
        Assert.Equal(0, fixture.Invoker.Calls);
        Assert.Contains("ModelRounds=3", fixture.Logger.Messages.Single(m => m.Contains("AI chat turn telemetry", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("en", "What is the price of SKU-42?")]
    [InlineData("ru", "Сколько стоит SKU-42?")]
    public async Task ExactSkuPrice_CanBeGroundedInOneReadWithoutCatalogSearch(
        string locale, string question)
    {
        await using var fixture = await CreateFixtureAsync(
            ToolCall("sql_readonly", "{\"sql\":\"SELECT id, sku, price FROM products WHERE sku = 'SKU-42' LIMIT 1\"}", "exact-price"),
            Text("100"));

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage(question)], fixture.StoreId, fixture.UserId,
            sessionId: "eval-direct-price-" + locale, locale: locale));

        Assert.Equal(2, fixture.Client.Requests.Count);
        var result = fixture.Client.Requests[1].Messages.Last().Content;
        using var blocks = JsonDocument.Parse(result);
        using var payload = JsonDocument.Parse(blocks.RootElement[0].GetProperty("content").GetString()!);
        Assert.Equal("products", payload.RootElement.GetProperty("table").GetString());
        Assert.Contains(payload.RootElement.GetProperty("columns").EnumerateArray(),
            column => column.GetString() == "price");
        Assert.Contains("SKU-42", result, StringComparison.Ordinal);
        Assert.Equal("100", AiSseTurnRecorder.Fold(chunks).Content.Trim());
        Assert.Equal(0, fixture.Invoker.Calls);
        var telemetry = fixture.Logger.Messages.Single(m => m.Contains("AI chat turn telemetry", StringComparison.Ordinal));
        Assert.Contains("ModelRounds=2", telemetry, StringComparison.Ordinal);
        Assert.Contains("ExecutedTools=1", telemetry, StringComparison.Ordinal);
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
    public async Task SummaryOnlyPlan_IsRejectedBeforePresentation_AndCanBeCorrected()
    {
        using var signingKey = new TemporaryEnvironmentVariable(
            "ENCRYPTION_KEY",
            Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()));
        await using var fixture = await CreateFixtureAsync(
            ToolCall("propose_plan", "{\"summary\":\"Create Approved\",\"steps\":[]}", "invalid-plan"),
            ToolCall("propose_plan", "{\"summary\":\"Create Approved\",\"steps\":[{\"tool\":\"platform_request\",\"description\":\"Create Approved\",\"args\":{\"method\":\"POST\",\"path\":\"/products\",\"body\":{\"name\":\"Approved\"}}}]}", "valid-plan"));

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("Create Approved")], fixture.StoreId, fixture.UserId,
            sessionId: "eval-invalid-plan", locale: "en"));

        Assert.Equal(2, fixture.Client.Requests.Count);
        Assert.Contains("Plan not presented", fixture.Client.Requests[1].Messages.Last().Content,
            StringComparison.Ordinal);
        Assert.Single(chunks, chunk => chunk.Contains("\"type\":\"plan\"", StringComparison.Ordinal));
        Assert.Equal(0, fixture.Invoker.Calls);
    }

    [Fact]
    public async Task MultipartPlatformStep_IsRejectedBeforePresentation_AndCanBeCorrected()
    {
        using var signingKey = new TemporaryEnvironmentVariable(
            "ENCRYPTION_KEY",
            Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()));
        const string multipart = """
            {"summary":"Save the generated file","steps":[{"description":"Upload the generated file","tool":"platform_request","args":{"method":"PUT","path":"/store-storage/files"}}]}
            """;
        const string corrected = """
            {"summary":"Save the generated file","steps":[{"description":"Save the generated text as a store file","tool":"platform_request","args":{"method":"POST","path":"/store-storage/files/from-text","body":{"fileName":"report.csv","text":"a,b"}}}]}
            """;
        await using var fixture = await CreateFixtureCoreAsync(new EmptyMcpGateway(), null, null, null,
            [ToolCall("propose_plan", multipart, "multipart-plan"), ToolCall("propose_plan", corrected, "json-plan")],
            capabilities: CabinetCapabilities.Standard());

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("Save this report as a store file")], fixture.StoreId, fixture.UserId,
            sessionId: "eval-multipart-plan", locale: "en"));

        Assert.Equal(2, fixture.Client.Requests.Count);
        var correction = fixture.Client.Requests[1].Messages.Last().Content;
        Assert.Contains("Plan not presented", correction, StringComparison.Ordinal);
        Assert.Contains("step 1", correction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("requires multipart", correction, StringComparison.Ordinal);
        Assert.Contains("JSON-only", correction, StringComparison.Ordinal);
        Assert.Contains("/store-storage/files/from-text", correction, StringComparison.Ordinal);
        Assert.Single(chunks, chunk => chunk.Contains("\"type\":\"plan\"", StringComparison.Ordinal));
        Assert.Equal(0, fixture.Invoker.Calls);
    }

    [Fact]
    public async Task InventedRoutePlatformStep_IsRejectedBeforePresentation_AndCanBeCorrected()
    {
        using var signingKey = new TemporaryEnvironmentVariable(
            "ENCRYPTION_KEY",
            Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()));
        const string invented = """
            {"summary":"Save the generated file","steps":[{"description":"Create the file","tool":"platform_request","args":{"method":"POST","path":"/store-files","body":{"name":"report.csv","content":"a,b"}}}]}
            """;
        const string corrected = """
            {"summary":"Save the generated file","steps":[{"description":"Save the generated text as a store file","tool":"platform_request","args":{"method":"POST","path":"/store-storage/files/from-text","body":{"fileName":"report.csv","text":"a,b"}}}]}
            """;
        await using var fixture = await CreateFixtureCoreAsync(new EmptyMcpGateway(), null, null, null,
            [ToolCall("propose_plan", invented, "invented-plan"), ToolCall("propose_plan", corrected, "json-plan")],
            capabilities: CabinetCapabilities.Standard());

        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("Save this report as a store file")], fixture.StoreId, fixture.UserId,
            sessionId: "eval-invented-plan", locale: "en"));

        Assert.Equal(2, fixture.Client.Requests.Count);
        var correction = fixture.Client.Requests[1].Messages.Last().Content;
        Assert.Contains("Plan not presented", correction, StringComparison.Ordinal);
        Assert.Contains("/store-files", correction, StringComparison.Ordinal);
        Assert.Single(chunks, chunk => chunk.Contains("\"type\":\"plan\"", StringComparison.Ordinal));
        Assert.Equal(0, fixture.Invoker.Calls);
    }

    [Fact]
    public async Task ConfirmedPlanStep_RecheckedAtExecution_NeverReachesInvoker()
    {
        using var signingKey = new TemporaryEnvironmentVariable(
            "ENCRYPTION_KEY",
            Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()));
        await using var fixture = await CreateFixtureCoreAsync(new EmptyMcpGateway(), null, null, null,
            [Text("done")], capabilities: CabinetCapabilities.Standard());
        using var argsDoc = JsonDocument.Parse(
            """{"method":"PUT","path":"/store-storage/files"}""");
        var steps = new List<PlanStepInput>
        {
            new()
            {
                Tool = "platform_request",
                Description = "Upload the generated file",
                Args = argsDoc.RootElement.Clone(),
            },
        };

        var chunks = await Collect(fixture.Chat.ExecutePlanStreamAsync(
            steps, fixture.StoreId, fixture.UserId, "proposal-1", locale: "en"));
        var serialized = string.Concat(chunks);

        Assert.Equal(0, fixture.Invoker.Calls);
        Assert.Contains("multipart_required", serialized, StringComparison.Ordinal);
        Assert.Contains("transport_mismatch", serialized, StringComparison.Ordinal);
        Assert.Contains("Route rejected before dispatch", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AttachmentReadInMixedBatch_CompletesBeforePlanIsPresented()
    {
        using var signingKey = new TemporaryEnvironmentVariable(
            "ENCRYPTION_KEY",
            Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()));
        const string sessionId = "eval-attachment-plan";
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
        Assert.DoesNotContain("premature-plan", serialized, StringComparison.Ordinal);
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
    public async Task FailedPythonPlan_ReviewsRealErrorAndProposesCorrectionWithoutExecutingIt()
    {
        using var key = new TemporaryEnvironmentVariable("ENCRYPTION_KEY", Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()));
        using var sandbox = new TemporaryEnvironmentVariable("SANDBOX_URL", "http://sandbox.example.test");
        var python = new ScriptedPythonHttp(new { exitCode = 1, stdout = "unverified partial output", stderr = "AttributeError: 'DelegatClient' object has no attribute 'request'" });
        const string fixedCode = "result = client.platform('GET', '/products?startIndex=0&pageSize=100')\nprint(result['items'])";
        var first = Text("FABRICATED PRE-EXECUTION TABLE").Take(1)
            .Concat(PythonPlan("client.get('/products')", "bad-code")).ToArray();
        await using var fixture = await CreateFixtureCoreAsync(new EmptyMcpGateway(), null, null, null,
            [first, ToolCall("run_python", JsonSerializer.Serialize(new { code = fixedCode }), "forbidden-review-retry"),
             PythonPlan(fixedCode, "corrected-plan")], python);
        var (proposalId, steps) = await PrepareConfirmedPlanAsync(fixture, "python-failure");
        var chunks = await Collect(fixture.Chat.ExecutePlanStreamAsync(steps, fixture.StoreId, fixture.UserId,
            proposalId, sessionId: "python-failure", locale: "en"));
        Assert.Equal(1, python.Calls);
        Assert.Equal(0, fixture.Invoker.Calls);
        Assert.Equal(["propose_plan"], fixture.Client.Requests[1].ToolNames);
        var reviewContext = fixture.Client.Requests[1].Messages.Last().Content;
        Assert.Contains("AttributeError", reviewContext);
        Assert.Contains("unverified partial output", reviewContext);
        Assert.Contains("Find products matching the requested rule", reviewContext);
        Assert.DoesNotContain("FABRICATED", reviewContext);
        Assert.Contains("client.platform", fixture.Client.Requests[1].System);
        Assert.Contains("Not run. Execution is already finished", fixture.Client.Requests[2].Messages.Last().Content);
        var turns = AiSseTurnRecorder.FoldTurns(chunks);
        Assert.Equal(2, turns.Count);
        using var failed = JsonDocument.Parse(turns[0].ToolCallsJson!);
        Assert.True(failed.RootElement.GetProperty("planFailed").GetBoolean());
        Assert.DoesNotContain("FABRICATED", turns[0].Content);
        using var recovery = JsonDocument.Parse(turns[1].ToolCallsJson!);
        var newProposalId = recovery.RootElement.GetProperty("plan").GetProperty("proposalId").GetString()!;
        Assert.NotEqual(proposalId, newProposalId);
        Assert.False(recovery.RootElement.TryGetProperty("planConfirmed", out _));
        await AiConversationTurnStore.UpsertAssistantAsync(fixture.Db, "python-failure", fixture.StoreId, fixture.UserId, turns[0], default);
        await AiConversationTurnStore.AppendAssistantAsync(fixture.Db, "python-failure", fixture.StoreId, fixture.UserId, turns[1], default);
        Assert.Null(await fixture.Chat.ConsumeConfirmedPlanAsync("python-failure", proposalId, fixture.StoreId, fixture.UserId, default));
        var corrected = await fixture.Chat.ConsumeConfirmedPlanAsync("python-failure", newProposalId, fixture.StoreId, fixture.UserId, default);
        Assert.Equal(fixedCode, corrected![0].Args!.Value.GetProperty("code").GetString());
        Assert.Equal(1, python.Calls); // Consuming approval does not itself dispatch any tool.
    }

    [Fact]
    public async Task SuccessfulPythonPlan_AnswersFromActualStdoutInASeparateTurn()
    {
        using var key = new TemporaryEnvironmentVariable("ENCRYPTION_KEY", Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()));
        using var sandbox = new TemporaryEnvironmentVariable("SANDBOX_URL", "http://sandbox.example.test");
        const string table = "| ID | Name | a count | consonants |\n| 12 | Cabinet stand | 2 | 8 |\nMatched: 1";
        var python = new ScriptedPythonHttp(new { exitCode = 0, stdout = table, stderr = "" });
        await using var fixture = await CreateFixtureCoreAsync(new EmptyMcpGateway(), null, null, null,
            [PythonPlan("print('computed table')", "calculate"), Text(table)], python);
        var (proposalId, steps) = await PrepareConfirmedPlanAsync(fixture, "python-success");
        var chunks = await Collect(fixture.Chat.ExecutePlanStreamAsync(steps, fixture.StoreId, fixture.UserId,
            proposalId, sessionId: "python-success", locale: "en"));
        Assert.Equal(1, python.Calls);
        Assert.Contains("Matched: 1", fixture.Client.Requests[1].Messages.Last().Content);
        var turns = AiSseTurnRecorder.FoldTurns(chunks);
        Assert.Equal(2, turns.Count);
        // The structured execution_status summary is not assistant prose: it lives
        // in the outcome envelope, and no text event may carry it.
        Assert.Contains("Plan steps completed: 1 of 1", turns[0].Content);
        Assert.Contains("executionOutcome", turns[0].ToolCallsJson);
        Assert.DoesNotContain(chunks,
            c => c.Contains("\"type\":\"text\"") && c.Contains("Plan steps completed"));
        Assert.Equal(table, turns[1].Content);
        Assert.DoesNotContain("planFailed", turns[1].ToolCallsJson);
        Assert.Contains("executionProposalId", turns[1].ToolCallsJson);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompleteSuccessfulPython_OffersCorrectionWithoutPublishingThenDeletingADraftTable(bool blockedRoundFirst)
    {
        using var key = new TemporaryEnvironmentVariable("ENCRYPTION_KEY", Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()));
        using var sandbox = new TemporaryEnvironmentVariable("SANDBOX_URL", "http://sandbox.example.test");
        const string draft = "UNVERIFIED TABLE THAT MUST NEVER BE VISIBLE";
        var python = new ScriptedPythonHttp(new { exitCode = 0, stdout = "Read 50 rows", stderr = "" });
        var recovery = Text(draft).Take(1).Concat(PythonPlan(
            "print('read remaining pages after checking completeness')", "complete-input")).ToArray();
        var responses = new List<IReadOnlyList<AiStreamEvent>> {
            PythonPlan("res = client.sql('SELECT id, sku, name FROM products')", "limited-input"),
        };
        if (blockedRoundFirst)
            responses.Add(Text(draft).Take(1).Concat(ToolCall("run_python", "{\"code\":\"print('retry')\"}", "blocked")).ToArray());
        responses.Add(recovery);
        await using var fixture = await CreateFixtureCoreAsync(new EmptyMcpGateway(), null, null, null, responses.ToArray(), python);
        var (proposalId, steps) = await PrepareConfirmedPlanAsync(fixture, "incomplete-success");
        var chunks = await Collect(fixture.Chat.ExecutePlanStreamAsync(steps, fixture.StoreId, fixture.UserId,
            proposalId, sessionId: "incomplete-success", locale: "en"));
        // Check every emitted event, not merely the folded snapshot after draft removal.
        Assert.DoesNotContain(draft, string.Concat(chunks));
        Assert.DoesNotContain("All steps finished", string.Concat(chunks));
        var turns = AiSseTurnRecorder.FoldTurns(chunks);
        Assert.Equal(2, turns.Count);
        Assert.Contains("Plan steps completed: 1 of 1", turns[0].Content);
        Assert.Contains("executionOutcome", turns[0].ToolCallsJson);
        Assert.DoesNotContain(chunks,
            c => c.Contains("\"type\":\"text\"") && c.Contains("Plan steps completed"));
        Assert.Equal("", turns[1].Content);
        using var review = JsonDocument.Parse(turns[1].ToolCallsJson!);
        Assert.NotEqual(proposalId, review.RootElement.GetProperty("plan").GetProperty("proposalId").GetString());
        Assert.Equal(1, python.Calls);
        Assert.Equal(0, fixture.Invoker.Calls);
    }

    [Fact]
    public async Task CompletedReview_DoesNotAutoContinueFromTheWordingOfItsFinalAnswer()
    {
        const string answer = "The table above is complete. I will check the catalog";
        await using var fixture = await CreateFixtureAsync(Text(answer));
        var chunks = await Collect(fixture.Chat.StreamChatAsync(
            [UserMessage("Review the actual complete output")], fixture.StoreId, fixture.UserId,
            locale: "en", planResultReview: true));
        Assert.Single(fixture.Client.Requests);
        Assert.Equal(answer, AiSseTurnRecorder.Fold(chunks).Content);
        Assert.Single(chunks, c => c.Contains("\"type\":\"text\"", StringComparison.Ordinal));
    }

    private static IReadOnlyList<AiStreamEvent> PythonPlan(string code, string id) => ToolCall("propose_plan",
        JsonSerializer.Serialize(new { summary = "Calculate matching products", steps = new[] {
            new { tool = "run_python", description = "Compute matching product names and counts", args = new { code } },
        } }), id);

    private static async Task<(string ProposalId, List<PlanStepInput> Steps)> PrepareConfirmedPlanAsync(ChatFixture fixture, string sessionId)
    {
        var user = UserMessage("Find products matching the requested rule");
        var initial = await Collect(fixture.Chat.StreamChatAsync([user], fixture.StoreId, fixture.UserId, sessionId: sessionId, locale: "en"));
        var snapshot = AiSseTurnRecorder.Fold(initial);
        Assert.DoesNotContain("FABRICATED", snapshot.Content);
        using var document = JsonDocument.Parse(snapshot.ToolCallsJson!);
        var proposalId = document.RootElement.GetProperty("plan").GetProperty("proposalId").GetString()!;
        await AiConversationTurnStore.AppendUserIfNeededAsync(fixture.Db, sessionId, fixture.StoreId, fixture.UserId, user, default);
        await AiConversationTurnStore.UpsertAssistantAsync(fixture.Db, sessionId, fixture.StoreId, fixture.UserId, snapshot, default);
        var steps = await fixture.Chat.ConsumeConfirmedPlanAsync(sessionId, proposalId, fixture.StoreId, fixture.UserId, default);
        return (proposalId, steps!.ToList());
    }

    private sealed class ScriptedPythonHttp(object payload) : HttpMessageHandler, IHttpClientFactory
    {
        public int Calls { get; private set; }
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            });
        }
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
        CreateFixtureCoreAsync(new EmptyMcpGateway(), null, null, null, responses);

    private static async Task<ChatFixture> CreateFixtureAsync(
        EmptyMcpGateway mcp,
        params IReadOnlyList<AiStreamEvent>[] responses)
        => await CreateFixtureCoreAsync(mcp, null, null, null, responses);

    private static Task<ChatFixture> CreateFixtureWithAttachmentAsync(
        string sessionId,
        string fileName,
        byte[] attachmentBytes,
        params IReadOnlyList<AiStreamEvent>[] responses) =>
        CreateFixtureCoreAsync(new EmptyMcpGateway(), sessionId, fileName, attachmentBytes, responses);

    private static async Task<ChatFixture> CreateFixtureCoreAsync(
        EmptyMcpGateway mcp,
        string? attachmentSessionId,
        string? attachmentFileName,
        byte[]? attachmentBytes,
        IReadOnlyList<AiStreamEvent>[] responses, IHttpClientFactory? pythonHttp = null,
        CabinetCapabilityIndex? capabilities = null)
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser("agent-eval");
        var store = data.AddStore("Eval store", "RUB", "UTC", user);
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
        IStorageService storage = attachmentBytes == null ? new FakeStorage() : new AttachmentStorage(attachmentBytes);
        var python = new PythonSandboxClient(
            db, pythonHttp ?? new ThrowingHttpFactory(), NullLogger<PythonSandboxClient>.Instance,
            new HttpContextAccessor(), new FakeJwt());
        var toolHost = new AgentToolHost(
            new CatalogSearchService(db),
            new SqlReadonlyService(db),
            new KnowledgeIndexService(db, storage, NullLogger<KnowledgeIndexService>.Instance),
            new AgentPlatformGateway(uploads, invoker, capabilities),
            python,
            new GrantAllPermissions(),
            db,
            NullLogger<AgentToolHost>.Instance,
            mcp,
            actions: capabilities != null ? new CabinetActionCatalog(index: capabilities) : null,
            chatAttachments: attachmentBytes == null ? null : new ChatAttachmentReadService(db, storage));
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
