using Microsoft.Extensions.AI;

namespace Delegat.AiRuntime.Tests.Support;

/// <summary>
/// Two AIFunctions with invocation counters. propose_plan records and returns a JSON ack;
/// sibling_tool records — tests assert it is NEVER invoked when the batch gate defers it.
/// </summary>
internal sealed class RecordingTools
{
    public RecordingTools()
    {
        ProposePlan = AIFunctionFactory.Create(
            new Func<string, object>(ProposePlanImpl),
            ProposePlanSiblingName,
            "Submit a plan proposal for operator confirmation");
        Sibling = AIFunctionFactory.Create(
            new Func<string, object>(SiblingImpl),
            SiblingName,
            "Sibling tool that must be deferred when batched with a plan");
    }

    public const string ProposePlanSiblingName = "propose_plan";
    public const string SiblingName = "sibling_tool";

    public AIFunction ProposePlan { get; }
    public AIFunction Sibling { get; }

    public int ProposePlanInvocations;
    public int SiblingInvocations;
    public List<string> InvocationLog { get; } = new();

    public IReadOnlyList<string> Names => [ProposePlanSiblingName, SiblingName];

    private object ProposePlanImpl(string title)
    {
        ProposePlanInvocations++;
        InvocationLog.Add($"{ProposePlanSiblingName}({title})");
        return new { ok = true, ack = "plan recorded", title };
    }

    private object SiblingImpl(string note)
    {
        SiblingInvocations++;
        InvocationLog.Add($"{SiblingName}({note})");
        return new { ok = true, note };
    }
}
