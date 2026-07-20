using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Security;
using Delegat.Marketplace.SDK.RateLimit;
using Delegat.Services.Plugins;
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
    private readonly PluginManager _pluginManager;
    private readonly ILogger<MarketplaceConnectionController> _logger;
    private readonly EncryptionService? _encryptionService;
    private readonly QuotaService _quotaService;
    private readonly DataContext _db;

    public MarketplaceConnectionController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider,
        IMarketplaceAdapterRegistry adapterRegistry,
        PluginManager pluginManager,
        ILogger<MarketplaceConnectionController> logger,
        QuotaService quotaService,
        DataContext db,
        EncryptionService? encryptionService = null)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
        _adapterRegistry = adapterRegistry;
        _pluginManager = pluginManager;
        _logger = logger;
        _quotaService = quotaService;
        _encryptionService = encryptionService;
        _db = db;
    }

    /// <summary>
    /// Get list of available marketplace adapters
    /// </summary>
    [HttpGet]
    [Route("/marketplace/adapters")]
    public IActionResult GetAvailableAdapters()
    {
        var adapters = _adapterRegistry.GetAll()
            .Select(a =>
            {
                var plugin = _pluginManager.GetPlugin(a.Key);
                return new
                {
                    key = a.Key,
                    displayName = a.DisplayName,
                    description = a.Description,
                    version = plugin?.Version,
                    stability = plugin?.Stability.ToString(),
                    capabilities = a.Capabilities.ToString(),
                    credentialsSchema = a.GetCredentialsSchema()
                };
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
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var result = await _db.marketplaceConnections
            .Where(c => c.storeId == selectedStoreId.Value)
            .OrderByDescending(c => c.createdAt)
            .Select(c => new MarketplaceConnection.ConnectionDTO
            {
                id = c.Id,
                name = c.name,
                adapterKey = c.adapterKey,
                isActive = c.isActive,
                lastSyncAt = c.lastSyncAt,
                categoryMappingsCount = c.categoryMappings.Count(),
                productLinksCount = c.productLinks.Count(),
            })
            .ToListAsync();

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
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var connection = await _db.marketplaceConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == connectionId && c.storeId == selectedStoreId.Value);

        if (connection == null)
            throw ApiException.NotFound("Connection not found", code: "connection_not_found");

        var hasCredentials = !string.IsNullOrEmpty(connection.credentialsJson);

        return Ok(new MarketplaceConnection.ConnectionDetailDTO
        {
            id = connection.Id,
            name = connection.name,
            adapterKey = connection.adapterKey,
            // Never return decrypted credentials — client must re-enter to rotate
            credentialsJson = null,
            hasCredentials = hasCredentials,
            settingsJson = connection.settingsJson,
            isActive = connection.isActive,
            lastSyncAt = connection.lastSyncAt,
            createdAt = connection.createdAt,
        });
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
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        await _quotaService.EnsureConnectionQuotaAsync(selectedStoreId.Value);

        // Verify that the adapter exists
        var adapter = _adapterRegistry.GetByKey(input.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest($"Unknown adapter: {input.adapterKey}");

        var repo = _unitOfWork.Repository<MarketplaceConnection>();

        var encryptedCreds = !string.IsNullOrEmpty(input.credentialsJson)
            ? (_encryptionService?.Encrypt(input.credentialsJson)
               ?? throw ApiException.InternalServerError("Encryption is not configured"))
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
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var repo = _unitOfWork.Repository<MarketplaceConnection>();

        var connection = await repo.FindAsync(
            c => c.Id == connectionId && c.storeId == selectedStoreId.Value);

        if (connection == null)
            throw ApiException.NotFound("Connection not found", code: "connection_not_found");

        // Empty/null credentialsJson means "keep existing" — do not overwrite with blanks
        if (string.IsNullOrWhiteSpace(input.credentialsJson) || IsBlankCredentialsObject(input.credentialsJson))
        {
            input.credentialsJson = null;
        }
        else if (_encryptionService != null)
        {
            input.credentialsJson = _encryptionService.Encrypt(input.credentialsJson);
        }
        else
        {
            throw ApiException.InternalServerError("Encryption is not configured");
        }

        connection.UpdateFill(input);
        await repo.UpdateAsync(connection);

        return Ok(new { success = true });
    }

    private static bool IsBlankCredentialsObject(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                return false;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(prop.Value.GetString()))
                    return false;
                if (prop.Value.ValueKind is not (System.Text.Json.JsonValueKind.String
                    or System.Text.Json.JsonValueKind.Null))
                    return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
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
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var repo = _unitOfWork.Repository<MarketplaceConnection>();

        var connection = await repo.FindAsync(
            c => c.Id == connectionId && c.storeId == selectedStoreId.Value,
            query => query
                .Include(c => c.categoryMappings)
                .Include(c => c.productLinks));

        if (connection == null)
            throw ApiException.NotFound("Connection not found", code: "connection_not_found");

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
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var repo = _unitOfWork.Repository<MarketplaceConnection>();

        var connection = await repo.FindAsync(
            c => c.Id == connectionId && c.storeId == selectedStoreId.Value);

        if (connection == null)
            throw ApiException.NotFound("Connection not found", code: "connection_not_found");

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest($"Unknown adapter: {connection.adapterKey}");

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
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var repo = _unitOfWork.Repository<MarketplaceConnection>();

        var connection = await repo.FindAsync(
            c => c.Id == connectionId && c.storeId == selectedStoreId.Value);

        if (connection == null)
            throw ApiException.NotFound("Connection not found", code: "connection_not_found");

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest($"Unknown adapter: {connection.adapterKey}");

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
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var repo = _unitOfWork.Repository<MarketplaceConnection>();

        var connection = await repo.FindAsync(
            c => c.Id == connectionId && c.storeId == selectedStoreId.Value);

        if (connection == null)
            throw ApiException.NotFound("Connection not found", code: "connection_not_found");

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest($"Unknown adapter: {connection.adapterKey}");

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
