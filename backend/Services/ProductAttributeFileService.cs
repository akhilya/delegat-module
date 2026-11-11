using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Middleware;
using Delegat.Enums;
using Delegat.Models;
using Delegat.Models.Products;

namespace Delegat.Services;

/// <summary>Validates and materializes product-owned files used by File attributes.</summary>
public sealed class ProductAttributeFileService
{
    public const int MaxFilesPerValue = 100;
    private readonly DataContext _db;

    public ProductAttributeFileService(DataContext db) => _db = db;

    public async Task EnsureRequiredCategoryFilesAsync(
        int? categoryId,
        IReadOnlyCollection<ProductAttributeValue.UserValueForm>? forms,
        CancellationToken ct = default,
        bool allowNegativeTemporaryIds = false)
    {
        if (!categoryId.HasValue) return;
        var required = await _db.categoryAttributes.AsNoTracking()
            .Where(a => a.categoryId == categoryId && a.isRequired && a.type == AttributeType.File)
            .Select(a => new { a.Id, a.allowMultipleFiles })
            .ToListAsync(ct);
        foreach (var attribute in required)
        {
            var form = forms is null ? default : forms.FirstOrDefault(f => f.attributeId == attribute.Id);
            if (forms == null || !forms.Any(f => f.attributeId == attribute.Id)
                || ParseFileIds(form.fileIds, form.value, allowNegativeTemporaryIds).Length == 0)
                throw ApiException.BadRequest(code: "required_attribute_file");
            ValidateCount(
                ParseFileIds(form.fileIds, form.value, allowNegativeTemporaryIds),
                attribute.allowMultipleFiles);
        }
    }

    public async Task EnsureRequiredStoreFilesAsync(
        int storeId,
        IReadOnlyCollection<StoreProductAttributeValue.UserValueForm>? forms,
        CancellationToken ct = default,
        bool allowNegativeTemporaryIds = false)
    {
        var required = await _db.storeAttributes.AsNoTracking()
            .Where(a => a.storeId == storeId && a.isRequired && a.type == AttributeType.File)
            .Select(a => new { a.Id, a.allowMultipleFiles })
            .ToListAsync(ct);
        foreach (var attribute in required)
        {
            var form = forms is null ? default : forms.FirstOrDefault(f => f.storeAttributeId == attribute.Id);
            if (forms == null || !forms.Any(f => f.storeAttributeId == attribute.Id)
                || ParseFileIds(form.fileIds, form.value, allowNegativeTemporaryIds).Length == 0)
                throw ApiException.BadRequest(code: "required_attribute_file");
            ValidateCount(
                ParseFileIds(form.fileIds, form.value, allowNegativeTemporaryIds),
                attribute.allowMultipleFiles);
        }
    }

    public async Task ApplyCategoryAsync(
        ProductAttributeValue value,
        ProductAttributeValue.UserValueForm form,
        int? productCategoryId,
        CancellationToken ct = default)
    {
        var attribute = await _db.categoryAttributes
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == form.attributeId && a.categoryId == productCategoryId, ct);
        if (attribute == null)
            throw ApiException.BadRequest(code: "attribute_not_found");

        var isFile = attribute.type == AttributeType.File;
        var ids = isFile ? ParseFileIds(form.fileIds, form.value) : form.fileIds ?? [];
        await ApplyAsync(value, ids, isFile,
            attribute.allowMultipleFiles, value.productId, ct);
    }

    public async Task ApplyStoreAsync(
        StoreProductAttributeValue value,
        StoreProductAttributeValue.UserValueForm form,
        int storeId,
        CancellationToken ct = default)
    {
        var attribute = await _db.storeAttributes
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == form.storeAttributeId && a.storeId == storeId, ct);
        if (attribute == null)
            throw ApiException.BadRequest(code: "store_attribute_not_found");

        var isFile = attribute.type == AttributeType.File;
        var ids = isFile ? ParseFileIds(form.fileIds, form.value) : form.fileIds ?? [];
        await ApplyAsync(value, ids, isFile,
            attribute.allowMultipleFiles, value.productId, ct);
    }

    private async Task ApplyAsync(
        ProductAttributeValue value,
        int[] ids,
        bool isFile,
        bool allowMultiple,
        int productId,
        CancellationToken ct)
    {
        if (!isFile)
        {
            if (ids.Length > 0)
                throw ApiException.BadRequest(code: "file_ids_require_file_attribute");
            return;
        }

        ValidateCount(ids, allowMultiple);
        var files = await GetOwnedFilesAsync(productId, ids, ct);
        if (files.Count != ids.Length)
            throw ApiException.BadRequest(code: "file_not_found");

        value.value = "";
        for (var i = 0; i < ids.Length; i++)
            value.files.Add(new ProductAttributeFile { fileId = ids[i], index = i });
    }

    private async Task ApplyAsync(
        StoreProductAttributeValue value,
        int[] ids,
        bool isFile,
        bool allowMultiple,
        int productId,
        CancellationToken ct)
    {
        if (!isFile)
        {
            if (ids.Length > 0)
                throw ApiException.BadRequest(code: "file_ids_require_file_attribute");
            return;
        }

        ValidateCount(ids, allowMultiple);
        var files = await GetOwnedFilesAsync(productId, ids, ct);
        if (files.Count != ids.Length)
            throw ApiException.BadRequest(code: "file_not_found");

        value.value = "";
        for (var i = 0; i < ids.Length; i++)
            value.files.Add(new StoreProductAttributeFile { fileId = ids[i], index = i });
    }

    private async Task<List<Models.File>> GetOwnedFilesAsync(int productId, int[] ids, CancellationToken ct)
        => ids.Length == 0
            ? []
            : await _db.files.Where(f => f.productId == productId && ids.Contains(f.Id)).ToListAsync(ct);

    public static void ValidateCount(int[] ids, bool allowMultiple)
    {
        if (ids.Length > MaxFilesPerValue)
            throw ApiException.BadRequest(code: "too_many_attribute_files");
        if (!allowMultiple && ids.Length > 1)
            throw ApiException.BadRequest(code: "attribute_multiple_files_disabled");
        if (ids.Distinct().Count() != ids.Length)
            throw ApiException.BadRequest(code: "duplicate_attribute_files");
    }

    public static int[] ParseFileIds(
        int[]? explicitIds,
        string? rawValue,
        bool allowNegativeTemporaryIds = false)
    {
        int[]? fromValue = null;
        if (!string.IsNullOrWhiteSpace(rawValue))
        {
            try
            {
                fromValue = JsonSerializer.Deserialize<int[]>(rawValue);
                if (fromValue is null)
                    throw new JsonException();
            }
            catch (JsonException)
            {
                throw ApiException.BadRequest(code: "invalid_attribute_file_value");
            }
        }

        if (explicitIds is not null && fromValue is not null
            && !explicitIds.SequenceEqual(fromValue))
            throw ApiException.BadRequest(code: "conflicting_attribute_file_values");

        var ids = explicitIds ?? fromValue ?? [];
        if (ids.Any(id => id == 0 || (id < 0 && !allowNegativeTemporaryIds)))
            throw ApiException.BadRequest(code: "invalid_attribute_file_value");
        return ids;
    }
}
