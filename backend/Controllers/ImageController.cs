using Microsoft.AspNetCore.Mvc;
using PrintO.Infrastructure.Auth;
using PrintO.Infrastructure.Data;
using PrintO.Infrastructure.Middleware;
using PrintO.Models;
using PrintO.Models.Products;

namespace PrintO.Controllers;

[ApiController]
[JwtAuthorize]
public class ImageController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceProvider _serviceProvider;

    public ImageController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
    }

    [HttpPost]
    [Route("products/{productId}/images")]
    public async Task<IActionResult> PostProductImages(int productId, int[] files)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("No store selected");

        var productRepo = _unitOfWork.Repository<Product>();
        var product = await productRepo.GetByIdAsync(productId);

        if (product == null)
            throw ApiException.NotFound("Product not found");

        if (product.storeId != selectedStoreId.Value)
            throw ApiException.Forbidden("Product does not belong to your store");

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
