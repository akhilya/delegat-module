using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Enums;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Products;
using Delegat.Services.Marketplace;
using Delegat.Services.Marketplace.Modifiers;

namespace Delegat.Controllers;

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
    /// Mapping metadata for the editor page. The per-field schema now lives in the graph
    /// (<see cref="CategoryMapping.graphJson"/>), so this returns only the mapping info the page needs.
    /// </summary>
    [HttpGet]
    [Route("/marketplace/mappings/{mappingId}/field-mappings")]
    public async Task<IActionResult> GetFieldMappings(int mappingId, CancellationToken ct)
    {
        var mapping = await GetMappingAsync(mappingId);
        return Ok(new
        {
            fieldMappings = Array.Empty<object>(),
            name = mapping.name,
            localCategoryId = mapping.localCategoryId,
            localCategoryName = mapping.localCategory?.name,
            externalCategoryId = mapping.externalCategoryId,
            externalCategoryName = mapping.externalCategoryName,
        });
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

}
