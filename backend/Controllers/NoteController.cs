using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Products;
using static Delegat.Models.Note;

namespace Delegat.Controllers;

/// <summary>
/// Controller for managing notes attached to products.
/// </summary>
[ApiController]
[JwtAuthorize]
public class NoteController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceProvider _serviceProvider;
    private readonly DataContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="NoteController"/> class.
    /// </summary>
    public NoteController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider,
        DataContext context)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
        _context = context;
    }

    /// <summary>
    /// Lists notes for the selected store with search, filters, and sorting.
    /// </summary>
    [HttpGet]
    [Route("/notes")]
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> ListNotes(
        [FromQuery] string? search = null,
        [FromQuery] string? productQuery = null,
        [FromQuery] Importance? importance = null,
        [FromQuery] int? authorUserId = null,
        [FromQuery] bool authorUnknown = false,
        [FromQuery] string sortBy = "createdAt",
        [FromQuery] bool sortDesc = true,
        [FromQuery] int startIndex = 0,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        if (startIndex < 0) startIndex = 0;
        if (pageSize < 1) pageSize = 25;
        if (pageSize > 200) pageSize = 200;

        var query =
            from n in _context.notes.AsNoTracking()
            join p in _context.products.AsNoTracking() on n.productId equals p.Id
            where p.storeId == selectedStoreId.Value && p.deletedAt == null
            select new { Note = n, Product = p };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(x => x.Note.text.Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(productQuery))
        {
            var pq = productQuery.Trim();
            query = query.Where(x =>
                x.Product.name.Contains(pq) ||
                x.Product.sku.Contains(pq));
        }

        if (importance.HasValue)
            query = query.Where(x => x.Note.importance == importance.Value);

        if (authorUnknown)
            query = query.Where(x => x.Note.authorUserId == null);
        else if (authorUserId.HasValue)
            query = query.Where(x => x.Note.authorUserId == authorUserId.Value);

        query = (sortBy?.ToLowerInvariant()) switch
        {
            "updatedat" => sortDesc
                ? query.OrderByDescending(x => x.Note.updatedAt).ThenByDescending(x => x.Note.Id)
                : query.OrderBy(x => x.Note.updatedAt).ThenBy(x => x.Note.Id),
            "importance" => sortDesc
                ? query.OrderByDescending(x => x.Note.importance).ThenByDescending(x => x.Note.createdAt)
                : query.OrderBy(x => x.Note.importance).ThenByDescending(x => x.Note.createdAt),
            "product" => sortDesc
                ? query.OrderByDescending(x => x.Product.name).ThenByDescending(x => x.Note.createdAt)
                : query.OrderBy(x => x.Product.name).ThenByDescending(x => x.Note.createdAt),
            _ => sortDesc
                ? query.OrderByDescending(x => x.Note.createdAt).ThenByDescending(x => x.Note.Id)
                : query.OrderBy(x => x.Note.createdAt).ThenBy(x => x.Note.Id),
        };

        var totalCount = await query.CountAsync(ct);

        var page = await query
            .Skip(startIndex)
            .Take(pageSize)
            .Select(x => new
            {
                x.Note.Id,
                x.Note.text,
                x.Note.importance,
                x.Note.authorUserId,
                x.Note.createdAt,
                x.Note.updatedAt,
                productId = x.Product.Id,
                productName = x.Product.name,
                productSku = x.Product.sku
            })
            .ToListAsync(ct);

        var authorIds = page
            .Where(i => i.authorUserId.HasValue)
            .Select(i => i.authorUserId!.Value)
            .Distinct()
            .ToList();

        var authorNames = authorIds.Count == 0
            ? new Dictionary<int, string?>()
            : await _context.Users.AsNoTracking()
                .Where(u => authorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => (string?)u.UserName, ct);

        var items = page.Select(i => new
        {
            id = i.Id,
            text = i.text,
            importance = i.importance,
            authorUserId = i.authorUserId,
            authorUserName = i.authorUserId.HasValue && authorNames.TryGetValue(i.authorUserId.Value, out var name)
                ? name
                : null,
            createdAt = i.createdAt,
            updatedAt = i.updatedAt,
            productId = i.productId,
            productName = i.productName,
            productSku = i.productSku
        }).ToList();

        var storeAuthorIds = await (
            from n in _context.notes.AsNoTracking()
            join p in _context.products.AsNoTracking() on n.productId equals p.Id
            where p.storeId == selectedStoreId.Value && p.deletedAt == null && n.authorUserId != null
            select n.authorUserId!.Value
        ).Distinct().ToListAsync(ct);

        var hasUnknownAuthor = await (
            from n in _context.notes.AsNoTracking()
            join p in _context.products.AsNoTracking() on n.productId equals p.Id
            where p.storeId == selectedStoreId.Value && p.deletedAt == null && n.authorUserId == null
            select n.Id
        ).AnyAsync(ct);

        var filterAuthorNames = storeAuthorIds.Count == 0
            ? new Dictionary<int, string?>()
            : await _context.Users.AsNoTracking()
                .Where(u => storeAuthorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => (string?)u.UserName, ct);

        var authors = storeAuthorIds
            .Select(id => new
            {
                id,
                userName = filterAuthorNames.TryGetValue(id, out var un) ? un : null
            })
            .OrderBy(a => a.userName)
            .ToList();

        return Ok(new
        {
            items,
            totalCount,
            startIndex,
            pageSize,
            lastPage = startIndex + pageSize >= totalCount,
            authors,
            hasUnknownAuthor
        });
    }

    /// <summary>
    /// Creates a new note for a product.
    /// </summary>
    [HttpPost]
    [Route("/products/{productId}/notes")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> PostNote(int productId, [FromBody] UserAddForm input)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var productRepo = _unitOfWork.Repository<Product>();
        var product = await productRepo.FindAsync(p => p.storeId == selectedStoreId.Value && p.Id == productId);

        if (product == null)
            throw ApiException.NotFound("Product not found", code: "product_not_found");

        var noteRepo = _unitOfWork.Repository<Note>();
        var note = new Note();
        note.AddFill(new AddForm(input, productId, _currentUserService.GetUserId()));
        await noteRepo.AddAsync(note);
        await _unitOfWork.SaveChangesAsync();

        return Ok(note.MapToDTO(_serviceProvider));
    }

    /// <summary>
    /// Updates an existing note on a product.
    /// </summary>
    [HttpPatch]
    [Route("/products/{productId}/notes/{id}")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> PatchNote(int productId, int id, [FromBody] UpdateForm input)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var productRepo = _unitOfWork.Repository<Product>();
        var product = await productRepo.FindAsync(p => p.storeId == selectedStoreId.Value && p.Id == productId);

        if (product == null)
            throw ApiException.NotFound("Product not found", code: "product_not_found");

        var noteRepo = _unitOfWork.Repository<Note>();
        var note = await noteRepo.FindAsync(n => n.productId == productId && n.Id == id);

        if (note == null)
            throw ApiException.NotFound("Note not found");

        note.UpdateFill(input);
        await noteRepo.UpdateAsync(note);
        await _unitOfWork.SaveChangesAsync();

        return Ok();
    }

    /// <summary>
    /// Deletes a note from a product.
    /// </summary>
    [HttpDelete]
    [Route("/products/{productId}/notes/{id}")]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> DeleteNote(int productId, int id)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var productRepo = _unitOfWork.Repository<Product>();
        var product = await productRepo.FindAsync(p => p.storeId == selectedStoreId.Value && p.Id == productId);

        if (product == null)
            throw ApiException.NotFound("Product not found", code: "product_not_found");

        var noteRepo = _unitOfWork.Repository<Note>();
        var note = await noteRepo.FindAsync(n => n.productId == productId && n.Id == id);

        if (note == null)
            throw ApiException.NotFound("Note not found");

        await noteRepo.DeleteAsync(note);
        await _unitOfWork.SaveChangesAsync();

        return Ok();
    }
}
