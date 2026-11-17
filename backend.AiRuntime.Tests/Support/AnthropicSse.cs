namespace Delegat.AiRuntime.Tests.Support;

/// <summary>
/// Canned Anthropic /v1/messages SSE bodies for the M1 prototype. Mirrors the OpenAI
/// fixtures: text interleaved between two tool_use blocks, input_json_delta fragments
/// (three fragments for the plan call), message_start/message_delta usage.
/// </summary>
internal static class AnthropicSse
{
    /// <summary>
    /// Round 1: text block, then tool_use propose_plan (args in three input_json_delta
    /// fragments), then tool_use sibling_tool, stop_reason=tool_use.
    /// </summary>
    public const string ToolCallBatchResponse = """
        event: message_start
        data: {"type":"message_start","message":{"id":"msg_m1_r1","type":"message","role":"assistant","model":"claude-m1-test","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":9,"output_tokens":1}}}

        event: content_block_start
        data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Thinking "}}

        event: content_block_stop
        data: {"type":"content_block_stop","index":0}

        event: content_block_start
        data: {"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu_m1_plan","name":"propose_plan","input":{}}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{\"ti"}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"tle\":\"bulk"}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"-rename\"}"}}

        event: content_block_stop
        data: {"type":"content_block_stop","index":1}

        event: content_block_start
        data: {"type":"content_block_start","index":2,"content_block":{"type":"tool_use","id":"toolu_m1_sib","name":"sibling_tool","input":{}}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":2,"delta":{"type":"input_json_delta","partial_json":"{\"note\":\"s1\"}"}}

        event: content_block_stop
        data: {"type":"content_block_stop","index":2}

        event: message_delta
        data: {"type":"message_delta","delta":{"stop_reason":"tool_use","stop_sequence":null},"usage":{"output_tokens":21}}

        event: message_stop
        data: {"type":"message_stop"}

        """;

    /// <summary>Round 1 variant: a single tool_use for an unregistered function name.</summary>
    public const string UnknownToolResponse = """
        event: message_start
        data: {"type":"message_start","message":{"id":"msg_m1_unk","type":"message","role":"assistant","model":"claude-m1-test","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":6,"output_tokens":1}}}

        event: content_block_start
        data: {"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"toolu_m1_unk","name":"nonexistent_tool","input":{}}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{\"q\":1}"}}

        event: content_block_stop
        data: {"type":"content_block_stop","index":0}

        event: message_delta
        data: {"type":"message_delta","delta":{"stop_reason":"tool_use","stop_sequence":null},"usage":{"output_tokens":8}}

        event: message_stop
        data: {"type":"message_stop"}

        """;

    /// <summary>Round 2 / standalone: plain text, stop_reason=end_turn.</summary>
    public const string TextResponse = """
        event: message_start
        data: {"type":"message_start","message":{"id":"msg_m1_r2","type":"message","role":"assistant","model":"claude-m1-test","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":30,"output_tokens":1}}}

        event: content_block_start
        data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Plan acknowledged."}}

        event: content_block_stop
        data: {"type":"content_block_stop","index":0}

        event: message_delta
        data: {"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":6}}

        event: message_stop
        data: {"type":"message_stop"}

        """;

    /// <summary>Valid opening events, then the stream stalls (used with a hanging content).</summary>
    public const string PartialPrefix = """
        event: message_start
        data: {"type":"message_start","message":{"id":"msg_m1_cx","type":"message","role":"assistant","model":"claude-m1-test","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":11,"output_tokens":1}}}

        event: content_block_start
        data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

        """;

    /// <summary>Malformed: an event whose data payload is truncated mid-JSON, then EOF.</summary>
    public const string MalformedResponse = "event: message_start\ndata: {\"type\":\"message_start\",\"message\":{\"id\":\"msg_m1_bad\",\"content\":[\n\n";

    public const string ServerErrorBody = "{\"type\":\"error\",\"error\":{\"type\":\"api_error\",\"message\":\"m1 scripted 500\"}}";
}
