using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Services;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
public class ThumbnailController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ThumbnailService _thumbnailService;
    private readonly DataContext _context;

    public ThumbnailController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        ThumbnailService thumbnailService,
        DataContext context)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _thumbnailService = thumbnailService;
        _context = context;
    }

    [HttpGet]
    [Route("/files/{id}/thumbnail")]
    [DisableRateLimiting]
    [ResponseCache(Duration = 31536000, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetThumbnail(int id, [FromQuery] int w = 256, CancellationToken ct = default)
    {
        if (!ThumbnailService.IsAllowedWidth(w))
            throw ApiException.BadRequest($"Invalid size. Allowed: 64, 128, 256, 512");

        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var fileRepo = _unitOfWork.Repository<Models.File>();
        var file = await fileRepo.GetByIdAsync(id);

        if (file == null)
            throw ApiException.NotFound("File not found", code: "file_not_found");

        // Include soft-deleted products — files remain accessible in trash views
        var product = await _context.products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == file.productId, ct);
        if (product == null || product.storeId != selectedStoreId.Value)
            throw ApiException.Forbidden("File does not belong to your store", code: "access_denied_store");

        if (!file.contentType.StartsWith("image/"))
            throw ApiException.BadRequest("File is not an image");

        var stream = await _thumbnailService.GetOrGenerateAsync(file.filePath, w, ct);
        if (stream == null)
            throw ApiException.InternalServerError("Failed to create thumbnail");

        Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
        return File(stream, "image/webp");
    }
}
