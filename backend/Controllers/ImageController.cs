using Microsoft.AspNetCore.Mvc;
using PrintO.Infrastructure.Auth;
using PrintO.Infrastructure.Data;
using PrintO.Infrastructure.Middleware;
using PrintO.Models;
using PrintO.Models.Products;

namespace PrintO.Controllers;

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
        var initialImages = (await imageRepo.FindAllAsync(i => i.productId == productId)).ToArray();
        var initialImagesCount = initialImages.Length;

        // Remove excess images
        if (initialImagesCount > files.Length)
        {
            for (int i = 0; i < initialImagesCount - files.Length; i++)
            {
                var imageToRemove = initialImages[initialImages.Length - i - 1];
                await imageRepo.DeleteAsync(imageToRemove);
            }
        }

        // Add new images
        int orderCounter = 0;
        if (initialImagesCount < files.Length)
        {
            for (int i = 0; i < files.Length - initialImagesCount; i++)
            {
                var imageIndex = files.Length - (++orderCounter);
                var addForm = new ImageReference.AddForm(files[files.Length - i - 1], productId, imageIndex);
                var newImage = new ImageReference();
                newImage.AddFill(addForm);
                await imageRepo.AddAsync(newImage);
            }
        }

        // Update existing images
        for (int i = 0; i < Math.Min(initialImagesCount, files.Length); i++)
        {
            var imageIndex = orderCounter++;
            var updateForm = new ImageReference.UpdateForm(i, files[i]);
            initialImages[i].UpdateFill(updateForm);
            await imageRepo.UpdateAsync(initialImages[i]);
        }

        await _unitOfWork.SaveChangesAsync();
        return Ok();
    }
}
