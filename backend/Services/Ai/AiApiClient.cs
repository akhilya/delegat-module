using System.ClientModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Delegat.Models.Ai;
using OpenAI;
using OpenAI.Chat;

namespace Delegat.Services.Ai;

/// <summary>
/// Transport layer for AI providers, built on the official SDKs:
/// - Anthropic (anthropic-sdk-csharp) for provider == Anthropic
/// - OpenAI (openai-dotnet) for provider == OpenAiCompatible (also LM Studio / Ollama / vLLM)
///
/// The public surface is provider-agnostic: requests come in as Anthropic-shaped
/// <see cref="AiRequest"/> DTOs and stream events go out as Anthropic-shaped
/// <see cref="AiStreamEvent"/>s - the agentic loop in AiChatService is unaware of the provider.
/// </summary>
public class AiApiClient : IAiApiClient
{
    private readonly ILogger<AiApiClient> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public AiApiClient(HttpClient http, ILogger<AiApiClient> logger)
    {
        // http is kept for DI compatibility (typed-client registration); SDKs manage their own transport
        _logger = logger;
    }

    /// <summary>
    /// Creates an HttpClient routed through a proxy URI (http://... or socks5://user:pass@host:port).
    /// WebProxy does NOT parse credentials from the URI automatically - they must be set explicitly.
    /// SocketsHttpHandler supports SOCKS5 in .NET 6+; credentials are passed via WebProxy.Credentials.
    /// Returns null when proxyUrl is absent so callers can skip the proxy branch.
    /// </summary>
    public static HttpClient? CreateProxiedClient(string? proxyUrl)
    {
        if (string.IsNullOrWhiteSpace(proxyUrl)) return null;

        var uri = new Uri(proxyUrl);

        // Strip credentials from the address - WebProxy ignores them in the URI and needs them set separately
        var proxyAddress = new Uri($"{uri.Scheme}://{uri.Host}:{uri.Port}");
        var proxy = new System.Net.WebProxy(proxyAddress);

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var sep = uri.UserInfo.IndexOf(':');
            var user = sep >= 0 ? Uri.UnescapeDataString(uri.UserInfo[..sep]) : Uri.UnescapeDataString(uri.UserInfo);
            var pass = sep >= 0 ? Uri.UnescapeDataString(uri.UserInfo[(sep + 1)..]) : "";
            proxy.Credentials = new System.Net.NetworkCredential(user, pass);
        }

        var handler = new System.Net.Http.SocketsHttpHandler
        {
            Proxy = proxy,
            UseProxy = true,
        };
        return new HttpClient(handler);
    }

    public Task<AiResponse> SendAsync(AiModelConfig model, AiRequest request, CancellationToken ct)
    {
        request.model = model.modelId;
        return model.provider == AiModelProvider.Anthropic
            ? SendAnthropicAsync(model, request, ct)
            : SendOpenAiAsync(model, request, ct);
    }

    public async IAsyncEnumerable<AiStreamEvent> StreamAsync(
        AiModelConfig model, AiRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        request.model = model.modelId;
        var inner = model.provider == AiModelProvider.Anthropic
            ? StreamAnthropicAsync(model, request, ct)
            : StreamOpenAiAsync(model, request, ct);

        // yield is not allowed inside try/catch bodies - drive the enumerator manually so a
        // provider exception becomes an "error" event instead of tearing down the SSE response
        var enumerator = inner.GetAsyncEnumerator(ct);
        try
        {
            while (true)
            {
                AiStreamEvent? ev = null;
                AiStreamEvent? errorEv = null;
                var has = false;
                try
                {
                    has = await enumerator.MoveNextAsync();
                    if (has) ev = enumerator.Current;
                }
                catch (OperationCanceledException) { has = false; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AI stream error ({Provider})", model.provider);
                    errorEv = new AiStreamEvent { type = "error", error = ex.Message };
                }

                if (errorEv != null)
                {
                    yield return errorEv;
                    yield break;
                }
                if (!has) break;
                yield return ev!;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    // =====================================================================
    // OpenAI-compatible path (official OpenAI .NET SDK)
    // =====================================================================

    private ChatClient BuildOpenAiClient(AiModelConfig model)
    {
        // SDK resolves operation paths relative to the endpoint, which must end with /v1
        // (api.openai.com/v1, LM Studio http://host:1234/v1, etc.)
        var baseUrl = model.apiUrl.TrimEnd('/');
        if (baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            baseUrl = baseUrl[..^"/chat/completions".Length].TrimEnd('/');
        if (!baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            baseUrl += "/v1";

        // Local servers don't need a key, but ApiKeyCredential rejects empty strings
        var key = string.IsNullOrWhiteSpace(model.apiKeyEncrypted) ? "not-needed" : model.apiKeyEncrypted;

        var options = new OpenAIClientOptions { Endpoint = new Uri(baseUrl) };
        var proxied = CreateProxiedClient(model.proxyUrl);
        if (proxied != null)
            options.Transport = new System.ClientModel.Primitives.HttpClientPipelineTransport(proxied);

        return new ChatClient(model.modelId, new ApiKeyCredential(key), options);
    }

    private ChatCompletionOptions BuildOpenAiOptions(AiRequest request)
    {
        var options = new ChatCompletionOptions
        {
            // Serialized as max_completion_tokens - required by newer OpenAI models,
            // accepted by modern OpenAI-compatible servers
            MaxOutputTokenCount = request.max_tokens,
        };
        if (!string.IsNullOrEmpty(request.user))
            options.EndUserId = request.user;

        if (request.tools != null)
        {
            foreach (var tool in request.tools)
            {
                options.Tools.Add(ChatTool.CreateFunctionTool(
                    tool.name,
                    tool.description,
                    BinaryData.FromString(JsonSerializer.Serialize(ToolSchemaElement(tool.input_schema), _jsonOptions))));
            }
        }
        return options;
    }

    // Converts our Anthropic-shaped message list (string content, or content-block arrays with
    // tool_use / tool_result) into OpenAI chat messages. Content is walked via JSON because
    // AiMessage.content holds anonymous objects.
    private List<OpenAI.Chat.ChatMessage> BuildOpenAiMessages(AiRequest request)
    {
        var messages = new List<OpenAI.Chat.ChatMessage>();
        if (!string.IsNullOrEmpty(request.system))
            messages.Add(new SystemChatMessage(request.system));

        var rawMessages = JsonSerializer.Serialize(request.messages, _jsonOptions);
        using var doc = JsonDocument.Parse(rawMessages);

        foreach (var msgEl in doc.RootElement.EnumerateArray())
        {
            var role = msgEl.GetProperty("role").GetString()!;
            var contentEl = msgEl.GetProperty("content");

            if (contentEl.ValueKind == JsonValueKind.String)
            {
                var text = contentEl.GetString() ?? "";
                messages.Add(role == "assistant"
                    ? new AssistantChatMessage(text)
                    : new UserChatMessage(text));
                continue;
            }

            if (contentEl.ValueKind != JsonValueKind.Array) continue;
            var blocks = contentEl.EnumerateArray().ToList();

            // tool_result blocks → one "tool" role message per block
            var toolResults = blocks.Where(b => BlockType(b) == "tool_result").ToList();
            if (toolResults.Count > 0)
            {
                foreach (var b in toolResults)
                {
                    var callId = b.TryGetProperty("tool_use_id", out var tid) ? tid.GetString() ?? "" : "";
                    var body = b.TryGetProperty("content", out var cv) ? cv.GetString() ?? "" : "";
                    messages.Add(new ToolChatMessage(callId, body));
                }
                continue;
            }

            // assistant turn: optional text + tool_use blocks → AssistantChatMessage with tool calls
            var toolUses = blocks.Where(b => BlockType(b) == "tool_use").ToList();
            var textParts = blocks.Where(b => BlockType(b) == "text")
                .Select(b => b.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "")
                .Where(t => t.Length > 0)
                .ToList();

            if (toolUses.Count > 0)
            {
                var calls = toolUses.Select((b, i) => ChatToolCall.CreateFunctionToolCall(
                    b.TryGetProperty("id", out var bid) ? bid.GetString() ?? $"call_{i}" : $"call_{i}",
                    b.TryGetProperty("name", out var bn) ? bn.GetString() ?? "" : "",
                    BinaryData.FromString(b.TryGetProperty("input", out var inp) ? inp.GetRawText() : "{}")))
                    .ToList();

                var assistant = new AssistantChatMessage(calls);
                foreach (var t in textParts)
                    assistant.Content.Add(ChatMessageContentPart.CreateTextPart(t));
                messages.Add(assistant);
                continue;
            }

            var joined = string.Join("\n", textParts);
            messages.Add(role == "assistant"
                ? new AssistantChatMessage(joined)
                : new UserChatMessage(joined));
        }
        return messages;

        static string? BlockType(JsonElement b)
            => b.TryGetProperty("type", out var t) ? t.GetString() : null;
    }

    private async Task<AiResponse> SendOpenAiAsync(AiModelConfig model, AiRequest request, CancellationToken ct)
    {
        var client = BuildOpenAiClient(model);
        var messages = BuildOpenAiMessages(request);
        var options = BuildOpenAiOptions(request);

        try
        {
            ChatCompletion completion = await client.CompleteChatAsync(messages, options, ct);

            var response = new AiResponse
            {
                id = completion.Id ?? Guid.NewGuid().ToString("N"),
                stop_reason = completion.FinishReason == ChatFinishReason.ToolCalls ? "tool_use" : "end_turn",
            };
            var text = string.Concat(completion.Content
                .Where(p => p.Kind == ChatMessageContentPartKind.Text)
                .Select(p => p.Text));
            if (text.Length > 0)
                response.content.Add(new AiContentBlock { type = "text", text = text });

            foreach (var call in completion.ToolCalls)
            {
                using var argsDoc = JsonDocument.Parse(
                    call.FunctionArguments.ToString() is { Length: > 0 } s ? s : "{}");
                response.content.Add(new AiContentBlock
                {
                    type = "tool_use",
                    id = call.Id,
                    name = NormalizeToolName(call.FunctionName),
                    input = argsDoc.RootElement.Clone(),
                });
            }
            return response;
        }
        catch (ClientResultException ex)
        {
            _logger.LogError(ex, "OpenAI API error {Status}", ex.Status);
            throw new HttpRequestException($"HTTP {ex.Status}: {ex.Message}");
        }
    }

    private async IAsyncEnumerable<AiStreamEvent> StreamOpenAiAsync(
        AiModelConfig model, AiRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var client = BuildOpenAiClient(model);
        var messages = BuildOpenAiMessages(request);
        var options = BuildOpenAiOptions(request);

        // Per-index accumulation of streamed tool calls. The SDK parses the wire format;
        // we only assemble fragments (id/name arrive on the first update, args in pieces)
        // and translate them into Anthropic-shaped block events for the agentic loop.
        var toolIds = new Dictionary<int, string>();
        var toolNames = new Dictionary<int, string>();
        var toolArgs = new Dictionary<int, System.Text.StringBuilder>();
        int? openIndex = null;

        await foreach (StreamingChatCompletionUpdate update in
            client.CompleteChatStreamingAsync(messages, options, ct))
        {
            foreach (var part in update.ContentUpdate)
            {
                if (part.Kind == ChatMessageContentPartKind.Text && part.Text is { Length: > 0 } txt)
                {
                    yield return new AiStreamEvent
                    {
                        type = "content_block_delta",
                        delta = new AiStreamDelta { type = "text_delta", text = txt }
                    };
                }
            }

            if (update.Usage is { } tokenUsage)
            {
                yield return new AiStreamEvent
                {
                    type = "usage",
                    usage = new AiStreamUsage
                    {
                        inputTokens = tokenUsage.InputTokenCount,
                        outputTokens = tokenUsage.OutputTokenCount,
                        cacheReadInputTokens = tokenUsage.InputTokenDetails?.CachedTokenCount,
                    },
                };
            }

            foreach (StreamingChatToolCallUpdate tc in update.ToolCallUpdates)
            {
                var index = tc.Index;
                if (!string.IsNullOrEmpty(tc.ToolCallId)) toolIds[index] = tc.ToolCallId;
                if (!string.IsNullOrEmpty(tc.FunctionName)) toolNames[index] = NormalizeToolName(tc.FunctionName);
                if (tc.FunctionArgumentsUpdate is { } args && args.ToArray().Length > 0)
                    (toolArgs.TryGetValue(index, out var sb) ? sb : toolArgs[index] = new()).Append(args.ToString());

                // Open the block as soon as the name is known; close the previous one first
                if (openIndex != index && toolNames.ContainsKey(index))
                {
                    if (openIndex.HasValue)
                    {
                        foreach (var evt in CloseToolBlock(openIndex.Value, toolArgs))
                            yield return evt;
                    }
                    openIndex = index;
                    yield return new AiStreamEvent
                    {
                        type = "content_block_start",
                        content_block = new AiContentBlock
                        {
                            type = "tool_use",
                            id = toolIds.TryGetValue(index, out var tid) ? tid : $"call_{index}",
                            name = toolNames[index],
                        }
                    };
                }
            }

            if (update.FinishReason != null)
            {
                if (openIndex.HasValue)
                {
                    foreach (var evt in CloseToolBlock(openIndex.Value, toolArgs))
                        yield return evt;
                    openIndex = null;
                }
                yield return new AiStreamEvent { type = "message_stop" };
            }
        }

        // Stream ended without a finish_reason chunk - still close what we have
        if (openIndex.HasValue)
        {
            foreach (var evt in CloseToolBlock(openIndex.Value, toolArgs))
                yield return evt;
            yield return new AiStreamEvent { type = "message_stop" };
        }
    }

    private static IEnumerable<AiStreamEvent> CloseToolBlock(
        int index, Dictionary<int, System.Text.StringBuilder> toolArgs)
    {
        var args = toolArgs.TryGetValue(index, out var sb) ? sb.ToString() : "";
        if (args.Length > 0)
        {
            yield return new AiStreamEvent
            {
                type = "content_block_delta",
                delta = new AiStreamDelta { type = "input_json_delta", partial_json = args }
            };
        }
        yield return new AiStreamEvent { type = "content_block_stop" };
    }

    // =====================================================================
    // Anthropic path (official anthropic-sdk-csharp)
    // =====================================================================

    internal Anthropic.AnthropicClient BuildAnthropicClient(AiModelConfig model, HttpClient? httpClient = null)
    {
        // Keep custom path prefixes (e.g. DeepSeek's /anthropic); the SDK appends /v1/messages.
        var baseUrl = GetAnthropicBaseUrl(model.apiUrl);

        var transport = CreateProxiedClient(model.proxyUrl) ?? httpClient;

        var apiKey = model.apiKeyEncrypted ?? string.Empty;

        return (baseUrl, transport) switch
        {
            (null, null) => new Anthropic.AnthropicClient { ApiKey = apiKey },
            (null, not null) => new Anthropic.AnthropicClient { ApiKey = apiKey, HttpClient = transport },
            (not null, null) => new Anthropic.AnthropicClient { ApiKey = apiKey, BaseUrl = baseUrl },
            (not null, not null) => new Anthropic.AnthropicClient
            {
                ApiKey = apiKey,
                BaseUrl = baseUrl,
                HttpClient = transport,
            },
        };
    }

    internal static string NormalizeAnthropicBaseUrl(string apiUrl)
    {
        var baseUrl = new Uri(apiUrl).GetLeftPart(UriPartial.Path).TrimEnd('/');
        foreach (var suffix in new[] { "/v1/messages", "/messages", "/v1" })
        {
            if (baseUrl.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = baseUrl[..^suffix.Length].TrimEnd('/');
                break;
            }
        }

        return baseUrl;
    }

    private static string? GetAnthropicBaseUrl(string apiUrl)
    {
        var uri = new Uri(apiUrl);
        var baseUrl = NormalizeAnthropicBaseUrl(apiUrl);
        var origin = uri.GetLeftPart(UriPartial.Authority);

        // Let the SDK use its default endpoint for Anthropic's canonical host.
        if (uri.Host.Equals("api.anthropic.com", StringComparison.OrdinalIgnoreCase)
            && baseUrl.Equals(origin, StringComparison.OrdinalIgnoreCase))
            return null;

        return baseUrl;
    }

    internal Anthropic.Models.Messages.MessageCreateParams BuildAnthropicParams(
        AiRequest request,
        AiModelConfig? model = null)
    {
        var messageParams = new List<Anthropic.Models.Messages.MessageParam>();
        var rawMessages = JsonSerializer.Serialize(request.messages, _jsonOptions);
        using var doc = JsonDocument.Parse(rawMessages);

        foreach (var msgEl in doc.RootElement.EnumerateArray())
        {
            var roleStr = msgEl.GetProperty("role").GetString()!;
            var isAssistant = roleStr == "assistant";
            var contentEl = msgEl.GetProperty("content");

            if (contentEl.ValueKind == JsonValueKind.String)
            {
                var content = contentEl.GetString() ?? "";
                messageParams.Add(isAssistant
                    ? new Anthropic.Models.Messages.AssistantMessageParam { Content = content }
                    : new Anthropic.Models.Messages.UserMessageParam { Content = content });
                continue;
            }

            var blocks = new List<Anthropic.Models.Messages.ContentBlockParam>();
            foreach (var b in contentEl.EnumerateArray())
            {
                var type = b.TryGetProperty("type", out var t) ? t.GetString() : null;
                switch (type)
                {
                    case "text":
                        blocks.Add(new Anthropic.Models.Messages.TextBlockParam
                        {
                            Text = b.GetProperty("text").GetString() ?? ""
                        });
                        break;
                    case "tool_use":
                        blocks.Add(new Anthropic.Models.Messages.ToolUseBlockParam
                        {
                            ID = b.GetProperty("id").GetString() ?? "",
                            Name = b.GetProperty("name").GetString() ?? "",
                            Input = ToJsonDictionary(b.TryGetProperty("input", out var inp) ? inp : default),
                        });
                        break;
                    case "tool_result":
                        blocks.Add(new Anthropic.Models.Messages.ToolResultBlockParam
                        {
                            ToolUseID = b.GetProperty("tool_use_id").GetString() ?? "",
                            Content = b.TryGetProperty("content", out var cv) ? cv.GetString() ?? "" : "",
                        });
                        break;
                }
            }
            messageParams.Add(isAssistant
                ? new Anthropic.Models.Messages.AssistantMessageParam { Content = blocks }
                : new Anthropic.Models.Messages.UserMessageParam { Content = blocks });
        }

        var systemPrompt = string.IsNullOrEmpty(request.system)
            ? null
            : (Anthropic.Models.Messages.MessageCreateParamsSystem)request.system!;
        var cacheToolDefinitions = model != null && IsAnthropicPromptCacheEnabled(request, model)
            && request.tools is { Count: > 0 };
        // A breakpoint after the last tool caches the unchanged system + tool prefix,
        // before per-round messages. Keep it opt-in: one-round turns pay cache-write cost
        // without a same-turn cache read.
        var anthropicTools = request.tools?.Select((toolConfig, index) =>
        {
            var cacheThisTool = cacheToolDefinitions && index == request.tools!.Count - 1;
            return (Anthropic.Models.Messages.ToolUnion)new Anthropic.Models.Messages.Tool
            {
                Name = toolConfig.name,
                Description = toolConfig.description,
                InputSchema = Anthropic.Models.Messages.InputSchema.FromRawUnchecked(
                    ToJsonDictionary(ToolSchemaElement(toolConfig.input_schema))),
                CacheControl = cacheThisTool
                    ? new Anthropic.Models.Messages.CacheControlEphemeral
                    {
                        Ttl = Anthropic.Models.Messages.Ttl.Ttl5m,
                    }
                    : null,
            };
        }).ToList();

        return new Anthropic.Models.Messages.MessageCreateParams
        {
            Model = request.model,
            MaxTokens = request.max_tokens,
            Messages = messageParams,
            System = systemPrompt,
            Metadata = string.IsNullOrEmpty(request.user)
                ? null
                : new Anthropic.Models.Messages.Metadata { UserID = request.user },
            Tools = anthropicTools,
        };
    }

    private static bool IsAnthropicPromptCacheEnabled(AiRequest request, AiModelConfig model)
    {
        if (!request.enable_prompt_cache
            || !bool.TryParse(Environment.GetEnvironmentVariable("AI_ANTHROPIC_PROMPT_CACHE"), out var enabled)
            || !enabled
            || !Uri.TryCreate(model.apiUrl, UriKind.Absolute, out var modelEndpoint)
            || !Uri.TryCreate(
                Environment.GetEnvironmentVariable("AI_ANTHROPIC_PROMPT_CACHE_ENDPOINT"),
                UriKind.Absolute,
                out var pilotEndpoint))
            return false;

        // Require an explicit origin allowlist so enabling the feature for a pilot
        // endpoint cannot silently turn it on for every Anthropic model in the app.
        return string.Equals(
            modelEndpoint.GetLeftPart(UriPartial.Authority),
            pilotEndpoint.GetLeftPart(UriPartial.Authority),
            StringComparison.OrdinalIgnoreCase);
    }

    // Some OpenAI-compatible models return tool names with a "functions." prefix (legacy Function
    // Calling format). Strip it so the dispatcher matches our tool registry.
    private static string NormalizeToolName(string? name)
    {
        const string prefix = "functions.";
        return name != null && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? name[prefix.Length..]
            : name ?? "";
    }

    private static Dictionary<string, JsonElement> ToJsonDictionary(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return new();
        return element.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    internal static JsonElement ToolSchemaElement(AiToolInputSchema schema) =>
        schema.RawSchema is { } raw
            ? raw.Clone()
            : JsonSerializer.SerializeToElement(schema, _jsonOptions);

    private async Task<AiResponse> SendAnthropicAsync(AiModelConfig model, AiRequest request, CancellationToken ct)
    {
        var client = BuildAnthropicClient(model);
        var createParams = BuildAnthropicParams(request, model);

        var message = await client.Messages.Create(createParams, cancellationToken: ct);

        var response = new AiResponse
        {
            id = message.ID,
            stop_reason = message.StopReason?.ToString() ?? "end_turn",
        };
        foreach (var block in message.Content)
        {
            if (block.TryPickText(out var textBlock))
            {
                response.content.Add(new AiContentBlock { type = "text", text = textBlock.Text });
            }
            else if (block.TryPickToolUse(out var toolUse))
            {
                response.content.Add(new AiContentBlock
                {
                    type = "tool_use",
                    id = toolUse.ID,
                    name = toolUse.Name,
                    input = JsonSerializer.SerializeToElement(toolUse.Input, _jsonOptions),
                });
            }
        }
        return response;
    }

    private async IAsyncEnumerable<AiStreamEvent> StreamAnthropicAsync(
        AiModelConfig model, AiRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var client = BuildAnthropicClient(model);
        var createParams = BuildAnthropicParams(request, model);

        await foreach (var streamEvent in client.Messages.CreateStreaming(createParams, cancellationToken: ct))
        {
            // Usage events are kept separate from content so the model loop can log
            // token counts without exposing them to the user-facing SSE stream.
            if (streamEvent.TryPickStart(out var messageStart))
            {
                var usage = messageStart.Message.Usage;
                yield return new AiStreamEvent
                {
                    type = "usage",
                    usage = new AiStreamUsage
                    {
                        inputTokens = usage.InputTokens,
                        outputTokens = usage.OutputTokens,
                        cacheReadInputTokens = usage.CacheReadInputTokens,
                        cacheCreationInputTokens = usage.CacheCreationInputTokens,
                    },
                };
            }
            else if (streamEvent.TryPickDelta(out var messageDelta))
            {
                var usage = messageDelta.Usage;
                yield return new AiStreamEvent
                {
                    type = "usage",
                    usage = new AiStreamUsage
                    {
                        inputTokens = usage.InputTokens,
                        outputTokens = usage.OutputTokens,
                        cacheReadInputTokens = usage.CacheReadInputTokens,
                        cacheCreationInputTokens = usage.CacheCreationInputTokens,
                    },
                };
            }
            else if (streamEvent.TryPickContentBlockStart(out var start))
            {
                var block = start.ContentBlock.TryPickToolUse(out var toolUse)
                    ? new AiContentBlock { type = "tool_use", id = toolUse.ID, name = toolUse.Name }
                    : new AiContentBlock { type = "text" };
                yield return new AiStreamEvent { type = "content_block_start", content_block = block };
            }
            else if (streamEvent.TryPickContentBlockDelta(out var deltaEvent))
            {
                if (deltaEvent.Delta.TryPickText(out var textDelta))
                {
                    yield return new AiStreamEvent
                    {
                        type = "content_block_delta",
                        delta = new AiStreamDelta { type = "text_delta", text = textDelta.Text }
                    };
                }
                else if (deltaEvent.Delta.TryPickInputJson(out var jsonDelta))
                {
                    yield return new AiStreamEvent
                    {
                        type = "content_block_delta",
                        delta = new AiStreamDelta { type = "input_json_delta", partial_json = jsonDelta.PartialJson }
                    };
                }
            }
            else if (streamEvent.TryPickContentBlockStop(out _))
            {
                yield return new AiStreamEvent { type = "content_block_stop" };
            }
            else if (streamEvent.TryPickStop(out _))
            {
                yield return new AiStreamEvent { type = "message_stop" };
            }
        }
    }
}
