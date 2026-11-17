using System.Net;
using Delegat.AiRuntime.Tests.Support;
using Microsoft.Extensions.AI;
using Xunit.Abstractions;

namespace Delegat.AiRuntime.Tests;

/// <summary>
/// Provider-parity proofs over the Anthropic SDK path (Anthropic 13.3.0 → the SDK's own
/// Microsoft.Extensions.AI.AsIChatClient adapter → BatchGateChatClient →
/// FunctionInvokingChatClient → ChatClientAgent, over a scripted HttpMessageHandler).
/// </summary>
public class AnthropicRuntimeTests
{
    private readonly ITestOutputHelper _output;

    public AnthropicRuntimeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task BatchGate_InspectsWholeBatch_BeforeAnyInvocation_OnlyPlanDispatched()
    {
        var http = new ScriptedHttpMessageHandler();
        http.EnqueueSse(AnthropicSse.ToolCallBatchResponse);
        http.EnqueueSse(AnthropicSse.TextResponse);
        var harness = TestAgentHarness.ForAnthropic(http);

        var updates = await TestAgentHarness.CollectAsync(harness.Agent, "make a plan");

        Assert.Equal(2, harness.Gate.Inspections.Count);
        var inspection = harness.Gate.Inspections[0];
        Assert.Equal(2, inspection.Calls.Count);
        Assert.Contains(inspection.Calls, c => c is { CallId: "toolu_m1_plan", Name: "propose_plan" });
        Assert.Contains(inspection.Calls, c => c is { CallId: "toolu_m1_sib", Name: "sibling_tool" });
        Assert.Equal(new[] { "toolu_m1_plan" }, inspection.AllowedCallIds);
        Assert.Equal(new[] { "toolu_m1_sib" }, inspection.DeferredCallIds);
        Assert.Equal(1, harness.Tools.ProposePlanInvocations);
        Assert.Equal(0, harness.Tools.SiblingInvocations);
        Assert.Empty(inspection.UnknownToolNames);

        Assert.Equal(2, http.RequestCount);
        Assert.Contains(updates, u => u.Text?.Contains("Plan acknowledged.") == true);
    }

    [Fact]
    public async Task ToolCallArguments_FragmentedAcrossThreeDeltas_ArriveAsOneCompleteCall()
    {
        var http = new ScriptedHttpMessageHandler();
        http.EnqueueSse(AnthropicSse.ToolCallBatchResponse);
        http.EnqueueSse(AnthropicSse.TextResponse);
        var harness = TestAgentHarness.ForAnthropic(http);

        await TestAgentHarness.CollectAsync(harness.Agent, "make a plan");

        // partial_json fragments "{\"ti" + "tle\":\"bulk" + "-rename\"}" assembled to ONE call.
        var plan = Assert.Single(harness.Gate.Inspections[0].Calls, c => c.Name == "propose_plan");
        using var args = System.Text.Json.JsonDocument.Parse(plan.ArgumentsJson);
        Assert.Equal("bulk-rename", args.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task StrippedSibling_KeepsWireHistoryConsistent_NoDanglingToolUse()
    {
        var http = new ScriptedHttpMessageHandler();
        http.EnqueueSse(AnthropicSse.ToolCallBatchResponse);
        http.EnqueueSse(AnthropicSse.TextResponse);
        var harness = TestAgentHarness.ForAnthropic(http);

        await TestAgentHarness.CollectAsync(harness.Agent, "make a plan");

        // Round-2 request contains the plan tool_use + tool_result but no toolu_m1_sib.
        Assert.Equal(2, http.RequestCount);
        var round2 = http.RequestBodies[1];
        Assert.Contains("toolu_m1_plan", round2);
        Assert.DoesNotContain("toolu_m1_sib", round2);
        _output.WriteLine("round2 request body: " + round2);
    }

    [Fact]
    public async Task Usage_CannedNumbers_SurfaceThroughRun()
    {
        var http = new ScriptedHttpMessageHandler();
        http.EnqueueSse(AnthropicSse.ToolCallBatchResponse);
        http.EnqueueSse(AnthropicSse.TextResponse);
        var harness = TestAgentHarness.ForAnthropic(http);

        var updates = await TestAgentHarness.CollectAsync(harness.Agent, "make a plan");

        var usages = updates
            .SelectMany(u => u.Contents)
            .OfType<UsageContent>()
            .Select(c => c.Details)
            .ToList();
        _output.WriteLine("usage details: " +
            string.Join(" | ", usages.Select(u => $"in={u.InputTokenCount} out={u.OutputTokenCount} total={u.TotalTokenCount}")));
        // Round 1 canned: input 9 + output 21 => total 30; round 2: input 30 + output 6 => 36.
        Assert.Contains(usages, u => u.TotalTokenCount == 30 || u.TotalTokenCount == 36);
    }

    [Fact]
    public async Task UnknownTool_FiccFeedsErrorResultBack_LoopContinues_Deterministically()
    {
        var http = new ScriptedHttpMessageHandler();
        http.EnqueueSse(AnthropicSse.UnknownToolResponse);
        http.EnqueueSse(AnthropicSse.TextResponse);
        var harness = TestAgentHarness.ForAnthropic(http);

        var updates = await TestAgentHarness.CollectAsync(harness.Agent, "call something");

        Assert.Equal(2, http.RequestCount);
        Assert.Equal(0, harness.Tools.ProposePlanInvocations);
        Assert.Equal(0, harness.Tools.SiblingInvocations);
        Assert.Contains("nonexistent_tool", harness.Gate.Inspections[0].UnknownToolNames);

        var round2 = http.RequestBodies[1];
        _output.WriteLine("unknown-tool round2 body: " + round2);
        Assert.Contains("toolu_m1_unk", round2);
        Assert.Contains(updates, u => u.Text?.Contains("Plan acknowledged.") == true);
    }

    [Fact]
    public async Task Cancellation_SlowStream_ObservesCancellation_NoInvocation()
    {
        var http = new ScriptedHttpMessageHandler();
        http.EnqueueHanging(TimeSpan.FromSeconds(60));
        var harness = TestAgentHarness.ForAnthropic(http);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var ex = await Record.ExceptionAsync(
            () => TestAgentHarness.CollectAsync(harness.Agent, "hi", cts.Token));

        _output.WriteLine($"cancellation surfaced as: {ex?.GetType().FullName}: {ex?.Message}");
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
        Assert.Equal(0, harness.Tools.ProposePlanInvocations);
        Assert.Equal(0, harness.Tools.SiblingInvocations);
    }

    [Fact]
    public async Task MalformedStream_SurfacesDeterministicError_NoHang_NoHalfInvocation()
    {
        var http = new ScriptedHttpMessageHandler();
        http.EnqueueSse(AnthropicSse.MalformedResponse);
        var harness = TestAgentHarness.ForAnthropic(http);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ex = await Record.ExceptionAsync(
            () => TestAgentHarness.CollectAsync(harness.Agent, "hi", cts.Token));

        _output.WriteLine($"malformed stream surfaced as: {ex?.GetType().FullName}: {ex?.Message}");
        Assert.NotNull(ex);
        Assert.Equal(0, harness.Tools.ProposePlanInvocations);
        Assert.Equal(0, harness.Tools.SiblingInvocations);
    }

    [Fact]
    public async Task NoToolCalls_RunEndsImmediately_ExactlyOneModelRequest()
    {
        var http = new ScriptedHttpMessageHandler();
        http.EnqueueSse(AnthropicSse.TextResponse);
        var harness = TestAgentHarness.ForAnthropic(http);

        var updates = await TestAgentHarness.CollectAsync(harness.Agent, "just talk");

        Assert.Equal(1, http.RequestCount);
        Assert.Equal(0, harness.Tools.ProposePlanInvocations);
        Assert.Contains(updates, u => u.Text?.Contains("Plan acknowledged.") == true);
    }

    [Fact]
    public async Task Http500_ThenSucceeds_RecordsSdkRetryBehavior()
    {
        var http = new ScriptedHttpMessageHandler();
        http.EnqueueStatus(HttpStatusCode.InternalServerError, AnthropicSse.ServerErrorBody);
        http.EnqueueSse(AnthropicSse.TextResponse);
        // MaxRetries=0 makes the failure deterministic — no SDK-level retry.
        var harness = TestAgentHarness.ForAnthropic(http, maxRetries: 0);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ex = await Record.ExceptionAsync(
            () => TestAgentHarness.CollectAsync(harness.Agent, "hi", cts.Token));

        _output.WriteLine($"500 with MaxRetries=0: requests={http.RequestCount}, " +
                          $"exception={ex?.GetType().FullName}: {ex?.Message}");
        Assert.NotNull(ex); // surfaced deterministically
        Assert.Equal(1, http.RequestCount);
        Assert.Equal(0, harness.Tools.ProposePlanInvocations);
    }
}
