using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Interfaces;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Services;

namespace Delegat.Controllers;

/// <summary>
/// Controller for managing product categories and their attributes.
/// </summary>
[ApiController]
[JwtAuthorize]
public class CategoryController : ControllerBase
{
    private const int AttributeOptionMaxPageSize = 100;
    private const int AttributeOptionMaxSearchLength = 200;
    private const int AttributeOptionMaxSelectedJsonLength = 8192;
    private const int AttributeOptionMaxSelectedValues = 100;

    public sealed record AttributeOption(string Value, string Label);

    public sealed record AttributeOptionPage(
        IReadOnlyList<AttributeOption> Items,
        int Page,
        int PageSize,
        int Total,
        bool HasMore,
        IReadOnlyList<AttributeOption> SelectedOptions);

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
            throw ApiException.BadRequest(code: "store_not_selected");

        var categories = await _context.categories.AsNoTracking()
            .Where(c => c.storeId == selectedStoreId.Value)
            .OrderBy(c => c.name)
            .Select(c => new Category.CategoryDTO
            {
                id = c.Id, name = c.name, description = c.description,
                hasSizes = c.hasSizes, createdAt = c.createdAt,
                attributesCount = c.attributes.Count,
            }).ToListAsync();

        // Count products per category with a single GROUP BY query instead of loading all products
        var productCounts = await _context.products
            .Where(p => p.storeId == selectedStoreId.Value && p.categoryId != null)
            .GroupBy(p => p.categoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.CategoryId!.Value, g => g.Count);

        var result = categories.Select(dto =>
            {
                dto.productsCount = productCounts.GetValueOrDefault(dto.id, 0);
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
            throw ApiException.BadRequest(code: "store_not_selected");

        var categoryRepo = _unitOfWork.Repository<Category>();

        var category = await categoryRepo.FindAsync(
            c => c.Id == categoryId && c.storeId == selectedStoreId.Value,
            query => query.Include(c => c.attributes));

        if (category == null)
            throw ApiException.NotFound(code: "category_not_found");

        var productsCount = await _context.products.CountAsync(p => p.categoryId == categoryId);

        var dto = ((IMapToDTO<Category.CategoryDetailDTO>)category).MapToDTO(_serviceProvider);
        dto.productsCount = productsCount;

        return Ok(dto);
    }

    /// <summary>Retrieves category-editor metadata without loading any attribute dictionaries.</summary>
    [HttpGet("/categories/{categoryId}/summary")]
    public async Task<IActionResult> GetCategorySummary(int categoryId)
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync();
        if (storeId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var category = await _context.categories.AsNoTracking()
            .Where(c => c.Id == categoryId && c.storeId == storeId.Value)
            .Select(c => new Category.EditorSummaryDTO
            {
                id = c.Id, name = c.name, description = c.description, hasSizes = c.hasSizes,
            }).SingleOrDefaultAsync();
        if (category == null)
            throw ApiException.NotFound(code: "category_not_found");

        category.attributes = await CategoryAttributeSummaries(categoryId).ToListAsync();
        return Ok(category);
    }

    /// <summary>
    /// Creates a new category in the current store.
    /// </summary>
    /// <param name="input">The category creation form data.</param>
    /// <returns>The new category ID and a redirect path to its edit page.</returns>
    [HttpPost]
    [Route("/categories")]
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> CreateCategory([FromBody] Category.UserAddForm input)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var categoryRepo = _unitOfWork.Repository<Category>();

        // Check name uniqueness
        var existingCategory = await categoryRepo.FindAsync(
            c => c.storeId == selectedStoreId.Value && c.name == input.name);
        if (existingCategory != null)
            throw ApiException.BadRequest(code: "category_name_taken");

        var category = new Category();
        category.AddFill(new Category.AddForm
        {
            name = input.name,
            description = input.description,
            hasSizes = input.hasSizes,
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
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> UpdateCategory(int categoryId, [FromBody] Category.UpdateForm input)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var categoryRepo = _unitOfWork.Repository<Category>();

        var category = await categoryRepo.FindAsync(
            c => c.Id == categoryId && c.storeId == selectedStoreId.Value);

        if (category == null)
            throw ApiException.NotFound(code: "category_not_found");

        // Check name uniqueness if name is being changed
        if (!string.IsNullOrEmpty(input.name) && input.name != category.name)
        {
            var existingCategory = await categoryRepo.FindAsync(
                c => c.storeId == selectedStoreId.Value && c.name == input.name && c.Id != categoryId);
            if (existingCategory != null)
                throw ApiException.BadRequest(code: "category_name_taken");
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
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> DeleteCategory(int categoryId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var categoryRepo = _unitOfWork.Repository<Category>();

        var category = await categoryRepo.FindAsync(
            c => c.Id == categoryId && c.storeId == selectedStoreId.Value,
            query => query.Include(c => c.attributes));

        if (category == null)
            throw ApiException.NotFound(code: "category_not_found");

        var activeCount = await _context.products.CountAsync(p => p.categoryId == categoryId);
        if (activeCount > 0)
            throw new ApiException(
                statusCode: StatusCodes.Status400BadRequest,
                data: new { productCount = activeCount },
                code: "category_has_products");

        // Trashed products are hidden by the global query filter, but their FK rows still
        // block the DELETE - detach them from the category (they stay in the trash)
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

    /// <summary>Deletes selected empty categories atomically. Never deletes or moves active products.</summary>
    [HttpPost("/categories/bulk-delete")]
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> BulkDeleteCategories([FromBody] Category.BulkDeleteForm input)
    {
        if (input.categoryIds == null || input.categoryIds.Length == 0 ||
            input.categoryIds.Length > 1000 || input.categoryIds.Any(id => id <= 0))
            throw ApiException.BadRequest(code: "categories_selection_invalid");
        var ids = input.categoryIds.Distinct().ToArray();
        var storeId = await _currentUserService.GetSelectedStoreIdAsync();
        if (storeId == null)
            throw ApiException.BadRequest(code: "store_not_selected");
        var categories = await _context.categories
            .Where(c => c.storeId == storeId.Value && ids.Contains(c.Id)).ToListAsync();
        if (categories.Count != ids.Length)
            throw ApiException.NotFound(code: "category_not_found");
        var activeCount = await _context.products.CountAsync(p => p.storeId == storeId.Value &&
            p.categoryId != null && ids.Contains(p.categoryId.Value));
        if (activeCount > 0)
            throw new ApiException(data: new { productCount = activeCount }, code: "category_has_products");

        // Trashed products stay in the trash; detach their optional category FK in the same save.
        var trashed = await _context.products.IgnoreQueryFilters()
            .Where(p => p.storeId == storeId.Value && p.deletedAt != null &&
                p.categoryId != null && ids.Contains(p.categoryId.Value)).ToListAsync();
        foreach (var product in trashed) product.categoryId = null;

        // Keep the FK values on delete stubs so EF orders dependent deletes before parent
        // deletes; otherwise the database cascade can remove a row before EF's DELETE runs.
        // Select only keys and FKs: category dictionaries may be very large.
        var values = await _context.productAttributeValues.AsNoTracking()
            .Where(v => ids.Contains(v.attribute.categoryId))
            .Select(v => new ProductAttributeValue { Id = v.Id, attributeId = v.attributeId }).ToListAsync();
        var attributes = await _context.categoryAttributes.AsNoTracking()
            .Where(a => ids.Contains(a.categoryId))
            .Select(a => new CategoryAttribute { Id = a.Id, categoryId = a.categoryId }).ToListAsync();
        _context.productAttributeValues.RemoveRange(values);
        _context.categoryAttributes.RemoveRange(attributes);
        _context.categories.RemoveRange(categories);
        await _unitOfWork.SaveChangesAsync();
        return Ok(new { deletedCount = categories.Count });
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
            throw ApiException.BadRequest(code: "store_not_selected");

        var categoryRepo = _unitOfWork.Repository<Category>();

        var category = await categoryRepo.FindAsync(
            c => c.Id == categoryId && c.storeId == selectedStoreId.Value,
            query => query.Include(c => c.attributes));

        if (category == null)
            throw ApiException.NotFound(code: "category_not_found");

        var result = category.attributes
            .OrderBy(a => a.sortOrder)
            .Select(a => ((IMapToDTO<CategoryAttribute.AttributeDTO>)a).MapToDTO(_serviceProvider))
            .ToList();

        return Ok(result);
    }

    /// <summary>Retrieves lightweight attribute rows, excluding dictionary and validation payloads.</summary>
    [HttpGet("/categories/{categoryId}/attributes/summary")]
    public async Task<IActionResult> GetCategoryAttributeSummaries(int categoryId)
    {
        await RequireAttributeCategoryAsync(categoryId);
        return Ok(await CategoryAttributeSummaries(categoryId).ToListAsync());
    }

    /// <summary>Loads one complete definition only when its editor needs it.</summary>
    [HttpGet("/categories/{categoryId}/attributes/{attributeId:int}")]
    public async Task<IActionResult> GetCategoryAttribute(int categoryId, int attributeId)
    {
        await RequireAttributeCategoryAsync(categoryId);
        var attribute = await _unitOfWork.Repository<CategoryAttribute>()
            .FindAsync(a => a.Id == attributeId && a.categoryId == categoryId);
        if (attribute == null)
            throw ApiException.NotFound(code: "attribute_not_found");
        return Ok(((IMapToDTO<CategoryAttribute.AttributeDTO>)attribute).MapToDTO(_serviceProvider));
    }

    /// <summary>Returns a bounded search page from a select attribute's JSON dictionary.</summary>
    [HttpGet("/categories/{categoryId}/attributes/{attributeId:int}/options")]
    public async Task<IActionResult> GetCategoryAttributeOptions(
        int categoryId,
        int attributeId,
        [FromQuery] string? query = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? selectedValues = null)
    {
        await RequireAttributeCategoryAsync(categoryId);

        var attribute = await _context.categoryAttributes.AsNoTracking()
            .Where(a => a.Id == attributeId && a.categoryId == categoryId)
            .Select(a => new { a.type, a.options })
            .FirstOrDefaultAsync();
        if (attribute == null)
            throw ApiException.NotFound(code: "attribute_not_found");
        if (attribute.type is not (Delegat.Enums.AttributeType.Select or Delegat.Enums.AttributeType.MultiSelect))
            throw ApiException.BadRequest(code: "attribute_options_not_available");

        var selected = ParseSelectedOptionValues(selectedValues);
        var dictionaryOptions = ParseAttributeOptions(attribute.options);
        var allOptions = dictionaryOptions;
        var normalizedQuery = query?.Trim();
        if (!string.IsNullOrEmpty(normalizedQuery))
        {
            normalizedQuery = normalizedQuery[..Math.Min(normalizedQuery.Length, AttributeOptionMaxSearchLength)];
            allOptions = allOptions.Where(option =>
                    option.Label.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                    || option.Value.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, AttributeOptionMaxPageSize);
        var total = allOptions.Count;
        var offset = (long)(page - 1) * pageSize;
        var items = offset >= total
            ? []
            : allOptions.Skip((int)offset).Take(pageSize).ToList();

        var selectedOptions = selected.Select(value =>
        {
            var match = dictionaryOptions.FirstOrDefault(option =>
                string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase)
                || string.Equals(option.Label, value, StringComparison.OrdinalIgnoreCase));
            return new AttributeOption(value, match?.Label ?? value);
        }).ToList();

        return Ok(new AttributeOptionPage(
            items,
            page,
            pageSize,
            total,
            offset + items.Count < total,
            selectedOptions));
    }

    private static List<string> ParseSelectedOptionValues(string? selectedValues)
    {
        if (string.IsNullOrWhiteSpace(selectedValues))
            return [];
        if (selectedValues.Length > AttributeOptionMaxSelectedJsonLength)
            throw ApiException.BadRequest(code: "attribute_options_selection_invalid");

        try
        {
            using var document = JsonDocument.Parse(selectedValues);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw ApiException.BadRequest(code: "attribute_options_selection_invalid");
            var values = document.RootElement.EnumerateArray()
                .Where(value => value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                .Select(value => value.ToString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
            if (values.Count > AttributeOptionMaxSelectedValues
                || values.Any(value => value.Length > 1024))
                throw ApiException.BadRequest(code: "attribute_options_selection_invalid");
            return values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (JsonException)
        {
            throw ApiException.BadRequest(code: "attribute_options_selection_invalid");
        }
    }

    private static List<AttributeOption> ParseAttributeOptions(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson))
            return [];

        try
        {
            using var document = JsonDocument.Parse(optionsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            var options = new List<AttributeOption>();
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                string? value;
                string? label;
                if (entry.ValueKind == JsonValueKind.Object)
                {
                    value = ReadOptionProperty(entry, "value") ?? ReadOptionProperty(entry, "Value");
                    label = ReadOptionProperty(entry, "label") ?? ReadOptionProperty(entry, "Label");
                }
                else if (entry.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                {
                    value = entry.ToString();
                    label = value;
                }
                else
                {
                    continue;
                }

                value = string.IsNullOrEmpty(value) ? label : value;
                label = string.IsNullOrEmpty(label) ? value : label;
                if (!string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(label))
                    options.Add(new AttributeOption(value, label));
            }
            return options;
        }
        catch (JsonException)
        {
            return optionsJson.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(value => new AttributeOption(value, value))
                .ToList();
        }
    }

    private static string? ReadOptionProperty(JsonElement entry, string name)
    {
        foreach (var property in entry.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.ToString();
        }
        return null;
    }

    private IQueryable<AttributeDefinitionSummary> CategoryAttributeSummaries(int categoryId) =>
        _context.categoryAttributes.AsNoTracking().Where(a => a.categoryId == categoryId)
            .OrderBy(a => a.sortOrder).ThenBy(a => a.Id).Select(AttributeDefinitionSummary.FromCategory);

    /// <summary>
    /// Creates a new attribute for a category.
    /// </summary>
    /// <param name="categoryId">The category identifier.</param>
    /// <param name="input">The attribute creation form data.</param>
    /// <returns>The new attribute ID.</returns>
    [HttpPost]
    [Route("/categories/{categoryId}/attributes")]
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> CreateAttribute(int categoryId, [FromBody] CategoryAttribute.UserAddForm input)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var categoryRepo = _unitOfWork.Repository<Category>();

        var category = await categoryRepo.FindAsync(
            c => c.Id == categoryId && c.storeId == selectedStoreId.Value,
            query => query.Include(c => c.attributes));

        if (category == null)
            throw ApiException.NotFound(code: "category_not_found");

        // Check key uniqueness
        if (category.attributes.Any(a => a.key == input.key))
            throw ApiException.BadRequest(code: "attribute_key_taken");

        if (category.attributes.Any(a =>
                string.Equals(a.name, input.name, StringComparison.OrdinalIgnoreCase)))
            throw ApiException.BadRequest(code: "attribute_name_taken");

        var attrRepo = _unitOfWork.Repository<CategoryAttribute>();

        // Get max sortOrder
        var maxSortOrder = category.attributes.Count > 0
            ? category.attributes.Max(a => a.sortOrder)
            : -1;

        var attribute = new CategoryAttribute();
        if (!string.IsNullOrWhiteSpace(input.unitCode) && input.type != Delegat.Enums.AttributeType.Number)
            throw ApiException.BadRequest(code: "attribute_unit_requires_number");
        AttributeDefinitionFileRules.Validate(
            input.type, input.allowMultipleFiles, input.isUnique, input.defaultValue);
        attribute.AddFill(new CategoryAttribute.AddForm
        {
            name = input.name,
            key = input.key,
            type = input.type,
            unitCode = input.unitCode,
            allowMultipleFiles = input.allowMultipleFiles,
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
            group = input.group,
            groupIcon = input.groupIcon,
            maxLength = input.maxLength,
            minValue = input.minValue,
            maxValue = input.maxValue,
            maxValues = input.maxValues,
            validationPattern = input.validationPattern,
            categoryId = categoryId
        });

        await attrRepo.AddAsync(attribute);
        await SyncAttributeGroupIconAsync(categoryId, attribute.group, attribute.groupIcon);

        return Ok(new { id = attribute.Id });
    }

    /// <summary>
    /// Updates an existing category attribute.
    /// </summary>
    /// <param name="categoryId">The category identifier.</param>
    /// <param name="attributeId">The attribute identifier.</param>
    /// <param name="input">The fields to update.</param>
    /// <param name="ct">Cancels the update.</param>
    /// <returns>A success indicator.</returns>
    [HttpPatch]
    [Route("/categories/{categoryId}/attributes/{attributeId}")]
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> UpdateAttribute(
        int categoryId,
        int attributeId,
        [FromBody] CategoryAttribute.UpdateForm input,
        CancellationToken ct)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var categoryRepo = _unitOfWork.Repository<Category>();

        var category = await categoryRepo.FindAsync(
            c => c.Id == categoryId && c.storeId == selectedStoreId.Value);

        if (category == null)
            throw ApiException.NotFound(code: "category_not_found");

        var attrRepo = _unitOfWork.Repository<CategoryAttribute>();

        var attribute = await attrRepo.FindAsync(
            a => a.Id == attributeId && a.categoryId == categoryId);

        if (attribute == null)
            throw ApiException.NotFound(code: "attribute_not_found");

        // Check key uniqueness if key is being changed
        if (!string.IsNullOrEmpty(input.key) && input.key != attribute.key)
        {
            var existingAttr = await attrRepo.FindAsync(
                a => a.categoryId == categoryId && a.key == input.key && a.Id != attributeId);
            if (existingAttr != null)
                throw ApiException.BadRequest(code: "attribute_key_taken");
        }

        await AttributeDefinitionFileRules.ValidateCategoryUpdateAsync(_context, attribute, input, ct);
        attribute.UpdateFill(input);
        ValidateAttributeUnit(attribute.type, attribute.unitCode);
        await attrRepo.UpdateAsync(attribute);
        await SyncAttributeGroupIconAsync(categoryId, attribute.group, attribute.groupIcon);

        return Ok(new { success = true });
    }

    private static void ValidateAttributeUnit(Delegat.Enums.AttributeType type, string? unitCode)
    {
        if (!string.IsNullOrWhiteSpace(unitCode) && type != Delegat.Enums.AttributeType.Number)
            throw ApiException.BadRequest(code: "attribute_unit_requires_number");
    }

    /// <summary>
    /// Deletes a category attribute and all its associated product values.
    /// </summary>
    /// <param name="categoryId">The category identifier.</param>
    /// <param name="attributeId">The attribute identifier.</param>
    /// <returns>A success indicator.</returns>
    [HttpDelete]
    [Route("/categories/{categoryId}/attributes/{attributeId}")]
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> DeleteAttribute(int categoryId, int attributeId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var categoryRepo = _unitOfWork.Repository<Category>();

        var category = await categoryRepo.FindAsync(
            c => c.Id == categoryId && c.storeId == selectedStoreId.Value);

        if (category == null)
            throw ApiException.NotFound(code: "category_not_found");

        var attrRepo = _unitOfWork.Repository<CategoryAttribute>();

        var attribute = await attrRepo.FindAsync(
            a => a.Id == attributeId && a.categoryId == categoryId,
            query => query.Include(a => a.values));

        if (attribute == null)
            throw ApiException.NotFound(code: "attribute_not_found");

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
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> ReorderAttributes(int categoryId, [FromBody] List<int> attributeIds)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var categoryRepo = _unitOfWork.Repository<Category>();

        var category = await categoryRepo.FindAsync(
            c => c.Id == categoryId && c.storeId == selectedStoreId.Value,
            query => query.Include(c => c.attributes));

        if (category == null)
            throw ApiException.NotFound(code: "category_not_found");

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

    /// <summary>Updates selected definitions atomically without changing other settings or values.</summary>
    [HttpPatch("/categories/{categoryId}/attributes/bulk")]
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> BulkUpdateAttributes(int categoryId, [FromBody] AttributeBulkUpdateForm input)
    {
        var ids = input.GetValidatedIds();
        input.ValidateChanges();
        await RequireAttributeCategoryAsync(categoryId);
        var repo = _unitOfWork.Repository<CategoryAttribute>();
        var attributes = await repo.FindAllAsync(
            a => a.categoryId == categoryId && ids.Contains(a.Id), query => query.AsTracking());
        if (attributes.Count != ids.Length)
            throw ApiException.NotFound(code: "attribute_not_found");

        foreach (var attribute in attributes)
        {
            AttributeDefinitionFileRules.Validate(
                attribute.type,
                attribute.type == Delegat.Enums.AttributeType.File && attribute.allowMultipleFiles,
                input.isUnique ?? attribute.isUnique,
                attribute.defaultValue);
            input.ApplyTo(attribute);
        }
        await _unitOfWork.SaveChangesAsync();
        return Ok(new { updatedCount = attributes.Count });
    }

    /// <summary>Deletes selected definitions and their product values in one save.</summary>
    [HttpPost("/categories/{categoryId}/attributes/bulk-delete")]
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> BulkDeleteAttributes(int categoryId, [FromBody] AttributeBulkSelectionForm input)
    {
        var ids = input.GetValidatedIds();
        await RequireAttributeCategoryAsync(categoryId);
        var repo = _unitOfWork.Repository<CategoryAttribute>();
        var attributes = await repo.FindAllAsync(
            a => a.categoryId == categoryId && ids.Contains(a.Id),
            query => query.Include(a => a.values));
        if (attributes.Count != ids.Length)
            throw ApiException.NotFound(code: "attribute_not_found");

        var values = attributes.SelectMany(a => a.values).ToList();
        _unitOfWork.Repository<ProductAttributeValue>().DeleteRange(values);
        repo.DeleteRange(attributes);
        await _unitOfWork.SaveChangesAsync();
        return Ok(new { deletedCount = attributes.Count, deletedValuesCount = values.Count });
    }

    private async Task RequireAttributeCategoryAsync(int categoryId)
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync();
        if (storeId == null)
            throw ApiException.BadRequest(code: "store_not_selected");
        var category = await _unitOfWork.Repository<Category>()
            .FindAsync(c => c.Id == categoryId && c.storeId == storeId.Value);
        if (category == null)
            throw ApiException.NotFound(code: "category_not_found");
    }

    private async Task SyncAttributeGroupIconAsync(int categoryId, string? group, string? groupIcon)
    {
        if (string.IsNullOrEmpty(group))
            return;

        var rows = await _context.categoryAttributes
            .Where(a => a.categoryId == categoryId && a.group == group)
            .ToListAsync();
        foreach (var row in rows)
            row.groupIcon = groupIcon;
        await _context.SaveChangesAsync();
    }
}
