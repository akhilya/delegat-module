using Microsoft.EntityFrameworkCore;
using Delegat.Models;
using Delegat.Infrastructure.Storage;
using Delegat.Services.Ai.Tools;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;

namespace Delegat.Services.Ai.Tools.Handlers;

public class FileToolHandlers : ToolHandlerBase
{
    private readonly DataContext _context;
    private readonly AiOperationService _operationService;
    private readonly IStorageService _storageService;
    private readonly ThumbnailService _thumbnailService;
    private readonly ILogger<FileToolHandlers> _logger;

    public FileToolHandlers(
        DataContext context,
        AiOperationService operationService,
        IStorageService storageService,
        ThumbnailService thumbnailService,
        ILogger<FileToolHandlers> logger)
    {
        _context = context;
        _operationService = operationService;
        _storageService = storageService;
        _thumbnailService = thumbnailService;
        _logger = logger;
    }

    internal async Task<(object, string)> ListFilesAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ListFilesInput>(json);
        if (p.Scope == "product")
        {
            if (!p.ProductId.HasValue) return (new { error = "scope=product requires productId" }, "No productId");
            if (!await ProductInStoreAsync(p.ProductId.Value, storeId, ct)) return (new { error = "Product not found" }, "Not found");

            var fq = _context.files.AsNoTracking().Where(f => f.productId == p.ProductId);
            if (!string.IsNullOrEmpty(p.Search))
                fq = fq.Where(f => (f.displayName ?? f.filePath).Contains(p.Search));
            else if (p.FolderId.HasValue)
                fq = fq.Where(f => f.folderId == p.FolderId);
            var files = await fq
                .Select(f => new { f.Id, name = f.displayName ?? f.filePath, type = f.fileType.ToString(), f.contentType, size = f.length, f.isPublic, f.folderId })
                .ToListAsync(ct);
            var folders = await _context.productFolders.AsNoTracking()
                .Where(f => f.productId == p.ProductId)
                .Select(f => new { f.Id, f.name, f.parentFolderId, fileCount = f.files.Count })
                .ToListAsync(ct);
            return (new { files, folders }, $"{files.Count} files, {folders.Count} folders");
        }
        else
        {
            var fq = _context.storeFiles.AsNoTracking().Where(f => f.storeId == storeId);
            if (!string.IsNullOrEmpty(p.Search))
                fq = fq.Where(f => (f.displayName ?? f.filePath).Contains(p.Search));
            else if (p.FolderId.HasValue)
                fq = fq.Where(f => f.folderId == p.FolderId);
            var files = await fq
                .Select(f => new { f.Id, name = f.displayName ?? f.filePath, type = f.fileType.ToString(), f.contentType, size = f.length, f.isPublic, f.folderId })
                .ToListAsync(ct);
            var folders = await _context.storeFolders.AsNoTracking()
                .Where(f => f.storeId == storeId)
                .Select(f => new { f.Id, f.name, f.parentFolderId, fileCount = f.files.Count })
                .ToListAsync(ct);
            return (new { files, folders }, $"{files.Count} files, {folders.Count} folders");
        }
    }

    internal async Task<(object, string)> CreateFolderAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<CreateFolderInput>(json);
        if (string.IsNullOrWhiteSpace(p.Name)) return (new { error = "Folder name is required" }, "No name");

        if (p.Scope == "product")
        {
            if (!p.ProductId.HasValue) return (new { error = "scope=product requires productId" }, "No productId");
            if (!await ProductInStoreAsync(p.ProductId.Value, storeId, ct)) return (new { error = "Product not found" }, "Not found");
            if (p.ParentFolderId.HasValue && !await _context.productFolders.AsNoTracking()
                    .AnyAsync(f => f.Id == p.ParentFolderId && f.productId == p.ProductId, ct))
                return (new { error = "Parent folder not found" }, "Not found");

            var folder = new ProductFolder { name = p.Name.Trim(), productId = p.ProductId.Value, parentFolderId = p.ParentFolderId };
            _context.productFolders.Add(folder);
            await _context.SaveChangesAsync(ct);
            return (new { id = folder.Id }, $"Folder \"{folder.name}\" created");
        }
        else
        {
            if (p.ParentFolderId.HasValue && !await _context.storeFolders.AsNoTracking()
                    .AnyAsync(f => f.Id == p.ParentFolderId && f.storeId == storeId, ct))
                return (new { error = "Parent folder not found" }, "Not found");

            var folder = new StoreFolder { name = p.Name.Trim(), storeId = storeId, parentFolderId = p.ParentFolderId };
            _context.storeFolders.Add(folder);
            await _context.SaveChangesAsync(ct);
            return (new { id = folder.Id }, $"Folder \"{folder.name}\" created");
        }
    }

    internal async Task<(object, string)> RenameFileAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<RenameFileInput>(json);
        if (string.IsNullOrWhiteSpace(p.NewName)) return (new { error = "New name is required" }, "No name");
        if (p.Scope == "product")
        {
            var file = await _context.files.FirstOrDefaultAsync(f => f.Id == p.FileId && f.product.storeId == storeId, ct);
            if (file == null) return (new { error = "File not found" }, "Not found");
            file.displayName = p.NewName.Trim();
            await _context.SaveChangesAsync(ct);
        }
        else
        {
            var file = await _context.storeFiles.FirstOrDefaultAsync(f => f.Id == p.FileId && f.storeId == storeId, ct);
            if (file == null) return (new { error = "File not found" }, "Not found");
            file.displayName = p.NewName.Trim();
            await _context.SaveChangesAsync(ct);
        }
        return (new { success = true }, $"File renamed to \"{p.NewName}\"");
    }

    internal async Task<(object, string)> RenameFolderAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<RenameFolderInput>(json);
        if (string.IsNullOrWhiteSpace(p.NewName)) return (new { error = "New name is required" }, "No name");
        if (p.Scope == "product")
        {
            var folder = await _context.productFolders.FirstOrDefaultAsync(f => f.Id == p.FolderId && f.product.storeId == storeId, ct);
            if (folder == null) return (new { error = "Folder not found" }, "Not found");
            folder.name = p.NewName.Trim();
            await _context.SaveChangesAsync(ct);
        }
        else
        {
            var folder = await _context.storeFolders.FirstOrDefaultAsync(f => f.Id == p.FolderId && f.storeId == storeId, ct);
            if (folder == null) return (new { error = "Folder not found" }, "Not found");
            folder.name = p.NewName.Trim();
            await _context.SaveChangesAsync(ct);
        }
        return (new { success = true }, $"Folder renamed to \"{p.NewName}\"");
    }

    internal async Task<(object, string)> MoveFilesAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<MoveFilesInput>(json);
        var movedFiles = 0;
        var movedFolders = 0;

        if (p.Scope == "product")
        {
            if (p.TargetFolderId.HasValue && !await _context.productFolders.AsNoTracking()
                    .AnyAsync(f => f.Id == p.TargetFolderId && f.product.storeId == storeId, ct))
                return (new { error = "Target folder not found" }, "Not found");

            if (p.FileIds?.Count > 0)
            {
                var files = await _context.files
                    .Where(f => p.FileIds.Contains(f.Id) && f.product.storeId == storeId)
                    .ToListAsync(ct);
                foreach (var f in files) f.folderId = p.TargetFolderId;
                movedFiles = files.Count;
            }
            if (p.FolderIds?.Count > 0)
            {
                var folders = await _context.productFolders
                    .Where(f => p.FolderIds.Contains(f.Id) && f.product.storeId == storeId)
                    .ToListAsync(ct);
                foreach (var f in folders)
                {
                    if (p.TargetFolderId == f.Id) return (new { error = "Cannot move a folder into itself" }, "Error");
                    f.parentFolderId = p.TargetFolderId;
                }
                movedFolders = folders.Count;
            }
        }
        else
        {
            if (p.TargetFolderId.HasValue && !await _context.storeFolders.AsNoTracking()
                    .AnyAsync(f => f.Id == p.TargetFolderId && f.storeId == storeId, ct))
                return (new { error = "Target folder not found" }, "Not found");

            if (p.FileIds?.Count > 0)
            {
                var files = await _context.storeFiles
                    .Where(f => p.FileIds.Contains(f.Id) && f.storeId == storeId)
                    .ToListAsync(ct);
                foreach (var f in files) f.folderId = p.TargetFolderId;
                movedFiles = files.Count;
            }
            if (p.FolderIds?.Count > 0)
            {
                var folders = await _context.storeFolders
                    .Where(f => p.FolderIds.Contains(f.Id) && f.storeId == storeId)
                    .ToListAsync(ct);
                foreach (var f in folders)
                {
                    if (p.TargetFolderId == f.Id) return (new { error = "Cannot move a folder into itself" }, "Error");
                    // Prevent moving into own descendant
                    var currentId = p.TargetFolderId;
                    while (currentId.HasValue)
                    {
                        if (currentId.Value == f.Id) return (new { error = "Cannot move a folder into its own descendant" }, "Error");
                        currentId = await _context.storeFolders.AsNoTracking()
                            .Where(x => x.Id == currentId.Value).Select(x => x.parentFolderId).FirstOrDefaultAsync(ct);
                    }
                    f.parentFolderId = p.TargetFolderId;
                }
                movedFolders = folders.Count;
            }
        }
        await _context.SaveChangesAsync(ct);
        return (new { movedFiles, movedFolders }, $"Moved: {movedFiles} files, {movedFolders} folders");
    }

    internal async Task<(object, string)> DeleteFilesAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<DeleteFilesInput>(json);
        if (p.FileIds.Count == 0) return (new { error = "fileIds not specified" }, "No files");

        if (p.Scope == "product")
        {
            var files = await _context.files
                .Where(f => p.FileIds.Contains(f.Id) && f.product.storeId == storeId)
                .ToListAsync(ct);
            var ids = files.Select(f => f.Id).ToList();
            if (ids.Count > 0)
            {
                await _context.tags.Where(t => ids.Contains(t.fileId)).ExecuteDeleteAsync(ct);
                await _context.fileShareTokens.Where(t => ids.Contains(t.fileId)).ExecuteDeleteAsync(ct);
            }
            foreach (var f in files)
            {
                await _thumbnailService.DeleteThumbnailsAsync(f.filePath);
                await _storageService.DeleteAsync(f.filePath);
                _context.files.Remove(f);
            }
            await _context.SaveChangesAsync(ct);
            await _operationService.RecordAsync(storeId, userId, "delete_files",
                $"Deleted {files.Count} product files", new { }, canRollback: false, ct);
            return (new { deleted = files.Count }, $"Deleted {files.Count} files");
        }
        else
        {
            var files = await _context.storeFiles
                .Where(f => p.FileIds.Contains(f.Id) && f.storeId == storeId)
                .ToListAsync(ct);
            var ids = files.Select(f => f.Id).ToList();
            if (ids.Count > 0)
            {
                await _context.storeFileTags.Where(t => ids.Contains(t.fileId)).ExecuteDeleteAsync(ct);
                await _context.storeFileShareTokens.Where(t => ids.Contains(t.fileId)).ExecuteDeleteAsync(ct);
            }
            foreach (var f in files)
            {
                await _thumbnailService.DeleteThumbnailsAsync(f.filePath);
                await _storageService.DeleteAsync(f.filePath);
                _context.storeFiles.Remove(f);
            }
            await _context.SaveChangesAsync(ct);
            await _operationService.RecordAsync(storeId, userId, "delete_files",
                $"Deleted {files.Count} store files", new { }, canRollback: false, ct);
            return (new { deleted = files.Count }, $"Deleted {files.Count} files");
        }
    }

    internal async Task<(object, string)> DeleteFolderAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<DeleteFolderInput>(json);
        var recursive = p.Recursive ?? false;

        if (p.Scope == "product")
        {
            var folder = await _context.productFolders.AsNoTracking()
                .Include(f => f.files).Include(f => f.childFolders)
                .FirstOrDefaultAsync(f => f.Id == p.FolderId && f.product.storeId == storeId, ct);
            if (folder == null) return (new { error = "Folder not found" }, "Not found");
            if (!recursive && (folder.files.Count > 0 || folder.childFolders.Count > 0))
                return (new { error = "Folder is not empty - specify recursive=true" }, "Not empty");

            var folderIds = new List<int> { folder.Id };
            if (recursive)
                await CollectProductSubfolderIdsAsync(folder.Id, folderIds, ct);

            var files = await _context.files
                .Where(f => f.folderId.HasValue && folderIds.Contains(f.folderId.Value))
                .ToListAsync(ct);
            var fileIds = files.Select(f => f.Id).ToList();
            if (fileIds.Count > 0)
            {
                await _context.tags.Where(t => fileIds.Contains(t.fileId)).ExecuteDeleteAsync(ct);
                await _context.fileShareTokens.Where(t => fileIds.Contains(t.fileId)).ExecuteDeleteAsync(ct);
            }
            await _context.folderShareTokens
                .Where(t => t.folderId.HasValue && folderIds.Contains(t.folderId.Value)).ExecuteDeleteAsync(ct);
            foreach (var f in files)
            {
                await _thumbnailService.DeleteThumbnailsAsync(f.filePath);
                await _storageService.DeleteAsync(f.filePath);
                _context.files.Remove(f);
            }
            await _context.SaveChangesAsync(ct);
            folderIds.Reverse();
            foreach (var fid in folderIds)
                await _context.productFolders.Where(f => f.Id == fid).ExecuteDeleteAsync(ct);

            await _operationService.RecordAsync(storeId, userId, "delete_folder",
                $"Deleted folder \"{folder.name}\" ({files.Count} files)", new { }, canRollback: false, ct);
            return (new { deletedFiles = files.Count }, $"Folder \"{folder.name}\" deleted ({files.Count} files)");
        }
        else
        {
            var folder = await _context.storeFolders.AsNoTracking()
                .Include(f => f.files).Include(f => f.childFolders)
                .FirstOrDefaultAsync(f => f.Id == p.FolderId && f.storeId == storeId, ct);
            if (folder == null) return (new { error = "Folder not found" }, "Not found");
            if (!recursive && (folder.files.Count > 0 || folder.childFolders.Count > 0))
                return (new { error = "Folder is not empty - specify recursive=true" }, "Not empty");

            var folderIds = new List<int> { folder.Id };
            if (recursive)
                await CollectStoreSubfolderIdsAsync(folder.Id, storeId, folderIds, ct);

            var files = await _context.storeFiles
                .Where(f => f.folderId.HasValue && folderIds.Contains(f.folderId.Value))
                .ToListAsync(ct);
            var fileIds = files.Select(f => f.Id).ToList();
            if (fileIds.Count > 0)
            {
                await _context.storeFileTags.Where(t => fileIds.Contains(t.fileId)).ExecuteDeleteAsync(ct);
                await _context.storeFileShareTokens.Where(t => fileIds.Contains(t.fileId)).ExecuteDeleteAsync(ct);
            }
            await _context.storeFolderShareTokens
                .Where(t => t.folderId.HasValue && folderIds.Contains(t.folderId.Value)).ExecuteDeleteAsync(ct);
            foreach (var f in files)
            {
                await _thumbnailService.DeleteThumbnailsAsync(f.filePath);
                await _storageService.DeleteAsync(f.filePath);
                _context.storeFiles.Remove(f);
            }
            await _context.SaveChangesAsync(ct);
            folderIds.Reverse();
            foreach (var fid in folderIds)
                await _context.storeFolders.Where(f => f.Id == fid).ExecuteDeleteAsync(ct);

            await _operationService.RecordAsync(storeId, userId, "delete_folder",
                $"Deleted folder \"{folder.name}\" ({files.Count} files)", new { }, canRollback: false, ct);
            return (new { deletedFiles = files.Count }, $"Folder \"{folder.name}\" deleted ({files.Count} files)");
        }
    }

    internal async Task<(object, string)> ShareFileAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ShareFileInput>(json);
        var ttl = p.TtlHours ?? 168;
        if (ttl is not (1 or 24 or 168 or 720))
            return (new { error = "Allowed ttlHours: 1, 24, 168 (a week), 720 (a month)" }, "Invalid TTL");
        if (!p.FileId.HasValue && !p.FolderId.HasValue)
            return (new { error = "Specify fileId or folderId" }, "No target");

        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var expiresAt = DateTime.UtcNow.AddHours(ttl);
        string url;

        if (p.Scope == "product")
        {
            if (p.FileId.HasValue)
            {
                var file = await _context.files.AsNoTracking()
                    .FirstOrDefaultAsync(f => f.Id == p.FileId && f.product.storeId == storeId, ct);
                if (file == null) return (new { error = "File not found" }, "Not found");
                _context.fileShareTokens.Add(new FileShareToken
                {
                    fileId = file.Id, token = token, expiresAt = expiresAt, createdByUserId = userId,
                });
                url = $"/api/shared/{token}";
            }
            else
            {
                var folder = await _context.productFolders.AsNoTracking()
                    .FirstOrDefaultAsync(f => f.Id == p.FolderId && f.product.storeId == storeId, ct);
                if (folder == null) return (new { error = "Folder not found" }, "Not found");
                _context.folderShareTokens.Add(new FolderShareToken
                {
                    productId = folder.productId, folderId = folder.Id, token = token,
                    expiresAt = expiresAt, createdByUserId = userId,
                });
                url = $"/api/shared/folder/{token}";
            }
        }
        else
        {
            if (p.FileId.HasValue)
            {
                var exists = await _context.storeFiles.AsNoTracking()
                    .AnyAsync(f => f.Id == p.FileId && f.storeId == storeId, ct);
                if (!exists) return (new { error = "File not found" }, "Not found");
                _context.storeFileShareTokens.Add(new StoreFileShareToken
                {
                    fileId = p.FileId.Value, token = token, expiresAt = expiresAt, createdByUserId = userId,
                });
                url = $"/api/shared/store-file/{token}";
            }
            else
            {
                var exists = await _context.storeFolders.AsNoTracking()
                    .AnyAsync(f => f.Id == p.FolderId && f.storeId == storeId, ct);
                if (!exists) return (new { error = "Folder not found" }, "Not found");
                _context.storeFolderShareTokens.Add(new StoreFolderShareToken
                {
                    storeId = storeId, folderId = p.FolderId, token = token,
                    expiresAt = expiresAt, createdByUserId = userId,
                });
                url = $"/api/shared/store-folder/{token}";
            }
        }
        await _context.SaveChangesAsync(ct);
        return (new { url, expiresAt }, $"Link created, valid until {expiresAt:yyyy-MM-dd HH:mm} UTC");
    }

    private async Task<bool> ProductInStoreAsync(int productId, int storeId, CancellationToken ct)
        => await _context.products.AsNoTracking().AnyAsync(x => x.Id == productId && x.storeId == storeId, ct);

    private async Task CollectProductSubfolderIdsAsync(int folderId, List<int> acc, CancellationToken ct)
    {
        var children = await _context.productFolders.AsNoTracking()
            .Where(f => f.parentFolderId == folderId).Select(f => f.Id).ToListAsync(ct);
        foreach (var c in children)
        {
            acc.Add(c);
            await CollectProductSubfolderIdsAsync(c, acc, ct);
        }
    }

    private async Task CollectStoreSubfolderIdsAsync(int folderId, int storeId, List<int> acc, CancellationToken ct)
    {
        var children = await _context.storeFolders.AsNoTracking()
            .Where(f => f.parentFolderId == folderId && f.storeId == storeId).Select(f => f.Id).ToListAsync(ct);
        foreach (var c in children)
        {
            acc.Add(c);
            await CollectStoreSubfolderIdsAsync(c, storeId, acc, ct);
        }
    }
}

// ---- File-specific DTOs ----

internal class ListFilesInput { public string Scope { get; set; } = "store"; public int? ProductId { get; set; } public int? FolderId { get; set; } public string? Search { get; set; } }
internal class CreateFolderInput { public string Scope { get; set; } = "store"; public string Name { get; set; } = ""; public int? ProductId { get; set; } public int? ParentFolderId { get; set; } }
internal class RenameFileInput { public string Scope { get; set; } = "store"; public int FileId { get; set; } public string NewName { get; set; } = ""; }
internal class RenameFolderInput { public string Scope { get; set; } = "store"; public int FolderId { get; set; } public string NewName { get; set; } = ""; }
internal class MoveFilesInput { public string Scope { get; set; } = "store"; public int? TargetFolderId { get; set; } public List<int>? FileIds { get; set; } public List<int>? FolderIds { get; set; } }
internal class DeleteFilesInput { public string Scope { get; set; } = "store"; public List<int> FileIds { get; set; } = []; }
internal class DeleteFolderInput { public string Scope { get; set; } = "store"; public int FolderId { get; set; } public bool? Recursive { get; set; } }
internal class ShareFileInput { public string Scope { get; set; } = "store"; public int? FileId { get; set; } public int? FolderId { get; set; } public int? TtlHours { get; set; } }
