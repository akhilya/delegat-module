using System.Text.Json;
using Delegat.Services.Ai.Agent;

namespace Delegat.Tests;

public class ProposePlanReaderTests
{
    [Fact]
    public void KeepsSummaryWhenStepsArriveAsText()
    {
        var json = """
            {"summary":"Export products with photos to Wildberries","steps":"[{\"tool\":\"platform_request\",\"description\":\"Export the products that have photos\",\"args\":{\"method\":\"POST\",\"path\":\"/marketplace/connections/4/products/export-batch\"}}]","affectedCount":12}
            """;

        var plan = ProposePlanReader.Read(json);

        Assert.Equal("Export products with photos to Wildberries", plan.Summary);
        Assert.True(plan.HasContent);
        Assert.Equal(12, plan.AffectedCount);
        Assert.Single(plan.Steps);
        Assert.Equal(
            "Export the products that have photos",
            plan.Steps[0].GetProperty("description").GetString());
        Assert.Equal("platform_request", plan.Steps[0].GetProperty("tool").GetString());
    }

    [Fact]
    public void EmptyCallIsNotAPlan()
    {
        var plan = ProposePlanReader.Read("{}");

        Assert.False(plan.HasContent);
        Assert.Empty(plan.Steps);
    }

    [Fact]
    public void ReadsANormalStepArray()
    {
        var plan = ProposePlanReader.Read("""
            {"summary":"Export","steps":[{"description":"Send the batch","tool":"platform_request","args":{"method":"POST"}}],"isDestructive":false}
            """);

        Assert.True(plan.HasContent);
        Assert.Equal("Send the batch", plan.Steps[0].GetProperty("description").GetString());
        Assert.Equal(JsonValueKind.Object, plan.Steps[0].GetProperty("args").ValueKind);
    }

    [Theory]
    [InlineData("{\"summary\":\"Update\",\"steps\":[]}")]
    [InlineData("{\"summary\":\"Update\",\"steps\":[{\"description\":\"Update\"}]}")]
    [InlineData("{\"summary\":\"Update\",\"steps\":[{\"tool\":\"platform_request\",\"description\":\"Update\",\"args\":null}]}")]
    [InlineData("{\"summary\":\"Update\",\"steps\":[{\"tool\":\"propose_plan\",\"description\":\"Update\",\"args\":{}}]}")]
    [InlineData("{\"steps\":[{\"tool\":\"platform_request\",\"description\":\"Update\",\"args\":{}}]}")]
    public void InvalidPlanCannotBePresented(string json) =>
        Assert.False(ProposePlanReader.Read(json).HasContent);
}
