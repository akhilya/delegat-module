using Microsoft.EntityFrameworkCore;
using Delegat.Models;
using Delegat.Services.Ai.Tools;

namespace Delegat.Services.Ai.Tools.Handlers;

public class GroupToolHandlers : ToolHandlerBase
{
    private readonly DataContext _context;
    private readonly AiOperationService _operationService;
    private readonly ILogger<GroupToolHandlers> _logger;

    public GroupToolHandlers(
        DataContext context,
        AiOperationService operationService,
        ILogger<GroupToolHandlers> logger)
    {
        _context = context;
        _operationService = operationService;
        _logger = logger;
    }

    internal async Task<(object, string)> ListGroupsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ListGroupsInput>(json);
        var q = _context.productGroups.AsNoTracking()
            .Where(g => g.storeId == storeId);
        if (!string.IsNullOrEmpty(p.Search))
            q = q.Where(g => g.name.Contains(p.Search));
        if (p.ProductId.HasValue)
            q = q.Where(g => g.members.Any(m => m.productId == p.ProductId));
        var groups = await q
            .Select(g => new { g.Id, g.name, g.description, memberCount = g.members.Count })
            .ToListAsync(ct);
        return (new { groups, count = groups.Count }, $"{groups.Count} групп");
    }

    internal async Task<(object, string)> GetGroupAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var group = await _context.productGroups.AsNoTracking()
            .Where(g => g.Id == p.Id && g.storeId == storeId)
            .Select(g => new
            {
                g.Id, g.name, g.description,
                members = g.members.OrderBy(m => m.sortOrder).Select(m => new
                {
                    m.productId,
                    m.product.name,
                    m.product.sku,
                    m.product.price,
                    quantity = m.product.storageQuantity,
                }).ToList()
            })
            .FirstOrDefaultAsync(ct);
        if (group == null) return (new { error = "Группа не найдена" }, "Не найдена");
        return (group, $"Группа \"{group.name}\": {group.members.Count} товаров");
    }

    internal async Task<(object, string)> CreateGroupAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<CreateGroupInput>(json);
        var group = new ProductGroup { name = p.Name, storeId = storeId };
        _context.productGroups.Add(group);
        await _context.SaveChangesAsync(ct);
        if (p.ProductIds?.Count > 0)
        {
            foreach (var pid in p.ProductIds)
                _context.productGroupMembers.Add(new ProductGroupMember { groupId = group.Id, productId = pid });
            await _context.SaveChangesAsync(ct);
        }
        return (new { id = group.Id }, $"Группа \"{p.Name}\" создана");
    }

    internal async Task<(object, string)> UpdateGroupAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<UpdateGroupInput>(json);
        var group = await _context.productGroups
            .FirstOrDefaultAsync(g => g.Id == p.Id && g.storeId == storeId, ct);
        if (group == null) return (new { error = "Группа не найдена" }, "Не найдена");

        var oldName = group.name;
        if (!string.IsNullOrEmpty(p.Name)) group.name = p.Name;
        if (p.Description != null) group.description = p.Description;
        group.updatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        await _operationService.RecordAsync(storeId, userId, "update_group",
            $"Группа \"{oldName}\" → \"{group.name}\"", new { }, canRollback: false, ct);
        return (new { success = true }, $"Группа \"{group.name}\" обновлена");
    }

    internal async Task<(object, string)> DeleteGroupAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var group = await _context.productGroups
            .Include(g => g.members)
            .FirstOrDefaultAsync(g => g.Id == p.Id && g.storeId == storeId, ct);
        if (group == null) return (new { error = "Группа не найдена" }, "Не найдена");

        var name = group.name;
        var memberCount = group.members.Count;
        _context.productGroups.Remove(group);
        await _context.SaveChangesAsync(ct);

        await _operationService.RecordAsync(storeId, userId, "delete_group",
            $"Удалена группа \"{name}\" ({memberCount} товаров остались в магазине)", new { }, canRollback: false, ct);
        return (new { success = true }, $"Группа \"{name}\" удалена, товары остались");
    }

    internal async Task<(object, string)> AddProductsToGroupAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<GroupProductsInput>(json);
        var existing = (await _context.productGroupMembers.AsNoTracking()
            .Where(m => m.groupId == p.GroupId && p.ProductIds.Contains(m.productId))
            .Select(m => m.productId).ToListAsync(ct)).ToHashSet();
        var toAdd = p.ProductIds.Where(id => !existing.Contains(id)).ToList();
        foreach (var pid in toAdd)
            _context.productGroupMembers.Add(new ProductGroupMember { groupId = p.GroupId, productId = pid });
        await _context.SaveChangesAsync(ct);
        return (new { added = toAdd.Count }, $"Добавлено {toAdd.Count} товаров в группу");
    }

    internal async Task<(object, string)> RemoveProductsFromGroupAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<GroupProductsInput>(json);
        var groupExists = await _context.productGroups.AsNoTracking()
            .AnyAsync(g => g.Id == p.GroupId && g.storeId == storeId, ct);
        if (!groupExists) return (new { error = "Группа не найдена" }, "Не найдена");

        var members = await _context.productGroupMembers
            .Where(m => m.groupId == p.GroupId && p.ProductIds.Contains(m.productId))
            .ToListAsync(ct);
        _context.productGroupMembers.RemoveRange(members);
        await _context.SaveChangesAsync(ct);

        await _operationService.RecordAsync(storeId, userId, "remove_from_group",
            $"Из группы {p.GroupId} убрано {members.Count} товаров", new { }, canRollback: false, ct);
        return (new { removed = members.Count }, $"Убрано {members.Count} товаров из группы");
    }
}

// ---- Group-specific DTOs ----

internal class ListGroupsInput { public string? Search { get; set; } public int? ProductId { get; set; } }
internal class UpdateGroupInput { public int Id { get; set; } public string? Name { get; set; } public string? Description { get; set; } }
internal class CreateGroupInput { public string Name { get; set; } = ""; public List<int>? ProductIds { get; set; } }
internal class GroupProductsInput { public int GroupId { get; set; } public List<int> ProductIds { get; set; } = []; }
