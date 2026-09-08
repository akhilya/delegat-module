using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Delegat.Models.Ai;
using Delegat.Services.Ai.Agent;
using Delegat.Services.Ai.Prompts;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Delegat.Services.Ai;

public class AiChatService
{
    private readonly AiModelConfigService _modelService;
    private readonly AiApiClient _apiClient;
    private readonly DataContext _context;
    private readonly AgentToolHost _toolHost;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly QuotaService _quota;
    private readonly ILogger<AiChatService> _logger;
    private readonly IMcpToolGateway _mcpTools;

    private string ResolveLocale()
    {
        var header = _httpContextAccessor.HttpContext?.Request.Headers["X-UI-Locale"].ToString();
        return AiPrompts.NormalizeLocale(header);
    }

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly Dictionary<string, string[]> _toolEffects = new()
    {
        ["platform_request"] = ["product", "mapping"],
        ["run_python"] = ["product"],
        ["match_files"] = ["product"],
    };

    public AiChatService(
        AiModelConfigService modelService,
        AiApiClient apiClient,
        DataContext context,
        AgentToolHost toolHost,
        IHttpContextAccessor httpContextAccessor,
        QuotaService quota,
        ILogger<AiChatService> logger,
        IMcpToolGateway mcpTools)
    {
        _modelService = modelService;
        _apiClient = apiClient;
        _context = context;
        _toolHost = toolHost;
        _httpContextAccessor = httpContextAccessor;
        _quota = quota;
        _logger = logger;
        _mcpTools = mcpTools;
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        List<ChatMessage> userMessages,
        int storeId,
        int userId,
        int? modelId = null,
        AiPageContext? pageContext = null,
        string? sessionId = null,
        string? locale = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var localeCode = locale ?? ResolveLocale();
        {
            var modelConfig = await _modelService.ResolveEnabledModelAsync(modelId, ct);
            if (modelConfig == null)
            {
                yield return Sse(SseEventType.Error, new
                {
                    message = "No connected or enabled AI models. An administrator can add and enable models in Settings → AI.",
                });
                yield return Sse(SseEventType.Done, new { });
                yield break;
            }

            var model = _modelService.DecryptForUse(modelConfig);
            var storeInfo = await _context.stores.AsNoTracking()
                .Where(s => s.Id == storeId)
                .Select(s => new { s.name, s.timezone })
                .FirstOrDefaultAsync(ct);
            var storeName = storeInfo?.name ?? "Store";
            var productCount = await _context.products.AsNoTracking()
                .CountAsync(p => p.storeId == storeId, ct);
            var connections = await _context.marketplaceConnections.AsNoTracking()
                .Where(c => c.storeId == storeId)
                .Select(c => new { c.Id, c.name, c.adapterKey, c.isActive, c.lastSyncAt })
                .ToListAsync(ct);
            var currentUser = await _context.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.UserName, u.isAdmin })
                .FirstOrDefaultAsync(ct);
            var storeSettings = await _context.aiStoreSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.StoreId == storeId, ct);
            var catalogSlotsRemaining = await _quota.GetRemainingProductSlotsAsync(storeId);

            var extraMcp = new List<AiTool>();
            try
            {
                extraMcp.AddRange(await _mcpTools.ListToolsAsync(ct));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Listing connected MCP tools failed");
            }

            var systemPrompt = AiPrompts.ChatSystem(
                storeName, storeId, productCount, connections
                    .Select(c => new AiPrompts.ConnectionInfo(c.Id, c.name, c.adapterKey, c.isActive, c.lastSyncAt))
                    .ToList(),
                currentUser?.UserName ?? "user", currentUser?.isAdmin ?? false,
                pageContext, storeSettings, storeInfo?.timezone, localeCode,
                catalogSlotsRemaining,
                AiPrompts.ConnectedMcpFromAiTools(extraMcp));

            var messages = userMessages.Select(m => new AiMessage { role = m.role, content = m.content }).ToList();
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                var cutoff = AgentFileUploadService.LiveCutoffUtc();
                var attachments = await _context.aiChatAttachments.AsNoTracking()
                    .Where(a => a.sessionId == sessionId && a.storeId == storeId && a.userId == userId
                        && a.createdAt >= cutoff)
                    .OrderBy(a => a.Id)
                    .Take(AgentFileUploadService.MaxChatAttachments)
                    .ToListAsync(ct);
                var inject = AgentChatAttachmentContext.UserMessage(attachments);
                if (inject != null)
                    messages.Insert(0, new AiMessage { role = "user", content = inject });
            }

            var tools = AgentToolCatalog.Build();
            tools.AddRange(extraMcp);
            var autoContinued = false;

            for (var iteration = 0; iteration < 20; iteration++)
            {
                var request = new AiRequest
                {
                    model = model.modelId,
                    max_tokens = model.maxTokens,
                    system = systemPrompt,
                    messages = messages,
                    tools = tools,
                    stream = true,
                    user = sessionId,
                };

                if (iteration > 0)
                    yield return Sse(SseEventType.Text, new { delta = "\n\n" });

                var textBuffer = new StringBuilder();
                var toolUses = new List<(string id, string name, string inputJson)>();
                var currentToolId = "";
                var currentToolName = "";
                var currentToolInput = new StringBuilder();
                var isCollectingTool = false;
                var gotApiError = false;

                await foreach (var ev in _apiClient.StreamAsync(model, request, ct))
                {
                    switch (ev.type)
                    {
                        case "content_block_start":
                            if (ev.content_block?.type == "tool_use")
                            {
                                currentToolId = ev.content_block.id ?? "";
                                currentToolName = ev.content_block.name ?? "";
                                currentToolInput.Clear();
                                isCollectingTool = true;
                            }
                            break;
                        case "content_block_delta":
                            if (ev.delta?.type == "text_delta" && ev.delta.text != null)
                            {
                                textBuffer.Append(ev.delta.text);
                                yield return Sse(SseEventType.Text, new { delta = ev.delta.text });
                            }
                            else if (ev.delta?.type == "input_json_delta" && isCollectingTool)
                            {
                                currentToolInput.Append(ev.delta.partial_json);
                            }
                            break;
                        case "content_block_stop":
                            if (isCollectingTool)
                            {
                                toolUses.Add((currentToolId, currentToolName, currentToolInput.ToString()));
                                isCollectingTool = false;
                            }
                            break;
                        case "error":
                            gotApiError = true;
                            _logger.LogWarning("API error event in iteration {Iter}: {Err}", iteration, ev.error);
                            break;
                    }
                }

                if (gotApiError)
                {
                    yield return Sse(SseEventType.Error, new { message = "Error requesting the AI API. Please try again." });
                    break;
                }

                if (isCollectingTool && currentToolName.Length > 0)
                    toolUses.Add((currentToolId, currentToolName, currentToolInput.ToString()));

                if (toolUses.Count == 0)
                {
                    var finalText = textBuffer.ToString();
                    if (!autoContinued && EndsWithUnfulfilledIntent(finalText))
                    {
                        autoContinued = true;
                        messages.Add(new AiMessage { role = "assistant", content = finalText });
                        messages.Add(new AiMessage
                        {
                            role = "user",
                            content = "Continue: do the catalog action you announced, in this same turn. Do not reply with text only."
                        });
                        continue;
                    }
                    break;
                }

                var assistantContent = new List<object>();
                if (textBuffer.Length > 0)
                    assistantContent.Add(new { type = "text", text = textBuffer.ToString() });
                foreach (var (tid, tname, tinput) in toolUses)
                    assistantContent.Add(new { type = "tool_use", id = tid, name = tname, input = ParseJsonOrEmpty(tinput) });
                messages.Add(new AiMessage { role = "assistant", content = assistantContent });

                var toolResults = new List<object>();
                var shouldBreak = false;
                foreach (var (toolId, toolName, toolInputJson) in toolUses)
                {
                    if (toolName == "propose_plan")
                    {
                        var planInput = ParseInput<ProposePlanInput>(toolInputJson);
                        yield return Sse(SseEventType.Plan, new
                        {
                            summary = planInput.Summary,
                            steps = planInput.Steps,
                            affectedCount = planInput.AffectedCount,
                            isDestructive = planInput.IsDestructive
                        });
                        toolResults.Add(new
                        {
                            type = "tool_result",
                            tool_use_id = toolId,
                            content = "Plan presented to user. Waiting for confirmation. Do not execute any operations until user confirms."
                        });
                        shouldBreak = true;
                        break;
                    }

                    yield return Sse(SseEventType.Tool, new { id = toolId, name = toolName, status = "running", input = toolInputJson });
                    var (result, summary) = await _toolHost.DispatchAsync(toolName, toolInputJson, storeId, userId, ct);
                    var resultJson = JsonSerializer.Serialize(result, _jsonOpts);
                    _toolEffects.TryGetValue(toolName, out var effects);
                    yield return Sse(SseEventType.Tool, new { id = toolId, name = toolName, status = "done", summary, result = resultJson, effects });
                    toolResults.Add(new { type = "tool_result", tool_use_id = toolId, content = resultJson });
                }

                messages.Add(new AiMessage { role = "user", content = toolResults });
                if (shouldBreak) break;
            }

            yield return Sse(SseEventType.Done, new { modelName = model.modelId });
        }
    }

    public async Task<string> ExplainErrorAsync(string errorMessage, string context,
        int? productId, int? connectionId, CancellationToken ct = default)
    {
        var model = await _modelService.GetDefaultModelAsync(ct);
        if (model == null) return "No enabled AI models.";
        var decrypted = _modelService.DecryptForUse(model);
        var request = new AiRequest
        {
            model = decrypted.modelId,
            max_tokens = 512,
            system = AiPrompts.ExplainErrorSystem(ResolveLocale()),
            messages =
            [
                new AiMessage
                {
                    role = "user",
                    content = AiPrompts.ExplainErrorUser(errorMessage, context, ResolveLocale())
                }
            ]
        };
        try
        {
            var response = await _apiClient.SendAsync(decrypted, request, ct);
            return response.content.FirstOrDefault(b => b.type == "text")?.text ?? errorMessage;
        }
        catch
        {
            return errorMessage;
        }
    }

    public async Task<string> GenerateTitleAsync(string firstMessage, CancellationToken ct = default)
    {
        var model = await _modelService.GetDefaultModelAsync(ct);
        if (model == null) return "New chat";
        var lang = AiPrompts.LanguageName(ResolveLocale());
        var decrypted = _modelService.DecryptForUse(model);
        var request = new AiRequest
        {
            model = decrypted.modelId,
            max_tokens = 20,
            system = $"Come up with a short title (3-5 words, no punctuation) in {lang} for a chat based on the user's first message. Reply with the title ONLY, no explanations.",
            messages =
            [
                new AiMessage { role = "user", content = firstMessage.Length > 300 ? firstMessage[..300] : firstMessage }
            ]
        };
        try
        {
            var response = await _apiClient.SendAsync(decrypted, request, ct);
            return response.content.FirstOrDefault(b => b.type == "text")?.text?.Trim() ?? "New chat";
        }
        catch { return "New chat"; }
    }

    public async Task<string> GenerateProductDescriptionAsync(
        int storeId,
        string name,
        string? sku,
        string? categoryName,
        string? attributesSummary,
        string? existingDescription,
        int? modelId,
        CancellationToken ct = default)
    {
        var modelConfig = await _modelService.ResolveEnabledModelAsync(modelId, ct);
        if (modelConfig == null)
            throw new InvalidOperationException("No connected or enabled AI models");

        var storeSettings = await _context.aiStoreSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.StoreId == storeId, ct);
        var decrypted = _modelService.DecryptForUse(modelConfig);
        var request = new AiRequest
        {
            model = decrypted.modelId,
            max_tokens = Math.Min(Math.Max(decrypted.maxTokens, 256), 4096),
            system = AiPrompts.GenerateProductDescriptionSystem(
                ResolveLocale(),
                improveExisting: !string.IsNullOrWhiteSpace(existingDescription)),
            messages =
            [
                new AiMessage
                {
                    role = "user",
                    content = AiPrompts.GenerateProductDescriptionUser(
                        name, sku, categoryName, attributesSummary, existingDescription, storeSettings)
                }
            ]
        };
        try
        {
            var response = await _apiClient.SendAsync(decrypted, request, ct);
            var text = string.Join("\n", response.content
                .Where(b => b.type == "text" && !string.IsNullOrWhiteSpace(b.text))
                .Select(b => b.text!.Trim()));
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("The model returned an empty response. Try a different model or retry the request.");
            return text;
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI description generation failed for model config {ModelConfigId}", modelConfig.Id);
            throw new InvalidOperationException($"Failed to get a response from AI: {ex.Message}", ex);
        }
    }

    public async IAsyncEnumerable<string> ExecutePlanStreamAsync(
        List<PlanStepInput> steps, int storeId, int userId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var forbidden = new HashSet<string> { "propose_plan" };
        var ok = 0;
        var errors = new List<string>();

        foreach (var step in steps)
        {
            if (ct.IsCancellationRequested) break;
            var tool = step.Tool ?? "";
            if (tool.StartsWith("functions.", StringComparison.OrdinalIgnoreCase))
                tool = tool["functions.".Length..];
            var argsJson = step.Args.HasValue ? step.Args.Value.GetRawText() : "{}";
            var stepId = Guid.NewGuid().ToString("N")[..8];
            yield return Sse(SseEventType.Tool, new { id = stepId, name = tool, status = "running", input = argsJson });

            string summary;
            bool failed;
            string resultJson;
            if (forbidden.Contains(tool))
            {
                summary = "Tool is not allowed inside a plan";
                failed = true;
                resultJson = "{}";
            }
            else
            {
                var (result, s) = await _toolHost.DispatchAsync(tool, argsJson, storeId, userId, ct);
                summary = s;
                failed = AgentToolResult.IsFailure(result, s);
                resultJson = JsonSerializer.Serialize(result, _jsonOpts);
            }

            _toolEffects.TryGetValue(tool, out var effects);
            yield return Sse(SseEventType.Tool, new { id = stepId, name = tool, status = "done", summary, result = resultJson, effects });
            if (failed)
            {
                errors.Add(summary);
                break;
            }
            ok++;
        }

        var text = AgentToolResult.FormatPlanOutcome(ok, steps.Count, errors, ResolveLocale());
        yield return Sse(SseEventType.Text, new { delta = text });
        yield return Sse(SseEventType.Done, new { ok = errors.Count == 0, failedCount = errors.Count });
    }

    private static readonly Regex _unfulfilledIntentRegex = new(
        "\\b(\u0432\u044b\u043f\u043e\u043b\u043d\u044e|\u043f\u0440\u043e\u0432\u0435\u0440\u044e|\u0432\u044b\u0437\u043e\u0432\u0443|\u043f\u043e\u043b\u0443\u0447\u0443|\u0441\u0434\u0435\u043b\u0430\u044e|\u043e\u0431\u043d\u043e\u0432\u043b\u044e|\u0441\u043e\u0437\u0434\u0430\u043c|\u0443\u0434\u0430\u043b\u044e|\u043d\u0430\u0439\u0434\u0443|\u043f\u043e\u0441\u043c\u043e\u0442\u0440\u044e|\u0437\u0430\u0433\u0440\u0443\u0436\u0443|\u0437\u0430\u043f\u0443\u0449\u0443|\u0437\u0430\u043f\u0440\u043e\u0448\u0443|\u043f\u043e\u0434\u043a\u043b\u044e\u0447\u0443|\u0437\u0430\u043f\u043e\u043b\u043d\u044e|\u043f\u0440\u0438\u0441\u0442\u0443\u043f\u0430\u044e|\u043d\u0430\u0447\u0438\u043d\u0430\u044e|\u0437\u0430\u043f\u0443\u0441\u043a\u0430\u044e|\u0432\u044b\u043f\u043e\u043b\u043d\u044f\u044e|\u043f\u043e\u043b\u0443\u0447\u0430\u044e|\u043f\u0440\u043e\u0432\u0435\u0440\u044f\u044e|I(?:'|\u2019)?ll|I will|let me|going to|now I)\\b[^.!?\\n]{0,120}[.!\u2026:]?\\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool EndsWithUnfulfilledIntent(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var tail = text.Length > 300 ? text[^300..] : text;
        return _unfulfilledIntentRegex.IsMatch(tail);
    }

    private static string Sse(string type, object data)
        => AiSseTurnRecorder.Format(type, data);

    private static T ParseInput<T>(string json) where T : new()
    {
        if (string.IsNullOrWhiteSpace(json)) return new T();
        try { return JsonSerializer.Deserialize<T>(json, _jsonOpts) ?? new T(); }
        catch { return new T(); }
    }

    private static object ParseJsonOrEmpty(string json)
    {
        try { return JsonDocument.Parse(json).RootElement; }
        catch { return new { }; }
    }

    private class ProposePlanInput
    {
        public string Summary { get; set; } = "";
        public List<object>? Steps { get; set; }
        public int AffectedCount { get; set; }
        public bool IsDestructive { get; set; }
    }
}
