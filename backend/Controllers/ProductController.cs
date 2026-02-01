using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrintO.Infrastructure.Auth;
using PrintO.Infrastructure.Data;
using PrintO.Infrastructure.Middleware;
using PrintO.Models.Products;

namespace PrintO.Controllers;

[ApiController]
[JwtAuthorize]
public class ProductController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProductController> _logger;

    public ProductController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider,
        ILogger<ProductController> logger)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    [HttpGet]
    [Route("/products")]
    public async Task<IActionResult> GetAllProducts(string? searchQuery, int startIndex = 0, int pageSize = 12)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("No store selected");

        var productRepo = _unitOfWork.Repository<Product>();

        IEnumerable<Product> products;

        if (string.IsNullOrEmpty(searchQuery))
        {
            products = await productRepo.FindAllAsync(
                p => p.storeId == selectedStoreId.Value,
                query => query
                    .Include(p => p.files)
                    .Include(p => p.images)
                    .Include(p => p.ozonIntegrations)
                    .Include(p => p.wbIntegrations));
        }
        else
        {
            products = await productRepo.FindAllAsync(
                p => p.storeId == selectedStoreId.Value &&
                    (p.name.Contains(searchQuery) ||
                     (p.oldSKU != null && p.oldSKU.Contains(searchQuery)) ||
                     p.newSKU.Contains(searchQuery) ||
                     (!string.IsNullOrEmpty(p.series) && p.series.Contains(searchQuery))),
                query => query
                    .Include(p => p.files)
                    .Include(p => p.images)
                    .Include(p => p.ozonIntegrations)
                    .Include(p => p.wbIntegrations));
        }

        var total = products.Count();
        var paged = products
            .OrderByDescending(p => p.Id)
            .Skip(startIndex)
            .Take(pageSize)
            .Select(p => ((Infrastructure.Interfaces.IMapToDTO<Product.ProductReviewDTO>)p).MapToDTO(_serviceProvider))
            .ToList();

        return Ok(new
        {
            items = paged,
            total
        });
    }
}
