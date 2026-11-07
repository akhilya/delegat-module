using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Delegat.Models.Ai;
using Delegat.Services.Ai.Agent;
using Delegat.Services.Ai.Prompts;
using Delegat.Infrastructure.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Delegat.Services.Ai;

public class AiChatService
{
    private readonly AiModelConfigService _modelService;
    private readonly IAiApiClient _apiClient;
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
    };

    public AiChatService(
        AiModelConfigService modelService,
        IAiApiClient apiClient,
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
        [EnumeratorCancellation] CancellationToken ct = default,
        bool planResultReview = false)
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
            using var turnTelemetry = new AiTurnTelemetry(
                _logger, storeId, model.provider.ToString(), model.modelId,
                AiPrompts.ChatPromptVersion);
            var storeInfo = await _context.stores.AsNoTracking()
                .Where(s => s.Id == storeId)
                .Select(s => new { s.name, s.timezone, s.extendedPriceParameters })
                .FirstOrDefaultAsync(ct);
            var storeName = storeInfo?.name ?? "Store";
            var productCount = await _context.products.AsNoTracking()
                .CountAsync(p => p.storeId == storeId, ct);
            var catalogProductsUsed = await _context.products.IgnoreQueryFilters().AsNoTracking()
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
            var catalogProductLimit = _quota.GetLimits().MaxProducts;
            var durableMemories = await _context.aiMemories.AsNoTracking()
                .Where(m => m.storeId == storeId && m.userId == userId)
                .OrderByDescending(m => m.updatedAt)
                .Take(AiMemoryService.MaxEntries).ToListAsync(ct);
            var relevantMemories = AiMemoryService.RankRelevant(
                durableMemories, MemorySearchQuery(userMessages));
            var memoryBlock = AiMemoryService.BuildPromptBlock(relevantMemories);

            var extraMcp = new List<AiTool>();
            try
            {
                if (!planResultReview) extraMcp.AddRange(await _mcpTools.ListToolsAsync(ct));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Listing connected MCP tools failed");
            }

            string? mappingExperience = null;
            if (pageContext is { Page: "fieldMapping", MappingId: int mappingId })
            {
                mappingExperience = await MappingExportExperience.ForMappingAsync(
                    _context, storeId, mappingId, storeInfo?.timezone, ct);
            }

            var systemPrompt = AiPrompts.ChatSystem(
                storeName, storeId, productCount, connections
                    .Select(c => new AiPrompts.ConnectionInfo(c.Id, c.name, c.adapterKey, c.isActive, c.lastSyncAt))
                    .ToList(),
                currentUser?.UserName ?? "user", currentUser?.isAdmin ?? false,
                pageContext, storeSettings, storeInfo?.timezone, localeCode,
                catalogSlotsRemaining,
                AiPrompts.ConnectedMcpFromAiTools(extraMcp),
                catalogProductLimit,
                catalogProductsUsed,
                storeInfo?.extendedPriceParameters ?? false,
                turnText: TurnText(userMessages),
                mappingExperience: mappingExperience,
                durableMemory: memoryBlock);
            if (planResultReview) systemPrompt += "\n\n" + AiPrompts.PlanResultFollowup;

            var history = AiChatHistoryBudget.Trim(
                userMessages, AiChatHistoryBudget.GetMaxCharacters());
            turnTelemetry.RecordHistoryTrim(history.RemovedTurns, history.RemovedCharacters, history.ExceedsBudget);
            if (history.Trimmed)
                systemPrompt += $"\n\n=== CONTEXT LIMIT ===\n{AiChatHistoryBudget.OmittedHistoryNotice}";

            var messages = history.Messages.Select(m => new AiMessage { role = m.role, content = m.content }).ToList();
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
            if (planResultReview) tools = tools.Where(t => t.name == AgentChatTurnGate.ProposePlanTool).ToList();
            var autoContinued = false;
            var answerNow = AgentChatTurnGate.AsksToAnswerNow(TurnText(userMessages));
            var toolRounds = 0;
            var spoke = false;
            var sawPlan = false;
            var nudgedSilence = false;

            // Keep calling tools until the task is closed. A stuck loop still has to end in a sentence.
            for (var iteration = 0; iteration < (planResultReview ? 3 : AgentChatTurnGate.RunawayToolRounds + 2); iteration++)
            {
                var speakOnly = AgentChatTurnGate.SpeakOnly(answerNow, toolRounds) || (planResultReview && iteration >= 2);
                List<AiTool>? passTools = speakOnly ? null : tools;

                var request = new AiRequest
                {
                    model = model.modelId,
                    max_tokens = model.maxTokens,
                    system = systemPrompt,
                    messages = messages,
                    tools = passTools,
                    stream = true,
                    user = sessionId,
                    enable_prompt_cache = true,
                };

                if (iteration > 0 && !planResultReview)
                    yield return Sse(SseEventType.Text, new { delta = "\n\n" });

                var textBuffer = new StringBuilder();
                var toolUses = new List<(string id, string name, string inputJson)>();
                var inputCharacters = AiTurnTelemetry.EstimateInputCharacters(request);
                var modelTimer = Stopwatch.StartNew();
                var roundUsage = new AiStreamUsage();
                var dsml = new DeepSeekDsmlGate();
                var dsmlSeq = 0;
                var currentToolId = "";
                var currentToolName = "";
                var currentToolInput = new StringBuilder();
                var isCollectingTool = false;
                var gotApiError = false;
                string? apiErrorCode = null;
                int? apiErrorStatus = null;

                using var modelSpan = turnTelemetry.StartModelRound();
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
                                var drain = dsml.Push(ev.delta.text);
                                if (drain.Visible.Length > 0)
                                {
                                    textBuffer.Append(drain.Visible);
                                    if (!planResultReview)
                                    {
                                        if (HasVisibleReply(drain.Visible))
                                            spoke = true;
                                        yield return Sse(SseEventType.Text, new { delta = drain.Visible });
                                    }
                                }
                                foreach (var invoke in drain.Invokes)
                                    toolUses.Add(($"dsml-{iteration}-{dsmlSeq++}", invoke.Name, invoke.ArgumentsJson));
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
                            apiErrorCode = ev.errorCode;
                            apiErrorStatus = ev.errorStatus;
                            if (apiErrorCode == null)
                                _logger.LogWarning("AI provider error event in iteration {Iter}", iteration);
                            break;
                        case "usage":
                            roundUsage.MergeLatest(ev.usage);
                            break;
                    }
                }

                modelSpan.SetOutcome(gotApiError ? "provider_error" : "completed");
                modelSpan.Finish();

                if (gotApiError)
                {
                    modelTimer.Stop();
                    turnTelemetry.RecordModelRound(
                        modelTimer.ElapsedMilliseconds,
                        inputCharacters,
                        textBuffer.Length + currentToolInput.Length
                            + toolUses.Sum(t => (long)t.inputJson.Length),
                        roundUsage);
                    turnTelemetry.Complete("provider_error");
                    yield return apiErrorCode is { Length: > 0 }
                        ? Sse(SseEventType.Error, new { code = apiErrorCode, status = apiErrorStatus })
                        : Sse(SseEventType.Error, new
                        {
                            message = iteration > 0
                                ? AgentToolResult.TurnInterruptedMessage
                                : AgentToolResult.ProviderErrorMessage,
                        });
                    break;
                }

                if (isCollectingTool && currentToolName.Length > 0)
                    toolUses.Add((currentToolId, currentToolName, currentToolInput.ToString()));

                var dsmlTail = dsml.Flush();
                if (dsmlTail.Visible.Length > 0)
                {
                    textBuffer.Append(dsmlTail.Visible);
                    if (!planResultReview)
                    {
                        if (HasVisibleReply(dsmlTail.Visible))
                            spoke = true;
                        yield return Sse(SseEventType.Text, new { delta = dsmlTail.Visible });
                    }
                }
                foreach (var invoke in dsmlTail.Invokes)
                    toolUses.Add(($"dsml-{iteration}-{dsmlSeq++}", invoke.Name, invoke.ArgumentsJson));

                modelTimer.Stop();
                turnTelemetry.RecordModelRound(
                    modelTimer.ElapsedMilliseconds,
                    inputCharacters,
                    textBuffer.Length + toolUses.Sum(t => (long)t.inputJson.Length),
                    roundUsage);

                if (toolUses.Count == 0)
                {
                    var finalText = textBuffer.ToString();
                    // Review prose is provisional until the model round ends.
                    // A table followed by propose_plan must never flash then vanish.
                    if (planResultReview && HasVisibleReply(finalText))
                    {
                        spoke = true;
                        yield return Sse(SseEventType.Text, new { delta = finalText });
                        break;
                    }
                    if (!planResultReview && passTools != null && !autoContinued && EndsWithUnfulfilledIntent(finalText))
                    {
                        autoContinued = true;
                        messages.Add(new AiMessage { role = "assistant", content = finalText });
                        messages.Add(new AiMessage
                        {
                            role = "user",
                            content = "Continue in this turn. If you announced a cabinet write, do only the minimum explicitly required read-only lookup(s) if needed, then call propose_plan by itself; if no lookup is needed, call propose_plan now. Never perform a write before confirmation. If you announced a live-data question, call the required read now. Do not reply with text only."
                        });
                        continue;
                    }
                    if (!spoke && !sawPlan && !nudgedSilence)
                    {
                        nudgedSilence = true;
                        if (finalText.Length > 0)
                            messages.Add(new AiMessage { role = "assistant", content = finalText });
                        messages.Add(new AiMessage
                        {
                            role = "user",
                            content = "You stopped with no reply. The task is not finished. If a fact is still missing, call the next tool. If you already have it, answer or propose_plan now. Do not end the turn on tools alone."
                        });
                        continue;
                    }
                    break;
                }

                turnTelemetry.RecordModelToolCalls(toolUses.Select(t => (t.name, t.inputJson)));

                var assistantContent = new List<object>();
                if (textBuffer.Length > 0)
                    assistantContent.Add(new { type = "text", text = textBuffer.ToString() });
                foreach (var (tid, tname, tinput) in toolUses)
                    assistantContent.Add(new { type = "tool_use", id = tid, name = tname, input = ParseJsonOrEmpty(tinput) });
                messages.Add(new AiMessage { role = "assistant", content = assistantContent });

                if (passTools == null)
                    break;

                var toolResults = new List<object>();
                var shouldBreak = false;
                var waitForPlan = AgentChatTurnGate.BatchHasProposePlan(toolUses);
                var attachmentReadInBatch = toolUses.Any(t =>
                    string.Equals(t.name, "read_chat_attachment", StringComparison.OrdinalIgnoreCase));
                var deferPlanForAttachmentRead = waitForPlan && attachmentReadInBatch;
                foreach (var (toolId, toolName, toolInputJson) in toolUses)
                {
                    if (planResultReview && !AgentChatTurnGate.IsProposePlan(toolName))
                    {
                        toolResults.Add(new { type = "tool_result", tool_use_id = toolId,
                            content = "Not run. Execution is already finished. Review the actual results and answer, or propose a corrected plan for fresh user confirmation. No tool may execute during this review." });
                        continue;
                    }
                    if (AgentChatTurnGate.IsProposePlan(toolName))
                    {
                        if (sawPlan)
                        {
                            toolResults.Add(new
                            {
                                type = "tool_result",
                                tool_use_id = toolId,
                                content = AgentChatTurnGate.AdditionalPlanDeferred
                            });
                            continue;
                        }

                        if (deferPlanForAttachmentRead)
                        {
                            toolResults.Add(new
                            {
                                type = "tool_result",
                                tool_use_id = toolId,
                                content = "Plan not presented. First read the attached file and use its actual contents; then propose the exact plan by itself.",
                            });
                            continue;
                        }

                        var planInput = ProposePlanReader.Read(toolInputJson);
                        if (!planInput.HasContent)
                        {
                            toolResults.Add(new
                            {
                                type = "tool_result",
                                tool_use_id = toolId,
                                content = "Plan not presented: provide a nonempty summary and at least one step. Every step needs a tool other than propose_plan, a nonempty description in the store language, and an args object with exact arguments. For a named export, productIds must be nonempty, unique, and not combined with allMatching/where; affectedCount, if provided, must match the number of unique ids. Call propose_plan again by itself."
                            });
                            continue;
                        }

                        var efficiencyReason = AgentPlanEfficiencyGuard.RejectionReason(planInput.Steps);
                        if (efficiencyReason != null)
                        {
                            toolResults.Add(new { type = "tool_result", tool_use_id = toolId, content = efficiencyReason });
                            continue;
                        }

                        var planCreatedAt = DateTime.UtcNow;
                        var serializedSteps = JsonSerializer.Serialize(planInput.Steps, _jsonOpts);
                        var stepsNode = JsonNode.Parse(serializedSteps)!;
                        var stepsHash = AiConversationTurnStore.ComputeStepsHash(stepsNode);
                        var expiresAt = planCreatedAt.AddMinutes(15);
                        var (proposalId, proposalNonce) = AiPlanProposalToken.Mint(
                            sessionId ?? "", storeId, userId, stepsHash, expiresAt);
                        turnTelemetry.Complete("plan_proposed");
                        yield return Sse(SseEventType.Plan, new
                        {
                            proposalId,
                            proposalNonce,
                            stepsHash,
                            createdAtUtc = planCreatedAt,
                            expiresAtUtc = expiresAt,
                            modelConfigId = modelConfig.Id,
                            discardDraftText = true,
                            summary = planInput.Summary,
                            steps = planInput.Steps,
                            affectedCount = planInput.AffectedCount,
                            isDestructive = planInput.IsDestructive
                        });
                        sawPlan = true;
                        toolResults.Add(new
                        {
                            type = "tool_result",
                            tool_use_id = toolId,
                            content = "Plan presented to user. Waiting for confirmation. Do not execute any operations until user confirms."
                        });
                        shouldBreak = true;
                        continue;
                    }

                    if (waitForPlan
                        && !(deferPlanForAttachmentRead
                            && string.Equals(toolName, "read_chat_attachment", StringComparison.OrdinalIgnoreCase)))
                    {
                        toolResults.Add(new
                        {
                            type = "tool_result",
                            tool_use_id = toolId,
                            content = AgentChatTurnGate.DeferredForPlan
                        });
                        continue;
                    }

                    if (AgentChatTurnGate.RequiresPlanConfirmation(toolName, toolInputJson))
                    {
                        toolResults.Add(new
                        {
                            type = "tool_result",
                            tool_use_id = toolId,
                            content = McpToolNames.TryParse(toolName, out _, out _)
                                || string.Equals(toolName, "run_python", StringComparison.OrdinalIgnoreCase)
                                ? AgentChatTurnGate.ExternalCapabilityNeedsPlan
                                : AgentChatTurnGate.WriteNeedsPlan
                        });
                        continue;
                    }

                    yield return Sse(SseEventType.Tool, new { id = toolId, name = toolName, status = "running", input = toolInputJson });
                    object result;
                    string summary;
                    var toolTimer = Stopwatch.StartNew();
                    using var toolSpan = turnTelemetry.StartToolExecution(toolName);
                    try
                    {
                        (result, summary) = await _toolHost.DispatchAsync(
                            toolName, toolInputJson, storeId, userId, ct,
                            allowConnectedMcp: true,
                            allowChatAttachments: true,
                            chatSessionId: sessionId);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Agent tool {Tool} threw", toolName);
                        result = new { error = ex.Message };
                        summary = $"Error: {ex.Message}";
                    }
                    toolTimer.Stop();
                    var toolFailed = AgentToolResult.IsFailure(result, summary);
                    toolSpan.SetOutcome(toolFailed ? "failed" : "completed");
                    toolSpan.Finish();
                    turnTelemetry.RecordToolExecution(
                        toolTimer.ElapsedMilliseconds, toolFailed);
                    var resultJson = AgentToolResult.TruncateForModel(JsonSerializer.Serialize(result, _jsonOpts));
                    _toolEffects.TryGetValue(toolName, out var effects);
                    yield return Sse(SseEventType.Tool, new { id = toolId, name = toolName, status = "done", summary, result = resultJson, effects });
                    toolResults.Add(new { type = "tool_result", tool_use_id = toolId, content = resultJson });
                }

                messages.Add(new AiMessage { role = "user", content = toolResults });
                if (shouldBreak) break;
                toolRounds++;
                if (toolRounds == AgentChatTurnGate.RunawayToolRounds)
                {
                    messages.Add(new AiMessage
                    {
                        role = "user",
                        content = "Close the task now from what you already have: a sentence or propose_plan. Do not call another tool."
                    });
                }
            }

            ct.ThrowIfCancellationRequested();
            turnTelemetry.CompleteIfUnset(sawPlan ? "plan_proposed" : "completed");

            if (!spoke && !sawPlan)
            {
                yield return Sse(SseEventType.Text, new
                {
                    delta = AgentChatTurnGate.SilentTurnFallback(localeCode)
                });
            }

            yield return Sse(SseEventType.Done, new { modelName = model.modelId });
        }
    }

    private static string? TurnText(List<ChatMessage> messages)
    {
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (!string.Equals(messages[i].role, "user", StringComparison.OrdinalIgnoreCase))
                continue;
            var content = messages[i].content;
            if (content.ValueKind == JsonValueKind.String)
                return content.GetString();
            if (content.ValueKind == JsonValueKind.Array)
            {
                var text = new StringBuilder();
                foreach (var block in content.EnumerateArray())
                {
                    if (block.ValueKind == JsonValueKind.Object
                        && block.TryGetProperty("text", out var part)
                        && part.ValueKind == JsonValueKind.String)
                        text.Append(part.GetString());
                }
                if (text.Length > 0)
                    return text.ToString();
            }
            if (content.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                continue;
            return content.ToString();
        }
        return null;
    }

    private static string MemorySearchQuery(List<ChatMessage> messages)
    {
        const int MaxRecentUserTurns = 3;
        const int MaxCharactersPerTurn = 600;
        var recentText = new List<string>(MaxRecentUserTurns);

        for (var i = messages.Count - 1; i >= 0 && recentText.Count < MaxRecentUserTurns; i--)
        {
            var message = messages[i];
            if (!string.Equals(message.role, "user", StringComparison.OrdinalIgnoreCase))
                continue;

            var content = message.content;
            string text;
            if (content.ValueKind == JsonValueKind.String)
            {
                text = content.GetString() ?? "";
            }
            else if (content.ValueKind == JsonValueKind.Array)
            {
                var builder = new StringBuilder();
                foreach (var block in content.EnumerateArray())
                {
                    // Tool results are data returned by the agent, not part of the user's
                    // search intent. Only user-authored text blocks influence memory ranking.
                    if (block.ValueKind == JsonValueKind.Object
                        && block.TryGetProperty("text", out var part)
                        && part.ValueKind == JsonValueKind.String)
                        builder.Append(part.GetString()).Append(' ');
                }
                text = builder.ToString();
            }
            else
            {
                continue;
            }

            text = text.Trim();
            if (text.Length > 0)
                recentText.Add(text[..Math.Min(text.Length, MaxCharactersPerTurn)]);
        }

        return string.Join(' ', recentText);
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
        catch (ApiException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI description generation failed for model config {ModelConfigId}", modelConfig.Id);
            throw new InvalidOperationException($"Failed to get a response from AI: {ex.Message}", ex);
        }
    }

    public async IAsyncEnumerable<string> ExecutePlanStreamAsync(
        List<PlanStepInput> steps, int storeId, int userId, string proposalId,
        [EnumeratorCancellation] CancellationToken ct = default,
        string? sessionId = null, string? locale = null)
    {
        var localeCode = AiPrompts.NormalizeLocale(locale ?? ResolveLocale());
        var followup = string.IsNullOrEmpty(sessionId) ? null
            : await LoadPlanFollowupAsync(sessionId, proposalId, storeId, userId, ct);
        var outcomes = new List<object>();
        var forbidden = new HashSet<string> { "propose_plan" };
        var ok = 0;
        var queued = 0;
        var cancelled = false;
        var errors = new List<string>();

        foreach (var step in steps)
        {
            if (ct.IsCancellationRequested)
            {
                cancelled = true;
                errors.Add("Execution was cancelled; remaining steps were not run.");
                break;
            }
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
                try
                {
                    var (result, s) = await _toolHost.DispatchAsync(
                        tool, argsJson, storeId, userId, ct, allowConnectedMcp: true);
                    summary = s;
                    failed = AgentToolResult.IsFailure(result, s);
                    if (!failed && AgentToolResult.IsQueuedJob(result))
                        queued++;
                    resultJson = AgentToolResult.TruncateForModel(JsonSerializer.Serialize(result, _jsonOpts));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    cancelled = true;
                    summary = "Execution was cancelled.";
                    failed = true;
                    resultJson = JsonSerializer.Serialize(new { error = summary }, _jsonOpts);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Plan tool {Tool} threw", tool);
                    summary = $"Error: {ex.Message}";
                    failed = true;
                    resultJson = JsonSerializer.Serialize(new { error = ex.Message }, _jsonOpts);
                }
            }

            _toolEffects.TryGetValue(tool, out var effects);
            outcomes.Add(new { step = outcomes.Count + 1, tool, description = step.Description,
                status = failed ? "failed" : AgentToolResult.IsQueuedJob(JsonSerializer.Deserialize<JsonElement>(resultJson)) ? "queued" : "completed",
                summary, result = JsonSerializer.Deserialize<JsonElement>(resultJson) });
            yield return Sse(SseEventType.Tool, new { id = stepId, name = tool, status = "done", summary, result = resultJson, effects });
            if (failed)
            {
                errors.Add(summary);
                break;
            }
            ok++;
        }

        var text = AgentToolResult.FormatPlanOutcome(ok, steps.Count, errors, localeCode, queued);
        var audit = await _context.aiAgentAudits
            .Where(a => a.storeId == storeId && a.userId == userId
                && a.tool == "plan_confirmation"
                && a.summary.StartsWith($"proposal:{proposalId}:"))
            .OrderByDescending(a => a.createdAt)
            .FirstOrDefaultAsync(CancellationToken.None);
        if (audit != null)
        {
            var outcome = cancelled ? "cancelled"
                : errors.Count == 0 ? (queued > 0 ? "queued" : "completed")
                : ok > 0 ? "partial" : "failed";
            audit.summary = $"proposal:{proposalId}: {outcome}; {ok}/{steps.Count} steps, {queued} queued";
            if (errors.Count > 0)
                audit.summary += $"; {errors[0]}";
            if (audit.summary.Length > 500) audit.summary = audit.summary[..500];
            await _context.SaveChangesAsync(CancellationToken.None);
        }
        // A successful tool exit is not a verified answer to the user's task.
        // Keep the card's execution status, but let review provide the visible answer.
        if (followup == null || errors.Count > 0)
            yield return Sse(SseEventType.Text, new { delta = text });
        yield return Sse(SseEventType.Done, new { ok = errors.Count == 0, failedCount = errors.Count, cancelled });
        if (cancelled || followup == null) yield break;
        ct.ThrowIfCancellationRequested();
        yield return Sse(SseEventType.AssistantStart, new { executionProposalId = proposalId });
        var review = JsonSerializer.SerializeToElement(new
        {
            originalUserRequest = followup.UserRequest,
            approvedPlan = AgentToolResult.TruncateForModel(followup.Plan.GetRawText()),
            outcome = new { succeeded = ok, total = steps.Count, queued, failed = errors.Count,
                skipped = steps.Count - outcomes.Count, results = AgentToolResult.TruncateForModel(JsonSerializer.Serialize(outcomes, _jsonOpts)) },
        }, _jsonOpts);
        await foreach (var chunk in StreamChatAsync(
            [new ChatMessage { role = "user", content = JsonSerializer.SerializeToElement(review.GetRawText()) }], storeId, userId, followup.ModelId,
            sessionId: sessionId, locale: localeCode, ct: ct, planResultReview: true))
            yield return chunk;
    }

    private sealed record PlanFollowup(string UserRequest, JsonElement Plan, int? ModelId);

    private async Task<PlanFollowup?> LoadPlanFollowupAsync(
        string sessionId, string proposalId, int storeId, int userId, CancellationToken ct)
    {
        var conversation = await _context.aiConversations.AsNoTracking()
            .Include(c => c.messages)
            .SingleOrDefaultAsync(c => c.sessionId == sessionId && c.storeId == storeId && c.userId == userId, ct);
        if (conversation == null) return null;
        var ordered = conversation.messages.OrderBy(m => m.createdAt).ThenBy(m => m.Id).ToList();
        var last = ordered.LastOrDefault();
        if (last?.role != "assistant" || string.IsNullOrEmpty(last.toolCallsJson)) return null;
        using var document = JsonDocument.Parse(last.toolCallsJson);
        if (!document.RootElement.TryGetProperty("plan", out var plan)
            || !plan.TryGetProperty("proposalId", out var id) || id.GetString() != proposalId) return null;
        var request = ordered.LastOrDefault(m => m.role == "user"
            && m.content != AiConversationTurnStore.ConfirmedPlanUserText)?.content ?? "";
        int? modelId = plan.TryGetProperty("modelConfigId", out var model) && model.TryGetInt32(out var configId) ? configId : null;
        return new PlanFollowup(request, plan.Clone(), modelId);
    }

    public Task<IReadOnlyList<PlanStepInput>?> ConsumeConfirmedPlanAsync(
        string sessionId,
        string proposalId,
        int storeId,
        int userId,
        CancellationToken ct) =>
        AiConversationTurnStore.ConsumePendingPlanAsync(_context, sessionId, proposalId, storeId, userId, ct);

    public Task<PlanConfirmationAttempt> ConsumeConfirmedPlanDetailedAsync(
        string sessionId,
        string proposalId,
        int storeId,
        int userId,
        CancellationToken ct) =>
        AiConversationTurnStore.ConsumePendingPlanDetailedAsync(
            _context, sessionId, proposalId, storeId, userId, ct);

    private static readonly Regex _unfulfilledIntentRegex = new(
        "\\b(\u0432\u044b\u043f\u043e\u043b\u043d\u044e|\u043f\u0440\u043e\u0432\u0435\u0440\u044e|\u0432\u044b\u0437\u043e\u0432\u0443|\u043f\u043e\u043b\u0443\u0447\u0443|\u0441\u0434\u0435\u043b\u0430\u044e|\u043e\u0431\u043d\u043e\u0432\u043b\u044e|\u0441\u043e\u0437\u0434\u0430\u043c|\u0443\u0434\u0430\u043b\u044e|\u043d\u0430\u0439\u0434\u0443|\u043f\u043e\u0441\u043c\u043e\u0442\u0440\u044e|\u0437\u0430\u0433\u0440\u0443\u0436\u0443|\u0437\u0430\u043f\u0443\u0449\u0443|\u0437\u0430\u043f\u0440\u043e\u0448\u0443|\u043f\u043e\u0434\u043a\u043b\u044e\u0447\u0443|\u0437\u0430\u043f\u043e\u043b\u043d\u044e|\u043f\u0440\u0438\u0441\u0442\u0443\u043f\u0430\u044e|\u043d\u0430\u0447\u0438\u043d\u0430\u044e|\u0437\u0430\u043f\u0443\u0441\u043a\u0430\u044e|\u0432\u044b\u043f\u043e\u043b\u043d\u044f\u044e|\u043f\u043e\u043b\u0443\u0447\u0430\u044e|\u043f\u0440\u043e\u0432\u0435\u0440\u044f\u044e|I(?:'|\u2019)?ll|I will|let me|going to|now I)\\b[^.!?\\n]{0,120}[.!\u2026:]?\\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool HasVisibleReply(string text) =>
        text.Any(c => !char.IsWhiteSpace(c));

    private static bool EndsWithUnfulfilledIntent(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var tail = text.Length > 300 ? text[^300..] : text;
        return _unfulfilledIntentRegex.IsMatch(tail);
    }

    private static string Sse(string type, object data)
        => AiSseTurnRecorder.Format(type, data);

    private static object ParseJsonOrEmpty(string json)
    {
        try { return JsonDocument.Parse(json).RootElement; }
        catch { return new { }; }
    }
}
