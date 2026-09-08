using System.Text;
using System.Text.Json;
using Delegat.Services.Ai.Agent;

namespace Delegat.Tests;

public class AgentToolResultTests
{
    [Fact]
    public void IsFailure_CabinetQuotaBodyWithoutErrorProperty_IsFailed()
    {
        var body = new
        {
            title = "Product limit reached (MAX_PRODUCTS=11)",
            status = 409,
            code = "product_limit_reached",
            data = new { limit = 11, used = 11, remaining = 0 },
        };

        Assert.True(AgentToolResult.IsFailure(body, "HTTP 409"));
    }

    [Fact]
    public void IsFailure_Http400Summary_IsFailed()
    {
        Assert.True(AgentToolResult.IsFailure(new { name = "X" }, "HTTP 400"));
        Assert.True(AgentToolResult.IsFailure(new { }, "HTTP 400 too large (5241532 bytes)"));
    }

    [Fact]
    public void IsFailure_OkCatalogPayload_IsNotFailed()
    {
        Assert.False(AgentToolResult.IsFailure(new { items = new[] { new { id = 1 } }, totalCount = 1 }, "HTTP 200"));
        Assert.False(AgentToolResult.IsFailure(new { id = 12, sku = "A" }, "HTTP 200"));
    }

    [Fact]
    public void InterpretResponse_Conflict_SetsErrorFromCode()
    {
        var json = """{"title":"Product limit reached (MAX_PRODUCTS=11)","status":409,"code":"product_limit_reached","data":{"limit":11,"used":11,"remaining":0}}""";
        var (result, summary) = CabinetRequestInvoker.InterpretResponse(409, Encoding.UTF8.GetBytes(json));
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.Equal("product_limit_reached", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal(409, doc.RootElement.GetProperty("status").GetInt32());
        Assert.True(AgentToolResult.IsFailure(result, summary));
        Assert.Contains("nothing was created", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HTTP 409", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void InterpretResponse_Validation400_FlattensFieldMessages()
    {
        var json = """
            {"title":"One or more validation errors occurred.","status":400,"errors":{"$.attributeValues[1].value":["The JSON value could not be converted to System.String. Path: $.attributeValues[1].value | LineNumber: 0 | BytePositionInLine: 12."]}}
            """;
        var (_, summary) = CabinetRequestInvoker.InterpretResponse(400, Encoding.UTF8.GetBytes(json));
        Assert.Contains("Characteristic values must be text", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("One or more validation errors occurred", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void InterpretResponse_Validation400_SetsErrorAndFailsPlan()
    {
        var json = """{"title":"One or more validation errors occurred.","status":400,"code":"validation_failed"}""";
        var (result, summary) = CabinetRequestInvoker.InterpretResponse(400, Encoding.UTF8.GetBytes(json));
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.Equal("validation_failed", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal(400, doc.RootElement.GetProperty("status").GetInt32());
        Assert.True(AgentToolResult.IsFailure(result, summary));
        Assert.DoesNotContain("Plan executed", AgentToolResult.FormatPlanOutcome(0, 1, [summary], "en"), StringComparison.Ordinal);
    }

    [Fact]
    public void InterpretResponse_OversizedProduct_SlmsFilesInsteadOfDropping()
    {
        var huge = new string('x', 300_000);
        var json = JsonSerializer.Serialize(new
        {
            id = 1,
            name = "Tank",
            sku = "_WA121",
            files = new[] { new { id = 9, data = huge } },
            attributeValues = new[] { new { attributeId = 146, value = "Brand" } },
        });
        Assert.True(json.Length > CabinetRequestInvoker.MaxResponseBytes);

        var (result, summary) = CabinetRequestInvoker.InterpretResponse(200, Encoding.UTF8.GetBytes(json));
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.False(doc.RootElement.TryGetProperty("error", out _));
        Assert.Equal(1, doc.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("Brand", doc.RootElement.GetProperty("attributeValues")[0].GetProperty("value").GetString());
        Assert.False(doc.RootElement.TryGetProperty("files", out _));
        Assert.Contains("trimmed", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatPlanOutcome_FailedCreate_DoesNotClaimSuccess()
    {
        var ru = AgentToolResult.FormatPlanOutcome(0, 1, ["Product limit reached — nothing was created."], "ru");
        Assert.Contains("Не получилось", ru, StringComparison.Ordinal);
        Assert.Contains("ничего не изменилось", ru, StringComparison.Ordinal);
        Assert.DoesNotContain("Plan executed", ru, StringComparison.Ordinal);

        var en = AgentToolResult.FormatPlanOutcome(0, 1, ["Product limit reached — nothing was created."], "en");
        Assert.Contains("Nothing was changed", en, StringComparison.Ordinal);
        Assert.DoesNotContain("Plan executed: 1 of 1", en, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatPlanOutcome_PartialSuccess_DoesNotClaimCatalogUnchanged()
    {
        var ru = AgentToolResult.FormatPlanOutcome(1, 2, ["Invalid barcode. EAN-8 (8 digits) or EAN-13 (13 digits) are allowed"], "ru");
        Assert.Contains("Сделано частично", ru, StringComparison.Ordinal);
        Assert.Contains("корзин", ru, StringComparison.Ordinal);
        Assert.DoesNotContain("ничего не изменилось", ru, StringComparison.Ordinal);

        var en = AgentToolResult.FormatPlanOutcome(1, 2, ["Invalid barcode. EAN-8 (8 digits) or EAN-13 (13 digits) are allowed"], "en");
        Assert.Contains("Partly done", en, StringComparison.Ordinal);
        Assert.Contains("trash", en, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Nothing was changed", en, StringComparison.Ordinal);
    }
}
