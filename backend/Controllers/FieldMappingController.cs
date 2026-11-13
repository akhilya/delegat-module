using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Enums;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Models.Products;
using Delegat.Services.Marketplace;
using Delegat.Services.Marketplace.Modifiers;
using IMarketplaceMetadataCache = Delegat.Marketplace.SDK.IMarketplaceMetadataCache;

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
    private readonly IMarketplaceMetadataCache _metadataCache;
    private readonly ILogger<FieldMappingController> _logger;

    public FieldMappingController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider,
        IMarketplaceAdapterRegistry adapterRegistry,
        MappingService mappingService,
        IMarketplaceMetadataCache metadataCache,
        ILogger<FieldMappingController> logger)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
        _adapterRegistry = adapterRegistry;
        _mappingService = mappingService;
        _metadataCache = metadataCache;
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

        var connection = mapping.connection;

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest(code: "adapter_not_found");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.GetCategoryFields))
            throw ApiException.BadRequest(code: "adapter_capability_missing");

        try
        {
            var fields = await MappingFieldSchema.GetCategoryFieldsAsync(
                _metadataCache, adapter, connection, mapping.externalCategoryId, ct);

            if (LocalCategoryAdapterFields.ShouldExposeLocalAttributes(connection.adapterKey))
            {
                var attrs = await _unitOfWork.Repository<CategoryAttribute>()
                    .FindAllAsync(a => a.categoryId == mapping.localCategoryId);
                fields = LocalCategoryAdapterFields.Merge(fields, attrs);
            }

            return Ok(fields.Select(MappingFieldSchema.ToDto));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting category fields for mapping {MappingId}", mappingId);
            throw ApiException.BadRequest(code: "external_fields_unavailable");
        }
    }

    /// <summary>
    /// Get marketplace price-API fields for the dedicated price mapping block.
    /// </summary>
    [HttpGet]
    [Route("/marketplace/mappings/{mappingId}/price-fields")]
    public async Task<IActionResult> GetPriceFields(int mappingId, CancellationToken ct)
    {
        var mapping = await GetMappingAsync(mappingId);

        var connection = mapping.connection;

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest(code: "adapter_not_found");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ExportPrices))
            return Ok(Array.Empty<object>());

        try
        {
            var fields = await MappingFieldSchema.GetPriceFieldsAsync(
                _metadataCache, adapter, connection, mapping.externalCategoryId, ct);
            var extendedPrice = await _unitOfWork.Repository<Store>().Query()
                .Where(s => s.Id == connection.storeId)
                .Select(s => s.extendedPriceParameters)
                .FirstOrDefaultAsync(ct);
            return Ok(MappingFieldSchema.VisiblePriceFields(fields, extendedPrice).Select(MappingFieldSchema.ToDto));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting price fields for mapping {MappingId}", mappingId);
            throw ApiException.BadRequest(code: "external_price_fields_unavailable");
        }
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

        var connection = mapping.connection;

        var storeAttributeRepo = _unitOfWork.Repository<StoreAttribute>();
        var storeAttributes = connection != null
            ? await storeAttributeRepo.FindAllAsync(sa => sa.storeId == connection.storeId)
            : new List<StoreAttribute>();

        var store = connection != null
            ? await _unitOfWork.Repository<Store>().GetByIdWithIncludesAsync(connection.storeId, q => q.Include(s => s.currencies))
            : null;

        var extendedPrice = store?.extendedPriceParameters ?? false;
        static object MapField(ProductFieldInfo f) => new
        {
            key = f.Key,
            displayName = f.DisplayName,
            type = f.Type,
        };

        return Ok(new
        {
            extendedPriceParameters = extendedPrice,
            currencies = store?.MapCurrencyDtos() ?? new List<Store.CurrencyDTO>(),
            productFields = MappingService.GetBaseProductFields().Select(MapField),
            stockFields = MappingService.GetStockProductFields().Select(MapField),
            priceFields = MappingService.GetPriceProductFields(extendedPrice).Select(MapField),
            categoryAttributes = category?.attributes?.Select(a => new
            {
                id = a.Id,
                key = a.key,
                name = a.name,
                type = a.type.ToString(),
                unitCode = a.unitCode,
                isRequired = a.isRequired,
                options = a.type is AttributeType.Select or AttributeType.MultiSelect
                    ? Delegat.Services.Marketplace.Mapping.OptionAlignmentHelper.ParseAttributeOptions(a.options)
                        .Select(o => new { value = o.Value, label = o.Label })
                        .ToList()
                    : null
            }) ?? Enumerable.Empty<object>(),
            storeAttributes = storeAttributes.Select(sa => new
            {
                id = sa.Id,
                key = sa.key,
                name = sa.name,
                type = sa.type.ToString(),
                unitCode = sa.unitCode,
                isRequired = sa.isRequired,
                options = sa.type is AttributeType.Select or AttributeType.MultiSelect
                    ? Delegat.Services.Marketplace.Mapping.OptionAlignmentHelper.ParseAttributeOptions(sa.options)
                        .Select(o => new { value = o.Value, label = o.Label })
                        .ToList()
                    : null
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
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");
        var storeId = selectedStoreId.Value;

        string? options = null;

        if (type == "category")
        {
            options = await _unitOfWork.Repository<CategoryAttribute>().Query()
                .Where(a => a.Id == id && a.category.storeId == storeId)
                .Select(a => a.options)
                .FirstOrDefaultAsync();
        }
        else if (type == "store")
        {
            options = await _unitOfWork.Repository<StoreAttribute>().Query()
                .Where(a => a.Id == id && a.storeId == storeId)
                .Select(a => a.options)
                .FirstOrDefaultAsync();
        }
        else
        {
            throw ApiException.BadRequest(code: "mapping_type_invalid");
        }

        return Ok(new { options });
    }

    #region Helpers
    private async Task<CategoryMapping> GetMappingAsync(int mappingId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var repo = _unitOfWork.Repository<CategoryMapping>();

        var mapping = await repo.FindAsync(
            m => m.Id == mappingId,
            query => query
                .Include(m => m.connection)
                .Include(m => m.localCategory));

        if (mapping == null)
            throw ApiException.NotFound(code: "mapping_not_found");

        if (mapping.connection?.storeId != selectedStoreId.Value)
            throw ApiException.NotFound(code: "mapping_not_found");

        return mapping;
    }

    #endregion

}
