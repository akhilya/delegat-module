using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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

    public ThumbnailController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        ThumbnailService thumbnailService)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _thumbnailService = thumbnailService;
    }

    [HttpGet]
    [Route("/files/{id}/thumbnail")]
    [DisableRateLimiting]
    [ResponseCache(Duration = 31536000, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetThumbnail(int id, [FromQuery] int w = 256, CancellationToken ct = default)
    {
        if (!ThumbnailService.IsAllowedWidth(w))
            throw ApiException.BadRequest($"Недопустимый размер. Допустимые: 64, 128, 256, 512");

        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var fileRepo = _unitOfWork.Repository<Models.File>();
        var file = await fileRepo.GetByIdAsync(id);

        if (file == null)
            throw ApiException.NotFound("Файл не найден");

        // Check store ownership via product
        var productRepo = _unitOfWork.Repository<Models.Products.Product>();
        var product = await productRepo.GetByIdAsync(file.productId);
        if (product == null || product.storeId != selectedStoreId.Value)
            throw ApiException.Forbidden("Файл не принадлежит вашему магазину");

        if (!file.contentType.StartsWith("image/"))
            throw ApiException.BadRequest("Файл не является изображением");

        var stream = await _thumbnailService.GetOrGenerateAsync(file.filePath, w, ct);
        if (stream == null)
            throw ApiException.InternalServerError("Не удалось создать миниатюру");

        Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
        return File(stream, "image/webp");
    }
}
