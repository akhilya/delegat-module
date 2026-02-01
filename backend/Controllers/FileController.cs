using Microsoft.AspNetCore.Mvc;
using PrintO.Infrastructure.Auth;
using PrintO.Infrastructure.Data;
using PrintO.Infrastructure.Middleware;
using PrintO.Infrastructure.Storage;
using PrintO.Models;
using PrintO.Models.Products;

namespace PrintO.Controllers;

[ApiController]
[JwtAuthorize]
public class FileController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IStorageService _storageService;
    private readonly IServiceProvider _serviceProvider;

    public FileController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IStorageService storageService,
        IServiceProvider serviceProvider)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _storageService = storageService;
        _serviceProvider = serviceProvider;
    }

    [HttpPut]
    [Route("/products/{productId}/files")]
    [RequestSizeLimit((long)2e+9)] // 2Gb
    public async Task<IActionResult> PutProductFiles(int productId, IFormFile[] files)
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

        var user = await _currentUserService.GetUserAsync();
        if (user == null)
            throw ApiException.Unauthorized();

        var fileRepo = _unitOfWork.Repository<Models.File>();
        var results = new List<object>();

        foreach (var file in files)
        {
            var fileId = await fileRepo.GenerateNextIdAsync();
            var filePath = $"{selectedStoreId}/{productId}/{fileId}/{file.FileName}";

            var fileEntity = new Models.File();
            fileEntity.AddFill(new Models.File.AddForm(filePath, user.Id, productId, file.ContentType, file.Length));
            fileEntity.Id = fileId;

            await _storageService.UploadAsync(file, filePath);
            await fileRepo.AddAsync(fileEntity);

            results.Add(fileEntity.MapToDTO(_serviceProvider));
        }

        await _unitOfWork.SaveChangesAsync();
        return Ok(results);
    }

    [HttpDelete]
    [Route("/products/{productId}/files/{id}")]
    public async Task<IActionResult> DeleteProductFile(int productId, int id)
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

        var fileRepo = _unitOfWork.Repository<Models.File>();
        var file = await fileRepo.GetByIdAsync(id);

        if (file == null)
            throw ApiException.NotFound("File not found");

        await _storageService.DeleteAsync(file.filePath);
        await fileRepo.DeleteAsync(file);
        await _unitOfWork.SaveChangesAsync();

        return Ok();
    }
}
