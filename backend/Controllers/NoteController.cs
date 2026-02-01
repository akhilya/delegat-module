using Microsoft.AspNetCore.Mvc;
using PrintO.Infrastructure.Auth;
using PrintO.Infrastructure.Data;
using PrintO.Infrastructure.Middleware;
using PrintO.Models;
using PrintO.Models.Products;
using static PrintO.Models.Note;

namespace PrintO.Controllers;

[ApiController]
[JwtAuthorize]
public class NoteController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceProvider _serviceProvider;

    public NoteController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
    }

    [HttpPost]
    [Route("/products/{productId}/notes")]
    public async Task<IActionResult> PostNote(int productId, [FromBody] UserAddForm input)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("No store selected");

        var productRepo = _unitOfWork.Repository<Product>();
        var product = await productRepo.FindAsync(p => p.storeId == selectedStoreId.Value && p.Id == productId);

        if (product == null)
            throw ApiException.NotFound("Product not found");

        var noteRepo = _unitOfWork.Repository<Note>();
        var note = new Note();
        note.AddFill(new AddForm(input, productId));
        await noteRepo.AddAsync(note);
        await _unitOfWork.SaveChangesAsync();

        return Ok(note.MapToDTO(_serviceProvider));
    }

    [HttpPatch]
    [Route("/products/{productId}/notes/{id}")]
    public async Task<IActionResult> PatchNote(int productId, int id, [FromBody] UpdateForm input)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("No store selected");

        var productRepo = _unitOfWork.Repository<Product>();
        var product = await productRepo.FindAsync(p => p.storeId == selectedStoreId.Value && p.Id == productId);

        if (product == null)
            throw ApiException.NotFound("Product not found");

        var noteRepo = _unitOfWork.Repository<Note>();
        var note = await noteRepo.FindAsync(n => n.productId == productId && n.Id == id);

        if (note == null)
            throw ApiException.NotFound("Note not found");

        note.UpdateFill(input);
        await noteRepo.UpdateAsync(note);
        await _unitOfWork.SaveChangesAsync();

        return Ok();
    }

    [HttpDelete]
    [Route("/products/{productId}/notes/{id}")]
    public async Task<IActionResult> DeleteNote(int productId, int id)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("No store selected");

        var productRepo = _unitOfWork.Repository<Product>();
        var product = await productRepo.FindAsync(p => p.storeId == selectedStoreId.Value && p.Id == productId);

        if (product == null)
            throw ApiException.NotFound("Product not found");

        var noteRepo = _unitOfWork.Repository<Note>();
        var note = await noteRepo.FindAsync(n => n.productId == productId && n.Id == id);

        if (note == null)
            throw ApiException.NotFound("Note not found");

        await noteRepo.DeleteAsync(note);
        await _unitOfWork.SaveChangesAsync();

        return Ok();
    }
}
