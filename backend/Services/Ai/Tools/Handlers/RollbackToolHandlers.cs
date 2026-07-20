using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Delegat.Models;
using Delegat.Models.Ai;
using Delegat.Models.Products;
using Delegat.Services.Ai.Tools;

namespace Delegat.Services.Ai.Tools.Handlers;

public class RollbackToolHandlers : ToolHandlerBase
{
    private readonly DataContext _context;
    private readonly AiOperationService _operationService;
    private readonly ILogger<RollbackToolHandlers> _logger;

    // RecordAsync historically wrote rollback JSON in PascalCase (no naming policy) while this
    // file deserialized expecting camelCase — every rollback silently no-op'ed. Case-insensitive
    // matching reads both old and new rows.
    private static readonly JsonSerializerOptions _rollbackJson = new() { PropertyNameCaseInsensitive = true };

    public RollbackToolHandlers(
        DataContext context,
        AiOperationService operationService,
        ILogger<RollbackToolHandlers> logger)
    {
        _context = context;
        _operationService = operationService;
        _logger = logger;
    }

    internal async Task<(object, string)> RollbackOperationAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<OptionalIdInput>(json);
        AiOperation? op;
        if (p.Id.HasValue)
            op = await _operationService.GetByIdAsync(p.Id.Value, userId, ct);
        else
            op = await _operationService.GetLastAsync(userId, storeId, ct);

        if (op == null) return (new { error = "No operations to undo" }, "No operations to undo");
        if (!op.canRollback || op.status == AiOperationStatus.RolledBack)
            return (new { error = "This operation cannot be undone" }, "Cannot undo");

        // Stale guard: without an explicit operationId, don't silently undo something old —
        // it may belong to a long-finished task the user no longer has in mind
        if (!p.Id.HasValue && op.createdAt < DateTime.UtcNow.AddMinutes(-30))
        {
            var age = DateTime.UtcNow - op.createdAt;
            var ageText = age.TotalHours >= 1 ? $"{(int)age.TotalHours} h ago" : $"{(int)age.TotalMinutes} min ago";
            return (new
            {
                requiresConfirmation = true,
                operationId = op.Id,
                description = op.description,
                createdAt = op.createdAt,
            }, $"The last operation was performed {ageText}: \"{op.description}\". " +
               $"This is an old operation — confirm with the user that this is the one to roll back, " +
               $"then retry the call with operationId={op.Id}");
        }

        var applied = await ApplyRollbackAsync(op, ct);
        if (!applied)
        {
            _logger.LogWarning("Rollback of operation {OpId} ({Type}) did not change any data", op.Id, op.operationType);
            return (new { error = "Rollback failed — no data was changed" },
                $"Rollback of operation \"{op.description}\" failed: no data changed");
        }

        await _operationService.MarkRolledBackAsync(op.Id, ct);
        return (new { success = true, operationId = op.Id, description = op.description },
            $"Undone: {op.description}");
    }

    // Returns true only when the rollback actually changed data.
    private async Task<bool> ApplyRollbackAsync(AiOperation op, CancellationToken ct)
    {
        switch (op.operationType)
        {
            case "update_prices":
            {
                var data = JsonSerializer.Deserialize<PriceRollbackData>(op.rollbackDataJson, _rollbackJson);
                if (data == null || data.Products.Count == 0) return false;
                var ids = data.Products.Select(p => p.Id).ToList();
                var products = await _context.products.Where(p => ids.Contains(p.Id)).ToListAsync(ct);
                foreach (var pr in products)
                {
                    var snap = data.Products.FirstOrDefault(p => p.Id == pr.Id);
                    if (snap != null) pr.price = snap.OldPrice;
                }
                return await _context.SaveChangesAsync(ct) > 0 || products.Count > 0;
            }
            case "add_category_field":
            {
                var data = JsonSerializer.Deserialize<FieldRollbackData>(op.rollbackDataJson, _rollbackJson);
                if (data == null || data.FieldId == 0) return false;
                var deleted = await _context.categoryAttributes.Where(a => a.Id == data.FieldId)
                    .ExecuteDeleteAsync(ct);
                return deleted > 0;
            }
            case "connect_field":
            case "set_static_value":
            case "add_modifier":
            case "disconnect_field":
            case "generate_mapping":
            case "merge_fields":
            case "unmerge_fields":
            {
                var data = JsonSerializer.Deserialize<GraphRollbackData>(op.rollbackDataJson, _rollbackJson);
                if (data == null || data.MappingId == 0) return false;
                var updated = await _context.categoryMappings.Where(m => m.Id == data.MappingId)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.graphJson, data.PreviousGraphJson), ct);
                return updated > 0;
            }
            case "generate_barcodes":
            {
                var data = JsonSerializer.Deserialize<BarcodeRollbackData>(op.rollbackDataJson, _rollbackJson);
                if (data == null || data.Products.Count == 0) return false;
                var ids = data.Products.Select(x => x.Id).ToList();
                var products = await _context.products.Where(x => ids.Contains(x.Id)).ToListAsync(ct);
                foreach (var pr in products)
                {
                    var snap = data.Products.FirstOrDefault(x => x.Id == pr.Id);
                    if (snap != null) pr.barcode = snap.OldBarcode;
                }
                await _context.SaveChangesAsync(ct);
                return products.Count > 0;
            }
            case "update_attribute":
            {
                var data = JsonSerializer.Deserialize<ProductAttributeRollbackData>(op.rollbackDataJson, _rollbackJson);
                if (data == null || data.Products.Count == 0) return false;
                foreach (var snap in data.Products)
                {
                    var val = await _context.productAttributeValues
                        .FirstOrDefaultAsync(v => v.productId == snap.ProductId && v.attributeId == snap.AttributeId, ct);
                    if (val != null)
                    {
                        val.value = snap.OldValue ?? string.Empty;
                    }
                    else if (snap.OldValue != null)
                    {
                        _context.productAttributeValues.Add(new ProductAttributeValue
                        { productId = snap.ProductId, attributeId = snap.AttributeId, value = snap.OldValue });
                    }
                }
                await _context.SaveChangesAsync(ct);
                return true;
            }
            default:
                return false;
        }
    }
}
