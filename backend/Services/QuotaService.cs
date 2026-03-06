using Microsoft.EntityFrameworkCore;
using PrintO.Infrastructure.Data;
using PrintO.Infrastructure.Middleware;
using PrintO.Models;
using PrintO.Models.Products;

namespace PrintO.Services;

public class QuotaService
{
    private readonly IUnitOfWork _unitOfWork;

    private static readonly int? MaxProducts = ParseEnvInt("MAX_PRODUCTS");
    private static readonly long? MaxStorageBytes = ParseEnvGb("MAX_STORAGE_GB");
    private static readonly int? MaxFiles = ParseEnvInt("MAX_FILES");
    private static readonly int? MaxConnections = ParseEnvInt("MAX_CONNECTIONS");

    public QuotaService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task EnsureProductQuotaAsync(int storeId)
    {
        if (MaxProducts == null) return;

        var count = await _unitOfWork.Repository<Product>()
            .CountAsync(p => p.storeId == storeId);

        if (count >= MaxProducts.Value)
            throw ApiException.Conflict($"Достигнут лимит товаров (MAX_PRODUCTS={MaxProducts.Value})");
    }

    public async Task EnsureStorageQuotaAsync(int storeId, long additionalBytes = 0)
    {
        if (MaxStorageBytes == null) return;

        var usedBytes = await _unitOfWork.Repository<Models.File>()
            .Query()
            .Where(f => f.product.storeId == storeId)
            .SumAsync(f => f.length);

        if (usedBytes + additionalBytes > MaxStorageBytes.Value)
            throw ApiException.Conflict($"Достигнут лимит хранилища (MAX_STORAGE_GB={Environment.GetEnvironmentVariable("MAX_STORAGE_GB")})");
    }

    public async Task EnsureFileQuotaAsync(int storeId, int additionalFiles = 1)
    {
        if (MaxFiles == null) return;

        var count = await _unitOfWork.Repository<Models.File>()
            .Query()
            .CountAsync(f => f.product.storeId == storeId);

        if (count + additionalFiles > MaxFiles.Value)
            throw ApiException.Conflict($"Достигнут лимит файлов (MAX_FILES={MaxFiles.Value})");
    }

    public async Task EnsureConnectionQuotaAsync(int storeId)
    {
        if (MaxConnections == null) return;

        var count = await _unitOfWork.Repository<Models.Marketplace.MarketplaceConnection>()
            .CountAsync(c => c.storeId == storeId);

        if (count >= MaxConnections.Value)
            throw ApiException.Conflict($"Достигнут лимит подключений (MAX_CONNECTIONS={MaxConnections.Value})");
    }

    private static int? ParseEnvInt(string key) =>
        int.TryParse(Environment.GetEnvironmentVariable(key), out var n) && n > 0 ? n : null;

    private static long? ParseEnvGb(string key) =>
        double.TryParse(Environment.GetEnvironmentVariable(key), out var gb) && gb > 0
            ? (long)(gb * 1024 * 1024 * 1024) : null;
}
