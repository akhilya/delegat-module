using System.Text.Json;
using System.Text.Json.Serialization;
using Delegat.Services.Ai.Agent;

namespace Delegat.Tests.Ai;

public class AgentResultCountsTests
{
    private static JsonElement Json(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    private static void Unknown(AgentResultCounts counts)
    {
        Assert.All(new[] { counts.Read, counts.Matched, counts.Written }, value =>
        {
            Assert.Null(value.Value);
            Assert.Equal("unknown", value.Accuracy);
        });
    }

    [Theory]
    [InlineData(JsonIgnoreCondition.WhenWritingNull)]
    [InlineData(JsonIgnoreCondition.WhenWritingDefault)]
    public void UnknownValueStaysExplicitNullUnderTransportDefaultOmission(JsonIgnoreCondition condition)
    {
        var evidence = AgentResultCounts.Observe("sql_readonly", "{}", Json("""{"rows":[]}"""));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = condition,
        };
        var wire = JsonSerializer.SerializeToElement(evidence, options);
        Assert.Equal(JsonValueKind.Null, wire.GetProperty("matched").GetProperty("value").ValueKind);
        Assert.Equal(0L, wire.GetProperty("read").GetProperty("value").GetInt64());
        Assert.Equal(0L, wire.GetProperty("written").GetProperty("value").GetInt64());
        Assert.Equal("unknown", wire.GetProperty("matched").GetProperty("accuracy").GetString());
    }

    [Fact]
    public void SqlRowsAreReturnedRows_NotAggregateCellOrScannedRecords()
    {
        var counts = AgentResultCounts.Observe("sql_readonly", """{"sql":"SELECT COUNT(*) AS n FROM products"}""",
            Json("""{"table":"products","rows":[{"n":75}]}"""));
        Assert.Equal(1L, counts.Read.Value);
        Assert.Equal("rows", counts.Read.Unit);
        Assert.Null(counts.Matched.Value);
        Assert.Equal(0L, counts.Written.Value);
        Assert.Equal("exact", counts.Written.Accuracy);
    }

    [Fact]
    public void EmptySuccessfulReadIsExactZero_NotUnknown()
    {
        var counts = AgentResultCounts.Observe("functions.sql_readonly", "{}",
            Json("""{"rows":[]}"""));
        Assert.Equal(0L, counts.Read.Value);
        Assert.Equal("exact", counts.Read.Accuracy);
        Assert.Equal("sql_readonly", counts.Source);
        Assert.Null(counts.Matched.Value);
    }

    [Theory]
    [InlineData(false, false, 3L)]
    [InlineData(true, false, null)]
    [InlineData(false, true, null)]
    public void CatalogMatchesOnlyCompleteReturnedResultSet(bool productsMore, bool filesMore, long? matched)
    {
        var counts = AgentResultCounts.Observe("search_catalog", "{}", new
        {
            products = new[] { new { id = 1 }, new { id = 2 } },
            files = new[] { new { id = 1 } },
            productsHasMore = productsMore,
            filesHasMore = filesMore,
        });
        Assert.Equal(3L, counts.Read.Value);
        Assert.Equal("results", counts.Read.Unit);
        Assert.Equal(matched, counts.Matched.Value);
        Assert.Equal(0L, counts.Written.Value);
    }

    [Fact]
    public void NativeProductsUsesActualGatewayPrefix_AndServerTotal_NotRequestedPageSize()
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            items = Enumerable.Range(1, 20).Select(id => new { id }),
            totalCount = 75,
            lastPage = true,
        });
        var (result, _) = CabinetRequestInvoker.InterpretResponse(200, body);
        var counts = AgentResultCounts.Observe("platform_request",
            """{"method":"GET","path":"/api/products?pageSize=100"}""", result);
        Assert.Equal(CabinetRequestInvoker.MaxListItems, counts.Read.Value);
        Assert.Equal(75L, counts.Matched.Value);
        Assert.Equal("products", counts.Read.Unit);
        Assert.Equal(0L, counts.Written.Value);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("\"75\"")]
    [InlineData("null")]
    [InlineData("1.5")]
    [InlineData("9007199254740992")]
    [InlineData("0")]
    public void InvalidOrContradictoryNativeTotalIsUnknown_WithoutLosingObservedPrefix(string total)
    {
        var counts = AgentResultCounts.Observe("platform_request",
            """{"method":"GET","path":"/products"}""",
            Json("""{"items":[{"id":1}],"totalCount":TOTAL}""".Replace("TOTAL", total)));
        Assert.Equal(1L, counts.Read.Value);
        Assert.Null(counts.Matched.Value);
        Assert.Equal(0L, counts.Written.Value);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void CompletedTypedImportCountsOnlyNewFiles_NotReceiptReplay(bool replay, int written)
    {
        var result = new GeneratedFileImportResult(
            "store", 7, null, null, "r.csv", "text/csv", 18,
            new string('a', 64), "/api/store-storage/files/7/download", replay, written);
        var counts = AgentResultCounts.Observe("import_generated_file", "{}", result);
        Assert.Equal((long)written, counts.Written.Value);
        Assert.Equal("exact", counts.Written.Accuracy);
        Assert.Equal("files", counts.Written.Unit);
        Assert.Null(counts.Read.Value);
        Assert.Null(counts.Matched.Value);
        Unknown(AgentResultCounts.Observe("import_generated_file", "{}",
            JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        Unknown(AgentResultCounts.Observe("import_generated_file", "{}",
            result with { Idempotent = !replay }));
    }

    [Theory]
    [InlineData("""{"status":"queued","operationId":"op1","count":3,"items":[1,2,3],"totalCount":3}""")]
    [InlineData("""{"status":202,"items":[1,2,3],"totalCount":3}""")]
    [InlineData("""{"status":"failed","rows":[1,2,3],"writtenCount":3}""")]
    [InlineData("""{"status":"completed","error":"partial failure","rows":[1,2,3]}""")]
    [InlineData("""{"outcomeUnknown":true,"rows":[1,2,3],"writtenCount":3}""")]
    [InlineData("""{"outcomeUnknown":null,"rows":[1,2,3],"writtenCount":3}""")]
    [InlineData("""{"isError":true,"rows":[1,2,3]}""")]
    [InlineData("""{"isError":null,"rows":[1,2,3]}""")]
    [InlineData("""{"errorClass":"storage","rows":[1,2,3]}""")]
    [InlineData("""{"status":null,"rows":[1,2,3]}""")]
    [InlineData("""{"status":{},"rows":[1,2,3]}""")]
    public void FailureQueuedOrUncertainResultNeverBecomesZeroWrites(string payload)
    {
        var result = Json(payload);
        Unknown(AgentResultCounts.Observe("sql_readonly", "{}", result));
        Unknown(AgentResultCounts.Observe("platform_request",
            """{"method":"GET","path":"/products"}""", result));
    }

    [Fact]
    public void UnsupportedSourcesAndWriteRequestsCannotMintEvidenceFromPlausibleCounts()
    {
        var result = Json("""{"rows":[1,2,3],"items":[1,2,3],"totalCount":3,"writtenCount":3}""");
        foreach (var tool in new[] { "run_python", "mcp_remote_write", "unknown" })
            Unknown(AgentResultCounts.Observe(tool, "{}", result));
        Unknown(AgentResultCounts.Observe("platform_request",
            """{"method":"PATCH","path":"/products/bulk","body":{"productIds":[1,2,3]}}""", result));
        Unknown(AgentResultCounts.Observe("platform_request",
            """{"method":"GET","path":"/products/7"}""", result));
        Unknown(AgentResultCounts.Observe("platform_request", "not-json", result));
        Unknown(AgentResultCounts.Observe("sql_readonly", "{}",
            new { stdout = """{"rows":[1,2,3],"writtenCount":3}""" }));
    }

    [Theory]
    [InlineData("\"75\"")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("2147483648")]
    [InlineData("1e1000")]
    public void MalformedOptionalPlanEstimateIsUnknown_WithoutThrowingOrInvalidatingSafeSteps(string count)
    {
        var plan = ProposePlanReader.Read("""
            {"summary":"Compute","affectedCount":COUNT,"steps":[{"tool":"run_python","description":"Compute","args":{"code":"print(1)"}}]}
            """.Replace("COUNT", count));
        Assert.True(plan.HasContent);
        Assert.Null(plan.AffectedCount);
        Assert.Equal("unknown", plan.AffectedCountAccuracy);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(75)]
    [InlineData(int.MaxValue)]
    public void ValidPlanCountIsAnEstimate_NotAnExecutionFact(int count)
    {
        var plan = ProposePlanReader.Read("""
            {"summary":"Compute","affectedCount":COUNT,"steps":[{"tool":"run_python","description":"Compute","args":{"code":"print(1)"}}]}
            """.Replace("COUNT", count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal(count, plan.AffectedCount);
        Assert.Equal("estimated", plan.AffectedCountAccuracy);
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("null", true)]
    public void NamedExportDistinguishesExplicitZeroFromUnspecifiedScope(string count, bool valid)
    {
        var plan = ProposePlanReader.Read("""
            {"summary":"Export","affectedCount":COUNT,"steps":[{"tool":"platform_request","description":"Export selected","args":{"method":"POST","path":"/marketplace/connections/3/products/export-batch","body":{"productIds":[1]}}}]}
            """.Replace("COUNT", count));
        Assert.Equal(valid, plan.HasContent);
    }
}
