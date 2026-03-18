using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrintO.Enums;
using PrintO.Infrastructure.Auth;
using PrintO.Infrastructure.Data;
using PrintO.Infrastructure.Middleware;
using PrintO.Models;
using PrintO.Models.Marketplace;
using PrintO.Models.Products;
using PrintO.Services.Marketplace;
using PrintO.Services.Marketplace.Modifiers;

namespace PrintO.Controllers;

[ApiController]
[JwtAuthorize]
public class FieldMappingController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceProvider _serviceProvider;
    private readonly IMarketplaceAdapterRegistry _adapterRegistry;
    private readonly MappingService _mappingService;
    private readonly DataContext _context;
    private readonly ILogger<FieldMappingController> _logger;

    public FieldMappingController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider,
        IMarketplaceAdapterRegistry adapterRegistry,
        MappingService mappingService,
        DataContext context,
        ILogger<FieldMappingController> logger)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
        _adapterRegistry = adapterRegistry;
        _mappingService = mappingService;
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get marketplace category fields
    /// </summary>
    [HttpGet]
    [Route("/marketplace/mappings/{mappingId}/external-fields")]
    public async Task<IActionResult> GetExternalFields(int mappingId, CancellationToken ct)
    {
        var mapping = await GetMappingAsync(mappingId);

        var connectionRepo = _unitOfWork.Repository<MarketplaceConnection>();
        var connection = await connectionRepo.FindAsync(c => c.Id == mapping.connectionId);

        if (connection == null)
            throw ApiException.NotFound("Подключение не найдено");

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest($"Неизвестный адаптер: {connection.adapterKey}");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.GetCategoryFields))
            throw ApiException.BadRequest("Адаптер не поддерживает получение полей категории");

        try
        {
            var fields = await adapter.GetCategoryFieldsAsync(connection, mapping.externalCategoryId, ct);
            return Ok(fields.Select(f => new
            {
                key = f.Key,
                name = f.Name,
                isRequired = f.IsRequired,
                type = f.Type,
                options = f.Options,
                description = f.Description,
                sortOrder = f.SortOrder,
                isAspect = f.IsAspect
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting category fields for mapping {MappingId}", mappingId);
            throw ApiException.BadRequest($"Не удалось получить поля категории: {ex.Message}");
        }
    }

    /// <summary>
    /// Get current field mappings
    /// </summary>
    [HttpGet]
    [Route("/marketplace/mappings/{mappingId}/field-mappings")]
    public async Task<IActionResult> GetFieldMappings(int mappingId, CancellationToken ct)
    {
        var mapping = await GetMappingAsync(mappingId);

        var repo = _unitOfWork.Repository<FieldMapping>();
        var fieldMappings = await repo.FindAllAsync(
            fm => fm.categoryMappingId == mappingId,
            query => query.OrderBy(fm => fm.sortOrder));

        // Lazy backfill: if any field mappings are missing type metadata, fetch from marketplace
        if (fieldMappings.Any(fm => fm.externalFieldType == null))
        {
            await BackfillFieldTypeMetadataAsync(mapping, fieldMappings, ct);
        }

        var result = fieldMappings.Select(fm => new
        {
            id = fm.Id,
            externalFieldKey = fm.externalFieldKey,
            externalFieldName = fm.externalFieldName,
            isRequired = fm.isRequired,
            isEnabled = fm.isEnabled,
            sourceType = fm.sourceType.ToString(),
            sourceField = fm.sourceField,
            sourceAttributeId = fm.sourceAttributeId,
            staticValue = fm.staticValue,
            transformExpression = fm.transformExpression,
            modifiersJson = fm.modifiersJson,
            externalFieldType = fm.externalFieldType,
            externalFieldOptions = (fm.externalFieldType == "select" || fm.externalFieldType == "multiselect")
                ? fm.externalFieldOptions : null,
            sortOrder = fm.sortOrder
        }).ToList();

        return Ok(new
        {
            fieldMappings = result,
            localCategoryId = mapping.localCategoryId,
            localCategoryName = mapping.localCategory?.name,
            externalCategoryId = mapping.externalCategoryId,
            externalCategoryName = mapping.externalCategoryName,
            name = mapping.name
        });
    }

    /// <summary>
    /// Save field mappings (bulk)
    /// </summary>
    [HttpPost]
    [Route("/marketplace/mappings/{mappingId}/field-mappings")]
    public async Task<IActionResult> SaveFieldMappings(
        int mappingId,
        [FromBody] SaveFieldMappingsInput input)
    {
        var mapping = await GetMappingAsync(mappingId);

        var repo = _unitOfWork.Repository<FieldMapping>();

        // Get existing mappings
        var existingMappings = await repo.FindAllAsync(fm => fm.categoryMappingId == mappingId);
        var existingDict = existingMappings.ToDictionary(fm => fm.externalFieldKey);

        foreach (var item in input.fieldMappings)
        {
            if (existingDict.TryGetValue(item.externalFieldKey, out var existing))
            {
                // Update existing
                existing.UpdateFill(new FieldMapping.UpdateForm
                {
                    isEnabled = item.isEnabled,
                    sourceType = Enum.Parse<FieldMappingSourceType>(item.sourceType),
                    sourceField = item.sourceField,
                    sourceAttributeId = item.sourceAttributeId,
                    staticValue = item.staticValue,
                    transformExpression = item.transformExpression,
                    modifiersJson = item.modifiersJson,
                    sortOrder = item.sortOrder
                });
                await repo.UpdateAsync(existing);
            }
            else
            {
                // Create new
                var newMapping = new FieldMapping();
                newMapping.AddFill(new FieldMapping.AddForm
                {
                    categoryMappingId = mappingId,
                    externalFieldKey = item.externalFieldKey,
                    externalFieldName = item.externalFieldName,
                    isRequired = item.isRequired,
                    isEnabled = item.isEnabled,
                    sourceType = Enum.Parse<FieldMappingSourceType>(item.sourceType),
                    sourceField = item.sourceField,
                    sourceAttributeId = item.sourceAttributeId,
                    staticValue = item.staticValue,
                    transformExpression = item.transformExpression,
                    modifiersJson = item.modifiersJson,
                    sortOrder = item.sortOrder
                });
                await repo.AddAsync(newMapping);
            }
        }

        return Ok(new { success = true, count = input.fieldMappings.Count });
    }

    /// <summary>
    /// Toggle field enable/disable
    /// </summary>
    [HttpPatch]
    [Route("/marketplace/mappings/{mappingId}/field-mappings/{fieldMappingId}/toggle")]
    public async Task<IActionResult> ToggleFieldMapping(int mappingId, int fieldMappingId)
    {
        await GetMappingAsync(mappingId);

        var repo = _unitOfWork.Repository<FieldMapping>();
        var fieldMapping = await repo.FindAsync(
            fm => fm.Id == fieldMappingId && fm.categoryMappingId == mappingId);

        if (fieldMapping == null)
            throw ApiException.NotFound("Маппинг поля не найден");

        fieldMapping.UpdateFill(new FieldMapping.UpdateForm
        {
            isEnabled = !fieldMapping.isEnabled
        });
        await repo.UpdateAsync(fieldMapping);

        return Ok(new { isEnabled = fieldMapping.isEnabled });
    }

    /// <summary>
    /// Update a single field mapping
    /// </summary>
    [HttpPatch]
    [Route("/marketplace/mappings/{mappingId}/field-mappings/{fieldMappingId}")]
    public async Task<IActionResult> UpdateFieldMapping(
        int mappingId,
        int fieldMappingId,
        [FromBody] FieldMappingUpdateInput input)
    {
        await GetMappingAsync(mappingId);

        var repo = _unitOfWork.Repository<FieldMapping>();
        var fieldMapping = await repo.FindAsync(
            fm => fm.Id == fieldMappingId && fm.categoryMappingId == mappingId);

        if (fieldMapping == null)
            throw ApiException.NotFound("Маппинг поля не найден");

        var updateForm = new FieldMapping.UpdateForm();

        if (input.isEnabled.HasValue)
            updateForm.isEnabled = input.isEnabled.Value;
        if (!string.IsNullOrEmpty(input.sourceType))
            updateForm.sourceType = Enum.Parse<FieldMappingSourceType>(input.sourceType);
        if (input.sourceField != null)
            updateForm.sourceField = input.sourceField;
        if (input.sourceAttributeId.HasValue)
            updateForm.sourceAttributeId = input.sourceAttributeId;
        if (input.staticValue != null)
            updateForm.staticValue = input.staticValue;
        if (input.transformExpression != null)
            updateForm.transformExpression = input.transformExpression;
        if (input.modifiersJson != null)
            updateForm.modifiersJson = input.modifiersJson;
        if (input.sortOrder.HasValue)
            updateForm.sortOrder = input.sortOrder.Value;

        fieldMapping.UpdateFill(updateForm);
        await repo.UpdateAsync(fieldMapping);

        return Ok(new { success = true });
    }

    /// <summary>
    /// Get available data sources for mapping
    /// </summary>
    [HttpGet]
    [Route("/marketplace/mappings/{mappingId}/available-sources")]
    public async Task<IActionResult> GetAvailableSources(int mappingId)
    {
        var mapping = await GetMappingAsync(mappingId);

        // Load the category and its attributes
        var categoryRepo = _unitOfWork.Repository<Category>();
        var category = await categoryRepo.FindAsync(
            c => c.Id == mapping.localCategoryId,
            query => query.Include(c => c.attributes));

        // Load store attributes
        var connectionRepo = _unitOfWork.Repository<MarketplaceConnection>();
        var connection = await connectionRepo.FindAsync(c => c.Id == mapping.connectionId);

        var storeAttributeRepo = _unitOfWork.Repository<StoreAttribute>();
        var storeAttributes = connection != null
            ? await storeAttributeRepo.FindAllAsync(sa => sa.storeId == connection.storeId)
            : new List<StoreAttribute>();

        var productFields = MappingService.GetAvailableProductFields();

        return Ok(new
        {
            productFields = productFields.Select(f => new
            {
                key = f.Key,
                displayName = f.DisplayName,
                type = f.Type
            }),
            categoryAttributes = category?.attributes?.Select(a => new
            {
                id = a.Id,
                key = a.key,
                name = a.name,
                type = a.type.ToString(),
                isRequired = a.isRequired
            }) ?? Enumerable.Empty<object>(),
            storeAttributes = storeAttributes.Select(sa => new
            {
                id = sa.Id,
                key = sa.key,
                name = sa.name,
                type = sa.type.ToString(),
                isRequired = sa.isRequired
            })
        });
    }

    /// <summary>
    /// Get options for a single attribute (category or store) on demand
    /// </summary>
    [HttpGet]
    [Route("/marketplace/attribute-options")]
    public async Task<IActionResult> GetAttributeOptions(
        [FromQuery] string type,
        [FromQuery] int id)
    {
        string? options = null;

        if (type == "category")
        {
            var repo = _unitOfWork.Repository<CategoryAttribute>();
            var attr = await repo.FindAsync(a => a.Id == id);
            options = attr?.options;
        }
        else if (type == "store")
        {
            var repo = _unitOfWork.Repository<StoreAttribute>();
            var attr = await repo.FindAsync(a => a.Id == id);
            options = attr?.options;
        }
        else
        {
            throw ApiException.BadRequest("Invalid type. Expected 'category' or 'store'.");
        }

        return Ok(new { options });
    }

    /// <summary>
    /// Preview modifier results for a specific product
    /// </summary>
    [HttpPost]
    [Route("/marketplace/mappings/{mappingId}/preview")]
    public async Task<IActionResult> PreviewFieldMappings(
        int mappingId,
        [FromBody] PreviewInput input,
        CancellationToken ct)
    {
        var mapping = await GetMappingAsync(mappingId);

        // Load product with all relations needed for field value extraction
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
            throw ApiException.NotFound("Товар не найден");

        // Build field mapping list: use overrides from request body if provided,
        // otherwise fall back to DB records.
        List<FieldMapping> fieldMappings;
        if (input.fieldMappings is { Count: > 0 })
        {
            fieldMappings = input.fieldMappings
                .Where(fm => fm.isEnabled)
                .Select(fm => new FieldMapping
                {
                    externalFieldKey = fm.externalFieldKey,
                    sourceType = fm.sourceType,
                    sourceField = fm.sourceField,
                    sourceAttributeId = fm.sourceAttributeId,
                    staticValue = fm.staticValue,
                    modifiersJson = fm.modifiersJson,
                    isEnabled = fm.isEnabled,
                })
                .ToList();
        }
        else
        {
            var repo = _unitOfWork.Repository<FieldMapping>();
            fieldMappings = (await repo.FindAllAsync(
                fm => fm.categoryMappingId == mappingId && fm.isEnabled,
                query => query.OrderBy(fm => fm.sortOrder))).ToList();
        }

        var previews = new List<object>();

        foreach (var fm in fieldMappings)
        {
            string? error = null;
            object? sourceValue = null;
            object? resultValue = null;

            try
            {
                sourceValue = _mappingService.GetFieldValue(product, fm);

                var modifierContext = new ModifierContext
                {
                    ResolveReference = (reference) => _mappingService.ResolveFieldReference(product, reference)
                };

                // Apply modifiers: prefer modifiersJson, fall back to legacy transformExpression
                if (!string.IsNullOrEmpty(fm.modifiersJson))
                {
                    resultValue = ModifierPipeline.ApplyModifiers(sourceValue, fm.modifiersJson, modifierContext);
                }
                else if (!string.IsNullOrEmpty(fm.transformExpression) && sourceValue != null)
                {
                    resultValue = sourceValue;
                }
                else
                {
                    resultValue = sourceValue;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            previews.Add(new
            {
                fieldMappingId = fm.Id,
                externalFieldKey = fm.externalFieldKey,
                sourceValue = FormatPreviewValue(sourceValue),
                resultValue = FormatPreviewValue(resultValue),
                error
            });
        }

        return Ok(new { previews });
    }

    private static string? FormatPreviewValue(object? value)
    {
        if (value == null) return null;
        if (value is string s) return s;
        if (value is List<string> list) return string.Join(", ", list);
        if (value is decimal d) return d.ToString();
        return value.ToString();
    }

    #region Helpers

    /// <summary>
    /// Backfill externalFieldType and externalFieldOptions from the marketplace API
    /// for field mappings that are missing this data (created before this feature).
    /// </summary>
    private async Task BackfillFieldTypeMetadataAsync(
        CategoryMapping mapping,
        List<FieldMapping> fieldMappings,
        CancellationToken ct)
    {
        try
        {
            var connectionRepo = _unitOfWork.Repository<MarketplaceConnection>();
            var connection = await connectionRepo.FindAsync(c => c.Id == mapping.connectionId);
            if (connection == null) return;

            var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
            if (adapter == null || !adapter.Capabilities.HasFlag(MarketplaceCapabilities.GetCategoryFields))
                return;

            var externalFields = await adapter.GetCategoryFieldsAsync(connection, mapping.externalCategoryId, ct);
            var fieldLookup = externalFields.ToDictionary(f => f.Key);

            var repo = _unitOfWork.Repository<FieldMapping>();
            foreach (var fm in fieldMappings.Where(fm => fm.externalFieldType == null))
            {
                if (fieldLookup.TryGetValue(fm.externalFieldKey, out var extField))
                {
                    fm.externalFieldType = extField.Type;
                    fm.externalFieldOptions = extField.Options != null && extField.Options.Count > 0
                        ? JsonSerializer.Serialize(extField.Options.Select(o => new { value = o.Value, label = o.Label }))
                        : null;
                    repo.UpdateWithoutSave(fm);
                }
                else
                {
                    // Field not found on marketplace - default to string
                    fm.externalFieldType = "string";
                    repo.UpdateWithoutSave(fm);
                }
            }

            await _unitOfWork.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to backfill field type metadata for mapping {MappingId}", mapping.Id);
        }
    }

    private async Task<CategoryMapping> GetMappingAsync(int mappingId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var repo = _unitOfWork.Repository<CategoryMapping>();

        var mapping = await repo.FindAsync(
            m => m.Id == mappingId,
            query => query
                .Include(m => m.connection)
                .Include(m => m.localCategory));

        if (mapping == null)
            throw ApiException.NotFound("Маппинг не найден");

        if (mapping.connection?.storeId != selectedStoreId.Value)
            throw ApiException.NotFound("Маппинг не найден");

        return mapping;
    }

    #endregion

    #region Input DTOs

    public struct SaveFieldMappingsInput
    {
        public List<FieldMappingItem> fieldMappings { get; set; }
    }

    public struct FieldMappingItem
    {
        public string externalFieldKey { get; set; }
        public string? externalFieldName { get; set; }
        public bool isRequired { get; set; }
        public bool isEnabled { get; set; }
        public string sourceType { get; set; }
        public string? sourceField { get; set; }
        public int? sourceAttributeId { get; set; }
        public string? staticValue { get; set; }
        public string? transformExpression { get; set; }
        public string? modifiersJson { get; set; }
        public int sortOrder { get; set; }
    }

    public struct FieldMappingUpdateInput
    {
        public bool? isEnabled { get; set; }
        public string? sourceType { get; set; }
        public string? sourceField { get; set; }
        public int? sourceAttributeId { get; set; }
        public string? staticValue { get; set; }
        public string? transformExpression { get; set; }
        public string? modifiersJson { get; set; }
        public int? sortOrder { get; set; }
    }

    public struct PreviewInput
    {
        public int productId { get; set; }
        /// <summary>
        /// Optional in-memory field mapping overrides (unsaved state from the UI).
        /// When provided, these are used instead of DB records.
        /// </summary>
        public List<PreviewFieldMappingItem>? fieldMappings { get; set; }
    }

    public struct PreviewFieldMappingItem
    {
        public string externalFieldKey { get; set; }
        public FieldMappingSourceType sourceType { get; set; }
        public string? sourceField { get; set; }
        public int? sourceAttributeId { get; set; }
        public string? staticValue { get; set; }
        public string? modifiersJson { get; set; }
        public bool isEnabled { get; set; }
    }

    #endregion
}
