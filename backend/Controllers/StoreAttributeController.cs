using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Interfaces;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
public class StoreAttributeController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StoreAttributeController> _logger;

    public StoreAttributeController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider,
        ILogger<StoreAttributeController> logger)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    [HttpGet]
    [Route("/store-attributes")]
    public async Task<IActionResult> GetAllStoreAttributes()
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var attrRepo = _unitOfWork.Repository<StoreAttribute>();

        var attributes = await attrRepo.FindAllAsync(
            a => a.storeId == selectedStoreId.Value);

        var result = attributes
            .OrderBy(a => a.sortOrder)
            .Select(a => ((IMapToDTO<StoreAttribute.AttributeDTO>)a).MapToDTO(_serviceProvider))
            .ToList();

        return Ok(result);
    }

    /// <summary>Retrieves list-only metadata without selecting dictionary or validation columns.</summary>
    [HttpGet("/store-attributes/summary")]
    public async Task<IActionResult> GetStoreAttributeSummaries()
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync();
        if (storeId == null)
            throw ApiException.BadRequest(code: "store_not_selected");
        var attributes = await _unitOfWork.Repository<StoreAttribute>().Query()
            .Where(a => a.storeId == storeId.Value)
            .OrderBy(a => a.sortOrder).ThenBy(a => a.Id)
            .Select(AttributeDefinitionSummary.FromStore).ToListAsync();
        return Ok(attributes);
    }

    [HttpGet]
    [Route("/store-attributes/{attributeId}")]
    public async Task<IActionResult> GetStoreAttribute(int attributeId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var attrRepo = _unitOfWork.Repository<StoreAttribute>();

        var attribute = await attrRepo.FindAsync(
            a => a.Id == attributeId && a.storeId == selectedStoreId.Value);

        if (attribute == null)
            throw ApiException.NotFound(code: "store_attribute_not_found");

        return Ok(((IMapToDTO<StoreAttribute.AttributeDTO>)attribute).MapToDTO(_serviceProvider));
    }

    [HttpPost]
    [Route("/store-attributes")]
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> CreateStoreAttribute([FromBody] StoreAttribute.UserAddForm input)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var attrRepo = _unitOfWork.Repository<StoreAttribute>();

        // Check key uniqueness within this store
        var existingAttr = await attrRepo.FindAsync(
            a => a.storeId == selectedStoreId.Value && a.key == input.key);
        if (existingAttr != null)
            throw ApiException.BadRequest(code: "store_attribute_key_taken");

        // Get max sortOrder
        var allAttrs = await attrRepo.FindAllAsync(a => a.storeId == selectedStoreId.Value);
        var maxSortOrder = allAttrs.Count > 0 ? allAttrs.Max(a => a.sortOrder) : -1;

        var attribute = new StoreAttribute();
        attribute.AddFill(new StoreAttribute.AddForm
        {
            name = input.name,
            key = input.key,
            type = input.type,
            isRequired = input.isRequired,
            isUnique = input.isUnique,
            isMultiline = input.isMultiline,
            allowCustomValues = input.allowCustomValues,
            showOnProductPage = input.showOnProductPage,
            sortOrder = maxSortOrder + 1,
            options = input.options,
            defaultValue = input.defaultValue,
            description = input.description,
            descriptionInTooltip = input.descriptionInTooltip,
            maxLength = input.maxLength,
            minValue = input.minValue,
            maxValue = input.maxValue,
            maxValues = input.maxValues,
            validationPattern = input.validationPattern,
            storeId = selectedStoreId.Value
        });

        await attrRepo.AddAsync(attribute);

        return Ok(new { id = attribute.Id });
    }

    [HttpPatch]
    [Route("/store-attributes/{attributeId}")]
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> UpdateStoreAttribute(int attributeId, [FromBody] StoreAttribute.UpdateForm input)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var attrRepo = _unitOfWork.Repository<StoreAttribute>();

        var attribute = await attrRepo.FindAsync(
            a => a.Id == attributeId && a.storeId == selectedStoreId.Value);

        if (attribute == null)
            throw ApiException.NotFound(code: "store_attribute_not_found");

        // Check key uniqueness if key is being changed
        if (!string.IsNullOrEmpty(input.key) && input.key != attribute.key)
        {
            var existingAttr = await attrRepo.FindAsync(
                a => a.storeId == selectedStoreId.Value && a.key == input.key && a.Id != attributeId);
            if (existingAttr != null)
                throw ApiException.BadRequest(code: "store_attribute_key_taken");
        }

        attribute.UpdateFill(input);
        await attrRepo.UpdateAsync(attribute);

        return Ok(new { success = true });
    }

    [HttpDelete]
    [Route("/store-attributes/{attributeId}")]
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> DeleteStoreAttribute(int attributeId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var attrRepo = _unitOfWork.Repository<StoreAttribute>();

        var attribute = await attrRepo.FindAsync(
            a => a.Id == attributeId && a.storeId == selectedStoreId.Value,
            query => query.Include(a => a.values));

        if (attribute == null)
            throw ApiException.NotFound(code: "store_attribute_not_found");

        // Delete attribute values first
        var valueRepo = _unitOfWork.Repository<StoreProductAttributeValue>();
        foreach (var value in attribute.values.ToList())
        {
            await valueRepo.DeleteAsync(value);
        }

        await attrRepo.DeleteAsync(attribute);

        return Ok(new { success = true, deletedValuesCount = attribute.values.Count });
    }

    /// <summary>Updates selected definitions atomically, preserving unspecified settings.</summary>
    [HttpPatch("/store-attributes/bulk")]
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> BulkUpdateAttributes([FromBody] AttributeBulkUpdateForm input)
    {
        var ids = input.GetValidatedIds();
        input.ValidateChanges();
        var storeId = await _currentUserService.GetSelectedStoreIdAsync();
        if (storeId == null)
            throw ApiException.BadRequest(code: "store_not_selected");
        var repo = _unitOfWork.Repository<StoreAttribute>();
        var attributes = await repo.FindAllAsync(
            a => a.storeId == storeId.Value && ids.Contains(a.Id), query => query.AsTracking());
        if (attributes.Count != ids.Length)
            throw ApiException.NotFound(code: "store_attribute_not_found");

        foreach (var attribute in attributes)
        {
            input.ApplyTo(attribute);
        }
        await _unitOfWork.SaveChangesAsync();
        return Ok(new { updatedCount = attributes.Count });
    }

    /// <summary>Deletes selected definitions and their product values in one save.</summary>
    [HttpPost("/store-attributes/bulk-delete")]
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> BulkDeleteAttributes([FromBody] AttributeBulkSelectionForm input)
    {
        var ids = input.GetValidatedIds();
        var storeId = await _currentUserService.GetSelectedStoreIdAsync();
        if (storeId == null)
            throw ApiException.BadRequest(code: "store_not_selected");
        var repo = _unitOfWork.Repository<StoreAttribute>();
        var attributes = await repo.FindAllAsync(
            a => a.storeId == storeId.Value && ids.Contains(a.Id),
            query => query.Include(a => a.values));
        if (attributes.Count != ids.Length)
            throw ApiException.NotFound(code: "store_attribute_not_found");

        var values = attributes.SelectMany(a => a.values).ToList();
        _unitOfWork.Repository<StoreProductAttributeValue>().DeleteRange(values);
        repo.DeleteRange(attributes);
        await _unitOfWork.SaveChangesAsync();
        return Ok(new { deletedCount = attributes.Count, deletedValuesCount = values.Count });
    }

    [HttpPost]
    [Route("/store-attributes/reorder")]
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> ReorderStoreAttributes([FromBody] List<int> attributeIds)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var attrRepo = _unitOfWork.Repository<StoreAttribute>();

        var attributes = await attrRepo.FindAllAsync(
            a => a.storeId == selectedStoreId.Value);

        for (int i = 0; i < attributeIds.Count; i++)
        {
            var attr = attributes.FirstOrDefault(a => a.Id == attributeIds[i]);
            if (attr != null)
            {
                attr.sortOrder = i;
                await attrRepo.UpdateAsync(attr);
            }
        }

        return Ok(new { success = true });
    }
}
