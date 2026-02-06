using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrintO.Enums;
using PrintO.Infrastructure.Auth;
using PrintO.Infrastructure.Data;
using PrintO.Infrastructure.Middleware;
using PrintO.Models;
using PrintO.Models.Marketplace;
using PrintO.Services.Marketplace;

namespace PrintO.Controllers;

[ApiController]
[JwtAuthorize]
public class FieldMappingController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceProvider _serviceProvider;
    private readonly IMarketplaceAdapterRegistry _adapterRegistry;
    private readonly ILogger<FieldMappingController> _logger;

    public FieldMappingController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider,
        IMarketplaceAdapterRegistry adapterRegistry,
        ILogger<FieldMappingController> logger)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
        _adapterRegistry = adapterRegistry;
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
                sortOrder = f.SortOrder
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
    public async Task<IActionResult> GetFieldMappings(int mappingId)
    {
        var mapping = await GetMappingAsync(mappingId);

        var repo = _unitOfWork.Repository<FieldMapping>();
        var fieldMappings = await repo.FindAllAsync(
            fm => fm.categoryMappingId == mappingId,
            query => query.OrderBy(fm => fm.sortOrder));

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
            sortOrder = fm.sortOrder
        }).ToList();

        return Ok(new
        {
            fieldMappings = result,
            localCategoryName = mapping.localCategory?.name,
            externalCategoryId = mapping.externalCategoryId,
            externalCategoryName = mapping.externalCategoryName
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
                type = a.type.ToString()
            }) ?? Enumerable.Empty<object>(),
            storeAttributes = storeAttributes.Select(sa => new
            {
                id = sa.Id,
                key = sa.key,
                name = sa.name,
                type = sa.type.ToString()
            })
        });
    }

    #region Helpers

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
        public int? sortOrder { get; set; }
    }

    #endregion
}
