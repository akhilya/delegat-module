using System.Text.Json;
using Delegat.Models.Ai;
using Delegat.Services.Ai.Agent;
using Delegat.Services.Ai.Prompts;

namespace Delegat.Tests;

public class AssistantContourTests
{
    [Fact]
    public void Instructions_MatchTheControllerBodies()
    {
        var prompt = Prompt(page: new AiPageContext { Page = "fieldMapping", MappingId = 9, ConnectionId = 3 })
            + "\n" + Prompt(turnText: "повысь все цены на 500")
            + "\n" + Prompt(turnText: "выгрузи все товары");
        var sources = new List<(string, string, bool)>
        {
            ("prompt", prompt, true),
            ("policy", PlatformRequestPolicy.SurfaceDescription, true),
            ("catalog", CabinetActionCatalog.InstructionText(), true),
        };
        foreach (var relative in new[] { "docs/ai/operator-playbook.md", "docs/api/mcp.md" })
        {
            var path = FindRepoFile(relative);
            if (path != null)
                sources.Add((relative, File.ReadAllText(path), false));
        }

        var errors = AssistantInstructionAudit.Check(sources);
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Fact]
    public void Guard_RejectsARegexPatternBesideTheModifier()
    {
        using var doc = JsonDocument.Parse("""
            {"adapterFieldKey":"name","modifierType":"regexReplace","pattern":"warhammer","replacement":"ВХ40"}
            """);
        var reason = AssistantBodyGuard.RejectReason(
            "POST", "/marketplace/connections/4/mappings/31/graph/modifiers", doc.RootElement);
        Assert.NotNull(reason);
        Assert.Contains("params.pattern", reason, StringComparison.Ordinal);
        Assert.Contains("not sent", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Guard_AcceptsTheSameRegexInsideParams()
    {
        using var doc = JsonDocument.Parse("""
            {"adapterFieldKey":"description","modifierType":"regexReplace","params":{"pattern":"(?i)warhammer(?:\\s*40\\s*[kк]|\\s*40\\s*0{3})?","replacement":"ВХ40"}}
            """);
        var reason = AssistantBodyGuard.RejectReason(
            "POST", "/api/marketplace/connections/4/mappings/31/graph/modifiers", doc.RootElement);
        Assert.Null(reason);
    }

    [Fact]
    public void Guard_RejectsAnInvalidPatternAndAMissingDivisor()
    {
        using var bad = JsonDocument.Parse("""
            {"adapterFieldKey":"description","modifierType":"regexReplace","params":{"pattern":"\\u{1F600}","replacement":""}}
            """);
        var invalid = AssistantBodyGuard.RejectReason(
            "POST", "/marketplace/connections/4/mappings/31/graph/modifiers", bad.RootElement);
        Assert.NotNull(invalid);
        Assert.Contains("not a .NET", invalid, StringComparison.Ordinal);

        using var divide = JsonDocument.Parse("""
            {"adapterFieldKey":"weight","modifierType":"divide","divisor":10}
            """);
        var missed = AssistantBodyGuard.RejectReason(
            "POST", "/marketplace/connections/4/mappings/31/graph/modifiers", divide.RootElement);
        Assert.NotNull(missed);
        Assert.Contains("params.divisor", missed, StringComparison.Ordinal);
    }

    [Fact]
    public void Guard_AcceptsDivideWhenTheDivisorIsPinned()
    {
        using var doc = JsonDocument.Parse("""
            {"adapterFieldKey":"height","modifierType":"divide","params":{},"paramSources":[{"param":"divisor","sourceType":"attribute","sourceAttributeId":4}]}
            """);
        var reason = AssistantBodyGuard.RejectReason(
            "POST", "/marketplace/connections/4/mappings/31/graph/modifiers", doc.RootElement);
        Assert.Null(reason);
    }

    private static string Prompt(AiPageContext? page = null, string? turnText = null) =>
        AiPrompts.ChatSystem("Store", 1, 0, [], "user", false, page, turnText: turnText);

    private static string? FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
