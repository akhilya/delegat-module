using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Delegat.Models.Products;
using Delegat.Services;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
public class ProductFolderController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IStorageService _storageService;
    private readonly DataContext _context;
    private readonly ThumbnailService _thumbnailService;

    public ProductFolderController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IStorageService storageService,
        DataContext context,
        ThumbnailService thumbnailService)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _storageService = storageService;
        _context = context;
        _thumbnailService = thumbnailService;
    }

    private async Task<Product> GetAndValidateProduct(int productId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var product = await _unitOfWork.Repository<Product>().GetByIdAsync(productId);
        if (product == null)
            throw ApiException.NotFound("Product not found", code: "product_not_found");
        if (product.storeId != selectedStoreId.Value)
            throw ApiException.Forbidden("Product does not belong to your store", code: "access_denied_store");

        return product;
    }

    [HttpGet]
    [Route("/products/{productId}/folders")]
    public async Task<IActionResult> GetProductFolders(int productId)
    {
        await GetAndValidateProduct(productId);

        var folders = await _context.productFolders
            .AsNoTracking()
            .Where(f => f.productId == productId)
            .Include(f => f.files)
            .Include(f => f.childFolders)
            .ToListAsync();

        var result = folders.Select(f => new
        {
            f.Id,
            f.name,
            f.productId,
            f.parentFolderId,
            f.createdAt,
            fileCount = f.files.Count,
            subfolderCount = f.childFolders.Count
        });

        return Ok(result);
    }

    public record CreateFolderRequest(string name, int? parentFolderId);

    [HttpPost]
    [Route("/products/{productId}/folders")]
    public async Task<IActionResult> CreateFolder(int productId, [FromBody] CreateFolderRequest request)
    {
        await GetAndValidateProduct(productId);

        if (string.IsNullOrWhiteSpace(request.name))
            throw ApiException.BadRequest("Folder name is required", code: "required_fields_missing");

        if (request.parentFolderId.HasValue)
        {
            var parent = await _context.productFolders
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == request.parentFolderId.Value && f.productId == productId);
            if (parent == null)
                throw ApiException.NotFound("Parent folder not found");
        }

        var folder = new ProductFolder
        {
            name = request.name.Trim(),
            productId = productId,
            parentFolderId = request.parentFolderId,
            createdAt = DateTime.UtcNow
        };

        _context.productFolders.Add(folder);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            folder.Id,
            folder.name,
            folder.productId,
            folder.parentFolderId,
            folder.createdAt,
            fileCount = 0,
            subfolderCount = 0
        });
    }

    public record RenameFolderRequest(string name);

    [HttpPut]
    [Route("/products/{productId}/folders/{id}")]
    public async Task<IActionResult> RenameFolder(int productId, int id, [FromBody] RenameFolderRequest request)
    {
        await GetAndValidateProduct(productId);

        if (string.IsNullOrWhiteSpace(request.name))
            throw ApiException.BadRequest("Folder name is required", code: "required_fields_missing");

        var folder = await _context.productFolders
            .FirstOrDefaultAsync(f => f.Id == id && f.productId == productId);
        if (folder == null)
            throw ApiException.NotFound("Folder not found");

        folder.name = request.name.Trim();
        await _context.SaveChangesAsync();

        return Ok(new { folder.Id, folder.name });
    }

    [HttpDelete]
    [Route("/products/{productId}/folders/{id}")]
    public async Task<IActionResult> DeleteFolder(int productId, int id, [FromQuery] bool recursive = false)
    {
        await GetAndValidateProduct(productId);

        var folder = await _context.productFolders
            .AsNoTracking()
            .Include(f => f.files)
            .Include(f => f.childFolders)
            .FirstOrDefaultAsync(f => f.Id == id && f.productId == productId);

        if (folder == null)
            throw ApiException.NotFound("Folder not found");

        if (!recursive)
        {
            if (folder.files.Count > 0 || folder.childFolders.Count > 0)
                throw ApiException.BadRequest("Folder is not empty. Use recursive=true to delete it with its contents", code: "folder_not_empty");

            await _context.productFolders.Where(f => f.Id == id).ExecuteDeleteAsync();
            return Ok();
        }

        // Recursive delete: collect all descendant folder IDs
        var allFolderIds = new List<int> { id };
        await CollectDescendantFolderIds(id, productId, allFolderIds);

        // Load all files in these folders to delete from storage
        var filesToDelete = await _context.files
            .AsNoTracking()
            .Where(f => f.folderId.HasValue && allFolderIds.Contains(f.folderId.Value))
            .ToListAsync();

        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            // Delete file tags first (FK constraint)
            var fileIdsToDelete = filesToDelete.Select(f => f.Id).ToList();
            if (fileIdsToDelete.Any())
                await _context.tags.Where(t => fileIdsToDelete.Contains(t.fileId)).ExecuteDeleteAsync();

            // Delete files from storage
            foreach (var file in filesToDelete)
            {
                await _thumbnailService.DeleteThumbnailsAsync(file.filePath);
                await _storageService.DeleteAsync(file.filePath);
            }

            // Delete files from DB
            await _context.files
                .Where(f => f.folderId.HasValue && allFolderIds.Contains(f.folderId.Value))
                .ExecuteDeleteAsync();

            // Delete folders from DB (children first due to FK)
            // Reverse so deepest children are deleted first
            allFolderIds.Reverse();
            foreach (var folderId in allFolderIds)
            {
                await _context.productFolders.Where(f => f.Id == folderId).ExecuteDeleteAsync();
            }
        });

        return Ok();
    }

    private async Task CollectDescendantFolderIds(int parentId, int productId, List<int> result)
    {
        var childIds = await _context.productFolders
            .AsNoTracking()
            .Where(f => f.parentFolderId == parentId && f.productId == productId)
            .Select(f => f.Id)
            .ToListAsync();

        foreach (var childId in childIds)
        {
            result.Add(childId);
            await CollectDescendantFolderIds(childId, productId, result);
        }
    }

    public record MoveFolderRequest(int? newParentFolderId);

    [HttpPatch]
    [Route("/products/{productId}/folders/{id}/move")]
    public async Task<IActionResult> MoveFolder(int productId, int id, [FromBody] MoveFolderRequest request)
    {
        await GetAndValidateProduct(productId);

        var folder = await _context.productFolders
            .FirstOrDefaultAsync(f => f.Id == id && f.productId == productId);
        if (folder == null)
            throw ApiException.NotFound("Folder not found");

        if (request.newParentFolderId == id)
            throw ApiException.BadRequest("Cannot move a folder into itself");

        if (request.newParentFolderId.HasValue)
        {
            var target = await _context.productFolders
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == request.newParentFolderId.Value && f.productId == productId);
            if (target == null)
                throw ApiException.NotFound("Target folder not found");

            // Cycle detection: walk up from target, if we encounter `id` it's a cycle
            var currentId = request.newParentFolderId;
            while (currentId.HasValue)
            {
                if (currentId.Value == id)
                    throw ApiException.BadRequest("Cannot move a folder into its own descendant");

                var parent = await _context.productFolders
                    .AsNoTracking()
                    .Where(f => f.Id == currentId.Value)
                    .Select(f => f.parentFolderId)
                    .FirstOrDefaultAsync();

                currentId = parent;
            }
        }

        folder.parentFolderId = request.newParentFolderId;
        await _context.SaveChangesAsync();

        return Ok(new { folder.Id, folder.parentFolderId });
    }

    [HttpGet]
    [Route("/products/{productId}/folders/{id}/download")]
    public async Task DownloadFolder(int productId, string id)
    {
        await GetAndValidateProduct(productId);

        int? folderId = id == "root" ? null : int.Parse(id);
        await StreamFolderZip(productId, folderId);
    }

    public record ShareFolderRequest(int ttlHours);

    [HttpPost]
    [Route("/products/{productId}/folders/{id}/share")]
    public async Task<IActionResult> ShareFolder(int productId, string id, [FromBody] ShareFolderRequest request)
    {
        var allowedTtl = new[] { 1, 24, 168, 720 };
        if (!allowedTtl.Contains(request.ttlHours))
            throw ApiException.BadRequest("Invalid TTL");

        await GetAndValidateProduct(productId);

        int? folderId = id == "root" ? null : int.Parse(id);

        if (folderId.HasValue)
        {
            var folder = await _context.productFolders.AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == folderId.Value && f.productId == productId);
            if (folder == null) throw ApiException.NotFound("Folder not found");
        }

        var user = await _currentUserService.GetUserAsync();
        if (user == null) throw ApiException.Unauthorized();

        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Base64UrlEncoder.Encode(tokenBytes);

        var shareToken = new FolderShareToken
        {
            productId = productId,
            folderId = folderId,
            token = token,
            expiresAt = DateTime.UtcNow.AddHours(request.ttlHours),
            createdAt = DateTime.UtcNow,
            createdByUserId = user.Id
        };

        _context.folderShareTokens.Add(shareToken);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            url = $"/api/shared/folder/{token}",
            expiresAt = shareToken.expiresAt
        });
    }

    /// <summary>
    /// Streams a zip archive of files in the given folder scope to Response.Body.
    /// </summary>
    internal async Task StreamFolderZip(int productId, int? folderId)
    {
        string folderName;
        var allFolderIds = new List<int>();

        if (folderId.HasValue)
        {
            var folder = await _context.productFolders.AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == folderId.Value && f.productId == productId);
            if (folder == null) throw ApiException.NotFound("Folder not found");
            folderName = folder.name;
            allFolderIds.Add(folderId.Value);
            await CollectDescendantFolderIds(folderId.Value, productId, allFolderIds);
        }
        else
        {
            folderName = "files";
        }

        // Get files in target scope
        List<Models.File> files;
        if (folderId.HasValue)
        {
            files = await _context.files.AsNoTracking()
                .Where(f => f.productId == productId && f.folderId.HasValue && allFolderIds.Contains(f.folderId.Value))
                .ToListAsync();
        }
        else
        {
            files = await _context.files.AsNoTracking()
                .Where(f => f.productId == productId)
                .ToListAsync();
        }

        // Build folder ID → relative path map
        var allFolders = await _context.productFolders.AsNoTracking()
            .Where(f => f.productId == productId)
            .Select(f => new { f.Id, f.name, f.parentFolderId })
            .ToListAsync();

        var folderPathCache = new Dictionary<int, string>();

        string BuildFolderPath(int fId)
        {
            if (folderPathCache.TryGetValue(fId, out var cached)) return cached;

            var parts = new List<string>();
            int? cur = fId;
            while (cur.HasValue)
            {
                var f = allFolders.FirstOrDefault(x => x.Id == cur.Value);
                if (f == null) break;
                parts.Insert(0, f.name);
                cur = f.parentFolderId;
            }

            if (folderId.HasValue)
            {
                var targetParts = new List<string>();
                int? tc = folderId.Value;
                while (tc.HasValue)
                {
                    var tf = allFolders.FirstOrDefault(x => x.Id == tc.Value);
                    if (tf == null) break;
                    targetParts.Insert(0, tf.name);
                    tc = tf.parentFolderId;
                }

                if (parts.Count > targetParts.Count)
                    parts = parts.Skip(targetParts.Count).ToList();
                else
                    parts.Clear();
            }

            var path = string.Join("/", parts);
            folderPathCache[fId] = path;
            return path;
        }

        var encodedName = Uri.EscapeDataString(folderName);
        Response.ContentType = "application/zip";
        Response.Headers["Content-Disposition"] = $"attachment; filename=\"{encodedName}.zip\"; filename*=UTF-8''{encodedName}.zip";

        var bodyStream = Response.BodyWriter.AsStream();
        using (var archive = new ZipArchive(bodyStream, ZipArchiveMode.Create, leaveOpen: false))
        {
            // Add empty folder entries to preserve folder structure
            foreach (var fId in allFolderIds)
            {
                var hasFiles = files.Any(f => f.folderId == fId);
                if (!hasFiles)
                {
                    var folderPath = BuildFolderPath(fId);
                    if (!string.IsNullOrEmpty(folderPath))
                        archive.CreateEntry(folderPath + "/");
                }
            }

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file.filePath);
                string entryName;
                if (file.folderId.HasValue)
                {
                    var folderPath = BuildFolderPath(file.folderId.Value);
                    entryName = string.IsNullOrEmpty(folderPath) ? fileName : $"{folderPath}/{fileName}";
                }
                else
                {
                    entryName = fileName;
                }

                try
                {
                    var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                    using var entryStream = entry.Open();
                    await _storageService.StreamToAsync(file.filePath, entryStream);
                }
                catch
                {
                    // File was deleted/moved from storage during archive formation - skip it
                }
            }
        }
    }
}

/// <summary>
/// Public controller for accessing shared folder links (no auth required).
/// </summary>
[ApiController]
public class SharedFolderController : ControllerBase
{
    private readonly DataContext _db;
    private readonly IStorageService _storageService;

    public SharedFolderController(DataContext db, IStorageService storageService)
    {
        _db = db;
        _storageService = storageService;
    }

    [HttpGet]
    [Route("/shared/folder/{token}")]
    public async Task GetSharedFolder(string token)
    {
        var shareToken = await _db.folderShareTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.token == token);

        if (shareToken == null || shareToken.IsExpired)
            throw ApiException.NotFound("Link not found or expired");

        // Reuse the zip streaming logic from ProductFolderController
        var folderController = HttpContext.RequestServices.GetRequiredService<ProductFolderController>();
        folderController.ControllerContext = ControllerContext;
        await folderController.StreamFolderZip(shareToken.productId, shareToken.folderId);
    }
}
