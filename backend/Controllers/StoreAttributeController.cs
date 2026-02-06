using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrintO.Infrastructure.Auth;
using PrintO.Infrastructure.Data;
using PrintO.Infrastructure.Interfaces;
using PrintO.Infrastructure.Middleware;
using PrintO.Models;

namespace PrintO.Controllers;

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
            throw ApiException.BadRequest("Магазин не выбран");

        var attrRepo = _unitOfWork.Repository<StoreAttribute>();

        var attributes = await attrRepo.FindAllAsync(
            a => a.storeId == selectedStoreId.Value);

        var result = attributes
            .OrderBy(a => a.sortOrder)
            .Select(a => ((IMapToDTO<StoreAttribute.AttributeDTO>)a).MapToDTO(_serviceProvider))
            .ToList();

        return Ok(result);
    }

    [HttpGet]
    [Route("/store-attributes/{attributeId}")]
    public async Task<IActionResult> GetStoreAttribute(int attributeId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var attrRepo = _unitOfWork.Repository<StoreAttribute>();

        var attribute = await attrRepo.FindAsync(
            a => a.Id == attributeId && a.storeId == selectedStoreId.Value);

        if (attribute == null)
            throw ApiException.NotFound("Атрибут магазина не найден");

        return Ok(((IMapToDTO<StoreAttribute.AttributeDTO>)attribute).MapToDTO(_serviceProvider));
    }

    [HttpPost]
    [Route("/store-attributes")]
    public async Task<IActionResult> CreateStoreAttribute([FromBody] StoreAttribute.UserAddForm input)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var attrRepo = _unitOfWork.Repository<StoreAttribute>();

        // Check key uniqueness within this store
        var existingAttr = await attrRepo.FindAsync(
            a => a.storeId == selectedStoreId.Value && a.key == input.key);
        if (existingAttr != null)
            throw ApiException.BadRequest("Атрибут с таким ключом уже существует");

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
            sortOrder = maxSortOrder + 1,
            options = input.options,
            defaultValue = input.defaultValue,
            storeId = selectedStoreId.Value
        });

        await attrRepo.AddAsync(attribute);

        return Ok(new { id = attribute.Id });
    }

    [HttpPatch]
    [Route("/store-attributes/{attributeId}")]
    public async Task<IActionResult> UpdateStoreAttribute(int attributeId, [FromBody] StoreAttribute.UpdateForm input)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var attrRepo = _unitOfWork.Repository<StoreAttribute>();

        var attribute = await attrRepo.FindAsync(
            a => a.Id == attributeId && a.storeId == selectedStoreId.Value);

        if (attribute == null)
            throw ApiException.NotFound("Атрибут магазина не найден");

        // Check key uniqueness if key is being changed
        if (!string.IsNullOrEmpty(input.key) && input.key != attribute.key)
        {
            var existingAttr = await attrRepo.FindAsync(
                a => a.storeId == selectedStoreId.Value && a.key == input.key && a.Id != attributeId);
            if (existingAttr != null)
                throw ApiException.BadRequest("Атрибут с таким ключом уже существует");
        }

        attribute.UpdateFill(input);
        await attrRepo.UpdateAsync(attribute);

        return Ok(new { success = true });
    }

    [HttpDelete]
    [Route("/store-attributes/{attributeId}")]
    public async Task<IActionResult> DeleteStoreAttribute(int attributeId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var attrRepo = _unitOfWork.Repository<StoreAttribute>();

        var attribute = await attrRepo.FindAsync(
            a => a.Id == attributeId && a.storeId == selectedStoreId.Value,
            query => query.Include(a => a.values));

        if (attribute == null)
            throw ApiException.NotFound("Атрибут магазина не найден");

        // Delete attribute values first
        var valueRepo = _unitOfWork.Repository<StoreProductAttributeValue>();
        foreach (var value in attribute.values.ToList())
        {
            await valueRepo.DeleteAsync(value);
        }

        await attrRepo.DeleteAsync(attribute);

        return Ok(new { success = true, deletedValuesCount = attribute.values.Count });
    }

    [HttpPost]
    [Route("/store-attributes/reorder")]
    public async Task<IActionResult> ReorderStoreAttributes([FromBody] List<int> attributeIds)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

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
