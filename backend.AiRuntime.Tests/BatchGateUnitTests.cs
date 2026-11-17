using Delegat.AiRuntime.Tests.Gating;
using Delegat.AiRuntime.Tests.Support;
using Microsoft.Extensions.AI;
using Xunit.Abstractions;

namespace Delegat.AiRuntime.Tests;

/// <summary>
/// Gate-level proofs independent of provider SDKs: a scripted IChatClient feeds canned
/// ChatResponses through both gate entry points (GetResponseAsync and
/// GetStreamingResponseAsync), and FICC's next-round messages are inspected directly —
/// the deferred-sibling history requirement for M3 is recorded here.
/// </summary>
public class BatchGateUnitTests
{
    private readonly ITestOutputHelper _output;

    public BatchGateUnitTests(ITestOutputHelper output) => _output = output;

    private static ChatResponse BatchResponse()
    {
        var message = new ChatMessage(ChatRole.Assistant,
        [
            new TextContent("Thinking "),
            new FunctionCallContent("call_plan", "propose_plan",
                new Dictionary<string, object?> { ["title"] = "bulk-rename" }),
            new FunctionCallContent("call_sib", "sibling_tool",
                new Dictionary<string, object?> { ["note"] = "s1" }),
        ])
        { MessageId = "m-r1" };

        return new ChatResponse(message)
        {
            ResponseId = "resp-r1",
            ModelId = "scripted",
            FinishReason = ChatFinishReason.ToolCalls,
            Usage = new UsageDetails { InputTokenCount = 3, OutputTokenCount = 4, TotalTokenCount = 7 },
        };
    }

    private static ChatResponse TextResponse() =>
        new(new ChatMessage(ChatRole.Assistant, "Plan acknowledged."))
        {
            ResponseId = "resp-r2",
            ModelId = "scripted",
            FinishReason = ChatFinishReason.Stop,
            Usage = new UsageDetails { InputTokenCount = 5, OutputTokenCount = 2, TotalTokenCount = 7 },
        };

    [Fact]
    public async Task NonStreamingPath_GateFiltersBatch_OnlyPlanInvoked()
    {
        var inner = new ScriptedChatClient();
        inner.Enqueue(BatchResponse());
        inner.Enqueue(TextResponse());
        var harness = TestAgentHarness.ForClient(inner);

        // Drive the non-streaming agent run — exercises the gate's GetResponseAsync path.
        var response = await harness.Agent.RunAsync("make a plan");

        var inspection = harness.Gate.Inspections[0];
        Assert.Equal(new[] { "call_plan" }, inspection.AllowedCallIds);
        Assert.Equal(new[] { "call_sib" }, inspection.DeferredCallIds);
        Assert.Equal(1, harness.Tools.ProposePlanInvocations);
        Assert.Equal(0, harness.Tools.SiblingInvocations);
        Assert.Equal(2, inner.Requests.Count);
        Assert.Contains("Plan acknowledged.", response.Text);
    }

    [Fact]
    public async Task NextRoundMessages_OnlyDispatchedCallsHaveResults_DeferredCallsAbsent()
    {
        var inner = new ScriptedChatClient();
        inner.Enqueue(BatchResponse());
        inner.Enqueue(TextResponse());
        var harness = TestAgentHarness.ForClient(inner);

        await harness.Agent.RunAsync("make a plan");

        // The second-round message list FICC produced contains the assistant message with
        // ONLY the allowed call and a tool-role FunctionResultContent for it. The deferred
        // call never appears — consistent wire history, but also proof that executing a
        // deferred call later requires the host to append a synthetic result (M3 item).
        Assert.Equal(2, inner.Requests.Count);
        var round2 = inner.Requests[1];

        var assistantCalls = round2
            .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
            .ToList();
        Assert.Single(assistantCalls);
        Assert.Equal("call_plan", assistantCalls[0].CallId);

        var results = round2
            .SelectMany(m => m.Contents.OfType<FunctionResultContent>())
            .ToList();
        var result = Assert.Single(results);
        Assert.Equal("call_plan", result.CallId);
        _output.WriteLine("round-2 result content: " + result.Result);

        // No message carries call_sib anywhere in the follow-up request.
        Assert.DoesNotContain(round2.SelectMany(m => m.Contents).OfType<AIContent>(),
            c => c is FunctionCallContent { CallId: "call_sib" } ||
                 c is FunctionResultContent { CallId: "call_sib" });
    }

    [Fact]
    public async Task Usage_PreservedAcrossGateFiltering_StreamingAndNonStreaming()
    {
        var inner = new ScriptedChatClient();
        inner.Enqueue(BatchResponse());
        inner.Enqueue(TextResponse());
        var harness = TestAgentHarness.ForClient(inner);

        var updates = await TestAgentHarness.CollectAsync(harness.Agent, "make a plan");

        var usages = updates.SelectMany(u => u.Contents).OfType<UsageContent>().ToList();
        _output.WriteLine("gate-forwarded usage entries: " + usages.Count);
        Assert.Contains(usages, u => u.Details.TotalTokenCount == 7);
    }

    [Fact]
    public async Task UnknownTool_ScriptedCall_FiccSynthesizesErrorResult_NextRoundContinues()
    {
        var inner = new ScriptedChatClient();
        inner.Enqueue(new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call_unk", "nonexistent_tool",
                new Dictionary<string, object?> { ["q"] = 1 })]))
        { FinishReason = ChatFinishReason.ToolCalls });
        inner.Enqueue(TextResponse());
        var harness = TestAgentHarness.ForClient(inner);

        var response = await harness.Agent.RunAsync("call something");

        Assert.Equal(2, inner.Requests.Count);
        var results = inner.Requests[1].SelectMany(m => m.Contents.OfType<FunctionResultContent>()).ToList();
        var result = Assert.Single(results);
        Assert.Equal("call_unk", result.CallId);
        _output.WriteLine($"FICC unknown-tool result: result={result.Result} exception={result.Exception}");
        Assert.Equal(0, harness.Tools.ProposePlanInvocations + harness.Tools.SiblingInvocations);
        Assert.Contains("Plan acknowledged.", response.Text);
    }
}
