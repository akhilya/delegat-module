namespace Delegat.AiRuntime.Tests.Support;

/// <summary>
/// Canned OpenAI chat-completion SSE bodies for the M1 prototype. Kept small but realistic:
/// tool-call arguments are fragmented across multiple deltas and a text delta is interleaved
/// between tool-call deltas to exercise the SDK/M.E.AI fragment assembly.
/// </summary>
internal static class OpenAiSse
{
    /// <summary>
    /// Round 1: one assistant turn containing a text delta interleaved between TWO tool calls
    /// (propose_plan + sibling_tool) whose JSON arguments arrive in three fragments each.
    /// Full plan arguments after assembly: {"title":"bulk-rename"}.
    /// </summary>
    public const string ToolCallBatchResponse = """
        data: {"id":"chatcmpl-m1-r1","object":"chat.completion.chunk","created":1730000000,"model":"gpt-m1-test","choices":[{"index":0,"delta":{"role":"assistant","tool_calls":[{"index":0,"id":"call_plan","type":"function","function":{"name":"propose_plan","arguments":""}}]},"finish_reason":null}]}

        data: {"id":"chatcmpl-m1-r1","object":"chat.completion.chunk","created":1730000000,"model":"gpt-m1-test","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"ti"}}]},"finish_reason":null}]}

        data: {"id":"chatcmpl-m1-r1","object":"chat.completion.chunk","created":1730000000,"model":"gpt-m1-test","choices":[{"index":0,"delta":{"content":"Thinking "},"finish_reason":null}]}

        data: {"id":"chatcmpl-m1-r1","object":"chat.completion.chunk","created":1730000000,"model":"gpt-m1-test","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"tle\":\"bulk"}}]},"finish_reason":null}]}

        data: {"id":"chatcmpl-m1-r1","object":"chat.completion.chunk","created":1730000000,"model":"gpt-m1-test","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"-rename\"}"}}]},"finish_reason":null}]}

        data: {"id":"chatcmpl-m1-r1","object":"chat.completion.chunk","created":1730000000,"model":"gpt-m1-test","choices":[{"index":0,"delta":{"tool_calls":[{"index":1,"id":"call_sib","type":"function","function":{"name":"sibling_tool","arguments":""}}]},"finish_reason":null}]}

        data: {"id":"chatcmpl-m1-r1","object":"chat.completion.chunk","created":1730000000,"model":"gpt-m1-test","choices":[{"index":0,"delta":{"tool_calls":[{"index":1,"function":{"arguments":"{\"no"}}]},"finish_reason":null}]}

        data: {"id":"chatcmpl-m1-r1","object":"chat.completion.chunk","created":1730000000,"model":"gpt-m1-test","choices":[{"index":0,"delta":{"tool_calls":[{"index":1,"function":{"arguments":"te\":"}}]},"finish_reason":null}]}

        data: {"id":"chatcmpl-m1-r1","object":"chat.completion.chunk","created":1730000000,"model":"gpt-m1-test","choices":[{"index":0,"delta":{"tool_calls":[{"index":1,"function":{"arguments":"\"s1\"}"}}]},"finish_reason":null}]}

        data: {"id":"chatcmpl-m1-r1","object":"chat.completion.chunk","created":1730000000,"model":"gpt-m1-test","choices":[{"index":0,"delta":{},"finish_reason":"tool_calls"}]}

        data: {"id":"chatcmpl-m1-r1","object":"chat.completion.chunk","created":1730000000,"model":"gpt-m1-test","choices":[],"usage":{"prompt_tokens":11,"completion_tokens":22,"total_tokens":33}}

        data: [DONE]

        """;

    /// <summary>Round 1 variant: a single call to a function the agent never registered.</summary>
    public const string UnknownToolResponse = """
        data: {"id":"chatcmpl-m1-unk","object":"chat.completion.chunk","created":1730000000,"model":"gpt-m1-test","choices":[{"index":0,"delta":{"role":"assistant","tool_calls":[{"index":0,"id":"call_unk","type":"function","function":{"name":"nonexistent_tool","arguments":""}}]},"finish_reason":null}]}

        data: {"id":"chatcmpl-m1-unk","object":"chat.completion.chunk","created":1730000000,"model":"gpt-m1-test","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"q\":1}"}}]},"finish_reason":null}]}

        data: {"id":"chatcmpl-m1-unk","object":"chat.completion.chunk","created":1730000000,"model":"gpt-m1-test","choices":[{"index":0,"delta":{},"finish_reason":"tool_calls"}]}

        data: {"id":"chatcmpl-m1-unk","object":"chat.completion.chunk","created":1730000000,"model":"gpt-m1-test","choices":[],"usage":{"prompt_tokens":7,"completion_tokens":5,"total_tokens":12}}

        data: [DONE]

        """;

    /// <summary>Round 1 variant: one tool call only (no batch), fragmented args.</summary>
    public const string SingleToolCallResponse = """
        data: {"id":"chatcmpl-m1-s1","object":"chat.completion.chunk","created":1730000000,"model":"gpt-m1-test","choices":[{"index":0,"delta":{"role":"assistant","tool_calls":[{"index":0,"id":"call_plan","type":"function","function":{"name":"propose_plan","arguments":""}}]},"finish_reason":null}]}

        data: {"id":"chatcmpl-m1-s1","object":"chat.completion.chunk","created":1730000000,"model":"gpt-m1-test","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"title\":"}}]},"finish_reason":null}]}

        data: {"id":"chatcmpl-m1-s1","object":"chat.completion.chunk","created":1730000000,"model":"gpt-m1-test","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"solo\"}"}}]},"finish_reason":null}]}

        data: {"id":"chatcmpl-m1-s1","object":"chat.completion.chunk","created":1730000000,"model":"gpt-m1-test","choices":[{"index":0,"delta":{},"finish_reason":"tool_calls"}]}

        data: {"id":"chatcmpl-m1-s1","object":"chat.completion.chunk","created":1730000000,"model":"gpt-m1-test","choices":[],"usage":{"prompt_tokens":5,"completion_tokens":4,"total_tokens":9}}

        data: [DONE]

        """;

    /// <summary>Round 2 / standalone: plain text finish, no tool calls.</summary>
    public const string TextResponse = """
        data: {"id":"chatcmpl-m1-r2","object":"chat.completion.chunk","created":1730000000,"model":"gpt-m1-test","choices":[{"index":0,"delta":{"role":"assistant","content":"Plan acknowledged."},"finish_reason":null}]}

        data: {"id":"chatcmpl-m1-r2","object":"chat.completion.chunk","created":1730000000,"model":"gpt-m1-test","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}

        data: {"id":"chatcmpl-m1-r2","object":"chat.completion.chunk","created":1730000000,"model":"gpt-m1-test","choices":[],"usage":{"prompt_tokens":40,"completion_tokens":6,"total_tokens":46}}

        data: [DONE]

        """;

    /// <summary>One valid text chunk, then the stream stalls (used with a hanging content).</summary>
    public const string PartialPrefix = """
        data: {"id":"chatcmpl-m1-cx","object":"chat.completion.chunk","created":1730000000,"model":"gpt-m1-test","choices":[{"index":0,"delta":{"role":"assistant","content":"Thinking"},"finish_reason":null}]}

        """;

    /// <summary>Malformed: an event whose data payload is not valid JSON, then EOF.</summary>
    public const string MalformedResponse = "data: {\"id\":\"chatcmpl-m1-bad\",\"choices\":[{\"index\":0,\"delta\":\n\n";

    public const string ServerErrorBody = "{\"error\":{\"message\":\"m1 scripted 500\",\"type\":\"server_error\"}}";
}
