using System.Text.Json;
using Delegat.Services.Ai;
using Delegat.Services.Ai.Agent;

namespace Delegat.Tests.Ai;

public class McpToolMapperTests
{
    [Fact]
    public void ToAiTool_PreservesCompleteJsonSchemaForProviderSerialization()
    {
        using var source = JsonDocument.Parse("""
            {
              "name": "update_record",
              "inputSchema": {
                "type": "object",
                "properties": {
                  "optionalName": { "type": ["string", "null"], "minLength": 2 },
                  "score": { "type": "number", "minimum": 0, "maximum": 10 },
                  "choice": { "type": "integer", "enum": [1, 2] },
                  "nested": {
                    "type": "object",
                    "properties": { "enabled": { "type": "boolean" } },
                    "required": ["enabled"]
                  }
                },
                "required": ["score"],
                "additionalProperties": false
              }
            }
            """);

        var tool = McpToolMapper.ToAiTool(4, source.RootElement);

        Assert.NotNull(tool);
        Assert.Contains("score", tool.input_schema.required!);

        var schema = AiApiClient.ToolSchemaElement(tool.input_schema);
        var optionalNameType = schema.GetProperty("properties").GetProperty("optionalName").GetProperty("type");
        Assert.Equal(JsonValueKind.Array, optionalNameType.ValueKind);
        Assert.Equal("null", optionalNameType[1].GetString());
        Assert.Equal(2, schema.GetProperty("properties").GetProperty("optionalName").GetProperty("minLength").GetInt32());
        Assert.Equal(1, schema.GetProperty("properties").GetProperty("choice").GetProperty("enum")[0].GetInt32());
        Assert.Equal("boolean", schema.GetProperty("properties").GetProperty("nested")
            .GetProperty("properties").GetProperty("enabled").GetProperty("type").GetString());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public void ToolSchemaElement_BuiltInToolsStillSerializeFromDto()
    {
        var schema = AiApiClient.ToolSchemaElement(new AiToolInputSchema
        {
            type = "object",
            properties = new Dictionary<string, AiToolProperty>
            {
                ["query"] = new() { type = "string" },
            },
            required = ["query"],
        });

        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.Equal("string", schema.GetProperty("properties").GetProperty("query").GetProperty("type").GetString());
        Assert.Equal("query", schema.GetProperty("required")[0].GetString());
    }
}
