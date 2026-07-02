using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Security;
using Delegat.Marketplace.SDK.RateLimit;
using Delegat.Models.Marketplace;
using Delegat.Services;
using Delegat.Services.Marketplace;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
public class MarketplaceConnectionController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceProvider _serviceProvider;
    private readonly IMarketplaceAdapterRegistry _adapterRegistry;
    private readonly ILogger<MarketplaceConnectionController> _logger;
    private readonly EncryptionService? _encryptionService;
    private readonly QuotaService _quotaService;

    public MarketplaceConnectionController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider,
        IMarketplaceAdapterRegistry adapterRegistry,
        ILogger<MarketplaceConnectionController> logger,
        QuotaService quotaService,
        EncryptionService? encryptionService = null)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
        _adapterRegistry = adapterRegistry;
        _logger = logger;
        _quotaService = quotaService;
        _encryptionService = encryptionService;
    }

    /// <summary>
    /// Get list of available marketplace adapters
    /// </summary>
    [HttpGet]
    [Route("/marketplace/adapters")]
    public IActionResult GetAvailableAdapters()
    {
        var adapters = _adapterRegistry.GetAll()
            .Select(a => new
            {
                key = a.Key,
                displayName = a.DisplayName,
                description = a.Description,
                capabilities = a.Capabilities.ToString(),
                credentialsSchema = a.GetCredentialsSchema()
            })
            .ToList();

        return Ok(adapters);
    }

    /// <summary>
    /// Get list of store connections
    /// </summary>
    [HttpGet]
    [Route("/marketplace/connections")]
    public async Task<IActionResult> GetConnections()
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var repo = _unitOfWork.Repository<MarketplaceConnection>();

        var connections = await repo.FindAllAsync(
            c => c.storeId == selectedStoreId.Value,
            query => query
                .Include(c => c.categoryMappings)
                .Include(c => c.productLinks));

        var result = connections
            .OrderByDescending(c => c.createdAt)
            .Select(c => ((Infrastructure.Interfaces.IMapToDTO<MarketplaceConnection.ConnectionDTO>)c).MapToDTO(_serviceProvider))
            .ToList();

        return Ok(result);
    }

    /// <summary>
    /// Get connection details
    /// </summary>
    [HttpGet]
    [Route("/marketplace/connections/{connectionId}")]
    public async Task<IActionResult> GetConnection(int connectionId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var repo = _unitOfWork.Repository<MarketplaceConnection>();

        var connection = await repo.FindAsync(
            c => c.Id == connectionId && c.storeId == selectedStoreId.Value,
            query => query
                .Include(c => c.categoryMappings)
                .Include(c => c.productLinks));

        if (connection == null)
            throw ApiException.NotFound("Подключение не найдено");

        return Ok(((Infrastructure.Interfaces.IMapToDTO<MarketplaceConnection.ConnectionDetailDTO>)connection).MapToDTO(_serviceProvider));
    }

    /// <summary>
    /// Create a new connection
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections")]
    public async Task<IActionResult> CreateConnection([FromBody] MarketplaceConnection.UserAddForm input)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        await _quotaService.EnsureConnectionQuotaAsync(selectedStoreId.Value);

        // Verify that the adapter exists
        var adapter = _adapterRegistry.GetByKey(input.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest($"Неизвестный адаптер: {input.adapterKey}");

        var repo = _unitOfWork.Repository<MarketplaceConnection>();

        var encryptedCreds = _encryptionService != null && !string.IsNullOrEmpty(input.credentialsJson)
            ? _encryptionService.Encrypt(input.credentialsJson)
            : input.credentialsJson;

        var connection = new MarketplaceConnection();
        connection.AddFill(new MarketplaceConnection.AddForm
        {
            name = input.name,
            adapterKey = input.adapterKey,
            credentialsJson = encryptedCreds,
            settingsJson = input.settingsJson,
            isActive = input.isActive,
            storeId = selectedStoreId.Value
        });

        await repo.AddAsync(connection);

        return Ok(new { id = connection.Id });
    }

    /// <summary>
    /// Update a connection
    /// </summary>
    [HttpPatch]
    [Route("/marketplace/connections/{connectionId}")]
    public async Task<IActionResult> UpdateConnection(int connectionId, [FromBody] MarketplaceConnection.UpdateForm input)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var repo = _unitOfWork.Repository<MarketplaceConnection>();

        var connection = await repo.FindAsync(
            c => c.Id == connectionId && c.storeId == selectedStoreId.Value);

        if (connection == null)
            throw ApiException.NotFound("Подключение не найдено");

        if (_encryptionService != null && input.credentialsJson != null)
            input.credentialsJson = _encryptionService.Encrypt(input.credentialsJson);

        connection.UpdateFill(input);
        await repo.UpdateAsync(connection);

        return Ok(new { success = true });
    }

    /// <summary>
    /// Delete a connection
    /// </summary>
    [HttpDelete]
    [Route("/marketplace/connections/{connectionId}")]
    public async Task<IActionResult> DeleteConnection(int connectionId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var repo = _unitOfWork.Repository<MarketplaceConnection>();

        var connection = await repo.FindAsync(
            c => c.Id == connectionId && c.storeId == selectedStoreId.Value,
            query => query
                .Include(c => c.categoryMappings)
                .Include(c => c.productLinks));

        if (connection == null)
            throw ApiException.NotFound("Подключение не найдено");

        // Delete related entities
        var categoryMappingRepo = _unitOfWork.Repository<CategoryMapping>();
        var productLinkRepo = _unitOfWork.Repository<ProductMarketplaceLink>();

        foreach (var mapping in connection.categoryMappings.ToList())
        {
            await categoryMappingRepo.DeleteAsync(mapping);
        }

        foreach (var link in connection.productLinks.ToList())
        {
            await productLinkRepo.DeleteAsync(link);
        }

        await repo.DeleteAsync(connection);

        return Ok(new { success = true });
    }

    /// <summary>
    /// Test marketplace connection
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/test")]
    public async Task<IActionResult> TestConnection(int connectionId, CancellationToken ct)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var repo = _unitOfWork.Repository<MarketplaceConnection>();

        var connection = await repo.FindAsync(
            c => c.Id == connectionId && c.storeId == selectedStoreId.Value);

        if (connection == null)
            throw ApiException.NotFound("Подключение не найдено");

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest($"Неизвестный адаптер: {connection.adapterKey}");

        try
        {
            var result = await adapter.TestConnectionAsync(connection, ct);
            return Ok(new
            {
                success = result.Success,
                message = result.Success ? result.Message : null,
                errorMessage = result.Success ? null : result.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing connection {ConnectionId}", connectionId);
            return Ok(new
            {
                success = false,
                message = (string?)null,
                errorMessage = ex.Message
            });
        }
    }

    /// <summary>
    /// Get adapter capabilities for a connection
    /// </summary>
    [HttpGet]
    [Route("/marketplace/connections/{connectionId}/capabilities")]
    public async Task<IActionResult> GetCapabilities(int connectionId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var repo = _unitOfWork.Repository<MarketplaceConnection>();

        var connection = await repo.FindAsync(
            c => c.Id == connectionId && c.storeId == selectedStoreId.Value);

        if (connection == null)
            throw ApiException.NotFound("Подключение не найдено");

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest($"Неизвестный адаптер: {connection.adapterKey}");

        var capabilities = adapter.Capabilities;

        return Ok(new
        {
            adapterKey = adapter.Key,
            displayName = adapter.DisplayName,
            productEditUrlTemplate = adapter.ProductEditUrlTemplate,
            capabilities = new
            {
                getCategories = capabilities.HasFlag(MarketplaceCapabilities.GetCategories),
                getCategoryFields = capabilities.HasFlag(MarketplaceCapabilities.GetCategoryFields),
                createCategory = capabilities.HasFlag(MarketplaceCapabilities.CreateCategory),
                importProduct = capabilities.HasFlag(MarketplaceCapabilities.ImportProduct),
                importAllProducts = capabilities.HasFlag(MarketplaceCapabilities.ImportAllProducts),
                importProductBatch = capabilities.HasFlag(MarketplaceCapabilities.ImportProductBatch),
                exportProduct = capabilities.HasFlag(MarketplaceCapabilities.ExportProduct),
                exportProductBulk = capabilities.HasFlag(MarketplaceCapabilities.ExportProductBulk),
                updateSku = capabilities.HasFlag(MarketplaceCapabilities.UpdateSku),
                importPrices = capabilities.HasFlag(MarketplaceCapabilities.ImportPrices),
                exportPrices = capabilities.HasFlag(MarketplaceCapabilities.ExportPrices),
                importStocks = capabilities.HasFlag(MarketplaceCapabilities.ImportStocks),
                exportStocks = capabilities.HasFlag(MarketplaceCapabilities.ExportStocks),
                getOrders = capabilities.HasFlag(MarketplaceCapabilities.GetOrders),
                updateOrderStatus = capabilities.HasFlag(MarketplaceCapabilities.UpdateOrderStatus),
                fieldBasedGrouping = capabilities.HasFlag(MarketplaceCapabilities.FieldBasedGrouping),
                endpointBasedGrouping = capabilities.HasFlag(MarketplaceCapabilities.EndpointBasedGrouping),
                importGroups = capabilities.HasFlag(MarketplaceCapabilities.ImportGroups),
                deleteGroups = capabilities.HasFlag(MarketplaceCapabilities.DeleteGroups),
                modifyGroupMembership = capabilities.HasFlag(MarketplaceCapabilities.ModifyGroupMembership),
                archiveProduct = capabilities.HasFlag(MarketplaceCapabilities.ArchiveProduct),
            }
        });
    }

    /// <summary>
    /// Get API rate limits and quotas for a connection
    /// </summary>
    [HttpGet]
    [Route("/marketplace/connections/{connectionId}/limits")]
    public async Task<IActionResult> GetLimits(int connectionId, CancellationToken ct)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var repo = _unitOfWork.Repository<MarketplaceConnection>();

        var connection = await repo.FindAsync(
            c => c.Id == connectionId && c.storeId == selectedStoreId.Value);

        if (connection == null)
            throw ApiException.NotFound("Подключение не найдено");

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest($"Неизвестный адаптер: {connection.adapterKey}");

        var rateLimitConfig = adapter.GetRateLimitConfig();

        // No rate limiting support
        if (rateLimitConfig == null)
        {
            return Ok(new
            {
                supported = false
            });
        }

        // Get quotas from marketplace API
        var quotas = await adapter.GetQuotasAsync(connection, ct);

        // Get rate limiter status
        var rateLimiter = adapter.GetRateLimiterForConnection(connection);
        var currentWindowRequests = rateLimiter?.GetCurrentWindowCount() ?? 0;
        var totalRequestsSinceStart = rateLimiter?.TotalRequests ?? 0;

        return Ok(new
        {
            supported = true,
            maxRequestsPerSecond = rateLimitConfig.MaxRequestsPerSecond,
            currentWindowRequests,
            totalRequestsSinceStart,
            quotas = quotas.Select(q => new
            {
                key = q.Key,
                displayName = q.DisplayName,
                limit = q.Limit,
                usage = q.Usage,
                resetsAt = q.ResetsAt
            })
        });
    }
}
