using Microsoft.AspNetCore.Mvc;
using PrintO.Infrastructure.Auth;
using PrintO.Infrastructure.Data;
using PrintO.Infrastructure.Middleware;
using PrintO.Models;
using PrintO.Models.Products;
using static PrintO.Models.FileTag;

namespace PrintO.Controllers;

[ApiController]
[JwtAuthorize]
public class FileTagController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceProvider _serviceProvider;

    public FileTagController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
    }

    [HttpPost]
    [Route("/products/{productId}/files/{fileId}/tags")]
    public async Task<IActionResult> PostFileTag(int productId, int fileId, [FromBody] UserAddForm input)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var fileRepo = _unitOfWork.Repository<Models.File>();
        var file = await fileRepo.FindAsync(f => f.productId == productId && f.Id == fileId);

        if (file == null)
            throw ApiException.NotFound("Файл не найден");

        var tagRepo = _unitOfWork.Repository<FileTag>();
        var tag = new FileTag();
        tag.AddFill(new AddForm(input, fileId));
        await tagRepo.AddAsync(tag);
        await _unitOfWork.SaveChangesAsync();

        return Ok();
    }

    [HttpPatch]
    [Route("/products/{productId}/files/{fileId}/tags/{id}")]
    public async Task<IActionResult> PatchFileTag(int productId, int fileId, int id, [FromBody] UpdateForm input)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var productRepo = _unitOfWork.Repository<Product>();
        var product = await productRepo.FindAsync(p => p.storeId == selectedStoreId.Value && p.Id == productId);

        if (product == null)
            throw ApiException.NotFound("Товар не найден");

        var tagRepo = _unitOfWork.Repository<FileTag>();
        var tag = await tagRepo.FindAsync(t => t.fileId == fileId && t.Id == id);

        if (tag == null)
            throw ApiException.NotFound("Тег не найден");

        tag.UpdateFill(input);
        await tagRepo.UpdateAsync(tag);
        await _unitOfWork.SaveChangesAsync();

        return Ok();
    }

    [HttpDelete]
    [Route("/products/{productId}/files/{fileId}/tags/{id}")]
    public async Task<IActionResult> DeleteFileTag(int productId, int fileId, int id)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var productRepo = _unitOfWork.Repository<Product>();
        var product = await productRepo.FindAsync(p => p.storeId == selectedStoreId.Value && p.Id == productId);

        if (product == null)
            throw ApiException.NotFound("Товар не найден");

        var tagRepo = _unitOfWork.Repository<FileTag>();
        var tag = await tagRepo.FindAsync(t => t.fileId == fileId && t.Id == id);

        if (tag == null)
            throw ApiException.NotFound("Тег не найден");

        await tagRepo.DeleteAsync(tag);
        await _unitOfWork.SaveChangesAsync();

        return Ok();
    }
}
