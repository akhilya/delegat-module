using System.Text.Json;
using Delegat.Services.Marketplace.Graph;
using Delegat.Services.Marketplace.Modifiers;

namespace Delegat.Tests.Graph;

public class GraphEvaluatorModifierTests
{
    public static IEnumerable<object?[]> GraphSamples()
    {
        foreach (var row in SampleTable())
            yield return new object?[] { row.Type, row.Input, row.InputType, row.SinkType, row.Expected };
    }

    [Fact]
    public void EveryKnownModifier_HasAGraphSample()
    {
        var covered = SampleTable().Select(s => s.Type).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = ModifierPipeline.KnownModifierTypes
            .Where(t => !covered.Contains(t))
            .OrderBy(t => t)
            .ToList();
        Assert.True(missing.Count == 0, "Missing samples: " + string.Join(", ", missing));
    }

    [Theory]
    [MemberData(nameof(GraphSamples))]
    public void Modifier_ThroughGraph(
        string type,
        string? input,
        string? inputType,
        string sinkType,
        object expected)
    {
        var sample = SampleTable().First(s =>
            s.Type == type && s.Input == input && s.InputType == inputType);
        var result = RunModifier(sample);
        GraphAssert.Output(result, "out", expected, sinkType);
    }

    [Fact]
    public void UnknownModifier_PassesThrough()
    {
        var result = new GraphEvalHarness()
            .Static("src", "keep")
            .Modifier("m", "notARealModifier")
            .Edge("src", "m")
            .Field("out", "string")
            .WireToField("m", "out")
            .Eval();

        GraphAssert.Output(result, "out", "keep", "string");
    }

    [Fact]
    public void ParamOverridePin_ReplacesFactor()
    {
        var result = new GraphEvalHarness()
            .Static("src", "10", "number")
            .Static("f", "3", "number")
            .Modifier("m", "multiply", new Dictionary<string, object> { ["factor"] = 1m })
            .Edge("src", "m")
            .Edge("f", "m", "p:factor")
            .Field("out", "number")
            .WireToField("m", "out")
            .Eval();

        GraphAssert.Output(result, "out", 30m, "number");
    }

    [Fact]
    public void DivideByZero_PassesOriginalThrough()
    {
        var result = new GraphEvalHarness()
            .Static("src", "10", "number")
            .Modifier("m", "divide", new Dictionary<string, object> { ["divisor"] = 0m })
            .Edge("src", "m")
            .Field("out", "number")
            .WireToField("m", "out")
            .Eval();

        GraphAssert.Output(result, "out", 10m, "number");
    }

    [Fact]
    public void ForceType_DoesNotChangeValue_OnlySinkCoerce()
    {
        var result = new GraphEvalHarness()
            .Static("src", "12.9")
            .Modifier("m", "forceType", new Dictionary<string, object> { ["type"] = "integer" })
            .Edge("src", "m")
            .Field("out", "string")
            .WireToField("m", "out")
            .Eval();

        Assert.Equal("12.9", result.NodeValues["m"]?.ToString());
        GraphAssert.Output(result, "out", 12L, "integer");
    }

    [Fact]
    public void LlmGenerate_Placeholder_PassThrough_AndResolved()
    {
        var waiting = new GraphEvalHarness()
            .Static("src", "in")
            .Modifier("m", "llmGenerate")
            .Edge("src", "m")
            .Field("out", "string")
            .WireToField("m", "out")
            .Eval();
        GraphAssert.Output(waiting, "out", "[waiting for generation...]", "string");

        var pass = new GraphEvalHarness { GenerativePassThrough = true }
            .Static("src", "in")
            .Modifier("m", "llmGenerate")
            .Edge("src", "m")
            .Field("out", "string")
            .WireToField("m", "out")
            .Eval();
        GraphAssert.Output(pass, "out", "in", "string");

        var done = new GraphEvalHarness
        {
            GenerativeOutputs = new Dictionary<string, string> { ["m"] = "generated" },
        }
            .Static("src", "in")
            .Modifier("m", "llmGenerate")
            .Edge("src", "m")
            .Field("out", "string")
            .WireToField("m", "out")
            .Eval();
        GraphAssert.Output(done, "out", "generated", "string");
    }

    [Fact]
    public void LlmGenerate_FieldPins_CollectedWhenPassThrough()
    {
        var result = new GraphEvalHarness { GenerativePassThrough = true }
            .Static("src", "in")
            .Static("extra", "pin-val")
            .Modifier("m", "llmGenerate", extra: d =>
            {
                d.fieldPins = [new GraphFieldPin { id = "p1", label = "P1" }];
            })
            .Edge("src", "m")
            .Edge("extra", "m", "field:p1")
            .Field("out", "string")
            .WireToField("m", "out")
            .Eval();

        Assert.Equal("pin-val", result.FieldPinValues["m"]["p1"]);
    }

    [Fact]
    public void RandomInt_WithSeed_IsDeterministic()
    {
        object? Run() => new GraphEvalHarness()
            .Static("src", "x")
            .Modifier("m", "randomInt", new Dictionary<string, object>
            {
                ["min"] = 10,
                ["max"] = 20,
                ["seed"] = 7,
            })
            .Edge("src", "m")
            .Field("out", "integer")
            .WireToField("m", "out")
            .Eval()
            .Outputs["out"];

        GraphAssert.Equal(Run(), Run());
    }

    [Fact]
    public void RegexReplace_StripsEmoji_AndIgnoresJsCodePointEscapes()
    {
        const string pattern = @"[\uD83C-\uD83E][\uDC00-\uDFFF]|[\u2600-\u27BF]|[\u2B00-\u2BFF]|\uFE0F|\u200D|\u20E3";
        var input = "Привет 😀 мир ⭐ ✅ ☀️!";
        var stripped = ModifierPipeline.ApplySingle("regexReplace", input, P(("pattern", pattern), ("replacement", "")));
        Assert.Equal("Привет  мир   !", stripped);

        var js = ModifierPipeline.ApplySingle(
            "regexReplace",
            input,
            P(("pattern", @"[\u{1F000}-\u{1FAFF}\u{2600}-\u{27BF}\u{2B00}-\u{2BFF}\u{FE0F}]"), ("replacement", "")));
        Assert.Equal(input, js);
    }

    [Fact]
    public void RegexReplace_KeepsLettersDigitsAndOrdinaryPunctuation()
    {
        const string pattern = @"[^0-9A-Za-z\u0400-\u04FF\r\n .,;:!?()\[\]{}«»""'…\-+–—=%№@#&*/|_~$^<>]";
        var input = "Фигурка ёж UZ-1: hello (12)!_) ↘ ★";
        var stripped = ModifierPipeline.ApplySingle("regexReplace", input, P(("pattern", pattern), ("replacement", "")));
        Assert.Equal("Фигурка ёж UZ-1: hello (12)!_)  ", stripped);
    }

    [Fact]
    public void ApplySingle_TruncateAndUppercase_MatchGraphSamples()
    {
        Assert.Equal("abc", ModifierPipeline.ApplySingle("truncate", "abcdef", P(("maxLength", 3))));
        Assert.Equal("AB", ModifierPipeline.ApplySingle("uppercase", "Ab", P()));
    }

    private static GraphEvalResult RunModifier(ModifierSample sample)
    {
        return new GraphEvalHarness()
            .Static("src", sample.Input, sample.InputType)
            .Modifier("m", sample.Type, sample.Params)
            .Edge("src", "m")
            .Field("out", sample.SinkType)
            .WireToField("m", "out")
            .Eval();
    }

    private static List<ModifierSample> SampleTable() =>
    [
        new("truncate", "abcdef", null, "string", "abc", P(("maxLength", 3))),
        new("split", "a-b-c", null, "string", "b", P(("delimiter", "-"), ("index", 1))),
        new("replace", "hello", null, "string", "hallo", P(("search", "e"), ("replacement", "a"))),
        new("regexReplace", "aa-11", null, "string", "aa-xx", P(("pattern", @"\d+"), ("replacement", "xx"))),
        new("regexExtract", "aa-11", null, "string", "11", P(("pattern", @"(\d+)"), ("group", 1))),
        new("append", "ab", null, "string", "ab!", P(("suffix", "!"))),
        new("prepend", "ab", null, "string", "!ab", P(("prefix", "!"))),
        new("uppercase", "Ab", null, "string", "AB", P()),
        new("lowercase", "Ab", null, "string", "ab", P()),
        new("trim", "  x  ", null, "string", "x", P()),
        new("multiply", "10", "number", "number", 20m, P(("factor", 2m))),
        new("divide", "10", "number", "number", 5m, P(("divisor", 2m))),
        new("add", "10", "number", "number", 13m, P(("addend", 3m))),
        new("subtract", "10", "number", "number", 7m, P(("subtrahend", 3m))),
        new("round", "1.26", "number", "number", 1.3m, P(("decimals", 1))),
        new("floor", "1.9", "number", "number", 1m, P()),
        new("ceiling", "1.1", "number", "number", 2m, P()),
        new("clamp", "50", "number", "number", 10m, P(("min", 0m), ("max", 10m))),
        new("valueMap", "a", "select", "select", "b", P(("mappings", Map(("a", "b"))), ("default", "z"))),
        new("optionMap", "a", "select", "select", "b", P(("mappings", Map(("a", "b"))))),
        new("toText", "x", "string", "string", "x", P()),
        new("forceType", "1", "string", "integer", 1L, P(("type", "integer"))),
        new("defaultValue", "", null, "string", "fallback", P(("value", "fallback"))),
        new("takeFirst", "[\"a\",\"b\"]", "array", "string", "a", P()),
        new("limit", "[\"a\",\"b\",\"c\"]", "array", "string", "[\"a\",\"b\"]", P(("count", 2))),
        new("skip", "[\"a\",\"b\",\"c\"]", "array", "string", "[\"c\"]", P(("count", 2))),
        new("reverse", "[\"a\",\"b\"]", "array", "string", "[\"b\",\"a\"]", P()),
        new("join", "[\"a\",\"b\"]", "array", "string", "a|b", P(("delimiter", "|"))),
        new("sort", "[\"b\",\"a\"]", "array", "string", "[\"a\",\"b\"]", P()),
        new("addItems", "[\"a\"]", "array", "string", "[\"a\",\"z\"]", P(("items", new List<string> { "z" }))),
        new("removeItems", "[\"a\",\"z\"]", "array", "string", "[\"a\"]", P(("items", new List<string> { "z" }))),
        new("template", "v", null, "string", "pre-v", P(("template", "pre-{{value}}"))),
        new("randomInt", "x", null, "integer", ExpectedRandomInt(), P(("min", 4), ("max", 4), ("seed", 1))),
        new("randomFloat", "x", null, "string", ExpectedRandomFloat(), P(("min", 1m), ("max", 1m), ("decimals", 2), ("seed", 1))),
        new("randomPick", "x", null, "string", "only", P(("choices", new List<string> { "only" }), ("seed", 1))),
        new("randomUuid", "x", null, "string", ExpectedRandomUuid(), P(("seed", 3))),
    ];

    private static Dictionary<string, object> P(params (string k, object v)[] pairs)
        => pairs.ToDictionary(p => p.k, p => p.v);

    private static JsonElement Map(params (string from, string to)[] pairs)
        => JsonSerializer.SerializeToElement(pairs.Select(p => new { from = p.from, to = p.to }).ToArray());

    private static object ExpectedRandomInt()
        => ModifierPipeline.ApplySingle("randomInt", "x", P(("min", 4), ("max", 4), ("seed", 1)))!;

    private static object ExpectedRandomFloat()
        => ModifierPipeline.ApplySingle("randomFloat", "x", P(("min", 1m), ("max", 1m), ("decimals", 2), ("seed", 1)))!;

    private static object ExpectedRandomUuid()
        => ModifierPipeline.ApplySingle("randomUuid", "x", P(("seed", 3)))!;

    private readonly record struct ModifierSample(
        string Type,
        string? Input,
        string? InputType,
        string SinkType,
        object Expected,
        Dictionary<string, object> Params);
}
