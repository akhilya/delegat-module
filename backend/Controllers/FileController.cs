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

    /// <summary>
    /// Initializes a new instance of the <see cref="FileController"/> class.
    /// </summary>
    /// <param name="currentUserService">Service for resolving the current authenticated user.</param>
    /// <param name="unitOfWork">Unit of work for database operations.</param>
    /// <param name="storageService">Object storage service for file uploads and deletions.</param>
    /// <param name="serviceProvider">Service provider for dependency resolution.</param>
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
    public async Task<IActionResult> PutProductFiles(int productId, IFormFile[] files)
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

            // Stream directly to storage without buffering entire file in memory
            using var stream = file.OpenReadStream();
            await _storageService.UploadAsync(stream, filePath, file.Length, file.ContentType);
            await fileRepo.AddAsync(fileEntity);

            results.Add(fileEntity.MapToDTO(_serviceProvider));
        }

        await _unitOfWork.SaveChangesAsync();
        return Ok(results);
    }

    /// <summary>
    /// Streaming upload for very large files (multi-GB). Reads multipart form data directly without model binding.
    /// </summary>
    /// <param name="productId">The product identifier.</param>
    /// <returns>A list of file DTOs for the uploaded files.</returns>
    [HttpPost]
    [Route("/products/{productId}/files/stream")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> StreamUploadFiles(int productId)
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

        var user = await _currentUserService.GetUserAsync();
        if (user == null)
            throw ApiException.Unauthorized();

        if (!Request.HasFormContentType)
            throw ApiException.BadRequest("Ожидается multipart/form-data");

        var boundary = Request.GetMultipartBoundary();
        if (string.IsNullOrEmpty(boundary))
            throw ApiException.BadRequest("Отсутствует boundary в заголовке content-type");

        var fileRepo = _unitOfWork.Repository<Models.File>();
        var results = new List<object>();

        var reader = new MultipartReader(boundary, Request.Body);
        MultipartSection? section;

        while ((section = await reader.ReadNextSectionAsync()) != null)
        {
            var contentDisposition = ContentDispositionHeaderValue.Parse(section.ContentDisposition);

            if (!contentDisposition.IsFileDisposition())
                continue;

            var fileName = contentDisposition.FileName.Value ?? "unknown";
            var contentType = section.ContentType ?? "application/octet-stream";

            var fileId = await fileRepo.GenerateNextIdAsync();
            var filePath = $"{selectedStoreId}/{productId}/{fileId}/{fileName}";

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
                    await _storageService.UploadAsync(fileStream, filePath, contentLength, contentType);
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
                await _storageService.UploadAsync(section.Body, filePath, contentLength, contentType);
            }

            var fileEntity = new Models.File();
            fileEntity.AddFill(new Models.File.AddForm(filePath, user.Id, productId, contentType, contentLength));
            fileEntity.Id = fileId;
            await fileRepo.AddAsync(fileEntity);

            results.Add(fileEntity.MapToDTO(_serviceProvider));
        }

        await _unitOfWork.SaveChangesAsync();
        return Ok(results);
    }

    /// <summary>
    /// Deletes a product file from both the database and object storage.
    /// </summary>
    /// <param name="productId">The product identifier.</param>
    /// <param name="id">The file identifier.</param>
    /// <returns>200 OK on success.</returns>
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

        await _storageService.DeleteAsync(file.filePath);
        await fileRepo.DeleteAsync(file);
        await _unitOfWork.SaveChangesAsync();

        return Ok();
    }
}
