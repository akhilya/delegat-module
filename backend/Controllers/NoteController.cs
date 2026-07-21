using Microsoft.AspNetCore.Mvc;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="NoteController"/> class.
    /// </summary>
    /// <param name="currentUserService">Service for resolving the current authenticated user.</param>
    /// <param name="unitOfWork">Unit of work for database operations.</param>
    /// <param name="serviceProvider">Service provider for dependency resolution.</param>
    public NoteController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Creates a new note for a product.
    /// </summary>
    /// <param name="productId">The product identifier.</param>
    /// <param name="input">The note creation form data.</param>
    /// <returns>The created note DTO.</returns>
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
        note.AddFill(new AddForm(input, productId));
        await noteRepo.AddAsync(note);
        await _unitOfWork.SaveChangesAsync();

        return Ok(note.MapToDTO(_serviceProvider));
    }

    /// <summary>
    /// Updates an existing note on a product.
    /// </summary>
    /// <param name="productId">The product identifier.</param>
    /// <param name="id">The note identifier.</param>
    /// <param name="input">The fields to update.</param>
    /// <returns>200 OK on success.</returns>
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
    /// <param name="productId">The product identifier.</param>
    /// <param name="id">The note identifier.</param>
    /// <returns>200 OK on success.</returns>
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
