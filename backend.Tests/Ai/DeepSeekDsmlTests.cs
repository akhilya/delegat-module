using System.Text.Json;
using Delegat.Services.Ai;

namespace Delegat.Tests;

public class DeepSeekDsmlTests
{
    private const string Bar = "\uFF5C";

    [Fact]
    public void UserSample_BecomesPlatformRequest_AndIsNotShown()
    {
        var sample = $"""
            <{Bar}{Bar}DSML{Bar}{Bar} calls>
            <{Bar}{Bar}DSML{Bar}{Bar} invoke name="platform_request">
            <{Bar}{Bar}DSML{Bar}{Bar} parameter name="method" string="true">GET</{Bar}{Bar}DSML{Bar}{Bar} parameter>
            <{Bar}{Bar}DSML{Bar}{Bar} parameter name="path" string="true">/marketplace/mapping-graph/blocks</{Bar}{Bar}DSML{Bar}{Bar} parameter>
            </{Bar}{Bar}DSML{Bar}{Bar} invoke>
            </{Bar}{Bar}DSML{Bar}{Bar} calls>
            """;

        var gate = new DeepSeekDsmlGate();
        var drain = gate.Push(sample);
        var tail = gate.Flush();

        Assert.Equal("", drain.Visible + tail.Visible);
        var invoke = Assert.Single(drain.Invokes.Concat(tail.Invokes));
        Assert.Equal("platform_request", invoke.Name);
        using var json = JsonDocument.Parse(invoke.ArgumentsJson);
        Assert.Equal("GET", json.RootElement.GetProperty("method").GetString());
        Assert.Equal("/marketplace/mapping-graph/blocks", json.RootElement.GetProperty("path").GetString());
    }

    [Fact]
    public void SingleBarAndAsciiPipe_ParseTheSameCall()
    {
        var sample = $"<{Bar}DSML{Bar}calls><{Bar}DSML{Bar}invoke name=\"platform_request\">" +
                     $"<{Bar}DSML{Bar}parameter name=\"method\" string=\"true\">GET</{Bar}DSML{Bar}parameter>" +
                     $"</{Bar}DSML{Bar}invoke></{Bar}DSML{Bar}calls>";

        var invokes = DeepSeekDsmlGate.ParseInvokes(sample);
        Assert.Equal("platform_request", Assert.Single(invokes).Name);

        var pipes = sample.Replace(Bar, "|");
        Assert.Equal("platform_request", Assert.Single(DeepSeekDsmlGate.ParseInvokes(pipes)).Name);
    }

    [Fact]
    public void Chunks_HoldTheTagUntilItCloses()
    {
        var sample = "Сначала блоки.\n" +
                     $"<{Bar}DSML{Bar} calls>" +
                     $"<{Bar}DSML{Bar} invoke name=\"functions.platform_request\">" +
                     $"<{Bar}DSML{Bar} parameter name=\"method\" string=\"true\">GET</{Bar}DSML{Bar} parameter>" +
                     $"<{Bar}DSML{Bar} parameter name=\"limit\" string=\"false\">12</{Bar}DSML{Bar} parameter>" +
                     $"</{Bar}DSML{Bar} invoke>" +
                     $"</{Bar}DSML{Bar} calls>\n" +
                     "дальше";

        var gate = new DeepSeekDsmlGate();
        var visible = "";
        var invokes = new List<DeepSeekDsmlGate.Invoke>();
        for (var i = 0; i < sample.Length; i += 3)
        {
            var drain = gate.Push(sample.Substring(i, Math.Min(3, sample.Length - i)));
            visible += drain.Visible;
            invokes.AddRange(drain.Invokes);
            Assert.DoesNotContain("DSML", drain.Visible, StringComparison.Ordinal);
        }
        var tail = gate.Flush();
        visible += tail.Visible;
        invokes.AddRange(tail.Invokes);

        Assert.Equal("Сначала блоки.\n\nдальше", visible);
        var invoke = Assert.Single(invokes);
        Assert.Equal("platform_request", invoke.Name);
        using var json = JsonDocument.Parse(invoke.ArgumentsJson);
        Assert.Equal("GET", json.RootElement.GetProperty("method").GetString());
        Assert.Equal(12, json.RootElement.GetProperty("limit").GetInt32());
    }

    [Fact]
    public void ComparisonSign_StaysVisible()
    {
        var gate = new DeepSeekDsmlGate();
        var first = gate.Push("price <");
        var second = gate.Push(" 10");
        var tail = gate.Flush();
        Assert.Equal("price < 10", first.Visible + second.Visible + tail.Visible);
        Assert.Empty(first.Invokes);
        Assert.Empty(second.Invokes);
    }

    [Fact]
    public void UnclosedMarkup_IsDroppedOnFlush()
    {
        var gate = new DeepSeekDsmlGate();
        var drain = gate.Push($"смотри <{Bar}DSML{Bar} calls><{Bar}DSML{Bar} invoke name=\"platform_request\">");
        var tail = gate.Flush();
        Assert.DoesNotContain("DSML", drain.Visible + tail.Visible, StringComparison.Ordinal);
        Assert.Equal("смотри ", drain.Visible + tail.Visible);
        Assert.Empty(drain.Invokes.Concat(tail.Invokes));
    }
}
