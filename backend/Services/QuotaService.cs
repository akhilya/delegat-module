using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Products;

namespace Delegat.Services;

public class QuotaService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly AppSettingsService _settingsService;

    public QuotaService(IUnitOfWork unitOfWork, AppSettingsService settingsService)
    {
        _unitOfWork = unitOfWork;
        _settingsService = settingsService;
    }

    public async Task EnsureProductQuotaAsync(int storeId)
    {
        var maxProducts = await _settingsService.GetPositiveIntAsync(AppSettingKeys.MaxProducts);
        if (maxProducts == null) return;

        var count = await _unitOfWork.Repository<Product>()
            .CountAsync(p => p.storeId == storeId);

        if (count >= maxProducts.Value)
            throw ApiException.Conflict($"Product limit reached (MAX_PRODUCTS={maxProducts.Value})");
    }

    public async Task EnsureStorageQuotaAsync(int storeId, long additionalBytes = 0)
    {
        var maxStorageBytes = await _settingsService.GetPositiveGigabytesAsBytesAsync(AppSettingKeys.MaxStorageGb);
        if (maxStorageBytes == null) return;

        var productFilesBytes = await _unitOfWork.Repository<Models.File>()
            .Query()
            .Where(f => f.product.storeId == storeId)
            .SumAsync(f => f.length);

        var storeFilesBytes = await _unitOfWork.Repository<StoreFile>()
            .Query()
            .Where(f => f.storeId == storeId)
            .SumAsync(f => f.length);

        if (productFilesBytes + storeFilesBytes + additionalBytes > maxStorageBytes.Value)
            throw ApiException.Conflict("Storage limit reached (MAX_STORAGE_GB)");
    }

    public async Task EnsureFileQuotaAsync(int storeId, int additionalFiles = 1)
    {
        var maxFiles = await _settingsService.GetPositiveIntAsync(AppSettingKeys.MaxFiles);
        if (maxFiles == null) return;

        var productFilesCount = await _unitOfWork.Repository<Models.File>()
            .Query()
            .CountAsync(f => f.product.storeId == storeId);

        var storeFilesCount = await _unitOfWork.Repository<StoreFile>()
            .Query()
            .CountAsync(f => f.storeId == storeId);

        if (productFilesCount + storeFilesCount + additionalFiles > maxFiles.Value)
            throw ApiException.Conflict($"File limit reached (MAX_FILES={maxFiles.Value})");
    }

    public async Task EnsureConnectionQuotaAsync(int storeId)
    {
        var maxConnections = await _settingsService.GetPositiveIntAsync(AppSettingKeys.MaxConnections);
        if (maxConnections == null) return;

        var count = await _unitOfWork.Repository<Models.Marketplace.MarketplaceConnection>()
            .CountAsync(c => c.storeId == storeId);

        if (count >= maxConnections.Value)
            throw ApiException.Conflict($"Connection limit reached (MAX_CONNECTIONS={maxConnections.Value})");
    }
}
