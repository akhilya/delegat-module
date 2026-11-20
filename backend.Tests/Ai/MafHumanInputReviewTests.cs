using System.Text.Json;
using Delegat.Services.Ai;
using Delegat.Services.Ai.Agent;
using Microsoft.Extensions.AI;
using ChatMessage = Delegat.Services.Ai.ChatMessage;
using FrameworkChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Delegat.Tests;

/// <summary>
/// Human-input and review-completion policy, asserted identically on the default MAF
/// runtime and the explicit legacy rollback: a presented ask_form is a hard turn
/// terminal (batch siblings defer, no follow-up model call, no nudge, no fallback),
/// every bounded review round advertises propose_plan, and a review that exhausts its
/// attempts without a valid plan or reply ends as review_incomplete with an
/// interrupted done - never the successful generic silent-turn text.
/// </summary>
public partial class MafEngineParityTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("legacy")]
    public async Task AskForm_ValidForm_PausesTheTurnWaitingForAnswers(string? runtime)
    {
        var response = new ChatResponse(new FrameworkChatMessage(ChatRole.Assistant,
            new List<AIContent>
            {
                new TextContent("Fill this in, then I will create the product."),
                new FunctionCallContent("form-1", "ask_form",
                    ParseArgs("""{"kind":"fields","title":"New product","followUp":"Create {name}","fields":[{"key":"name","label":"Name","type":"text","placeholder":"Example: Ork and Co mug"}]}""")),
            }))
        { FinishReason = ChatFinishReason.ToolCalls };

        var legacy = new ScriptedAiStreamingClient(ToLegacyResponse(response));
        await using var fixture = await CreateFixtureCoreAsync(
            new EmptyMcpGateway(), null, null, null, [response], legacy, runtime: runtime);

        var events = await ProviderCollectAsync(fixture.Chat.StreamChatEventsAsync(
            [UserMessage("Ask me what you need to create a product")],
            fixture.StoreId, fixture.UserId,
            sessionId: "ask-form-pause", locale: "en"));

        var requests = runtime == null ? fixture.Client.Requests : legacy.Requests;
        Assert.Single(requests);
        var formDone = Assert.Single(events, e => e.Type == SseEventType.Tool
            && e.Data.GetProperty("status").GetString() == "done");
        Assert.Equal("ask_form", formDone.Data.GetProperty("name").GetString());
        Assert.DoesNotContain(events, e => e.Type == SseEventType.Plan);
        Assert.DoesNotContain(events, e => e.Type == SseEventType.Error);
        Assert.Single(events, e => e.Type == SseEventType.Done);
        var folded = AiSseTurnRecorder.Fold(events.Select(e => e.ToSse()).ToList());
        Assert.DoesNotContain("stopped", folded.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fixture.Invoker.Calls);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(null, false)]
    [InlineData("legacy", true)]
    [InlineData("legacy", false)]
    public async Task AskForm_InPlanBatch_FormWinsAndNothingElseRuns(
        string? runtime, bool planFirst)
    {
        var plan = (AIContent)new FunctionCallContent("plan-1", "propose_plan",
            ParseArgs("""{"summary":"Create Mug","steps":[{"description":"Create Mug","tool":"platform_request","args":{"method":"POST","path":"/products","body":{"name":"Mug"}}}]}"""));
        var form = (AIContent)new FunctionCallContent("form-1", "ask_form",
            ParseArgs("""{"kind":"fields","title":"Missing facts","followUp":"Create {name}","fields":[{"key":"name","label":"Name","type":"text"}]}"""));
        var read = (AIContent)new FunctionCallContent("read-1", "platform_request",
            ParseArgs("""{"method":"GET","path":"/products"}"""));
        var ordered = planFirst ? new[] { plan, form, read } : new[] { form, plan, read };
        var response = new ChatResponse(new FrameworkChatMessage(ChatRole.Assistant,
            ordered.ToList()))
        { FinishReason = ChatFinishReason.ToolCalls };

        var legacy = new ScriptedAiStreamingClient(ToLegacyResponse(response));
        await using var fixture = await CreateFixtureCoreAsync(
            new EmptyMcpGateway(), null, null, null, [response], legacy, runtime: runtime);

        var events = await ProviderCollectAsync(fixture.Chat.StreamChatEventsAsync(
            [UserMessage("Create a mug")], fixture.StoreId, fixture.UserId,
            sessionId: $"ask-form-batch-{planFirst}", locale: "en"));

        var requests = runtime == null ? fixture.Client.Requests : legacy.Requests;
        Assert.Single(requests);
        Assert.Contains(events, e => e.Type == SseEventType.Tool
            && e.Data.GetProperty("name").GetString() == "ask_form"
            && e.Data.GetProperty("status").GetString() == "done");
        Assert.DoesNotContain(events, e => e.Type == SseEventType.Plan);
        Assert.DoesNotContain(events, e => e.Type == SseEventType.Error);
        Assert.Single(events, e => e.Type == SseEventType.Done);
        Assert.Equal(0, fixture.Invoker.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("legacy")]
    public async Task AskForm_InvalidForm_DoesNotPause_AndRepairSucceeds(string? runtime)
    {
        var invalid = ToolCall("ask_form",
            """{"kind":"fields","followUp":"Ship to {barcode}","fields":[{"key":"name","label":"Name","type":"text"}]}""",
            "bad-form");
        var valid = ToolCall("ask_form",
            """{"kind":"fields","followUp":"Create {name}","fields":[{"key":"name","label":"Name","type":"text"}]}""",
            "good-form");

        var legacy = new ScriptedAiStreamingClient(
            ToLegacyResponse(invalid), ToLegacyResponse(valid));
        await using var fixture = await CreateFixtureCoreAsync(
            new EmptyMcpGateway(), null, null, null, [invalid, valid], legacy, runtime: runtime);

        var events = await ProviderCollectAsync(fixture.Chat.StreamChatEventsAsync(
            [UserMessage("Ask me for the product details")],
            fixture.StoreId, fixture.UserId,
            sessionId: "ask-form-repair", locale: "en"));

        var requests = runtime == null ? fixture.Client.Requests : legacy.Requests;
        Assert.Equal(2, requests.Count);
        // The appended tool_result message is the last request item for both
        // runtimes (tool role under MAF, user role under the legacy loop).
        var secondHistory = requests[1].Messages.Last().Content;
        Assert.Contains("unknown_follow_up_fields", secondHistory, StringComparison.Ordinal);
        Assert.Contains("barcode", secondHistory, StringComparison.Ordinal);
        var doneForms = events.Where(e => e.Type == SseEventType.Tool
            && e.Data.GetProperty("name").GetString() == "ask_form"
            && e.Data.GetProperty("status").GetString() == "done").ToList();
        Assert.Equal(2, doneForms.Count);
        Assert.Contains(doneForms, e =>
            e.Data.GetProperty("result").GetString()!.Contains("\"form\":true", StringComparison.Ordinal));
        Assert.DoesNotContain(events, e => e.Type == SseEventType.Plan);
        Assert.DoesNotContain(events, e => e.Type == SseEventType.Error);
        var folded = AiSseTurnRecorder.Fold(events.Select(e => e.ToSse()).ToList());
        Assert.DoesNotContain("stopped", folded.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fixture.Invoker.Calls);
    }

    [Fact]
    public async Task ReviewMode_ThirdRequest_RetainsValidRecoveryProposal_LegacyRuntime()
    {
        using var signingKey = SigningKey();
        var responses = new object[]
        {
            ToolCall("propose_plan", "{\"summary\":\"\",\"steps\":[]}", "bad-plan-1"),
            ToolCall("propose_plan", "{\"summary\":\"\",\"steps\":[]}", "bad-plan-2"),
            ToolCall("propose_plan",
                "{\"summary\":\"Retry the write\",\"steps\":[{\"description\":\"Retry\",\"tool\":\"platform_request\",\"args\":{\"method\":\"POST\",\"path\":\"/products\",\"body\":{\"name\":\"Retry\"}}}]}",
                "late-plan"),
        };
        var legacy = new ScriptedAiStreamingClient(
            responses.Cast<ChatResponse>().Select(ToLegacyResponse).ToArray());
        await using var fixture = await CreateFixtureCoreAsync(
            new EmptyMcpGateway(), null, null, null, [], legacy, runtime: "legacy");

        var events = await ProviderCollectAsync(fixture.Chat.StreamChatEventsAsync(
            [UserMessage("Review the actual results")],
            fixture.StoreId, fixture.UserId,
            sessionId: "review-third-legacy", locale: "en", planResultReview: true));

        Assert.Equal(3, legacy.Requests.Count);
        Assert.All(legacy.Requests, r => AssertReviewToolNames(r.ToolNames));
        var wire = string.Concat(events.Select(e => e.ToSse()));
        Assert.Contains("\"type\":\"plan\"", wire, StringComparison.Ordinal);
        Assert.Contains("proposalNonce", wire, StringComparison.Ordinal);
        Assert.Contains("Retry the write", wire, StringComparison.Ordinal);
        Assert.DoesNotContain(events, e => e.Type == SseEventType.Error);
        Assert.Equal(0, fixture.Invoker.Calls);
        Assert.Equal(0, fixture.Factory.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("legacy")]
    public async Task ReviewMode_AllInvalidProposals_EndsAsReviewIncomplete(string? runtime)
    {
        var responses = Enumerable.Range(1, AgentChatTurnGate.ReviewMaxRequests)
            .Select(i => (object)ToolCall("propose_plan",
                "{\"summary\":\"\",\"steps\":[]}", $"bad-plan-{i}"))
            .ToArray();
        var legacy = new ScriptedAiStreamingClient(
            responses.Cast<ChatResponse>().Select(ToLegacyResponse).ToArray());
        await using var fixture = await CreateFixtureCoreAsync(
            new EmptyMcpGateway(), null, null, null, responses, legacy, runtime: runtime);

        var events = await ProviderCollectAsync(fixture.Chat.StreamChatEventsAsync(
            [UserMessage("Review the actual results")],
            fixture.StoreId, fixture.UserId,
            sessionId: "review-incomplete", locale: "en", planResultReview: true));

        var requests = runtime == null ? fixture.Client.Requests : legacy.Requests;
        Assert.Equal(AgentChatTurnGate.ReviewMaxRequests, requests.Count);
        Assert.All(requests, r => AssertReviewToolNames(r.ToolNames));
        Assert.DoesNotContain(events, e => e.Type == SseEventType.Plan);
        var error = Assert.Single(events, e => e.Type == SseEventType.Error);
        Assert.Equal("review_incomplete", error.Data.GetProperty("code").GetString());
        var done = Assert.Single(events, e => e.Type == SseEventType.Done);
        Assert.True(done.Data.GetProperty("interrupted").GetBoolean());
        Assert.DoesNotContain(events, e => e.Type == SseEventType.Text
            && e.Data.GetProperty("delta").GetString()!.Contains("stopped",
                StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, fixture.Invoker.Calls);

        var canonical = events.Select((item, index) => item with
        {
            RunId = "review-run",
            MessageId = "review-message",
            DbId = 42,
            Phase = "review",
            Sequence = index + 1,
        });
        var snapshot = AiSseTurnRecorder.FoldEvents(canonical);
        using var envelope = JsonDocument.Parse(snapshot.ToolCallsJson!);
        Assert.Equal("review_incomplete", envelope.RootElement.GetProperty("requestError").GetProperty("code").GetString());
        Assert.Equal("interrupted", envelope.RootElement.GetProperty("messageState").GetString());
        Assert.Equal("review", envelope.RootElement.GetProperty("phase").GetString());
        Assert.False(envelope.RootElement.TryGetProperty("planExecuted", out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("legacy")]
    public async Task ReviewMode_FormCannotBlockRecoveryProposal(string? runtime)
    {
        using var signingKey = SigningKey();
        var response = ToolBatch(
            ("ask_form", "{\"kind\":\"fields\",\"fields\":[{\"key\":\"name\",\"label\":\"Name\",\"type\":\"text\"}]}", "off-script-form"),
            ("propose_plan", "{\"summary\":\"Prepare a continuation\",\"steps\":[{\"description\":\"Create Mug\",\"tool\":\"platform_request\",\"args\":{\"method\":\"POST\",\"path\":\"/products\",\"body\":{\"name\":\"Mug\"}}}]}", "recovery"));
        var legacy = new ScriptedAiStreamingClient(ToLegacyResponse(response));
        await using var fixture = await CreateFixtureCoreAsync(
            new EmptyMcpGateway(), null, null, null, [response], legacy, runtime: runtime);
        var events = await ProviderCollectAsync(fixture.Chat.StreamChatEventsAsync(
            [UserMessage("Review the actual results")], fixture.StoreId, fixture.UserId,
            sessionId: "review-form-deferral", locale: "en", planResultReview: true));
        var requests = runtime == null ? fixture.Client.Requests : legacy.Requests;
        Assert.Single(requests);
        AssertReviewToolNames(requests[0].ToolNames);
        Assert.Single(events, item => item.Type == SseEventType.Plan);
        Assert.DoesNotContain(events, item => item.Type == SseEventType.Tool || item.Type == SseEventType.Error);
        Assert.Equal(0, fixture.Invoker.Calls);
    }

    /// <summary>Legacy transport equivalent of one scripted MAF response: text deltas
    /// and content_block triplets so both runtimes replay the identical model round.</summary>
    private static IReadOnlyList<AiStreamEvent> ToLegacyResponse(ChatResponse response)
    {
        var events = new List<AiStreamEvent>();
        foreach (var message in response.Messages)
        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case TextContent text:
                    events.Add(new AiStreamEvent
                    {
                        type = "content_block_delta",
                        delta = new AiStreamDelta { type = "text_delta", text = text.Text },
                    });
                    break;
                case FunctionCallContent call:
                    events.Add(new AiStreamEvent
                    {
                        type = "content_block_start",
                        content_block = new AiContentBlock
                        {
                            type = "tool_use",
                            id = call.CallId,
                            name = call.Name,
                        },
                    });
                    events.Add(new AiStreamEvent
                    {
                        type = "content_block_delta",
                        delta = new AiStreamDelta
                        {
                            type = "input_json_delta",
                            partial_json = JsonSerializer.Serialize(call.Arguments),
                        },
                    });
                    events.Add(new AiStreamEvent { type = "content_block_stop" });
                    break;
            }
        }
        events.Add(new AiStreamEvent { type = "message_stop" });
        return events;
    }
}
