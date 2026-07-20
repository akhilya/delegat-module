using Microsoft.EntityFrameworkCore;
using Delegat.Enums;
using Delegat.Models;
using Delegat.Services.Ai.Tools;

namespace Delegat.Services.Ai.Tools.Handlers;

public class CategoryToolHandlers : ToolHandlerBase
{
    private readonly DataContext _context;
    private readonly AiOperationService _operationService;
    private readonly ILogger<CategoryToolHandlers> _logger;

    public CategoryToolHandlers(
        DataContext context,
        AiOperationService operationService,
        ILogger<CategoryToolHandlers> logger)
    {
        _context = context;
        _operationService = operationService;
        _logger = logger;
    }

    internal async Task<(object, string)> ListCategoriesAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var cats = await _context.categories.AsNoTracking()
            .Where(c => c.storeId == storeId)
            .Include(c => c.attributes)
            .OrderBy(c => c.name)
            .Select(c => new { c.Id, c.name, c.description, attributeCount = c.attributes.Count })
            .ToListAsync(ct);
        return (new { categories = cats, count = cats.Count }, $"{cats.Count} categories");
    }

    internal async Task<(object, string)> GetCategoryAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var cat = await _context.categories.AsNoTracking()
            .Include(c => c.attributes)
            .FirstOrDefaultAsync(c => c.Id == p.Id && c.storeId == storeId, ct);
        if (cat == null) return (new { error = "Category not found" }, "Category not found");
        return (new
        {
            cat.Id, cat.name, cat.description,
            attributes = cat.attributes.OrderBy(a => a.sortOrder)
                .Select(a => new
                {
                    a.Id, a.name, a.key,
                    type = a.type.ToString(),
                    a.isRequired,
                    options = ParseAttributeOptions(a.options),
                })
        }, cat.name);
    }

    internal async Task<(object, string)> CreateCategoryAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<CreateCategoryInput>(json);
        var cat = new Category { name = p.Name, description = p.Description, storeId = storeId };
        _context.categories.Add(cat);
        await _context.SaveChangesAsync(ct);
        return (new { id = cat.Id }, $"Category \"{p.Name}\" created");
    }

    internal async Task<(object, string)> UpdateCategoryAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<UpdateCategoryInput>(json);
        var cat = await _context.categories.FirstOrDefaultAsync(c => c.Id == p.Id && c.storeId == storeId, ct);
        if (cat == null) return (new { error = "Category not found" }, "Category not found");
        if (p.Name != null) cat.name = p.Name;
        if (p.Description != null) cat.description = p.Description;
        await _context.SaveChangesAsync(ct);
        return (new { success = true }, $"Category updated");
    }

    internal async Task<(object, string)> DeleteCategoryAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var cat = await _context.categories.FirstOrDefaultAsync(c => c.Id == p.Id && c.storeId == storeId, ct);
        if (cat == null) return (new { error = "Category not found" }, "Category not found");

        var activeCount = await _context.products.CountAsync(x => x.categoryId == p.Id, ct);
        if (activeCount > 0)
            return (new { error = $"The category has {activeCount} products — move or delete them first" },
                $"Cannot delete: category has {activeCount} products");

        // Trashed products are hidden by the global query filter, but their FK rows still
        // block the DELETE — detach them from the category (they stay in the trash)
        await _context.products.IgnoreQueryFilters()
            .Where(x => x.categoryId == p.Id && x.deletedAt != null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.categoryId, (int?)null), ct);

        _context.categories.Remove(cat);
        await _context.SaveChangesAsync(ct);
        return (new { success = true }, $"Category \"{cat.name}\" deleted");
    }

    internal async Task<(object, string)> ListCategoryFieldsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var raw = await _context.categoryAttributes.AsNoTracking()
            .Where(a => a.categoryId == p.Id && a.category.storeId == storeId)
            .OrderBy(a => a.sortOrder)
            .Select(a => new { a.Id, a.name, a.key, a.type, a.isRequired, a.group, a.options })
            .ToListAsync(ct);
        var fields = raw.Select(a => new
        {
            a.Id, a.name, a.key,
            type = a.type.ToString(),
            a.isRequired, a.group,
            options = ParseAttributeOptions(a.options),
        }).ToList();
        return (new { fields, count = fields.Count }, $"{fields.Count} fields");
    }

    internal async Task<(object, string)> AddCategoryFieldAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<AddFieldInput>(json);
        var cat = await _context.categories.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == p.CategoryId && c.storeId == storeId, ct);
        if (cat == null) return (new { error = "Category not found" }, "Category not found");

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
            $"Added field \"{p.Name}\" to category {p.CategoryId}",
            AiOperationService.BuildFieldRollback(attr.Id), ct: ct);
        return (new { id = attr.Id, key }, $"Field \"{p.Name}\" added");
    }

    internal async Task<(object, string)> UpdateCategoryFieldAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<UpdateFieldInput>(json);
        var attr = await _context.categoryAttributes
            .FirstOrDefaultAsync(a => a.Id == p.FieldId && a.category.storeId == storeId, ct);
        if (attr == null) return (new { error = "Field not found" }, "Field not found");
        if (p.Name != null) attr.name = p.Name;
        if (p.IsRequired.HasValue) attr.isRequired = p.IsRequired.Value;
        await _context.SaveChangesAsync(ct);
        return (new { success = true }, $"Field updated");
    }

    internal async Task<(object, string)> DeleteCategoryFieldAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<FieldIdInput>(json);
        var attr = await _context.categoryAttributes
            .FirstOrDefaultAsync(a => a.Id == p.FieldId && a.category.storeId == storeId, ct);
        if (attr == null) return (new { error = "Field not found" }, "Field not found");
        _context.categoryAttributes.Remove(attr);
        await _context.SaveChangesAsync(ct);
        return (new { success = true }, $"Field \"{attr.name}\" deleted");
    }

    internal async Task<(object, string)> FindCategoriesWithFieldAsync(string json, int storeId, int userId, CancellationToken ct)
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
        return (new { categories = cats, count = cats.Count }, $"Found {cats.Count} categories with field '{p.Search}'");
    }

    // Select/MultiSelect options are stored as a JSON string — parse so the model sees real values
    private static object? ParseAttributeOptions(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson)) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<object>(optionsJson); }
        catch { return optionsJson; }
    }
}

// ---- Category-specific DTOs ----

internal class CreateCategoryInput { public string Name { get; set; } = ""; public string? Description { get; set; } }
internal class UpdateCategoryInput { public int Id { get; set; } public string? Name { get; set; } public string? Description { get; set; } }
internal class AddFieldInput { public int CategoryId { get; set; } public string Name { get; set; } = ""; public string? Key { get; set; } public string Type { get; set; } = "Text"; public bool? IsRequired { get; set; } public string? Group { get; set; } }
internal class UpdateFieldInput { public int FieldId { get; set; } public string? Name { get; set; } public bool? IsRequired { get; set; } }
internal class FieldIdInput { public int FieldId { get; set; } }
