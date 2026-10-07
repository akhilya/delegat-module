using System.Security.Cryptography;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Security;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Delegat.Models.Products;
using Delegat.Services;

namespace Delegat.Controllers;

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
    private readonly QuotaService _quotaService;
    private readonly DataContext _context;

    public FileController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IStorageService storageService,
        IServiceProvider serviceProvider,
        ThumbnailService thumbnailService,
        QuotaService quotaService,
        DataContext context)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _storageService = storageService;
        _serviceProvider = serviceProvider;
        _thumbnailService = thumbnailService;
        _quotaService = quotaService;
        _context = context;
    }

    public record BulkMoveRequest(int? targetFolderId, int[] fileIds, int[] folderIds);

    [HttpPost]
    [Route("/products/{productId}/bulk-move")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> BulkMove(int productId, [FromBody] BulkMoveRequest request)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var productRepo = _unitOfWork.Repository<Product>();
        var product = await productRepo.GetByIdAsync(productId);

        if (product == null)
            throw ApiException.NotFound("Product not found", code: "product_not_found");

        if (product.storeId != selectedStoreId.Value)
            throw ApiException.Forbidden("Product does not belong to your store", code: "access_denied_store");

        var folderRepo = _unitOfWork.Repository<Models.ProductFolder>();

        // Validate target folder
        if (request.targetFolderId.HasValue)
        {
            var targetFolder = await folderRepo.GetByIdAsync(request.targetFolderId.Value);
            if (targetFolder == null || targetFolder.productId != productId)
                throw ApiException.NotFound("Target folder not found");
        }

        // Move files
        if (request.fileIds is { Length: > 0 })
        {
            var fileRepo = _unitOfWork.Repository<Models.File>();
            var files = await fileRepo.FindAllAsync(
                f => request.fileIds.Contains(f.Id) && f.productId == productId);

            if (files.Count != request.fileIds.Length)
                throw ApiException.NotFound("One or more files not found", code: "file_not_found");

            foreach (var file in files)
            {
                file.folderId = request.targetFolderId;
                fileRepo.UpdateWithoutSave(file);
            }
        }

        // Move folders
        if (request.folderIds is { Length: > 0 })
        {
            var folders = await folderRepo.FindAllAsync(
                f => request.folderIds.Contains(f.Id) && f.productId == productId);

            if (folders.Count != request.folderIds.Length)
                throw ApiException.NotFound("One or more folders not found");

            var movingIds = new HashSet<int>(request.folderIds);

            foreach (var folder in folders)
            {
                if (request.targetFolderId == folder.Id)
                    throw ApiException.BadRequest("Cannot move a folder into itself");

                // Cycle detection: walk up from target, if we hit any folder being moved - cycle
                if (request.targetFolderId.HasValue)
                {
                    var currentId = request.targetFolderId;
                    while (currentId.HasValue)
                    {
                        if (movingIds.Contains(currentId.Value))
                            throw ApiException.BadRequest("Cannot move a folder into its own descendant");

                        var parentFolder = await folderRepo.GetByIdAsync(currentId.Value);
                        currentId = parentFolder?.parentFolderId;
                    }
                }

                folder.parentFolderId = request.targetFolderId;
                folderRepo.UpdateWithoutSave(folder);
            }
        }

        await _unitOfWork.SaveChangesAsync();
        return Ok();
    }

    /// <summary>
    /// Downloads a file by id, proxying through backend with JWT authorization.
    /// </summary>
    [HttpGet]
    [Route("/files/{id}/download")]
    public async Task DownloadFile(int id, CancellationToken ct)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var fileRepo = _unitOfWork.Repository<Models.File>();
        var file = await fileRepo.GetByIdAsync(id);

        if (file == null)
            throw ApiException.NotFound("File not found", code: "file_not_found");

        var product = await _context.products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == file.productId);
        if (product == null || product.storeId != selectedStoreId.Value)
            throw ApiException.Forbidden("File does not belong to your store", code: "access_denied_store");

        // Verify the file exists in storage before committing response headers
        if (!await _storageService.ExistsAsync(file.filePath))
            throw ApiException.NotFound("File not found in storage", code: "file_not_found");

        var fileName = file.displayName ?? Path.GetFileName(file.filePath);
        StorageDownloadHttp.Prepare(
            Response, file.contentType, fileName, inline: file.contentType.StartsWith("image/"), file.length);
        await _storageService.StreamToAsync(file.filePath, Response.Body, ct);
    }

    /// <summary>
    /// Lists product files for a folder with offset paging (infinite scroll).
    /// Pass <c>folderId</c> (omit value for root) to scope; returns <c>items</c>, <c>totalCount</c>, <c>lastPage</c>.
    /// </summary>
    [HttpGet]
    [Route("/products/{productId}/files")]
    public async Task<IActionResult> GetProductFiles(
        int productId,
        [FromQuery] int? folderId = null,
        [FromQuery] int startIndex = 0,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var product = await _context.products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (product == null)
            throw ApiException.NotFound("Product not found", code: "product_not_found");
        if (product.storeId != selectedStoreId.Value)
            throw ApiException.Forbidden("Product does not belong to your store", code: "access_denied_store");

        if (startIndex < 0) startIndex = 0;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 200) pageSize = 200;

        var folderScoped = Request.Query.ContainsKey("folderId") || Request.Query.ContainsKey("inFolder");
        var query = _context.files
            .AsNoTracking()
            .Include(f => f.tags)
            .ThenInclude(t => t.tagDefinition)
            .Where(f => f.productId == productId);

        if (folderScoped)
            query = query.Where(f => f.folderId == folderId);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(f => f.uploadDateTime)
            .Skip(startIndex)
            .Take(pageSize)
            .ToListAsync(ct);

        return Ok(new
        {
            items = items.Select(f => f.MapToDTO(_serviceProvider)),
            totalCount = total,
            lastPage = startIndex + pageSize >= total,
        });
    }

    /// <summary>
    /// Uploads one or more files for a product. The body is stored as it arrives (up to 10 GB).
    /// </summary>
    /// <param name="productId">The product identifier.</param>
    /// <param name="folderId">Optional target folder.</param>
    /// <param name="ct">Cancels the upload.</param>
    /// <returns>A list of file DTOs for the uploaded files.</returns>
    [HttpPut]
    [Route("/products/{productId}/files")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    [DisableRequestSizeLimit]
    [DisableFormValueModelBinding]
    public Task<IActionResult> PutProductFiles(int productId, [FromQuery] int? folderId = null, CancellationToken ct = default)
        => SaveUploadedProductFiles(productId, folderId, ct);

    /// <summary>
    /// Streaming upload for very large files (multi-GB). Same path as <see cref="PutProductFiles"/>.
    /// </summary>
    /// <param name="productId">The product identifier.</param>
    /// <param name="folderId">Optional target folder.</param>
    /// <param name="ct">Cancels the upload.</param>
    /// <returns>A list of file DTOs for the uploaded files.</returns>
    [HttpPost]
    [Route("/products/{productId}/files/stream")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    [DisableRequestSizeLimit]
    [DisableFormValueModelBinding]
    public Task<IActionResult> StreamUploadFiles(int productId, [FromQuery] int? folderId = null, CancellationToken ct = default)
        => SaveUploadedProductFiles(productId, folderId, ct);

    private async Task<IActionResult> SaveUploadedProductFiles(int productId, int? folderId, CancellationToken ct)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var productRepo = _unitOfWork.Repository<Product>();
        var product = await productRepo.GetByIdAsync(productId);

        if (product == null)
            throw ApiException.NotFound("Product not found", code: "product_not_found");

        if (product.storeId != selectedStoreId.Value)
            throw ApiException.Forbidden("Product does not belong to your store", code: "access_denied_store");

        await _quotaService.EnsureFileQuotaAsync(selectedStoreId.Value);
        await _quotaService.EnsureStorageQuotaAsync(selectedStoreId.Value);

        if (folderId.HasValue)
        {
            var folderRepo = _unitOfWork.Repository<Models.ProductFolder>();
            var folder = await folderRepo.GetByIdAsync(folderId.Value);
            if (folder == null || folder.productId != productId)
                throw ApiException.NotFound("Folder not found");
        }

        var user = await _currentUserService.GetUserAsync();
        if (user == null)
            throw ApiException.Unauthorized();

        var saved = await MultipartObjectUpload.SaveAsync(
            Request,
            _storageService,
            fileName => $"{selectedStoreId}/{productId}/{Guid.NewGuid():N}/{fileName}",
            ct);

        if (saved.Count == 0)
            return Ok(Array.Empty<object>());

        try
        {
            await _quotaService.EnsureFileQuotaAsync(selectedStoreId.Value, saved.Count);
            await _quotaService.EnsureStorageQuotaAsync(selectedStoreId.Value, saved.Sum(file => file.Length));
        }
        catch
        {
            foreach (var item in saved)
                await _storageService.DeleteAsync(item.Path, ct);
            throw;
        }

        var fileRepo = _unitOfWork.Repository<Models.File>();
        var fileEntities = new List<Models.File>();
        foreach (var item in saved)
        {
            var fileEntity = new Models.File();
            fileEntity.AddFill(new Models.File.AddForm(item.Path, user.Id, productId, item.ContentType, item.Length));
            fileEntity.folderId = folderId;
            await fileRepo.AddWithoutSaveAsync(fileEntity);
            fileEntities.Add(fileEntity);
        }

        await _unitOfWork.SaveChangesAsync();
        return Ok(fileEntities.Select(e => e.MapToDTO(_serviceProvider)).ToList());
    }

    /// <summary>
    /// Request body for moving a file into a folder.
    /// </summary>
    /// <param name="folderId">Target folder id, or null for root.</param>
    public record MoveFileRequest(int? folderId);

    [HttpPatch]
    [Route("/products/{productId}/files/{id}/move")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> MoveFile(int productId, int id, [FromBody] MoveFileRequest request)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var productRepo = _unitOfWork.Repository<Product>();
        var product = await productRepo.GetByIdAsync(productId);

        if (product == null)
            throw ApiException.NotFound("Product not found", code: "product_not_found");

        if (product.storeId != selectedStoreId.Value)
            throw ApiException.Forbidden("Product does not belong to your store", code: "access_denied_store");

        var fileRepo = _unitOfWork.Repository<Models.File>();
        var file = await fileRepo.GetByIdTrackingAsync(id);

        if (file == null || file.productId != productId)
            throw ApiException.NotFound("File not found", code: "file_not_found");

        if (request.folderId.HasValue)
        {
            var folderRepo = _unitOfWork.Repository<Models.ProductFolder>();
            var folder = await folderRepo.GetByIdAsync(request.folderId.Value);
            if (folder == null || folder.productId != productId)
                throw ApiException.NotFound("Folder not found");
        }

        file.folderId = request.folderId;
        await _unitOfWork.SaveChangesAsync();

        return Ok(file.MapToDTO(_serviceProvider));
    }

    public record RenameFileRequest(string name);

    [HttpPatch]
    [Route("/products/{productId}/files/{id}/rename")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> RenameFile(int productId, int id, [FromBody] RenameFileRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.name))
            throw ApiException.BadRequest("File name is required", code: "required_fields_missing");

        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var productRepo = _unitOfWork.Repository<Product>();
        var product = await productRepo.GetByIdAsync(productId);

        if (product == null)
            throw ApiException.NotFound("Product not found", code: "product_not_found");

        if (product.storeId != selectedStoreId.Value)
            throw ApiException.Forbidden("Product does not belong to your store", code: "access_denied_store");

        var fileRepo = _unitOfWork.Repository<Models.File>();
        var file = await fileRepo.GetByIdTrackingAsync(id);

        if (file == null || file.productId != productId)
            throw ApiException.NotFound("File not found", code: "file_not_found");

        var sanitizedName = request.name.Trim();
        file.displayName = sanitizedName;
        await _unitOfWork.SaveChangesAsync();

        return Ok(file.MapToDTO(_serviceProvider));
    }

    /// <summary>
    /// Deletes a product file from both the database and object storage.
    /// </summary>
    /// <param name="productId">The product identifier.</param>
    /// <param name="id">The file identifier.</param>
    /// <returns>200 OK on success.</returns>
    [HttpDelete]
    [Route("/products/{productId}/files/{id}")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> DeleteProductFile(int productId, int id)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var productRepo = _unitOfWork.Repository<Product>();
        var product = await productRepo.GetByIdAsync(productId);

        if (product == null)
            throw ApiException.NotFound("Product not found", code: "product_not_found");

        if (product.storeId != selectedStoreId.Value)
            throw ApiException.Forbidden("Product does not belong to your store", code: "access_denied_store");

        var fileRepo = _unitOfWork.Repository<Models.File>();
        var file = await fileRepo.GetByIdAsync(id);

        if (file == null)
            throw ApiException.NotFound("File not found", code: "file_not_found");

        // Delete tags before file (FK constraint)
        await _context.tags.Where(t => t.fileId == id).ExecuteDeleteAsync();

        await _thumbnailService.DeleteThumbnailsAsync(file.filePath);
        await _storageService.DeleteAsync(file.filePath);
        await fileRepo.DeleteAsync(file);
        await _unitOfWork.SaveChangesAsync();

        return Ok();
    }

    /// <summary>
    /// Bulk-deletes multiple files for a product in a single request.
    /// </summary>
    public record BulkDeleteFilesRequest(int[] fileIds);

    [HttpPost]
    [Route("/products/{productId}/files/bulk-delete")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> BulkDeleteProductFiles(int productId, [FromBody] BulkDeleteFilesRequest request)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var productRepo = _unitOfWork.Repository<Product>();
        var product = await productRepo.GetByIdAsync(productId);

        if (product == null)
            throw ApiException.NotFound("Product not found", code: "product_not_found");

        if (product.storeId != selectedStoreId.Value)
            throw ApiException.Forbidden("Product does not belong to your store", code: "access_denied_store");

        if (request.fileIds == null || request.fileIds.Length == 0)
            throw ApiException.BadRequest("No files specified", code: "required_fields_missing");

        var fileRepo = _unitOfWork.Repository<Models.File>();
        var files = await fileRepo.FindAllAsync(f => request.fileIds.Contains(f.Id) && f.productId == productId);
        var deletedCount = 0;

        // Delete tags before files (FK constraint)
        var fileIds = files.Select(f => f.Id).ToList();
        if (fileIds.Any())
            await _context.tags.Where(t => fileIds.Contains(t.fileId)).ExecuteDeleteAsync();

        foreach (var file in files)
        {
            await _thumbnailService.DeleteThumbnailsAsync(file.filePath);
            await _storageService.DeleteAsync(file.filePath);
            fileRepo.DeleteWithoutSave(file);
            deletedCount++;
        }

        await _unitOfWork.SaveChangesAsync();
        return Ok(new { deletedCount });
    }

    /// <summary>
    /// Creates a temporary share link for a file.
    /// </summary>
    public record ShareFileRequest(int ttlHours);

    [HttpPost]
    [Route("/files/{id}/share")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> ShareFile(int id, [FromBody] ShareFileRequest request)
    {
        var allowedTtl = new[] { 1, 24, 168, 720 };
        if (!allowedTtl.Contains(request.ttlHours))
            throw ApiException.BadRequest("Invalid TTL");

        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var fileRepo = _unitOfWork.Repository<Models.File>();
        var file = await fileRepo.GetByIdAsync(id);

        if (file == null)
            throw ApiException.NotFound("File not found", code: "file_not_found");

        var product = await _context.products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == file.productId);
        if (product == null || product.storeId != selectedStoreId.Value)
            throw ApiException.Forbidden("File does not belong to your store", code: "access_denied_store");

        var user = await _currentUserService.GetUserAsync();
        if (user == null)
            throw ApiException.Unauthorized();

        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Base64UrlEncoder.Encode(tokenBytes);

        var shareToken = new FileShareToken
        {
            fileId = id,
            token = token,
            expiresAt = DateTime.UtcNow.AddHours(request.ttlHours),
            createdAt = DateTime.UtcNow,
            createdByUserId = user.Id
        };

        var shareRepo = _unitOfWork.Repository<FileShareToken>();
        await shareRepo.AddWithoutSaveAsync(shareToken);
        await _unitOfWork.SaveChangesAsync();

        return Ok(new
        {
            url = $"/api/shared/{token}",
            token,
            expiresAt = shareToken.expiresAt
        });
    }

    [HttpDelete]
    [Route("/files/shares/{token}")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> RevokeShare(string token)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var share = await _context.fileShareTokens
            .Include(t => t.file)
            .FirstOrDefaultAsync(t => t.token == token);
        if (share == null)
            throw ApiException.NotFound("Link not found");

        var product = await _context.products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == share.file.productId);
        if (product == null || product.storeId != selectedStoreId.Value)
            throw ApiException.Forbidden("Link does not belong to your store", code: "access_denied_store");

        _context.fileShareTokens.Remove(share);
        await _context.SaveChangesAsync();
        return Ok(new { success = true });
    }

    public record SetPublicRequest(bool isPublic);

    [HttpPatch]
    [Route("/products/{productId}/files/{id}/public")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> SetFilePublic(int productId, int id, [FromBody] SetPublicRequest request)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var productRepo = _unitOfWork.Repository<Product>();
        var product = await productRepo.GetByIdAsync(productId);

        if (product == null)
            throw ApiException.NotFound("Product not found", code: "product_not_found");

        if (product.storeId != selectedStoreId.Value)
            throw ApiException.Forbidden("Product does not belong to your store", code: "access_denied_store");

        var fileRepo = _unitOfWork.Repository<Models.File>();
        var file = await fileRepo.GetByIdTrackingAsync(id);

        if (file == null || file.productId != productId)
            throw ApiException.NotFound("File not found", code: "file_not_found");

        file.isPublic = request.isPublic;
        if (request.isPublic)
            file.publicToken ??= Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        else
            file.publicToken = null;

        await _unitOfWork.SaveChangesAsync();

        return Ok(file.MapToDTO(_serviceProvider));
    }
}

/// <summary>
/// Public controller for accessing shared file links (no auth required).
/// </summary>
[ApiController]
public class SharedFileController : ControllerBase
{
    private readonly DataContext _db;
    private readonly IStorageService _storageService;

    public SharedFileController(DataContext db, IStorageService storageService)
    {
        _db = db;
        _storageService = storageService;
    }

    [HttpGet]
    [Route("/shared/{token}")]
    public async Task GetSharedFile(string token, CancellationToken ct)
    {
        var shareToken = await _db.fileShareTokens
            .Include(t => t.file)
            .FirstOrDefaultAsync(t => t.token == token);

        if (shareToken == null || shareToken.IsExpired)
            throw ApiException.NotFound("Link not found or expired");

        // Verify the file exists in storage before committing response headers
        if (!await _storageService.ExistsAsync(shareToken.file.filePath))
            throw ApiException.NotFound("File not found in storage", code: "file_not_found");

        var fileName = shareToken.file.displayName ?? Path.GetFileName(shareToken.file.filePath);
        StorageDownloadHttp.Prepare(
            Response,
            shareToken.file.contentType,
            fileName,
            inline: shareToken.file.contentType.StartsWith("image/"),
            shareToken.file.length);
        await _storageService.StreamToAsync(shareToken.file.filePath, Response.Body, ct);
    }
}

/// <summary>
/// Serves permanently public product files - no authentication required.
/// Files must have isPublic = true to be accessible here.
/// URL format: /api/pub/files/{token}/{filename} - filename segment is decorative;
/// the server always serves the actual file from the DB record.
/// </summary>
[ApiController]
public class PublicFileController : ControllerBase
{
    private readonly DataContext _db;
    private readonly IStorageService _storageService;

    public PublicFileController(DataContext db, IStorageService storageService)
    {
        _db = db;
        _storageService = storageService;
    }

    [HttpGet]
    [Route("/pub/files/{token}/{filename}")]
    public async Task GetPublicFile(string token, string filename, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < 16)
            throw ApiException.NotFound("File not found", code: "file_not_found");

        var file = await _db.files.FirstOrDefaultAsync(f => f.publicToken == token && f.isPublic);

        if (file == null)
            throw ApiException.NotFound("File not found", code: "file_not_found");

        if (!await _storageService.ExistsAsync(file.filePath))
            throw ApiException.NotFound("File not found in storage", code: "file_not_found");

        var actualName = file.displayName ?? Path.GetFileName(file.filePath);
        StorageDownloadHttp.Prepare(
            Response, file.contentType, actualName, inline: file.contentType.StartsWith("image/"), file.length);
        Response.Headers["Cache-Control"] = "private, max-age=3600";
        await _storageService.StreamToAsync(file.filePath, Response.Body, ct);
    }

    [HttpGet]
    [Route("/pub/store-files/{token}/{filename}")]
    public async Task GetPublicStoreFile(string token, string filename, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < 16)
            throw ApiException.NotFound("File not found", code: "file_not_found");

        var file = await _db.storeFiles.FirstOrDefaultAsync(f => f.publicToken == token && f.isPublic);

        if (file == null)
            throw ApiException.NotFound("File not found", code: "file_not_found");

        if (!await _storageService.ExistsAsync(file.filePath))
            throw ApiException.NotFound("File not found in storage", code: "file_not_found");

        var actualName = file.displayName ?? Path.GetFileName(file.filePath);
        StorageDownloadHttp.Prepare(
            Response, file.contentType, actualName, inline: file.contentType.StartsWith("image/"), file.length);
        Response.Headers["Cache-Control"] = "private, max-age=3600";
        await _storageService.StreamToAsync(file.filePath, Response.Body, ct);
    }
}
