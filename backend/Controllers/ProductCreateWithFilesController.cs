using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Enums;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Delegat.Models.Products;
using Delegat.Services;

namespace Delegat.Controllers;

/// <summary>Creates a product, its uploaded files, attribute links, and gallery in one DB transaction.</summary>
[ApiController]
[JwtAuthorize]
[RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
public sealed class ProductCreateWithFilesController : ControllerBase
{
    private const int MaxGalleryItems = 200;
    private readonly ICurrentUserService _currentUser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly DataContext _db;
    private readonly IStorageService _storage;
    private readonly QuotaService _quota;
    private readonly AttributeUniquenessService _attributeUniqueness;
    private readonly ProductAttributeFileService _attributeFiles;

    public ProductCreateWithFilesController(
        ICurrentUserService currentUser,
        IUnitOfWork unitOfWork,
        DataContext db,
        IStorageService storage,
        QuotaService quota,
        AttributeUniquenessService attributeUniqueness,
        ProductAttributeFileService attributeFiles)
    {
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
        _db = db;
        _storage = storage;
        _quota = quota;
        _attributeUniqueness = attributeUniqueness;
        _attributeFiles = attributeFiles;
    }

    /// <summary>
    /// Creates a product with uploaded attribute files and/or gallery media.
    /// The multipart <c>product</c> field contains JSON; file part names use
    /// <c>file_-1</c>, <c>file_-2</c>, etc. and are referenced by negative IDs in that JSON.
    /// </summary>
    [HttpPost]
    [Route("/products/with-files")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    [DisableRequestSizeLimit]
    [DisableFormValueModelBinding]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var storeId = await _currentUser.GetSelectedStoreIdAsync();
        if (storeId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        await _quota.EnsureProductQuotaAsync(storeId.Value);
        var user = await _currentUser.GetUserAsync();
        if (user == null)
            throw ApiException.Unauthorized();

        CreateProductPayload? payload = null;
        var expectedTemporaryIds = new HashSet<int>();
        var upload = await MultipartObjectUpload.SaveProductCreateAsync(
            Request,
            _storage,
            (field, fileName) =>
            {
                if (!TryParseTemporaryFileId(field, out var temporaryId))
                    throw ApiException.BadRequest(code: "invalid_uploaded_file_reference");
                if (!expectedTemporaryIds.Contains(temporaryId))
                    throw ApiException.BadRequest(code: "unreferenced_uploaded_file");
                return $"stores/{storeId}/products/pending/{Guid.NewGuid():N}/{fileName}";
            },
            ct,
            async (metadata, validationCt) =>
            {
                var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
                try
                {
                    payload = JsonSerializer.Deserialize<CreateProductPayload>(metadata, options)
                        ?? throw new JsonException("Empty product payload.");
                }
                catch (JsonException)
                {
                    throw ApiException.BadRequest(code: "product_metadata_invalid");
                }

                if (!TryValidateModel(payload.Product))
                    throw ApiException.BadRequest(code: "product_metadata_invalid");

                await ValidateMetadataBeforeUploadAsync(
                    storeId.Value, payload, expectedTemporaryIds, validationCt);
            });

        var completed = false;
        try
        {
            var input = payload!.Product;

            var uploadedByTemporaryId = BuildUploadMap(upload.Files);
            var referencedTemporaryIds = new HashSet<int>();
            var categoryFileAttributes = await GetCategoryFileAttributeIdsAsync(input, ct);
            var storeFileAttributes = await GetStoreFileAttributeIdsAsync(storeId.Value, input, ct);
            var categoryValues = MapCategoryAttributeForms(
                input.attributeValues, categoryFileAttributes, uploadedByTemporaryId, referencedTemporaryIds);
            var storeValues = MapStoreAttributeForms(
                input.storeAttributeValues, storeFileAttributes, uploadedByTemporaryId, referencedTemporaryIds);

            await _attributeFiles.EnsureRequiredCategoryFilesAsync(
                input.categoryId, categoryValues, ct, allowNegativeTemporaryIds: true);
            await _attributeFiles.EnsureRequiredStoreFilesAsync(
                storeId.Value, storeValues, ct, allowNegativeTemporaryIds: true);

            var galleryTemporaryIds = payload.GalleryFileIds ?? [];
            if (galleryTemporaryIds.Length > MaxGalleryItems)
                throw ApiException.BadRequest(code: "too_many_gallery_media_items");
            var galleryFileIds = ResolveTemporaryFileIds(
                galleryTemporaryIds, uploadedByTemporaryId, referencedTemporaryIds);
            ValidateGalleryFiles(galleryFileIds, uploadedByTemporaryId);
            if (referencedTemporaryIds.Count != uploadedByTemporaryId.Count)
                throw ApiException.BadRequest(code: "unreferenced_uploaded_file");

            await _quota.EnsureFileQuotaAsync(storeId.Value, upload.Files.Count);
            await _quota.EnsureStorageQuotaAsync(
                storeId.Value, upload.Files.Sum(f => f.Object.Length));

            var product = new Product();
            product.AddFill(new Product.AddForm(input, storeId.Value, input.categoryId));

            await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                _db.products.Add(product);
                await _db.SaveChangesAsync(ct);

                var fileEntities = new Dictionary<int, Models.File>();
                foreach (var (temporaryId, namedFile) in uploadedByTemporaryId)
                {
                    var item = namedFile.Object;
                    var file = new Models.File();
                    file.AddFill(new Models.File.AddForm(
                        item.Path, user.Id, product.Id, item.ContentType, item.Length));
                    file.displayName = item.FileName;
                    file.fileType = item.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                        ? FileType.Image
                        : FileType.Other;
                    _db.files.Add(file);
                    fileEntities.Add(temporaryId, file);
                }

                await _db.SaveChangesAsync(ct);

                categoryValues = MapPersistedFileIds(categoryValues, fileEntities, categoryFileAttributes);
                storeValues = MapPersistedFileIds(storeValues, fileEntities, storeFileAttributes);
                var persistedGalleryFileIds = galleryFileIds
                    .Select(id => fileEntities[id].Id)
                    .ToArray();

                foreach (var form in categoryValues)
                {
                    var value = new ProductAttributeValue
                    {
                        productId = product.Id,
                        attributeId = form.attributeId,
                        value = form.value
                    };
                    await _attributeFiles.ApplyCategoryAsync(value, form, product.categoryId, ct);
                    _db.productAttributeValues.Add(value);
                }

                foreach (var form in storeValues)
                {
                    var value = new StoreProductAttributeValue
                    {
                        productId = product.Id,
                        storeAttributeId = form.storeAttributeId,
                        value = form.value
                    };
                    await _attributeFiles.ApplyStoreAsync(value, form, storeId.Value, ct);
                    _db.storeProductAttributeValues.Add(value);
                }

                for (var index = 0; index < persistedGalleryFileIds.Length; index++)
                {
                    _db.images.Add(new ImageReference
                    {
                        fileId = persistedGalleryFileIds[index],
                        productId = product.Id,
                        index = index
                    });
                }

                await _db.SaveChangesAsync(ct);
            }, ct);

            completed = true;
            return Ok(new { id = product.Id, next = $"/products/{product.Id}/edit" });
        }
        finally
        {
            if (!completed)
            {
                foreach (var file in upload.Files)
                    await _storage.DeleteAsync(file.Object.Path, CancellationToken.None);
            }
        }
    }

    private async Task ValidateMetadataBeforeUploadAsync(
        int storeId,
        CreateProductPayload payload,
        HashSet<int> expectedTemporaryIds,
        CancellationToken ct)
    {
        var input = payload.Product;
        ValidateRequiredProductFields(input);

        if (!string.IsNullOrWhiteSpace(input.barcode)
            && !ProductController.IsValidEanBarcode(input.barcode))
            throw ApiException.BadRequest(code: "barcode_invalid");

        if (input.categoryId.HasValue
            && !await _db.categories.AnyAsync(
                category => category.Id == input.categoryId.Value && category.storeId == storeId, ct))
            throw ApiException.BadRequest(code: "category_not_found");

        if (await _db.products.AnyAsync(product => product.storeId == storeId && product.sku == input.sku, ct))
            throw new ApiException(
                statusCode: StatusCodes.Status400BadRequest,
                data: new { sku = input.sku },
                code: "sku_already_used");

        await _attributeUniqueness.EnsureUniqueAsync(
            storeId,
            input.categoryId,
            input.attributeValues,
            input.storeAttributeValues,
            ct: ct);

        var categoryFileAttributeIds = await GetCategoryFileAttributeIdsAsync(input, ct);
        var storeFileAttributeIds = await GetStoreFileAttributeIdsAsync(storeId, input, ct);

        if (input.attributeValues?.Any(form =>
                form.fileIds is { Length: > 0 } && !categoryFileAttributeIds.Contains(form.attributeId)) == true
            || input.storeAttributeValues?.Any(form =>
                form.fileIds is { Length: > 0 } && !storeFileAttributeIds.Contains(form.storeAttributeId)) == true)
            throw ApiException.BadRequest(code: "file_ids_require_file_attribute");

        var categoryMultiple = await _db.categoryAttributes.AsNoTracking()
            .Where(attribute => categoryFileAttributeIds.Contains(attribute.Id))
            .ToDictionaryAsync(attribute => attribute.Id, attribute => attribute.allowMultipleFiles, ct);
        foreach (var form in input.attributeValues ?? [])
        {
            if (!categoryFileAttributeIds.Contains(form.attributeId)) continue;
            var ids = ProductAttributeFileService.ParseFileIds(
                form.fileIds, form.value, allowNegativeTemporaryIds: true);
            ProductAttributeFileService.ValidateCount(ids, categoryMultiple[form.attributeId]);
            AddExpectedTemporaryIds(expectedTemporaryIds, ids);
        }

        var storeMultiple = await _db.storeAttributes.AsNoTracking()
            .Where(attribute => storeFileAttributeIds.Contains(attribute.Id))
            .ToDictionaryAsync(attribute => attribute.Id, attribute => attribute.allowMultipleFiles, ct);
        foreach (var form in input.storeAttributeValues ?? [])
        {
            if (!storeFileAttributeIds.Contains(form.storeAttributeId)) continue;
            var ids = ProductAttributeFileService.ParseFileIds(
                form.fileIds, form.value, allowNegativeTemporaryIds: true);
            ProductAttributeFileService.ValidateCount(ids, storeMultiple[form.storeAttributeId]);
            AddExpectedTemporaryIds(expectedTemporaryIds, ids);
        }

        await _attributeFiles.EnsureRequiredCategoryFilesAsync(
            input.categoryId, input.attributeValues, ct, allowNegativeTemporaryIds: true);
        await _attributeFiles.EnsureRequiredStoreFilesAsync(
            storeId, input.storeAttributeValues, ct, allowNegativeTemporaryIds: true);

        var galleryIds = ProductAttributeFileService.ParseFileIds(
            payload.GalleryFileIds, rawValue: null, allowNegativeTemporaryIds: true);
        if (galleryIds.Length > MaxGalleryItems)
            throw ApiException.BadRequest(code: "too_many_gallery_media_items");
        if (galleryIds.Distinct().Count() != galleryIds.Length)
            throw ApiException.BadRequest(code: "duplicate_media_files");
        AddExpectedTemporaryIds(expectedTemporaryIds, galleryIds);

        if (expectedTemporaryIds.Count > MultipartObjectUpload.MaxProductCreateFiles)
            throw ApiException.BadRequest(code: "too_many_uploaded_files");
        await _quota.EnsureFileQuotaAsync(storeId, expectedTemporaryIds.Count);
    }

    private static void AddExpectedTemporaryIds(HashSet<int> expected, IEnumerable<int> ids)
    {
        foreach (var id in ids)
        {
            if (id >= 0)
                throw ApiException.BadRequest(code: "invalid_uploaded_file_reference");
            expected.Add(id);
        }
    }

    private async Task<HashSet<int>> GetCategoryFileAttributeIdsAsync(
        Product.UserAddForm input,
        CancellationToken ct)
    {
        var ids = input.attributeValues?.Select(v => v.attributeId).Distinct().ToArray() ?? [];
        if (ids.Length == 0) return [];
        return (await _db.categoryAttributes.AsNoTracking()
                .Where(a => ids.Contains(a.Id) && a.categoryId == input.categoryId && a.type == AttributeType.File)
                .Select(a => a.Id)
                .ToListAsync(ct))
            .ToHashSet();
    }

    private async Task<HashSet<int>> GetStoreFileAttributeIdsAsync(
        int storeId,
        Product.UserAddForm input,
        CancellationToken ct)
    {
        var ids = input.storeAttributeValues?.Select(v => v.storeAttributeId).Distinct().ToArray() ?? [];
        if (ids.Length == 0) return [];
        return (await _db.storeAttributes.AsNoTracking()
                .Where(a => ids.Contains(a.Id) && a.storeId == storeId && a.type == AttributeType.File)
                .Select(a => a.Id)
                .ToListAsync(ct))
            .ToHashSet();
    }

    private static List<ProductAttributeValue.UserValueForm> MapCategoryAttributeForms(
        IReadOnlyCollection<ProductAttributeValue.UserValueForm>? forms,
        HashSet<int> fileAttributeIds,
        IReadOnlyDictionary<int, MultipartObjectUpload.NamedSavedObject> uploads,
        HashSet<int> referenced)
    {
        var mapped = new List<ProductAttributeValue.UserValueForm>();
        foreach (var form in forms ?? [])
        {
            if (!fileAttributeIds.Contains(form.attributeId))
            {
                mapped.Add(form);
                continue;
            }

            var temporaryIds = ProductAttributeFileService.ParseFileIds(
                form.fileIds, form.value, allowNegativeTemporaryIds: true);
            mapped.Add(new ProductAttributeValue.UserValueForm
            {
                attributeId = form.attributeId,
                value = string.Empty,
                fileIds = ResolveTemporaryFileIds(temporaryIds, uploads, referenced)
            });
        }
        return mapped;
    }

    private static List<StoreProductAttributeValue.UserValueForm> MapStoreAttributeForms(
        IReadOnlyCollection<StoreProductAttributeValue.UserValueForm>? forms,
        HashSet<int> fileAttributeIds,
        IReadOnlyDictionary<int, MultipartObjectUpload.NamedSavedObject> uploads,
        HashSet<int> referenced)
    {
        var mapped = new List<StoreProductAttributeValue.UserValueForm>();
        foreach (var form in forms ?? [])
        {
            if (!fileAttributeIds.Contains(form.storeAttributeId))
            {
                mapped.Add(form);
                continue;
            }

            var temporaryIds = ProductAttributeFileService.ParseFileIds(
                form.fileIds, form.value, allowNegativeTemporaryIds: true);
            mapped.Add(new StoreProductAttributeValue.UserValueForm
            {
                storeAttributeId = form.storeAttributeId,
                value = string.Empty,
                fileIds = ResolveTemporaryFileIds(temporaryIds, uploads, referenced)
            });
        }
        return mapped;
    }

    private static List<ProductAttributeValue.UserValueForm> MapPersistedFileIds(
        IEnumerable<ProductAttributeValue.UserValueForm> forms,
        IReadOnlyDictionary<int, Models.File> files,
        HashSet<int> fileAttributeIds)
    {
        return forms.Select(form =>
        {
            if (fileAttributeIds.Contains(form.attributeId) && form.fileIds != null)
            {
                form.fileIds = form.fileIds.Select(id => files[id].Id).ToArray();
                form.value = string.Empty;
            }
            return form;
        }).ToList();
    }

    private static List<StoreProductAttributeValue.UserValueForm> MapPersistedFileIds(
        IEnumerable<StoreProductAttributeValue.UserValueForm> forms,
        IReadOnlyDictionary<int, Models.File> files,
        HashSet<int> fileAttributeIds)
    {
        return forms.Select(form =>
        {
            if (fileAttributeIds.Contains(form.storeAttributeId) && form.fileIds != null)
            {
                form.fileIds = form.fileIds.Select(id => files[id].Id).ToArray();
                form.value = string.Empty;
            }
            return form;
        }).ToList();
    }

    private static Dictionary<int, MultipartObjectUpload.NamedSavedObject> BuildUploadMap(
        IReadOnlyList<MultipartObjectUpload.NamedSavedObject> files)
    {
        var result = new Dictionary<int, MultipartObjectUpload.NamedSavedObject>();
        foreach (var file in files)
        {
            if (!TryParseTemporaryFileId(file.FieldName, out var id))
                throw ApiException.BadRequest(code: "invalid_uploaded_file_reference");
            if (!result.TryAdd(id, file))
                throw ApiException.BadRequest(code: "duplicate_uploaded_file_reference");
        }
        return result;
    }

    private static int[] ResolveTemporaryFileIds(
        int[] temporaryIds,
        IReadOnlyDictionary<int, MultipartObjectUpload.NamedSavedObject> uploads,
        HashSet<int> referenced)
    {
        var resolved = new int[temporaryIds.Length];
        for (var i = 0; i < temporaryIds.Length; i++)
        {
            var temporaryId = temporaryIds[i];
            if (temporaryId >= 0 || !uploads.ContainsKey(temporaryId))
                throw ApiException.BadRequest(code: "file_not_found");
            referenced.Add(temporaryId);
            // Temporary IDs are replaced with persisted file IDs after file rows are saved.
            resolved[i] = temporaryId;
        }
        return resolved;
    }

    private static void ValidateGalleryFiles(
        int[] temporaryIds,
        IReadOnlyDictionary<int, MultipartObjectUpload.NamedSavedObject> uploads)
    {
        if (temporaryIds.Distinct().Count() != temporaryIds.Length)
            throw ApiException.BadRequest(code: "duplicate_media_files");
        foreach (var id in temporaryIds)
        {
            var contentType = uploads[id].Object.ContentType;
            if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                && !contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
                throw ApiException.BadRequest(code: "unsupported_media_file");
        }
    }

    private static bool TryParseTemporaryFileId(string value, out int id)
    {
        id = 0;
        return value.StartsWith("file_", StringComparison.Ordinal)
            && int.TryParse(value.AsSpan(5), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out id)
            && id < 0;
    }

    private static void ValidateRequiredProductFields(Product.UserAddForm input)
    {
        if (string.IsNullOrWhiteSpace(input.sku))
            throw ApiException.BadRequest(code: "sku_required");
        if (string.IsNullOrWhiteSpace(input.name))
            throw ApiException.BadRequest(code: "product_name_required");
        if (input.sku.Length > Product.PRODUCT_SKU_MAX_LENGTH)
            throw ApiException.BadRequest(code: "sku_too_long");
        if (input.name.Length > Product.PRODUCT_NAME_MAX_LENGTH)
            throw ApiException.BadRequest(code: "product_name_too_long");
    }

    public sealed record CreateProductPayload(Product.UserAddForm Product, int[]? GalleryFileIds);
}
