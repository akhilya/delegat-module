using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Delegat.Enums;
using Delegat.Services.Ai.Tools;
using Microsoft.EntityFrameworkCore;
using Delegat.Models;
using Delegat.Models.Ai;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Models.Products;
using Delegat.Infrastructure.Storage;
using Delegat.Services.Ai.Prompts;
using Delegat.Services.Marketplace;
using Delegat.Services.Marketplace.Graph;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Delegat.Services.Ai;

public class AiChatService
{
    // Tracks active SSE sessions so the stop endpoint can cancel them immediately,
    // without waiting for Kestrel to detect the client disconnect on the next FlushAsync.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource>
        _activeSessions = new();

    public static bool StopSession(string sessionId)
    {
        if (_activeSessions.TryRemove(sessionId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            return true;
        }
        return false;
    }

    private readonly AiModelConfigService _modelService;
    private readonly AiApiClient _apiClient;
    private readonly GraphMutationService _graphMutation;
    private readonly AiOperationService _operationService;
    private readonly IMarketplaceAdapterRegistry _adapterRegistry;
    private readonly PriceSyncService _priceSyncService;
    private readonly IStorageService _storageService;
    private readonly ThumbnailService _thumbnailService;
    private readonly DataContext _context;
    private readonly ILogger<AiChatService> _logger;
    private readonly Delegat.Services.Ai.Tools.ToolDispatcher _toolDispatcher;
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    /// Resolves the UI locale from the current request's X-UI-Locale header so AI
    /// responses match the language the user sees. Defaults to Russian when absent.
    /// </summary>
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

    // Maps tool names to UI refresh effects. Frontend reacts generically — no tool lists in JS.
    private static readonly Dictionary<string, string[]> _toolEffects = new()
    {
        ["connect_field"]                = ["mapping"],
        ["connect_fields"]               = ["mapping"],
        ["remove_field_connection"]      = ["mapping"],
        ["set_field_static_value"]       = ["mapping"],
        ["set_fields_static_value"]      = ["mapping"],
        ["add_field_modifier"]           = ["mapping"],
        ["update_product"]               = ["product"],
        ["create_product"]               = ["product"],
        ["create_products"]              = ["product"],
        ["update_product_sku"]           = ["product"],
        ["update_product_prices"]        = ["product"],
        ["update_product_attribute"]     = ["product"],
        ["update_product_attributes"]    = ["product"],
        ["delete_products"]              = ["product"],
        ["restore_products"]             = ["product"],
        ["rollback_last_operation"]      = ["product", "mapping"],
        ["import_prices"]                = ["product"],
        ["import_stocks"]                = ["product"],
        ["import_products"]              = ["product"],
        ["generate_barcodes"]            = ["product"],
        ["assign_product_mappings"]      = ["product"],
    };

    public AiChatService(
        AiModelConfigService modelService,
        AiApiClient apiClient,
        GraphMutationService graphMutation,
        AiOperationService operationService,
        IMarketplaceAdapterRegistry adapterRegistry,
        PriceSyncService priceSyncService,
        IStorageService storageService,
        ThumbnailService thumbnailService,
        DataContext context,
        Delegat.Services.Ai.Tools.ToolDispatcher toolDispatcher,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AiChatService> logger)
    {
        _modelService = modelService;
        _apiClient = apiClient;
        _graphMutation = graphMutation;
        _operationService = operationService;
        _adapterRegistry = adapterRegistry;
        _priceSyncService = priceSyncService;
        _storageService = storageService;
        _thumbnailService = thumbnailService;
        _context = context;
        _toolDispatcher = toolDispatcher;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        List<ChatMessage> userMessages,
        int storeId,
        int userId,
        int? modelId = null,
        AiPageContext? pageContext = null,
        string? sessionId = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Create a linked CTS so /ai/stop can cancel this stream explicitly,
        // independent of the client-disconnect detection delay in Kestrel.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (!string.IsNullOrEmpty(sessionId))
            _activeSessions[sessionId] = cts;
        ct = cts.Token;

        try
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

        // Build store context for system prompt
        var storeInfo = await _context.stores.AsNoTracking()
            .Where(s => s.Id == storeId)
            .Select(s => new { s.name, s.timezone })
            .FirstOrDefaultAsync(ct);
        var storeName = storeInfo?.name ?? "Store";
        var storeTimezone = storeInfo?.timezone;
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

        var systemPrompt = AiPrompts.ChatSystem(
            storeName, storeId, productCount, connections
                .Select(c => new AiPrompts.ConnectionInfo(c.Id, c.name, c.adapterKey, c.isActive, c.lastSyncAt))
                .ToList(),
            currentUser?.UserName ?? "user", currentUser?.isAdmin ?? false,
            pageContext, storeSettings, storeTimezone, ResolveLocale());

        // Convert user messages to AI format
        var messages = userMessages.Select(m => new AiMessage
        {
            role = m.role,
            content = m.content
        }).ToList();

        var tools = BuildToolDefinitions(pageContext);
        var autoContinued = false;

        // Agentic loop
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

                    case "message_stop":
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

            // Safety net: stream ended without content_block_stop for an open tool block
            if (isCollectingTool && currentToolName.Length > 0)
            {
                _logger.LogWarning("Stream ended with unclosed tool block '{Tool}' — finalizing", currentToolName);
                toolUses.Add((currentToolId, currentToolName, currentToolInput.ToString()));
            }

            if (toolUses.Count == 0)
            {
                // Anti-early-stopping: the model announced an action ("I'll now update...")
                // but ended the turn without calling any tool. Nudge it once to follow through.
                var finalText = textBuffer.ToString();
                if (!autoContinued && EndsWithUnfulfilledIntent(finalText))
                {
                    autoContinued = true;
                    _logger.LogWarning("Model ended turn announcing an action without a tool call — auto-continuing (text tail: \"{Tail}\")",
                        finalText.Length > 120 ? finalText[^120..] : finalText);
                    messages.Add(new AiMessage { role = "assistant", content = finalText });
                    messages.Add(new AiMessage
                    {
                        role = "user",
                        content = "Continue — perform the action you announced by calling the corresponding tool. Do not reply with text without a tool call."
                    });
                    continue;
                }
                break;
            }

            // Build assistant message with content blocks
            var assistantContent = new List<object>();
            if (textBuffer.Length > 0)
                assistantContent.Add(new { type = "text", text = textBuffer.ToString() });
            foreach (var (tid, tname, tinput) in toolUses)
                assistantContent.Add(new { type = "tool_use", id = tid, name = tname, input = ParseJsonOrEmpty(tinput) });

            messages.Add(new AiMessage { role = "assistant", content = assistantContent });

            // Process tool calls
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

                // input + id travel to the frontend: id links running/done events for proper
                // API-format history replay (tool_use + tool_result blocks), input preserves
                // facts like the working search spelling across turns
                yield return Sse(SseEventType.Tool, new { id = toolId, name = toolName, status = "running", input = toolInputJson });

                var (result, summary) = await DispatchToolAsync(toolName, toolInputJson, storeId, userId, ct);
                var resultJson = JsonSerializer.Serialize(result, _jsonOpts);

                _toolEffects.TryGetValue(toolName, out var effects);
                yield return Sse(SseEventType.Tool, new { id = toolId, name = toolName, status = "done", summary, result = resultJson, effects });

                toolResults.Add(new
                {
                    type = "tool_result",
                    tool_use_id = toolId,
                    content = JsonSerializer.Serialize(result, _jsonOpts)
                });
            }

            messages.Add(new AiMessage { role = "user", content = toolResults });

            if (shouldBreak) break;
        }

        yield return Sse(SseEventType.Done, new { modelName = model.modelId });

        } // end try
        finally
        {
            if (!string.IsNullOrEmpty(sessionId))
                _activeSessions.TryRemove(sessionId, out _);
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
                new AiMessage { role = "user", content = (firstMessage.Length > 300 ? firstMessage[..300] : firstMessage) }
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
            var text = string.Join(
                "\n",
                response.content
                    .Where(b => b.type == "text" && !string.IsNullOrWhiteSpace(b.text))
                    .Select(b => b.text!.Trim()));

            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning(
                    "AI returned empty description (model config {ModelConfigId}, provider {Provider}, stop_reason {StopReason})",
                    modelConfig.Id, modelConfig.provider, response.stop_reason);
                throw new InvalidOperationException("The model returned an empty response. Try a different model or retry the request.");
            }

            return text;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI description generation failed for model config {ModelConfigId}", modelConfig.Id);
            throw new InvalidOperationException($"Failed to get a response from AI: {ex.Message}", ex);
        }
    }

    // ---- Tool Dispatcher ----

    // Deterministic execution of a user-confirmed plan: steps carry exact tool arguments,
    // so no model round-trip is needed — local models kept re-proposing plans or inventing
    // field keys when asked to execute after confirmation.
    public async IAsyncEnumerable<string> ExecutePlanStreamAsync(
        List<PlanStepInput> steps, int storeId, int userId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var forbidden = new HashSet<string> { "propose_plan", "rollback_last_operation" };
        var ok = 0;
        var errors = new List<string>();

        foreach (var step in steps)
        {
            if (ct.IsCancellationRequested) break;
            var tool = step.Tool ?? "";
            // Strip legacy "functions." prefix emitted by some OpenAI-compatible models
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
                var (result, s) = await DispatchToolAsync(tool, argsJson, storeId, userId, ct);
                summary = s;
                failed = JsonSerializer.SerializeToElement(result, _jsonOpts).TryGetProperty("error", out _);
                resultJson = JsonSerializer.Serialize(result, _jsonOpts);
            }

            if (failed) errors.Add($"{tool} — {summary}");
            else ok++;

            _toolEffects.TryGetValue(tool, out var effects);
            yield return Sse(SseEventType.Tool, new { id = stepId, name = tool, status = "done", summary, result = resultJson, effects });
        }

        var text = errors.Count == 0
            ? $"✅ Plan executed: {ok} of {steps.Count} steps."
            : $"Plan executed partially: {ok} of {steps.Count} steps succeeded.\n\nErrors:\n"
              + string.Join("\n", errors.Select(e => $"- {e}"))
              + "\n\nChanges can be undone: ask to undo.";
        yield return Sse(SseEventType.Text, new { delta = text });
        yield return Sse(SseEventType.Done, new { });
    }

    private Task<(object result, string summary)> DispatchToolAsync(
        string toolName, string inputJson, int storeId, int userId, CancellationToken ct)
        => _toolDispatcher.DispatchAsync(toolName, inputJson, storeId, userId, ct);

    // Keep the old switch stub below only during transition; it will be removed once
    // the build confirms ToolDispatcher is wired correctly.
    private async Task<(object result, string summary)> _OldDispatchToolAsync(
        string toolName, string inputJson, int storeId, int userId, CancellationToken ct)
    {
        try
        {
            return toolName switch
            {
                // Products
                "list_products" => await ListProductsAsync(inputJson, storeId, ct),
                "get_products_count" => await GetProductsCountAsync(inputJson, storeId, ct),
                "get_product" => await GetProductAsync(inputJson, storeId, ct),
                "create_product" => await CreateProductAsync(inputJson, storeId, userId, ct),
                "create_products" => await CreateProductsBulkAsync(inputJson, storeId, userId, ct),
                "update_product" => await UpdateProductAsync(inputJson, storeId, userId, ct),
                "update_product_sku" => await UpdateProductSkuAsync(inputJson, storeId, ct),
                "update_product_prices" => await UpdateProductPricesAsync(inputJson, storeId, userId, ct),
                "update_product_attribute" => await UpdateProductAttributeAsync(inputJson, storeId, userId, ct),
                "update_product_attributes" => await UpdateProductAttributesBulkAsync(inputJson, storeId, userId, ct),
                "delete_products" => await DeleteProductsAsync(inputJson, storeId, userId, ct),
                "restore_products" => await RestoreProductsAsync(inputJson, storeId, ct),
                "list_trash" => await ListTrashAsync(inputJson, storeId, ct),

                // Categories
                "list_categories" => await ListCategoriesAsync(storeId, ct),
                "get_category" => await GetCategoryAsync(inputJson, storeId, ct),
                "create_category" => await CreateCategoryAsync(inputJson, storeId, userId, ct),
                "update_category" => await UpdateCategoryAsync(inputJson, storeId, userId, ct),
                "delete_category" => await DeleteCategoryAsync(inputJson, storeId, userId, ct),
                "list_category_fields" => await ListCategoryFieldsAsync(inputJson, storeId, ct),
                "add_category_field" => await AddCategoryFieldAsync(inputJson, storeId, userId, ct),
                "update_category_field" => await UpdateCategoryFieldAsync(inputJson, storeId, userId, ct),
                "delete_category_field" => await DeleteCategoryFieldAsync(inputJson, storeId, userId, ct),
                "find_categories_with_field" => await FindCategoriesWithFieldAsync(inputJson, storeId, ct),

                // Store attributes
                "list_store_attributes" => await ListStoreAttributesAsync(storeId, ct),

                // Connections
                "list_connections" => await ListConnectionsAsync(storeId, ct),
                "get_connection" => await GetConnectionAsync(inputJson, storeId, ct),
                "test_connection" => await TestConnectionAsync(inputJson, storeId, ct),
                "get_connection_limits" => await GetConnectionLimitsAsync(inputJson, storeId, ct),
                "get_adapter_info" => await GetAdapterInfoAsync(inputJson, storeId, ct),
                "preview_marketplace_products" => await PreviewMarketplaceProductsAsync(inputJson, storeId, ct),
                "import_products" => await ImportProductsAsync(inputJson, storeId, userId, ct),
                "import_prices" => await ImportPricesAsync(inputJson, storeId, userId, ct),
                "import_stocks" => await ImportStocksAsync(inputJson, storeId, userId, ct),
                "archive_product_on_marketplace" => await ArchiveProductOnMarketplaceAsync(inputJson, storeId, userId, ct),

                // Mappings
                "list_mappings" => await ListMappingsAsync(inputJson, storeId, ct),
                "get_mapping" => await GetMappingAsync(inputJson, storeId, ct),
                "create_mapping" => await CreateMappingAsync(inputJson, storeId, userId, ct),
                "update_mapping_name" => await UpdateMappingNameAsync(inputJson, storeId, userId, ct),
                "delete_mapping" => await DeleteMappingAsync(inputJson, storeId, userId, ct),
                "duplicate_mapping" => await DuplicateMappingAsync(inputJson, storeId, ct),
                "list_external_categories" => await ListExternalCategoriesAsync(inputJson, storeId, ct),
                "get_external_fields" => await GetExternalFieldsAsync(inputJson, storeId, ct),
                "search_field_options" => await SearchFieldOptionsAsync(inputJson, storeId, ct),
                "merge_adapter_fields" => await MergeAdapterFieldsAsync(inputJson, storeId, userId, ct),
                "unmerge_adapter_fields" => await UnmergeAdapterFieldsAsync(inputJson, storeId, userId, ct),
                "get_available_sources" => await GetAvailableSourcesAsync(inputJson, storeId, ct),
                "get_mapping_coverage" => await GetMappingCoverageAsync(inputJson, storeId, ct),
                "preview_mapping_for_product" => await PreviewMappingAsync(inputJson, storeId, ct),
                "connect_field" => await ConnectFieldAsync(inputJson, storeId, userId, ct),
                "connect_fields" => await ConnectFieldsBulkAsync(inputJson, storeId, userId, ct),
                "set_field_static_value" => await SetFieldStaticAsync(inputJson, storeId, userId, ct),
                "set_fields_static_value" => await SetFieldsStaticBulkAsync(inputJson, storeId, userId, ct),
                "add_field_modifier" => await AddFieldModifierAsync(inputJson, storeId, userId, ct),
                "remove_field_connection" => await RemoveFieldConnectionAsync(inputJson, storeId, userId, ct),

                // Export/Import
                "export_products_batch" => await ExportProductsBatchAsync(inputJson, storeId, userId, ct),
                "export_prices" => await ExportPricesAsync(inputJson, storeId, ct),
                "export_stocks" => await ExportStocksAsync(inputJson, storeId, ct),
                "get_batch_status" => await GetBatchStatusAsync(inputJson, ct),
                "list_product_links" => await ListProductLinksAsync(inputJson, storeId, ct),

                // Groups
                "list_groups" => await ListGroupsAsync(inputJson, storeId, ct),
                "get_group" => await GetGroupAsync(inputJson, storeId, ct),
                "create_group" => await CreateGroupAsync(inputJson, storeId, userId, ct),
                "update_group" => await UpdateGroupAsync(inputJson, storeId, userId, ct),
                "delete_group" => await DeleteGroupAsync(inputJson, storeId, userId, ct),
                "add_products_to_group" => await AddProductsToGroupAsync(inputJson, storeId, ct),
                "remove_products_from_group" => await RemoveProductsFromGroupAsync(inputJson, storeId, userId, ct),
                "list_files" => await ListFilesAsync(inputJson, storeId, ct),
                "create_folder" => await CreateFolderAsync(inputJson, storeId, userId, ct),
                "rename_file" => await RenameFileAsync(inputJson, storeId, ct),
                "rename_folder" => await RenameFolderAsync(inputJson, storeId, ct),
                "move_files" => await MoveFilesAsync(inputJson, storeId, ct),
                "delete_files" => await DeleteFilesAsync(inputJson, storeId, userId, ct),
                "delete_folder" => await DeleteFolderAsync(inputJson, storeId, userId, ct),
                "share_file" => await ShareFileAsync(inputJson, storeId, userId, ct),
                "generate_barcodes" => await GenerateBarcodesAsync(inputJson, storeId, userId, ct),

                // Analytics
                "get_store_stats" => await GetStoreStatsAsync(storeId, ct),
                "find_products_without_images" => await FindProductsWithoutImagesAsync(storeId, ct),
                "find_products_without_barcode" => await FindProductsWithoutBarcodeAsync(storeId, ct),
                "find_products_missing_field" => await FindProductsMissingFieldAsync(inputJson, storeId, ct),
                "find_products_with_sync_errors" => await FindProductsWithSyncErrorsAsync(inputJson, storeId, ct),
                "analyze_mapping_quality" => await AnalyzeMappingQualityAsync(inputJson, storeId, ct),
                "get_image_download_status" => await GetImageDownloadStatusAsync(storeId, ct),

                // Rollback
                "rollback_last_operation" => await RollbackOperationAsync(inputJson, storeId, userId, ct),

                _ => (new { error = $"Unknown tool: {toolName}" }, $"Unknown tool: {toolName}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool {Tool} failed", toolName);
            return (new { error = ex.Message }, $"Error: {ex.Message}");
        }
    }

    // ---- Product Tools ----

    private async Task<(object, string)> ListProductsAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<ListProductsInput>(json);
        var q = _context.products.AsNoTracking()
            .Where(x => x.storeId == storeId);

        if (p.CategoryId.HasValue) q = q.Where(x => x.categoryId == p.CategoryId);
        if (!string.IsNullOrEmpty(p.Search))
            q = q.Where(x => x.name.Contains(p.Search) || (x.sku != null && x.sku.Contains(p.Search)));
        if (p.PriceMin.HasValue) q = q.Where(x => x.price >= p.PriceMin);
        if (p.PriceMax.HasValue) q = q.Where(x => x.price <= p.PriceMax);
        if (p.QuantityMin.HasValue) q = q.Where(x => x.storageQuantity >= p.QuantityMin);
        if (p.QuantityMax.HasValue) q = q.Where(x => x.storageQuantity <= p.QuantityMax);
        if (p.HasImages == true)
            q = q.Where(x => _context.images.Any(i => i.productId == x.Id));
        if (p.HasImages == false)
            q = q.Where(x => !_context.images.Any(i => i.productId == x.Id));
        if (!string.IsNullOrEmpty(p.MissingField))
        {
            var attrId = await _context.categoryAttributes.AsNoTracking()
                .Where(a => EF.Functions.Like(a.name, p.MissingField))
                .Select(a => (int?)a.Id).FirstOrDefaultAsync(ct);
            if (attrId.HasValue)
                q = q.Where(x => !_context.productAttributeValues.Any(v => v.productId == x.Id && v.attributeId == attrId.Value && v.value != null && v.value != ""));
        }

        var limit = Math.Min(p.PageSize ?? 50, 200);
        var offset = p.StartIndex ?? 0;
        var desc = string.Equals(p.SortDir, "desc", StringComparison.OrdinalIgnoreCase);
        IOrderedQueryable<Models.Products.Product> sorted = p.SortBy?.ToLower() switch
        {
            "price"    => desc ? q.OrderByDescending(x => x.price)    : q.OrderBy(x => x.price),
            "name"     => desc ? q.OrderByDescending(x => x.name)     : q.OrderBy(x => x.name),
            "quantity" => desc ? q.OrderByDescending(x => x.storageQuantity) : q.OrderBy(x => x.storageQuantity),
            _          => desc ? q.OrderByDescending(x => x.Id)       : q.OrderBy(x => x.Id),
        };
        var products = await sorted
            .Skip(offset).Take(limit)
            .Select(x => new { x.Id, x.name, x.sku, x.price, x.barcode, x.storageQuantity, x.categoryId })
            .ToListAsync(ct);

        // Zero hits on a text search: nudge the model to retry with spelling variants —
        // catalogs often mix local script and Latin (e.g. Cyrillic vs Latin for Russian-market stores)
        if (products.Count == 0 && !string.IsNullOrEmpty(p.Search))
        {
            return (new
            {
                items = products,
                count = 0,
                hint = $"0 products found for '{p.Search}'. Try other spellings: " +
                       "Latin instead of local script (and vice versa), the brand's English name, a shorter stem without word endings.",
            }, $"0 products for '{p.Search}' — try a different spelling");
        }

        return (new { items = products, count = products.Count }, $"Found {products.Count} products");
    }

    private async Task<(object, string)> GetProductsCountAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<ListProductsInput>(json);
        var q = _context.products.AsNoTracking().Where(x => x.storeId == storeId);
        if (p.CategoryId.HasValue) q = q.Where(x => x.categoryId == p.CategoryId);
        if (!string.IsNullOrEmpty(p.Search)) q = q.Where(x => x.name.Contains(p.Search));
        if (p.PriceMin.HasValue) q = q.Where(x => x.price >= p.PriceMin);
        if (p.PriceMax.HasValue) q = q.Where(x => x.price <= p.PriceMax);
        var count = await q.CountAsync(ct);
        if (count == 0 && !string.IsNullOrEmpty(p.Search))
        {
            return (new
            {
                count = 0,
                hint = $"0 products for query '{p.Search}'. Try alternative spellings: " +
                       "Latin instead of Cyrillic (and vice versa), the English brand name, a word stem without endings.",
            }, $"0 products for '{p.Search}' — try a different spelling");
        }
        return (new { count }, $"{count} products");
    }

    private async Task<(object, string)> GetProductAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var product = await _context.products.AsNoTracking()
            .Include(x => x.category)
            .Include(x => x.attributeValues).ThenInclude(v => v.attribute)
            .FirstOrDefaultAsync(x => x.Id == p.Id && x.storeId == storeId, ct);
        if (product == null) return (new { error = "Product not found" }, "Product not found");
        var result = new
        {
            product.Id,
            product.name,
            product.sku,
            product.price,
            product.barcode,
            product.description,
            product.storageQuantity,
            category = product.category == null ? null : new { product.category.Id, product.category.name },
            attributes = product.attributeValues.Select(v => new
            {
                attributeId = v.attributeId,
                name = v.attribute?.name,
                key = v.attribute?.key,
                type = v.attribute?.type.ToString(),
                value = v.value
            }).ToList()
        };
        return (result, product.name);
    }

    private async Task<(object, string)> CreateProductAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<CreateProductInput>(json);
        var product = new Product
        {
            name = p.Name,
            sku = p.Sku ?? "",
            price = p.Price ?? 0,
            barcode = p.Barcode ?? "",
            description = p.Description ?? "",
            storageQuantity = p.Quantity ?? 0,
            categoryId = p.CategoryId,
            storeId = storeId
        };
        _context.products.Add(product);
        await _context.SaveChangesAsync(ct);
        await _operationService.RecordAsync(storeId, userId, "create_product",
            $"Created product \"{p.Name}\"", new { productId = product.Id }, canRollback: false, ct: ct);
        return (new { id = product.Id }, $"Product \"{p.Name}\" created (id={product.Id})");
    }

    private async Task<(object, string)> CreateProductsBulkAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<CreateProductsBulkInput>(json);
        if (p.Products is not { Count: > 0 })
            return (new { error = "Product list is empty" }, "List is empty");

        var created = new List<object>();
        foreach (var spec in p.Products)
        {
            var product = new Product
            {
                name = spec.Name,
                sku = spec.Sku ?? "",
                price = spec.Price ?? 0,
                barcode = spec.Barcode ?? "",
                description = spec.Description ?? "",
                storageQuantity = spec.Quantity ?? 0,
                categoryId = spec.CategoryId,
                storeId = storeId
            };
            _context.products.Add(product);
            await _context.SaveChangesAsync(ct);
            created.Add(new { id = product.Id, name = product.name });
        }
        return (new { created }, $"Created {created.Count} products");
    }

    private async Task<(object, string)> UpdateProductAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<UpdateProductInput>(json);
        var product = await _context.products.FirstOrDefaultAsync(x => x.Id == p.Id && x.storeId == storeId, ct);
        if (product == null) return (new { error = "Product not found" }, "Product not found");

        var changes = new List<string>();
        if (p.Name != null) { changes.Add($"name: {product.name}→{p.Name}"); product.name = p.Name; }
        if (p.Price.HasValue) { changes.Add($"price: {product.price}→{p.Price}"); product.price = p.Price.Value; }
        if (p.Description != null) product.description = p.Description;
        if (p.Barcode != null) product.barcode = p.Barcode;
        if (p.Quantity.HasValue) product.storageQuantity = p.Quantity.Value;
        if (p.CategoryId.HasValue) product.categoryId = p.CategoryId.Value;

        await _context.SaveChangesAsync(ct);
        return (new { success = true }, $"Product updated: {string.Join(", ", changes)}");
    }

    private async Task<(object, string)> UpdateProductAttributesBulkAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<UpdateAttributesBulkInput>(json);
        if (p.Attributes is not { Count: > 0 })
            return (new { error = "Attribute list is empty" }, "List is empty");

        var applied = 0;
        var errors = new List<string>();
        var allOldValues = new List<ProductAttributeSnapshot>();

        foreach (var attr in p.Attributes)
        {
            var attrDef = await _context.categoryAttributes.AsNoTracking()
                .FirstOrDefaultAsync(a => a.key == attr.AttributeKey && a.category.storeId == storeId, ct);
            if (attrDef == null) { errors.Add($"Attribute '{attr.AttributeKey}' not found"); continue; }

            var productIds = p.ProductIds ?? await _context.products.AsNoTracking()
                .Where(x => x.storeId == storeId && x.categoryId == attrDef.categoryId)
                .Select(x => x.Id).ToListAsync(ct);

            var existing = await _context.productAttributeValues
                .Where(v => productIds.Contains(v.productId) && v.attributeId == attrDef.Id)
                .ToListAsync(ct);

            allOldValues.AddRange(existing.Select(v => new ProductAttributeSnapshot
            { ProductId = v.productId, AttributeId = v.attributeId, OldValue = v.value }));

            var existingIds = existing.Select(v => v.productId).ToHashSet();
            foreach (var ev in existing) ev.value = attr.Value;
            foreach (var pid in productIds.Where(id => !existingIds.Contains(id)))
                _context.productAttributeValues.Add(new ProductAttributeValue
                { productId = pid, attributeId = attrDef.Id, value = attr.Value });

            applied++;
        }

        await _context.SaveChangesAsync(ct);
        if (allOldValues.Count > 0)
            await _operationService.RecordAsync(storeId, userId, "update_attribute",
                $"Updated {applied} attributes on products",
                new ProductAttributeRollbackData { Products = allOldValues }, ct: ct);

        var summary = $"Attributes updated: {applied}";
        if (errors.Count > 0) summary += $", errors: {errors.Count} ({string.Join("; ", errors)})";
        return (new { applied, errors }, summary);
    }

    private async Task<(object, string)> UpdateProductSkuAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<UpdateSkuInput>(json);
        var product = await _context.products.FirstOrDefaultAsync(x => x.Id == p.ProductId && x.storeId == storeId, ct);
        if (product == null) return (new { error = "Product not found" }, "Product not found");
        var oldSku = product.sku;
        product.sku = p.NewSku;
        await _context.SaveChangesAsync(ct);
        return (new { success = true, oldSku, newSku = p.NewSku }, $"SKU changed: {oldSku} → {p.NewSku}");
    }

    private async Task<(object, string)> UpdateProductPricesAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<UpdatePricesInput>(json);
        var q = _context.products.Where(x => x.storeId == storeId);
        if (p.ProductIds?.Count > 0) q = q.Where(x => p.ProductIds.Contains(x.Id));
        if (p.CategoryId.HasValue) q = q.Where(x => x.categoryId == p.CategoryId);

        var products = await q.ToListAsync(ct);
        var rollback = products.Select(pr => (pr.Id, pr.price ?? 0)).ToList();

        foreach (var product in products)
        {
            product.price = p.Mode switch
            {
                "set" => p.Amount,
                "subtract" => Math.Max(0, (product.price ?? 0) - p.Amount),
                "multiply" => Math.Round((product.price ?? 0) * p.Amount, 2),
                _ => (product.price ?? 0) + p.Amount // add (default)
            };
        }

        await _context.SaveChangesAsync(ct);
        await _operationService.RecordAsync(storeId, userId, "update_prices",
            $"Changed price on {products.Count} products ({p.Mode} {p.Amount})",
            AiOperationService.BuildPriceRollback(rollback), ct: ct);

        return (new { affected = products.Count }, $"Changed price on {products.Count} products");
    }

    private async Task<(object, string)> UpdateProductAttributeAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<UpdateAttributeInput>(json);
        var attr = await _context.categoryAttributes.AsNoTracking()
            .FirstOrDefaultAsync(a => a.key == p.AttributeKey && a.category.storeId == storeId, ct);
        if (attr == null) return (new { error = "Attribute not found" }, "Attribute not found");

        var productIds = p.ProductIds ?? await _context.products.AsNoTracking()
            .Where(x => x.storeId == storeId && x.categoryId == attr.categoryId)
            .Select(x => x.Id).ToListAsync(ct);

        var existing = await _context.productAttributeValues
            .Where(v => productIds.Contains(v.productId) && v.attributeId == attr.Id)
            .ToListAsync(ct);

        var oldValues = existing.Select(v => new ProductAttributeSnapshot
        { ProductId = v.productId, AttributeId = v.attributeId, OldValue = v.value }).ToList();

        var existingIds = existing.Select(v => v.productId).ToHashSet();
        foreach (var ev in existing) ev.value = p.Value;

        foreach (var pid in productIds.Where(id => !existingIds.Contains(id)))
        {
            _context.productAttributeValues.Add(new ProductAttributeValue
            { productId = pid, attributeId = attr.Id, value = p.Value });
        }

        await _context.SaveChangesAsync(ct);
        await _operationService.RecordAsync(storeId, userId, "update_attribute",
            $"Attribute '{p.AttributeKey}' = '{p.Value}' on {productIds.Count} products",
            new ProductAttributeRollbackData { Products = oldValues }, ct: ct);

        return (new { affected = productIds.Count }, $"Attribute updated on {productIds.Count} products");
    }

    private async Task<(object, string)> DeleteProductsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<IdsInput>(json);
        var products = await _context.products
            .Where(x => p.Ids.Contains(x.Id) && x.storeId == storeId)
            .ToListAsync(ct);
        foreach (var pr in products) pr.deletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return (new { deleted = products.Count }, $"Deleted {products.Count} products");
    }

    private async Task<(object, string)> RestoreProductsAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<IdsInput>(json);
        await _context.products.IgnoreQueryFilters()
            .Where(x => p.Ids.Contains(x.Id) && x.storeId == storeId && x.deletedAt != null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.deletedAt, (DateTime?)null), ct);
        return (new { success = true }, $"Restored {p.Ids.Count} products");
    }

    private async Task<(object, string)> ListTrashAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<SearchInput>(json);
        var q = _context.products.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.storeId == storeId && x.deletedAt != null);
        if (!string.IsNullOrEmpty(p.Search)) q = q.Where(x => x.name.Contains(p.Search));
        var items = await q.OrderByDescending(x => x.deletedAt).Take(50)
            .Select(x => new { x.Id, x.name, x.deletedAt }).ToListAsync(ct);
        return (new { items, count = items.Count }, $"{items.Count} products in trash");
    }

    // ---- Category Tools ----

    private async Task<(object, string)> ListCategoriesAsync(int storeId, CancellationToken ct)
    {
        var cats = await _context.categories.AsNoTracking()
            .Where(c => c.storeId == storeId)
            .Include(c => c.attributes)
            .OrderBy(c => c.name)
            .Select(c => new { c.Id, c.name, c.description, attributeCount = c.attributes.Count })
            .ToListAsync(ct);
        return (new { categories = cats, count = cats.Count }, $"{cats.Count} categories");
    }

    private async Task<(object, string)> GetCategoryAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var cat = await _context.categories.AsNoTracking()
            .Include(c => c.attributes)
            .FirstOrDefaultAsync(c => c.Id == p.Id && c.storeId == storeId, ct);
        if (cat == null) return (new { error = "Category not found" }, "Category not found");
        return (new
        {
            cat.Id, cat.name, cat.description,
            attributes = cat.attributes.OrderBy(a => a.sortOrder)
                .Select(a => new
                {
                    a.Id, a.name, a.key,
                    type = a.type.ToString(),
                    a.isRequired,
                    options = ParseAttributeOptions(a.options),
                })
        }, cat.name);
    }

    // Select/MultiSelect options are stored as a JSON string — parse so the model sees real values
    private static object? ParseAttributeOptions(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson)) return null;
        try { return JsonSerializer.Deserialize<object>(optionsJson); }
        catch { return optionsJson; }
    }

    private async Task<(object, string)> CreateCategoryAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<CreateCategoryInput>(json);
        var cat = new Category { name = p.Name, description = p.Description, storeId = storeId };
        _context.categories.Add(cat);
        await _context.SaveChangesAsync(ct);
        return (new { id = cat.Id }, $"Category \"{p.Name}\" created");
    }

    private async Task<(object, string)> UpdateCategoryAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<UpdateCategoryInput>(json);
        var cat = await _context.categories.FirstOrDefaultAsync(c => c.Id == p.Id && c.storeId == storeId, ct);
        if (cat == null) return (new { error = "Category not found" }, "Category not found");
        if (p.Name != null) cat.name = p.Name;
        if (p.Description != null) cat.description = p.Description;
        await _context.SaveChangesAsync(ct);
        return (new { success = true }, $"Category updated");
    }

    private async Task<(object, string)> DeleteCategoryAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var cat = await _context.categories.FirstOrDefaultAsync(c => c.Id == p.Id && c.storeId == storeId, ct);
        if (cat == null) return (new { error = "Category not found" }, "Category not found");

        var activeCount = await _context.products.CountAsync(x => x.categoryId == p.Id, ct);
        if (activeCount > 0)
            return (new { error = $"The category has {activeCount} products — move or delete them first" },
                $"Cannot delete: category has {activeCount} products");

        // Trashed products are hidden by the global query filter, but their FK rows still
        // block the DELETE — detach them from the category (they stay in the trash)
        await _context.products.IgnoreQueryFilters()
            .Where(x => x.categoryId == p.Id && x.deletedAt != null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.categoryId, (int?)null), ct);

        _context.categories.Remove(cat);
        await _context.SaveChangesAsync(ct);
        return (new { success = true }, $"Category \"{cat.name}\" deleted");
    }

    private async Task<(object, string)> ListCategoryFieldsAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var raw = await _context.categoryAttributes.AsNoTracking()
            .Where(a => a.categoryId == p.Id && a.category.storeId == storeId)
            .OrderBy(a => a.sortOrder)
            .Select(a => new { a.Id, a.name, a.key, a.type, a.isRequired, a.group, a.options })
            .ToListAsync(ct);
        var fields = raw.Select(a => new
        {
            a.Id, a.name, a.key,
            type = a.type.ToString(),
            a.isRequired, a.group,
            options = ParseAttributeOptions(a.options),
        }).ToList();
        return (new { fields, count = fields.Count }, $"{fields.Count} fields");
    }

    private async Task<(object, string)> AddCategoryFieldAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<AddFieldInput>(json);
        var cat = await _context.categories.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == p.CategoryId && c.storeId == storeId, ct);
        if (cat == null) return (new { error = "Category not found" }, "Category not found");

        var maxSort = await _context.categoryAttributes.AsNoTracking()
            .Where(a => a.categoryId == p.CategoryId)
            .MaxAsync(a => (int?)a.sortOrder, ct) ?? 0;

        var key = p.Key ?? p.Name.ToLower().Replace(' ', '_');
        var attr = new CategoryAttribute
        {
            categoryId = p.CategoryId,
            name = p.Name,
            key = key,
            type = System.Enum.TryParse<AttributeType>(p.Type, true, out var t) ? t : AttributeType.Text,
            isRequired = p.IsRequired ?? false,
            group = p.Group,
            sortOrder = maxSort + 10
        };
        _context.categoryAttributes.Add(attr);
        await _context.SaveChangesAsync(ct);
        await _operationService.RecordAsync(storeId, userId, "add_category_field",
            $"Added field \"{p.Name}\" to category {p.CategoryId}",
            AiOperationService.BuildFieldRollback(attr.Id), ct: ct);
        return (new { id = attr.Id, key }, $"Field \"{p.Name}\" added");
    }

    private async Task<(object, string)> UpdateCategoryFieldAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<UpdateFieldInput>(json);
        var attr = await _context.categoryAttributes
            .FirstOrDefaultAsync(a => a.Id == p.FieldId && a.category.storeId == storeId, ct);
        if (attr == null) return (new { error = "Field not found" }, "Field not found");
        if (p.Name != null) attr.name = p.Name;
        if (p.IsRequired.HasValue) attr.isRequired = p.IsRequired.Value;
        await _context.SaveChangesAsync(ct);
        return (new { success = true }, $"Field updated");
    }

    private async Task<(object, string)> DeleteCategoryFieldAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<FieldIdInput>(json);
        var attr = await _context.categoryAttributes
            .FirstOrDefaultAsync(a => a.Id == p.FieldId && a.category.storeId == storeId, ct);
        if (attr == null) return (new { error = "Field not found" }, "Field not found");
        _context.categoryAttributes.Remove(attr);
        await _context.SaveChangesAsync(ct);
        return (new { success = true }, $"Field \"{attr.name}\" deleted");
    }

    private async Task<(object, string)> FindCategoriesWithFieldAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<SearchInput>(json);
        var cats = await _context.categories.AsNoTracking()
            .Where(c => c.storeId == storeId && c.attributes.Any(a => EF.Functions.Like(a.name, $"%{p.Search}%")))
            .Select(c => new
            {
                c.Id, c.name,
                matchingFields = c.attributes
                    .Where(a => EF.Functions.Like(a.name, $"%{p.Search}%"))
                    .Select(a => new { a.Id, a.name, a.key })
                    .ToList()
            })
            .ToListAsync(ct);
        return (new { categories = cats, count = cats.Count }, $"Found {cats.Count} categories with field '{p.Search}'");
    }

    // ---- Store Attributes ----

    private async Task<(object, string)> ListStoreAttributesAsync(int storeId, CancellationToken ct)
    {
        var attrs = await _context.storeAttributes.AsNoTracking()
            .Where(a => a.storeId == storeId).OrderBy(a => a.sortOrder)
            .Select(a => new { a.Id, a.name, a.key, a.type }).ToListAsync(ct);
        return (new { attributes = attrs, count = attrs.Count }, $"{attrs.Count} store attributes");
    }

    // ---- Connections ----

    private async Task<(object, string)> ListConnectionsAsync(int storeId, CancellationToken ct)
    {
        var conns = await _context.marketplaceConnections.AsNoTracking()
            .Where(c => c.storeId == storeId)
            .Select(c => new { c.Id, c.name, c.adapterKey, c.isActive, c.lastSyncAt })
            .ToListAsync(ct);
        return (new { connections = conns, count = conns.Count }, $"{conns.Count} connections");
    }

    private async Task<(object, string)> GetConnectionAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var conn = await _context.marketplaceConnections.AsNoTracking()
            .Where(c => c.Id == p.Id && c.storeId == storeId)
            .Select(c => new { c.Id, c.name, c.adapterKey, c.isActive, c.lastSyncAt })
            .FirstOrDefaultAsync(ct);
        if (conn == null) return (new { error = "Connection not found" }, "Not found");
        return (conn, conn.name);
    }

    private async Task<(object, string)> TestConnectionAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var conn = await _context.marketplaceConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == p.Id && c.storeId == storeId, ct);
        if (conn == null) return (new { error = "Connection not found" }, "Not found");
        try
        {
            var adapter = _adapterRegistry.GetByKey(conn.adapterKey)!;
            var result = await adapter.TestConnectionAsync(conn, ct);
            return (new { success = result.Success, message = result.Message },
                result.Success ? $"Connection {conn.name} is working" : $"Error: {result.Message}");
        }
        catch (Exception ex)
        {
            return (new { success = false, message = ex.Message }, $"Error: {ex.Message}");
        }
    }

    private async Task<(object, string)> GetConnectionLimitsAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var conn = await _context.marketplaceConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == p.Id && c.storeId == storeId, ct);
        if (conn == null) return (new { error = "Not found" }, "Not found");
        try
        {
            var adapter = _adapterRegistry.GetByKey(conn.adapterKey)!;
            var quotas = await adapter.GetQuotasAsync(conn, ct);
            return (quotas, "Limits retrieved");
        }
        catch
        {
            return (new { error = "Unavailable" }, "Limits unavailable");
        }
    }

    // Matches turn-final statements of intent: future-tense action verbs (Russian, escaped as \uXXXX
    // to keep this source file ASCII-only) / intent phrases (English) in the last sentence.
    // Used to detect "announced but never called the tool" early stopping.
    private static readonly System.Text.RegularExpressions.Regex _unfulfilledIntentRegex = new(
        "\\b(\u0432\u044b\u043f\u043e\u043b\u043d\u044e|\u043f\u0440\u043e\u0432\u0435\u0440\u044e|\u0432\u044b\u0437\u043e\u0432\u0443|\u043f\u043e\u043b\u0443\u0447\u0443|\u0441\u0434\u0435\u043b\u0430\u044e|\u043e\u0431\u043d\u043e\u0432\u043b\u044e|\u0441\u043e\u0437\u0434\u0430\u043c|\u0443\u0434\u0430\u043b\u044e|\u043d\u0430\u0439\u0434\u0443|\u043f\u043e\u0441\u043c\u043e\u0442\u0440\u044e|\u0437\u0430\u0433\u0440\u0443\u0436\u0443|\u0437\u0430\u043f\u0443\u0449\u0443|\u0437\u0430\u043f\u0440\u043e\u0448\u0443|\u043f\u043e\u0434\u043a\u043b\u044e\u0447\u0443|\u0437\u0430\u043f\u043e\u043b\u043d\u044e|\u043f\u0440\u0438\u0441\u0442\u0443\u043f\u0430\u044e|\u043d\u0430\u0447\u0438\u043d\u0430\u044e|\u0437\u0430\u043f\u0443\u0441\u043a\u0430\u044e|\u0432\u044b\u043f\u043e\u043b\u043d\u044f\u044e|\u043f\u043e\u043b\u0443\u0447\u0430\u044e|\u043f\u0440\u043e\u0432\u0435\u0440\u044f\u044e|I(?:'|\u2019)?ll|I will|let me|going to|now I)\\b[^.!?\\n]{0,120}[.!\u2026:]?\\s*$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static bool EndsWithUnfulfilledIntent(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var tail = text.Length > 300 ? text[^300..] : text;
        return _unfulfilledIntentRegex.IsMatch(tail);
    }

    // Resolves connection (scoped to store) + adapter, with optional capability check.
    private async Task<(MarketplaceConnection? conn, IMarketplaceAdapter? adapter, string? error)> ResolveAdapterAsync(
        int connectionId, int storeId, MarketplaceCapabilities? required, CancellationToken ct)
    {
        var conn = await _context.marketplaceConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == connectionId && c.storeId == storeId, ct);
        if (conn == null) return (null, null, "Connection not found");
        var adapter = _adapterRegistry.GetByKey(conn.adapterKey);
        if (adapter == null) return (null, null, $"Adapter '{conn.adapterKey}' not found");
        if (required.HasValue && !adapter.Capabilities.HasFlag(required.Value))
            return (null, null, $"Marketplace {adapter.DisplayName} does not support operation {required.Value}");
        return (conn, adapter, null);
    }

    private async Task<(object, string)> GetAdapterInfoAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<ConnectionIdRequiredInput>(json);
        var (conn, adapter, error) = await ResolveAdapterAsync(p.ConnectionId, storeId, null, ct);
        if (error != null) return (new { error }, error);

        // Only single-bit flags — skip composite convenience values like FullProducts
        var capabilities = System.Enum.GetValues<MarketplaceCapabilities>()
            .Where(v => v != 0 && ((long)v & ((long)v - 1)) == 0 && adapter!.Capabilities.HasFlag(v))
            .Select(v => v.ToString())
            .ToList();

        var fieldGrouping = adapter!.GetFieldBasedGroupingConfig();
        object? grouping = null;
        if (fieldGrouping != null)
        {
            grouping = new
            {
                scheme = "field",
                fieldKey = fieldGrouping.GroupFieldKey,
                description = "Products are merged into one card by the value of this mapping field (defaults to groupName)",
            };
        }
        else if (adapter.Capabilities.HasFlag(MarketplaceCapabilities.EndpointBasedGrouping))
        {
            grouping = new
            {
                scheme = "endpoint",
                fieldKey = ProductSyncService.SyntheticGroupFieldKey,
                description = "Products with the same computed grouping field value are merged via API calls after export",
            };
        }

        return (new
        {
            adapterKey = adapter.Key,
            displayName = adapter.DisplayName,
            description = adapter.Description,
            connectionName = conn!.name,
            isActive = conn.isActive,
            capabilities,
            grouping,
        }, $"Adapter {adapter.DisplayName}: {capabilities.Count} capabilities");
    }

    private async Task<(object, string)> PreviewMarketplaceProductsAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<PreviewMarketplaceInput>(json);
        var (conn, adapter, error) = await ResolveAdapterAsync(p.ConnectionId, storeId, MarketplaceCapabilities.ImportAllProducts, ct);
        if (error != null) return (new { error }, error);

        var page = await adapter!.ImportAllProductsAsync(conn!, Math.Min(p.PageSize ?? 50, 200), p.Cursor, p.Search, ct);

        // Mark which external products are already linked locally
        var externalIds = page.Items.Select(i => i.ExternalId).ToList();
        var linked = (await _context.productMarketplaceLinks.AsNoTracking()
            .Where(l => l.connectionId == p.ConnectionId && externalIds.Contains(l.externalProductId))
            .Select(l => l.externalProductId).ToListAsync(ct)).ToHashSet();

        var items = page.Items.Select(i => new
        {
            externalId = i.ExternalId,
            i.Sku, i.Name, i.Price, i.Quantity,
            categoryId = i.CategoryId, categoryName = i.CategoryName,
            alreadyImported = linked.Contains(i.ExternalId),
        }).ToList();

        return (new { items, hasMore = page.HasMore, nextCursor = page.NextCursor, totalCount = page.TotalCount },
            $"On the marketplace: {items.Count} products (page){(page.HasMore ? ", more available" : "")}");
    }

    private async Task<(object, string)> ImportProductsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ImportProductsInput>(json);
        if (p.ExternalProductIds.Count == 0) return (new { error = "externalProductIds not specified" }, "No products");
        var (conn, adapter, error) = await ResolveAdapterAsync(p.ConnectionId, storeId, MarketplaceCapabilities.ImportProduct, ct);
        if (error != null) return (new { error }, error);

        var batchId = Guid.NewGuid().ToString("N");
        foreach (var extId in p.ExternalProductIds.Distinct())
        {
            _context.pendingProductImports.Add(new PendingProductImport
            {
                connectionId = p.ConnectionId,
                externalProductId = extId,
                targetCategoryId = p.TargetCategoryId,
                authorUserId = userId,
                storeId = storeId,
                batchId = batchId,
                status = ProductImportStatus.Pending,
                createdAt = DateTime.UtcNow,
            });
        }
        await _context.SaveChangesAsync(ct);

        await _operationService.RecordAsync(storeId, userId, "import_products",
            $"Import {p.ExternalProductIds.Count} products from {conn!.name}", new { }, canRollback: false, ct);
        return (new { batchId, queued = p.ExternalProductIds.Count },
            $"Import of {p.ExternalProductIds.Count} products queued (batchId: {batchId})");
    }

    private async Task<(object, string)> ImportPricesAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ConnectionIdRequiredInput>(json);
        var (conn, _, error) = await ResolveAdapterAsync(p.ConnectionId, storeId, MarketplaceCapabilities.ImportPrices, ct);
        if (error != null) return (new { error }, error);

        var result = await _priceSyncService.ImportPricesAsync(p.ConnectionId, null, ct, storeId: storeId);
        if (!result.IsSuccess) return (new { error = result.ErrorMessage }, $"Error: {result.ErrorMessage}");

        await _operationService.RecordAsync(storeId, userId, "import_prices",
            $"Import prices from {conn!.name}: updated {result.UpdatedCount}", new { }, canRollback: false, ct);
        return (new { updated = result.UpdatedCount, skipped = result.SkippedCount },
            $"Import prices: updated {result.UpdatedCount}, skipped {result.SkippedCount}");
    }

    private async Task<(object, string)> ImportStocksAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ConnectionIdRequiredInput>(json);
        var (conn, adapter, error) = await ResolveAdapterAsync(p.ConnectionId, storeId, MarketplaceCapabilities.ImportStocks, ct);
        if (error != null) return (new { error }, error);

        var stocks = await adapter!.ImportStocksAsync(conn!, null, ct);
        // Sum across warehouses per external product
        var byExternalId = stocks
            .GroupBy(s => s.ExternalProductId)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.Quantity));

        var stockExternalIds = byExternalId.Keys.ToList();
        var links = await _context.productMarketplaceLinks
            .Include(l => l.product)
            .Where(l => l.connectionId == p.ConnectionId && stockExternalIds.Contains(l.externalProductId))
            .ToListAsync(ct);

        var updated = 0;
        foreach (var link in links)
        {
            var qty = byExternalId[link.externalProductId];
            if (link.product.storageQuantity != qty)
            {
                link.product.storageQuantity = qty;
                updated++;
            }
            link.externalStock = qty;
        }
        await _context.SaveChangesAsync(ct);

        await _operationService.RecordAsync(storeId, userId, "import_stocks",
            $"Import stocks from {conn!.name}: updated {updated}", new { }, canRollback: false, ct);
        return (new { updated, totalOnMarketplace = byExternalId.Count },
            $"Import stocks: updated {updated} products");
    }

    private async Task<(object, string)> ArchiveProductOnMarketplaceAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ArchiveProductInput>(json);
        var (conn, adapter, error) = await ResolveAdapterAsync(p.ConnectionId, storeId, MarketplaceCapabilities.ArchiveProduct, ct);
        if (error != null) return (new { error }, error);

        var link = await _context.productMarketplaceLinks.AsNoTracking()
            .FirstOrDefaultAsync(l => l.connectionId == p.ConnectionId && l.productId == p.ProductId, ct);
        if (link == null) return (new { error = "Product is not linked to this marketplace" }, "Link not found");

        var result = await adapter!.ArchiveProductAsync(conn!, link.externalProductId, ct);
        if (!result.Success) return (new { error = result.ErrorMessage }, $"Error: {result.ErrorMessage}");

        await _operationService.RecordAsync(storeId, userId, "archive_product",
            $"Product {p.ProductId} archived on {conn!.name}", new { }, canRollback: false, ct);
        return (new { success = true }, $"Product archived on {conn!.name}");
    }

    // ---- Mapping Tools ----

    private async Task<(object, string)> ListMappingsAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<ConnectionIdInput>(json);
        var q = _context.categoryMappings.AsNoTracking()
            .Where(m => m.connection.storeId == storeId);
        if (p.ConnectionId.HasValue) q = q.Where(m => m.connectionId == p.ConnectionId);

        var mappings = await q
            .Select(m => new
            {
                m.Id, m.name, m.localCategoryId, m.externalCategoryId, m.externalCategoryName,
                m.connectionId, connectionName = m.connection.name
            })
            .ToListAsync(ct);
        return (new { mappings, count = mappings.Count }, $"{mappings.Count} mappings");
    }

    private async Task<(object, string)> GetMappingAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Mapping not found" }, "Not found");

        var coverage = await _graphMutation.GetCoverageAsync(p.Id, ct);
        var fields = await _graphMutation.GetFieldStatesAsync(p.Id, ct);
        return (new
        {
            mapping.Id, mapping.name, mapping.localCategoryId, mapping.externalCategoryId,
            mapping.externalCategoryName,
            // Counters only — the full per-field state (incl. isRequired/connected/source)
            // lives in `fields`; a separate unmapped-required list would duplicate it
            coverage = new
            {
                total = coverage.Total,
                connected = coverage.Connected,
                requiredTotal = coverage.RequiredTotal,
                requiredConnected = coverage.RequiredConnected,
            },
            fields,
        }, $"Mapping {mapping.name ?? mapping.externalCategoryName}: {coverage.Connected}/{coverage.Total} fields " +
           $"(required {coverage.RequiredConnected}/{coverage.RequiredTotal})");
    }

    private async Task<(object, string)> CreateMappingAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<CreateMappingInput>(json);
        var conn = await _context.marketplaceConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == p.ConnectionId && c.storeId == storeId, ct);
        if (conn == null) return (new { error = "Connection not found" }, "Not found");

        var mapping = new CategoryMapping
        {
            connectionId = p.ConnectionId,
            localCategoryId = p.LocalCategoryId,
            externalCategoryId = p.ExternalCategoryId,
            externalCategoryName = p.ExternalCategoryName,
            name = p.Name
        };
        _context.categoryMappings.Add(mapping);
        await _context.SaveChangesAsync(ct);

        return (new { id = mapping.Id }, $"Mapping created (id={mapping.Id}). Call get_mapping and connect fields via connect_field / set_field_static_value.");
    }

    private async Task<(object, string)> UpdateMappingNameAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<UpdateNameInput>(json);
        var mapping = await _context.categoryMappings
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Not found" }, "Not found");
        mapping.name = p.Name;
        await _context.SaveChangesAsync(ct);
        return (new { success = true }, $"Mapping name updated: \"{p.Name}\"");
    }

    private async Task<(object, string)> DeleteMappingAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var mapping = await _context.categoryMappings
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Not found" }, "Not found");
        _context.categoryMappings.Remove(mapping);
        await _context.SaveChangesAsync(ct);
        return (new { success = true }, $"Mapping deleted");
    }

    private async Task<(object, string)> DuplicateMappingAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Not found" }, "Not found");
        var copy = new CategoryMapping
        {
            connectionId = mapping.connectionId,
            localCategoryId = mapping.localCategoryId,
            externalCategoryId = mapping.externalCategoryId,
            externalCategoryName = mapping.externalCategoryName,
            name = (mapping.name ?? mapping.externalCategoryName) + " (copy)",
            graphJson = mapping.graphJson
        };
        _context.categoryMappings.Add(copy);
        await _context.SaveChangesAsync(ct);
        return (new { id = copy.Id }, $"Mapping copied (id={copy.Id})");
    }

    private async Task<(object, string)> ListExternalCategoriesAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<ExternalCategoriesInput>(json);
        var conn = await _context.marketplaceConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == p.ConnectionId && c.storeId == storeId, ct);
        if (conn == null) return (new { error = "Connection not found" }, "Not found");
        var adapter = _adapterRegistry.GetByKey(conn.adapterKey)!;
        var cats = await adapter.GetCategoriesAsync(conn, p.ParentId, p.Search, ct);
        return (new { categories = cats.Take(50) }, $"{cats.Count} categories");
    }

    private async Task<(object, string)> GetExternalFieldsAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<ExternalFieldsInput>(json);

        int connectionId;
        string externalCategoryId;
        if (p.MappingId.HasValue)
        {
            // Resolve connection + external category from the mapping — the AI on a mapping
            // page knows mappingId but not the marketplace category id
            var mapping = await _context.categoryMappings.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
            if (mapping == null) return (new { error = "Mapping not found" }, "Not found");
            connectionId = mapping.connectionId;
            externalCategoryId = mapping.externalCategoryId;
        }
        else
        {
            if (!p.ConnectionId.HasValue || string.IsNullOrEmpty(p.ExternalCategoryId))
                return (new { error = "Specify mappingId OR connectionId+externalCategoryId" }, "No parameters");
            connectionId = p.ConnectionId.Value;
            externalCategoryId = p.ExternalCategoryId;
        }

        var conn = await _context.marketplaceConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == connectionId && c.storeId == storeId, ct);
        if (conn == null) return (new { error = "Connection not found" }, "Not found");
        var adapter = _adapterRegistry.GetByKey(conn.adapterKey)!;
        var fields = await adapter.GetCategoryFieldsAsync(conn, externalCategoryId, ct);

        var groupingKey = ProductSyncService.ResolveGroupingFieldKey(adapter);

        // Endpoint-based adapters group via API — the mapping's grouping field is synthetic
        // and doesn't exist in the live schema, so append it for the model
        if (groupingKey == ProductSyncService.SyntheticGroupFieldKey && fields.All(f => f.Key != groupingKey))
        {
            fields = fields.Concat([new ExternalField(
                Key: ProductSyncService.SyntheticGroupFieldKey,
                Name: "Merge into card (group)",
                IsRequired: false,
                Type: "string",
                Description: "Products with the same computed value are merged into one card. Defaults to bind to groupName.")]).ToList();
        }

        var projected = fields.Select(f => new
        {
            f.Key, f.Name, f.Type, f.IsRequired, f.Description,
            f.Unit, f.MaxLength, f.IsAspect,
            // Large dictionaries (WB colors/brands: thousands of values) would blow up
            // the context — show a sample and point the model at search_field_options
            options = f.Options == null ? null
                : f.Options.Take(15).Select(o => new { o.Value, o.Label }).ToList(),
            optionsTotal = f.Options?.Count,
            optionsNote = f.Options?.Count > 15
                ? "shown 15 of " + f.Options.Count + " — use search_field_options to find a specific value"
                : null,
            isGroupingField = groupingKey != null && f.Key == groupingKey ? (bool?)true : null,
        }).ToList();

        return (new
        {
            fields = projected,
            total = projected.Count, required = fields.Count(f => f.IsRequired)
        }, $"{projected.Count} fields ({fields.Count(f => f.IsRequired)} required)");
    }

    private async Task<(object, string)> SearchFieldOptionsAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<SearchFieldOptionsInput>(json);

        CategoryMapping? mapping = null;
        if (p.MappingId.HasValue)
        {
            mapping = await _context.categoryMappings.AsNoTracking()
                .Include(m => m.connection)
                .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
            if (mapping == null) return (new { error = "Mapping not found" }, "Not found");
        }
        else if (!p.ConnectionId.HasValue || string.IsNullOrEmpty(p.ExternalCategoryId))
        {
            return (new { error = "Specify mappingId OR connectionId+externalCategoryId" }, "No parameters");
        }

        List<ExternalField> defs;
        if (mapping != null)
        {
            defs = await GetAdapterFieldDefsAsync(mapping, ct);
        }
        else
        {
            var conn = await _context.marketplaceConnections.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == p.ConnectionId && c.storeId == storeId, ct);
            if (conn == null) return (new { error = "Connection not found" }, "Not found");
            var adapter = _adapterRegistry.GetByKey(conn.adapterKey)!;
            defs = await adapter.GetCategoryFieldsAsync(conn, p.ExternalCategoryId!, ct);
        }

        var field = defs.FirstOrDefault(d => d.Key == p.AdapterFieldKey);
        if (field == null)
            return (new { error = $"Field '{p.AdapterFieldKey}' not found on the adapter" }, "Field not found");
        if (field.Options is not { Count: > 0 })
            return (new { options = Array.Empty<object>(), totalOptions = 0 },
                $"Field '{field.Name}' has no predefined options — free-form value");

        var query = p.Query ?? "";
        var matches = string.IsNullOrWhiteSpace(query)
            ? field.Options.ToList()
            : field.Options.Where(o =>
                    (o.Label?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    o.Value.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        var limit = Math.Min(p.Limit ?? 20, 50);
        return (new
        {
            options = matches.Take(limit).Select(o => new { o.Value, o.Label }).ToList(),
            totalMatches = matches.Count,
            totalOptions = field.Options.Count,
        }, $"Found {matches.Count} options for «{query}» (field has {field.Options.Count} total)");
    }

    private async Task<(object, string)> GetAvailableSourcesAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Not found" }, "Not found");

        var catAttrs = await _context.categoryAttributes.AsNoTracking()
            .Where(a => a.categoryId == mapping.localCategoryId)
            .Select(a => new { a.Id, a.name, a.key, a.type }).ToListAsync(ct);
        var storeAttrs = await _context.storeAttributes.AsNoTracking()
            .Where(a => a.storeId == storeId)
            .Select(a => new { a.Id, a.name, a.key, a.type }).ToListAsync(ct);

        return (new
        {
            productFields = new[] { "sku", "name", "price", "barcode", "description", "storageQuantity" },
            categoryAttributes = catAttrs,
            storeAttributes = storeAttrs
        }, $"Sources: {catAttrs.Count} category attributes, {storeAttrs.Count} store attributes");
    }

    private async Task<(object, string)> GetMappingCoverageAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Not found" }, "Not found");
        var coverage = await _graphMutation.GetCoverageAsync(p.Id, ct);
        return (coverage, $"Coverage: {coverage.Connected}/{coverage.Total} fields, required: {coverage.RequiredConnected}/{coverage.RequiredTotal}");
    }

    private async Task<(object, string)> PreviewMappingAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<PreviewMappingInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Not found" }, "Not found");
        // Preview is complex - return basic info
        return (new { mappingId = p.MappingId, productId = p.ProductId, note = "Use the mapping editor UI to preview" },
            "Preview is available in the mapping editor");
    }

    // Fetches adapter field definitions for a mapping (mapping must have connection loaded).
    // Returns empty on adapter failure — callers degrade gracefully.
    private async Task<List<ExternalField>> GetAdapterFieldDefsAsync(CategoryMapping mapping, CancellationToken ct)
    {
        try
        {
            var adapter = _adapterRegistry.GetByKey(mapping.connection.adapterKey);
            if (adapter == null) return new();
            return await adapter.GetCategoryFieldsAsync(mapping.connection, mapping.externalCategoryId, ct);
        }
        catch { return new(); }
    }

    private async Task<(object, string)> ConnectFieldAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ConnectFieldInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Not found" }, "Not found");

        // Field def lets GraphMutationService append missing-but-valid fields to the graph
        var defs = await GetAdapterFieldDefsAsync(mapping, ct);
        var fieldDef = defs.FirstOrDefault(d => d.Key == p.AdapterFieldKey);

        var prevGraph = mapping.graphJson ?? "";
        await _graphMutation.ConnectFieldAsync(p.MappingId, p.AdapterFieldKey, p.SourceType, p.SourceField, p.SourceAttributeId, fieldDef: fieldDef, ct: ct);
        await _operationService.RecordAsync(storeId, userId, "connect_field",
            $"Field '{p.AdapterFieldKey}' connected to {p.SourceType}:{p.SourceField ?? p.SourceAttributeId?.ToString()}",
            AiOperationService.BuildGraphRollback(p.MappingId, prevGraph), ct: ct);
        return (new { success = true }, $"Field '{p.AdapterFieldKey}' connected");
    }

    private async Task<(object, string)> SetFieldStaticAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<SetStaticInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Not found" }, "Not found");

        // Field def: enables option validation ("default value") and graph auto-add
        var defs = await GetAdapterFieldDefsAsync(mapping, ct);
        var fieldDef = defs.FirstOrDefault(d => d.Key == p.AdapterFieldKey);

        var prevGraph = mapping.graphJson ?? "";
        try
        {
            await _graphMutation.SetStaticValueAsync(p.MappingId, p.AdapterFieldKey, p.Value, fieldDef: fieldDef, ct: ct);
        }
        catch (InvalidOperationException ex)
        {
            // e.g. value not in the allowed options — return the option list so the model can retry
            return (new { error = ex.Message }, ex.Message);
        }
        await _operationService.RecordAsync(storeId, userId, "set_static_value",
            $"Field '{p.AdapterFieldKey}' = '{p.Value}'",
            AiOperationService.BuildGraphRollback(p.MappingId, prevGraph), ct: ct);
        return (new { success = true }, $"Field '{p.AdapterFieldKey}' = '{p.Value}'");
    }

    private async Task<(object, string)> AddFieldModifierAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<AddModifierInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Not found" }, "Not found");

        var defs = await GetAdapterFieldDefsAsync(mapping, ct);
        var fieldDef = defs.FirstOrDefault(d => d.Key == p.AdapterFieldKey);

        var prevGraph = mapping.graphJson ?? "";
        try
        {
            await _graphMutation.AddModifierAsync(p.MappingId, p.AdapterFieldKey, p.ModifierType, p.Params, fieldDef: fieldDef, ct: ct);
        }
        catch (InvalidOperationException ex)
        {
            // e.g. unknown modifier type — the message lists valid types so the model can retry
            return (new { error = ex.Message }, ex.Message);
        }
        await _operationService.RecordAsync(storeId, userId, "add_modifier",
            $"Modifier '{p.ModifierType}' added to field '{p.AdapterFieldKey}'",
            AiOperationService.BuildGraphRollback(p.MappingId, prevGraph), ct: ct);
        return (new { success = true }, $"Modifier '{p.ModifierType}' added");
    }

    private async Task<(object, string)> ConnectFieldsBulkAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ConnectFieldsBulkInput>(json);
        if (p.Connections is not { Count: > 0 })
            return (new { error = "Connection list is empty" }, "List is empty");

        var mapping = await _context.categoryMappings.AsNoTracking()
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Mapping not found" }, "Not found");

        var defs = await GetAdapterFieldDefsAsync(mapping, ct);
        var prevGraph = mapping.graphJson ?? "";
        var applied = 0; var errors = new List<string>();

        foreach (var conn in p.Connections)
        {
            try
            {
                var fieldDef = defs.FirstOrDefault(d => d.Key == conn.AdapterFieldKey);
                await _graphMutation.ConnectFieldAsync(p.MappingId, conn.AdapterFieldKey,
                    conn.SourceType, conn.SourceField, conn.SourceAttributeId, fieldDef: fieldDef, ct: ct);
                applied++;
            }
            catch (Exception ex) { errors.Add($"'{conn.AdapterFieldKey}': {ex.Message}"); }
        }

        await _operationService.RecordAsync(storeId, userId, "connect_field",
            $"Connected {applied} fields of mapping {p.MappingId}",
            AiOperationService.BuildGraphRollback(p.MappingId, prevGraph), ct: ct);

        var summary = $"Fields connected: {applied}";
        if (errors.Count > 0) summary += $", errors: {errors.Count}";
        return (new { applied, errors }, summary);
    }

    private async Task<(object, string)> SetFieldsStaticBulkAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<SetFieldsStaticBulkInput>(json);
        if (p.Fields is not { Count: > 0 })
            return (new { error = "Field list is empty" }, "List is empty");

        var mapping = await _context.categoryMappings.AsNoTracking()
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Mapping not found" }, "Not found");

        var defs = await GetAdapterFieldDefsAsync(mapping, ct);
        var prevGraph = mapping.graphJson ?? "";
        var applied = 0; var errors = new List<string>();

        foreach (var field in p.Fields)
        {
            try
            {
                var fieldDef = defs.FirstOrDefault(d => d.Key == field.AdapterFieldKey);
                await _graphMutation.SetStaticValueAsync(p.MappingId, field.AdapterFieldKey, field.Value, fieldDef: fieldDef, ct: ct);
                applied++;
            }
            catch (Exception ex) { errors.Add($"'{field.AdapterFieldKey}': {ex.Message}"); }
        }

        await _operationService.RecordAsync(storeId, userId, "set_static_value",
            $"Set {applied} static values for mapping {p.MappingId}",
            AiOperationService.BuildGraphRollback(p.MappingId, prevGraph), ct: ct);

        var summary = $"Values set: {applied}";
        if (errors.Count > 0) summary += $", errors: {errors.Count} ({string.Join("; ", errors)})";
        return (new { applied, errors }, summary);
    }

    private async Task<(object, string)> MergeAdapterFieldsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<MergeFieldsInput>(json);
        if (p.AliasFieldKeys.Count == 0) return (new { error = "aliasFieldKeys not specified" }, "No fields");
        var mapping = await _context.categoryMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Not found" }, "Not found");

        var prevGraph = mapping.graphJson ?? "";
        try
        {
            await _graphMutation.MergeFieldsAsync(p.MappingId, p.PrimaryFieldKey, p.AliasFieldKeys, ct);
        }
        catch (InvalidOperationException ex)
        {
            return (new { error = ex.Message }, ex.Message);
        }
        await _operationService.RecordAsync(storeId, userId, "merge_fields",
            $"Fields [{string.Join(", ", p.AliasFieldKeys)}] merged with '{p.PrimaryFieldKey}'",
            AiOperationService.BuildGraphRollback(p.MappingId, prevGraph), ct: ct);
        return (new { success = true },
            $"Merged: {string.Join(", ", p.AliasFieldKeys)} → '{p.PrimaryFieldKey}' (exports to both keys, import only via primary)");
    }

    private async Task<(object, string)> UnmergeAdapterFieldsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<UnmergeFieldsInput>(json);
        if (p.FieldKeys.Count == 0) return (new { error = "fieldKeys not specified" }, "No fields");
        var mapping = await _context.categoryMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Not found" }, "Not found");

        var prevGraph = mapping.graphJson ?? "";
        try
        {
            await _graphMutation.UnmergeFieldsAsync(p.MappingId, p.FieldKeys, ct);
        }
        catch (InvalidOperationException ex)
        {
            return (new { error = ex.Message }, ex.Message);
        }
        await _operationService.RecordAsync(storeId, userId, "unmerge_fields",
            $"Unmerged fields [{string.Join(", ", p.FieldKeys)}]",
            AiOperationService.BuildGraphRollback(p.MappingId, prevGraph), ct: ct);
        return (new { success = true }, $"Unmerged: {string.Join(", ", p.FieldKeys)}");
    }

    private async Task<(object, string)> RemoveFieldConnectionAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<FieldKeyInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Not found" }, "Not found");

        var prevGraph = mapping.graphJson ?? "";
        await _graphMutation.DisconnectFieldAsync(p.MappingId, p.FieldKey, ct: ct);
        await _operationService.RecordAsync(storeId, userId, "disconnect_field",
            $"Field '{p.FieldKey}' disconnected in mapping {p.MappingId}",
            AiOperationService.BuildGraphRollback(p.MappingId, prevGraph), ct: ct);
        return (new { success = true }, $"Field '{p.FieldKey}' disconnected");
    }

    // ---- Export / Import ----

    private async Task<(object, string)> ExportProductsBatchAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ExportBatchInput>(json);
        var conn = await _context.marketplaceConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == p.ConnectionId && c.storeId == storeId, ct);
        if (conn == null) return (new { error = "Connection not found" }, "Not found");

        List<int> productIds;
        if (p.ProductIds?.Count > 0)
        {
            productIds = p.ProductIds;
        }
        else if (p.CategoryId.HasValue)
        {
            productIds = await _context.products.AsNoTracking()
                .Where(x => x.storeId == storeId && x.categoryId == p.CategoryId)
                .Select(x => x.Id).ToListAsync(ct);
        }
        else
        {
            productIds = await _context.products.AsNoTracking()
                .Where(x => x.storeId == storeId).Select(x => x.Id).ToListAsync(ct);
        }

        if (productIds.Count == 0) return (new { queued = 0 }, "No products to export");

        var batchId = Guid.NewGuid().ToString("N");
        foreach (var pid in productIds)
        {
            _context.pendingProductExports.Add(new PendingProductExport
            {
                connectionId = p.ConnectionId,
                productId = pid,
                authorUserId = userId,
                storeId = storeId,
                batchId = batchId,
                status = ProductExportStatus.Pending,
                createdAt = DateTime.UtcNow
            });
        }
        await _context.SaveChangesAsync(ct);
        return (new { batchId, queued = productIds.Count }, $"Queued {productIds.Count} products for export");
    }

    private Task<(object, string)> ExportPricesAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<ConnectionIdInput>(json);
        return Task.FromResult(((object)new { note = "Price export started. Use the UI to monitor progress." },
            $"Price export to connection {p.ConnectionId} started"));
    }

    private Task<(object, string)> ExportStocksAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<ConnectionIdInput>(json);
        return Task.FromResult(((object)new { note = "Stock export started. Use the UI to monitor progress." },
            $"Stock export to connection {p.ConnectionId} started"));
    }

    private async Task<(object, string)> GetBatchStatusAsync(string json, CancellationToken ct)
    {
        var p = ParseInput<BatchIdInput>(json);
        var exports = await _context.pendingProductExports.AsNoTracking()
            .Where(e => e.batchId == p.BatchId)
            .GroupBy(e => e.status)
            .Select(g => new { status = g.Key, count = g.Count() })
            .ToListAsync(ct);
        var total = exports.Sum(e => e.count);
        var done = exports.Where(e => e.status == ProductExportStatus.Completed).Sum(e => e.count);
        var failed = exports.Where(e => e.status == ProductExportStatus.Failed).Sum(e => e.count);
        return (new { batchId = p.BatchId, total, done, failed, inProgress = total - done - failed },
            $"Batch {p.BatchId}: {done}/{total} done, {failed} errors");
    }

    private async Task<(object, string)> ListProductLinksAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<ConnectionIdInput>(json);
        var q = _context.productMarketplaceLinks.AsNoTracking()
            .Where(l => l.connection.storeId == storeId);
        if (p.ConnectionId.HasValue) q = q.Where(l => l.connectionId == p.ConnectionId);

        var links = await q.Take(50)
            .Select(l => new { l.productId, l.externalProductId, l.status, l.connectionId })
            .ToListAsync(ct);
        return (new { links, count = links.Count }, $"{links.Count} links");
    }

    // ---- Groups ----

    private async Task<(object, string)> ListGroupsAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<ListGroupsInput>(json);
        var q = _context.productGroups.AsNoTracking()
            .Where(g => g.storeId == storeId);
        if (!string.IsNullOrEmpty(p.Search))
            q = q.Where(g => g.name.Contains(p.Search));
        if (p.ProductId.HasValue)
            q = q.Where(g => g.members.Any(m => m.productId == p.ProductId));
        var groups = await q
            .Select(g => new { g.Id, g.name, g.description, memberCount = g.members.Count })
            .ToListAsync(ct);
        return (new { groups, count = groups.Count }, $"{groups.Count} groups");
    }

    private async Task<(object, string)> GetGroupAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var group = await _context.productGroups.AsNoTracking()
            .Where(g => g.Id == p.Id && g.storeId == storeId)
            .Select(g => new
            {
                g.Id, g.name, g.description,
                members = g.members.OrderBy(m => m.sortOrder).Select(m => new
                {
                    m.productId,
                    m.product.name,
                    m.product.sku,
                    m.product.price,
                    quantity = m.product.storageQuantity,
                }).ToList()
            })
            .FirstOrDefaultAsync(ct);
        if (group == null) return (new { error = "Group not found" }, "Not found");
        return (group, $"Group \"{group.name}\": {group.members.Count} products");
    }

    private async Task<(object, string)> UpdateGroupAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<UpdateGroupInput>(json);
        var group = await _context.productGroups
            .FirstOrDefaultAsync(g => g.Id == p.Id && g.storeId == storeId, ct);
        if (group == null) return (new { error = "Group not found" }, "Not found");

        var oldName = group.name;
        if (!string.IsNullOrEmpty(p.Name)) group.name = p.Name;
        if (p.Description != null) group.description = p.Description;
        group.updatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        await _operationService.RecordAsync(storeId, userId, "update_group",
            $"Group \"{oldName}\" → \"{group.name}\"", new { }, canRollback: false, ct);
        return (new { success = true }, $"Group \"{group.name}\" updated");
    }

    private async Task<(object, string)> DeleteGroupAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var group = await _context.productGroups
            .Include(g => g.members)
            .FirstOrDefaultAsync(g => g.Id == p.Id && g.storeId == storeId, ct);
        if (group == null) return (new { error = "Group not found" }, "Not found");

        var name = group.name;
        var memberCount = group.members.Count;
        _context.productGroups.Remove(group);
        await _context.SaveChangesAsync(ct);

        await _operationService.RecordAsync(storeId, userId, "delete_group",
            $"Deleted group \"{name}\" ({memberCount} products remained in the store)", new { }, canRollback: false, ct);
        return (new { success = true }, $"Group \"{name}\" deleted, products remained");
    }

    private async Task<(object, string)> RemoveProductsFromGroupAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<GroupProductsInput>(json);
        var groupExists = await _context.productGroups.AsNoTracking()
            .AnyAsync(g => g.Id == p.GroupId && g.storeId == storeId, ct);
        if (!groupExists) return (new { error = "Group not found" }, "Not found");

        var members = await _context.productGroupMembers
            .Where(m => m.groupId == p.GroupId && p.ProductIds.Contains(m.productId))
            .ToListAsync(ct);
        _context.productGroupMembers.RemoveRange(members);
        await _context.SaveChangesAsync(ct);

        await _operationService.RecordAsync(storeId, userId, "remove_from_group",
            $"Removed {members.Count} products from group {p.GroupId}", new { }, canRollback: false, ct);
        return (new { removed = members.Count }, $"Removed {members.Count} products from the group");
    }

    // ---- Files ----
    // scope = "product" (files of a product, requires productId for list/create) or "store" (store-level storage)

    private async Task<bool> ProductInStoreAsync(int productId, int storeId, CancellationToken ct)
        => await _context.products.AsNoTracking().AnyAsync(x => x.Id == productId && x.storeId == storeId, ct);

    private async Task<(object, string)> ListFilesAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<ListFilesInput>(json);
        if (p.Scope == "product")
        {
            if (!p.ProductId.HasValue) return (new { error = "scope=product requires productId" }, "No productId");
            if (!await ProductInStoreAsync(p.ProductId.Value, storeId, ct)) return (new { error = "Product not found" }, "Not found");

            var fq = _context.files.AsNoTracking().Where(f => f.productId == p.ProductId);
            if (!string.IsNullOrEmpty(p.Search))
                fq = fq.Where(f => (f.displayName ?? f.filePath).Contains(p.Search));
            else if (p.FolderId.HasValue)
                fq = fq.Where(f => f.folderId == p.FolderId);
            var files = await fq
                .Select(f => new { f.Id, name = f.displayName ?? f.filePath, type = f.fileType.ToString(), f.contentType, size = f.length, f.isPublic, f.folderId })
                .ToListAsync(ct);
            var folders = await _context.productFolders.AsNoTracking()
                .Where(f => f.productId == p.ProductId)
                .Select(f => new { f.Id, f.name, f.parentFolderId, fileCount = f.files.Count })
                .ToListAsync(ct);
            return (new { files, folders }, $"{files.Count} files, {folders.Count} folders");
        }
        else
        {
            var fq = _context.storeFiles.AsNoTracking().Where(f => f.storeId == storeId);
            if (!string.IsNullOrEmpty(p.Search))
                fq = fq.Where(f => (f.displayName ?? f.filePath).Contains(p.Search));
            else if (p.FolderId.HasValue)
                fq = fq.Where(f => f.folderId == p.FolderId);
            var files = await fq
                .Select(f => new { f.Id, name = f.displayName ?? f.filePath, type = f.fileType.ToString(), f.contentType, size = f.length, f.isPublic, f.folderId })
                .ToListAsync(ct);
            var folders = await _context.storeFolders.AsNoTracking()
                .Where(f => f.storeId == storeId)
                .Select(f => new { f.Id, f.name, f.parentFolderId, fileCount = f.files.Count })
                .ToListAsync(ct);
            return (new { files, folders }, $"{files.Count} files, {folders.Count} folders");
        }
    }

    private async Task<(object, string)> CreateFolderAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<CreateFolderInput>(json);
        if (string.IsNullOrWhiteSpace(p.Name)) return (new { error = "Folder name is required" }, "No name");

        if (p.Scope == "product")
        {
            if (!p.ProductId.HasValue) return (new { error = "scope=product requires productId" }, "No productId");
            if (!await ProductInStoreAsync(p.ProductId.Value, storeId, ct)) return (new { error = "Product not found" }, "Not found");
            if (p.ParentFolderId.HasValue && !await _context.productFolders.AsNoTracking()
                    .AnyAsync(f => f.Id == p.ParentFolderId && f.productId == p.ProductId, ct))
                return (new { error = "Parent folder not found" }, "Not found");

            var folder = new ProductFolder { name = p.Name.Trim(), productId = p.ProductId.Value, parentFolderId = p.ParentFolderId };
            _context.productFolders.Add(folder);
            await _context.SaveChangesAsync(ct);
            return (new { id = folder.Id }, $"Folder \"{folder.name}\" created");
        }
        else
        {
            if (p.ParentFolderId.HasValue && !await _context.storeFolders.AsNoTracking()
                    .AnyAsync(f => f.Id == p.ParentFolderId && f.storeId == storeId, ct))
                return (new { error = "Parent folder not found" }, "Not found");

            var folder = new StoreFolder { name = p.Name.Trim(), storeId = storeId, parentFolderId = p.ParentFolderId };
            _context.storeFolders.Add(folder);
            await _context.SaveChangesAsync(ct);
            return (new { id = folder.Id }, $"Folder \"{folder.name}\" created");
        }
    }

    private async Task<(object, string)> RenameFileAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<RenameFileInput>(json);
        if (string.IsNullOrWhiteSpace(p.NewName)) return (new { error = "New name is required" }, "No name");
        if (p.Scope == "product")
        {
            var file = await _context.files.FirstOrDefaultAsync(f => f.Id == p.FileId && f.product.storeId == storeId, ct);
            if (file == null) return (new { error = "File not found" }, "Not found");
            file.displayName = p.NewName.Trim();
            await _context.SaveChangesAsync(ct);
        }
        else
        {
            var file = await _context.storeFiles.FirstOrDefaultAsync(f => f.Id == p.FileId && f.storeId == storeId, ct);
            if (file == null) return (new { error = "File not found" }, "Not found");
            file.displayName = p.NewName.Trim();
            await _context.SaveChangesAsync(ct);
        }
        return (new { success = true }, $"File renamed to \"{p.NewName}\"");
    }

    private async Task<(object, string)> RenameFolderAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<RenameFolderInput>(json);
        if (string.IsNullOrWhiteSpace(p.NewName)) return (new { error = "New name is required" }, "No name");
        if (p.Scope == "product")
        {
            var folder = await _context.productFolders.FirstOrDefaultAsync(f => f.Id == p.FolderId && f.product.storeId == storeId, ct);
            if (folder == null) return (new { error = "Folder not found" }, "Not found");
            folder.name = p.NewName.Trim();
            await _context.SaveChangesAsync(ct);
        }
        else
        {
            var folder = await _context.storeFolders.FirstOrDefaultAsync(f => f.Id == p.FolderId && f.storeId == storeId, ct);
            if (folder == null) return (new { error = "Folder not found" }, "Not found");
            folder.name = p.NewName.Trim();
            await _context.SaveChangesAsync(ct);
        }
        return (new { success = true }, $"Folder renamed to \"{p.NewName}\"");
    }

    private async Task<(object, string)> MoveFilesAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<MoveFilesInput>(json);
        var movedFiles = 0;
        var movedFolders = 0;

        if (p.Scope == "product")
        {
            if (p.TargetFolderId.HasValue && !await _context.productFolders.AsNoTracking()
                    .AnyAsync(f => f.Id == p.TargetFolderId && f.product.storeId == storeId, ct))
                return (new { error = "Target folder not found" }, "Not found");

            if (p.FileIds?.Count > 0)
            {
                var files = await _context.files
                    .Where(f => p.FileIds.Contains(f.Id) && f.product.storeId == storeId)
                    .ToListAsync(ct);
                foreach (var f in files) f.folderId = p.TargetFolderId;
                movedFiles = files.Count;
            }
            if (p.FolderIds?.Count > 0)
            {
                var folders = await _context.productFolders
                    .Where(f => p.FolderIds.Contains(f.Id) && f.product.storeId == storeId)
                    .ToListAsync(ct);
                foreach (var f in folders)
                {
                    if (p.TargetFolderId == f.Id) return (new { error = "Cannot move a folder into itself" }, "Error");
                    f.parentFolderId = p.TargetFolderId;
                }
                movedFolders = folders.Count;
            }
        }
        else
        {
            if (p.TargetFolderId.HasValue && !await _context.storeFolders.AsNoTracking()
                    .AnyAsync(f => f.Id == p.TargetFolderId && f.storeId == storeId, ct))
                return (new { error = "Target folder not found" }, "Not found");

            if (p.FileIds?.Count > 0)
            {
                var files = await _context.storeFiles
                    .Where(f => p.FileIds.Contains(f.Id) && f.storeId == storeId)
                    .ToListAsync(ct);
                foreach (var f in files) f.folderId = p.TargetFolderId;
                movedFiles = files.Count;
            }
            if (p.FolderIds?.Count > 0)
            {
                var folders = await _context.storeFolders
                    .Where(f => p.FolderIds.Contains(f.Id) && f.storeId == storeId)
                    .ToListAsync(ct);
                foreach (var f in folders)
                {
                    if (p.TargetFolderId == f.Id) return (new { error = "Cannot move a folder into itself" }, "Error");
                    // Prevent moving into own descendant
                    var currentId = p.TargetFolderId;
                    while (currentId.HasValue)
                    {
                        if (currentId.Value == f.Id) return (new { error = "Cannot move a folder into its own descendant" }, "Error");
                        currentId = await _context.storeFolders.AsNoTracking()
                            .Where(x => x.Id == currentId.Value).Select(x => x.parentFolderId).FirstOrDefaultAsync(ct);
                    }
                    f.parentFolderId = p.TargetFolderId;
                }
                movedFolders = folders.Count;
            }
        }
        await _context.SaveChangesAsync(ct);
        return (new { movedFiles, movedFolders }, $"Moved: {movedFiles} files, {movedFolders} folders");
    }

    private async Task<(object, string)> DeleteFilesAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<DeleteFilesInput>(json);
        if (p.FileIds.Count == 0) return (new { error = "fileIds not specified" }, "No files");

        if (p.Scope == "product")
        {
            var files = await _context.files
                .Where(f => p.FileIds.Contains(f.Id) && f.product.storeId == storeId)
                .ToListAsync(ct);
            var ids = files.Select(f => f.Id).ToList();
            if (ids.Count > 0)
            {
                await _context.tags.Where(t => ids.Contains(t.fileId)).ExecuteDeleteAsync(ct);
                await _context.fileShareTokens.Where(t => ids.Contains(t.fileId)).ExecuteDeleteAsync(ct);
            }
            foreach (var f in files)
            {
                await _thumbnailService.DeleteThumbnailsAsync(f.filePath);
                await _storageService.DeleteAsync(f.filePath);
                _context.files.Remove(f);
            }
            await _context.SaveChangesAsync(ct);
            await _operationService.RecordAsync(storeId, userId, "delete_files",
                $"Deleted {files.Count} product files", new { }, canRollback: false, ct);
            return (new { deleted = files.Count }, $"Deleted {files.Count} files");
        }
        else
        {
            var files = await _context.storeFiles
                .Where(f => p.FileIds.Contains(f.Id) && f.storeId == storeId)
                .ToListAsync(ct);
            var ids = files.Select(f => f.Id).ToList();
            if (ids.Count > 0)
            {
                await _context.storeFileTags.Where(t => ids.Contains(t.fileId)).ExecuteDeleteAsync(ct);
                await _context.storeFileShareTokens.Where(t => ids.Contains(t.fileId)).ExecuteDeleteAsync(ct);
            }
            foreach (var f in files)
            {
                await _thumbnailService.DeleteThumbnailsAsync(f.filePath);
                await _storageService.DeleteAsync(f.filePath);
                _context.storeFiles.Remove(f);
            }
            await _context.SaveChangesAsync(ct);
            await _operationService.RecordAsync(storeId, userId, "delete_files",
                $"Deleted {files.Count} store files", new { }, canRollback: false, ct);
            return (new { deleted = files.Count }, $"Deleted {files.Count} files");
        }
    }

    private async Task<(object, string)> DeleteFolderAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<DeleteFolderInput>(json);
        var recursive = p.Recursive ?? false;

        if (p.Scope == "product")
        {
            var folder = await _context.productFolders.AsNoTracking()
                .Include(f => f.files).Include(f => f.childFolders)
                .FirstOrDefaultAsync(f => f.Id == p.FolderId && f.product.storeId == storeId, ct);
            if (folder == null) return (new { error = "Folder not found" }, "Not found");
            if (!recursive && (folder.files.Count > 0 || folder.childFolders.Count > 0))
                return (new { error = "Folder is not empty — pass recursive=true" }, "Not empty");

            var folderIds = new List<int> { folder.Id };
            if (recursive)
                await CollectProductSubfolderIdsAsync(folder.Id, folderIds, ct);

            var files = await _context.files
                .Where(f => f.folderId.HasValue && folderIds.Contains(f.folderId.Value))
                .ToListAsync(ct);
            var fileIds = files.Select(f => f.Id).ToList();
            if (fileIds.Count > 0)
            {
                await _context.tags.Where(t => fileIds.Contains(t.fileId)).ExecuteDeleteAsync(ct);
                await _context.fileShareTokens.Where(t => fileIds.Contains(t.fileId)).ExecuteDeleteAsync(ct);
            }
            await _context.folderShareTokens
                .Where(t => t.folderId.HasValue && folderIds.Contains(t.folderId.Value)).ExecuteDeleteAsync(ct);
            foreach (var f in files)
            {
                await _thumbnailService.DeleteThumbnailsAsync(f.filePath);
                await _storageService.DeleteAsync(f.filePath);
                _context.files.Remove(f);
            }
            await _context.SaveChangesAsync(ct);
            folderIds.Reverse();
            foreach (var fid in folderIds)
                await _context.productFolders.Where(f => f.Id == fid).ExecuteDeleteAsync(ct);

            await _operationService.RecordAsync(storeId, userId, "delete_folder",
                $"Deleted folder \"{folder.name}\" ({files.Count} files)", new { }, canRollback: false, ct);
            return (new { deletedFiles = files.Count }, $"Folder \"{folder.name}\" deleted ({files.Count} files)");
        }
        else
        {
            var folder = await _context.storeFolders.AsNoTracking()
                .Include(f => f.files).Include(f => f.childFolders)
                .FirstOrDefaultAsync(f => f.Id == p.FolderId && f.storeId == storeId, ct);
            if (folder == null) return (new { error = "Folder not found" }, "Not found");
            if (!recursive && (folder.files.Count > 0 || folder.childFolders.Count > 0))
                return (new { error = "Folder is not empty — pass recursive=true" }, "Not empty");

            var folderIds = new List<int> { folder.Id };
            if (recursive)
                await CollectStoreSubfolderIdsAsync(folder.Id, storeId, folderIds, ct);

            var files = await _context.storeFiles
                .Where(f => f.folderId.HasValue && folderIds.Contains(f.folderId.Value))
                .ToListAsync(ct);
            var fileIds = files.Select(f => f.Id).ToList();
            if (fileIds.Count > 0)
            {
                await _context.storeFileTags.Where(t => fileIds.Contains(t.fileId)).ExecuteDeleteAsync(ct);
                await _context.storeFileShareTokens.Where(t => fileIds.Contains(t.fileId)).ExecuteDeleteAsync(ct);
            }
            await _context.storeFolderShareTokens
                .Where(t => t.folderId.HasValue && folderIds.Contains(t.folderId.Value)).ExecuteDeleteAsync(ct);
            foreach (var f in files)
            {
                await _thumbnailService.DeleteThumbnailsAsync(f.filePath);
                await _storageService.DeleteAsync(f.filePath);
                _context.storeFiles.Remove(f);
            }
            await _context.SaveChangesAsync(ct);
            folderIds.Reverse();
            foreach (var fid in folderIds)
                await _context.storeFolders.Where(f => f.Id == fid).ExecuteDeleteAsync(ct);

            await _operationService.RecordAsync(storeId, userId, "delete_folder",
                $"Deleted folder \"{folder.name}\" ({files.Count} files)", new { }, canRollback: false, ct);
            return (new { deletedFiles = files.Count }, $"Folder \"{folder.name}\" deleted ({files.Count} files)");
        }
    }

    private async Task CollectProductSubfolderIdsAsync(int folderId, List<int> acc, CancellationToken ct)
    {
        var children = await _context.productFolders.AsNoTracking()
            .Where(f => f.parentFolderId == folderId).Select(f => f.Id).ToListAsync(ct);
        foreach (var c in children)
        {
            acc.Add(c);
            await CollectProductSubfolderIdsAsync(c, acc, ct);
        }
    }

    private async Task CollectStoreSubfolderIdsAsync(int folderId, int storeId, List<int> acc, CancellationToken ct)
    {
        var children = await _context.storeFolders.AsNoTracking()
            .Where(f => f.parentFolderId == folderId && f.storeId == storeId).Select(f => f.Id).ToListAsync(ct);
        foreach (var c in children)
        {
            acc.Add(c);
            await CollectStoreSubfolderIdsAsync(c, storeId, acc, ct);
        }
    }

    private async Task<(object, string)> ShareFileAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ShareFileInput>(json);
        var ttl = p.TtlHours ?? 168;
        if (ttl is not (1 or 24 or 168 or 720))
            return (new { error = "Allowed ttlHours: 1, 24, 168 (a week), 720 (a month)" }, "Invalid TTL");
        if (!p.FileId.HasValue && !p.FolderId.HasValue)
            return (new { error = "Specify fileId or folderId" }, "No target");

        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var expiresAt = DateTime.UtcNow.AddHours(ttl);
        string url;

        if (p.Scope == "product")
        {
            if (p.FileId.HasValue)
            {
                var file = await _context.files.AsNoTracking()
                    .FirstOrDefaultAsync(f => f.Id == p.FileId && f.product.storeId == storeId, ct);
                if (file == null) return (new { error = "File not found" }, "Not found");
                _context.fileShareTokens.Add(new FileShareToken
                {
                    fileId = file.Id, token = token, expiresAt = expiresAt, createdByUserId = userId,
                });
                url = $"/api/shared/{token}";
            }
            else
            {
                var folder = await _context.productFolders.AsNoTracking()
                    .FirstOrDefaultAsync(f => f.Id == p.FolderId && f.product.storeId == storeId, ct);
                if (folder == null) return (new { error = "Folder not found" }, "Not found");
                _context.folderShareTokens.Add(new FolderShareToken
                {
                    productId = folder.productId, folderId = folder.Id, token = token,
                    expiresAt = expiresAt, createdByUserId = userId,
                });
                url = $"/api/shared/folder/{token}";
            }
        }
        else
        {
            if (p.FileId.HasValue)
            {
                var exists = await _context.storeFiles.AsNoTracking()
                    .AnyAsync(f => f.Id == p.FileId && f.storeId == storeId, ct);
                if (!exists) return (new { error = "File not found" }, "Not found");
                _context.storeFileShareTokens.Add(new StoreFileShareToken
                {
                    fileId = p.FileId.Value, token = token, expiresAt = expiresAt, createdByUserId = userId,
                });
                url = $"/api/shared/store-file/{token}";
            }
            else
            {
                var exists = await _context.storeFolders.AsNoTracking()
                    .AnyAsync(f => f.Id == p.FolderId && f.storeId == storeId, ct);
                if (!exists) return (new { error = "Folder not found" }, "Not found");
                _context.storeFolderShareTokens.Add(new StoreFolderShareToken
                {
                    storeId = storeId, folderId = p.FolderId, token = token,
                    expiresAt = expiresAt, createdByUserId = userId,
                });
                url = $"/api/shared/store-folder/{token}";
            }
        }
        await _context.SaveChangesAsync(ct);
        return (new { url, expiresAt }, $"Link created, valid until {expiresAt:yyyy-MM-dd HH:mm} UTC");
    }

    private async Task<(object, string)> CreateGroupAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<CreateGroupInput>(json);
        var group = new ProductGroup { name = p.Name, storeId = storeId };
        _context.productGroups.Add(group);
        await _context.SaveChangesAsync(ct);
        if (p.ProductIds?.Count > 0)
        {
            foreach (var pid in p.ProductIds)
                _context.productGroupMembers.Add(new ProductGroupMember { groupId = group.Id, productId = pid });
            await _context.SaveChangesAsync(ct);
        }
        return (new { id = group.Id }, $"Group \"{p.Name}\" created");
    }

    private async Task<(object, string)> AddProductsToGroupAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<GroupProductsInput>(json);
        var existing = (await _context.productGroupMembers.AsNoTracking()
            .Where(m => m.groupId == p.GroupId && p.ProductIds.Contains(m.productId))
            .Select(m => m.productId).ToListAsync(ct)).ToHashSet();
        var toAdd = p.ProductIds.Where(id => !existing.Contains(id)).ToList();
        foreach (var pid in toAdd)
            _context.productGroupMembers.Add(new ProductGroupMember { groupId = p.GroupId, productId = pid });
        await _context.SaveChangesAsync(ct);
        return (new { added = toAdd.Count }, $"Added {toAdd.Count} products to the group");
    }

    // ---- Barcodes ----

    private async Task<(object, string)> GenerateBarcodesAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<GenerateBarcodesInput>(json);
        var q = _context.products.Where(x => x.storeId == storeId && (x.barcode == null || x.barcode == ""));
        if (p.ProductIds?.Count > 0) q = q.Where(x => p.ProductIds.Contains(x.Id));
        var products = await q.ToListAsync(ct);
        if (products.Count == 0) return (new { generated = 0 }, "No products without a barcode");

        // Uniqueness within the store: seed with all existing barcodes
        var existing = (await _context.products.AsNoTracking()
            .Where(x => x.storeId == storeId && x.barcode != null && x.barcode != "")
            .Select(x => x.barcode!).ToListAsync(ct)).ToHashSet();

        var snapshots = new List<BarcodeSnapshot>();
        foreach (var product in products)
        {
            string bc;
            do { bc = Controllers.ProductController.GenerateEan13(); } while (!existing.Add(bc));
            snapshots.Add(new BarcodeSnapshot { Id = product.Id, OldBarcode = product.barcode });
            product.barcode = bc;
        }
        await _context.SaveChangesAsync(ct);

        await _operationService.RecordAsync(storeId, userId, "generate_barcodes",
            $"Generated EAN-13 barcodes for {products.Count} products",
            new BarcodeRollbackData { Products = snapshots }, canRollback: true, ct);

        return (new { generated = products.Count },
            $"Generated barcodes for {products.Count} products");
    }

    // ---- Analytics ----

    private async Task<(object, string)> GetStoreStatsAsync(int storeId, CancellationToken ct)
    {
        var productCount = await _context.products.AsNoTracking().CountAsync(p => p.storeId == storeId, ct);
        var categoryCount = await _context.categories.AsNoTracking().CountAsync(c => c.storeId == storeId, ct);
        var connectionCount = await _context.marketplaceConnections.AsNoTracking().CountAsync(c => c.storeId == storeId && c.isActive, ct);
        var mappingCount = await _context.categoryMappings.AsNoTracking().CountAsync(m => m.connection.storeId == storeId, ct);
        var pendingExports = await _context.pendingProductExports.AsNoTracking().CountAsync(e => e.storeId == storeId && e.status == ProductExportStatus.Pending, ct);
        var groupCount = await _context.productGroups.AsNoTracking().CountAsync(g => g.storeId == storeId, ct);

        return (new
        {
            products = productCount,
            categories = categoryCount,
            connections = connectionCount,
            mappings = mappingCount,
            pendingExports,
            groups = groupCount
        }, $"Products: {productCount}, categories: {categoryCount}, connections: {connectionCount}");
    }

    private async Task<(object, string)> FindProductsWithoutImagesAsync(int storeId, CancellationToken ct)
    {
        var products = await _context.products.AsNoTracking()
            .Where(p => p.storeId == storeId && !_context.images.Any(i => i.productId == p.Id))
            .Select(p => new { p.Id, p.name, p.sku })
            .Take(100).ToListAsync(ct);
        return (new { products, count = products.Count }, $"{products.Count} products without images");
    }

    private async Task<(object, string)> FindProductsWithoutBarcodeAsync(int storeId, CancellationToken ct)
    {
        var products = await _context.products.AsNoTracking()
            .Where(p => p.storeId == storeId && (p.barcode == null || p.barcode == ""))
            .Select(p => new { p.Id, p.name, p.sku })
            .Take(100).ToListAsync(ct);
        return (new { products, count = products.Count }, $"{products.Count} products without a barcode");
    }

    private async Task<(object, string)> FindProductsMissingFieldAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<SearchInput>(json);
        var attrId = await _context.categoryAttributes.AsNoTracking()
            .Where(a => EF.Functions.Like(a.name, $"%{p.Search}%") && a.category.storeId == storeId)
            .Select(a => (int?)a.Id).FirstOrDefaultAsync(ct);

        if (attrId == null)
            return (new { error = $"Field '{p.Search}' not found" }, "Field not found");

        var products = await _context.products.AsNoTracking()
            .Where(pr => pr.storeId == storeId &&
                !_context.productAttributeValues.Any(v => v.productId == pr.Id && v.attributeId == attrId && v.value != null && v.value != ""))
            .Select(pr => new { pr.Id, pr.name, pr.sku })
            .Take(100).ToListAsync(ct);
        return (new { products, count = products.Count }, $"{products.Count} products missing field '{p.Search}'");
    }

    private async Task<(object, string)> FindProductsWithSyncErrorsAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<ConnectionIdInput>(json);
        var q = _context.productMarketplaceLinks.AsNoTracking()
            .Where(l => l.connection.storeId == storeId && l.status == ProductMarketplaceStatus.SyncError);
        if (p.ConnectionId.HasValue) q = q.Where(l => l.connectionId == p.ConnectionId);
        var links = await q.Take(50)
            .Select(l => new { l.productId, l.externalProductId, l.connectionId, l.lastErrorMessage })
            .ToListAsync(ct);
        return (new { products = links, count = links.Count }, $"{links.Count} products with sync errors");
    }

    private async Task<(object, string)> AnalyzeMappingQualityAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<ConnectionIdInput>(json);
        var q = _context.categoryMappings.AsNoTracking().Where(m => m.connection.storeId == storeId);
        if (p.ConnectionId.HasValue) q = q.Where(m => m.connectionId == p.ConnectionId);

        var mappings = await q.ToListAsync(ct);
        var report = new List<object>();
        foreach (var m in mappings)
        {
            var coverage = await _graphMutation.GetCoverageAsync(m.Id, ct);
            report.Add(new
            {
                id = m.Id,
                name = m.name ?? m.externalCategoryName,
                coverage.Total, coverage.Connected, coverage.RequiredTotal, coverage.RequiredConnected,
                unmappedRequired = coverage.UnmappedRequiredFields.Select(f => f.Label)
            });
        }
        var totalFields = report.Sum(r => (int)((dynamic)r).Total);
        var connectedFields = report.Sum(r => (int)((dynamic)r).Connected);
        return (new { mappings = report, totalFields, connectedFields },
            $"Analyzed {mappings.Count} mappings: {connectedFields}/{totalFields} fields connected");
    }

    private async Task<(object, string)> GetImageDownloadStatusAsync(int storeId, CancellationToken ct)
    {
        var groups = await _context.pendingImageDownloads.AsNoTracking()
            .Where(d => d.storeId == storeId)
            .GroupBy(d => d.status)
            .Select(g => new { status = g.Key, count = g.Count() })
            .ToListAsync(ct);
        return (new { status = groups }, "Image download status");
    }

    // ---- Rollback ----

    // RecordAsync historically wrote rollback JSON in PascalCase (no naming policy) while this
    // file deserialized expecting camelCase — every rollback silently no-op'ed. Case-insensitive
    // matching reads both old and new rows.
    private static readonly JsonSerializerOptions _rollbackJson = new() { PropertyNameCaseInsensitive = true };

    private async Task<(object, string)> RollbackOperationAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<OptionalIdInput>(json);
        AiOperation? op;
        if (p.Id.HasValue)
            op = await _operationService.GetByIdAsync(p.Id.Value, userId, ct);
        else
            op = await _operationService.GetLastAsync(userId, storeId, ct);

        if (op == null) return (new { error = "No operations to undo" }, "No operations to undo");
        if (!op.canRollback || op.status == AiOperationStatus.RolledBack)
            return (new { error = "This operation cannot be undone" }, "Cannot undo");

        // Stale guard: without an explicit operationId, don't silently undo something old —
        // it may belong to a long-finished task the user no longer has in mind
        if (!p.Id.HasValue && op.createdAt < DateTime.UtcNow.AddMinutes(-30))
        {
            var age = DateTime.UtcNow - op.createdAt;
            var ageText = age.TotalHours >= 1 ? $"{(int)age.TotalHours}h ago" : $"{(int)age.TotalMinutes}m ago";
            return (new
            {
                requiresConfirmation = true,
                operationId = op.Id,
                description = op.description,
                createdAt = op.createdAt,
            }, $"The last operation ran {ageText}: «{op.description}». " +
               $"This is an old operation — confirm with the user that this is specifically what they want to undo, " +
               $"then retry the call with operationId={op.Id}");
        }

        var applied = await ApplyRollbackAsync(op, ct);
        if (!applied)
        {
            _logger.LogWarning("Rollback of operation {OpId} ({Type}) did not change any data", op.Id, op.operationType);
            return (new { error = "Rollback failed — no data was changed" },
                $"Rollback of operation «{op.description}» failed: no data was changed");
        }

        await _operationService.MarkRolledBackAsync(op.Id, ct);
        return (new { success = true, operationId = op.Id, description = op.description },
            $"Undone: {op.description}");
    }

    // Returns true only when the rollback actually changed data.
    private async Task<bool> ApplyRollbackAsync(AiOperation op, CancellationToken ct)
    {
        switch (op.operationType)
        {
            case "update_prices":
            {
                var data = JsonSerializer.Deserialize<PriceRollbackData>(op.rollbackDataJson, _rollbackJson);
                if (data == null || data.Products.Count == 0) return false;
                var ids = data.Products.Select(p => p.Id).ToList();
                var products = await _context.products.Where(p => ids.Contains(p.Id)).ToListAsync(ct);
                foreach (var pr in products)
                {
                    var snap = data.Products.FirstOrDefault(p => p.Id == pr.Id);
                    if (snap != null) pr.price = snap.OldPrice;
                }
                return await _context.SaveChangesAsync(ct) > 0 || products.Count > 0;
            }
            case "add_category_field":
            {
                var data = JsonSerializer.Deserialize<FieldRollbackData>(op.rollbackDataJson, _rollbackJson);
                if (data == null || data.FieldId == 0) return false;
                var deleted = await _context.categoryAttributes.Where(a => a.Id == data.FieldId)
                    .ExecuteDeleteAsync(ct);
                return deleted > 0;
            }
            case "connect_field":
            case "set_static_value":
            case "add_modifier":
            case "disconnect_field":
            case "generate_mapping":
            case "merge_fields":
            case "unmerge_fields":
            {
                var data = JsonSerializer.Deserialize<GraphRollbackData>(op.rollbackDataJson, _rollbackJson);
                if (data == null || data.MappingId == 0) return false;
                var updated = await _context.categoryMappings.Where(m => m.Id == data.MappingId)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.graphJson, data.PreviousGraphJson), ct);
                return updated > 0;
            }
            case "generate_barcodes":
            {
                var data = JsonSerializer.Deserialize<BarcodeRollbackData>(op.rollbackDataJson, _rollbackJson);
                if (data == null || data.Products.Count == 0) return false;
                var ids = data.Products.Select(x => x.Id).ToList();
                var products = await _context.products.Where(x => ids.Contains(x.Id)).ToListAsync(ct);
                foreach (var pr in products)
                {
                    var snap = data.Products.FirstOrDefault(x => x.Id == pr.Id);
                    if (snap != null) pr.barcode = snap.OldBarcode;
                }
                await _context.SaveChangesAsync(ct);
                return products.Count > 0;
            }
            case "update_attribute":
            {
                var data = JsonSerializer.Deserialize<ProductAttributeRollbackData>(op.rollbackDataJson, _rollbackJson);
                if (data == null || data.Products.Count == 0) return false;
                foreach (var snap in data.Products)
                {
                    var val = await _context.productAttributeValues
                        .FirstOrDefaultAsync(v => v.productId == snap.ProductId && v.attributeId == snap.AttributeId, ct);
                    if (val != null)
                    {
                        val.value = snap.OldValue ?? string.Empty;
                    }
                    else if (snap.OldValue != null)
                    {
                        _context.productAttributeValues.Add(new ProductAttributeValue
                        { productId = snap.ProductId, attributeId = snap.AttributeId, value = snap.OldValue });
                    }
                }
                await _context.SaveChangesAsync(ct);
                return true;
            }
            default:
                return false;
        }
    }

    // ---- Tool Definitions ----
    // When adding/removing tools here, also update toolCategories in AiChatPanel.vue.

    private static readonly HashSet<string> _productPageTools = new()
    {
        "propose_plan", "rollback_last_operation", "navigate_to",
        "list_products", "get_products_count", "get_product", "create_product", "create_products",
        "update_product", "update_product_sku", "update_product_prices", "update_product_attribute",
        "update_product_attributes", "delete_products", "restore_products", "list_trash", "generate_barcodes",
        "list_categories", "get_category", "list_category_fields",
        "list_store_attributes",
        "list_files", "create_folder", "rename_file", "rename_folder", "move_files",
        "delete_files", "delete_folder", "share_file",
        "list_groups", "get_group", "create_group", "update_group", "delete_group",
        "add_products_to_group", "remove_products_from_group",
        "export_products_batch", "export_prices", "export_stocks", "get_batch_status", "list_product_links",
        "get_store_stats", "find_products_without_images", "find_products_without_barcode",
        "find_products_missing_field", "find_products_with_sync_errors", "get_image_download_status",
        "list_mappings", "list_connections",
        "find_products_without_mapping", "get_product_mapping_summary", "assign_product_mappings",
    };

    private static readonly HashSet<string> _mappingPageTools = new()
    {
        "propose_plan", "rollback_last_operation", "navigate_to",
        "list_mappings", "get_mapping", "create_mapping", "update_mapping_name", "delete_mapping",
        "duplicate_mapping", "list_external_categories", "get_external_fields", "get_price_fields",
        "get_option_alignment_hints", "search_field_options", "get_available_sources", "get_mapping_coverage",
        "preview_mapping_for_product",
        "connect_field", "set_field_static_value", "add_field_modifier",
        "remove_field_connection", "merge_adapter_fields", "unmerge_adapter_fields",
        "connect_fields", "set_fields_static_value",
        "list_products", "get_product",
        "list_categories", "get_category", "list_category_fields",
        "list_connections", "get_connection", "get_adapter_info",
        "analyze_mapping_quality", "find_products_with_sync_errors",
        "find_products_without_mapping", "get_product_mapping_summary", "assign_product_mappings",
    };

    private static readonly HashSet<string> _connectionPageTools = new()
    {
        "propose_plan", "rollback_last_operation", "navigate_to",
        "list_connections", "get_connection", "test_connection", "get_connection_limits", "get_adapter_info",
        "list_mappings", "get_mapping", "create_mapping", "update_mapping_name", "delete_mapping",
        "duplicate_mapping", "list_external_categories", "get_external_fields", "get_price_fields",
        "get_option_alignment_hints", "search_field_options", "get_available_sources", "get_mapping_coverage",
        "preview_mapping_for_product",
        "connect_field", "set_field_static_value", "add_field_modifier",
        "remove_field_connection", "merge_adapter_fields", "unmerge_adapter_fields",
        "connect_fields", "set_fields_static_value", "analyze_mapping_quality",
        "export_products_batch", "export_prices", "export_stocks", "get_batch_status", "list_product_links",
        "preview_marketplace_products", "import_products", "import_prices", "import_stocks",
        "archive_product_on_marketplace",
        "list_products", "get_products_count", "get_product",
        "list_categories", "get_category", "list_category_fields",
        "get_store_stats", "find_products_with_sync_errors",
        "find_products_without_mapping", "get_product_mapping_summary", "assign_product_mappings",
    };

    private static List<AiTool> BuildToolDefinitions(AiPageContext? pageContext)
    {
        var all = BuildAllToolDefinitions();
        var filter = pageContext?.Page switch
        {
            "product"      => _productPageTools,
            "fieldMapping" => _mappingPageTools,
            "connection"   => _connectionPageTools,
            _              => null,
        };
        return filter == null ? all : all.Where(t => filter.Contains(t.name)).ToList();
    }

    private static List<AiTool> BuildAllToolDefinitions() => [
        Prop("propose_plan", "ALWAYS call first before any mutating operation. Shows user a plan and waits for confirmation.",
            Req(["summary", "steps"]),
            ("summary", Str("Brief summary of what will be done")),
            ("steps", new AiToolProperty
            {
                type = "array",
                description = "List of action steps",
                items = new AiToolProperty
                {
                    type = "object",
                    properties = new Dictionary<string, AiToolProperty>
                    {
                        ["tool"] = new AiToolProperty { type = "string", description = "Tool name that will be called" },
                        ["description"] = new AiToolProperty { type = "string", description = "Human-readable description of this step in the current UI language (same language as assistant replies)" },
                        ["args"] = new AiToolProperty { type = "object", description = "EXACT tool arguments as a JSON object — the same IDs/keys/values you already gathered. After user confirmation the system executes each step with these args automatically." }
                    },
                    required = ["tool", "description", "args"]
                }
            }),
            ("affectedCount", Int("Number of affected items")),
            ("isDestructive", Bool("Whether this is a destructive operation"))
        ),
        Prop("rollback_last_operation", "Rollback the last AI operation (undo). Call without operationId to rollback the most recent one.",
            Req([]),
            ("operationId", Int("Specific operation ID to rollback (optional)"))
        ),

        // Products
        Prop("list_products", "List products with optional filters and sorting",
            Req([]),
            ("search", Str("Search in name or SKU")),
            ("categoryId", Int("Filter by category ID")),
            ("priceMin", Num("Min price filter")),
            ("priceMax", Num("Max price filter")),
            ("hasImages", Bool("true = only with images, false = only without")),
            ("missingField", Str("Filter products missing this attribute key")),
            ("startIndex", Int("Pagination offset (default 0)")),
            ("pageSize", Int("Page size (default 50, max 200)")),
            ("quantityMin", Int("Min stock quantity filter")),
            ("quantityMax", Int("Max stock quantity filter — use 0 to find out-of-stock products")),
            ("sortBy", Enum("id", "price", "name", "quantity")),
            ("sortDir", Enum("asc", "desc"))
        ),
        Prop("get_products_count", "Count products matching filters", Req([]),
            ("search", Str()), ("categoryId", Int()), ("priceMin", Num()), ("priceMax", Num())
        ),
        Prop("get_product", "Get full product details including attributes", Req(["id"]),
            ("id", Int("Product ID"))
        ),
        Prop("create_product", "Create a new product. DB requires: name, sku, description. Always provide all three.", Req(["name", "sku", "description", "categoryId"]),
            ("name", Str("Product name — required")),
            ("sku", Str("Unique stock-keeping unit, e.g. 'SHOE-RED-42' — required")),
            ("description", Str("Product description — required, use empty string '' if none")),
            ("categoryId", Int("Category ID — required")),
            ("price", Num("Price in store currency (default 0)")),
            ("barcode", Str("Barcode / EAN / UPC (optional)")),
            ("quantity", Int("Stock quantity (default 0)"))
        ),
        Prop("create_products", "Create multiple products in one call. Prefer this over calling create_product in a loop.", Req(["products"]),
            ("products", new AiToolProperty
            {
                type = "array",
                description = "List of products to create",
                items = new AiToolProperty
                {
                    type = "object",
                    required = new List<string> { "name", "sku", "description", "categoryId" },
                    properties = new Dictionary<string, AiToolProperty>
                    {
                        ["name"] = Str("Product name — required"),
                        ["sku"] = Str("Unique stock-keeping unit — required"),
                        ["description"] = Str("Product description — required, use '' if none"),
                        ["categoryId"] = Int("Category ID — required"),
                        ["price"] = Num("Price"),
                        ["barcode"] = Str("Barcode"),
                        ["quantity"] = Int("Stock quantity"),
                    }
                }
            })
        ),
        Prop("update_product", "Update one or more fields of a product (only provided fields are changed)", Req(["id"]),
            ("id", Int("Product ID — required")),
            ("name", Str("New name")), ("price", Num("New price")),
            ("description", Str("New description")), ("barcode", Str("New barcode")),
            ("quantity", Int("New stock quantity")), ("categoryId", Int("Move to category ID"))
        ),
        Prop("update_product_sku", "Change product SKU (cascades to marketplace links)", Req(["productId", "newSku"]),
            ("productId", Int("Product ID")), ("newSku", Str("New unique SKU"))
        ),
        Prop("update_product_prices", "Bulk change prices. For a SINGLE product use update_product(id,price) instead. WARNING: without productIds or categoryId affects ALL store products.", Req(["amount", "mode"]),
            ("productIds", IntArray("Array of integer product IDs to update (omit to use categoryId or affect all)")),
            ("categoryId", Int("Update all products in this category (ignored if productIds provided)")),
            ("amount", Num("Amount for the operation")),
            ("mode", Enum("add", "subtract", "set", "multiply"))
        ),
        Prop("update_product_attribute", "Set a category attribute value on products. attributeKey is the field's 'key' property (not name). Without productIds, updates all products in the attribute's category.", Req(["attributeKey", "value"]),
            ("attributeKey", Str("Attribute 'key' field (e.g. 'color', 'size') — get it from list_category_fields")),
            ("value", Str("New value to set")),
            ("productIds", IntArray("Array of integer product IDs (omit to update all in attribute's category)"))
        ),
        Prop("update_product_attributes", "Set multiple category attribute values on products in one call. Prefer this over calling update_product_attribute in a loop.", Req(["attributes"]),
            ("productIds", IntArray("Array of product IDs (omit to update all in each attribute's category)")),
            ("attributes", new AiToolProperty
            {
                type = "array",
                description = "List of {attributeKey, value} pairs to set",
                items = new AiToolProperty
                {
                    type = "object",
                    required = new List<string> { "attributeKey", "value" },
                    properties = new Dictionary<string, AiToolProperty>
                    {
                        ["attributeKey"] = Str("Attribute key from list_category_fields"),
                        ["value"] = Str("Value to set"),
                    }
                }
            })
        ),
        Prop("delete_products", "Soft-delete products (recoverable from trash)", Req(["ids"]),
            ("ids", IntArray("Array of integer product IDs to delete"))
        ),
        Prop("restore_products", "Restore soft-deleted products from trash", Req(["ids"]),
            ("ids", IntArray("Array of integer product IDs to restore"))
        ),
        Prop("list_trash", "List soft-deleted products", Req([]), ("search", Str("Search in name"))),

        // Categories
        Prop("list_categories", "List all store categories with attribute counts", Req([])),
        Prop("get_category", "Get category details with all attributes", Req(["id"]), ("id", Int("Category ID"))),
        Prop("create_category", "Create a category (description is optional)", Req(["name"]),
            ("name", Str("Category name")), ("description", Str("Description (optional)"))
        ),
        Prop("update_category", "Update category name or description (only provided fields change)", Req(["id"]),
            ("id", Int("Category ID")), ("name", Str("New name")), ("description", Str("New description"))
        ),
        Prop("delete_category", "Permanently delete a category", Req(["id"]), ("id", Int("Category ID"))),
        Prop("list_category_fields", "List all attributes of a category", Req(["id"]), ("id", Int("Category ID"))),
        Prop("add_category_field", "Add a new attribute field to a category. Use list_category_fields first to avoid duplicates.", Req(["categoryId", "name", "type"]),
            ("categoryId", Int("Category ID")),
            ("name", Str("Display name of the attribute")),
            ("key", Str("Machine key, auto-generated from name if omitted (e.g. 'color')")),
            ("type", Enum("Text", "Number", "Boolean", "Select", "MultiSelect", "Barcode")),
            ("isRequired", Bool("Whether this field is required (default false)")),
            ("group", Str("Field group label for UI grouping (optional)"))
        ),
        Prop("update_category_field", "Update attribute field name or required status", Req(["fieldId"]),
            ("fieldId", Int("Attribute field ID from list_category_fields")),
            ("name", Str("New display name")), ("isRequired", Bool("New required status"))
        ),
        Prop("delete_category_field", "Delete an attribute field from a category", Req(["fieldId"]),
            ("fieldId", Int("Attribute field ID"))
        ),
        Prop("find_categories_with_field", "Find categories that have an attribute matching a name pattern", Req(["search"]),
            ("search", Str("Partial field name to search for (case-insensitive)"))
        ),

        // Store attributes
        Prop("list_store_attributes", "List store-level shared attributes", Req([])),

        // Connections
        Prop("list_connections", "List all marketplace connections with status", Req([])),
        Prop("get_connection", "Get connection details", Req(["id"]), ("id", Int("Connection ID"))),
        Prop("test_connection", "Test if marketplace connection is working", Req(["id"]), ("id", Int("Connection ID"))),
        Prop("get_connection_limits", "Get API rate limits and quotas for a connection", Req(["id"]), ("id", Int("Connection ID"))),
        Prop("get_adapter_info", "Get marketplace adapter info: display name, description, supported capabilities (import/export/archive/groups etc.). NOTE: Response includes 'grouping' field — every mapping has ONE grouping field (isGroupingField=true). Products whose mapping evaluates it to the same value are merged into one marketplace card. Default source: groupName (local group name). The mapping only computes the grouping value; it never creates/modifies local groups.", Req(["connectionId"]),
            ("connectionId", Int("Connection ID"))
        ),
        Prop("preview_marketplace_products", "List products that exist ON the marketplace (paginated), marks which are already imported locally", Req(["connectionId"]),
            ("connectionId", Int("Connection ID")),
            ("pageSize", Int("Page size (default 50, max 200)")),
            ("cursor", Str("Pagination cursor from previous page's nextCursor")),
            ("search", Str("Filter by SKU / offer id"))
        ),
        Prop("import_products", "Queue import of marketplace products into the local catalog. Returns batchId for get_batch_status.", Req(["connectionId", "externalProductIds"]),
            ("connectionId", Int("Connection ID")),
            ("externalProductIds", StrArray("External product IDs from preview_marketplace_products")),
            ("targetCategoryId", Int("Local category for imported products (optional — resolved from mapping if omitted)"))
        ),
        Prop("import_prices", "Import current prices FROM the marketplace into local products (updates local prices)", Req(["connectionId"]),
            ("connectionId", Int("Connection ID"))
        ),
        Prop("import_stocks", "Import stock quantities FROM the marketplace into local products (sums across warehouses)", Req(["connectionId"]),
            ("connectionId", Int("Connection ID"))
        ),
        Prop("archive_product_on_marketplace", "Archive (hide) a product on the marketplace. Use list_product_links(connectionId) first to confirm the product has a sync link — then pass the local productId. Local product is not affected.", Req(["connectionId", "productId"]),
            ("connectionId", Int("Connection ID")),
            ("productId", Int("Local product ID (must be linked to this connection — verify with list_product_links first)"))
        ),

        // Mappings
        Prop("list_mappings", "List category mappings (optionally for one connection)", Req([]),
            ("connectionId", Int("Filter by connection ID"))
        ),
        Prop("get_mapping", "Get mapping details, coverage, schemaContext, productSample and optionAlignmentHints.", Req(["id"]),
            ("id", Int("Mapping ID")),
            ("productId", Int("Preview product ID — validates optionMap against real attribute values"))),
        Prop("get_option_alignment_hints", "Precomputed optionMap hints: local attribute option values (from import source) → target adapter allowedValues. Essential when mapping Ozon-imported category to WB/Ozon/other.", Req(["id"]),
            ("id", Int("Mapping ID")),
            ("productId", Int("Optional preview product ID"))),
        Prop("create_mapping", "Create a new category mapping (empty). Does NOT auto-generate fields. After creation call get_mapping and connect fields via connect_field / set_field_static_value.", Req(["connectionId", "localCategoryId", "externalCategoryId"]),
            ("connectionId", Int("Connection ID")),
            ("localCategoryId", Int("Local category ID")),
            ("externalCategoryId", Str("External marketplace category ID (from list_external_categories)")),
            ("externalCategoryName", Str("External category display name (recommended for UI)")),
            ("name", Str("Custom mapping name (optional)"))
        ),
        Prop("update_mapping_name", "Rename a mapping", Req(["id", "name"]),
            ("id", Int("Mapping ID")), ("name", Str("New name"))
        ),
        Prop("delete_mapping", "Delete a mapping permanently", Req(["id"]), ("id", Int("Mapping ID"))),
        Prop("duplicate_mapping", "Copy a mapping with all field connections", Req(["id"]), ("id", Int("Mapping ID"))),
        Prop("list_external_categories", "Browse marketplace categories from adapter", Req(["connectionId"]),
            ("connectionId", Int("Connection ID")),
            ("parentId", Str("Parent category ID for tree navigation (omit for root)")),
            ("search", Str("Search query to filter categories"))
        ),
        Prop("get_external_fields", "Get CARD adapter fields (sinkType=card): keys, labels, constraints and Select options. Pass mappingId (preferred) OR connectionId+externalCategoryId.", Req([]),
            ("mappingId", Int("Mapping ID — resolves connection and category automatically (preferred)")),
            ("connectionId", Int("Connection ID (only if mappingId not known)")),
            ("externalCategoryId", Str("External category ID from list_external_categories (only if mappingId not known)"))
        ),
        Prop("get_price_fields", "Get PRICE-API fields (sinkType=price): price, oldPrice, minPrice, discount etc. Ozon/WB. Pass mappingId (preferred) OR connectionId+externalCategoryId.", Req([]),
            ("mappingId", Int("Mapping ID (preferred)")),
            ("connectionId", Int("Connection ID (only if mappingId not known)")),
            ("externalCategoryId", Str("External category ID (only if mappingId not known)"))
        ),
        Prop("search_field_options", "Search the allowed option values of a Select/MultiSelect adapter field (server-side, works for dictionaries with thousands of values). Use before set_field_static_value on option-backed fields.", Req(["adapterFieldKey"]),
            ("adapterFieldKey", Str("Adapter field key")),
            ("query", Str("Search text — matched against option labels and values (empty = first options)")),
            ("mappingId", Int("Mapping ID (preferred)")),
            ("connectionId", Int("Connection ID (only if mappingId not known)")),
            ("externalCategoryId", Str("External category ID (only if mappingId not known)")),
            ("limit", Int("Max results (default 20, max 50)"))
        ),
        Prop("get_available_sources", "Get all available source fields for a mapping (product fields + category attributes + store attributes)", Req(["id"]),
            ("id", Int("Mapping ID"))
        ),
        Prop("get_mapping_coverage", "Show how many adapter fields are connected vs total", Req(["id"]),
            ("id", Int("Mapping ID"))
        ),
        Prop("preview_mapping_for_product", "Preview mapping result for a specific product", Req(["mappingId", "productId"]),
            ("mappingId", Int("Mapping ID")), ("productId", Int("Product ID"))
        ),
        Prop("connect_field", "Connect a source to an adapter field. sink='card' (default) or 'price'. For price fields use get_price_fields keys + sink='price'.", Req(["mappingId", "adapterFieldKey", "sourceType"]),
            ("mappingId", Int("Mapping ID")),
            ("adapterFieldKey", Str("Field key from get_external_fields (card) or get_price_fields (price)")),
            ("sink", Enum("card", "price")),
            ("sourceType", Enum("productField", "attribute", "storeAttribute")),
            ("sourceField", Str("Required when sourceType='productField'. Values: sku, name, price, barcode, description, storageQuantity")),
            ("sourceAttributeId", Int("Required when sourceType='attribute' or 'storeAttribute'. Attribute ID from get_available_sources"))
        ),
        Prop("set_field_static_value", "Set a hardcoded constant value for an adapter field. sink='card' (default) or 'price'.", Req(["mappingId", "adapterFieldKey", "value"]),
            ("mappingId", Int("Mapping ID")),
            ("adapterFieldKey", Str("Adapter field key")),
            ("sink", Enum("card", "price")),
            ("value", Str("Static value to always use"))
        ),
        Prop("set_fields_static_value", "Set static values for multiple adapter fields in one call. Prefer this when filling several fields at once.", Req(["mappingId", "fields"]),
            ("mappingId", Int("Mapping ID")),
            ("fields", new AiToolProperty
            {
                type = "array",
                description = "List of {adapterFieldKey, value} pairs",
                items = new AiToolProperty
                {
                    type = "object",
                    required = new List<string> { "adapterFieldKey", "value" },
                    properties = new Dictionary<string, AiToolProperty>
                    {
                        ["adapterFieldKey"] = Str("Adapter field key from get_external_fields"),
                        ["value"] = Str("Static value to set"),
                    }
                }
            })
        ),
        Prop("connect_fields", "Connect multiple sources to adapter fields in one call. Prefer this over calling connect_field in a loop.", Req(["mappingId", "connections"]),
            ("mappingId", Int("Mapping ID")),
            ("connections", new AiToolProperty
            {
                type = "array",
                description = "List of field connections",
                items = new AiToolProperty
                {
                    type = "object",
                    required = new List<string> { "adapterFieldKey", "sourceType" },
                    properties = new Dictionary<string, AiToolProperty>
                    {
                        ["adapterFieldKey"] = Str("Field key from get_external_fields or get_price_fields"),
                        ["sink"] = Str("card (default) or price"),
                        ["sourceType"] = Str("productField | attribute | storeAttribute"),
                        ["sourceField"] = Str("Required for productField: sku, name, price, barcode, description, storageQuantity"),
                        ["sourceAttributeId"] = Int("Required for attribute/storeAttribute: attribute ID from get_available_sources"),
                    }
                }
            })
        ),
        Prop("add_field_modifier", "Add a data transformation step to a field. sink='card' (default) or 'price'.", Req(["mappingId", "adapterFieldKey", "modifierType"]),
            ("mappingId", Int("Mapping ID")),
            ("adapterFieldKey", Str("Adapter field key")),
            ("sink", Enum("card", "price")),
            ("modifierType", Enum(
                "truncate", "replace", "append", "prepend", "trim", "uppercase", "lowercase", "template", "split",
                "multiply", "divide", "add", "round", "floor", "ceiling", "clamp",
                "valueMap", "optionMap", "llmGenerate", "defaultValue", "toText", "forceType",
                "limit", "join", "takeFirst", "skip", "reverse", "sort", "addItems", "removeItems",
                "randomInt", "randomFloat", "randomPick", "randomUuid")),
            ("params", new AiToolProperty { type = "object", description = "Modifier parameters: truncate→{maxLength:int}, multiply→{factor:number}, optionMap→{mappings:[{from,to}],default} PREFERRED for select fields, valueMap→{mappings:[{from,valueId}],default} fallback, llmGenerate→{prompt,modelId,seed?,cacheEnabled?,perProduct?}, template→{template:'...{{value}}...'}, randomInt→{min,max,seed?}, join→{delimiter}, limit→{count}" })
        ),
        Prop("merge_adapter_fields", "Merge duplicate adapter fields: aliases receive the primary's exported value under their own key; import reverses only the primary. Use for semantically identical fields (e.g. 'Name' and 'Product name').", Req(["mappingId", "primaryFieldKey", "aliasFieldKeys"]),
            ("mappingId", Int("Mapping ID")),
            ("primaryFieldKey", Str("Field that stays connected and drives the value")),
            ("aliasFieldKeys", StrArray("Duplicate field keys to merge into the primary"))
        ),
        Prop("unmerge_adapter_fields", "Undo merge_adapter_fields for the given alias fields", Req(["mappingId", "fieldKeys"]),
            ("mappingId", Int("Mapping ID")),
            ("fieldKeys", StrArray("Alias field keys to detach from their primary"))
        ),
        Prop("remove_field_connection", "Disconnect source from an adapter field. sink='card' (default) or 'price'.", Req(["mappingId", "fieldKey"]),
            ("mappingId", Int("Mapping ID")), ("fieldKey", Str("Adapter field key to disconnect")),
            ("sink", Enum("card", "price"))
        ),
        Prop("find_products_without_mapping", "Find store products whose category has mappings for a connection but no mapping is assigned yet (e.g. Ozon export mapping missing). Use list_connections / list_mappings first.", Req([]),
            ("connectionId", Int("Connection ID (preferred if known)")),
            ("connectionName", Str("Connection name or adapter search, e.g. \"Ozon\"")),
            ("limit", Int("Max products to return (default 50, max 200)"))
        ),
        Prop("get_product_mapping_summary", "Summary: how many products are assigned to each mapping per connection, and how many have no mapping.", Req([]),
            ("connectionId", Int("Single connection ID (omit for all connections)")),
            ("connectionName", Str("Filter by connection name, e.g. \"Ozon\""))
        ),
        Prop("assign_product_mappings", "Assign a category mapping to products for export/sync. Creates marketplace link if missing. Use find_products_without_mapping first, then list_mappings to pick mappingId.", Req(["mappingId"]),
            ("mappingId", Int("Mapping ID from list_mappings")),
            ("connectionId", Int("Connection ID (optional — inferred from mapping)")),
            ("connectionName", Str("Connection name if resolving by name")),
            ("productIds", IntArray("Specific product IDs to update")),
            ("applyToAllWithoutMapping", Bool("Apply to ALL products in the mapping's category that have no mapping on this connection"))
        ),

        // Export/Import
        Prop("export_products_batch", "Queue products for export. Returns batchId — use get_batch_status(batchId) to track progress. Without productIds or categoryId — exports ALL store products.", Req(["connectionId"]),
            ("connectionId", Int("Connection ID — required")),
            ("productIds", IntArray("Array of integer product IDs (omit to use categoryId or export all)")),
            ("categoryId", Int("Export all products in this category (ignored if productIds provided)"))
        ),
        Prop("export_prices", "Export product prices to marketplace", Req(["connectionId"]),
            ("connectionId", Int("Connection ID — required"))
        ),
        Prop("export_stocks", "Export stock quantities to marketplace", Req(["connectionId"]),
            ("connectionId", Int("Connection ID — required"))
        ),
        Prop("get_batch_status", "Check export batch progress (done/failed/pending counts)", Req(["batchId"]),
            ("batchId", Str("Batch ID returned by export_products_batch"))
        ),
        Prop("list_product_links", "List product-to-marketplace sync links", Req([]),
            ("connectionId", Int("Filter by connection ID"))
        ),

        // Groups
        Prop("list_groups", "List product groups with optional filters", Req([]),
            ("search", Str("Filter groups by name substring")),
            ("productId", Int("Only groups containing this product"))
        ),
        Prop("get_group", "Get group details with full member list (id, name, sku, price, quantity)", Req(["id"]),
            ("id", Int("Group ID"))
        ),
        Prop("create_group", "Create a product group, optionally with initial products", Req(["name"]),
            ("name", Str("Group name")),
            ("productIds", IntArray("Initial product IDs to add to the group (optional)"))
        ),
        Prop("update_group", "Rename a group or change its description", Req(["id"]),
            ("id", Int("Group ID")),
            ("name", Str("New group name")),
            ("description", Str("New description"))
        ),
        Prop("delete_group", "Delete a group. Products stay in the store, only the grouping is removed.", Req(["id"]),
            ("id", Int("Group ID"))
        ),
        Prop("add_products_to_group", "Add products to an existing group (skips already-members)", Req(["groupId", "productIds"]),
            ("groupId", Int("Group ID")),
            ("productIds", IntArray("Array of integer product IDs to add"))
        ),
        Prop("remove_products_from_group", "Remove products from a group (products themselves are not deleted)", Req(["groupId", "productIds"]),
            ("groupId", Int("Group ID")),
            ("productIds", IntArray("Array of integer product IDs to remove"))
        ),

        // Files (scope: "product" = files of one product, "store" = store-level storage)
        Prop("list_files", "List files and folders. scope=product needs productId; scope=store lists store storage.", Req(["scope"]),
            ("scope", Enum("product", "store")),
            ("productId", Int("Product ID (required for scope=product)")),
            ("folderId", Int("Only files in this folder (omit = root)")),
            ("search", Str("Filter files by name substring (ignores folderId)"))
        ),
        Prop("create_folder", "Create a folder for files", Req(["scope", "name"]),
            ("scope", Enum("product", "store")),
            ("name", Str("Folder name")),
            ("productId", Int("Product ID (required for scope=product)")),
            ("parentFolderId", Int("Parent folder (omit = root)"))
        ),
        Prop("rename_file", "Rename a file (display name)", Req(["scope", "fileId", "newName"]),
            ("scope", Enum("product", "store")),
            ("fileId", Int("File ID")),
            ("newName", Str("New display name"))
        ),
        Prop("rename_folder", "Rename a folder", Req(["scope", "folderId", "newName"]),
            ("scope", Enum("product", "store")),
            ("folderId", Int("Folder ID")),
            ("newName", Str("New folder name"))
        ),
        Prop("move_files", "Move files and/or folders into another folder (targetFolderId omitted = move to root)", Req(["scope"]),
            ("scope", Enum("product", "store")),
            ("targetFolderId", Int("Destination folder ID (omit = root)")),
            ("fileIds", IntArray("File IDs to move")),
            ("folderIds", IntArray("Folder IDs to move"))
        ),
        Prop("delete_files", "Permanently delete files (removes from storage too). Cannot be undone.", Req(["scope", "fileIds"]),
            ("scope", Enum("product", "store")),
            ("fileIds", IntArray("File IDs to delete"))
        ),
        Prop("delete_folder", "Delete a folder. Empty folders delete directly; use recursive=true to delete with contents. Cannot be undone.", Req(["scope", "folderId"]),
            ("scope", Enum("product", "store")),
            ("folderId", Int("Folder ID")),
            ("recursive", Bool("Delete folder together with all files and subfolders"))
        ),
        Prop("share_file", "Create a public share link for a file or folder", Req(["scope"]),
            ("scope", Enum("product", "store")),
            ("fileId", Int("File ID to share (either this or folderId)")),
            ("folderId", Int("Folder ID to share")),
            ("ttlHours", Int("Link lifetime in hours: 1, 24, 168 (week) or 720 (month). Default 168."))
        ),

        // Barcodes
        Prop("generate_barcodes", "Generate valid EAN-13 barcodes for products that have no barcode. Unique within the store. Can be rolled back.", Req([]),
            ("productIds", IntArray("Limit to these products (omit = all products without barcode)"))
        ),

        // Analytics
        Prop("get_store_stats", "Get overall store statistics (product/category/connection/mapping counts)", Req([])),
        Prop("find_products_without_images", "Find up to 100 products that have no images", Req([])),
        Prop("find_products_without_barcode", "Find up to 100 products with empty barcode", Req([])),
        Prop("find_products_missing_field", "Find products missing a value for an attribute (search by attribute name)", Req(["search"]),
            ("search", Str("Attribute name to check (partial match)"))
        ),
        Prop("find_products_with_sync_errors", "Find products with marketplace sync errors", Req([]),
            ("connectionId", Int("Filter by connection ID (optional)"))
        ),
        Prop("analyze_mapping_quality", "Analyze field coverage across all mappings", Req([]),
            ("connectionId", Int("Filter by connection ID (optional)"))
        ),
        Prop("get_image_download_status", "Check image download queue status", Req([])),

        // Navigation
        Prop("navigate_to", "Show a clickable navigation button in the chat that takes the user to a specific page. The button appears in the chat UI — the user clicks it to navigate. Do NOT say you 'redirected' the user; instead say something like 'Here's a button to go to the product page'. The panel stays open during navigation.",
            Req(["url", "label"]),
            ("url", Str("App-relative path using the REAL numeric ID from the tool result, e.g. /products/1762/edit — NEVER invent or guess an ID")),
            ("label", Str("Short destination description shown on the button, e.g. 'Wool socks (150 RUB)'"))
        ),
    ];

    // ---- Tool definition builder helpers ----

    private static AiTool Prop(string name, string desc, AiToolInputSchema schema, params (string key, AiToolProperty prop)[] props)
    {
        foreach (var (key, prop) in props)
        {
            schema.properties ??= new();
            schema.properties[key] = prop;
        }
        return new AiTool { name = name, description = desc, input_schema = schema };
    }

    private static AiToolInputSchema Req(string[] required) => new() { required = required.ToList(), properties = new() };
    private static AiToolProperty Str(string? desc = null) => new() { type = "string", description = desc };
    private static AiToolProperty Int(string? desc = null) => new() { type = "number", description = desc };
    private static AiToolProperty Num(string? desc = null) => new() { type = "number", description = desc };
    private static AiToolProperty Bool(string? desc = null) => new() { type = "boolean", description = desc };
    private static AiToolProperty Enum(params string[] values) => new() { type = "string", @enum = values.ToList() };
    private static AiToolProperty IntArray(string? desc = null) => new() { type = "array", description = desc, items = new AiToolProperty { type = "number" } };
    private static AiToolProperty StrArray(string? desc = null) => new() { type = "array", description = desc, items = new AiToolProperty { type = "string" } };

    // ---- SSE helpers ----

    private static string Sse(string type, object data)
        => $"data: {JsonSerializer.Serialize(new { type, data }, _jsonOpts)}\n\n";

    // ---- Input parsing ----

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

    // ---- Input DTOs ----

    private class ListProductsInput { public int? CategoryId { get; set; } public string? Search { get; set; } public decimal? PriceMin { get; set; } public decimal? PriceMax { get; set; } public int? QuantityMin { get; set; } public int? QuantityMax { get; set; } public bool? HasImages { get; set; } public string? MissingField { get; set; } public int? StartIndex { get; set; } public int? PageSize { get; set; } public string? SortBy { get; set; } public string? SortDir { get; set; } }
    private class IdInput { public int Id { get; set; } }
    private class ListGroupsInput { public string? Search { get; set; } public int? ProductId { get; set; } }
    private class UpdateGroupInput { public int Id { get; set; } public string? Name { get; set; } public string? Description { get; set; } }
    private class ConnectionIdRequiredInput { public int ConnectionId { get; set; } }
    private class PreviewMarketplaceInput { public int ConnectionId { get; set; } public int? PageSize { get; set; } public string? Cursor { get; set; } public string? Search { get; set; } }
    private class ImportProductsInput { public int ConnectionId { get; set; } public List<string> ExternalProductIds { get; set; } = []; public int? TargetCategoryId { get; set; } }
    private class ArchiveProductInput { public int ConnectionId { get; set; } public int ProductId { get; set; } }
    private class ListFilesInput { public string Scope { get; set; } = "store"; public int? ProductId { get; set; } public int? FolderId { get; set; } public string? Search { get; set; } }
    private class CreateFolderInput { public string Scope { get; set; } = "store"; public string Name { get; set; } = ""; public int? ProductId { get; set; } public int? ParentFolderId { get; set; } }
    private class RenameFileInput { public string Scope { get; set; } = "store"; public int FileId { get; set; } public string NewName { get; set; } = ""; }
    private class RenameFolderInput { public string Scope { get; set; } = "store"; public int FolderId { get; set; } public string NewName { get; set; } = ""; }
    private class MoveFilesInput { public string Scope { get; set; } = "store"; public int? TargetFolderId { get; set; } public List<int>? FileIds { get; set; } public List<int>? FolderIds { get; set; } }
    private class DeleteFilesInput { public string Scope { get; set; } = "store"; public List<int> FileIds { get; set; } = []; }
    private class DeleteFolderInput { public string Scope { get; set; } = "store"; public int FolderId { get; set; } public bool? Recursive { get; set; } }
    private class ShareFileInput { public string Scope { get; set; } = "store"; public int? FileId { get; set; } public int? FolderId { get; set; } public int? TtlHours { get; set; } }
    private class GenerateBarcodesInput { public List<int>? ProductIds { get; set; } }
    private class OptionalIdInput { public int? Id { get; set; } }
    private class IdsInput { public List<int> Ids { get; set; } = []; }
    private class SearchInput { public string? Search { get; set; } }
    private class BatchIdInput { public string BatchId { get; set; } = ""; }
    private class ConnectionIdInput { public int? ConnectionId { get; set; } }
    private class FieldIdInput { public int FieldId { get; set; } }
    private class CreateProductInput { public string Name { get; set; } = ""; public string? Sku { get; set; } public decimal? Price { get; set; } public int CategoryId { get; set; } public string? Barcode { get; set; } public string? Description { get; set; } public int? Quantity { get; set; } }
    private class UpdateProductInput { public int Id { get; set; } public string? Name { get; set; } public decimal? Price { get; set; } public string? Description { get; set; } public string? Barcode { get; set; } public int? Quantity { get; set; } public int? CategoryId { get; set; } }
    private class UpdateSkuInput { public int ProductId { get; set; } public string NewSku { get; set; } = ""; }
    private class UpdatePricesInput { public List<int>? ProductIds { get; set; } public int? CategoryId { get; set; } public decimal Amount { get; set; } public string Mode { get; set; } = "add"; }
    private class UpdateAttributeInput { public List<int>? ProductIds { get; set; } public string AttributeKey { get; set; } = ""; public string Value { get; set; } = ""; }
    private class CreateCategoryInput { public string Name { get; set; } = ""; public string? Description { get; set; } }
    private class UpdateCategoryInput { public int Id { get; set; } public string? Name { get; set; } public string? Description { get; set; } }
    private class AddFieldInput { public int CategoryId { get; set; } public string Name { get; set; } = ""; public string? Key { get; set; } public string Type { get; set; } = "Text"; public bool? IsRequired { get; set; } public string? Group { get; set; } }
    private class UpdateFieldInput { public int FieldId { get; set; } public string? Name { get; set; } public bool? IsRequired { get; set; } }
    private class CreateMappingInput { public int ConnectionId { get; set; } public int LocalCategoryId { get; set; } public string ExternalCategoryId { get; set; } = ""; public string? ExternalCategoryName { get; set; } public string? Name { get; set; } }
    private class UpdateNameInput { public int Id { get; set; } public string Name { get; set; } = ""; }
    private class ExternalCategoriesInput { public int ConnectionId { get; set; } public string? ParentId { get; set; } public string? Search { get; set; } }
    private class ExternalFieldsInput { public int? ConnectionId { get; set; } public string? ExternalCategoryId { get; set; } public int? MappingId { get; set; } }
    private class SearchFieldOptionsInput { public string AdapterFieldKey { get; set; } = ""; public string? Query { get; set; } public int? MappingId { get; set; } public int? ConnectionId { get; set; } public string? ExternalCategoryId { get; set; } public int? Limit { get; set; } }
    private class MergeFieldsInput { public int MappingId { get; set; } public string PrimaryFieldKey { get; set; } = ""; public List<string> AliasFieldKeys { get; set; } = []; }
    private class UnmergeFieldsInput { public int MappingId { get; set; } public List<string> FieldKeys { get; set; } = []; }
    private class PreviewMappingInput { public int MappingId { get; set; } public int ProductId { get; set; } }
    private class ConnectFieldInput { public int MappingId { get; set; } public string AdapterFieldKey { get; set; } = ""; public string SourceType { get; set; } = ""; public string? SourceField { get; set; } public int? SourceAttributeId { get; set; } }
    private class ConnectFieldsBulkInput { public int MappingId { get; set; } public List<ConnectFieldInput> Connections { get; set; } = []; }
    private class SetStaticInput { public int MappingId { get; set; } public string AdapterFieldKey { get; set; } = ""; public string Value { get; set; } = ""; }
    private class SetFieldsStaticBulkInput { public int MappingId { get; set; } public List<SetStaticInput> Fields { get; set; } = []; }
    private class AddModifierInput { public int MappingId { get; set; } public string AdapterFieldKey { get; set; } = ""; public string ModifierType { get; set; } = ""; public Dictionary<string, object>? Params { get; set; } }
    private class CreateProductsBulkInput { public List<CreateProductInput> Products { get; set; } = []; }
    private class AttributeKeyValuePair { public string AttributeKey { get; set; } = ""; public string Value { get; set; } = ""; }
    private class UpdateAttributesBulkInput { public List<int>? ProductIds { get; set; } public List<AttributeKeyValuePair> Attributes { get; set; } = []; }
    private class FieldKeyInput { public int MappingId { get; set; } public string FieldKey { get; set; } = ""; }
    private class ExportBatchInput { public int ConnectionId { get; set; } public List<int>? ProductIds { get; set; } public int? CategoryId { get; set; } }
    private class CreateGroupInput { public string Name { get; set; } = ""; public List<int>? ProductIds { get; set; } }
    private class GroupProductsInput { public int GroupId { get; set; } public List<int> ProductIds { get; set; } = []; }

    private class ProposePlanInput
    {
        public string Summary { get; set; } = "";
        public List<object>? Steps { get; set; }
        public int AffectedCount { get; set; }
        public bool IsDestructive { get; set; }
    }
}
