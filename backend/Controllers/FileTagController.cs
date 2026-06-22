using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrintO.Enums;
using PrintO.Infrastructure.Auth;
using PrintO.Infrastructure.Data;
using PrintO.Infrastructure.Middleware;
using PrintO.Models;

namespace PrintO.Controllers;

/// <summary>
/// Manage store-wide tag palette and per-file tag assignments.
/// </summary>
[ApiController]
[JwtAuthorize]
public class FileTagController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly DataContext _context;

    public FileTagController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        DataContext context)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _context = context;
    }

    // ── Store Tag Palette ──────────────────────────────────────────────

    /// <summary>Get all tag definitions for the current store.</summary>
    [HttpGet]
    [Route("/tags")]
    public async Task<IActionResult> GetTags(CancellationToken ct)
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        if (storeId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var tags = await _context.tagDefinitions
            .Where(td => td.storeId == storeId.Value)
            .OrderBy(td => td.name)
            .Select(td => new
            {
                td.Id,
                td.name,
                td.color,
                usageCount = _context.tags.Count(ft => ft.tagDefinitionId == td.Id)
                    + _context.storeFileTags.Count(ft => ft.tagDefinitionId == td.Id)
            })
            .ToListAsync(ct);

        return Ok(tags);
    }

    /// <summary>Create a new tag definition in the store palette.</summary>
    [HttpPost]
    [Route("/tags")]
    public async Task<IActionResult> CreateTag([FromBody] CreateTagInput input, CancellationToken ct)
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        if (storeId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        if (string.IsNullOrWhiteSpace(input.name))
            throw ApiException.BadRequest("Название тэга обязательно");

        var trimmedName = input.name.Trim();
        if (trimmedName.Length > TagDefinition.TAG_NAME_MAX_LENGTH)
            throw ApiException.BadRequest($"Название тэга не может быть длиннее {TagDefinition.TAG_NAME_MAX_LENGTH} символов");

        // Check for duplicate name in store
        var exists = await _context.tagDefinitions
            .AnyAsync(td => td.storeId == storeId.Value && td.name == trimmedName, ct);
        if (exists)
            throw ApiException.BadRequest($"Тэг «{trimmedName}» уже существует");

        var tag = new TagDefinition
        {
            name = trimmedName,
            color = input.color,
            storeId = storeId.Value,
            createdAt = DateTime.UtcNow
        };
        _context.tagDefinitions.Add(tag);
        await _context.SaveChangesAsync(ct);

        return Ok(new { tag.Id, tag.name, tag.color });
    }

    /// <summary>Update a tag definition — changes apply everywhere it's used.</summary>
    [HttpPatch]
    [Route("/tags/{id}")]
    public async Task<IActionResult> UpdateTag(int id, [FromBody] UpdateTagInput input, CancellationToken ct)
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        if (storeId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var tag = await _context.tagDefinitions
            .FirstOrDefaultAsync(td => td.Id == id && td.storeId == storeId.Value, ct);
        if (tag == null)
            throw ApiException.NotFound("Тэг не найден");

        if (input.name != null)
        {
            var trimmed = input.name.Trim();
            if (trimmed.Length == 0)
                throw ApiException.BadRequest("Название тэга обязательно");
            if (trimmed.Length > TagDefinition.TAG_NAME_MAX_LENGTH)
                throw ApiException.BadRequest($"Название тэга не может быть длиннее {TagDefinition.TAG_NAME_MAX_LENGTH} символов");

            // Check duplicate (different id, same name)
            var duplicate = await _context.tagDefinitions
                .AnyAsync(td => td.storeId == storeId.Value && td.name == trimmed && td.Id != id, ct);
            if (duplicate)
                throw ApiException.BadRequest($"Тэг «{trimmed}» уже существует");

            tag.name = trimmed;
        }

        if (input.color.HasValue)
            tag.color = input.color.Value;

        await _context.SaveChangesAsync(ct);
        return Ok(new { tag.Id, tag.name, tag.color });
    }

    /// <summary>Delete a tag definition + all file assignments.</summary>
    [HttpDelete]
    [Route("/tags/{id}")]
    public async Task<IActionResult> DeleteTag(int id, CancellationToken ct)
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        if (storeId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var tag = await _context.tagDefinitions
            .FirstOrDefaultAsync(td => td.Id == id && td.storeId == storeId.Value, ct);
        if (tag == null)
            throw ApiException.NotFound("Тэг не найден");

        // Remove all assignments first
        await _context.tags
            .Where(ft => ft.tagDefinitionId == id)
            .ExecuteDeleteAsync(ct);
        await _context.storeFileTags
            .Where(ft => ft.tagDefinitionId == id)
            .ExecuteDeleteAsync(ct);

        _context.tagDefinitions.Remove(tag);
        await _context.SaveChangesAsync(ct);

        return Ok();
    }

    // ── File Tag Assignment ────────────────────────────────────────────

    /// <summary>Assign a tag to a file.</summary>
    [HttpPost]
    [Route("/files/{fileId}/tags/{tagId}")]
    public async Task<IActionResult> AssignTag(int fileId, int tagId, CancellationToken ct)
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        if (storeId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        // Verify file belongs to this store
        var fileExists = await _context.files
            .AnyAsync(f => f.Id == fileId && f.product.storeId == storeId.Value, ct);
        if (!fileExists)
            throw ApiException.NotFound("Файл не найден");

        // Verify tag belongs to this store
        var tagExists = await _context.tagDefinitions
            .AnyAsync(td => td.Id == tagId && td.storeId == storeId.Value, ct);
        if (!tagExists)
            throw ApiException.NotFound("Тэг не найден");

        // Check if already assigned
        var alreadyAssigned = await _context.tags
            .AnyAsync(ft => ft.fileId == fileId && ft.tagDefinitionId == tagId, ct);
        if (alreadyAssigned)
            return Ok(); // idempotent

        _context.tags.Add(new FileTag { fileId = fileId, tagDefinitionId = tagId });
        await _context.SaveChangesAsync(ct);

        return Ok();
    }

    /// <summary>Remove a tag from a file.</summary>
    [HttpDelete]
    [Route("/files/{fileId}/tags/{tagId}")]
    public async Task<IActionResult> RemoveTag(int fileId, int tagId, CancellationToken ct)
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        if (storeId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        await _context.tags
            .Where(ft => ft.fileId == fileId && ft.tagDefinitionId == tagId)
            .ExecuteDeleteAsync(ct);

        return Ok();
    }

    /// <summary>Assign a tag to multiple files at once.</summary>
    [HttpPost]
    [Route("/files/bulk-tag")]
    public async Task<IActionResult> BulkAssignTag([FromBody] BulkTagInput input, CancellationToken ct)
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        if (storeId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        if (input.fileIds == null || input.fileIds.Length == 0)
            throw ApiException.BadRequest("Файлы не указаны");

        // Verify tag
        var tagExists = await _context.tagDefinitions
            .AnyAsync(td => td.Id == input.tagId && td.storeId == storeId.Value, ct);
        if (!tagExists)
            throw ApiException.NotFound("Тэг не найден");

        // Get already-assigned file IDs to avoid duplicates
        var alreadyAssigned = await _context.tags
            .Where(ft => ft.tagDefinitionId == input.tagId && input.fileIds.Contains(ft.fileId))
            .Select(ft => ft.fileId)
            .ToListAsync(ct);
        var alreadySet = new HashSet<int>(alreadyAssigned);

        var added = 0;
        foreach (var fileId in input.fileIds)
        {
            if (alreadySet.Contains(fileId)) continue;
            _context.tags.Add(new FileTag { fileId = fileId, tagDefinitionId = input.tagId });
            added++;
        }

        if (added > 0)
            await _context.SaveChangesAsync(ct);

        return Ok(new { added });
    }

    // ── DTOs ───────────────────────────────────────────────────────────

    public struct CreateTagInput
    {
        public string name { get; set; }
        public FileTagColor color { get; set; }
    }

    public struct UpdateTagInput
    {
        public string? name { get; set; }
        public FileTagColor? color { get; set; }
    }

    public struct BulkTagInput
    {
        public int[] fileIds { get; set; }
        public int tagId { get; set; }
    }
}
