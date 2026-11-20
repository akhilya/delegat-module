using Delegat.Models.Ai;
using Delegat.Services.Ai;
using Delegat.Services.Ai.Agent;
using Delegat.Services.Ai.Agent.Maf;

namespace Delegat.Tests;

/// <summary>
/// M6 cutover: MAF is the default runtime, legacy is an explicit rollback, and durable
/// pins survive the new default in both directions - no silent engine selection and no
/// fallback on provider errors.
/// </summary>
public partial class MafEngineParityTests
{
    [Theory]
    [InlineData(null, "maf")]
    [InlineData("", "maf")]
    [InlineData("   ", "maf")]
    [InlineData("maf", "maf")]
    [InlineData("MAF", "maf")]
    [InlineData("legacy", "legacy")]
    [InlineData(" Legacy ", "legacy")]
    public void RuntimeSetting_DefaultsToMaf_AndRequiresExplicitLegacy(
        string? setting, string expected) =>
        Assert.Equal(expected, MafChatEngine.ResolveRuntime(setting));

    [Theory]
    [InlineData("maff")]
    [InlineData("legacyy")]
    public void UnknownRuntimeSetting_IsRejectedInsteadOfSilentlySelectingAnotherEngine(
        string setting) =>
        Assert.Throws<InvalidOperationException>(
            () => MafChatEngine.ResolveRuntime(setting));

    [Fact]
    public async Task PinnedLegacyTurn_SurvivesTheNewMafDefault()
    {
        var legacy = new ScriptedAiStreamingClient([
            new AiStreamEvent
            {
                type = "content_block_delta",
                delta = new AiStreamDelta
                {
                    type = "text_delta",
                    text = "pinned legacy answer",
                },
            },
            new AiStreamEvent { type = "message_stop" },
        ]);
        await using var fixture = await CreateFixtureCoreAsync(
            new EmptyMcpGateway(), null, null, null, [], legacy);

        var events = await ProviderCollectAsync(fixture.Chat.StreamChatEventsAsync(
            [UserMessage("hello")], fixture.StoreId, fixture.UserId,
            sessionId: "pinned-legacy", locale: "en", runtimeOverride: "legacy"));

        Assert.Equal("pinned legacy answer",
            AiSseTurnRecorder.Fold(events.Select(e => e.ToSse()).ToList()).Content);
        Assert.Single(legacy.Requests);
        Assert.Empty(fixture.Client.Requests);
        Assert.Equal(0, fixture.Factory.Calls);
        Assert.Equal(0, fixture.Invoker.Calls);
        Assert.Equal(0, fixture.Mcp.Calls);
    }

    [Fact]
    public async Task PinnedMafTurn_SurvivesAnExplicitLegacyRollback()
    {
        var legacy = new ScriptedAiStreamingClient();
        await using var fixture = await CreateFixtureCoreAsync(
            new EmptyMcpGateway(), null, null, null,
            [Text("pinned maf answer")], legacy, runtime: "legacy");

        var events = await ProviderCollectAsync(fixture.Chat.StreamChatEventsAsync(
            [UserMessage("hello")], fixture.StoreId, fixture.UserId,
            sessionId: "pinned-maf", locale: "en", runtimeOverride: "maf"));

        Assert.Equal("pinned maf answer",
            AiSseTurnRecorder.Fold(events.Select(e => e.ToSse()).ToList()).Content);
        Assert.Empty(legacy.Requests);
        Assert.Single(fixture.Client.Requests);
        Assert.Equal(1, fixture.Factory.Calls);
        Assert.Equal(0, fixture.Invoker.Calls);
        Assert.Equal(0, fixture.Mcp.Calls);
    }
}
