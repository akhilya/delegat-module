using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using PrintO.Infrastructure.Auth;
using PrintO.Infrastructure.Data;
using PrintO.Infrastructure.Middleware;
using PrintO.Infrastructure.Storage;
using PrintO.Models;
using PrintO.Models.Products;
using PrintO.Services;

namespace PrintO.Controllers;

/// <summary>
/// Controller for uploading and deleting product files in object storage.
/// </summary>
[ApiController]
[JwtAuthorize]
public class FileController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IStorageService _storageService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ThumbnailService _thumbnailService;

    public FileController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IStorageService storageService,
        IServiceProvider serviceProvider,
        ThumbnailService thumbnailService)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _storageService = storageService;
        _serviceProvider = serviceProvider;
        _thumbnailService = thumbnailService;
    }

    /// <summary>
    /// Uploads one or more files for a product using standard model binding (up to 10 GB).
    /// </summary>
    /// <param name="productId">The product identifier.</param>
    /// <param name="files">The files to upload.</param>
    /// <returns>A list of file DTOs for the uploaded files.</returns>
    [HttpPut]
    [Route("/products/{productId}/files")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 10L * 1024 * 1024 * 1024)] // 10GB
    public async Task<IActionResult> PutProductFiles(int productId, IFormFile[] files, [FromQuery] int? folderId = null)
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

        if (folderId.HasValue)
        {
            var folderRepo = _unitOfWork.Repository<Models.ProductFolder>();
            var folder = await folderRepo.GetByIdAsync(folderId.Value);
            if (folder == null || folder.productId != productId)
                throw ApiException.NotFound("Папка не найдена");
        }

        var user = await _currentUserService.GetUserAsync();
        if (user == null)
            throw ApiException.Unauthorized();

        var fileRepo = _unitOfWork.Repository<Models.File>();
        var fileEntities = new List<Models.File>();

        foreach (var file in files)
        {
            var uniqueKey = Guid.NewGuid().ToString("N");
            var filePath = $"{selectedStoreId}/{productId}/{uniqueKey}/{file.FileName}";

            var fileEntity = new Models.File();
            fileEntity.AddFill(new Models.File.AddForm(filePath, user.Id, productId, file.ContentType, file.Length));
            fileEntity.folderId = folderId;

            // Stream directly to storage without buffering entire file in memory
            using var stream = file.OpenReadStream();
            var uploaded = await _storageService.UploadAsync(stream, filePath, file.Length, file.ContentType);
            if (!uploaded)
                throw ApiException.InternalServerError($"Не удалось загрузить файл {file.FileName} в хранилище");
            await fileRepo.AddWithoutSaveAsync(fileEntity);
            fileEntities.Add(fileEntity);
        }

        await _unitOfWork.SaveChangesAsync();

        return Ok(fileEntities.Select(e => e.MapToDTO(_serviceProvider)).ToList());
    }

    /// <summary>
    /// Streaming upload for very large files (multi-GB). Reads multipart form data directly without model binding.
    /// </summary>
    /// <param name="productId">The product identifier.</param>
    /// <returns>A list of file DTOs for the uploaded files.</returns>
    [HttpPost]
    [Route("/products/{productId}/files/stream")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> StreamUploadFiles(int productId, [FromQuery] int? folderId = null)
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

        if (folderId.HasValue)
        {
            var folderRepo = _unitOfWork.Repository<Models.ProductFolder>();
            var folder = await folderRepo.GetByIdAsync(folderId.Value);
            if (folder == null || folder.productId != productId)
                throw ApiException.NotFound("Папка не найдена");
        }

        var user = await _currentUserService.GetUserAsync();
        if (user == null)
            throw ApiException.Unauthorized();

        if (!Request.HasFormContentType)
            throw ApiException.BadRequest("Ожидается multipart/form-data");

        var boundary = Request.GetMultipartBoundary();
        if (string.IsNullOrEmpty(boundary))
            throw ApiException.BadRequest("Отсутствует boundary в заголовке content-type");

        var fileRepo = _unitOfWork.Repository<Models.File>();
        var fileEntities = new List<Models.File>();

        var reader = new MultipartReader(boundary, Request.Body);
        MultipartSection? section;

        while ((section = await reader.ReadNextSectionAsync()) != null)
        {
            var contentDisposition = ContentDispositionHeaderValue.Parse(section.ContentDisposition);

            if (!contentDisposition.IsFileDisposition())
                continue;

            var fileName = contentDisposition.FileName.Value ?? "unknown";
            var contentType = section.ContentType ?? "application/octet-stream";

            var uniqueKey = Guid.NewGuid().ToString("N");
            var filePath = $"{selectedStoreId}/{productId}/{uniqueKey}/{fileName}";

            // Get content length from headers if available
            long contentLength = 0;
            if (section.Headers != null && section.Headers.TryGetValue("Content-Length", out var lengthHeader))
            {
                long.TryParse(lengthHeader.FirstOrDefault(), out contentLength);
            }

            // For streaming upload, we need to buffer to temp file first if size unknown
            if (contentLength == 0)
            {
                // Stream to temp file first to get size
                var tempPath = Path.GetTempFileName();
                try
                {
                    using (var tempStream = System.IO.File.Create(tempPath))
                    {
                        await section.Body.CopyToAsync(tempStream);
                        contentLength = tempStream.Length;
                    }

                    using var fileStream = System.IO.File.OpenRead(tempPath);
                    var uploaded = await _storageService.UploadAsync(fileStream, filePath, contentLength, contentType);
                    if (!uploaded)
                        throw ApiException.InternalServerError($"Не удалось загрузить файл {fileName} в хранилище");
                }
                finally
                {
                    if (System.IO.File.Exists(tempPath))
                        System.IO.File.Delete(tempPath);
                }
            }
            else
            {
                // Direct stream upload when size is known
                var uploaded = await _storageService.UploadAsync(section.Body, filePath, contentLength, contentType);
                if (!uploaded)
                    throw ApiException.InternalServerError($"Не удалось загрузить файл {fileName} в хранилище");
            }

            var fileEntity = new Models.File();
            fileEntity.AddFill(new Models.File.AddForm(filePath, user.Id, productId, contentType, contentLength));
            fileEntity.folderId = folderId;
            await fileRepo.AddWithoutSaveAsync(fileEntity);
            fileEntities.Add(fileEntity);
        }

        await _unitOfWork.SaveChangesAsync();
        return Ok(fileEntities.Select(e => e.MapToDTO(_serviceProvider)).ToList());
    }

    /// <summary>
    /// Deletes a product file from both the database and object storage.
    /// </summary>
    /// <param name="productId">The product identifier.</param>
    /// <param name="id">The file identifier.</param>
    /// <returns>200 OK on success.</returns>
    public record MoveFileRequest(int? folderId);

    [HttpPatch]
    [Route("/products/{productId}/files/{id}/move")]
    public async Task<IActionResult> MoveFile(int productId, int id, [FromBody] MoveFileRequest request)
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

        var fileRepo = _unitOfWork.Repository<Models.File>();
        var file = await fileRepo.GetByIdTrackingAsync(id);

        if (file == null || file.productId != productId)
            throw ApiException.NotFound("Файл не найден");

        if (request.folderId.HasValue)
        {
            var folderRepo = _unitOfWork.Repository<Models.ProductFolder>();
            var folder = await folderRepo.GetByIdAsync(request.folderId.Value);
            if (folder == null || folder.productId != productId)
                throw ApiException.NotFound("Папка не найдена");
        }

        file.folderId = request.folderId;
        await _unitOfWork.SaveChangesAsync();

        return Ok(file.MapToDTO(_serviceProvider));
    }

    [HttpDelete]
    [Route("/products/{productId}/files/{id}")]
    public async Task<IActionResult> DeleteProductFile(int productId, int id)
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

        var fileRepo = _unitOfWork.Repository<Models.File>();
        var file = await fileRepo.GetByIdAsync(id);

        if (file == null)
            throw ApiException.NotFound("Файл не найден");

        await _thumbnailService.DeleteThumbnailsAsync(file.filePath);
        await _storageService.DeleteAsync(file.filePath);
        await fileRepo.DeleteAsync(file);
        await _unitOfWork.SaveChangesAsync();

        return Ok();
    }
}
