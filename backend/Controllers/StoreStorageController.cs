using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Security;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Delegat.Services;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
public class StoreStorageController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IStorageService _storageService;
    private readonly DataContext _context;
    private readonly ThumbnailService _thumbnailService;
    private readonly QuotaService _quotaService;
    private readonly IServiceProvider _serviceProvider;

    public StoreStorageController(
        ICurrentUserService currentUserService,
        IStorageService storageService,
        DataContext context,
        ThumbnailService thumbnailService,
        QuotaService quotaService,
        IServiceProvider serviceProvider)
    {
        _currentUserService = currentUserService;
        _storageService = storageService;
        _context = context;
        _thumbnailService = thumbnailService;
        _quotaService = quotaService;
        _serviceProvider = serviceProvider;
    }

    private async Task<int> GetSelectedStoreId(CancellationToken ct = default)
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        if (storeId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        return storeId.Value;
    }

    [HttpGet]
    [Route("/store-storage/files")]
    public async Task<IActionResult> GetFiles(CancellationToken ct)
    {
        var storeId = await GetSelectedStoreId(ct);

        var files = await _context.storeFiles
            .AsNoTracking()
            .Include(f => f.tags)
            .ThenInclude(t => t.tagDefinition)
            .Where(f => f.storeId == storeId)
            .OrderByDescending(f => f.uploadDateTime)
            .ToListAsync(ct);

        return Ok(files.Select(f => f.MapToDTO(_serviceProvider)));
    }

    [HttpPut]
    [Route("/store-storage/files")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 10L * 1024 * 1024 * 1024)]
    public async Task<IActionResult> UploadFiles(IFormFile[] files, [FromQuery] int? folderId = null, CancellationToken ct = default)
    {
        var storeId = await GetSelectedStoreId(ct);
        var user = await _currentUserService.GetUserAsync();
        if (user == null)
            throw ApiException.Unauthorized();

        await _quotaService.EnsureFileQuotaAsync(storeId, files.Length);
        await _quotaService.EnsureStorageQuotaAsync(storeId, files.Sum(f => f.Length));

        if (folderId.HasValue)
        {
            var folderExists = await _context.storeFolders
                .AnyAsync(f => f.Id == folderId.Value && f.storeId == storeId, ct);
            if (!folderExists)
                throw ApiException.NotFound("Folder not found");
        }

        var fileEntities = new List<StoreFile>();

        foreach (var file in files)
        {
            var uniqueKey = Guid.NewGuid().ToString("N");
            var safeName = FileNameSafety.Sanitize(file.FileName);
            var filePath = $"{storeId}/store/{uniqueKey}/{safeName}";

            using var stream = file.OpenReadStream();
            var uploaded = await _storageService.UploadAsync(stream, filePath, file.Length, file.ContentType);
            if (!uploaded)
                throw ApiException.InternalServerError($"Failed to upload file {file.FileName} to storage");

            var entity = new StoreFile
            {
                folderId = folderId
            };
            entity.Fill(filePath, user.Id, storeId, file.ContentType, file.Length);

            _context.storeFiles.Add(entity);
            fileEntities.Add(entity);
        }

        await _context.SaveChangesAsync(ct);
        return Ok(fileEntities.Select(f => f.MapToDTO(_serviceProvider)));
    }

    [HttpGet]
    [Route("/store-storage/files/{id}/download")]
    public async Task DownloadFile(int id, CancellationToken ct)
    {
        var storeId = await GetSelectedStoreId(ct);
        var file = await _context.storeFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id && f.storeId == storeId, ct);

        if (file == null)
            throw ApiException.NotFound("File not found", code: "file_not_found");

        if (!await _storageService.ExistsAsync(file.filePath))
            throw ApiException.NotFound("File not found in storage", code: "file_not_found");

        var fileName = file.displayName ?? Path.GetFileName(file.filePath);
        var isImage = file.contentType.StartsWith("image/");
        var disposition = isImage ? "inline" : "attachment";
        var encodedFileName = Uri.EscapeDataString(fileName);

        Response.ContentType = file.contentType;
        Response.ContentLength = file.length;
        Response.Headers["Content-Disposition"] = $"{disposition}; filename=\"{encodedFileName}\"; filename*=UTF-8''{encodedFileName}";

        await _storageService.StreamToAsync(file.filePath, Response.Body);
    }

    [HttpGet]
    [Route("/store-storage/files/{id}/thumbnail")]
    [DisableRateLimiting]
    [ResponseCache(Duration = 31536000, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetThumbnail(int id, [FromQuery] int w = 256, CancellationToken ct = default)
    {
        if (!ThumbnailService.IsAllowedWidth(w))
            throw ApiException.BadRequest("Invalid size. Allowed values: 64, 128, 256, 512");

        var storeId = await GetSelectedStoreId(ct);
        var file = await _context.storeFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id && f.storeId == storeId, ct);

        if (file == null)
            throw ApiException.NotFound("File not found", code: "file_not_found");

        if (!file.contentType.StartsWith("image/"))
            throw ApiException.BadRequest("File is not an image");

        var stream = await _thumbnailService.GetOrGenerateAsync(file.filePath, w, ct);
        if (stream == null)
            throw ApiException.InternalServerError("Failed to create thumbnail");

        Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
        return File(stream, "image/webp");
    }

    public record RenameFileRequest(string name);

    [HttpPatch]
    [Route("/store-storage/files/{id}/rename")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> RenameFile(int id, [FromBody] RenameFileRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.name))
            throw ApiException.BadRequest("File name is required", code: "required_fields_missing");

        var storeId = await GetSelectedStoreId(ct);
        var file = await _context.storeFiles.FirstOrDefaultAsync(f => f.Id == id && f.storeId == storeId, ct);
        if (file == null)
            throw ApiException.NotFound("File not found", code: "file_not_found");

        file.displayName = request.name.Trim();
        await _context.SaveChangesAsync(ct);

        return Ok(file.MapToDTO(_serviceProvider));
    }

    public record SetPublicRequest(bool isPublic);

    [HttpPatch]
    [Route("/store-storage/files/{id}/public")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> SetFilePublic(int id, [FromBody] SetPublicRequest request, CancellationToken ct)
    {
        var storeId = await GetSelectedStoreId(ct);
        var file = await _context.storeFiles.FirstOrDefaultAsync(f => f.Id == id && f.storeId == storeId, ct);
        if (file == null)
            throw ApiException.NotFound("File not found", code: "file_not_found");

        file.isPublic = request.isPublic;
        if (request.isPublic)
            file.publicToken ??= Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        else
            file.publicToken = null;
        await _context.SaveChangesAsync(ct);

        return Ok(file.MapToDTO(_serviceProvider));
    }

    public record MoveFileRequest(int? folderId);

    [HttpPatch]
    [Route("/store-storage/files/{id}/move")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> MoveFile(int id, [FromBody] MoveFileRequest request, CancellationToken ct)
    {
        var storeId = await GetSelectedStoreId(ct);
        var file = await _context.storeFiles.FirstOrDefaultAsync(f => f.Id == id && f.storeId == storeId, ct);
        if (file == null)
            throw ApiException.NotFound("File not found", code: "file_not_found");

        if (request.folderId.HasValue)
        {
            var folderExists = await _context.storeFolders
                .AnyAsync(f => f.Id == request.folderId.Value && f.storeId == storeId, ct);
            if (!folderExists)
                throw ApiException.NotFound("Folder not found");
        }

        file.folderId = request.folderId;
        await _context.SaveChangesAsync(ct);

        return Ok(file.MapToDTO(_serviceProvider));
    }

    [HttpDelete]
    [Route("/store-storage/files/{id}")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> DeleteFile(int id, CancellationToken ct)
    {
        var storeId = await GetSelectedStoreId(ct);
        var file = await _context.storeFiles.FirstOrDefaultAsync(f => f.Id == id && f.storeId == storeId, ct);
        if (file == null)
            throw ApiException.NotFound("File not found", code: "file_not_found");

        await _context.storeFileTags.Where(t => t.fileId == id).ExecuteDeleteAsync(ct);
        await _context.storeFileShareTokens.Where(t => t.fileId == id).ExecuteDeleteAsync(ct);
        await _thumbnailService.DeleteThumbnailsAsync(file.filePath);
        await _storageService.DeleteAsync(file.filePath);

        _context.storeFiles.Remove(file);
        await _context.SaveChangesAsync(ct);

        return Ok();
    }

    public record BulkDeleteFilesRequest(int[] fileIds);

    [HttpPost]
    [Route("/store-storage/files/bulk-delete")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> BulkDeleteFiles([FromBody] BulkDeleteFilesRequest request, CancellationToken ct)
    {
        var storeId = await GetSelectedStoreId(ct);
        if (request.fileIds == null || request.fileIds.Length == 0)
            throw ApiException.BadRequest("No files specified", code: "required_fields_missing");

        var files = await _context.storeFiles
            .Where(f => request.fileIds.Contains(f.Id) && f.storeId == storeId)
            .ToListAsync(ct);

        var fileIds = files.Select(f => f.Id).ToList();
        if (fileIds.Count > 0)
        {
            await _context.storeFileTags.Where(t => fileIds.Contains(t.fileId)).ExecuteDeleteAsync(ct);
            await _context.storeFileShareTokens.Where(t => fileIds.Contains(t.fileId)).ExecuteDeleteAsync(ct);
        }

        foreach (var file in files)
        {
            await _thumbnailService.DeleteThumbnailsAsync(file.filePath);
            await _storageService.DeleteAsync(file.filePath);
            _context.storeFiles.Remove(file);
        }

        await _context.SaveChangesAsync(ct);
        return Ok(new { deletedCount = files.Count });
    }

    [HttpGet]
    [Route("/store-storage/folders")]
    public async Task<IActionResult> GetFolders(CancellationToken ct)
    {
        var storeId = await GetSelectedStoreId(ct);
        var folders = await _context.storeFolders
            .AsNoTracking()
            .Where(f => f.storeId == storeId)
            .Include(f => f.files)
            .Include(f => f.childFolders)
            .ToListAsync(ct);

        return Ok(folders.Select(f => new
        {
            f.Id,
            f.name,
            f.storeId,
            f.parentFolderId,
            f.createdAt,
            fileCount = f.files.Count,
            subfolderCount = f.childFolders.Count
        }));
    }

    public record CreateFolderRequest(string name, int? parentFolderId);

    [HttpPost]
    [Route("/store-storage/folders")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> CreateFolder([FromBody] CreateFolderRequest request, CancellationToken ct)
    {
        var storeId = await GetSelectedStoreId(ct);
        if (string.IsNullOrWhiteSpace(request.name))
            throw ApiException.BadRequest("Folder name is required", code: "required_fields_missing");

        if (request.parentFolderId.HasValue)
        {
            var parentExists = await _context.storeFolders
                .AnyAsync(f => f.Id == request.parentFolderId.Value && f.storeId == storeId, ct);
            if (!parentExists)
                throw ApiException.NotFound("Parent folder not found");
        }

        var folder = new StoreFolder
        {
            name = request.name.Trim(),
            storeId = storeId,
            parentFolderId = request.parentFolderId,
            createdAt = DateTime.UtcNow
        };

        _context.storeFolders.Add(folder);
        await _context.SaveChangesAsync(ct);

        return Ok(new
        {
            folder.Id,
            folder.name,
            folder.storeId,
            folder.parentFolderId,
            folder.createdAt,
            fileCount = 0,
            subfolderCount = 0
        });
    }

    public record RenameFolderRequest(string name);

    [HttpPut]
    [Route("/store-storage/folders/{id}")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> RenameFolder(int id, [FromBody] RenameFolderRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.name))
            throw ApiException.BadRequest("Folder name is required", code: "required_fields_missing");

        var storeId = await GetSelectedStoreId(ct);
        var folder = await _context.storeFolders.FirstOrDefaultAsync(f => f.Id == id && f.storeId == storeId, ct);
        if (folder == null)
            throw ApiException.NotFound("Folder not found");

        folder.name = request.name.Trim();
        await _context.SaveChangesAsync(ct);

        return Ok(new { folder.Id, folder.name });
    }

    [HttpDelete]
    [Route("/store-storage/folders/{id}")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> DeleteFolder(int id, [FromQuery] bool recursive = false, CancellationToken ct = default)
    {
        var storeId = await GetSelectedStoreId(ct);
        var folder = await _context.storeFolders
            .AsNoTracking()
            .Include(f => f.files)
            .Include(f => f.childFolders)
            .FirstOrDefaultAsync(f => f.Id == id && f.storeId == storeId, ct);

        if (folder == null)
            throw ApiException.NotFound("Folder not found");

        if (!recursive)
        {
            if (folder.files.Count > 0 || folder.childFolders.Count > 0)
                throw ApiException.BadRequest("Folder is not empty. Use recursive=true to delete with its contents", code: "folder_not_empty");

            await _context.storeFolderShareTokens.Where(t => t.folderId == id).ExecuteDeleteAsync(ct);
            await _context.storeFolders.Where(f => f.Id == id).ExecuteDeleteAsync(ct);
            return Ok();
        }

        var allFolderIds = new List<int> { id };
        await CollectDescendantFolderIds(id, storeId, allFolderIds, ct);

        var filesToDelete = await _context.storeFiles
            .AsNoTracking()
            .Where(f => f.folderId.HasValue && allFolderIds.Contains(f.folderId.Value))
            .ToListAsync(ct);

        var fileIds = filesToDelete.Select(f => f.Id).ToList();
        if (fileIds.Count > 0)
        {
            await _context.storeFileTags.Where(t => fileIds.Contains(t.fileId)).ExecuteDeleteAsync(ct);
            await _context.storeFileShareTokens.Where(t => fileIds.Contains(t.fileId)).ExecuteDeleteAsync(ct);
        }
        await _context.storeFolderShareTokens
            .Where(t => t.folderId.HasValue && allFolderIds.Contains(t.folderId.Value))
            .ExecuteDeleteAsync(ct);

        foreach (var file in filesToDelete)
        {
            await _thumbnailService.DeleteThumbnailsAsync(file.filePath);
            await _storageService.DeleteAsync(file.filePath);
        }

        await _context.storeFiles
            .Where(f => f.folderId.HasValue && allFolderIds.Contains(f.folderId.Value))
            .ExecuteDeleteAsync(ct);

        allFolderIds.Reverse();
        foreach (var folderId in allFolderIds)
            await _context.storeFolders.Where(f => f.Id == folderId).ExecuteDeleteAsync(ct);

        return Ok();
    }

    public record MoveFolderRequest(int? newParentFolderId);

    [HttpPatch]
    [Route("/store-storage/folders/{id}/move")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> MoveFolder(int id, [FromBody] MoveFolderRequest request, CancellationToken ct)
    {
        var storeId = await GetSelectedStoreId(ct);
        var folder = await _context.storeFolders.FirstOrDefaultAsync(f => f.Id == id && f.storeId == storeId, ct);
        if (folder == null)
            throw ApiException.NotFound("Folder not found");

        if (request.newParentFolderId == id)
            throw ApiException.BadRequest("Cannot move a folder into itself");

        if (request.newParentFolderId.HasValue)
        {
            var target = await _context.storeFolders
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == request.newParentFolderId.Value && f.storeId == storeId, ct);
            if (target == null)
                throw ApiException.NotFound("Target folder not found");

            var currentId = request.newParentFolderId;
            while (currentId.HasValue)
            {
                if (currentId.Value == id)
                    throw ApiException.BadRequest("Cannot move a folder into its own descendant");

                currentId = await _context.storeFolders
                    .AsNoTracking()
                    .Where(f => f.Id == currentId.Value)
                    .Select(f => f.parentFolderId)
                    .FirstOrDefaultAsync(ct);
            }
        }

        folder.parentFolderId = request.newParentFolderId;
        await _context.SaveChangesAsync(ct);

        return Ok(new { folder.Id, folder.parentFolderId });
    }

    public record BulkMoveRequest(int? targetFolderId, int[] fileIds, int[] folderIds);

    [HttpPost]
    [Route("/store-storage/bulk-move")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> BulkMove([FromBody] BulkMoveRequest request, CancellationToken ct)
    {
        var storeId = await GetSelectedStoreId(ct);

        if (request.targetFolderId.HasValue)
        {
            var targetExists = await _context.storeFolders
                .AnyAsync(f => f.Id == request.targetFolderId.Value && f.storeId == storeId, ct);
            if (!targetExists)
                throw ApiException.NotFound("Target folder not found");
        }

        if (request.fileIds is { Length: > 0 })
        {
            var files = await _context.storeFiles
                .Where(f => request.fileIds.Contains(f.Id) && f.storeId == storeId)
                .ToListAsync(ct);

            if (files.Count != request.fileIds.Length)
                throw ApiException.NotFound("One or more files not found", code: "file_not_found");

            foreach (var file in files)
                file.folderId = request.targetFolderId;
        }

        if (request.folderIds is { Length: > 0 })
        {
            var folders = await _context.storeFolders
                .Where(f => request.folderIds.Contains(f.Id) && f.storeId == storeId)
                .ToListAsync(ct);

            if (folders.Count != request.folderIds.Length)
                throw ApiException.NotFound("One or more folders not found");

            var movingIds = new HashSet<int>(request.folderIds);

            foreach (var folder in folders)
            {
                if (request.targetFolderId == folder.Id)
                    throw ApiException.BadRequest("Cannot move a folder into itself");

                var currentId = request.targetFolderId;
                while (currentId.HasValue)
                {
                    if (movingIds.Contains(currentId.Value))
                        throw ApiException.BadRequest("Cannot move a folder into its own descendant");

                    currentId = await _context.storeFolders
                        .AsNoTracking()
                        .Where(f => f.Id == currentId.Value)
                        .Select(f => f.parentFolderId)
                        .FirstOrDefaultAsync(ct);
                }

                folder.parentFolderId = request.targetFolderId;
            }
        }

        await _context.SaveChangesAsync(ct);
        return Ok();
    }

    [HttpGet]
    [Route("/store-storage/folders/{id}/download")]
    public async Task DownloadFolder(string id, CancellationToken ct)
    {
        var storeId = await GetSelectedStoreId(ct);
        var folderId = id == "root" ? (int?)null : int.Parse(id);
        await StreamFolderZip(storeId, folderId, ct);
    }

    public record ShareRequest(int ttlHours);

    [HttpPost]
    [Route("/store-storage/files/{id}/share")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> ShareFile(int id, [FromBody] ShareRequest request, CancellationToken ct)
    {
        ValidateTtl(request.ttlHours);
        var storeId = await GetSelectedStoreId(ct);
        var user = await _currentUserService.GetUserAsync();
        if (user == null)
            throw ApiException.Unauthorized();

        var fileExists = await _context.storeFiles.AnyAsync(f => f.Id == id && f.storeId == storeId, ct);
        if (!fileExists)
            throw ApiException.NotFound("File not found", code: "file_not_found");

        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var shareToken = new StoreFileShareToken
        {
            fileId = id,
            token = token,
            expiresAt = DateTime.UtcNow.AddHours(request.ttlHours),
            createdAt = DateTime.UtcNow,
            createdByUserId = user.Id
        };

        _context.storeFileShareTokens.Add(shareToken);
        await _context.SaveChangesAsync(ct);

        return Ok(new { url = $"/api/shared/store-file/{token}", expiresAt = shareToken.expiresAt });
    }

    [HttpPost]
    [Route("/store-storage/folders/{id}/share")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> ShareFolder(string id, [FromBody] ShareRequest request, CancellationToken ct)
    {
        ValidateTtl(request.ttlHours);
        var storeId = await GetSelectedStoreId(ct);
        var user = await _currentUserService.GetUserAsync();
        if (user == null)
            throw ApiException.Unauthorized();

        var folderId = id == "root" ? (int?)null : int.Parse(id);
        if (!folderId.HasValue)
            throw ApiException.BadRequest("Cannot create a public link for the storage root folder");

        var folderExists = await _context.storeFolders
            .AnyAsync(f => f.Id == folderId.Value && f.storeId == storeId, ct);
        if (!folderExists)
            throw ApiException.NotFound("Folder not found");

        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var shareToken = new StoreFolderShareToken
        {
            storeId = storeId,
            folderId = folderId,
            token = token,
            expiresAt = DateTime.UtcNow.AddHours(request.ttlHours),
            createdAt = DateTime.UtcNow,
            createdByUserId = user.Id
        };

        _context.storeFolderShareTokens.Add(shareToken);
        await _context.SaveChangesAsync(ct);

        return Ok(new { url = $"/api/shared/store-folder/{token}", token, expiresAt = shareToken.expiresAt });
    }

    [HttpDelete]
    [Route("/store-storage/shares/file/{token}")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> RevokeFileShare(string token, CancellationToken ct)
    {
        var storeId = await GetSelectedStoreId(ct);
        var share = await _context.storeFileShareTokens
            .Include(t => t.file)
            .FirstOrDefaultAsync(t => t.token == token, ct);
        if (share == null || share.file.storeId != storeId)
            throw ApiException.NotFound("Link not found");

        _context.storeFileShareTokens.Remove(share);
        await _context.SaveChangesAsync(ct);
        return Ok(new { success = true });
    }

    [HttpDelete]
    [Route("/store-storage/shares/folder/{token}")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> RevokeFolderShare(string token, CancellationToken ct)
    {
        var storeId = await GetSelectedStoreId(ct);
        var share = await _context.storeFolderShareTokens
            .FirstOrDefaultAsync(t => t.token == token && t.storeId == storeId, ct);
        if (share == null)
            throw ApiException.NotFound("Link not found");

        _context.storeFolderShareTokens.Remove(share);
        await _context.SaveChangesAsync(ct);
        return Ok(new { success = true });
    }

    [HttpPost]
    [Route("/store-storage/files/{fileId}/tags/{tagId}")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> AssignTag(int fileId, int tagId, CancellationToken ct)
    {
        var storeId = await GetSelectedStoreId(ct);
        var fileExists = await _context.storeFiles.AnyAsync(f => f.Id == fileId && f.storeId == storeId, ct);
        if (!fileExists)
            throw ApiException.NotFound("File not found", code: "file_not_found");

        var tagExists = await _context.tagDefinitions.AnyAsync(t => t.Id == tagId && t.storeId == storeId, ct);
        if (!tagExists)
            throw ApiException.NotFound("Tag not found");

        var alreadyAssigned = await _context.storeFileTags
            .AnyAsync(t => t.fileId == fileId && t.tagDefinitionId == tagId, ct);
        if (alreadyAssigned)
            return Ok();

        _context.storeFileTags.Add(new StoreFileTag { fileId = fileId, tagDefinitionId = tagId });
        await _context.SaveChangesAsync(ct);

        return Ok();
    }

    [HttpDelete]
    [Route("/store-storage/files/{fileId}/tags/{tagId}")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> RemoveTag(int fileId, int tagId, CancellationToken ct)
    {
        var storeId = await GetSelectedStoreId(ct);
        var fileExists = await _context.storeFiles.AnyAsync(f => f.Id == fileId && f.storeId == storeId, ct);
        if (!fileExists)
            throw ApiException.NotFound("File not found", code: "file_not_found");

        await _context.storeFileTags
            .Where(t => t.fileId == fileId && t.tagDefinitionId == tagId)
            .ExecuteDeleteAsync(ct);

        return Ok();
    }

    public record BulkTagInput(int[] fileIds, int tagId);

    [HttpPost]
    [Route("/store-storage/files/bulk-tag")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> BulkAssignTag([FromBody] BulkTagInput input, CancellationToken ct)
    {
        var storeId = await GetSelectedStoreId(ct);
        if (input.fileIds == null || input.fileIds.Length == 0)
            throw ApiException.BadRequest("No files specified", code: "required_fields_missing");

        var tagExists = await _context.tagDefinitions.AnyAsync(t => t.Id == input.tagId && t.storeId == storeId, ct);
        if (!tagExists)
            throw ApiException.NotFound("Tag not found");

        var validFileIds = await _context.storeFiles
            .Where(f => input.fileIds.Contains(f.Id) && f.storeId == storeId)
            .Select(f => f.Id)
            .ToListAsync(ct);

        if (validFileIds.Count != input.fileIds.Length)
            throw ApiException.NotFound("One or more files not found", code: "file_not_found");

        var alreadyAssigned = await _context.storeFileTags
            .Where(t => t.tagDefinitionId == input.tagId && validFileIds.Contains(t.fileId))
            .Select(t => t.fileId)
            .ToListAsync(ct);

        var alreadySet = new HashSet<int>(alreadyAssigned);
        foreach (var fileId in validFileIds)
        {
            if (!alreadySet.Contains(fileId))
                _context.storeFileTags.Add(new StoreFileTag { fileId = fileId, tagDefinitionId = input.tagId });
        }

        await _context.SaveChangesAsync(ct);
        return Ok(new { added = validFileIds.Count - alreadySet.Count });
    }

    internal async Task StreamFolderZip(int storeId, int? folderId, CancellationToken ct = default)
    {
        string folderName;
        var allFolderIds = new List<int>();

        if (folderId.HasValue)
        {
            var folder = await _context.storeFolders
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == folderId.Value && f.storeId == storeId, ct);
            if (folder == null)
                throw ApiException.NotFound("Folder not found");

            folderName = folder.name;
            allFolderIds.Add(folderId.Value);
            await CollectDescendantFolderIds(folderId.Value, storeId, allFolderIds, ct);
        }
        else
        {
            folderName = "store-storage";
        }

        var files = folderId.HasValue
            ? await _context.storeFiles.AsNoTracking()
                .Where(f => f.storeId == storeId && f.folderId.HasValue && allFolderIds.Contains(f.folderId.Value))
                .ToListAsync(ct)
            : await _context.storeFiles.AsNoTracking()
                .Where(f => f.storeId == storeId)
                .ToListAsync(ct);

        var allFolders = await _context.storeFolders.AsNoTracking()
            .Where(f => f.storeId == storeId)
            .Select(f => new { f.Id, f.name, f.parentFolderId })
            .ToListAsync(ct);

        var folderPathCache = new Dictionary<int, string>();
        string BuildFolderPath(int fId)
        {
            if (folderPathCache.TryGetValue(fId, out var cached)) return cached;

            var parts = new List<string>();
            int? cur = fId;
            while (cur.HasValue)
            {
                var folder = allFolders.FirstOrDefault(x => x.Id == cur.Value);
                if (folder == null) break;
                parts.Insert(0, folder.name);
                cur = folder.parentFolderId;
            }

            if (folderId.HasValue)
            {
                var targetParts = new List<string>();
                int? target = folderId.Value;
                while (target.HasValue)
                {
                    var folder = allFolders.FirstOrDefault(x => x.Id == target.Value);
                    if (folder == null) break;
                    targetParts.Insert(0, folder.name);
                    target = folder.parentFolderId;
                }

                parts = parts.Count > targetParts.Count
                    ? parts.Skip(targetParts.Count).ToList()
                    : new List<string>();
            }

            var path = string.Join("/", parts);
            folderPathCache[fId] = path;
            return path;
        }

        var encodedName = Uri.EscapeDataString(folderName);
        Response.ContentType = "application/zip";
        Response.Headers["Content-Disposition"] = $"attachment; filename=\"{encodedName}.zip\"; filename*=UTF-8''{encodedName}.zip";

        var bodyStream = Response.BodyWriter.AsStream();
        using var archive = new ZipArchive(bodyStream, ZipArchiveMode.Create, leaveOpen: false);

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
            var fileName = file.displayName ?? Path.GetFileName(file.filePath);
            var entryName = file.folderId.HasValue
                ? string.IsNullOrEmpty(BuildFolderPath(file.folderId.Value))
                    ? fileName
                    : $"{BuildFolderPath(file.folderId.Value)}/{fileName}"
                : fileName;

            try
            {
                var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                await using var entryStream = entry.Open();
                await _storageService.StreamToAsync(file.filePath, entryStream);
            }
            catch
            {
            }
        }
    }

    private async Task CollectDescendantFolderIds(int parentId, int storeId, List<int> result, CancellationToken ct)
    {
        var childIds = await _context.storeFolders
            .AsNoTracking()
            .Where(f => f.parentFolderId == parentId && f.storeId == storeId)
            .Select(f => f.Id)
            .ToListAsync(ct);

        foreach (var childId in childIds)
        {
            result.Add(childId);
            await CollectDescendantFolderIds(childId, storeId, result, ct);
        }
    }

    private static void ValidateTtl(int ttlHours)
    {
        var allowedTtl = new[] { 1, 24, 168, 720 };
        if (!allowedTtl.Contains(ttlHours))
            throw ApiException.BadRequest("Invalid TTL");
    }
}

[ApiController]
public class SharedStoreStorageController : ControllerBase
{
    private readonly DataContext _context;
    private readonly IStorageService _storageService;

    public SharedStoreStorageController(DataContext context, IStorageService storageService)
    {
        _context = context;
        _storageService = storageService;
    }

    [HttpGet]
    [Route("/shared/store-file/{token}")]
    public async Task GetSharedFile(string token, CancellationToken ct)
    {
        var shareToken = await _context.storeFileShareTokens
            .Include(t => t.file)
            .FirstOrDefaultAsync(t => t.token == token, ct);

        if (shareToken == null || shareToken.IsExpired)
            throw ApiException.NotFound("Link not found or expired");

        if (!await _storageService.ExistsAsync(shareToken.file.filePath))
            throw ApiException.NotFound("File not found in storage", code: "file_not_found");

        var fileName = shareToken.file.displayName ?? Path.GetFileName(shareToken.file.filePath);
        var isImage = shareToken.file.contentType.StartsWith("image/");
        var disposition = isImage ? "inline" : "attachment";
        var encodedFileName = Uri.EscapeDataString(fileName);

        Response.ContentType = shareToken.file.contentType;
        Response.ContentLength = shareToken.file.length;
        Response.Headers["Content-Disposition"] = $"{disposition}; filename=\"{encodedFileName}\"; filename*=UTF-8''{encodedFileName}";

        await _storageService.StreamToAsync(shareToken.file.filePath, Response.Body);
    }

    [HttpGet]
    [Route("/shared/store-folder/{token}")]
    public async Task GetSharedFolder(string token, CancellationToken ct)
    {
        var shareToken = await _context.storeFolderShareTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.token == token, ct);

        if (shareToken == null || shareToken.IsExpired)
            throw ApiException.NotFound("Link not found or expired");

        if (!shareToken.folderId.HasValue)
            throw ApiException.NotFound("Links to the root folder are no longer supported");

        var folderController = HttpContext.RequestServices.GetRequiredService<StoreStorageController>();
        folderController.ControllerContext = ControllerContext;
        await folderController.StreamFolderZip(shareToken.storeId, shareToken.folderId, ct);
    }
}
