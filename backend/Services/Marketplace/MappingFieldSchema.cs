using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using IMarketplaceMetadataCache = Delegat.Marketplace.SDK.IMarketplaceMetadataCache;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Marketplace field schema (card + price fields) for a category mapping, shared by the mapping
/// editor endpoints through the process-wide metadata cache so one page load calls the marketplace once.
/// </summary>
public static class MappingFieldSchema
{
    public static async Task<List<ExternalField>> GetCategoryFieldsAsync(
        IMarketplaceMetadataCache cache,
        IMarketplaceAdapter adapter,
        MarketplaceConnection connection,
        string externalCategoryId,
        CancellationToken ct)
    {
        var key = MarketplaceMetadataCache.CategoryFieldsKey(connection.adapterKey, connection.Id, externalCategoryId);
        if (cache.TryGetValue(key, out List<ExternalField>? fields) && fields != null)
            return fields;
        fields = await adapter.GetCategoryFieldsAsync(connection, externalCategoryId, ct);
        cache.Set(key, fields, MarketplaceMetadataCache.CategoryFieldsTtl);
        return fields;
    }

    public static async Task<List<ExternalField>> GetPriceFieldsAsync(
        IMarketplaceMetadataCache cache,
        IMarketplaceAdapter adapter,
        MarketplaceConnection connection,
        string externalCategoryId,
        CancellationToken ct)
    {
        var key = MarketplaceMetadataCache.PriceFieldsKey(connection.adapterKey, connection.Id, externalCategoryId);
        if (cache.TryGetValue(key, out List<ExternalField>? fields) && fields != null)
            return fields;
        fields = await adapter.GetPriceFieldsAsync(connection, externalCategoryId, ct);
        cache.Set(key, fields, MarketplaceMetadataCache.CategoryFieldsTtl);
        return fields;
    }

    /// <summary>Drop cached schema so the next read reflects the live marketplace (explicit field sync).</summary>
    public static void Invalidate(IMarketplaceMetadataCache cache, MarketplaceConnection connection, string externalCategoryId)
    {
        cache.Remove(MarketplaceMetadataCache.CategoryFieldsKey(connection.adapterKey, connection.Id, externalCategoryId));
        cache.Remove(MarketplaceMetadataCache.PriceFieldsKey(connection.adapterKey, connection.Id, externalCategoryId));
    }

    /// <summary>Price fields visible for the store (extended price parameters are opt-in).</summary>
    public static List<ExternalField> VisiblePriceFields(IEnumerable<ExternalField> fields, bool extendedPriceParameters) =>
        extendedPriceParameters
            ? fields.ToList()
            : fields.Where(f => !CategoryImportService.IsStoreExtendedPriceKey(f.Key)).ToList();

    public static object ToDto(ExternalField f) => new
    {
        key = f.Key,
        name = f.Name,
        isRequired = f.IsRequired,
        type = f.Type,
        options = f.Options,
        description = f.Description,
        unit = AttributeFieldConstraints.ResolveUnitLabel(f),
        unitCode = AttributeFieldConstraints.ResolveUnitCode(f),
        unitId = f.UnitId,
        group = f.Group,
        minValue = f.MinValue,
        maxValue = f.MaxValue,
        maxLength = f.MaxLength,
        maxValues = f.MaxValues,
        allowCustomValues = f.AllowCustomValues,
        sortOrder = f.SortOrder,
        isAspect = f.IsAspect,
    };
}
