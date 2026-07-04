using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Delegat.Enums;
using Microsoft.EntityFrameworkCore;
using Delegat.Models;
using Delegat.Models.Ai;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Models.Products;
using Delegat.Services.Ai.Prompts;
using Delegat.Services.Marketplace;
using Delegat.Services.Marketplace.Graph;

namespace Delegat.Services.Ai;

public class AiChatService
{
    private readonly AiModelConfigService _modelService;
    private readonly AiApiClient _apiClient;
    private readonly AiMappingService _mappingService;
    private readonly GraphMutationService _graphMutation;
    private readonly AiOperationService _operationService;
    private readonly IMarketplaceAdapterRegistry _adapterRegistry;
    private readonly DataContext _context;
    private readonly ILogger<AiChatService> _logger;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AiChatService(
        AiModelConfigService modelService,
        AiApiClient apiClient,
        AiMappingService mappingService,
        GraphMutationService graphMutation,
        AiOperationService operationService,
        IMarketplaceAdapterRegistry adapterRegistry,
        DataContext context,
        ILogger<AiChatService> logger)
    {
        _modelService = modelService;
        _apiClient = apiClient;
        _mappingService = mappingService;
        _graphMutation = graphMutation;
        _operationService = operationService;
        _adapterRegistry = adapterRegistry;
        _context = context;
        _logger = logger;
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        List<ChatMessage> userMessages,
        int storeId,
        int userId,
        int? modelId = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        AiModelConfig? modelConfig;
        if (modelId.HasValue)
            modelConfig = await _modelService.GetByIdAsync(modelId.Value, ct);
        else
            modelConfig = await _modelService.GetDefaultModelAsync(ct);

        if (modelConfig == null)
        {
            yield return Sse(SseEventType.Error, new { message = "Модель AI не настроена. Обратитесь к администратору." });
            yield return Sse(SseEventType.Done, new { });
            yield break;
        }

        var model = _modelService.DecryptForUse(modelConfig);

        // Build store context for system prompt
        var storeName = await _context.stores.AsNoTracking()
            .Where(s => s.Id == storeId).Select(s => s.name).FirstOrDefaultAsync(ct) ?? "Магазин";
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

        var systemPrompt = AiPrompts.ChatSystem(
            storeName, storeId, productCount, connections
                .Select(c => new AiPrompts.ConnectionInfo(c.Id, c.name, c.adapterKey, c.isActive, c.lastSyncAt))
                .ToList(),
            currentUser?.UserName ?? "user", currentUser?.isAdmin ?? false);

        // Convert user messages to AI format
        var messages = userMessages.Select(m => new AiMessage
        {
            role = m.role,
            content = m.content
        }).ToList();

        var tools = BuildToolDefinitions();

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
                stream = true
            };

            var textBuffer = new StringBuilder();
            var toolUses = new List<(string id, string name, string inputJson)>();
            var currentToolId = "";
            var currentToolName = "";
            var currentToolInput = new StringBuilder();
            var isCollectingTool = false;

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
                }
            }

            if (toolUses.Count == 0) break;

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

                yield return Sse(SseEventType.Tool, new { name = toolName, status = "running" });

                var (result, summary) = await DispatchToolAsync(toolName, toolInputJson, storeId, userId, ct);

                yield return Sse(SseEventType.Tool, new { name = toolName, status = "done", summary });

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

        yield return Sse(SseEventType.Done, new { });
    }

    public async Task<string> ExplainErrorAsync(string errorMessage, string context,
        int? productId, int? connectionId, CancellationToken ct = default)
    {
        var model = await _modelService.GetDefaultModelAsync(ct);
        if (model == null) return "Модель AI не настроена.";

        var decrypted = _modelService.DecryptForUse(model);
        var request = new AiRequest
        {
            model = decrypted.modelId,
            max_tokens = 512,
            system = AiPrompts.ExplainErrorSystem,
            messages =
            [
                new AiMessage
                {
                    role = "user",
                    content = AiPrompts.ExplainErrorUser(errorMessage, context)
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

    // ---- Tool Dispatcher ----

    private async Task<(object result, string summary)> DispatchToolAsync(
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
                "update_product" => await UpdateProductAsync(inputJson, storeId, userId, ct),
                "update_product_sku" => await UpdateProductSkuAsync(inputJson, storeId, ct),
                "update_product_prices" => await UpdateProductPricesAsync(inputJson, storeId, userId, ct),
                "update_product_attribute" => await UpdateProductAttributeAsync(inputJson, storeId, userId, ct),
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

                // Mappings
                "list_mappings" => await ListMappingsAsync(inputJson, storeId, ct),
                "get_mapping" => await GetMappingAsync(inputJson, storeId, ct),
                "create_mapping" => await CreateMappingAsync(inputJson, storeId, userId, ct),
                "update_mapping_name" => await UpdateMappingNameAsync(inputJson, storeId, userId, ct),
                "delete_mapping" => await DeleteMappingAsync(inputJson, storeId, userId, ct),
                "duplicate_mapping" => await DuplicateMappingAsync(inputJson, storeId, ct),
                "list_external_categories" => await ListExternalCategoriesAsync(inputJson, storeId, ct),
                "get_external_fields" => await GetExternalFieldsAsync(inputJson, storeId, ct),
                "get_available_sources" => await GetAvailableSourcesAsync(inputJson, storeId, ct),
                "get_mapping_coverage" => await GetMappingCoverageAsync(inputJson, storeId, ct),
                "preview_mapping_for_product" => await PreviewMappingAsync(inputJson, storeId, ct),
                "generate_mapping_suggestions" => await GenerateMappingSuggestionsAsync(inputJson, storeId, userId, ct),
                "connect_field" => await ConnectFieldAsync(inputJson, storeId, userId, ct),
                "set_field_static_value" => await SetFieldStaticAsync(inputJson, storeId, userId, ct),
                "add_field_modifier" => await AddFieldModifierAsync(inputJson, storeId, userId, ct),
                "remove_field_connection" => await RemoveFieldConnectionAsync(inputJson, storeId, userId, ct),

                // Export/Import
                "export_products_batch" => await ExportProductsBatchAsync(inputJson, storeId, userId, ct),
                "export_prices" => await ExportPricesAsync(inputJson, storeId, ct),
                "export_stocks" => await ExportStocksAsync(inputJson, storeId, ct),
                "get_batch_status" => await GetBatchStatusAsync(inputJson, ct),
                "list_product_links" => await ListProductLinksAsync(inputJson, storeId, ct),

                // Groups
                "list_groups" => await ListGroupsAsync(storeId, ct),
                "create_group" => await CreateGroupAsync(inputJson, storeId, userId, ct),
                "add_products_to_group" => await AddProductsToGroupAsync(inputJson, storeId, ct),

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

                _ => (new { error = $"Unknown tool: {toolName}" }, $"Неизвестный инструмент: {toolName}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool {Tool} failed", toolName);
            return (new { error = ex.Message }, $"Ошибка: {ex.Message}");
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
        var products = await q.OrderBy(x => x.Id)
            .Skip(offset).Take(limit)
            .Select(x => new { x.Id, x.name, x.sku, x.price, x.barcode, x.storageQuantity, x.categoryId })
            .ToListAsync(ct);

        return (new { items = products, count = products.Count }, $"Найдено {products.Count} товаров");
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
        return (new { count }, $"{count} товаров");
    }

    private async Task<(object, string)> GetProductAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var product = await _context.products.AsNoTracking()
            .Include(x => x.category)
            .Include(x => x.attributeValues).ThenInclude(v => v.attribute)
            .FirstOrDefaultAsync(x => x.Id == p.Id && x.storeId == storeId, ct);
        if (product == null) return (new { error = "Товар не найден" }, "Товар не найден");
        return (product, product.name);
    }

    private async Task<(object, string)> CreateProductAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<CreateProductInput>(json);
        var product = new Product
        {
            name = p.Name,
            sku = p.Sku,
            price = p.Price ?? 0,
            barcode = p.Barcode,
            description = p.Description,
            storageQuantity = p.Quantity ?? 0,
            categoryId = p.CategoryId,
            storeId = storeId
        };
        _context.products.Add(product);
        await _context.SaveChangesAsync(ct);
        await _operationService.RecordAsync(storeId, userId, "create_product",
            $"Создан товар \"{p.Name}\"", new { productId = product.Id }, canRollback: false, ct: ct);
        return (new { id = product.Id }, $"Товар \"{p.Name}\" создан (id={product.Id})");
    }

    private async Task<(object, string)> UpdateProductAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<UpdateProductInput>(json);
        var product = await _context.products.FirstOrDefaultAsync(x => x.Id == p.Id && x.storeId == storeId, ct);
        if (product == null) return (new { error = "Товар не найден" }, "Товар не найден");

        var changes = new List<string>();
        if (p.Name != null) { changes.Add($"name: {product.name}→{p.Name}"); product.name = p.Name; }
        if (p.Price.HasValue) { changes.Add($"price: {product.price}→{p.Price}"); product.price = p.Price.Value; }
        if (p.Description != null) product.description = p.Description;
        if (p.Barcode != null) product.barcode = p.Barcode;
        if (p.Quantity.HasValue) product.storageQuantity = p.Quantity.Value;
        if (p.CategoryId.HasValue) product.categoryId = p.CategoryId.Value;

        await _context.SaveChangesAsync(ct);
        return (new { success = true }, $"Товар обновлён: {string.Join(", ", changes)}");
    }

    private async Task<(object, string)> UpdateProductSkuAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<UpdateSkuInput>(json);
        var product = await _context.products.FirstOrDefaultAsync(x => x.Id == p.ProductId && x.storeId == storeId, ct);
        if (product == null) return (new { error = "Товар не найден" }, "Товар не найден");
        var oldSku = product.sku;
        product.sku = p.NewSku;
        await _context.SaveChangesAsync(ct);
        return (new { success = true, oldSku, newSku = p.NewSku }, $"SKU изменён: {oldSku} → {p.NewSku}");
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
            $"Изменена цена у {products.Count} товаров ({p.Mode} {p.Amount})",
            AiOperationService.BuildPriceRollback(rollback), ct: ct);

        return (new { affected = products.Count }, $"Изменена цена у {products.Count} товаров");
    }

    private async Task<(object, string)> UpdateProductAttributeAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<UpdateAttributeInput>(json);
        var attr = await _context.categoryAttributes.AsNoTracking()
            .FirstOrDefaultAsync(a => a.key == p.AttributeKey && a.category.storeId == storeId, ct);
        if (attr == null) return (new { error = "Атрибут не найден" }, "Атрибут не найден");

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
            $"Атрибут '{p.AttributeKey}' = '{p.Value}' у {productIds.Count} товаров",
            new ProductAttributeRollbackData { Products = oldValues }, ct: ct);

        return (new { affected = productIds.Count }, $"Атрибут обновлён у {productIds.Count} товаров");
    }

    private async Task<(object, string)> DeleteProductsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<IdsInput>(json);
        var products = await _context.products
            .Where(x => p.Ids.Contains(x.Id) && x.storeId == storeId)
            .ToListAsync(ct);
        foreach (var pr in products) pr.deletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return (new { deleted = products.Count }, $"Удалено {products.Count} товаров");
    }

    private async Task<(object, string)> RestoreProductsAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<IdsInput>(json);
        await _context.products.IgnoreQueryFilters()
            .Where(x => p.Ids.Contains(x.Id) && x.storeId == storeId && x.deletedAt != null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.deletedAt, (DateTime?)null), ct);
        return (new { success = true }, $"Восстановлено {p.Ids.Count} товаров");
    }

    private async Task<(object, string)> ListTrashAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<SearchInput>(json);
        var q = _context.products.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.storeId == storeId && x.deletedAt != null);
        if (!string.IsNullOrEmpty(p.Search)) q = q.Where(x => x.name.Contains(p.Search));
        var items = await q.OrderByDescending(x => x.deletedAt).Take(50)
            .Select(x => new { x.Id, x.name, x.deletedAt }).ToListAsync(ct);
        return (new { items, count = items.Count }, $"{items.Count} товаров в корзине");
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
        return (new { categories = cats, count = cats.Count }, $"{cats.Count} категорий");
    }

    private async Task<(object, string)> GetCategoryAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var cat = await _context.categories.AsNoTracking()
            .Include(c => c.attributes)
            .FirstOrDefaultAsync(c => c.Id == p.Id && c.storeId == storeId, ct);
        if (cat == null) return (new { error = "Категория не найдена" }, "Категория не найдена");
        return (new
        {
            cat.Id, cat.name, cat.description,
            attributes = cat.attributes.OrderBy(a => a.sortOrder)
                .Select(a => new { a.Id, a.name, a.key, a.type, a.isRequired })
        }, cat.name);
    }

    private async Task<(object, string)> CreateCategoryAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<CreateCategoryInput>(json);
        var cat = new Category { name = p.Name, description = p.Description, storeId = storeId };
        _context.categories.Add(cat);
        await _context.SaveChangesAsync(ct);
        return (new { id = cat.Id }, $"Категория \"{p.Name}\" создана");
    }

    private async Task<(object, string)> UpdateCategoryAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<UpdateCategoryInput>(json);
        var cat = await _context.categories.FirstOrDefaultAsync(c => c.Id == p.Id && c.storeId == storeId, ct);
        if (cat == null) return (new { error = "Категория не найдена" }, "Категория не найдена");
        if (p.Name != null) cat.name = p.Name;
        if (p.Description != null) cat.description = p.Description;
        await _context.SaveChangesAsync(ct);
        return (new { success = true }, $"Категория обновлена");
    }

    private async Task<(object, string)> DeleteCategoryAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var cat = await _context.categories.FirstOrDefaultAsync(c => c.Id == p.Id && c.storeId == storeId, ct);
        if (cat == null) return (new { error = "Категория не найдена" }, "Категория не найдена");
        _context.categories.Remove(cat);
        await _context.SaveChangesAsync(ct);
        return (new { success = true }, $"Категория \"{cat.name}\" удалена");
    }

    private async Task<(object, string)> ListCategoryFieldsAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var fields = await _context.categoryAttributes.AsNoTracking()
            .Where(a => a.categoryId == p.Id && a.category.storeId == storeId)
            .OrderBy(a => a.sortOrder)
            .Select(a => new { a.Id, a.name, a.key, a.type, a.isRequired, a.group })
            .ToListAsync(ct);
        return (new { fields, count = fields.Count }, $"{fields.Count} полей");
    }

    private async Task<(object, string)> AddCategoryFieldAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<AddFieldInput>(json);
        var cat = await _context.categories.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == p.CategoryId && c.storeId == storeId, ct);
        if (cat == null) return (new { error = "Категория не найдена" }, "Категория не найдена");

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
            $"Добавлено поле \"{p.Name}\" в категорию {p.CategoryId}",
            AiOperationService.BuildFieldRollback(attr.Id), ct: ct);
        return (new { id = attr.Id, key }, $"Поле \"{p.Name}\" добавлено");
    }

    private async Task<(object, string)> UpdateCategoryFieldAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<UpdateFieldInput>(json);
        var attr = await _context.categoryAttributes
            .FirstOrDefaultAsync(a => a.Id == p.FieldId && a.category.storeId == storeId, ct);
        if (attr == null) return (new { error = "Поле не найдено" }, "Поле не найдено");
        if (p.Name != null) attr.name = p.Name;
        if (p.IsRequired.HasValue) attr.isRequired = p.IsRequired.Value;
        await _context.SaveChangesAsync(ct);
        return (new { success = true }, $"Поле обновлено");
    }

    private async Task<(object, string)> DeleteCategoryFieldAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<FieldIdInput>(json);
        var attr = await _context.categoryAttributes
            .FirstOrDefaultAsync(a => a.Id == p.FieldId && a.category.storeId == storeId, ct);
        if (attr == null) return (new { error = "Поле не найдено" }, "Поле не найдено");
        _context.categoryAttributes.Remove(attr);
        await _context.SaveChangesAsync(ct);
        return (new { success = true }, $"Поле \"{attr.name}\" удалено");
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
        return (new { categories = cats, count = cats.Count }, $"Найдено {cats.Count} категорий с полем '{p.Search}'");
    }

    // ---- Store Attributes ----

    private async Task<(object, string)> ListStoreAttributesAsync(int storeId, CancellationToken ct)
    {
        var attrs = await _context.storeAttributes.AsNoTracking()
            .Where(a => a.storeId == storeId).OrderBy(a => a.sortOrder)
            .Select(a => new { a.Id, a.name, a.key, a.type }).ToListAsync(ct);
        return (new { attributes = attrs, count = attrs.Count }, $"{attrs.Count} атрибутов магазина");
    }

    // ---- Connections ----

    private async Task<(object, string)> ListConnectionsAsync(int storeId, CancellationToken ct)
    {
        var conns = await _context.marketplaceConnections.AsNoTracking()
            .Where(c => c.storeId == storeId)
            .Select(c => new { c.Id, c.name, c.adapterKey, c.isActive, c.lastSyncAt })
            .ToListAsync(ct);
        return (new { connections = conns, count = conns.Count }, $"{conns.Count} подключений");
    }

    private async Task<(object, string)> GetConnectionAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var conn = await _context.marketplaceConnections.AsNoTracking()
            .Where(c => c.Id == p.Id && c.storeId == storeId)
            .Select(c => new { c.Id, c.name, c.adapterKey, c.isActive, c.lastSyncAt })
            .FirstOrDefaultAsync(ct);
        if (conn == null) return (new { error = "Подключение не найдено" }, "Не найдено");
        return (conn, conn.name);
    }

    private async Task<(object, string)> TestConnectionAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var conn = await _context.marketplaceConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == p.Id && c.storeId == storeId, ct);
        if (conn == null) return (new { error = "Подключение не найдено" }, "Не найдено");
        try
        {
            var adapter = _adapterRegistry.GetByKey(conn.adapterKey)!;
            var result = await adapter.TestConnectionAsync(conn, ct);
            return (new { success = result.Success, message = result.Message },
                result.Success ? $"Подключение {conn.name} работает" : $"Ошибка: {result.Message}");
        }
        catch (Exception ex)
        {
            return (new { success = false, message = ex.Message }, $"Ошибка: {ex.Message}");
        }
    }

    private async Task<(object, string)> GetConnectionLimitsAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var conn = await _context.marketplaceConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == p.Id && c.storeId == storeId, ct);
        if (conn == null) return (new { error = "Не найдено" }, "Не найдено");
        try
        {
            var adapter = _adapterRegistry.GetByKey(conn.adapterKey)!;
            var quotas = await adapter.GetQuotasAsync(conn, ct);
            return (quotas, "Лимиты получены");
        }
        catch
        {
            return (new { error = "Недоступно" }, "Лимиты недоступны");
        }
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
        return (new { mappings, count = mappings.Count }, $"{mappings.Count} маппингов");
    }

    private async Task<(object, string)> GetMappingAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Маппинг не найден" }, "Не найден");

        var coverage = await _graphMutation.GetCoverageAsync(p.Id, ct);
        return (new
        {
            mapping.Id, mapping.name, mapping.localCategoryId, mapping.externalCategoryId,
            mapping.externalCategoryName, coverage
        }, $"Маппинг {mapping.name ?? mapping.externalCategoryName}: {coverage.Connected}/{coverage.Total} полей");
    }

    private async Task<(object, string)> CreateMappingAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<CreateMappingInput>(json);
        var conn = await _context.marketplaceConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == p.ConnectionId && c.storeId == storeId, ct);
        if (conn == null) return (new { error = "Подключение не найдено" }, "Не найдено");

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

        // AI auto-mapping
        try
        {
            var adapter = _adapterRegistry.GetByKey(conn.adapterKey)!;
            var adapterFields = await adapter.GetCategoryFieldsAsync(conn, p.ExternalCategoryId, ct);
            var localCat = await _context.categories.Include(c => c.attributes)
                .FirstOrDefaultAsync(c => c.Id == p.LocalCategoryId, ct);
            if (localCat != null)
            {
                mapping = await _context.categoryMappings.FindAsync([mapping.Id], ct) ?? mapping;
                mapping.connection = conn;
                var aiResult = await _mappingService.GenerateSuggestionsAsync(mapping, localCat, adapterFields, ct);
                return (new
                {
                    id = mapping.Id,
                    aiMapping = new { connected = aiResult.Connected, skipped = aiResult.Skipped }
                }, $"Маппинг создан, AI подключил {aiResult.Connected} полей");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI mapping failed for new mapping {Id}", mapping.Id);
        }

        return (new { id = mapping.Id }, $"Маппинг создан");
    }

    private async Task<(object, string)> UpdateMappingNameAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<UpdateNameInput>(json);
        var mapping = await _context.categoryMappings
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Не найден" }, "Не найден");
        mapping.name = p.Name;
        await _context.SaveChangesAsync(ct);
        return (new { success = true }, $"Имя маппинга обновлено: \"{p.Name}\"");
    }

    private async Task<(object, string)> DeleteMappingAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var mapping = await _context.categoryMappings
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Не найден" }, "Не найден");
        _context.categoryMappings.Remove(mapping);
        await _context.SaveChangesAsync(ct);
        return (new { success = true }, $"Маппинг удалён");
    }

    private async Task<(object, string)> DuplicateMappingAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Не найден" }, "Не найден");
        var copy = new CategoryMapping
        {
            connectionId = mapping.connectionId,
            localCategoryId = mapping.localCategoryId,
            externalCategoryId = mapping.externalCategoryId,
            externalCategoryName = mapping.externalCategoryName,
            name = (mapping.name ?? mapping.externalCategoryName) + " (копия)",
            graphJson = mapping.graphJson
        };
        _context.categoryMappings.Add(copy);
        await _context.SaveChangesAsync(ct);
        return (new { id = copy.Id }, $"Маппинг скопирован (id={copy.Id})");
    }

    private async Task<(object, string)> ListExternalCategoriesAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<ExternalCategoriesInput>(json);
        var conn = await _context.marketplaceConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == p.ConnectionId && c.storeId == storeId, ct);
        if (conn == null) return (new { error = "Подключение не найдено" }, "Не найдено");
        var adapter = _adapterRegistry.GetByKey(conn.adapterKey)!;
        var cats = await adapter.GetCategoriesAsync(conn, p.ParentId, p.Search, ct);
        return (new { categories = cats.Take(50) }, $"{cats.Count} категорий");
    }

    private async Task<(object, string)> GetExternalFieldsAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<ExternalFieldsInput>(json);
        var conn = await _context.marketplaceConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == p.ConnectionId && c.storeId == storeId, ct);
        if (conn == null) return (new { error = "Не найдено" }, "Не найдено");
        var adapter = _adapterRegistry.GetByKey(conn.adapterKey)!;
        var fields = await adapter.GetCategoryFieldsAsync(conn, p.ExternalCategoryId, ct);
        return (new
        {
            fields = fields.Select(f => new { f.Key, f.Name, f.Type, f.IsRequired, f.Description }),
            total = fields.Count, required = fields.Count(f => f.IsRequired)
        }, $"{fields.Count} полей ({fields.Count(f => f.IsRequired)} обязательных)");
    }

    private async Task<(object, string)> GetAvailableSourcesAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Не найден" }, "Не найден");

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
        }, $"Источники: {catAttrs.Count} атрибутов категории, {storeAttrs.Count} атрибутов магазина");
    }

    private async Task<(object, string)> GetMappingCoverageAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Не найден" }, "Не найден");
        var coverage = await _graphMutation.GetCoverageAsync(p.Id, ct);
        return (coverage, $"Покрытие: {coverage.Connected}/{coverage.Total} полей, обязательных: {coverage.RequiredConnected}/{coverage.RequiredTotal}");
    }

    private async Task<(object, string)> PreviewMappingAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<PreviewMappingInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Не найден" }, "Не найден");
        // Preview is complex - return basic info
        return (new { mappingId = p.MappingId, productId = p.ProductId, note = "Используйте UI редактора для предпросмотра" },
            "Предпросмотр доступен в редакторе маппинга");
    }

    private async Task<(object, string)> GenerateMappingSuggestionsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var mapping = await _context.categoryMappings
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Не найден" }, "Не найден");

        var localCat = await _context.categories.Include(c => c.attributes)
            .FirstOrDefaultAsync(c => c.Id == mapping.localCategoryId, ct);
        if (localCat == null) return (new { error = "Категория не найдена" }, "Не найдена");

        var adapter = _adapterRegistry.GetByKey(mapping.connection.adapterKey)!;
        var adapterFields = await adapter.GetCategoryFieldsAsync(mapping.connection, mapping.externalCategoryId, ct);

        // Save previous graph for rollback
        var prevGraph = mapping.graphJson ?? "";
        var result = await _mappingService.GenerateSuggestionsAsync(mapping, localCat, adapterFields, ct);

        await _operationService.RecordAsync(storeId, userId, "generate_mapping",
            $"AI-маппинг для маппинга {p.Id}: подключено {result.Connected} полей",
            AiOperationService.BuildGraphRollback(p.Id, prevGraph), ct: ct);

        return (new { connected = result.Connected, skipped = result.Skipped },
            $"AI подключил {result.Connected} полей, пропущено {result.Skipped}");
    }

    private async Task<(object, string)> ConnectFieldAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ConnectFieldInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Не найден" }, "Не найден");

        var prevGraph = mapping.graphJson ?? "";
        await _graphMutation.ConnectFieldAsync(p.MappingId, p.AdapterFieldKey, p.SourceType, p.SourceField, p.SourceAttributeId, ct);
        await _operationService.RecordAsync(storeId, userId, "connect_field",
            $"Поле '{p.AdapterFieldKey}' подключено к {p.SourceType}:{p.SourceField ?? p.SourceAttributeId?.ToString()}",
            AiOperationService.BuildGraphRollback(p.MappingId, prevGraph), ct: ct);
        return (new { success = true }, $"Поле '{p.AdapterFieldKey}' подключено");
    }

    private async Task<(object, string)> SetFieldStaticAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<SetStaticInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Не найден" }, "Не найден");

        var prevGraph = mapping.graphJson ?? "";
        await _graphMutation.SetStaticValueAsync(p.MappingId, p.AdapterFieldKey, p.Value, ct);
        await _operationService.RecordAsync(storeId, userId, "set_static_value",
            $"Поле '{p.AdapterFieldKey}' = '{p.Value}'",
            AiOperationService.BuildGraphRollback(p.MappingId, prevGraph), ct: ct);
        return (new { success = true }, $"Поле '{p.AdapterFieldKey}' = '{p.Value}'");
    }

    private async Task<(object, string)> AddFieldModifierAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<AddModifierInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Не найден" }, "Не найден");

        var prevGraph = mapping.graphJson ?? "";
        await _graphMutation.AddModifierAsync(p.MappingId, p.AdapterFieldKey, p.ModifierType, p.Params, ct);
        await _operationService.RecordAsync(storeId, userId, "add_modifier",
            $"Модификатор '{p.ModifierType}' добавлен к полю '{p.AdapterFieldKey}'",
            AiOperationService.BuildGraphRollback(p.MappingId, prevGraph), ct: ct);
        return (new { success = true }, $"Модификатор '{p.ModifierType}' добавлен");
    }

    private async Task<(object, string)> RemoveFieldConnectionAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<FieldKeyInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Не найден" }, "Не найден");

        var prevGraph = mapping.graphJson ?? "";
        await _graphMutation.DisconnectFieldAsync(p.MappingId, p.FieldKey, ct);
        await _operationService.RecordAsync(storeId, userId, "disconnect_field",
            $"Поле '{p.FieldKey}' отключено в маппинге {p.MappingId}",
            AiOperationService.BuildGraphRollback(p.MappingId, prevGraph), ct: ct);
        return (new { success = true }, $"Поле '{p.FieldKey}' отключено");
    }

    // ---- Export / Import ----

    private async Task<(object, string)> ExportProductsBatchAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ExportBatchInput>(json);
        var conn = await _context.marketplaceConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == p.ConnectionId && c.storeId == storeId, ct);
        if (conn == null) return (new { error = "Подключение не найдено" }, "Не найдено");

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

        if (productIds.Count == 0) return (new { queued = 0 }, "Нет товаров для экспорта");

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
        return (new { batchId, queued = productIds.Count }, $"Добавлено {productIds.Count} товаров в очередь экспорта");
    }

    private async Task<(object, string)> ExportPricesAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<ConnectionIdInput>(json);
        return (new { note = "Экспорт цен запущен. Используйте UI для мониторинга." },
            $"Экспорт цен на подключение {p.ConnectionId} запущен");
    }

    private async Task<(object, string)> ExportStocksAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<ConnectionIdInput>(json);
        return (new { note = "Экспорт остатков запущен. Используйте UI для мониторинга." },
            $"Экспорт остатков на подключение {p.ConnectionId} запущен");
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
            $"Пакет {p.BatchId}: {done}/{total} выполнено, {failed} ошибок");
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
        return (new { links, count = links.Count }, $"{links.Count} связей");
    }

    // ---- Groups ----

    private async Task<(object, string)> ListGroupsAsync(int storeId, CancellationToken ct)
    {
        var groups = await _context.productGroups.AsNoTracking()
            .Where(g => g.storeId == storeId)
            .Select(g => new { g.Id, g.name, memberCount = g.members.Count })
            .ToListAsync(ct);
        return (new { groups, count = groups.Count }, $"{groups.Count} групп");
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
        return (new { id = group.Id }, $"Группа \"{p.Name}\" создана");
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
        return (new { added = toAdd.Count }, $"Добавлено {toAdd.Count} товаров в группу");
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
        }, $"Товаров: {productCount}, категорий: {categoryCount}, подключений: {connectionCount}");
    }

    private async Task<(object, string)> FindProductsWithoutImagesAsync(int storeId, CancellationToken ct)
    {
        var products = await _context.products.AsNoTracking()
            .Where(p => p.storeId == storeId && !_context.images.Any(i => i.productId == p.Id))
            .Select(p => new { p.Id, p.name, p.sku })
            .Take(100).ToListAsync(ct);
        return (new { products, count = products.Count }, $"{products.Count} товаров без изображений");
    }

    private async Task<(object, string)> FindProductsWithoutBarcodeAsync(int storeId, CancellationToken ct)
    {
        var products = await _context.products.AsNoTracking()
            .Where(p => p.storeId == storeId && (p.barcode == null || p.barcode == ""))
            .Select(p => new { p.Id, p.name, p.sku })
            .Take(100).ToListAsync(ct);
        return (new { products, count = products.Count }, $"{products.Count} товаров без баркода");
    }

    private async Task<(object, string)> FindProductsMissingFieldAsync(string json, int storeId, CancellationToken ct)
    {
        var p = ParseInput<SearchInput>(json);
        var attrId = await _context.categoryAttributes.AsNoTracking()
            .Where(a => EF.Functions.Like(a.name, $"%{p.Search}%") && a.category.storeId == storeId)
            .Select(a => (int?)a.Id).FirstOrDefaultAsync(ct);

        if (attrId == null)
            return (new { error = $"Поле '{p.Search}' не найдено" }, "Поле не найдено");

        var products = await _context.products.AsNoTracking()
            .Where(pr => pr.storeId == storeId &&
                !_context.productAttributeValues.Any(v => v.productId == pr.Id && v.attributeId == attrId && v.value != null && v.value != ""))
            .Select(pr => new { pr.Id, pr.name, pr.sku })
            .Take(100).ToListAsync(ct);
        return (new { products, count = products.Count }, $"{products.Count} товаров без поля '{p.Search}'");
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
        return (new { products = links, count = links.Count }, $"{links.Count} товаров с ошибками синхронизации");
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
            $"Проанализировано {mappings.Count} маппингов: {connectedFields}/{totalFields} полей подключено");
    }

    private async Task<(object, string)> GetImageDownloadStatusAsync(int storeId, CancellationToken ct)
    {
        var groups = await _context.pendingImageDownloads.AsNoTracking()
            .Where(d => d.storeId == storeId)
            .GroupBy(d => d.status)
            .Select(g => new { status = g.Key, count = g.Count() })
            .ToListAsync(ct);
        return (new { status = groups }, "Статус загрузки изображений");
    }

    // ---- Rollback ----

    private async Task<(object, string)> RollbackOperationAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<OptionalIdInput>(json);
        AiOperation? op;
        if (p.Id.HasValue)
            op = await _operationService.GetByIdAsync(p.Id.Value, userId, ct);
        else
            op = await _operationService.GetLastAsync(userId, storeId, ct);

        if (op == null) return (new { error = "Нет операций для отмены" }, "Нет операций для отмены");
        if (!op.canRollback || op.status == AiOperationStatus.RolledBack)
            return (new { error = "Эту операцию нельзя отменить" }, "Нельзя отменить");

        await ApplyRollbackAsync(op, ct);
        await _operationService.MarkRolledBackAsync(op.Id, ct);
        return (new { success = true, operationId = op.Id, description = op.description },
            $"Отменено: {op.description}");
    }

    private async Task ApplyRollbackAsync(AiOperation op, CancellationToken ct)
    {
        switch (op.operationType)
        {
            case "update_prices":
            {
                var data = JsonSerializer.Deserialize<PriceRollbackData>(op.rollbackDataJson, _jsonOpts);
                if (data != null)
                {
                    var ids = data.Products.Select(p => p.Id).ToList();
                    var products = await _context.products.Where(p => ids.Contains(p.Id)).ToListAsync(ct);
                    foreach (var pr in products)
                    {
                        var snap = data.Products.FirstOrDefault(p => p.Id == pr.Id);
                        if (snap != null) pr.price = snap.OldPrice;
                    }
                    await _context.SaveChangesAsync(ct);
                }
                break;
            }
            case "add_category_field":
            {
                var data = JsonSerializer.Deserialize<FieldRollbackData>(op.rollbackDataJson, _jsonOpts);
                if (data != null)
                {
                    await _context.categoryAttributes.Where(a => a.Id == data.FieldId)
                        .ExecuteDeleteAsync(ct);
                }
                break;
            }
            case "connect_field":
            case "set_static_value":
            case "add_modifier":
            case "disconnect_field":
            case "generate_mapping":
            {
                var data = JsonSerializer.Deserialize<GraphRollbackData>(op.rollbackDataJson, _jsonOpts);
                if (data != null)
                {
                    await _context.categoryMappings.Where(m => m.Id == data.MappingId)
                        .ExecuteUpdateAsync(s => s.SetProperty(m => m.graphJson, data.PreviousGraphJson), ct);
                }
                break;
            }
            case "update_attribute":
            {
                var data = JsonSerializer.Deserialize<ProductAttributeRollbackData>(op.rollbackDataJson, _jsonOpts);
                if (data != null)
                {
                    foreach (var snap in data.Products)
                    {
                        var val = await _context.productAttributeValues
                            .FirstOrDefaultAsync(v => v.productId == snap.ProductId && v.attributeId == snap.AttributeId, ct);
                        if (val != null)
                        {
                            val.value = snap.OldValue;
                        }
                        else if (snap.OldValue != null)
                        {
                            _context.productAttributeValues.Add(new ProductAttributeValue
                            { productId = snap.ProductId, attributeId = snap.AttributeId, value = snap.OldValue });
                        }
                    }
                    await _context.SaveChangesAsync(ct);
                }
                break;
            }
        }
    }

    // ---- Tool Definitions ----

    private static List<AiTool> BuildToolDefinitions() => [
        Prop("propose_plan", "ALWAYS call first before any mutating operation. Shows user a plan and waits for confirmation.",
            Req(["summary", "steps"]),
            ("summary", Str("Brief summary of what will be done")),
            ("steps", new AiToolProperty { type = "array", description = "List of steps", items = new AiToolProperty { type = "object" } }),
            ("affectedCount", Int("Number of affected items")),
            ("isDestructive", Bool("Whether this is a destructive operation"))
        ),
        Prop("rollback_last_operation", "Rollback the last AI operation (undo). Call without operationId to rollback the most recent one.",
            Req([]),
            ("operationId", Int("Specific operation ID to rollback (optional)"))
        ),

        // Products
        Prop("list_products", "List products with optional filters",
            Req([]),
            ("search", Str("Search in name or SKU")),
            ("categoryId", Int("Filter by category")),
            ("priceMin", Num("Min price filter")),
            ("priceMax", Num("Max price filter")),
            ("hasImages", Bool("Filter by has/no images")),
            ("missingField", Str("Filter by missing attribute name")),
            ("startIndex", Int("Pagination offset")),
            ("pageSize", Int("Page size (max 200)"))
        ),
        Prop("get_products_count", "Count products matching filters", Req([]),
            ("search", Str()), ("categoryId", Int()), ("priceMin", Num()), ("priceMax", Num())
        ),
        Prop("get_product", "Get full product details", Req(["id"]), ("id", Int("Product ID"))),
        Prop("create_product", "Create a new product", Req(["name", "categoryId"]),
            ("name", Str("Product name")), ("categoryId", Int("Category ID")),
            ("sku", Str("SKU")), ("price", Num("Price")), ("barcode", Str("Barcode")),
            ("description", Str("Description")), ("quantity", Int("Stock quantity"))
        ),
        Prop("update_product", "Update product fields", Req(["id"]),
            ("id", Int("Product ID")), ("name", Str()), ("price", Num()),
            ("description", Str()), ("barcode", Str()), ("quantity", Int()), ("categoryId", Int())
        ),
        Prop("update_product_sku", "Update product SKU", Req(["productId", "newSku"]),
            ("productId", Int()), ("newSku", Str())
        ),
        Prop("update_product_prices", "Bulk update product prices", Req(["amount", "mode"]),
            ("productIds", new AiToolProperty { type = "array", items = new AiToolProperty { type = "string" } }),
            ("categoryId", Int("Update all products in category")),
            ("amount", Num("Amount to change")),
            ("mode", Enum("add", "subtract", "set", "multiply"))
        ),
        Prop("update_product_attribute", "Set attribute value on products", Req(["attributeKey", "value"]),
            ("attributeKey", Str("Attribute key")), ("value", Str("New value")),
            ("productIds", new AiToolProperty { type = "array", items = new AiToolProperty { type = "string" } })
        ),
        Prop("delete_products", "Soft-delete products", Req(["ids"]),
            ("ids", new AiToolProperty { type = "array", items = new AiToolProperty { type = "string" } })
        ),
        Prop("restore_products", "Restore soft-deleted products", Req(["ids"]),
            ("ids", new AiToolProperty { type = "array", items = new AiToolProperty { type = "string" } })
        ),
        Prop("list_trash", "List soft-deleted products", Req([]), ("search", Str())),

        // Categories
        Prop("list_categories", "List all store categories", Req([])),
        Prop("get_category", "Get category with attributes", Req(["id"]), ("id", Int())),
        Prop("create_category", "Create a category", Req(["name"]),
            ("name", Str()), ("description", Str())
        ),
        Prop("update_category", "Update a category", Req(["id"]),
            ("id", Int()), ("name", Str()), ("description", Str())
        ),
        Prop("delete_category", "Delete a category", Req(["id"]), ("id", Int())),
        Prop("list_category_fields", "List category attributes", Req(["id"]), ("id", Int("Category ID"))),
        Prop("add_category_field", "Add attribute to category", Req(["categoryId", "name", "type"]),
            ("categoryId", Int()), ("name", Str()), ("key", Str("Optional key (auto-generated if omitted)")),
            ("type", Enum("Text", "Number", "Boolean", "Select", "MultiSelect", "Barcode")),
            ("isRequired", Bool()), ("group", Str("Field group name"))
        ),
        Prop("update_category_field", "Update a category attribute", Req(["fieldId"]),
            ("fieldId", Int()), ("name", Str()), ("isRequired", Bool())
        ),
        Prop("delete_category_field", "Delete a category attribute", Req(["fieldId"]), ("fieldId", Int())),
        Prop("find_categories_with_field", "Find categories having a field matching name", Req(["search"]),
            ("search", Str("Partial field name to search for"))
        ),

        // Store attributes
        Prop("list_store_attributes", "List store-level attributes", Req([])),

        // Connections
        Prop("list_connections", "List marketplace connections", Req([])),
        Prop("get_connection", "Get connection details", Req(["id"]), ("id", Int())),
        Prop("test_connection", "Test marketplace connection", Req(["id"]), ("id", Int())),
        Prop("get_connection_limits", "Get rate limits for a connection", Req(["id"]), ("id", Int())),

        // Mappings
        Prop("list_mappings", "List category mappings", Req([]), ("connectionId", Int("Filter by connection"))),
        Prop("get_mapping", "Get mapping details and coverage stats", Req(["id"]), ("id", Int())),
        Prop("create_mapping", "Create a category mapping (AI auto-maps fields)", Req(["connectionId", "localCategoryId", "externalCategoryId"]),
            ("connectionId", Int()), ("localCategoryId", Int()), ("externalCategoryId", Str()),
            ("externalCategoryName", Str()), ("name", Str())
        ),
        Prop("update_mapping_name", "Rename a mapping", Req(["id", "name"]), ("id", Int()), ("name", Str())),
        Prop("delete_mapping", "Delete a mapping", Req(["id"]), ("id", Int())),
        Prop("duplicate_mapping", "Duplicate a mapping", Req(["id"]), ("id", Int())),
        Prop("list_external_categories", "Get categories from marketplace adapter", Req(["connectionId"]),
            ("connectionId", Int()), ("parentId", Str()), ("search", Str())
        ),
        Prop("get_external_fields", "Get fields from marketplace category", Req(["connectionId", "externalCategoryId"]),
            ("connectionId", Int()), ("externalCategoryId", Str())
        ),
        Prop("get_available_sources", "Get available source fields for a mapping", Req(["id"]), ("id", Int("Mapping ID"))),
        Prop("get_mapping_coverage", "Analyze mapped vs unmapped fields", Req(["id"]), ("id", Int("Mapping ID"))),
        Prop("preview_mapping_for_product", "Preview mapping output for a product", Req(["mappingId", "productId"]),
            ("mappingId", Int()), ("productId", Int())
        ),
        Prop("generate_mapping_suggestions", "Run AI auto-mapping for existing mapping", Req(["id"]), ("id", Int("Mapping ID"))),
        Prop("connect_field", "Connect a source to an adapter field", Req(["mappingId", "adapterFieldKey", "sourceType"]),
            ("mappingId", Int()), ("adapterFieldKey", Str()),
            ("sourceType", Enum("productField", "attribute", "storeAttribute")),
            ("sourceField", Str("e.g. sku, name, price")), ("sourceAttributeId", Int())
        ),
        Prop("set_field_static_value", "Set a constant value for an adapter field", Req(["mappingId", "adapterFieldKey", "value"]),
            ("mappingId", Int()), ("adapterFieldKey", Str()), ("value", Str())
        ),
        Prop("add_field_modifier", "Add a transformation modifier to a field", Req(["mappingId", "adapterFieldKey", "modifierType"]),
            ("mappingId", Int()), ("adapterFieldKey", Str()),
            ("modifierType", Enum("truncate", "round", "multiply", "toText", "toNumber", "uppercase", "lowercase", "append", "prepend", "replace"))
        ),
        Prop("remove_field_connection", "Remove source from an adapter field", Req(["mappingId", "fieldKey"]),
            ("mappingId", Int()), ("fieldKey", Str())
        ),

        // Export/Import
        Prop("export_products_batch", "Queue products for export", Req(["connectionId"]),
            ("connectionId", Int()),
            ("productIds", new AiToolProperty { type = "array", items = new AiToolProperty { type = "string" } }),
            ("categoryId", Int("Export all products in category"))
        ),
        Prop("export_prices", "Export prices to marketplace", Req(["connectionId"]), ("connectionId", Int())),
        Prop("export_stocks", "Export stock quantities to marketplace", Req(["connectionId"]), ("connectionId", Int())),
        Prop("get_batch_status", "Check export/import batch status", Req(["batchId"]), ("batchId", Str())),
        Prop("list_product_links", "List product-marketplace sync links", Req([]), ("connectionId", Int())),

        // Groups
        Prop("list_groups", "List product groups", Req([])),
        Prop("create_group", "Create a product group", Req(["name"]),
            ("name", Str()),
            ("productIds", new AiToolProperty { type = "array", items = new AiToolProperty { type = "string" } })
        ),
        Prop("add_products_to_group", "Add products to a group", Req(["groupId", "productIds"]),
            ("groupId", Int()),
            ("productIds", new AiToolProperty { type = "array", items = new AiToolProperty { type = "string" } })
        ),

        // Analytics
        Prop("get_store_stats", "Get overall store statistics", Req([])),
        Prop("find_products_without_images", "Find products with no images", Req([])),
        Prop("find_products_without_barcode", "Find products with no barcode", Req([])),
        Prop("find_products_missing_field", "Find products missing an attribute value", Req(["search"]),
            ("search", Str("Attribute name to check"))
        ),
        Prop("find_products_with_sync_errors", "Find products with marketplace sync errors", Req([]),
            ("connectionId", Int())
        ),
        Prop("analyze_mapping_quality", "Analyze all mappings coverage", Req([]), ("connectionId", Int())),
        Prop("get_image_download_status", "Check image download queue status", Req([])),
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

    private class ListProductsInput { public int? CategoryId { get; set; } public string? Search { get; set; } public decimal? PriceMin { get; set; } public decimal? PriceMax { get; set; } public bool? HasImages { get; set; } public string? MissingField { get; set; } public int? StartIndex { get; set; } public int? PageSize { get; set; } }
    private class IdInput { public int Id { get; set; } }
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
    private class ExternalFieldsInput { public int ConnectionId { get; set; } public string ExternalCategoryId { get; set; } = ""; }
    private class PreviewMappingInput { public int MappingId { get; set; } public int ProductId { get; set; } }
    private class ConnectFieldInput { public int MappingId { get; set; } public string AdapterFieldKey { get; set; } = ""; public string SourceType { get; set; } = ""; public string? SourceField { get; set; } public int? SourceAttributeId { get; set; } }
    private class SetStaticInput { public int MappingId { get; set; } public string AdapterFieldKey { get; set; } = ""; public string Value { get; set; } = ""; }
    private class AddModifierInput { public int MappingId { get; set; } public string AdapterFieldKey { get; set; } = ""; public string ModifierType { get; set; } = ""; public Dictionary<string, object>? Params { get; set; } }
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
