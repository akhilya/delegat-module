using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Products;

namespace Delegat.Controllers;

/// <summary>
/// Controller for managing product image references and their display order.
/// </summary>
[ApiController]
[JwtAuthorize]
public class ImageController : ControllerBase
{
    private const int MaxGalleryItems = 200;
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly DataContext _context;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageController"/> class.
    /// </summary>
    /// <param name="currentUserService">Service for resolving the current authenticated user.</param>
    /// <param name="unitOfWork">Unit of work for database operations.</param>
    /// <param name="context">Database context for tracked media replacement.</param>
    /// <param name="serviceProvider">Service provider for dependency resolution.</param>
    public ImageController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        DataContext context,
        IServiceProvider serviceProvider)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _context = context;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Replaces the image references for a product, synchronizing additions, removals, and order changes.
    /// </summary>
    /// <param name="productId">The product identifier.</param>
    /// <param name="files">Ordered array of file IDs that should be the product's images.</param>
    /// <returns>200 OK on success.</returns>
    [HttpPost]
    [Route("products/{productId}/images")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> PostProductImages(int productId, int[]? files)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var productRepo = _unitOfWork.Repository<Product>();
        var product = await productRepo.GetByIdAsync(productId);

        if (product == null)
            throw ApiException.NotFound(code: "product_not_found");

        if (product.storeId != selectedStoreId.Value)
            throw ApiException.Forbidden(code: "access_denied_store");

        files ??= [];
        await ValidateMediaFilesAsync(productId, files, imagesOnly: true);
        var imageRepo = _unitOfWork.Repository<ImageReference>();
        var existingMedia = await _context.images
            .Where(i => i.productId == productId)
            .ToListAsync();
        var existingIds = existingMedia.Select(i => i.fileId).Distinct().ToArray();
        var existingFiles = await _context.files.AsNoTracking()
            .Where(f => existingIds.Contains(f.Id) && f.productId == productId)
            .ToListAsync();
        var fileById = existingFiles.ToDictionary(f => f.Id);

        // Legacy image replacement only replaces image slots. Videos keep their relative
        // positions; surplus replacement images are appended after the existing sequence.
        var nextImage = 0;
        var replacementMedia = new List<int>();
        foreach (var reference in existingMedia.OrderBy(i => i.index))
        {
            if (!fileById.TryGetValue(reference.fileId, out var currentFile))
                continue;
            if (currentFile.contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
                replacementMedia.Add(reference.fileId);
            else if (currentFile.contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                && nextImage < files.Length)
                replacementMedia.Add(files[nextImage++]);
        }
        while (nextImage < files.Length)
            replacementMedia.Add(files[nextImage++]);

        await ReplaceMediaReferencesAsync(productId, replacementMedia, imageRepo, existingMedia);
        return Ok();
    }

    /// <summary>Replaces the ordered image/video media gallery for a product.</summary>
    [HttpPost]
    [Route("products/{productId}/media")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> PostProductMedia(int productId, int[] files)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var product = await _unitOfWork.Repository<Product>().GetByIdAsync(productId);
        if (product == null)
            throw ApiException.NotFound(code: "product_not_found");
        if (product.storeId != selectedStoreId.Value)
            throw ApiException.Forbidden(code: "access_denied_store");

        await ValidateMediaFilesAsync(productId, files, imagesOnly: false);

        files ??= [];
        if (files.Length > MaxGalleryItems)
            throw ApiException.BadRequest(code: "too_many_gallery_media_items");

        var imageRepo = _unitOfWork.Repository<ImageReference>();
        var existing = await _context.images
            .Where(i => i.productId == productId)
            .ToListAsync();
        await ReplaceMediaReferencesAsync(productId, files, imageRepo, existing);
        return Ok();
    }

    private async Task ReplaceMediaReferencesAsync(
        int productId,
        IReadOnlyList<int> fileIds,
        Delegat.Infrastructure.Data.Repository<ImageReference> imageRepo,
        IReadOnlyCollection<ImageReference> existing)
    {
        if (existing.Count > 0)
            imageRepo.DeleteRange(existing.ToList());

        for (var index = 0; index < fileIds.Count; index++)
        {
            var reference = new ImageReference();
            reference.AddFill(new ImageReference.AddForm(fileIds[index], productId, index));
            await imageRepo.AddWithoutSaveAsync(reference);
        }

        await _unitOfWork.SaveChangesAsync();
    }

    private async Task ValidateMediaFilesAsync(int productId, int[]? fileIds, bool imagesOnly)
    {
        fileIds ??= [];
        if (fileIds.Length > MaxGalleryItems)
            throw ApiException.BadRequest(code: "too_many_gallery_media_items");
        if (fileIds.Distinct().Count() != fileIds.Length)
            throw ApiException.BadRequest(code: "duplicate_media_files");

        var files = await _unitOfWork.Repository<Models.File>()
            .FindAllAsync(f => f.productId == productId && fileIds.Contains(f.Id));
        if (files.Count != fileIds.Length)
            throw ApiException.BadRequest(code: "file_not_found");

        if (files.Any(f => !IsSupportedMedia(f.contentType, imagesOnly)))
            throw ApiException.BadRequest(code: imagesOnly ? "image_file_required" : "unsupported_media_file");
    }

    private static bool IsSupportedMedia(string? contentType, bool imagesOnly)
        => !string.IsNullOrWhiteSpace(contentType)
            && (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                || (!imagesOnly && contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)));
}
