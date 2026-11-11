using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Enums;
using Delegat.Infrastructure;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services;
using Delegat.Services.Marketplace;
using Delegat.Services.Marketplace.Graph;
using Delegat.Services.Ai;
using Delegat.Services.Plugins;
using IMarketplaceMetadataCache = Delegat.Marketplace.SDK.IMarketplaceMetadataCache;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
public class CategoryMappingController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly DataContext _context;
    private readonly IServiceProvider _serviceProvider;
    private readonly IMarketplaceAdapterRegistry _adapterRegistry;
    private readonly CategoryImportQueueService _categoryImportQueue;
    private readonly PluginVersionSyncService _pluginVersionSyncService;
    private readonly IMarketplaceMetadataCache _metadataCache;
    private readonly ILogger<CategoryMappingController> _logger;

    private readonly IOperationLogger _operationLogger;
    private readonly GraphMutationService _graphMutation;
    private readonly ProductMappingSelectionService _mappingSelection;

    public CategoryMappingController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        DataContext context,
        IServiceProvider serviceProvider,
        IMarketplaceAdapterRegistry adapterRegistry,
        CategoryImportQueueService categoryImportQueue,
        PluginVersionSyncService pluginVersionSyncService,
        IMarketplaceMetadataCache metadataCache,
        ILogger<CategoryMappingController> logger,
        IOperationLogger operationLogger,
        GraphMutationService graphMutation,
        ProductMappingSelectionService mappingSelection)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _context = context;
        _serviceProvider = serviceProvider;
        _adapterRegistry = adapterRegistry;
        _categoryImportQueue = categoryImportQueue;
        _pluginVersionSyncService = pluginVersionSyncService;
        _metadataCache = metadataCache;
        _logger = logger;
        _operationLogger = operationLogger;
        _graphMutation = graphMutation;
        _mappingSelection = mappingSelection;
    }

    /// <summary>
    /// Get categories from marketplace
    /// </summary>
    [HttpGet]
    [Route("/marketplace/connections/{connectionId}/external-categories")]
    public async Task<IActionResult> GetExternalCategories(
        int connectionId,
        string? parentId = null,
        string? search = null,
        CancellationToken ct = default)
    {
        var connection = await GetConnectionAsync(connectionId);

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest(code: "adapter_not_found");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.GetCategories))
            throw ApiException.BadRequest(code: "adapter_capability_missing");

        try
        {
            var categories = await adapter.GetCategoriesAsync(connection, parentId, search, ct);
            return Ok(categories.Select(c => new
            {
                id = c.Id,
                name = c.Name,
                parentId = c.ParentId,
                hasChildren = c.HasChildren,
                fullPath = c.FullPath
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting categories from marketplace for connection {ConnectionId}", connectionId);
            throw ApiException.BadRequest(code: "external_categories_unavailable");
        }
    }

    /// <summary>
    /// Get list of category mappings for a connection
    /// </summary>
    [HttpGet]
    [Route("/marketplace/connections/{connectionId}/mappings")]
    public async Task<IActionResult> GetMappings(int connectionId)
    {
        var connection = await GetConnectionAsync(connectionId);

        var mappings = await _context.categoryMappings.AsNoTracking()
            .Where(m => m.connectionId == connectionId)
            .Select(m => new
            {
                id = m.Id,
                name = m.name,
                localCategoryId = m.localCategoryId,
                localCategoryName = m.localCategory.name,
                externalCategoryId = m.externalCategoryId,
                externalCategoryName = m.externalCategoryName,
                graphJson = m.graphJson,
            })
            .ToListAsync();

        var categoryIds = mappings.Select(m => m.localCategoryId).Distinct().ToList();
        var mappingIds = mappings.Select(m => m.id).ToList();

        // Products in the mapped local category (what the list column "Products" means).
        var productsByCategory = categoryIds.Count == 0
            ? new Dictionary<int, int>()
            : await _context.products.AsNoTracking()
                .Where(p =>
                    p.storeId == connection.storeId &&
                    p.deletedAt == null &&
                    p.categoryId != null &&
                    categoryIds.Contains(p.categoryId.Value))
                .GroupBy(p => p.categoryId!.Value)
                .Select(g => new { CategoryId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.CategoryId, x => x.Count);

        // Synced marketplace links still using this mapping (delete / reassign impact).
        var linkedByMapping = mappingIds.Count == 0
            ? new Dictionary<int, int>()
            : await _context.productMarketplaceLinks.AsNoTracking()
                .Where(l =>
                    l.categoryMappingId != null &&
                    mappingIds.Contains(l.categoryMappingId.Value) &&
                    l.product.deletedAt == null)
                .GroupBy(l => l.categoryMappingId!.Value)
                .Select(g => new { MappingId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.MappingId, x => x.Count);

        // Field count comes from the graph's adapter schema (parsed in memory, not in SQL).
        var shaped = mappings.Select(m => new
        {
            m.id,
            m.name,
            m.localCategoryId,
            m.localCategoryName,
            m.externalCategoryId,
            m.externalCategoryName,
            fieldMappingsCount = MappingService.SchemaFields(m.graphJson).Count,
            productsCount = productsByCategory.GetValueOrDefault(m.localCategoryId, 0),
            linkedProductsCount = linkedByMapping.GetValueOrDefault(m.id, 0),
        });

        return Ok(shaped);
    }

    /// <summary>
    /// Create a category mapping
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/mappings")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> CreateMapping(
        int connectionId,
        [FromBody] CreateMappingInput input,
        CancellationToken ct = default)
    {
        var connection = await GetConnectionAsync(connectionId);

        // Verify that the local category exists
        var categoryRepo = _unitOfWork.Repository<Category>();
        var category = await categoryRepo.FindAsync(
            c => c.Id == input.localCategoryId && c.storeId == connection.storeId);

        if (category == null)
            throw ApiException.NotFound(code: "category_not_found");

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest(code: "adapter_not_found");

        var eligibility = await ExternalCategoryEligibility.EnsureMappableAsync(
            adapter, connection, input.externalCategoryId, ct);
        if (!eligibility.IsSuccess)
            throw ApiException.BadRequest(code: "category_not_mappable");

        var mappingRepo = _unitOfWork.Repository<CategoryMapping>();

        var mapping = new CategoryMapping();
        mapping.AddFill(new CategoryMapping.AddForm
        {
            connectionId = connectionId,
            localCategoryId = input.localCategoryId,
            externalCategoryId = input.externalCategoryId,
            externalCategoryName = input.externalCategoryName ?? eligibility.Category?.Name,
            name = input.name
        });

        await mappingRepo.AddAsync(mapping);

        var fieldsCreated = 0;
        if (adapter.Capabilities.HasFlag(MarketplaceCapabilities.GetCategoryFields))
        {
            fieldsCreated = await CreateFieldMappingsAsync(
                mapping, connection, input.localCategoryId, eligibility.Fields, adapter,
                skipAutoMapping: input.useAiMapping == true, ct: ct);
        }

        return Ok(new { id = mapping.Id, fieldsCreated });
    }

    /// <summary>
    /// Create an external marketplace category from a local category and map it immediately.
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/create-external-category")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> CreateExternalCategory(
        int connectionId,
        [FromBody] CreateExternalCategoryInput input,
        CancellationToken ct = default)
    {
        var connection = await GetConnectionAsync(connectionId);

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest($"Unknown adapter: {connection.adapterKey}");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.CreateCategory))
            throw ApiException.BadRequest(code: "adapter_capability_missing");

        var category = await _unitOfWork.Repository<Category>().FindAsync(
            c => c.Id == input.localCategoryId && c.storeId == connection.storeId,
            query => query.Include(c => c.attributes));

        if (category == null)
            throw ApiException.NotFound(code: "category_not_found");

        var attributes = category.attributes.AsEnumerable();
        if (input.selectedAttributeKeys != null)
        {
            var keys = new HashSet<string>(
                input.selectedAttributeKeys
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .Select(k => k.Trim()),
                StringComparer.OrdinalIgnoreCase);
            attributes = attributes.Where(a => keys.Contains(a.key ?? ""));
        }

        var request = new Models.Marketplace.DTOs.CreateCategoryRequest
        {
            Name = string.IsNullOrWhiteSpace(input.externalCategoryName) ? category.name : input.externalCategoryName.Trim(),
            Description = string.IsNullOrWhiteSpace(input.description) ? category.description : input.description.Trim(),
            ParentId = input.parentExternalCategoryId,
            StoreId = input.storeId,
            LanguageId = input.languageId,
            Fields = attributes
                .OrderBy(a => a.sortOrder)
                .Select(a => new Models.Marketplace.DTOs.CreateCategoryField
                {
                    Key = a.key,
                    Name = a.name,
                    Type = MapAttributeType(a.type),
                    IsRequired = a.isRequired,
                    Description = a.description,
                    Group = a.group,
                    SortOrder = a.sortOrder,
                     Options = ParseAttributeOptions(a.options),
                     Unit = a.unitCode == null ? null : AttributeUnits.Find(a.unitCode)?.Symbol ?? a.unitCode,
                     UnitCode = a.unitCode
                })
                .ToList()
        };

        var result = await adapter.CreateCategoryAsync(connection, request, ct);
        if (!result.Success || string.IsNullOrWhiteSpace(result.ExternalCategoryId))
        {
            var errorMessage = !string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? result.ErrorMessage
                : result.Success
                    ? "Adapter reported successful category creation but did not return an external category ID"
                    : "Failed to create category on the marketplace";

            throw ApiException.BadRequest(
                errorMessage,
                result.Details);
        }

        var mapping = new CategoryMapping();
        mapping.AddFill(new CategoryMapping.AddForm
        {
            connectionId = connectionId,
            localCategoryId = category.Id,
            externalCategoryId = result.ExternalCategoryId,
            externalCategoryName = result.ExternalCategoryName ?? request.Name,
            name = input.mappingName
        });

        await _unitOfWork.Repository<CategoryMapping>().AddAsync(mapping);

        var fields = result.Fields;
        if (fields == null || fields.Count == 0)
            fields = await adapter.GetCategoryFieldsAsync(connection, result.ExternalCategoryId, ct);

        var fieldsCreated = await CreateFieldMappingsAsync(
            mapping, connection, category.Id, fields, adapter,
            skipAutoMapping: input.skipAutoMapping == true, ct: ct);

        return Ok(new
        {
            id = mapping.Id,
            externalCategoryId = result.ExternalCategoryId,
            externalCategoryName = result.ExternalCategoryName ?? request.Name,
            fieldsCreated
        });
    }

    /// <summary>
    /// Update a category mapping (rename)
    /// </summary>
    [HttpPatch]
    [Route("/marketplace/connections/{connectionId}/mappings/{mappingId}")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> UpdateMapping(
        int connectionId,
        int mappingId,
        [FromBody] UpdateMappingInput input)
    {
        await GetConnectionAsync(connectionId);

        var mappingRepo = _unitOfWork.Repository<CategoryMapping>();
        var mapping = await mappingRepo.FindAsync(
            m => m.Id == mappingId && m.connectionId == connectionId);

        if (mapping == null)
            throw ApiException.NotFound(code: "mapping_not_found");

        mapping.UpdateFill(new CategoryMapping.UpdateForm
        {
            name = input.name
        });

        await mappingRepo.UpdateAsync(mapping);

        return Ok(new { success = true });
    }

    /// <summary>
    /// Fetch the current adapter field schema and append any new fields to the mapping graph.
    /// Existing bindings and user configuration are preserved (additive sync only).
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/mappings/{mappingId}/sync-fields")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> SyncMappingFields(
        int connectionId,
        int mappingId,
        CancellationToken ct)
    {
        await GetConnectionAsync(connectionId);

        var exists = await _context.categoryMappings.AnyAsync(
            m => m.Id == mappingId && m.connectionId == connectionId, ct);
        if (!exists)
            throw ApiException.NotFound(code: "mapping_not_found");

        try
        {
            var result = await _pluginVersionSyncService.SyncMappingFieldsAsync(
                connectionId, mappingId, ct);

            var message = result.TotalAdded > 0
                ? $"Fields added: {result.CardFieldsAdded} card"
                  + (result.PriceFieldsAdded > 0 ? $", {result.PriceFieldsAdded} price" : "")
                : "Schema is up to date - no new fields";

            return Ok(new
            {
                success = true,
                cardFieldsAdded = result.CardFieldsAdded,
                priceFieldsAdded = result.PriceFieldsAdded,
                totalAdded = result.TotalAdded,
                totalFromApi = result.TotalFromApi,
                existingCount = result.ExistingCount,
                addedFieldKeys = result.AddedFieldKeys,
                addedPriceFieldKeys = result.AddedPriceFieldKeys,
                message,
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex, "Rejected invalid mapping operation for {MappingId}", mappingId);
            throw ApiException.BadRequest(code: "mapping_graph_invalid");
        }
    }

    /// <summary>
    /// Get the visual node graph for a mapping. Returns the saved graph, or null when none
    /// exists yet (the client then auto-migrates the current field mappings into a graph).
    /// </summary>
    [HttpGet]
    [Route("/marketplace/connections/{connectionId}/mappings/{mappingId}/graph")]
    public async Task<IActionResult> GetMappingGraph(int connectionId, int mappingId, CancellationToken ct = default)
    {
        var connection = await GetConnectionAsync(connectionId);

        var mapping = await _unitOfWork.Repository<CategoryMapping>()
            .FindAsync(m => m.Id == mappingId && m.connectionId == connectionId);

        if (mapping == null)
            throw ApiException.NotFound(code: "mapping_not_found");

        object? graph = null;
        var graphJson = mapping.graphJson;
        if (!string.IsNullOrWhiteSpace(graphJson))
        {
            var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
            if (adapter != null)
            {
                try
                {
                    if (adapter.Capabilities.HasFlag(MarketplaceCapabilities.GetCategoryFields))
                    {
                        var cardFields = await adapter.GetCategoryFieldsAsync(connection, mapping.externalCategoryId, ct);
                        if (LocalCategoryAdapterFields.ShouldExposeLocalAttributes(adapter.Key))
                        {
                            var attrs = await _unitOfWork.Repository<CategoryAttribute>()
                                .FindAllAsync(a => a.categoryId == mapping.localCategoryId);
                            cardFields = LocalCategoryAdapterFields.Merge(cardFields, attrs);
                        }
                        var cardSpecs = cardFields.OrderBy(f => f.SortOrder).Select((field, index) =>
                        {
                            var (sourceType, sourceField, sourceAttributeId) =
                                CategoryImportService.DetermineAutoMapping(field, new Dictionary<string, int>());
                            return new FieldSpec
                            {
                                externalFieldKey = field.Key,
                                externalFieldName = field.Name,
                                externalFieldType = field.Type,
                                externalFieldUnit = field.Unit,
                                externalFieldUnitCode = AttributeUnits.NormalizeCode(field.UnitCode ?? field.Unit),
                                externalFieldUnitId = field.UnitId,
                                externalFieldGroup = field.Group,
                                externalFieldDescription = field.Description,
                                isRequired = sourceType != FieldMappingSourceType.ProductField && field.IsRequired,
                                isEnabled = true,
                                sortOrder = index,
                                sourceType = sourceType,
                                sourceField = sourceField,
                                sourceAttributeId = sourceAttributeId,
                            };
                        }).ToList();
                        graphJson = GraphGenerator.SyncAdapterFields(graphJson, cardSpecs);
                    }

                    if (adapter.Capabilities.HasFlag(MarketplaceCapabilities.ExportPrices))
                    {
                        var priceFields = await adapter.GetPriceFieldsAsync(connection, mapping.externalCategoryId, ct);
                        var extendedPrice = await _unitOfWork.Repository<Store>().Query()
                            .Where(s => s.Id == connection.storeId)
                            .Select(s => s.extendedPriceParameters)
                            .FirstOrDefaultAsync(ct);
                        var priceSpecs = CategoryImportService.BuildPriceFieldSpecs(
                            priceFields,
                            new Dictionary<string, int>(),
                            skipAutoMapping: false,
                            extendedPriceParameters: extendedPrice);
                        graphJson = GraphGenerator.SyncPriceFields(graphJson, priceSpecs);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to sync fields in graph for mapping {MappingId}", mappingId);
                }
            }

            try { graph = JsonSerializer.Deserialize<JsonElement>(graphJson); }
            catch (JsonException) { graph = null; }
        }

        return Ok(new
        {
            hasGraph = graph != null,
            graph,
            blocks = MappingGraphBlockCatalog.Describe(),
        });
    }

    /// <summary>Node types, pin ids, pin types, and layout sizes for the mapping canvas.</summary>
    [HttpGet]
    [Route("/marketplace/mapping-graph/blocks")]
    public IActionResult GetMappingGraphBlocks()
        => Ok(MappingGraphBlockCatalog.Describe());

    /// <summary>
    /// Save (or clear) the visual node graph for a mapping. A null/empty graph reverts the
    /// mapping to the legacy per-field export. Existing per-field modifiersJson is left intact,
    /// so reverse (import) of unchanged linear fields keeps working.
    /// </summary>
    [HttpPut]
    [HttpPatch]
    [Route("/marketplace/connections/{connectionId}/mappings/{mappingId}/graph")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> SaveMappingGraph(
        int connectionId,
        int mappingId,
        [FromBody] SaveGraphInput input)
    {
        await GetConnectionAsync(connectionId);

        var repo = _unitOfWork.Repository<CategoryMapping>();
        var mapping = await repo.FindAsync(m => m.Id == mappingId && m.connectionId == connectionId);

        if (mapping == null)
            throw ApiException.NotFound(code: "mapping_not_found");

        mapping.graphJson = input.graph.HasValue
            && input.graph.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                ? input.graph.Value.GetRawText()
                : null;

        await repo.UpdateAsync(mapping);

        // The graph is the single source of truth - no per-field projection needed (input.fields ignored).
        return Ok(new { success = true, hasGraph = mapping.graphJson != null });
    }

    /// <summary>
    /// Wire one adapter sink (e.g. Medusa collection title) without replacing the whole graph.
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/mappings/{mappingId}/graph/connect")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> ConnectMappingGraphField(
        int connectionId,
        int mappingId,
        [FromBody] GraphConnectInput input,
        CancellationToken ct)
    {
        await EnsureMappingOnConnectionAsync(connectionId, mappingId);
        if (string.IsNullOrWhiteSpace(input.adapterFieldKey) || string.IsNullOrWhiteSpace(input.sourceType))
            throw ApiException.BadRequest(code: "mapping_source_required");

        try
        {
            if (string.Equals(input.sourceType, "static", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(input.staticValue))
            {
                await _graphMutation.SetStaticValueAsync(
                    mappingId,
                    input.adapterFieldKey.Trim(),
                    input.staticValue.Trim(),
                    input.sink,
                    StubField(input.adapterFieldKey),
                    ct);
            }
            else
            {
                await _graphMutation.ConnectFieldAsync(
                    mappingId,
                    input.adapterFieldKey.Trim(),
                    input.sourceType.Trim(),
                    input.sourceField,
                    input.sourceAttributeId,
                    input.sink,
                    StubField(input.adapterFieldKey),
                    ct);
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex, "Rejected invalid graph connection for {MappingId}", mappingId);
            throw ApiException.BadRequest(code: "mapping_graph_invalid");
        }

        return Ok(new { success = true });
    }

    /// <summary>
    /// Insert a canvas modifier (valueMap, replace, …) on a sink field.
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/mappings/{mappingId}/graph/modifiers")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> AddMappingGraphModifier(
        int connectionId,
        int mappingId,
        [FromBody] GraphModifierInput input,
        CancellationToken ct)
    {
        await EnsureMappingOnConnectionAsync(connectionId, mappingId);
        if (string.IsNullOrWhiteSpace(input.adapterFieldKey) || string.IsNullOrWhiteSpace(input.modifierType))
            throw ApiException.BadRequest(code: "mapping_modifier_required");

        try
        {
            await _graphMutation.AddModifierAsync(
                mappingId,
                input.adapterFieldKey.Trim(),
                input.modifierType.Trim(),
                GraphModifierParams.Merge(
                    input.modifierType,
                    ToModifierParams(input.@params),
                    input.pattern,
                    input.replacement,
                    input.ignoreCase,
                    input.group),
                input.sink,
                StubField(input.adapterFieldKey),
                input.removeModifierTypes,
                input.paramSources?
                    .Where(s => !string.IsNullOrWhiteSpace(s.param) && !string.IsNullOrWhiteSpace(s.sourceType))
                    .Select(s => new GraphMutationService.ModifierParamSource(
                        s.param.Trim(),
                        s.sourceType.Trim(),
                        s.sourceField,
                        s.sourceAttributeId,
                        s.staticValue))
                    .ToList(),
                ct);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex, "Rejected invalid graph modifier for {MappingId}", mappingId);
            throw ApiException.BadRequest(code: "mapping_graph_invalid");
        }

        return Ok(new { success = true });
    }

    /// <summary>
    /// String switch on Merge into card (<c>__group__</c>): PIM group name → shop collection title.
    /// Does not rename local groups. Not valueId / category-attribute optionMap.
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/mappings/{mappingId}/graph/group-titles")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> RemapMappingGroupTitles(
        int connectionId,
        int mappingId,
        [FromBody] GraphGroupTitlesInput input,
        CancellationToken ct)
    {
        await EnsureMappingOnConnectionAsync(connectionId, mappingId);
        var maps = (input.mappings ?? [])
            .Where(m => !string.IsNullOrWhiteSpace(m.from) && !string.IsNullOrWhiteSpace(m.to))
            .Select(m => new GroupTitlePair { From = m.from.Trim(), To = m.to.Trim() })
            .ToList();
        if (maps.Count == 0)
            throw ApiException.BadRequest(code: "mapping_pairs_required");

        try
        {
            await _graphMutation.RemapGroupTitlesAsync(mappingId, maps, ct);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex, "Rejected invalid group title mapping for {MappingId}", mappingId);
            throw ApiException.BadRequest(code: "mapping_graph_invalid");
        }

        return Ok(new
        {
            success = true,
            adapterFieldKey = GraphMutationService.GroupTitleFieldKey,
            sourceType = "groupName",
            modifierType = "valueMap",
        });
    }

    private async Task EnsureMappingOnConnectionAsync(int connectionId, int mappingId)
    {
        await GetConnectionAsync(connectionId);
        var mapping = await _unitOfWork.Repository<CategoryMapping>()
            .FindAsync(m => m.Id == mappingId && m.connectionId == connectionId);
        if (mapping == null)
            throw ApiException.NotFound(code: "mapping_not_found");
    }

    private static ExternalField StubField(string key)
    {
        var k = key.Trim();
        return new ExternalField(k, k, false, "string");
    }

    private static Dictionary<string, object>? ToModifierParams(Dictionary<string, JsonElement>? raw)
    {
        if (raw == null || raw.Count == 0)
            return null;
        var map = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var kv in raw)
            map[kv.Key] = kv.Value.Clone();
        return map;
    }

    /// <summary>
    /// Preview the result of a node graph for a specific product. Uses the graph in the request
    /// body when provided (unsaved edits from the editor), otherwise the saved graph.
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/mappings/{mappingId}/graph/preview")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> PreviewMappingGraph(
        int connectionId,
        int mappingId,
        [FromBody] GraphPreviewInput input,
        CancellationToken ct = default)
    {
        await GetConnectionAsync(connectionId);

        var mapping = await _unitOfWork.Repository<CategoryMapping>()
            .FindAsync(m => m.Id == mappingId && m.connectionId == connectionId);

        if (mapping == null)
            throw ApiException.NotFound(code: "mapping_not_found");

        var graphJson = input.graph.HasValue
            && input.graph.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                ? input.graph.Value.GetRawText()
                : mapping.graphJson;

        if (string.IsNullOrWhiteSpace(graphJson))
            return Ok(new
            {
                outputs = new Dictionary<string, string?>(),
                fieldTypes = new Dictionary<string, string>(),
                nodeValues = new Dictionary<string, string?>(),
            });

        // Load product with all relations needed for source resolution (matches the field-mapping preview).
        var product = await _context.products
            .Include(p => p.category)
            .Include(p => p.attributeValues)
                .ThenInclude(av => av.attribute)
            .Include(p => p.storeAttributeValues)
                .ThenInclude(sav => sav.storeAttribute)
            .Include(p => p.files)
            .Include(p => p.images)
                .ThenInclude(i => i.file)
            .Include(p => p.groupMemberships)
                .ThenInclude(gm => gm.group)
            .FirstOrDefaultAsync(p => p.Id == input.productId, ct);

        if (product == null)
            throw ApiException.NotFound(code: "product_not_found");

        var mappingService = _serviceProvider.GetRequiredService<MappingService>();
        var eval = await mappingService.EvaluateGraphAsync(product, graphJson, mappingId, ct: ct);

        var outputs = eval.Outputs.ToDictionary(kv => kv.Key, kv => FormatGraphValue(kv.Value));
        var nodeValues = eval.NodeValues.ToDictionary(kv => kv.Key, kv => FormatGraphValue(kv.Value));
        var generativeStatuses = eval.GenerativeStatuses.ToDictionary(
            kv => kv.Key,
            kv => new
            {
                status = kv.Value.Status,
                output = kv.Value.Output,
                operationId = kv.Value.OperationId,
                errorMessage = kv.Value.ErrorMessage,
            });

        return Ok(new { outputs, fieldTypes = eval.FieldTypes, nodeValues, generativeStatuses });
    }

    /// <summary>
    /// Duplicate a category mapping with all its field mappings
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/mappings/{mappingId}/duplicate")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> DuplicateMapping(
        int connectionId,
        int mappingId,
        CancellationToken ct = default)
    {
        await GetConnectionAsync(connectionId);

        var mappingRepo = _unitOfWork.Repository<CategoryMapping>();
        var original = await mappingRepo.FindAsync(
            m => m.Id == mappingId && m.connectionId == connectionId,
            query => query);

        if (original == null)
            throw ApiException.NotFound(code: "mapping_not_found");

        // Create the duplicate mapping
        var duplicate = new CategoryMapping();
        duplicate.AddFill(new CategoryMapping.AddForm
        {
            connectionId = original.connectionId,
            localCategoryId = original.localCategoryId,
            externalCategoryId = original.externalCategoryId,
            externalCategoryName = original.externalCategoryName,
            name = string.IsNullOrWhiteSpace(original.name)
                ? "Copy"
                : $"{original.name} (copy)"
        });

        await mappingRepo.AddAsync(duplicate);

        // The graph (single source of truth) carries all field config - clone it directly. Keys/attributeIds
        // are identical for the duplicate since it shares the same local category.
        duplicate.graphJson = original.graphJson;
        await mappingRepo.UpdateAsync(duplicate);
        var fieldsCreated = MappingService.SchemaFields(original.graphJson).Count;

        return Ok(new { id = duplicate.Id, fieldsCreated });
    }

    /// <summary>
    /// Delete a category mapping
    /// </summary>
    [HttpDelete]
    [Route("/marketplace/connections/{connectionId}/mappings/{mappingId}")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> DeleteMapping(
        int connectionId,
        int mappingId,
        [FromQuery] int? reassignToMappingId = null,
        [FromQuery] bool force = false,
        CancellationToken ct = default)
    {
        await GetConnectionAsync(connectionId);

        var mappingRepo = _unitOfWork.Repository<CategoryMapping>();
        var mapping = await mappingRepo.FindAsync(
            m => m.Id == mappingId && m.connectionId == connectionId,
            query => query);

        if (mapping == null)
            throw ApiException.NotFound(code: "mapping_not_found");

        var linkedProducts = await _context.productMarketplaceLinks
            .Where(l => l.connectionId == connectionId && l.categoryMappingId == mappingId)
            .Include(l => l.product)
            .ToListAsync(ct);

        if (linkedProducts.Count > 0 && force)
        {
            // Forced delete: detach links - products stay linked to the marketplace
            // but lose their mapping until the user assigns a new one
            foreach (var link in linkedProducts)
                link.categoryMappingId = null;
            await _context.SaveChangesAsync(ct);
            await mappingRepo.DeleteAsync(mapping);
            return Ok(new { success = true, reassignedProducts = 0, detachedProducts = linkedProducts.Count });
        }

        if (linkedProducts.Count > 0)
        {
            if (!reassignToMappingId.HasValue)
            {
                var replacements = await _context.categoryMappings
                    .Where(m => m.connectionId == connectionId &&
                                m.localCategoryId == mapping.localCategoryId &&
                                m.Id != mappingId)
                    .Select(m => new
                    {
                        id = m.Id,
                        name = m.name,
                        externalCategoryId = m.externalCategoryId,
                        externalCategoryName = m.externalCategoryName
                    })
                    .ToListAsync(ct);

                throw new ApiException(
                    $"Mapping is used by products: {linkedProducts.Count}. Select another mapping to reassign before deleting.",
                    StatusCodes.Status409Conflict,
                    data: new
                    {
                        linkedProductsCount = linkedProducts.Count,
                        products = linkedProducts.Take(10).Select(l => new
                        {
                            id = l.productId,
                            name = l.product.name,
                            sku = l.product.sku
                        }),
                        replacementMappings = replacements
                    },
                    code: "conflict");
            }

            var replacement = await _context.categoryMappings
                .FirstOrDefaultAsync(m =>
                    m.Id == reassignToMappingId.Value &&
                    m.connectionId == connectionId &&
                    m.localCategoryId == mapping.localCategoryId &&
                    m.Id != mappingId,
                    ct);

            if (replacement == null)
                throw ApiException.BadRequest(code: "mapping_replacement_invalid");

            foreach (var link in linkedProducts)
                link.categoryMappingId = replacement.Id;

            await _context.SaveChangesAsync(ct);
        }

        await mappingRepo.DeleteAsync(mapping);

        return Ok(new { success = true, reassignedProducts = linkedProducts.Count });
    }

    /// <summary>
    /// Import a category from the marketplace (create local category + attributes + mappings)
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/import-category")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> ImportCategory(
        int connectionId,
        [FromBody] ImportCategoryInput input,
        CancellationToken ct)
    {
        var connection = await GetConnectionAsync(connectionId);

        // Validate localCategoryId if provided
        if (input.localCategoryId.HasValue)
        {
            var categoryRepo = _unitOfWork.Repository<Category>();
            var existingCategory = await categoryRepo.FindAsync(
                c => c.Id == input.localCategoryId.Value && c.storeId == connection.storeId);
            if (existingCategory == null)
                throw ApiException.NotFound(code: "category_not_found");
        }

        var storeId = connection.storeId;
        var userId = _currentUserService.GetUserId() ?? 0;
        var operationId = string.IsNullOrWhiteSpace(input.operationId)
            ? Guid.NewGuid().ToString("N")
            : input.operationId.Trim();

        var queued = await _categoryImportQueue.EnqueueIfNeededAsync(
            connection,
            input.externalCategoryId,
            input.externalCategoryName,
            input.localCategoryName,
            input.localCategoryId,
            input.selectedFieldKeys,
            operationId,
            userId,
            ct,
            input.attributeDecisions ?? new List<CategoryImportAttributeDecision>());

        if (queued == null)
        {
            var existing = await _context.categoryMappings.AsNoTracking()
                .FirstOrDefaultAsync(
                    m => m.connectionId == connectionId
                         && m.externalCategoryId == input.externalCategoryId.Trim(),
                    ct);
            return Ok(new
            {
                queued = false,
                alreadyMapped = true,
                categoryMappingId = existing?.Id,
                localCategoryId = existing?.localCategoryId,
                operationId,
                message = "Category is already mapped"
            });
        }

        var opExists = await _context.operations.AsNoTracking()
            .AnyAsync(o => o.operationId == operationId, ct);
        if (!opExists)
        {
            var label = input.externalCategoryName ?? input.localCategoryName ?? input.externalCategoryId;
            await _operationLogger.StartAsync(
                operationId, storeId, userId, "category-import",
                $"Import category: {label}",
                connectionId: connectionId, connectionName: connection.name,
                batchId: operationId, batchType: "category-import", totalItems: 1);
        }

        return Ok(new
        {
            queued = true,
            operationId,
            message = "Category import queued"
        });
    }

    /// <summary>
    /// Get external category fields and optional per-field details before import.
    /// </summary>
    [HttpGet]
    [Route("/marketplace/connections/{connectionId}/external-category-fields")]
    public async Task<IActionResult> GetExternalCategoryFields(
        int connectionId,
        [FromQuery] string externalCategoryId,
        [FromQuery] string? fieldKey = null,
        [FromQuery] int? localCategoryId = null,
        CancellationToken ct = default)
    {
        var connection = await GetConnectionAsync(connectionId);

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest($"Unknown adapter: {connection.adapterKey}");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.GetCategoryFields))
            throw ApiException.BadRequest(code: "adapter_capability_missing");

        List<CategoryAttribute>? localAttributes = null;
        if (localCategoryId.HasValue)
        {
            var localCategory = await _context.categories
                .AsNoTracking()
                .Where(category => category.Id == localCategoryId.Value
                    && category.storeId == connection.storeId)
                .Select(category => new { category.Id })
                .FirstOrDefaultAsync(ct);
            if (localCategory == null)
                throw ApiException.NotFound(code: "category_not_found");

            localAttributes = await _context.categoryAttributes
                .AsNoTracking()
                .Where(attribute => attribute.categoryId == localCategory.Id)
                .ToListAsync(ct);
        }

        try
        {
            var cacheKey = MarketplaceMetadataCache.CategoryFieldsKey(
                connection.adapterKey, connection.Id, externalCategoryId);
            if (!_metadataCache.TryGetValue(cacheKey, out List<ExternalField>? fields) || fields == null)
            {
                fields = await adapter.GetCategoryFieldsAsync(connection, externalCategoryId, ct);
                _metadataCache.Set(cacheKey, fields, MarketplaceMetadataCache.CategoryFieldsTtl);
            }

            var selectedFields = string.IsNullOrWhiteSpace(fieldKey)
                ? fields.OrderBy(f => f.SortOrder)
                : fields
                    .Where(f => string.Equals(f.Key, fieldKey, StringComparison.Ordinal))
                    .OrderBy(f => f.SortOrder);

            var result = selectedFields.Select(f =>
            {
                var importType = AttributeImportSemantics.ResolveType(f);
                var isFile = importType == AttributeType.File;
                var unitCode = importType == AttributeType.Number
                    ? AttributeFieldConstraints.ResolveUnitCode(f)
                    : null;
                var duplicateCandidates = localAttributes == null || CategoryImportService.IsBaseOrSpecialField(f)
                    ? Array.Empty<AttributeDuplicateCandidate>()
                    : AttributeDuplicateMatcher.FindCandidates(
                        f,
                        importType,
                        unitCode,
                        localAttributes).ToArray();
                var attributeKey = CategoryImportService.SanitizeKey(f.Key);
                var maxLength = isFile ? null : AttributeFieldConstraints.NormalizeMaxLength(f.MaxLength);
                var (minValue, maxValue) = AttributeFieldConstraints.NormalizeRange(
                    isFile ? null : f.MinValue, isFile ? null : f.MaxValue);
                var defaultValue = StringClip.ClipOptional(
                    isFile ? null : CategoryImportService.DetermineDefaultValue(f),
                    CategoryAttribute.ATTRIBUTE_DEFAULT_MAX_LENGTH);
                if (defaultValue != null && maxLength.HasValue)
                    defaultValue = StringClip.ClipOptional(defaultValue, maxLength.Value);

                return new
                {
                    key = f.Key,
                    attributeKey,
                    name = f.Name,
                    importName = StringClip.ClipRequired(
                        AttributeImportSemantics.NormalizeName(f, unitCode),
                        CategoryAttribute.ATTRIBUTE_NAME_MAX_LENGTH,
                        attributeKey),
                    importType = importType.ToString(),
                    unitCode,
                    isRequired = f.IsRequired,
                    attributeIsRequired = false,
                    isUnique = false,
                    isMultiline = false,
                    showOnProductPage = true,
                    allowCustomValues = !isFile && f.AllowCustomValues,
                    allowMultipleFiles = AttributeImportSemantics.AllowsMultipleFiles(f),
                    type = f.Type,
                    optionsCount = isFile ? 0 : f.Options?.Count ?? 0,
                    options = isFile || string.IsNullOrWhiteSpace(fieldKey)
                        ? null
                        : f.Options?.Select(o => new { value = o.Value, label = o.Label }).ToList(),
                    defaultValue,
                    description = StringClip.ClipOptional(
                        f.Description,
                        CategoryAttribute.ATTRIBUTE_DESCRIPTION_MAX_LENGTH),
                    descriptionInTooltip = !string.IsNullOrWhiteSpace(f.Description),
                    group = f.Group,
                    attributeGroup = StringClip.ClipOptional(
                        adapter.DisplayName,
                        CategoryAttribute.ATTRIBUTE_GROUP_MAX_LENGTH),
                    groupIcon = StringClip.ClipOptional(
                        AttributeGroupIconValue.ForAdapter(adapter.Key),
                        CategoryAttribute.ATTRIBUTE_GROUP_ICON_MAX_LENGTH),
                    sortOrder = f.SortOrder,
                    maxLength,
                    minValue,
                    maxValue,
                    maxValues = isFile ? null : AttributeFieldConstraints.NormalizeMaxValues(f.MaxValues),
                    validationPattern = AttributeFieldConstraints.UsableValidationPattern(
                        isFile ? null : f.ValidationPattern),
                    potentialDuplicates = duplicateCandidates.Select(candidate => new
                    {
                        id = candidate.Attribute.Id,
                        name = candidate.Attribute.name,
                        type = candidate.Attribute.type.ToString(),
                        unitCode = candidate.Attribute.unitCode,
                        description = candidate.Attribute.description,
                        score = candidate.Score,
                        matchedBy = candidate.MatchedBy,
                    }),
                    isBaseField = CategoryImportService.IsBaseOrSpecialField(f)
                };
            });

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting category fields for connection {ConnectionId}, category {CategoryId}",
                connectionId, externalCategoryId);
            throw ApiException.BadRequest(code: "external_fields_unavailable");
        }
    }

    /// <summary>
    /// Get local store categories for selection
    /// </summary>
    [HttpGet]
    [Route("/marketplace/connections/{connectionId}/local-categories")]
    public async Task<IActionResult> GetLocalCategories(int connectionId)
    {
        var connection = await GetConnectionAsync(connectionId);

        // Project directly in SQL - avoids loading LONGTEXT attribute options into memory just to count them
        var result = await _context.categories
            .AsNoTracking()
            .Where(c => c.storeId == connection.storeId)
            .Select(c => new
            {
                id = c.Id,
                name = c.name,
                attributesCount = c.attributes.Count()
            })
            .ToListAsync();

        return Ok(result);
    }

    /// <summary>
    /// Resolve available mappings for a list of products (pre-export check).
    /// Uses POST so large selections do not hit Request-URI Too Large (414).
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/resolve-mappings")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> ResolveMappings(
        int connectionId,
        [FromBody] ResolveMappingsInput? input,
        CancellationToken ct = default)
    {
        var connection = await GetConnectionAsync(connectionId);
        var productIds = input?.productIds ?? [];
        var products = await _mappingSelection.ResolveAsync(connection.storeId, connectionId, productIds, ct);
        return Ok(new
        {
            products = products.Select(product => new
            {
                productId = product.ProductId,
                productName = product.ProductName,
                categoryId = product.CategoryId,
                categoryName = product.CategoryName,
                selectedMappingId = product.SelectedMappingId,
                availableMappings = product.AvailableMappings.Select(m => new
                {
                    id = m.Id,
                    name = m.Name,
                    externalCategoryId = m.ExternalCategoryId,
                    externalCategoryName = m.ExternalCategoryName,
                    localCategoryName = m.LocalCategoryName,
                }),
                needsSelection = product.NeedsSelection,
            }),
        });
    }

    /// <summary>
    /// Save mapping selections for products before export.
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/save-mapping-selections")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> SaveMappingSelections(
        int connectionId,
        [FromBody] SaveMappingSelectionsInput input,
        CancellationToken ct = default)
    {
        var connection = await GetConnectionAsync(connectionId);
        var selections = (input.selections ?? [])
            .Select(s => new MappingSelection(s.productId, s.categoryMappingId))
            .ToList();
        var updated = await _mappingSelection.SaveAsync(connection.storeId, connectionId, selections, ct);
        return Ok(new { updated });
    }

    #region Helpers

    private async Task<MarketplaceConnection> GetConnectionAsync(int connectionId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var repo = _unitOfWork.Repository<MarketplaceConnection>();

        var connection = await repo.FindAsync(
            c => c.Id == connectionId && c.storeId == selectedStoreId.Value);

        if (connection == null)
            throw ApiException.NotFound(code: "connection_not_found");

        return connection;
    }

    private async Task<int> CreateFieldMappingsAsync(
        CategoryMapping mapping,
        MarketplaceConnection connection,
        int localCategoryId,
        IReadOnlyCollection<Models.Marketplace.DTOs.ExternalField> fields,
        IMarketplaceAdapter adapter,
        bool skipAutoMapping = false,
        CancellationToken ct = default)
    {
        var categoryAttributes = await _unitOfWork.Repository<CategoryAttribute>()
            .FindAllAsync(a => a.categoryId == localCategoryId);

        if (LocalCategoryAdapterFields.ShouldExposeLocalAttributes(adapter.Key))
            fields = LocalCategoryAdapterFields.Merge(fields.ToList(), categoryAttributes);

        var attributeMap = categoryAttributes
            .SelectMany(a => new[]
            {
                new { Key = a.key, a.Id },
                new { Key = a.name, a.Id }
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        var sortOrder = 0;
        var specs = fields.OrderBy(f => f.SortOrder).Select(field =>
        {
            var (sourceType, sourceField, sourceAttributeId) = skipAutoMapping
                ? (FieldMappingSourceType.Static, (string?)null, (int?)null)
                : DetermineAutoMapping(field, attributeMap);
            return new FieldSpec
            {
                externalFieldKey = field.Key,
                externalFieldName = field.Name,
                externalFieldType = field.Type,
                externalFieldUnit = field.Unit,
                externalFieldUnitCode = AttributeUnits.NormalizeCode(field.UnitCode ?? field.Unit),
                externalFieldUnitId = field.UnitId,
                isRequired = sourceType != FieldMappingSourceType.ProductField && field.IsRequired,
                isEnabled = true,
                sortOrder = sortOrder++,
                sourceType = sourceType,
                sourceField = sourceField,
                sourceAttributeId = sourceAttributeId,
            };
        }).ToList();

        // Every mapping carries a grouping field (synthetic for endpoint-based adapters)
        GraphGenerator.EnsureGroupingField(specs, adapter);

        List<FieldSpec> priceSpecs = new();
        if (adapter.Capabilities.HasFlag(MarketplaceCapabilities.ExportPrices))
        {
            try
            {
                var priceFields = await adapter.GetPriceFieldsAsync(connection, mapping.externalCategoryId, ct);
                var extendedPrice = await _unitOfWork.Repository<Store>().Query()
                    .Where(s => s.Id == connection.storeId)
                    .Select(s => s.extendedPriceParameters)
                    .FirstOrDefaultAsync(ct);
                priceSpecs = CategoryImportService.BuildPriceFieldSpecs(
                    priceFields, attributeMap, skipAutoMapping, extendedPrice);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch price fields for mapping {MappingId}", mapping.Id);
            }
        }

        // The graph is the single source of truth - generate it onto the (tracked) mapping entity.
        mapping.graphJson = GraphGenerator.BuildGraphJson(specs, priceSpecs);
        await _unitOfWork.Repository<CategoryMapping>().UpdateAsync(mapping);

        return specs.Count + priceSpecs.Count;
    }

    /// <summary>
    /// Format a graph output value for preview display.
    /// </summary>
    private static string? FormatGraphValue(object? value)
    {
        if (value == null) return null;
        if (value is string s) return s;
        if (value is System.Collections.IEnumerable en && value is not string)
            return string.Join(", ", en.Cast<object?>().Select(x => x?.ToString()));
        return value.ToString();
    }

    #endregion

    #region Auto-mapping helpers

    /// <summary>
    /// Determine automatic mapping for a field
    /// </summary>
    private static (FieldMappingSourceType sourceType, string? sourceField, int? sourceAttributeId)
        DetermineAutoMapping(Models.Marketplace.DTOs.ExternalField field, Dictionary<string, int> attributeMap)
    {
        var keyLower = field.Key.ToLowerInvariant();
        var nameLower = field.Name.ToLowerInvariant();

        // Try to match against basic product fields
        if (IsSkuField(keyLower, nameLower))
            return (FieldMappingSourceType.ProductField, "sku", null);

        if (CategoryImportService.IsBarcodeField(field))
            return (FieldMappingSourceType.ProductField, "barcode", null);

        if (IsNameField(keyLower, nameLower))
            return (FieldMappingSourceType.ProductField, "name", null);

        if (IsDescriptionField(keyLower, nameLower))
            return (FieldMappingSourceType.ProductField, "description", null);

        if (IsPriceField(keyLower, nameLower))
            return (FieldMappingSourceType.ProductField, "price", null);

        if (IsStockField(keyLower, nameLower))
            return (FieldMappingSourceType.ProductField, "storageQuantity", null);

        // Map product-grouping fields to the group name
        if (IsGroupingField(field.Key, field.Name))
            return (FieldMappingSourceType.GroupName, null, null);

        // Map image fields to the product's images
        if (IsImageField(field.Key, field.Name))
            return (FieldMappingSourceType.ProductField, "images", null);

        // Try to match against local category attributes by key or name
        foreach (var kvp in attributeMap)
        {
            if (kvp.Key.Equals(field.Key, StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.Equals(field.Name, StringComparison.OrdinalIgnoreCase))
            {
                return (FieldMappingSourceType.CategoryAttribute, null, kvp.Value);
            }
        }

        // Default - static empty value
        return (FieldMappingSourceType.Static, null, null);
    }

    private static bool IsSkuField(string key, string name)
    {
        // Ozon attribute 9024 = SKU/Article number
        if (key == "9024") return true;
        if (key == "model" || name == "model") return true;

        // NB: patterns include Russian words because Ozon field keys/names are in Russian
        var patterns = new[] { "sku", "\u0430\u0440\u0442\u0438\u043a\u0443\u043b", "article", "vendor_code", "vendorcode" };
        return patterns.Any(p => key.Contains(p) || name.Contains(p));
    }

    private static bool IsNameField(string key, string name)
    {
        // Ozon attribute 4180 = Product name
        if (key == "4180") return true;

        // Use Contains for name so it matches Ozon's product-name field
        var exactPatterns = new[] { "name", "title" };
        var containsPatterns = new[] { "\u043d\u0430\u0437\u0432\u0430\u043d\u0438\u0435", "\u043d\u0430\u0438\u043c\u0435\u043d\u043e\u0432\u0430\u043d\u0438\u0435" };
        var compactKey = key.Replace("-", "", StringComparison.Ordinal);

        return compactKey is "productname" or "itemname" ||
               exactPatterns.Any(p => key == p || name == p || key.StartsWith(p + "_") || key.EndsWith("_" + p)) ||
               containsPatterns.Any(p => name.Contains(p));
    }

    private static bool IsDescriptionField(string key, string name)
    {
        // Ozon attribute 4191 = Annotation (Description)
        if (key == "4191") return true;

        var patterns = new[] { "description", "\u043e\u043f\u0438\u0441\u0430\u043d\u0438\u0435", "desc", "\u0430\u043d\u043d\u043e\u0442\u0430\u0446\u0438\u044f", "annotation" };
        return patterns.Any(p => key.Contains(p) || name.Contains(p));
    }

    private static bool IsPriceField(string key, string name)
    {
        var patterns = new[] { "price", "\u0446\u0435\u043d\u0430", "\u0441\u0442\u043e\u0438\u043c\u043e\u0441\u0442\u044c", "cost" };
        return patterns.Any(p => key == p || name == p || key.StartsWith(p + "_"));
    }

    private static bool IsStockField(string key, string name)
    {
        var exactPatterns = new[] { "stock", "\u043e\u0441\u0442\u0430\u0442\u043e\u043a", "\u043e\u0441\u0442\u0430\u0442\u043a\u0438", "quantity", "count" };
        if (exactPatterns.Any(p => key == p || name == p))
            return true;

        if (key.StartsWith("stock_") || key.EndsWith("_stock"))
            return true;

        return false;
    }

    private static bool IsGroupingField(string key, string name)
    {
        // Ozon field 9048 - "Model name (for merging into a single card)"
        if (key == "9048") return true;

        var nameLower = name.ToLowerInvariant();
        return nameLower.Contains("\u0434\u043b\u044f \u043e\u0431\u044a\u0435\u0434\u0438\u043d\u0435\u043d\u0438\u044f") ||
               nameLower.Contains("\u043e\u0431\u044a\u0435\u0434\u0438\u043d\u0435\u043d\u0438\u0435 \u0432 \u043a\u0430\u0440\u0442\u043e\u0447\u043a\u0443") ||
               nameLower.Contains("\u0433\u0440\u0443\u043f\u043f\u0438\u0440\u043e\u0432\u043a\u0430 \u0442\u043e\u0432\u0430\u0440\u043e\u0432");
    }

    /// <summary>
    /// Checks whether a field is an image field.
    /// Such fields should not create attributes - they are handled separately.
    /// </summary>
    private static bool IsImageField(string key, string name)
    {
        var keyLower = key.ToLowerInvariant();
        var nameLower = name.ToLowerInvariant();

        // Standard image field keys
        if (keyLower == "images" || keyLower == "primary_image" ||
            keyLower == "image_url" || keyLower == "image_urls" ||
            keyLower == "images360" || keyLower == "color_image")
            return true;

        // Name patterns for image fields
        var patterns = new[] { "\u0438\u0437\u043e\u0431\u0440\u0430\u0436\u0435\u043d", "\u0444\u043e\u0442\u043e", "\u043a\u0430\u0440\u0442\u0438\u043d\u043a", "image", "photo", "picture" };
        return patterns.Any(p => nameLower.Contains(p));
    }

    private static string MapAttributeType(AttributeType type)
        => type switch
        {
            AttributeType.Number => "number",
            AttributeType.Boolean => "boolean",
            AttributeType.Select => "select",
            AttributeType.MultiSelect => "multiselect",
            AttributeType.Barcode => "string",
            _ => "string"
        };

    private static List<Models.Marketplace.DTOs.ExternalFieldOption>? ParseAttributeOptions(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(optionsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            var options = new List<Models.Marketplace.DTOs.ExternalFieldOption>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        options.Add(new Models.Marketplace.DTOs.ExternalFieldOption(value, value));
                    continue;
                }

                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var valueText = TryGetString(item, "value") ?? TryGetString(item, "id") ?? TryGetString(item, "key");
                var labelText = TryGetString(item, "label") ?? TryGetString(item, "name") ?? valueText;
                if (!string.IsNullOrWhiteSpace(valueText))
                    options.Add(new Models.Marketplace.DTOs.ExternalFieldOption(valueText, labelText ?? valueText));
            }

            return options.Count > 0 ? options : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    #endregion

    #region Input DTOs

    public struct CreateMappingInput
    {
        public int localCategoryId { get; set; }
        public string externalCategoryId { get; set; }
        public string? externalCategoryName { get; set; }
        public string? name { get; set; }
        public bool? useAiMapping { get; set; }
    }

    public struct CreateExternalCategoryInput
    {
        public int localCategoryId { get; set; }
        public string? externalCategoryName { get; set; }
        public string? description { get; set; }
        public string? parentExternalCategoryId { get; set; }
        public string? mappingName { get; set; }
        public int? storeId { get; set; }
        public int? languageId { get; set; }
        public List<string>? selectedAttributeKeys { get; set; }
        public bool? skipAutoMapping { get; set; }
    }

    public struct UpdateMappingInput
    {
        public string? name { get; set; }
    }

    public struct SaveMappingSelectionsInput
    {
        public List<MappingSelectionItem> selections { get; set; }
    }

    public sealed class ResolveMappingsInput
    {
        public List<int>? productIds { get; set; }
    }

    public struct MappingSelectionItem
    {
        public int productId { get; set; }
        public int categoryMappingId { get; set; }
    }

    public struct ImportCategoryInput
    {
        public string externalCategoryId { get; set; }
        public string? externalCategoryName { get; set; }
        public string? localCategoryName { get; set; }
        public int? localCategoryId { get; set; }
        public List<string>? selectedFieldKeys { get; set; }
        public List<CategoryImportAttributeDecision>? attributeDecisions { get; set; }
        public string? operationId { get; set; }
    }

    /// <summary>
    /// Body for saving a visual node graph. A null graph clears it (reverts to per-field export).
    /// </summary>
    public struct SaveGraphInput
    {
        public JsonElement? graph { get; set; }

        /// <summary>
        /// Per-field projection of the graph back onto FieldMapping rows so the existing import
        /// (reverse) path keeps working. Reversible attribute-lanes carry a real source +
        /// modifiersJson; fan-in / op / non-attribute lanes are Static (import skips them).
        /// </summary>
        public List<FieldProjection>? fields { get; set; }
    }

    /// <summary>
    /// One field's projected source + modifier chain (computed by the editor from the graph).
    /// </summary>
    public struct FieldProjection
    {
        public string externalFieldKey { get; set; }
        public FieldMappingSourceType sourceType { get; set; }
        public int? sourceAttributeId { get; set; }
        public string? modifiersJson { get; set; }
    }

    /// <summary>
    /// Body for previewing a node graph against a product. graph is optional (unsaved edits).
    /// </summary>
    public struct GraphPreviewInput
    {
        public int productId { get; set; }
        public JsonElement? graph { get; set; }
    }

    public struct GraphConnectInput
    {
        public string adapterFieldKey { get; set; }
        public string sourceType { get; set; }
        public string? sourceField { get; set; }
        public int? sourceAttributeId { get; set; }
        public string? staticValue { get; set; }
        public string? sink { get; set; }
    }

    public struct GraphModifierParamSource
    {
        public string param { get; set; }
        public string sourceType { get; set; }
        public string? sourceField { get; set; }
        public int? sourceAttributeId { get; set; }
        public string? staticValue { get; set; }
    }

    public struct GraphModifierInput
    {
        public string adapterFieldKey { get; set; }
        public string modifierType { get; set; }
        public Dictionary<string, JsonElement>? @params { get; set; }
        /// <summary>Accepted beside modifierType and copied into params when params.pattern is absent.</summary>
        public string? pattern { get; set; }
        /// <summary>Accepted beside modifierType and copied into params when params.replacement is absent.</summary>
        public string? replacement { get; set; }
        public bool? ignoreCase { get; set; }
        public int? group { get; set; }
        public string? sink { get; set; }
        /// <summary>
        /// Modifier types to splice off this field before the new block is inserted
        /// (literal <c>replace</c> blocks when switching that field to <c>regexReplace</c>).
        /// </summary>
        public List<string>? removeModifierTypes { get; set; }
        public List<GraphModifierParamSource>? paramSources { get; set; }
    }

    public struct GraphGroupTitlesInput
    {
        public List<GraphGroupTitlePair>? mappings { get; set; }
    }

    public struct GraphGroupTitlePair
    {
        public string from { get; set; }
        public string to { get; set; }
    }

    #endregion
}
