using System.Text.Json;
using Delegat.Enums;
using Delegat.Models;
using Delegat.Models.Marketplace.DTOs;

namespace Delegat.Services.Marketplace;

/// <summary>Conservative automatic bindings for marketplace fields.</summary>
internal static class FieldAutoMapping
{
    internal static (FieldMappingSourceType sourceType, string? sourceField, int? sourceAttributeId) Resolve(
        ExternalField field, IReadOnlyCollection<CategoryAttribute> attributes, int? preferredAttributeId = null)
    {
        if (CategoryImportService.IsWbPackagingWeightCharacteristic(field.Key, field.Name))
            return (FieldMappingSourceType.Static, null, null);
        var basic = CategoryImportService.DetermineAutoMapping(field, new Dictionary<string, int>());
        if (basic.sourceType != FieldMappingSourceType.Static)
            return basic;

        if (preferredAttributeId is int selected && attributes.Any(a => a.Id == selected))
            return (FieldMappingSourceType.CategoryAttribute, null, selected);

        var candidates = AttributeDuplicateMatcher.FindCandidates(field, AttributeImportSemantics.ResolveType(field),
            AttributeFieldConstraints.ResolveUnitCode(field), attributes, allowConvertibleUnits: true);
        return candidates.Count > 0 && (candidates.Count == 1 || candidates[0].Score - candidates[1].Score >= 5)
            ? (FieldMappingSourceType.CategoryAttribute, null, candidates[0].Attribute.Id)
            : (FieldMappingSourceType.Static, null, null);
    }

    internal static string? Conversion(ExternalField field, CategoryAttribute? attribute)
    {
        var fromUnit = AttributeUnits.NormalizeCode(attribute?.unitCode);
        var toUnit = AttributeFieldConstraints.ResolveUnitCode(field);
        return attribute != null && !AttributeUnits.AreSame(fromUnit, toUnit)
            && AttributeUnits.TryGetConversion(fromUnit, toUnit, out _)
            ? JsonSerializer.Serialize(new[] { new { type = "unitConversion", enabled = true, @params = new { fromUnit, toUnit } } })
            : null;
    }
}
