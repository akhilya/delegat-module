using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Interfaces;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;

namespace Delegat.Controllers;

/// <summary>
/// Controller for managing product categories and their attributes.
/// </summary>
[ApiController]
[JwtAuthorize]
public class CategoryController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CategoryController> _logger;
    private readonly DataContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="CategoryController"/> class.
    /// </summary>
    /// <param name="currentUserService">Service for resolving the current authenticated user.</param>
    /// <param name="unitOfWork">Unit of work for database operations.</param>
    /// <param name="serviceProvider">Service provider for dependency resolution.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="context">Database context for direct query access.</param>
    public CategoryController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider,
        ILogger<CategoryController> logger,
        DataContext context)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _context = context;
    }

    /// <summary>
    /// Retrieves all categories for the current store, including product counts per category.
    /// </summary>
    /// <returns>A list of category DTOs ordered by name.</returns>
    [HttpGet]
    [Route("/categories")]
    public async Task<IActionResult> GetAllCategories()
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var categoryRepo = _unitOfWork.Repository<Category>();

        var categories = await categoryRepo.FindAllAsync(
            c => c.storeId == selectedStoreId.Value,
            query => query.Include(c => c.attributes));

        // Count products per category with a single GROUP BY query instead of loading all products
        var productCounts = await _context.products
            .Where(p => p.storeId == selectedStoreId.Value && p.categoryId != null)
            .GroupBy(p => p.categoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.CategoryId!.Value, g => g.Count);

        var result = categories
            .OrderBy(c => c.name)
            .Select(c =>
            {
                var dto = new Category.CategoryDTO
                {
                    id = c.Id,
                    name = c.name,
                    description = c.description,
                    createdAt = c.createdAt,
                    productsCount = productCounts.GetValueOrDefault(c.Id, 0),
                    attributesCount = c.attributes?.Count ?? 0
                };
                return dto;
            })
            .ToList();

        return Ok(result);
    }

    /// <summary>
    /// Retrieves a single category by its identifier, including attributes and product count.
    /// </summary>
    /// <param name="categoryId">The category identifier.</param>
    /// <returns>The category detail DTO.</returns>
    [HttpGet]
    [Route("/categories/{categoryId}")]
    public async Task<IActionResult> GetCategory(int categoryId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var categoryRepo = _unitOfWork.Repository<Category>();

        var category = await categoryRepo.FindAsync(
            c => c.Id == categoryId && c.storeId == selectedStoreId.Value,
            query => query.Include(c => c.attributes));

        if (category == null)
            throw ApiException.NotFound("Категория не найдена");

        var productsCount = await _context.products.CountAsync(p => p.categoryId == categoryId);

        var dto = new Category.CategoryDetailDTO
        {
            id = category.Id,
            name = category.name,
            description = category.description,
            createdAt = category.createdAt,
            attributes = category.attributes?
                .OrderBy(a => a.sortOrder)
                .Select(a => ((IMapToDTO<CategoryAttribute.AttributeDTO>)a).MapToDTO(_serviceProvider))
                .ToList() ?? new List<CategoryAttribute.AttributeDTO>(),
            productsCount = productsCount
        };

        return Ok(dto);
    }

    /// <summary>
    /// Creates a new category in the current store.
    /// </summary>
    /// <param name="input">The category creation form data.</param>
    /// <returns>The new category ID and a redirect path to its edit page.</returns>
    [HttpPost]
    [Route("/categories")]
    public async Task<IActionResult> CreateCategory([FromBody] Category.UserAddForm input)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var categoryRepo = _unitOfWork.Repository<Category>();

        // Check name uniqueness
        var existingCategory = await categoryRepo.FindAsync(
            c => c.storeId == selectedStoreId.Value && c.name == input.name);
        if (existingCategory != null)
            throw ApiException.BadRequest("Категория с таким именем уже существует");

        var category = new Category();
        category.AddFill(new Category.AddForm
        {
            name = input.name,
            description = input.description,
            storeId = selectedStoreId.Value
        });

        await categoryRepo.AddAsync(category);

        return Ok(new { id = category.Id, next = $"/categories/{category.Id}/edit" });
    }

    /// <summary>
    /// Updates an existing category's name and/or description.
    /// </summary>
    /// <param name="categoryId">The category identifier.</param>
    /// <param name="input">The fields to update.</param>
    /// <returns>A success indicator.</returns>
    [HttpPatch]
    [Route("/categories/{categoryId}")]
    public async Task<IActionResult> UpdateCategory(int categoryId, [FromBody] Category.UpdateForm input)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var categoryRepo = _unitOfWork.Repository<Category>();

        var category = await categoryRepo.FindAsync(
            c => c.Id == categoryId && c.storeId == selectedStoreId.Value);

        if (category == null)
            throw ApiException.NotFound("Категория не найдена");

        // Check name uniqueness if name is being changed
        if (!string.IsNullOrEmpty(input.name) && input.name != category.name)
        {
            var existingCategory = await categoryRepo.FindAsync(
                c => c.storeId == selectedStoreId.Value && c.name == input.name && c.Id != categoryId);
            if (existingCategory != null)
                throw ApiException.BadRequest("Категория с таким именем уже существует");
        }

        category.UpdateFill(input);
        await categoryRepo.UpdateAsync(category);

        return Ok(new { success = true });
    }

    /// <summary>
    /// Deletes a category and its attributes. Fails if the category still has products.
    /// </summary>
    /// <param name="categoryId">The category identifier.</param>
    /// <returns>A redirect path to the categories list.</returns>
    [HttpDelete]
    [Route("/categories/{categoryId}")]
    public async Task<IActionResult> DeleteCategory(int categoryId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var categoryRepo = _unitOfWork.Repository<Category>();

        var category = await categoryRepo.FindAsync(
            c => c.Id == categoryId && c.storeId == selectedStoreId.Value,
            query => query.Include(c => c.attributes));

        if (category == null)
            throw ApiException.NotFound("Категория не найдена");

        var activeCount = await _context.products.CountAsync(p => p.categoryId == categoryId);
        if (activeCount > 0)
            throw ApiException.BadRequest($"Невозможно удалить категорию с {activeCount} товарами. Сначала переместите или удалите товары.");

        // Trashed products are hidden by the global query filter, but their FK rows still
        // block the DELETE — detach them from the category (they stay in the trash)
        await _context.products.IgnoreQueryFilters()
            .Where(p => p.categoryId == categoryId && p.deletedAt != null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.categoryId, (int?)null));

        // Delete attributes and category in a single save
        var attrRepo = _unitOfWork.Repository<CategoryAttribute>();
        attrRepo.DeleteRange(category.attributes.ToList());
        categoryRepo.DeleteWithoutSave(category);
        await _unitOfWork.SaveChangesAsync();

        return Ok(new { next = "/categories" });
    }

    // Category Attributes endpoints

    /// <summary>
    /// Retrieves all attributes for a specific category, ordered by sort order.
    /// </summary>
    /// <param name="categoryId">The category identifier.</param>
    /// <returns>A list of attribute DTOs.</returns>
    [HttpGet]
    [Route("/categories/{categoryId}/attributes")]
    public async Task<IActionResult> GetCategoryAttributes(int categoryId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var categoryRepo = _unitOfWork.Repository<Category>();

        var category = await categoryRepo.FindAsync(
            c => c.Id == categoryId && c.storeId == selectedStoreId.Value,
            query => query.Include(c => c.attributes));

        if (category == null)
            throw ApiException.NotFound("Категория не найдена");

        var result = category.attributes
            .OrderBy(a => a.sortOrder)
            .Select(a => ((IMapToDTO<CategoryAttribute.AttributeDTO>)a).MapToDTO(_serviceProvider))
            .ToList();

        return Ok(result);
    }

    /// <summary>
    /// Creates a new attribute for a category.
    /// </summary>
    /// <param name="categoryId">The category identifier.</param>
    /// <param name="input">The attribute creation form data.</param>
    /// <returns>The new attribute ID.</returns>
    [HttpPost]
    [Route("/categories/{categoryId}/attributes")]
    public async Task<IActionResult> CreateAttribute(int categoryId, [FromBody] CategoryAttribute.UserAddForm input)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var categoryRepo = _unitOfWork.Repository<Category>();

        var category = await categoryRepo.FindAsync(
            c => c.Id == categoryId && c.storeId == selectedStoreId.Value,
            query => query.Include(c => c.attributes));

        if (category == null)
            throw ApiException.NotFound("Категория не найдена");

        // Check key uniqueness
        if (category.attributes.Any(a => a.key == input.key))
            throw ApiException.BadRequest("Атрибут с таким ключом уже существует");

        if (category.attributes.Any(a =>
                string.Equals(a.name, input.name, StringComparison.OrdinalIgnoreCase)))
            throw ApiException.BadRequest("Атрибут с таким названием уже существует");

        var attrRepo = _unitOfWork.Repository<CategoryAttribute>();

        // Get max sortOrder
        var maxSortOrder = category.attributes.Count > 0
            ? category.attributes.Max(a => a.sortOrder)
            : -1;

        var attribute = new CategoryAttribute();
        attribute.AddFill(new CategoryAttribute.AddForm
        {
            name = input.name,
            key = input.key,
            type = input.type,
            isRequired = input.isRequired,
            isMultiline = input.isMultiline,
            showOnProductPage = input.showOnProductPage,
            sortOrder = maxSortOrder + 1,
            options = input.options,
            defaultValue = input.defaultValue,
            description = input.description,
            descriptionInTooltip = input.descriptionInTooltip,
            group = input.group,
            categoryId = categoryId
        });

        await attrRepo.AddAsync(attribute);

        return Ok(new { id = attribute.Id });
    }

    /// <summary>
    /// Updates an existing category attribute.
    /// </summary>
    /// <param name="categoryId">The category identifier.</param>
    /// <param name="attributeId">The attribute identifier.</param>
    /// <param name="input">The fields to update.</param>
    /// <returns>A success indicator.</returns>
    [HttpPatch]
    [Route("/categories/{categoryId}/attributes/{attributeId}")]
    public async Task<IActionResult> UpdateAttribute(int categoryId, int attributeId, [FromBody] CategoryAttribute.UpdateForm input)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var categoryRepo = _unitOfWork.Repository<Category>();

        var category = await categoryRepo.FindAsync(
            c => c.Id == categoryId && c.storeId == selectedStoreId.Value);

        if (category == null)
            throw ApiException.NotFound("Категория не найдена");

        var attrRepo = _unitOfWork.Repository<CategoryAttribute>();

        var attribute = await attrRepo.FindAsync(
            a => a.Id == attributeId && a.categoryId == categoryId);

        if (attribute == null)
            throw ApiException.NotFound("Атрибут не найден");

        // Check key uniqueness if key is being changed
        if (!string.IsNullOrEmpty(input.key) && input.key != attribute.key)
        {
            var existingAttr = await attrRepo.FindAsync(
                a => a.categoryId == categoryId && a.key == input.key && a.Id != attributeId);
            if (existingAttr != null)
                throw ApiException.BadRequest("Атрибут с таким ключом уже существует");
        }

        attribute.UpdateFill(input);
        await attrRepo.UpdateAsync(attribute);

        return Ok(new { success = true });
    }

    /// <summary>
    /// Deletes a category attribute and all its associated product values.
    /// </summary>
    /// <param name="categoryId">The category identifier.</param>
    /// <param name="attributeId">The attribute identifier.</param>
    /// <returns>A success indicator.</returns>
    [HttpDelete]
    [Route("/categories/{categoryId}/attributes/{attributeId}")]
    public async Task<IActionResult> DeleteAttribute(int categoryId, int attributeId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var categoryRepo = _unitOfWork.Repository<Category>();

        var category = await categoryRepo.FindAsync(
            c => c.Id == categoryId && c.storeId == selectedStoreId.Value);

        if (category == null)
            throw ApiException.NotFound("Категория не найдена");

        var attrRepo = _unitOfWork.Repository<CategoryAttribute>();

        var attribute = await attrRepo.FindAsync(
            a => a.Id == attributeId && a.categoryId == categoryId,
            query => query.Include(a => a.values));

        if (attribute == null)
            throw ApiException.NotFound("Атрибут не найден");

        // Delete attribute values and attribute in a single save
        var valueRepo = _unitOfWork.Repository<ProductAttributeValue>();
        valueRepo.DeleteRange(attribute.values.ToList());
        attrRepo.DeleteWithoutSave(attribute);
        await _unitOfWork.SaveChangesAsync();

        return Ok(new { success = true });
    }

    /// <summary>
    /// Reorders category attributes based on the provided list of attribute IDs.
    /// </summary>
    /// <param name="categoryId">The category identifier.</param>
    /// <param name="attributeIds">Ordered list of attribute IDs defining the new sort order.</param>
    /// <returns>A success indicator.</returns>
    [HttpPost]
    [Route("/categories/{categoryId}/attributes/reorder")]
    public async Task<IActionResult> ReorderAttributes(int categoryId, [FromBody] List<int> attributeIds)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var categoryRepo = _unitOfWork.Repository<Category>();

        var category = await categoryRepo.FindAsync(
            c => c.Id == categoryId && c.storeId == selectedStoreId.Value,
            query => query.Include(c => c.attributes));

        if (category == null)
            throw ApiException.NotFound("Категория не найдена");

        var attrRepo = _unitOfWork.Repository<CategoryAttribute>();

        for (int i = 0; i < attributeIds.Count; i++)
        {
            var attr = category.attributes.FirstOrDefault(a => a.Id == attributeIds[i]);
            if (attr != null)
            {
                attr.sortOrder = i;
                attrRepo.UpdateWithoutSave(attr);
            }
        }

        await _unitOfWork.SaveChangesAsync();

        return Ok(new { success = true });
    }
}
