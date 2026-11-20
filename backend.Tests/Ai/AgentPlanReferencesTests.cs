using System.Text.Json;
using Delegat.Services.Ai.Agent;

namespace Delegat.Tests;

public class AgentPlanReferencesTests
{
    private static JsonElement Step(string tool, object args, string description = "Do the step") =>
        JsonSerializer.SerializeToElement(new { tool, description, args });

    private static JsonElement PlatformStep(string method, string path, object? body = null) =>
        Step("platform_request", new { method, path, body });

    private static JsonElement Result(object value) =>
        JsonSerializer.SerializeToElement(value);

    [Fact]
    public void ValidGraph_PassesAndValidationSeesConcreteIds()
    {
        var steps = new List<JsonElement>
        {
            PlatformStep("POST", "/categories", new { name = "Example" }),
            PlatformStep("POST", "/categories/$step:0.id/attributes",
                new { name = "Label", key = "label", type = 0 }),
            PlatformStep("POST", "/products", new
            {
                name = "One",
                sku = "ONE",
                categoryId = "$step:0.id",
                attributeValues = new[] { new { attributeId = "$step:1.id", value = "sample" } },
            }),
        };

        Assert.Null(AgentPlanReferences.RejectionReason(steps));

        var forValidation = AgentPlanReferences.ForValidation(steps);
        Assert.Equal("/categories/1/attributes",
            forValidation[1].GetProperty("args").GetProperty("path").GetString());
        var productBody = forValidation[2].GetProperty("args").GetProperty("body");
        Assert.Equal(1, productBody.GetProperty("categoryId").GetInt32());
        Assert.Equal(JsonValueKind.Number, productBody.GetProperty("categoryId").ValueKind);
        Assert.Equal(1,
            productBody.GetProperty("attributeValues")[0].GetProperty("attributeId").GetInt32());
        // The signed template is never rewritten.
        Assert.Equal("$step:0.id",
            steps[2].GetProperty("args").GetProperty("body").GetProperty("categoryId").GetString());
    }

    [Fact]
    public void ResolveArguments_SubstitutesPositiveIntegerIds()
    {
        var results = new List<JsonElement> { Result(new { id = 42 }), Result(new { id = 7 }) };
        var resolved = AgentPlanReferences.ResolveArguments(
            "platform_request",
            """{"method":"POST","path":"/categories/$step:0.id/attributes","body":{"name":"L","attributeId":"$step:1.id"}}""",
            2, results);
        using var doc = JsonDocument.Parse(resolved);
        Assert.Equal("/categories/42/attributes", doc.RootElement.GetProperty("path").GetString());
        Assert.Equal(JsonValueKind.Number, doc.RootElement.GetProperty("body")
            .GetProperty("attributeId").ValueKind);
        Assert.Equal(7, doc.RootElement.GetProperty("body").GetProperty("attributeId").GetInt32());
        Assert.Equal("POST", doc.RootElement.GetProperty("method").GetString());
    }

    [Fact]
    public void ResolveArguments_NestedArrayObjectIdProperty_Resolves()
    {
        var resolved = AgentPlanReferences.ResolveArguments(
            "platform_request",
            """{"method":"POST","path":"/products","body":{"groups":[[{"categoryId":"$step:0.id"}]]}}""",
            1, [Result(new { id = 42 })]);
        using var doc = JsonDocument.Parse(resolved);
        Assert.Equal(42, doc.RootElement.GetProperty("body")
            .GetProperty("groups")[0][0].GetProperty("categoryId").GetInt32());
    }

    [Fact]
    public void ResolveArguments_FunctionsPrefixedPlatformRequest_Resolves()
    {
        var resolved = AgentPlanReferences.ResolveArguments(
            "functions.platform_request",
            """{"method":"POST","path":"/products","body":{"categoryId":"$step:0.id"}}""",
            1, [Result(new { id = 9 })]);
        using var doc = JsonDocument.Parse(resolved);
        Assert.Equal(9, doc.RootElement.GetProperty("body").GetProperty("categoryId").GetInt32());
    }

    [Fact]
    public void MarkerlessArgs_AreReturnedUnchanged()
    {
        const string args = """{"method":"GET","path":"/products?barcode=$step"}""";
        Assert.Same(args, AgentPlanReferences.ResolveArguments("platform_request", args, 0, []));
        Assert.Same(args, AgentPlanReferences.ResolveArguments("run_python", args, 0, []));
    }

    [Theory]
    [InlineData("/categories/$step:1.id/attributes")] // self reference
    [InlineData("/categories/$step:2.id/attributes")] // forward reference
    [InlineData("/categories/$step:-1.id/attributes")] // negative
    [InlineData("/categories/$step:x.id/attributes")] // malformed index
    [InlineData("/categories/$step:0/attributes")] // missing .id suffix
    [InlineData("/categories/$step:0.name/attributes")] // wrong member
    [InlineData("/categories/x$step:0.id/attributes")] // partial segment
    [InlineData("/categories/$step:0.idx/attributes")] // partial marker in segment
    public void MalformedOrForwardPathMarkers_AreRejected(string path)
    {
        var steps = new List<JsonElement>
        {
            PlatformStep("POST", "/categories", new { name = "Example" }),
            PlatformStep("POST", path, new { name = "Label" }),
        };
        Assert.NotNull(AgentPlanReferences.RejectionReason(steps));
    }

    [Fact]
    public void MarkerInQuery_IsRejected()
    {
        var steps = new List<JsonElement>
        {
            PlatformStep("POST", "/categories", new { name = "Example" }),
            PlatformStep("GET", "/products?categoryId=$step:0.id"),
        };
        Assert.NotNull(AgentPlanReferences.RejectionReason(steps));
    }

    [Fact]
    public void MarkerInMethod_IsRejected()
    {
        var steps = new List<JsonElement>
        {
            PlatformStep("POST", "/categories", new { name = "Example" }),
            Step("platform_request", new { method = "$step:0.id", path = "/products" }),
        };
        Assert.NotNull(AgentPlanReferences.RejectionReason(steps));
    }

    [Fact]
    public void MarkerInToolAndDescription_IsRejected()
    {
        Assert.NotNull(AgentPlanReferences.RejectionReason(
        [
            Step("$step:0.id", new { method = "GET", path = "/products" }),
        ]));
        Assert.NotNull(AgentPlanReferences.RejectionReason(
        [
            Step("platform_request", new { method = "GET", path = "/products" },
                description: "read $step:0.id"),
        ]));
    }

    [Fact]
    public void MarkerInArbitraryBodyText_IsRejected()
    {
        var steps = new List<JsonElement>
        {
            PlatformStep("POST", "/categories", new { name = "Example" }),
            PlatformStep("POST", "/products", new { name = "renamed via $step:0.id" }),
        };
        Assert.NotNull(AgentPlanReferences.RejectionReason(steps));
    }

    [Fact]
    public void MarkerInNonIdBodyProperty_IsRejected()
    {
        var steps = new List<JsonElement>
        {
            PlatformStep("POST", "/categories", new { name = "Example" }),
            PlatformStep("POST", "/products", new { name = "$step:0.id" }),
        };
        Assert.NotNull(AgentPlanReferences.RejectionReason(steps));
    }

    [Fact]
    public void MarkerInIdsArray_IsRejected()
    {
        var steps = new List<JsonElement>
        {
            PlatformStep("POST", "/categories", new { name = "Example" }),
            PlatformStep("POST", "/products/bulk-delete", new { productIds = new[] { "$step:0.id" } }),
        };
        Assert.NotNull(AgentPlanReferences.RejectionReason(steps));
    }

    [Fact]
    public void MarkerEmbeddedInIdValue_IsRejected()
    {
        var steps = new List<JsonElement>
        {
            PlatformStep("POST", "/categories", new { name = "Example" }),
            PlatformStep("POST", "/products", new { categoryId = "cat-$step:0.id" }),
        };
        Assert.NotNull(AgentPlanReferences.RejectionReason(steps));
    }

    [Fact]
    public void NonPostProducer_IsRejected()
    {
        var steps = new List<JsonElement>
        {
            PlatformStep("GET", "/categories"),
            PlatformStep("POST", "/products", new { categoryId = "$step:0.id" }),
        };
        Assert.NotNull(AgentPlanReferences.RejectionReason(steps));
    }

    [Fact]
    public void PythonOrMcpProducers_AreRejected()
    {
        foreach (var producerTool in new[] { "run_python", "mcp3_create_record" })
        {
            var steps = new List<JsonElement>
            {
                Step(producerTool, new { code = "print(1)", id = 3 }),
                PlatformStep("POST", "/products", new { categoryId = "$step:0.id" }),
            };
            Assert.NotNull(AgentPlanReferences.RejectionReason(steps));
        }
    }

    [Fact]
    public void MarkerInPythonOrMcpArgs_IsRejected()
    {
        var steps = new List<JsonElement>
        {
            PlatformStep("POST", "/categories", new { name = "Example" }),
            Step("run_python", new { code = "print($step:0.id)" }),
        };
        Assert.NotNull(AgentPlanReferences.RejectionReason(steps));
    }

    [Fact]
    public void UnrelatedSiblingStrings_AreLeftAlone()
    {
        var steps = new List<JsonElement>
        {
            PlatformStep("POST", "/categories", new { name = "Example" }),
            PlatformStep("POST", "/products", new
            {
                name = "step 0 costs $step",
                categoryId = "$step:0.id",
                note = "upgrade to step:0 later",
            }),
        };
        Assert.Null(AgentPlanReferences.RejectionReason(steps));
        var forValidation = AgentPlanReferences.ForValidation(steps);
        var body = forValidation[1].GetProperty("args").GetProperty("body");
        Assert.Equal(1, body.GetProperty("categoryId").GetInt32());
        Assert.Equal("step 0 costs $step", body.GetProperty("name").GetString());
        Assert.Equal("upgrade to step:0 later", body.GetProperty("note").GetString());
    }

    [Fact]
    public void ResolveArguments_MissingResultRefuses()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AgentPlanReferences.ResolveArguments(
                "platform_request",
                """{"method":"POST","path":"/products","body":{"categoryId":"$step:0.id"}}""",
                1, []));
        Assert.Contains("references step 1", ex.Message);
    }

    [Fact]
    public void ResolveArguments_ForwardReferenceRefuses()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AgentPlanReferences.ResolveArguments(
                "platform_request",
                """{"method":"POST","path":"/products","body":{"categoryId":"$step:1.id"}}""",
                1, [Result(new { id = 5 }), Result(new { id = 6 })]));
    }

    [Theory]
    [InlineData("null")] // missing result entry
    [InlineData("{\"id\":null}")]
    [InlineData("{\"id\":\"42\"}")] // string id
    [InlineData("{\"id\":4.5}")] // non-integer
    [InlineData("{\"id\":0}")] // non-positive
    [InlineData("{\"id\":-3}")]
    [InlineData("{}")] // absent
    [InlineData("{\"error\":\"boom\"}")]
    [InlineData("{\"status\":\"pending\"}")] // queued job, not a completed create
    [InlineData("{\"id\":42,\"status\":\"pending\"}")] // queued even with an id
    [InlineData("{\"id\":42,\"status\":\"failed\"}")]
    [InlineData("{\"id\":42,\"error\":\"boom\"}")]
    [InlineData("{\"id\":42,\"status\":400}")] // HTTP-style error status
    public void ResolveArguments_UnusableResultRefuses(string resultJson)
    {
        var result = resultJson == "null"
            ? default
            : JsonDocument.Parse(resultJson).RootElement.Clone();
        Assert.Throws<InvalidOperationException>(() =>
            AgentPlanReferences.ResolveArguments(
                "platform_request",
                """{"method":"POST","path":"/products","body":{"categoryId":"$step:0.id"}}""",
                1, [result]));
    }

    [Fact]
    public void ResolveArguments_NonPlatformToolRefuses()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AgentPlanReferences.ResolveArguments(
                "run_python",
                """{"code":"print('$step:0.id')"}""",
                1, [Result(new { id = 5 })]));
    }

    [Fact]
    public void ResolveArguments_MalformedMarkerRefuses()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AgentPlanReferences.ResolveArguments(
                "platform_request",
                """{"method":"POST","path":"/categories/$step:abc.id/attributes"}""",
                1, [Result(new { id = 5 })]));
    }
}
