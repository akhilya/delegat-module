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

    [Theory]
    [InlineData("[1194,1239,1344]", 3, true)]
    [InlineData("[1194,1239,1344]", 50, false)]
    [InlineData("[1194,1194,1344]", 3, false)]
    [InlineData("[]", 0, false)]
    public void NamedExportRequiresUniqueIdsAndConsistentCount(string productIds, int count, bool expected)
    {
        var json = """
            {"summary":"Export to Ozon","affectedCount":__COUNT__,"steps":[{"description":"Export selected products","tool":"platform_request","args":{"method":"POST","path":"/marketplace/connections/3/products/export-batch","body":{"productIds":__IDS__}}}]}
            """.Replace("__COUNT__", count.ToString()).Replace("__IDS__", productIds);
        Assert.Equal(expected, ProposePlanReader.Read(json).HasContent);
    }

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(17, 17, true)]
    [InlineData(49, 49, true)]
    [InlineData(49, 50, false)]
    public void NamedExportCountIsDerivedFromArgumentsNotAFixedBatchSize(int idsCount, int count, bool expected)
    {
        var ids = string.Join(",", Enumerable.Range(1000, idsCount));
        var json = """
            {"summary":"Export selected products","affectedCount":__COUNT__,"steps":[{"description":"Export selected products","tool":"platform_request","args":{"method":"POST","path":"/marketplace/connections/12/products/export-batch","body":{"productIds":[__IDS__]}}}]}
            """.Replace("__COUNT__", count.ToString()).Replace("__IDS__", ids);
        Assert.Equal(expected, ProposePlanReader.Read(json).HasContent);
    }

    [Fact]
    public void NamedExportCannotAlsoSelectTheWholeCatalog()
    {
        var plan = ProposePlanReader.Read("""
            {"summary":"Export to Ozon","affectedCount":2,"steps":[{"description":"Export selected products","tool":"platform_request","args":{"method":"POST","path":"/marketplace/connections/3/products/export-batch","body":{"productIds":[1194,1239],"allMatching":true}}}]}
            """);
        Assert.False(plan.HasContent);
    }

    // A step may carry only tool, description, and args. Extra step-level
    // fields (like the real model's outputs beside args) must be rejected
    // before signing, never silently dropped by the executor.
    [Theory]
    [InlineData("outputs", "[{\"fileName\":\"report.csv\",\"contentType\":\"text/csv\"}]")]
    [InlineData("code", "\"print('ok')\"")]
    [InlineData("body", "{}")]
    [InlineData("artifactId", "\"63b2101f95c742febc235f67bba05bba\"")]
    public void StepLevelFieldsBesideArgsAreRejected(string field, string value)
    {
        var json = $$"""
            {"summary":"Generate CSV","steps":[{"tool":"run_python","description":"Generate CSV","args":{"code":"print('ok')"},"{{field}}":{{value}}}]}
            """;
        Assert.False(ProposePlanReader.Read(json).HasContent);
    }

    [Fact]
    public void NestedArgsOutputsStaysByteIdentical()
    {
        var json = """
            {"summary":"Generate CSV","steps":[{"tool":"run_python","description":"Generate CSV","args":{"code":"print('ok')","outputs":[{"fileName":"report.csv","contentType":"text/csv"}]}}]}
            """;
        var plan = ProposePlanReader.Read(json);

        Assert.True(plan.HasContent);
        var outputs = plan.Steps[0].GetProperty("args").GetProperty("outputs").GetRawText();
        Assert.Equal(
            """[{"fileName":"report.csv","contentType":"text/csv"}]""",
            outputs);
    }
}
