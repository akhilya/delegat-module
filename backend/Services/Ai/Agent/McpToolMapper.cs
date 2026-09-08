using System.Text.Json;

namespace Delegat.Services.Ai.Agent;

public static class McpToolMapper
{
    public const int MaxToolsPerServer = 32;

    public static AiTool? ToAiTool(int serverId, JsonElement tool)
    {
        var remote = tool.TryGetProperty("name", out var n) ? n.GetString() : null;
        if (string.IsNullOrWhiteSpace(remote)) return null;

        var description = tool.TryGetProperty("description", out var d) ? d.GetString() : null;
        var schema = new AiToolInputSchema { type = "object", properties = new(), required = new() };
        if (tool.TryGetProperty("inputSchema", out var input) && input.ValueKind == JsonValueKind.Object)
            schema = ReadSchema(input);

        return new AiTool
        {
            name = McpToolNames.ForLlm(serverId, remote),
            description = string.IsNullOrWhiteSpace(description) ? remote : description,
            input_schema = schema,
            McpRemoteName = remote,
        };
    }

    private static AiToolInputSchema ReadSchema(JsonElement input)
    {
        var schema = new AiToolInputSchema
        {
            type = ReadType(input) ?? "object",
            properties = new(),
            required = new(),
        };
        if (input.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in props.EnumerateObject())
                schema.properties[p.Name] = ReadProperty(p.Value);
        }
        if (input.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in req.EnumerateArray())
            {
                var name = item.GetString();
                if (!string.IsNullOrWhiteSpace(name)) schema.required.Add(name);
            }
        }
        return schema;
    }

    private static AiToolProperty ReadProperty(JsonElement el)
    {
        var prop = new AiToolProperty
        {
            type = ReadType(el) ?? "string",
            description = el.TryGetProperty("description", out var d) ? d.GetString() : null,
        };
        if (el.TryGetProperty("enum", out var en) && en.ValueKind == JsonValueKind.Array)
        {
            prop.@enum = en.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!)
                .ToList();
        }
        if (el.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object)
            prop.items = ReadProperty(items);
        if (el.TryGetProperty("properties", out var nested) && nested.ValueKind == JsonValueKind.Object)
        {
            prop.properties = new Dictionary<string, AiToolProperty>();
            foreach (var p in nested.EnumerateObject())
                prop.properties[p.Name] = ReadProperty(p.Value);
        }
        if (el.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array)
        {
            prop.required = req.EnumerateArray()
                .Select(x => x.GetString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .ToList();
        }
        return prop;
    }

    private static string? ReadType(JsonElement el)
    {
        if (!el.TryGetProperty("type", out var t)) return null;
        if (t.ValueKind == JsonValueKind.String) return t.GetString();
        if (t.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in t.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s && s != "null")
                    return s;
            }
        }
        return null;
    }
}
