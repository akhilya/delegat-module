using Delegat.Enums;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services;

/// <summary>Enforces invariants specific to File attribute definitions.</summary>
public static class AttributeDefinitionFileRules
{
    public static void Validate(
        AttributeType type,
        bool allowMultipleFiles,
        bool isUnique,
        string? defaultValue)
    {
        if (allowMultipleFiles && type != AttributeType.File)
            throw ApiException.BadRequest(code: "multiple_files_requires_file_attribute");

        if (type != AttributeType.File)
            return;

        if (isUnique)
            throw ApiException.BadRequest(code: "file_attribute_cannot_be_unique");
        if (!string.IsNullOrWhiteSpace(defaultValue))
            throw ApiException.BadRequest(code: "file_attribute_default_unsupported");
    }

    public static async Task ValidateCategoryUpdateAsync(
        DataContext db,
        CategoryAttribute current,
        CategoryAttribute.UpdateForm input,
        CancellationToken ct = default)
    {
        var nextType = input.type ?? current.type;
        var nextMultiple = input.allowMultipleFiles
            ?? (nextType == AttributeType.File && current.allowMultipleFiles);
        Validate(nextType, nextMultiple, input.isUnique ?? current.isUnique, input.defaultValue);

        if (IsFileTypeChange(current.type, nextType)
            && await db.productAttributeValues.AnyAsync(v => v.attributeId == current.Id, ct))
            throw ApiException.Conflict(code: "attribute_type_change_with_values");

        if (current.type == AttributeType.File && nextType == AttributeType.File && !nextMultiple
            && await HasMultipleCategoryFilesAsync(db, current.Id, ct))
            throw ApiException.Conflict(code: "attribute_multiple_files_in_use");
    }

    public static async Task ValidateStoreUpdateAsync(
        DataContext db,
        StoreAttribute current,
        StoreAttribute.UpdateForm input,
        CancellationToken ct = default)
    {
        var nextType = input.type ?? current.type;
        var nextMultiple = input.allowMultipleFiles
            ?? (nextType == AttributeType.File && current.allowMultipleFiles);
        Validate(nextType, nextMultiple, input.isUnique ?? current.isUnique, input.defaultValue);

        if (IsFileTypeChange(current.type, nextType)
            && await db.storeProductAttributeValues.AnyAsync(v => v.storeAttributeId == current.Id, ct))
            throw ApiException.Conflict(code: "attribute_type_change_with_values");

        if (current.type == AttributeType.File && nextType == AttributeType.File && !nextMultiple
            && await HasMultipleStoreFilesAsync(db, current.Id, ct))
            throw ApiException.Conflict(code: "attribute_multiple_files_in_use");
    }

    private static bool IsFileTypeChange(AttributeType current, AttributeType next)
        => current != next && (current == AttributeType.File || next == AttributeType.File);

    private static async Task<bool> HasMultipleCategoryFilesAsync(DataContext db, int attributeId, CancellationToken ct)
        => await db.productAttributeFiles
            .Where(link => db.productAttributeValues.Any(value =>
                value.Id == link.attributeValueId && value.attributeId == attributeId))
            .GroupBy(link => link.attributeValueId)
            .AnyAsync(group => group.Count() > 1, ct);

    private static async Task<bool> HasMultipleStoreFilesAsync(DataContext db, int attributeId, CancellationToken ct)
        => await db.storeProductAttributeFiles
            .Where(link => db.storeProductAttributeValues.Any(value =>
                value.Id == link.attributeValueId && value.storeAttributeId == attributeId))
            .GroupBy(link => link.attributeValueId)
            .AnyAsync(group => group.Count() > 1, ct);
}
