using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Delegat.Models.Ai;

namespace Delegat.Services.Ai;

public class AiOperationService
{
    private readonly DataContext _context;
    private const int MaxOperationsPerUser = 20;

    public AiOperationService(DataContext context)
    {
        _context = context;
    }

    public async Task<int> RecordAsync(int storeId, int userId, string operationType,
        string description, object rollbackData, bool canRollback = true, CancellationToken ct = default)
    {
        var op = new AiOperation
        {
            storeId = storeId,
            userId = userId,
            operationType = operationType,
            description = description,
            rollbackDataJson = JsonSerializer.Serialize(rollbackData),
            canRollback = canRollback,
            status = AiOperationStatus.Completed
        };

        _context.aiOperations.Add(op);
        await _context.SaveChangesAsync(ct);

        // Prune old operations (keep last N per user)
        var old = await _context.aiOperations
            .Where(o => o.userId == userId && o.storeId == storeId)
            .OrderByDescending(o => o.createdAt)
            .Skip(MaxOperationsPerUser)
            .ToListAsync(ct);

        if (old.Count > 0)
        {
            _context.aiOperations.RemoveRange(old);
            await _context.SaveChangesAsync(ct);
        }

        return op.Id;
    }

    public async Task<AiOperation?> GetLastAsync(int userId, int storeId, CancellationToken ct = default)
        => await _context.aiOperations
            .Where(o => o.userId == userId && o.storeId == storeId && o.status == AiOperationStatus.Completed && o.canRollback)
            .OrderByDescending(o => o.createdAt)
            .FirstOrDefaultAsync(ct);

    public async Task<AiOperation?> GetByIdAsync(int operationId, int userId, CancellationToken ct = default)
        => await _context.aiOperations
            .FirstOrDefaultAsync(o => o.Id == operationId && o.userId == userId, ct);

    public async Task MarkRolledBackAsync(int operationId, CancellationToken ct = default)
    {
        await _context.aiOperations
            .Where(o => o.Id == operationId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.status, AiOperationStatus.RolledBack), ct);
    }

    public async Task<List<AiOperation>> GetRecentAsync(int userId, int storeId, int limit = 10, CancellationToken ct = default)
        => await _context.aiOperations
            .Where(o => o.userId == userId && o.storeId == storeId)
            .OrderByDescending(o => o.createdAt)
            .Take(limit)
            .ToListAsync(ct);

    // Rollback data structure helpers
    public static PriceRollbackData BuildPriceRollback(IEnumerable<(int id, decimal oldPrice)> products)
        => new() { Products = products.Select(p => new ProductPriceSnapshot { Id = p.id, OldPrice = p.oldPrice }).ToList() };

    public static FieldRollbackData BuildFieldRollback(int fieldId)
        => new() { FieldId = fieldId };

    public static GraphRollbackData BuildGraphRollback(int mappingId, string previousGraphJson)
        => new() { MappingId = mappingId, PreviousGraphJson = previousGraphJson };
}

// Rollback data shapes
public record PriceRollbackData
{
    public List<ProductPriceSnapshot> Products { get; set; } = new();
}

public record ProductPriceSnapshot
{
    public int Id { get; set; }
    public decimal OldPrice { get; set; }
}

public record FieldRollbackData
{
    public int FieldId { get; set; }
}

public record GraphRollbackData
{
    public int MappingId { get; set; }
    public string PreviousGraphJson { get; set; } = "";
}

public record BulkFieldRollbackData
{
    public List<int> CreatedFieldIds { get; set; } = new();
}

public record ProductAttributeRollbackData
{
    public List<ProductAttributeSnapshot> Products { get; set; } = new();
}

public record ProductAttributeSnapshot
{
    public int ProductId { get; set; }
    public int AttributeId { get; set; }
    public string? OldValue { get; set; }
}
