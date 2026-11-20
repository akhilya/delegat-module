using System.Text.Json;
using Delegat.Enums;
using Delegat.Services.Ai;
using Delegat.Services.Ai.Agent;
using Microsoft.Extensions.AI;

namespace Delegat.Tests;

/// <summary>
/// Signed `$step:N.id` references in confirmed native plans: the deterministic
/// executor resolves earlier POST ids inside the existing per-step guard, a
/// malformed graph refuses every step before the first call, a resumed prefix
/// reuses the saved id without replaying the create, and the bounded review
/// surface admits built-in reads plus propose_plan on both runtimes while
/// mutations, Python, and connected MCP stay refused.
/// </summary>
public partial class MafEngineParityTests
{
    private static readonly string[] ExpectedReviewToolNames =
    [
        "search_actions", "search_knowledge", "get_doc", "search_catalog",
        "sql_readonly", "memory_search", "platform_request", "propose_plan",
    ];

    private static void AssertReviewToolNames(IEnumerable<string> toolNames) =>
        Assert.Equal(
            ExpectedReviewToolNames.OrderBy(n => n, StringComparer.Ordinal),
            toolNames.OrderBy(n => n, StringComparer.Ordinal));

    private static PlanStepInput PlanStep(string tool, string argsJson, string description) =>
        new()
        {
            Tool = tool,
            Description = description,
            Args = JsonDocument.Parse(argsJson).RootElement.Clone(),
        };

    private static List<PlanStepInput> DependentCreateSteps() =>
    [
        PlanStep("platform_request",
            """{"method":"POST","path":"/categories","body":{"name":"Example"}}""",
            "Create the category"),
        PlanStep("platform_request",
            """{"method":"POST","path":"/categories/$step:0.id/attributes","body":{"name":"Label","key":"label","type":0}}""",
            "Create the attribute"),
        PlanStep("platform_request",
            """{"method":"POST","path":"/products","body":{"name":"One","sku":"ONE","categoryId":"$step:0.id","attributeValues":[{"attributeId":"$step:1.id","value":"sample"}]}}""",
            "Create the product"),
    ];

    /// <summary>
    /// One category, an attribute definition per reflected <see cref="AttributeType"/>,
    /// then five products wired to the new ids. Fake ids count up from 101.
    /// </summary>
    private static List<PlanStepInput> CategoryFamilySteps()
    {
        var steps = new List<PlanStepInput>
        {
            PlanStep("platform_request",
                """{"method":"POST","path":"/categories","body":{"name":"Example"}}""",
                "Create the category"),
        };
        foreach (var type in Enum.GetValues<AttributeType>())
        {
            var kind = (int)type;
            steps.Add(PlanStep("platform_request",
                JsonSerializer.Serialize(new
                {
                    method = "POST",
                    path = "/categories/$step:0.id/attributes",
                    body = new { name = type.ToString(), key = $"attr-{kind}", type = kind },
                }),
                $"Create the {type} attribute"));
        }
        for (var p = 0; p < 5; p++)
        {
            // Six non-File definitions only; an empty optional File attribute is
            // not assigned, and no optional barcode is requested.
            steps.Add(PlanStep("platform_request",
                JsonSerializer.Serialize(new
                {
                    method = "POST",
                    path = "/products",
                    body = new
                    {
                        name = $"Product {p}",
                        sku = $"SKU-{p}",
                        categoryId = "$step:0.id",
                        attributeValues = Enumerable.Range(1, 6)
                            .Select(a => new { attributeId = $"$step:{a}.id", value = $"sample-{a}" })
                            .ToArray(),
                    },
                }),
                $"Create product {p}"));
        }
        return steps;
    }

    [Fact]
    public async Task ExecutePlan_DependentCreates_ResolveSignedReferences()
    {
        var nextId = 100;
        var invoker = new RecordingInvoker((method, path, body) =>
            (new { id = ++nextId }, $"created {nextId}"));
        await using var fixture = await CreateFixtureCoreAsync(
            new EmptyMcpGateway(), null, null, null, [], requestInvoker: invoker);

        var events = await ProviderCollectAsync(fixture.Chat.ExecutePlanEventsAsync(
            CategoryFamilySteps(), fixture.StoreId, fixture.UserId,
            "dependent-creates", locale: "en"));

        Assert.Equal(13, invoker.Calls);
        Assert.Equal("/api/categories", invoker.Requests[0].Path);
        for (var i = 0; i < 7; i++)
        {
            var request = invoker.Requests[1 + i];
            Assert.Equal("/api/categories/101/attributes", request.Path);
            Assert.Equal(i, request.Body!.Value.GetProperty("type").GetInt32());
        }
        for (var p = 0; p < 5; p++)
        {
            var request = invoker.Requests[8 + p];
            Assert.Equal("/api/products", request.Path);
            var productBody = request.Body!.Value;
            Assert.Equal(101, productBody.GetProperty("categoryId").GetInt32());
            Assert.False(productBody.TryGetProperty("barcode", out _));
            var values = productBody.GetProperty("attributeValues").EnumerateArray().ToArray();
            Assert.Equal(6, values.Length);
            for (var a = 0; a < 6; a++)
                Assert.Equal(102 + a, values[a].GetProperty("attributeId").GetInt32());
        }
        var done = events.Where(e => e.Type == SseEventType.Tool
            && e.Data.GetProperty("status").GetString() == "done").ToList();
        Assert.Equal(13, done.Count);
        var productIds = new HashSet<int>();
        foreach (var item in done.Skip(8))
            productIds.Add(JsonDocument.Parse(item.Data.GetProperty("result").GetString()!)
                .RootElement.GetProperty("id").GetInt32());
        Assert.Equal(5, productIds.Count);
        // The SSE input stays the signed template; the resolved request shows
        // separately so the checkpoint keeps matching the approved plan.
        Assert.Contains("$step:0.id",
            done[1].Data.GetProperty("input").GetString(), StringComparison.Ordinal);
        Assert.Contains("/categories/101/attributes",
            done[1].Data.GetProperty("resolvedInput").GetString(),
            StringComparison.Ordinal);
        var status = Assert.Single(events, e => e.Type == SseEventType.ExecutionStatus);
        Assert.Equal("completed", status.Data.GetProperty("status").GetString());
        Assert.Equal(13, status.Data.GetProperty("succeeded").GetInt32());
        Assert.Empty(fixture.Client.Requests);
    }

    [Fact]
    public async Task ExecutePlan_FailedCreate_StopsBeforeDependentProduct()
    {
        var invoker = new RecordingInvoker((method, path, body) =>
            path.Contains("/attributes", StringComparison.Ordinal)
                ? ((object)new { error = "attribute rejected" }, "Error: attribute rejected")
                : ((object)new { id = 55 }, "created"));
        await using var fixture = await CreateFixtureCoreAsync(
            new EmptyMcpGateway(), null, null, null, [], requestInvoker: invoker);

        var events = await ProviderCollectAsync(fixture.Chat.ExecutePlanEventsAsync(
            DependentCreateSteps(), fixture.StoreId, fixture.UserId,
            "dependent-failure", locale: "en"));

        Assert.Equal(2, invoker.Calls);
        Assert.Equal("/api/categories/55/attributes", invoker.Requests[1].Path);
        var status = Assert.Single(events, e => e.Type == SseEventType.ExecutionStatus);
        Assert.Equal("partial", status.Data.GetProperty("status").GetString());
        Assert.Equal(1, status.Data.GetProperty("succeeded").GetInt32());
        Assert.Equal(1, status.Data.GetProperty("skipped").GetInt32());
        Assert.True(status.Data.GetProperty("outcomeUnknown").GetBoolean());
        Assert.Empty(fixture.Client.Requests);
    }

    [Fact]
    public async Task ExecutePlan_MalformedReference_RefusesBeforeAnyCall()
    {
        var invoker = new RecordingInvoker();
        await using var fixture = await CreateFixtureCoreAsync(
            new EmptyMcpGateway(), null, null, null, [], requestInvoker: invoker);
        var steps = new List<PlanStepInput>
        {
            PlanStep("platform_request",
                """{"method":"POST","path":"/categories","body":{"name":"Example"}}""",
                "Create the category"),
            PlanStep("platform_request",
                """{"method":"POST","path":"/categories/$step:0.title/attributes","body":{"name":"Label"}}""",
                "Create the attribute"),
        };

        var events = await ProviderCollectAsync(fixture.Chat.ExecutePlanEventsAsync(
            steps, fixture.StoreId, fixture.UserId, "malformed-graph", locale: "en"));

        Assert.Equal(0, invoker.Calls);
        Assert.DoesNotContain(events, e => e.Type == SseEventType.Tool);
        var status = Assert.Single(events, e => e.Type == SseEventType.ExecutionStatus);
        Assert.Equal("failed", status.Data.GetProperty("status").GetString());
        Assert.Equal(0, status.Data.GetProperty("succeeded").GetInt32());
        Assert.Equal(2, status.Data.GetProperty("skipped").GetInt32());
        Assert.True(status.Data.GetProperty("outcomeUnknown").GetBoolean());
        Assert.Empty(fixture.Client.Requests);
    }

    [Fact]
    public async Task ExecutePlan_Resume_ReusesCheckpointIdWithoutReplayingCreate()
    {
        const string createArgs =
            """{"method":"POST","path":"/categories","body":{"name":"Example"}}""";
        var steps = new List<PlanStepInput>
        {
            PlanStep("platform_request", createArgs, "Create the category"),
            PlanStep("platform_request",
                """{"method":"POST","path":"/categories/$step:0.id/attributes","body":{"name":"Label","key":"label","type":0}}""",
                "Create the attribute"),
        };
        var outcome = JsonSerializer.SerializeToElement(new
        {
            step = 1,
            tool = "platform_request",
            description = "Create the category",
            status = "completed",
            summary = "created",
            errorClass = (string?)null,
            result = new { id = 42 },
            counts = new { },
        });
        var resume = new AiExecutionResume(
            [new AiExecutionStepCheckpoint(0, "platform_request", createArgs, "call-0", outcome)],
            false);
        var invoker = new RecordingInvoker((method, path, body) => (new { id = 77 }, "created"));
        await using var fixture = await CreateFixtureCoreAsync(
            new EmptyMcpGateway(), null, null, null, [], requestInvoker: invoker);

        var events = await ProviderCollectAsync(fixture.Chat.ExecutePlanEventsAsync(
            steps, fixture.StoreId, fixture.UserId, "resume-dependent", locale: "en",
            resume: resume));

        Assert.Equal(1, invoker.Calls);
        Assert.Equal("/api/categories/42/attributes", invoker.Requests[0].Path);
        var status = Assert.Single(events, e => e.Type == SseEventType.ExecutionStatus);
        Assert.Equal("completed", status.Data.GetProperty("status").GetString());
        Assert.Equal(2, status.Data.GetProperty("succeeded").GetInt32());
        Assert.Empty(fixture.Client.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("legacy")]
    public async Task ReviewMode_BuiltinReadRuns_AndRecoveryPlanPresents(string? runtime)
    {
        using var signingKey = SigningKey();
        var responses = new object[]
        {
            ToolCall("search_actions", "{\"query\":\"create attribute\"}", "discover-1"),
            ToolCall("propose_plan",
                "{\"summary\":\"Create the attribute\",\"steps\":[{\"description\":\"Create the attribute\",\"tool\":\"platform_request\",\"args\":{\"method\":\"POST\",\"path\":\"/categories/7/attributes\",\"body\":{\"name\":\"Label\",\"key\":\"label\",\"type\":0}}}]}",
                "recovery-plan"),
        };
        var legacy = new ScriptedAiStreamingClient(
            responses.Cast<ChatResponse>().Select(ToLegacyResponse).ToArray());
        await using var fixture = await CreateFixtureCoreAsync(
            new EmptyMcpGateway(), null, null, null, responses, legacy, runtime: runtime);

        var events = await ProviderCollectAsync(fixture.Chat.StreamChatEventsAsync(
            [UserMessage("Review the actual results")], fixture.StoreId, fixture.UserId,
            sessionId: "review-read-recovery", locale: "en", planResultReview: true));

        var requests = runtime == null ? fixture.Client.Requests : legacy.Requests;
        Assert.Equal(2, requests.Count);
        Assert.Single(events, e => e.Type == SseEventType.Tool
            && e.Data.GetProperty("name").GetString() == "search_actions"
            && e.Data.GetProperty("status").GetString() == "done");
        Assert.Single(events, e => e.Type == SseEventType.Plan);
        Assert.Equal(0, fixture.Invoker.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("legacy")]
    public async Task ReviewMode_ReadOnlyProposal_IsRejectedThenCorrected(string? runtime)
    {
        using var signingKey = SigningKey();
        var responses = new object[]
        {
            ToolCall("propose_plan",
                "{\"summary\":\"List attribute types\",\"steps\":[{\"description\":\"Look up attribute actions\",\"tool\":\"search_actions\",\"args\":{\"query\":\"attribute types\"}}]}",
                "read-plan"),
            ToolCall("propose_plan",
                "{\"summary\":\"Create the attribute\",\"steps\":[{\"description\":\"Create the attribute\",\"tool\":\"platform_request\",\"args\":{\"method\":\"POST\",\"path\":\"/categories/7/attributes\",\"body\":{\"name\":\"Label\",\"key\":\"label\",\"type\":0}}}]}",
                "fix-plan"),
        };
        var legacy = new ScriptedAiStreamingClient(
            responses.Cast<ChatResponse>().Select(ToLegacyResponse).ToArray());
        await using var fixture = await CreateFixtureCoreAsync(
            new EmptyMcpGateway(), null, null, null, responses, legacy, runtime: runtime);

        var events = await ProviderCollectAsync(fixture.Chat.StreamChatEventsAsync(
            [UserMessage("Review the actual results")], fixture.StoreId, fixture.UserId,
            sessionId: "review-readonly-plan", locale: "en", planResultReview: true));

        var requests = runtime == null ? fixture.Client.Requests : legacy.Requests;
        Assert.Equal(2, requests.Count);
        Assert.Contains("built-in read-only lookups",
            string.Concat(requests[1].Messages.Select(m => m.Content)),
            StringComparison.Ordinal);
        Assert.Single(events, e => e.Type == SseEventType.Plan);
        Assert.Equal(0, fixture.Invoker.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("legacy")]
    public async Task ReviewMode_MutationPythonMcp_AreAllRefused(string? runtime)
    {
        using var signingKey = SigningKey();
        var mcp = new EmptyMcpGateway(new AiTool
        {
            name = "mcp3_update_record",
            description = "Update a connected record",
            input_schema = new AiToolInputSchema { type = "object" },
        });
        var responses = new object[]
        {
            ToolBatch(
                ("platform_request", "{\"method\":\"POST\",\"path\":\"/products\",\"body\":{\"name\":\"X\"}}", "write-1"),
                ("run_python", "{\"code\":\"print(1)\"}", "python-1"),
                ("mcp3_update_record", "{\"id\":1}", "mcp-1")),
            Text("Execution already finished; the results are summarized above."),
        };
        var legacy = new ScriptedAiStreamingClient(
            responses.Cast<ChatResponse>().Select(ToLegacyResponse).ToArray());
        await using var fixture = await CreateFixtureCoreAsync(
            mcp, null, null, null, responses, legacy, runtime: runtime);

        var events = await ProviderCollectAsync(fixture.Chat.StreamChatEventsAsync(
            [UserMessage("Review the actual results")], fixture.StoreId, fixture.UserId,
            sessionId: "review-blocked-calls", locale: "en", planResultReview: true));

        var requests = runtime == null ? fixture.Client.Requests : legacy.Requests;
        Assert.Equal(2, requests.Count);
        var history = string.Concat(requests[1].Messages.Select(m => m.Content));
        Assert.Equal(3,
            history.Split("Not run. Execution is already finished").Length - 1);
        Assert.Equal(0, fixture.Invoker.Calls);
        Assert.Equal(0, mcp.Calls);
        Assert.DoesNotContain(events, e => e.Type == SseEventType.Plan);
    }
}
