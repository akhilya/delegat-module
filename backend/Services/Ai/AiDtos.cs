using System.Text.Json.Serialization;

namespace Delegat.Services.Ai;

// ---- Request DTOs ----

public class AiRequest
{
    public string model { get; set; } = null!;
    public int max_tokens { get; set; } = 8192;
    public string? system { get; set; }
    public List<AiMessage> messages { get; set; } = new();
    public List<AiTool>? tools { get; set; }
    public bool stream { get; set; }
    public string? user { get; set; } // session/conversation id - breaks KV-cache between sessions

    // Internal hint: eligible chat turns may opt into provider-side prompt caching.
    [JsonIgnore]
    public bool enable_prompt_cache { get; set; }
}

public class AiMessage
{
    public string role { get; set; } = null!; // "user" | "assistant"
    public object content { get; set; } = null!; // string or List<AiContentBlock>
}

public class AiTool
{
    public string name { get; set; } = null!;
    public string? description { get; set; }
    public AiToolInputSchema input_schema { get; set; } = null!;

    [JsonIgnore]
    public string? McpRemoteName { get; set; }
}

public class AiToolInputSchema
{
    public string type { get; set; } = "object";
    public Dictionary<string, AiToolProperty>? properties { get; set; }
    public List<string>? required { get; set; }

    // MCP schemas can contain JSON Schema keywords not represented by the legacy DTO.
    // Keep the source schema for provider serialization so constraints are not silently lost.
    [JsonIgnore]
    public System.Text.Json.JsonElement? RawSchema { get; set; }
}

public class AiToolProperty
{
    public string type { get; set; } = "string";
    public string? description { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? @enum { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AiToolProperty? items { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, AiToolProperty>? properties { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? required { get; set; }
}

// ---- Response DTOs ----

public class AiResponse
{
    public string id { get; set; } = null!;
    public string stop_reason { get; set; } = null!;
    public List<AiContentBlock> content { get; set; } = new();
}

public class AiContentBlock
{
    public string type { get; set; } = null!; // "text" | "tool_use"
    public string? text { get; set; }

    // tool_use
    public string? id { get; set; }
    public string? name { get; set; }
    public System.Text.Json.JsonElement? input { get; set; }
}

// ---- Streaming ----

public class AiStreamEvent
{
    public string type { get; set; } = null!;
    public AiStreamDelta? delta { get; set; }
    public AiContentBlock? content_block { get; set; }
    public AiStreamUsage? usage { get; set; }
    public int? index { get; set; }
    public string? error { get; set; }
    public string? errorCode { get; set; }
    public int? errorStatus { get; set; }
}

/// <summary>Provider-reported token usage, when available in the response stream.</summary>
public class AiStreamUsage
{
    public long? inputTokens { get; set; }
    public long? outputTokens { get; set; }
    public long? cacheReadInputTokens { get; set; }
    public long? cacheCreationInputTokens { get; set; }

    public void MergeLatest(AiStreamUsage? update)
    {
        if (update == null) return;
        inputTokens = Max(inputTokens, update.inputTokens);
        outputTokens = Max(outputTokens, update.outputTokens);
        cacheReadInputTokens = Max(cacheReadInputTokens, update.cacheReadInputTokens);
        cacheCreationInputTokens = Max(cacheCreationInputTokens, update.cacheCreationInputTokens);
    }

    private static long? Max(long? current, long? update) =>
        update.HasValue ? Math.Max(current ?? 0, update.Value) : current;
}

public class AiStreamDelta
{
    public string? type { get; set; }
    public string? text { get; set; }
    public string? partial_json { get; set; }
}

// ---- Chat API DTOs ----

public class ChatRequest
{
    public List<ChatMessage> messages { get; set; } = new();
    public int? modelId { get; set; }
}

public class ChatMessage
{
    public string role { get; set; } = null!;
    // Accepts both plain string and content-block arrays (tool_use / tool_result)
    public System.Text.Json.JsonElement content { get; set; }
}

// ---- Plan execution ----

/// <summary>One confirmed plan step, executed deterministically by the backend.</summary>
public class PlanStepInput
{
    public string Tool { get; set; } = "";
    public string? Description { get; set; }
    public System.Text.Json.JsonElement? Args { get; set; }
}

// ---- SSE event types ----

public static class SseEventType
{
    public const string Text = "text";
    public const string Tool = "tool";
    public const string Plan = "plan";
    public const string Error = "error";
    public const string Done = "done";
}
