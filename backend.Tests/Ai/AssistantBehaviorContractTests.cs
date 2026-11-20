using System.Text.Json;
using System.Text.RegularExpressions;
using Delegat.Models.Ai;
using Delegat.Services.Ai.Agent;
using Delegat.Services.Ai.Prompts;

namespace Delegat.Tests;

[Collection("sandbox-env")]
public class AssistantBehaviorContractTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    public void CapabilityGuidance_IsSharedByRenderedChatAndPlatformSurface(string locale)
    {
        var prompt = Prompt(locale, turnText: "повысь все цены на 500");
        Assert.Contains(AssistantCapabilityContract.Guidance, prompt, StringComparison.Ordinal);
        Assert.Contains(AssistantCapabilityContract.Guidance, PlatformRequestPolicy.SurfaceDescription,
            StringComparison.Ordinal);
        var errors = AssistantInstructionAudit.Check(
        [
            ("chat", prompt, true),
            ("platform", PlatformRequestPolicy.SurfaceDescription, true),
        ]);
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    public void FailureGuidance_DoesNotRelabelEvery404AsAnInventedRoute(string locale)
    {
        var prompt = Normalize(Prompt(locale));
        Assert.DoesNotContain("A 404 means the path is wrong", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("a 404 means the route is not a search_actions hit", prompt,
            StringComparison.Ordinal);
        Assert.Contains("route_not_found means the path is unsupported", prompt, StringComparison.Ordinal);
        Assert.Contains("method_not_allowed means a wrong verb for a known route", prompt,
            StringComparison.Ordinal);
        Assert.Contains("resource_not_found means a dispatched resource was not found", prompt,
            StringComparison.Ordinal);
        Assert.Contains("An HTTP 404 alone proves neither", prompt, StringComparison.Ordinal);
        Assert.Contains("Explain only verified causes; label uncertainty", prompt, StringComparison.Ordinal);
        Assert.Contains("do not automatically retry a failed write", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoveryGuidance_UsesTheActualHitFieldsWithoutInferringGlobalAbsence()
    {
        var guidance = Normalize(AssistantCapabilityContract.Guidance);
        Assert.NotNull(typeof(CabinetActionCatalog.ActionHit).GetProperty("CallableViaJson"));
        Assert.NotNull(typeof(CabinetActionCatalog.ActionHit).GetProperty("Transport"));
        Assert.Contains("reports callableViaJson and, for non-JSON hits, transport", guidance);
        Assert.DoesNotContain("requiresMultipart", guidance);
        Assert.DoesNotContain("binaryDownload", guidance);
        Assert.Contains("do not invent a route or infer global product absence", guidance);
        Assert.Contains("Binary download routes may be omitted", guidance);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ArtifactGuidance_RespectsTheActualSurfaceAndOptionalCapture(bool cabinet, bool capture)
    {
        using var sandbox = new EnvironmentScope("SANDBOX_URL", "http://sandbox.example.test");
        using var enabled = new EnvironmentScope("AI_ENABLED", "true");
        using var secret = new EnvironmentScope("SANDBOX_SECRET", capture ? "behavior-contract-secret" : null);
        var tools = AgentToolCatalog.Build(includeGeneratedArtifacts: cabinet);
        Assert.Equal(cabinet, tools.Any(t => t.name == GeneratedFileContract.ImportToolName));
        var python = Assert.Single(tools, t => t.name == "run_python");
        Assert.Equal(cabinet && capture, python.input_schema.properties!.ContainsKey("outputs"));
        var prompt = Normalize(Prompt("en"));
        Assert.Contains("A description is not permission or proof that an optional tool is enabled", prompt);
        Assert.Contains("cabinet-only capabilities when exposed", prompt);
        Assert.Contains("external MCP and Agent HTTP do not expose that artifact import", prompt);
        Assert.Contains("No read-only Python exemption exists", prompt);
        Assert.DoesNotContain("behavior-contract-secret", prompt + JsonSerializer.Serialize(tools));
    }

    [Fact]
    public void SharedFileContracts_DescribeImplementedHandoffWithoutAdvertisingItExternally()
    {
        foreach (var text in new[]
        {
            AssistantWriteContract.StoreFileFromText,
            AssistantWriteContract.ProductFileFromText,
        })
        {
            Assert.DoesNotContain("planned HTTP artifact handoff", text);
            Assert.Contains("separate confirmed import_generated_file plan", text);
            Assert.Contains("returned immutable artifact metadata", text);
            Assert.Contains("external MCP and Agent HTTP do not expose that import tool", text);
        }
    }

    [Fact]
    public void ReviewGuidance_ReusesImmutableOutputWithoutDumpingOrRecalculatingIt()
    {
        var review = Normalize(AiPrompts.PlanResultFollowup);
        Assert.Contains("do not propose another plan simply to verify the same successful calculation again", review);
        Assert.Contains("summarize the result and file availability instead of dumping the full table", review);
        Assert.Contains("An explicitly requested inline table is an exception only when complete output supports it",
            review);
        Assert.Contains("reuse its exact artifactId, sha256, length, and expiresAtUtc", review);
        Assert.Contains("Do not recompute mentally or change the classification rules", review);
        Assert.Contains("Saving a captured artifact permanently requires a fresh import_generated_file proposal", review);
        Assert.Contains("A completed import, including an idempotent replay, already supplies the saved-file result",
            review);
        Assert.Contains("Downloading an existing result needs no new computation or import", review);
    }

    [Fact]
    public void ReviewGuidance_PreservesFailureUncertaintyAndProposesOnlyTheUnfinishedTask()
    {
        var review = Normalize(AiPrompts.PlanResultFollowup);
        Assert.Contains("Do not claim nothing changed from an exit code", review);
        Assert.Contains("Never replay successful steps, broaden targets", review);
        Assert.Contains("requires a NEW explicit confirmation", review);
        Assert.Contains("The ONLY permitted tool in this review is propose_plan", review);
        Assert.Contains("A new plan discards preceding review draft text", review);
        Assert.Contains("Put a short verified bridge in its summary: what completed, what remains", review);
        Assert.Contains("Do not claim the proposed step has already run", review);
        Assert.Contains("never fabricate a file, link, or button", review);
    }

    [Fact]
    public void ReviewGuidance_DistinguishesHostEvidenceFromCalculationsAndModelEstimates()
    {
        var review = Normalize(AiPrompts.PlanResultFollowup);
        Assert.Contains("Unknown/null evidence stays unknown", review);
        Assert.Contains("affectedCount is a model estimate, not an observed result", review);
        Assert.Contains("do not sum overlapping steps", review);
        Assert.Contains("Preserve an exact zero, including an idempotent import replay", review);
        Assert.Contains("Unknown host counts do not invalidate a successful calculation", review);
        Assert.Contains("A queued job is not finished work", review);
        Assert.Contains("Missing context is not permission to invent an earlier result", review);
    }

    [Theory]
    [InlineData("professional")]
    [InlineData("friendly")]
    [InlineData("expert")]
    public void RegisterGuidance_PreservesConfiguredStyleAndConsistencyAcrossPhases(string style)
    {
        var prompt = Normalize(Prompt("ru", new AiStoreSettings { CommunicationStyle = style }));
        Assert.Contains("Communication style:", prompt);
        Assert.Contains("Keep the same level of formality in plans, result reviews, and recovery", prompt);
        Assert.Contains("For Russian, use the formal second person by default", prompt);
        Assert.Contains("follow an explicit user/style preference consistently", prompt);
        Assert.Contains("UI language: Russian", prompt);
    }

    private static string Prompt(string locale, AiStoreSettings? settings = null, string? turnText = null) =>
        AiPrompts.ChatSystem("Store", 1, 0, [], "user", false, settings: settings, locale: locale, turnText: turnText);

    private static string Normalize(string text) => Regex.Replace(text, @"\s+", " ");

    private sealed class EnvironmentScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        public EnvironmentScope(string name, string? value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
    }
}
