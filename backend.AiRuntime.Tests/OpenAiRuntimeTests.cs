using System.Net;
using Delegat.AiRuntime.Tests.Support;
using Microsoft.Extensions.AI;
using Xunit.Abstractions;

namespace Delegat.AiRuntime.Tests;

/// <summary>
/// M1 prototype proofs over the OpenAI SDK path (OpenAI 2.15.0 → M.E.AI.OpenAI adapter →
/// BatchGateChatClient → FunctionInvokingChatClient → ChatClientAgent, all over a scripted
/// HttpMessageHandler — no live calls).
/// </summary>
public class OpenAiRuntimeTests
{
    private readonly ITestOutputHelper _output;

    public OpenAiRuntimeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task BatchGate_InspectsWholeBatch_BeforeAnyInvocation_OnlyPlanDispatched()
    {
        var http = new ScriptedHttpMessageHandler();
        http.EnqueueSse(OpenAiSse.ToolCallBatchResponse);
        http.EnqueueSse(OpenAiSse.TextResponse);
        var harness = TestAgentHarness.ForOpenAi(http);

        var updates = await TestAgentHarness.CollectAsync(harness.Agent, "make a plan");

        // The gate observed BOTH calls of the round as one batch before FICC dispatched any.
        Assert.Equal(2, harness.Gate.Inspections.Count); // round 1 (calls) + round 2 (none)
        var inspection = harness.Gate.Inspections[0];
        Assert.Equal(2, inspection.Calls.Count);
        Assert.Contains(inspection.Calls, c => c is { CallId: "call_plan", Name: "propose_plan" });
        Assert.Contains(inspection.Calls, c => c is { CallId: "call_sib", Name: "sibling_tool" });

        // Only propose_plan was forwarded/dispatched; the sibling was recorded deferred.
        Assert.Equal(new[] { "call_plan" }, inspection.AllowedCallIds);
        Assert.Equal(new[] { "call_sib" }, inspection.DeferredCallIds);
        Assert.Equal(1, harness.Tools.ProposePlanInvocations);
        Assert.Equal(0, harness.Tools.SiblingInvocations);
        Assert.Empty(inspection.UnknownToolNames);

        // FICC drove the second round on its own; the run terminated with final text.
        Assert.Equal(2, http.RequestCount);
        Assert.Contains(updates, u => u.Text?.Contains("Plan acknowledged.") == true);
    }

    [Fact]
    public async Task ToolCallArguments_FragmentedAcrossThreeDeltas_ArriveAsOneCompleteCall()
    {
        var http = new ScriptedHttpMessageHandler();
        http.EnqueueSse(OpenAiSse.ToolCallBatchResponse);
        http.EnqueueSse(OpenAiSse.TextResponse);
        var harness = TestAgentHarness.ForOpenAi(http);

        await TestAgentHarness.CollectAsync(harness.Agent, "make a plan");

        // plan args were sent as three deltas "{\"ti" + "tle\":\"bulk" + "-rename\"}"
        // interleaved with a text delta — the gate saw ONE complete FunctionCallContent.
        var plan = Assert.Single(harness.Gate.Inspections[0].Calls, c => c.Name == "propose_plan");
        using var args = System.Text.Json.JsonDocument.Parse(plan.ArgumentsJson);
        Assert.Equal("bulk-rename", args.RootElement.GetProperty("title").GetString());
        var sib = Assert.Single(harness.Gate.Inspections[0].Calls, c => c.Name == "sibling_tool");
        using var sibArgs = System.Text.Json.JsonDocument.Parse(sib.ArgumentsJson);
        Assert.Equal("s1", sibArgs.RootElement.GetProperty("note").GetString());
    }

    [Fact]
    public async Task StrippedSibling_KeepsWireHistoryConsistent_NoDanglingToolCall()
    {
        var http = new ScriptedHttpMessageHandler();
        http.EnqueueSse(OpenAiSse.ToolCallBatchResponse);
        http.EnqueueSse(OpenAiSse.TextResponse);
        var harness = TestAgentHarness.ForOpenAi(http);

        await TestAgentHarness.CollectAsync(harness.Agent, "make a plan");

        // Round-2 request must contain the plan call + its tool result, and must NOT
        // contain call_sib: stripping deferred calls avoids dangling tool_calls that
        // providers would reject (tool_use without result) or re-execute blindly.
        Assert.Equal(2, http.RequestCount);
        var round2 = http.RequestBodies[1];
        Assert.Contains("call_plan", round2);
        Assert.DoesNotContain("call_sib", round2);
        _output.WriteLine("round2 request body: " + round2);
    }

    [Fact]
    public async Task FiccDrivesAllRounds_TwoModelRequests_OneInvocation_FinalText()
    {
        var http = new ScriptedHttpMessageHandler();
        http.EnqueueSse(OpenAiSse.SingleToolCallResponse);
        http.EnqueueSse(OpenAiSse.TextResponse);
        var harness = TestAgentHarness.ForOpenAi(http);

        var updates = await TestAgentHarness.CollectAsync(harness.Agent, "call the plan tool");

        // Exactly one loop: two provider HTTP requests, tool invoked once, final text —
        // no manual round or second loop anywhere in test code.
        Assert.Equal(2, http.RequestCount);
        Assert.Equal(1, harness.Tools.ProposePlanInvocations);
        Assert.Contains(updates, u => u.Text?.Contains("Plan acknowledged.") == true);
    }

    [Fact]
    public async Task Usage_CannedNumbers_SurfaceThroughRun()
    {
        var http = new ScriptedHttpMessageHandler();
        http.EnqueueSse(OpenAiSse.SingleToolCallResponse);
        http.EnqueueSse(OpenAiSse.TextResponse);
        var harness = TestAgentHarness.ForOpenAi(http);

        var updates = await TestAgentHarness.CollectAsync(harness.Agent, "call the plan tool");

        var usages = updates
            .SelectMany(u => u.Contents)
            .OfType<UsageContent>()
            .Select(c => c.Details)
            .ToList();
        _output.WriteLine("usage details: " +
            string.Join(" | ", usages.Select(u => $"in={u.InputTokenCount} out={u.OutputTokenCount} total={u.TotalTokenCount}")));
        Assert.Contains(usages, u => u.TotalTokenCount == 9 || u.TotalTokenCount == 46);
    }

    [Fact]
    public async Task UnknownTool_FiccFeedsErrorResultBack_LoopContinues_Deterministically()
    {
        var http = new ScriptedHttpMessageHandler();
        http.EnqueueSse(OpenAiSse.UnknownToolResponse);
        http.EnqueueSse(OpenAiSse.TextResponse);
        var harness = TestAgentHarness.ForOpenAi(http);

        var updates = await TestAgentHarness.CollectAsync(harness.Agent, "call something");

        // FICC default (TerminateOnUnknownCalls=false): an error FunctionResultContent is
        // synthesized and sent back; the loop continues to the next model round and ends
        // normally — no exception, no invocation, deterministic.
        Assert.Equal(2, http.RequestCount);
        Assert.Equal(0, harness.Tools.ProposePlanInvocations);
        Assert.Equal(0, harness.Tools.SiblingInvocations);
        Assert.Contains("nonexistent_tool", harness.Gate.Inspections[0].UnknownToolNames);

        var round2 = http.RequestBodies[1];
        _output.WriteLine("unknown-tool round2 body: " + round2);
        Assert.Contains("call_unk", round2); // the synthesized tool result is on the wire
        Assert.Contains(updates, u => u.Text?.Contains("Plan acknowledged.") == true);
    }

    [Fact]
    public async Task Cancellation_SlowStream_ObservesCancellation_NoInvocation()
    {
        var http = new ScriptedHttpMessageHandler();
        http.EnqueueHanging(TimeSpan.FromSeconds(60));
        var harness = TestAgentHarness.ForOpenAi(http);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var ex = await Record.ExceptionAsync(
            () => TestAgentHarness.CollectAsync(harness.Agent, "hi", cts.Token));

        _output.WriteLine($"cancellation surfaced as: {ex?.GetType().FullName}: {ex?.Message}");
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
        Assert.Equal(0, harness.Tools.ProposePlanInvocations);
        Assert.Equal(0, harness.Tools.SiblingInvocations);
    }

    [Fact]
    public async Task Cancellation_MidStreamRead_ObservesCancellation_NoInvocation()
    {
        // Headers + one valid chunk arrive promptly, then the body stalls: cancellation
        // must interrupt the SDK inside the SSE read, not only before headers.
        var http = new ScriptedHttpMessageHandler();
        http.EnqueueHangingMidStream(OpenAiSse.PartialPrefix, TimeSpan.FromSeconds(60));
        var harness = TestAgentHarness.ForOpenAi(http);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var ex = await Record.ExceptionAsync(
            () => TestAgentHarness.CollectAsync(harness.Agent, "hi", cts.Token));

        _output.WriteLine($"mid-stream cancellation surfaced as: {ex?.GetType().FullName}: {ex?.Message}");
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
        Assert.Equal(1, http.RequestCount);
        Assert.Equal(0, harness.Tools.ProposePlanInvocations);
        Assert.Equal(0, harness.Tools.SiblingInvocations);
    }

    [Fact]
    public async Task MalformedStream_SurfacesDeterministicError_NoHang_NoHalfInvocation()
    {
        var http = new ScriptedHttpMessageHandler();
        http.EnqueueSse(OpenAiSse.MalformedResponse);
        var harness = TestAgentHarness.ForOpenAi(http);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ex = await Record.ExceptionAsync(
            () => TestAgentHarness.CollectAsync(harness.Agent, "hi", cts.Token));

        _output.WriteLine($"malformed stream surfaced as: {ex?.GetType().FullName}: {ex?.Message}");
        // Parse error, not a stall: JsonReaderException is internal, so assert its
        // public base JsonException and explicitly reject a cancellation outcome.
        Assert.IsAssignableFrom<System.Text.Json.JsonException>(ex);
        Assert.IsNotAssignableFrom<OperationCanceledException>(ex);
        Assert.Equal(0, harness.Tools.ProposePlanInvocations);
        Assert.Equal(0, harness.Tools.SiblingInvocations);
    }

    [Fact]
    public async Task NoToolCalls_RunEndsImmediately_ExactlyOneModelRequest()
    {
        var http = new ScriptedHttpMessageHandler();
        http.EnqueueSse(OpenAiSse.TextResponse);
        var harness = TestAgentHarness.ForOpenAi(http);

        var updates = await TestAgentHarness.CollectAsync(harness.Agent, "just talk");

        Assert.Equal(1, http.RequestCount); // no continuation nudge needed
        Assert.Equal(0, harness.Tools.ProposePlanInvocations);
        Assert.Contains(updates, u => u.Text?.Contains("Plan acknowledged.") == true);
    }

    [Fact]
    public async Task Http500_ThenSucceeds_RecordsSdkRetryBehavior()
    {
        var http = new ScriptedHttpMessageHandler();
        http.EnqueueStatus(HttpStatusCode.InternalServerError, OpenAiSse.ServerErrorBody);
        http.EnqueueSse(OpenAiSse.SingleToolCallResponse);
        http.EnqueueSse(OpenAiSse.TextResponse);
        var harness = TestAgentHarness.ForOpenAi(http);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ex = await Record.ExceptionAsync(
            () => TestAgentHarness.CollectAsync(harness.Agent, "hi", cts.Token));

        // Deterministic either way; recorded for the M3 retry policy decision.
        _output.WriteLine($"500-then-succeed: requests={http.RequestCount}, " +
                          $"exception={ex?.GetType().FullName}: {ex?.Message}");
        if (ex is null)
        {
            // The SDK retried transparently beneath the agent loop.
            Assert.Equal(3, http.RequestCount);
            Assert.Equal(1, harness.Tools.ProposePlanInvocations);
        }
        else
        {
            // The failure surfaced deterministically without a hidden retry.
            Assert.Equal(1, http.RequestCount);
            Assert.Equal(0, harness.Tools.ProposePlanInvocations);
        }
    }
}
