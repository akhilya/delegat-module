using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Products;
using System.Globalization;

namespace Delegat.Services;

/// <summary>
/// Enforces per-store quotas from environment variables only (Control Plane / tariff).
/// Empty or invalid values mean no limit.
/// </summary>
public class QuotaService
{
    private readonly IUnitOfWork _unitOfWork;

    public QuotaService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task EnsureProductQuotaAsync(int storeId)
    {
        var maxProducts = ReadPositiveInt("MAX_PRODUCTS");
        if (maxProducts == null) return;

        var count = await _unitOfWork.Repository<Product>()
            .CountAsync(p => p.storeId == storeId);

        if (count >= maxProducts.Value)
            throw ApiException.Conflict($"Product limit reached (MAX_PRODUCTS={maxProducts.Value})");
    }

    public async Task EnsureStorageQuotaAsync(int storeId, long additionalBytes = 0)
    {
        var maxStorageBytes = ReadPositiveGigabytesAsBytes("MAX_STORAGE_GB");
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
        var maxFiles = ReadPositiveInt("MAX_FILES");
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
        var maxConnections = ReadPositiveInt("MAX_CONNECTIONS");
        if (maxConnections == null) return;

        var count = await _unitOfWork.Repository<Models.Marketplace.MarketplaceConnection>()
            .CountAsync(c => c.storeId == storeId);

        if (count >= maxConnections.Value)
            throw ApiException.Conflict($"Connection limit reached (MAX_CONNECTIONS={maxConnections.Value})");
    }

    private static int? ReadPositiveInt(string envName)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0
            ? n
            : null;
    }

    private static long? ReadPositiveGigabytesAsBytes(string envName)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!decimal.TryParse(raw.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var gb) || gb <= 0)
            return null;
        return (long)(gb * 1024m * 1024m * 1024m);
    }
}
