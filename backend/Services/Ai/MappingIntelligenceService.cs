using Microsoft.EntityFrameworkCore;
using Delegat.Enums;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Models.Products;
using Delegat.Services.Marketplace.Mapping;

namespace Delegat.Services.Ai;

/// <summary>
/// Builds cross-marketplace mapping context: category schema origin, sibling mappings,
/// product attribute samples, and precomputed option alignment hints.
/// </summary>
public class MappingIntelligenceService
{
    private readonly DataContext _context;

    public MappingIntelligenceService(DataContext context)
    {
        _context = context;
    }

    public async Task<SchemaContextDto> GetSchemaContextAsync(CategoryMapping mapping, CancellationToken ct = default)
    {
        var siblings = await _context.categoryMappings.AsNoTracking()
            .Include(m => m.connection)
            .Where(m => m.localCategoryId == mapping.localCategoryId)
            .OrderBy(m => m.createdAt)
            .Select(m => new
            {
                m.Id,
                m.connectionId,
                m.externalCategoryName,
                m.name,
                m.createdAt,
                adapterKey = m.connection.adapterKey,
                connectionName = m.connection.name,
            })
            .ToListAsync(ct);

        var source = siblings.FirstOrDefault(m => m.Id != mapping.Id);

        return new SchemaContextDto
        {
            LocalCategoryId = mapping.localCategoryId,
            CurrentAdapterKey = mapping.connection?.adapterKey,
            CurrentConnectionName = mapping.connection?.name,
            SchemaSource = source == null ? null : new SchemaSourceDto
            {
                MappingId = source.Id,
                ConnectionId = source.connectionId,
                ConnectionName = source.connectionName,
                AdapterKey = source.adapterKey,
                ExternalCategoryName = source.externalCategoryName ?? source.name,
                Hint = "Локальная категория, скорее всего, создана/наполнена при импорте с этого маркетплейса. " +
                       "Значения атрибутов (options) и ключи словарей — оттуда. При маппинге на другой адаптер " +
                       "для Select/MultiSelect с allowedValues обязательно добавляй optionMap: локальное value → target allowedValue.",
            },
            SiblingMappings = siblings
                .Where(m => m.Id != mapping.Id)
                .Select(m => new SiblingMappingDto
                {
                    MappingId = m.Id,
                    ConnectionName = m.connectionName,
                    AdapterKey = m.adapterKey,
                    ExternalCategoryName = m.externalCategoryName ?? m.name,
                })
                .ToList(),
        };
    }

    public async Task<ProductSampleDto?> GetProductSampleAsync(
        int localCategoryId, int? productId, CancellationToken ct = default)
    {
        if (!productId.HasValue) return null;

        var product = await _context.products.AsNoTracking()
            .Include(p => p.attributeValues)
            .ThenInclude(v => v.attribute)
            .FirstOrDefaultAsync(p => p.Id == productId && p.categoryId == localCategoryId, ct);

        if (product == null) return null;

        return new ProductSampleDto
        {
            ProductId = product.Id,
            Name = product.name,
            Sku = product.sku,
            Attributes = product.attributeValues
                .Where(v => v.attribute != null)
                .Select(v => new ProductAttributeSampleDto
                {
                    AttributeId = v.attributeId,
                    AttributeName = v.attribute.name,
                    AttributeKey = v.attribute.key,
                    StoredValue = v.value,
                    AttributeType = v.attribute.type.ToString(),
                    LocalOptions = OptionAlignmentHelper.ParseAttributeOptions(v.attribute.options)
                        .Take(30)
                        .Select(o => new { o.Value, o.Label })
                        .ToList(),
                })
                .ToList(),
        };
    }

    public async Task<List<OptionAlignmentHintDto>> BuildAlignmentHintsAsync(
        CategoryMapping mapping,
        IReadOnlyList<ExternalField> targetFields,
        int? productId = null,
        CancellationToken ct = default)
    {
        var attrs = await _context.categoryAttributes.AsNoTracking()
            .Where(a => a.categoryId == mapping.localCategoryId)
            .ToListAsync(ct);

        Product? product = null;
        if (productId.HasValue)
        {
            product = await _context.products.AsNoTracking()
                .Include(p => p.attributeValues)
                .FirstOrDefaultAsync(p => p.Id == productId && p.categoryId == mapping.localCategoryId, ct);
        }

        var hints = new List<OptionAlignmentHintDto>();
        var usedPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var selectAttrs = attrs
            .Where(a => a.type is AttributeType.Select or AttributeType.MultiSelect)
            .ToList();

        foreach (var field in targetFields.Where(f => f.Options is { Count: > 0 and <= 200 }))
        {
            OptionAlignmentHintDto? bestHint = null;
            var bestScore = 0;

            foreach (var attr in selectAttrs)
            {
                var localOpts = OptionAlignmentHelper.ParseAttributeOptions(attr.options);
                if (localOpts.Count == 0) continue;

                var score = AttributeFieldMatchScore(attr, field);
                if (score < 50) continue;

                if (!OptionAlignmentHelper.NeedsValueMap(localOpts, field.Options!))
                {
                    if (score < 80) continue;
                }

                var mappings = OptionAlignmentHelper.BuildValueMapEntries(localOpts, field.Options!);
                if (mappings.Count == 0 && score < 80) continue;

                mappings = OptionAlignmentHelper.NormalizeValueMapMappings(
                    mappings, localOpts, field.Options!);

                if (score > bestScore)
                {
                    bestScore = score;
                    string? productValue = product?.attributeValues
                        .FirstOrDefault(v => v.attributeId == attr.Id)?.value;
                    string? productTarget = null;
                    if (!string.IsNullOrEmpty(productValue))
                    {
                        var entry = mappings.FirstOrDefault(m =>
                            m["from"].Equals(productValue, StringComparison.OrdinalIgnoreCase));
                        productTarget = entry?.GetValueOrDefault("to");
                    }

                    bestHint = new OptionAlignmentHintDto
                    {
                        AdapterFieldKey = field.Key,
                        AdapterFieldLabel = field.Name,
                        SourceAttributeId = attr.Id,
                        SourceAttributeName = attr.name,
                        SuggestedSourceType = "attribute",
                        MatchScore = score,
                        ValueMapMappings = mappings,
                        UnmappedLocalCount = Math.Max(0, localOpts.Count - mappings.Count),
                        ProductStoredValue = productValue,
                        ProductMappedTargetValue = productTarget,
                        Note = mappings.Count > 0
                            ? $"Словарь категории: {mappings.Count}/{localOpts.Count} опций (score={score})"
                            : $"Совпадение имён (score={score}), optionMap не требуется",
                    };
                }
            }

            if (bestHint == null) continue;

            var pairKey = $"{bestHint.SourceAttributeId}:{bestHint.AdapterFieldKey}";
            if (!usedPairs.Add(pairKey)) continue;
            hints.Add(bestHint);
        }

        return hints.OrderByDescending(h => h.MatchScore).ToList();
    }

    private static int AttributeFieldMatchScore(CategoryAttribute attr, ExternalField field)
    {
        var scores = new[]
        {
            NamesMatch(attr.name, field.Name) ? 100 : 0,
            NamesMatch(attr.name, field.Key) ? 90 : 0,
            NamesMatch(attr.key, field.Key) ? 95 : 0,
            NamesMatch(attr.key, field.Name) ? 85 : 0,
            PartialNameMatch(attr.name, field.Name),
            PartialNameMatch(attr.name, field.Key),
            PartialNameMatch(attr.key, field.Name),
            PartialNameMatch(attr.key, field.Key),
        };
        return scores.Max();
    }

    private static int PartialNameMatch(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return 0;
        var na = NormalizeName(a);
        var nb = NormalizeName(b);
        if (na.Length < 3 || nb.Length < 3) return 0;
        if (na.Contains(nb, StringComparison.Ordinal) || nb.Contains(na, StringComparison.Ordinal))
            return 70;
        return 0;
    }

    private static bool NamesMatch(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        var na = NormalizeName(a);
        var nb = NormalizeName(b);
        return na == nb || na.Contains(nb, StringComparison.Ordinal) || nb.Contains(na, StringComparison.Ordinal);
    }

    private static string NormalizeName(string s)
        => s.Trim().ToLowerInvariant().Replace(" ", "");
}

public class SchemaContextDto
{
    public int LocalCategoryId { get; set; }
    public string? CurrentAdapterKey { get; set; }
    public string? CurrentConnectionName { get; set; }
    public SchemaSourceDto? SchemaSource { get; set; }
    public List<SiblingMappingDto> SiblingMappings { get; set; } = [];
}

public class SchemaSourceDto
{
    public int MappingId { get; set; }
    public int ConnectionId { get; set; }
    public string? ConnectionName { get; set; }
    public string? AdapterKey { get; set; }
    public string? ExternalCategoryName { get; set; }
    public string? Hint { get; set; }
}

public class SiblingMappingDto
{
    public int MappingId { get; set; }
    public string? ConnectionName { get; set; }
    public string? AdapterKey { get; set; }
    public string? ExternalCategoryName { get; set; }
}

public class ProductSampleDto
{
    public int ProductId { get; set; }
    public string? Name { get; set; }
    public string? Sku { get; set; }
    public List<ProductAttributeSampleDto> Attributes { get; set; } = [];
}

public class ProductAttributeSampleDto
{
    public int AttributeId { get; set; }
    public string? AttributeName { get; set; }
    public string? AttributeKey { get; set; }
    public string? StoredValue { get; set; }
    public string? AttributeType { get; set; }
    public object? LocalOptions { get; set; }
}

public class OptionAlignmentHintDto
{
    public string AdapterFieldKey { get; set; } = "";
    public string? AdapterFieldLabel { get; set; }
    public int SourceAttributeId { get; set; }
    public string? SourceAttributeName { get; set; }
    public string SuggestedSourceType { get; set; } = "attribute";
    public int MatchScore { get; set; }
    public List<Dictionary<string, string>> ValueMapMappings { get; set; } = [];
    public int UnmappedLocalCount { get; set; }
    public string? ProductStoredValue { get; set; }
    public string? ProductMappedTargetValue { get; set; }
    public string? Note { get; set; }
}