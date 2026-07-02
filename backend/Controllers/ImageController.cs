using Microsoft.AspNetCore.Mvc;
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
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageController"/> class.
    /// </summary>
    /// <param name="currentUserService">Service for resolving the current authenticated user.</param>
    /// <param name="unitOfWork">Unit of work for database operations.</param>
    /// <param name="serviceProvider">Service provider for dependency resolution.</param>
    public ImageController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
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
    public async Task<IActionResult> PostProductImages(int productId, int[] files)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var productRepo = _unitOfWork.Repository<Product>();
        var product = await productRepo.GetByIdAsync(productId);

        if (product == null)
            throw ApiException.NotFound("Товар не найден");

        if (product.storeId != selectedStoreId.Value)
            throw ApiException.Forbidden("Товар не принадлежит вашему магазину");

        var imageRepo = _unitOfWork.Repository<ImageReference>();
        var existingImages = await imageRepo.FindAllAsync(i => i.productId == productId);

        // Replace all: delete existing, then recreate in the desired order.
        // This is idempotent and avoids concurrency issues with stale entities
        // when multiple drag-and-drop requests arrive in quick succession.
        if (existingImages.Any())
            imageRepo.DeleteRange(existingImages);

        for (int i = 0; i < files.Length; i++)
        {
            var newImage = new ImageReference();
            newImage.AddFill(new ImageReference.AddForm(files[i], productId, i));
            await imageRepo.AddWithoutSaveAsync(newImage);
        }

        await _unitOfWork.SaveChangesAsync();
        return Ok();
    }
}
