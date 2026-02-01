using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrintO.Infrastructure.Auth;
using PrintO.Infrastructure.Data;
using PrintO.Infrastructure.Interfaces;
using PrintO.Infrastructure.Middleware;
using PrintO.Infrastructure.Storage;
using PrintO.Models;
using PrintO.Models.Products;
using PrintO.Models.Products.Figurine;
using System.Text.RegularExpressions;

namespace PrintO.Controllers;

[ApiController]
[JwtAuthorize]
public class FigurineController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IStorageService _storageService;
    private readonly IServiceProvider _serviceProvider;

    public FigurineController(
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

    [HttpGet]
    [Route("products/{productId}/figurine")]
    public async Task<IActionResult> GetFigurine(int productId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("No store selected");

        var figurineRepo = _unitOfWork.Repository<FigurineReference>();
        var figurine = await figurineRepo.FindAsync(
            f => f.product.storeId == selectedStoreId.Value && f.productId == productId,
            query => query
                .Include(f => f.product)
                    .ThenInclude(p => p.files)
                        .ThenInclude(f => f.tags)
                .Include(f => f.product)
                    .ThenInclude(p => p.images)
                .Include(f => f.product)
                    .ThenInclude(p => p.notes)
                .Include(f => f.product)
                    .ThenInclude(p => p.ozonIntegrations)
                .Include(f => f.product)
                    .ThenInclude(p => p.wbIntegrations)
                .Include(f => f.variations));

        if (figurine == null)
            throw ApiException.NotFound("Figurine not found");

        return Ok(figurine.MapToDTO(_serviceProvider));
    }

    [HttpPost]
    [Route("products/figurines")]
    public async Task<IActionResult> PostFigurine([FromBody] UserAddForm input)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("No store selected");

        var productRepo = _unitOfWork.Repository<Product>();
        var existingProduct = await productRepo.FindAsync(p => p.storeId == selectedStoreId.Value && p.newSKU == input.product.SKU);
        if (existingProduct != null)
            throw ApiException.BadRequest("Product with such SKU already exists");

        var figurineRepo = _unitOfWork.Repository<FigurineReference>();
        var variationRepo = _unitOfWork.Repository<FigurineVariation>();

        var existingVariation = await variationRepo.FindAsync(
            v => v.figurine.product.storeId == selectedStoreId.Value && v.separateSKU == input.product.SKU,
            query => query.Include(v => v.figurine).ThenInclude(f => f.product));
        if (existingVariation != null)
            throw ApiException.BadRequest("Figurine variation with such SKU already exists");

        // Create product
        var product = new Product();
        product.AddFill(new Product.AddForm(input.product, selectedStoreId.Value));
        await productRepo.AddAsync(product);
        await _unitOfWork.SaveChangesAsync();

        // Create figurine reference
        var figurine = new FigurineReference();
        figurine.AddFill(new FigurineReference.AddForm(product.Id));
        await figurineRepo.AddAsync(figurine);
        await _unitOfWork.SaveChangesAsync();

        // Create variations
        foreach (var variationAdd in input.variations)
        {
            var separateSKU = await GenerateUniqueFigurineVariationSKU(product.Id);
            var variation = new FigurineVariation();
            variation.AddFill(new FigurineVariation.AddForm(variationAdd, figurine.Id, separateSKU));
            await variationRepo.AddAsync(variation);
        }

        await _unitOfWork.SaveChangesAsync();

        return Ok(new { next = $"/products/{product.Id}/edit" });
    }

    [HttpPatch]
    [Route("products/{productId}/figurines")]
    public async Task<IActionResult> PatchFigurine(int productId, [FromBody] UserUpdateForm input)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("No store selected");

        var figurineRepo = _unitOfWork.Repository<FigurineReference>();
        var figurine = await figurineRepo.FindAsync(
            f => f.product.storeId == selectedStoreId.Value && f.productId == productId,
            query => query.Include(f => f.product));

        if (figurine == null)
            throw ApiException.NotFound("Figurine not found");

        // Update product
        var productRepo = _unitOfWork.Repository<Product>();
        var product = await productRepo.GetByIdAsync(productId);
        if (product != null)
        {
            product.UpdateFill(input.product);
            await productRepo.UpdateAsync(product);
        }

        var variationRepo = _unitOfWork.Repository<FigurineVariation>();

        // Update variations
        foreach (var variationUpdate in input.updateVariations ?? Array.Empty<FigurineVariation.UpdateForm>())
        {
            var variation = await variationRepo.GetByIdAsync(variationUpdate.id);
            if (variation != null && variation.figurineId == figurine.Id)
            {
                variation.UpdateFill(variationUpdate);
                await variationRepo.UpdateAsync(variation);
            }
        }

        // Add new variations
        foreach (var variationAdd in input.addVariations ?? Array.Empty<FigurineVariation.UserAddForm>())
        {
            var separateSKU = await GenerateUniqueFigurineVariationSKU(productId);
            var variation = new FigurineVariation();
            variation.AddFill(new FigurineVariation.AddForm(variationAdd, figurine.Id, separateSKU));
            await variationRepo.AddAsync(variation);
        }

        // Remove variations
        foreach (var removeVariationId in input.removeVariations ?? Array.Empty<int>())
        {
            var variation = await variationRepo.GetByIdAsync(removeVariationId);
            if (variation != null && variation.figurineId == figurine.Id)
            {
                await variationRepo.DeleteAsync(variation);
            }
        }

        await _unitOfWork.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete]
    [Route("products/{productId}/figurines")]
    public async Task<IActionResult> DeleteFigurine(int productId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("No store selected");

        var figurineRepo = _unitOfWork.Repository<FigurineReference>();
        var figurine = await figurineRepo.FindAsync(
            f => f.product.storeId == selectedStoreId.Value && f.productId == productId,
            query => query.Include(f => f.product));

        if (figurine == null)
            throw ApiException.NotFound("Figurine not found");

        // Delete images
        var imageRepo = _unitOfWork.Repository<ImageReference>();
        var images = await imageRepo.FindAllAsync(i => i.productId == productId);
        foreach (var image in images)
        {
            await imageRepo.DeleteAsync(image);
        }

        // Delete files from storage and database
        var fileRepo = _unitOfWork.Repository<Models.File>();
        var files = await fileRepo.FindAllAsync(f => f.productId == productId);
        foreach (var file in files)
        {
            await _storageService.DeleteAsync(file.filePath);
            await fileRepo.DeleteAsync(file);
        }

        // Delete variations
        var variationRepo = _unitOfWork.Repository<FigurineVariation>();
        var variations = await variationRepo.FindAllAsync(v => v.figurineId == figurine.Id);
        foreach (var variation in variations)
        {
            await variationRepo.DeleteAsync(variation);
        }

        // Delete figurine reference
        await figurineRepo.DeleteAsync(figurine);

        // Delete product
        var productRepo = _unitOfWork.Repository<Product>();
        await productRepo.DeleteAsync(figurine.product);

        await _unitOfWork.SaveChangesAsync();

        return Ok(new { next = "/products" });
    }

    private async Task<string> GenerateUniqueFigurineVariationSKU(int productId)
    {
        var figurineRepo = _unitOfWork.Repository<FigurineReference>();
        var figurine = await figurineRepo.FindAsync(
            f => f.productId == productId,
            query => query.Include(f => f.product).Include(f => f.variations));

        if (figurine == null)
            throw ApiException.NotFound("Figurine not found");

        if (figurine.variations.Count == 0)
        {
            return figurine.product.newSKU;
        }
        else
        {
            var match = Regex.Match(figurine.product.newSKU, @"^\D+");
            string prefix = match.Success ? match.Value : "";
            var numberMatch = Regex.Match(figurine.product.newSKU, @"\d+");
            int number = numberMatch.Success ? int.Parse(numberMatch.Value) : 0;

            var variationRepo = _unitOfWork.Repository<FigurineVariation>();
            string newSeparateSKU;

            do
            {
                number++;
                newSeparateSKU = prefix + number;
            }
            while (await variationRepo.AnyAsync(v => v.separateSKU == newSeparateSKU));

            return newSeparateSKU;
        }
    }

    public struct UserAddForm
    {
        public Product.UserAddForm product { get; set; }
        public FigurineVariation.UserAddForm[] variations { get; set; }
    }

    public struct UserUpdateForm
    {
        public Product.UpdateForm product { get; set; }
        public FigurineVariation.UpdateForm[]? updateVariations { get; set; }
        public FigurineVariation.UserAddForm[]? addVariations { get; set; }
        public int[]? removeVariations { get; set; }
    }
}
