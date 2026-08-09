using System.Globalization;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Products;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services;

/// <summary>
/// Enforces and reports per-store quotas from environment variables only (Control Plane / tariff).
/// Empty or invalid values mean no limit.
/// </summary>
public class QuotaService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly DataContext _db;

    public QuotaService(IUnitOfWork unitOfWork, DataContext db)
    {
        _unitOfWork = unitOfWork;
        _db = db;
    }

    public async Task EnsureProductQuotaAsync(int storeId)
    {
        var maxProducts = ReadPositiveInt("MAX_PRODUCTS");
        if (maxProducts == null) return;

        var count = await _unitOfWork.Repository<Product>()
            .CountAsync(p => p.storeId == storeId);

        if (count >= maxProducts.Value)
        {
            throw ApiException.Conflict(
                $"Product limit reached (MAX_PRODUCTS={maxProducts.Value})",
                data: new { limit = maxProducts.Value, used = count, remaining = 0 },
                code: "product_limit_reached");
        }
    }

    public async Task EnsureStorageQuotaAsync(int storeId, long additionalBytes = 0)
    {
        var maxStorageBytes = ReadPositiveGigabytesAsBytes("MAX_STORAGE_GB");
        if (maxStorageBytes == null) return;

        var used = await GetStoreStorageBytesAsync(storeId);
        if (used + additionalBytes > maxStorageBytes.Value)
        {
            var remaining = Math.Max(0, maxStorageBytes.Value - used);
            throw ApiException.Conflict(
                "Storage limit reached (MAX_STORAGE_GB)",
                data: new
                {
                    limit = FormatBytes(maxStorageBytes.Value),
                    used = FormatBytes(used),
                    remaining = FormatBytes(remaining),
                    needed = FormatBytes(additionalBytes),
                    limitBytes = maxStorageBytes.Value,
                    usedBytes = used,
                    remainingBytes = remaining,
                    neededBytes = additionalBytes,
                },
                code: "quota_storage_reached");
        }
    }

    public async Task EnsureFileQuotaAsync(int storeId, int additionalFiles = 1)
    {
        var maxFiles = ReadPositiveInt("MAX_FILES");
        if (maxFiles == null) return;

        var used = await GetStoreFileCountAsync(storeId);
        if (used + additionalFiles > maxFiles.Value)
        {
            var remaining = Math.Max(0, maxFiles.Value - used);
            throw ApiException.Conflict(
                $"File limit reached (MAX_FILES={maxFiles.Value})",
                data: new
                {
                    limit = maxFiles.Value,
                    used,
                    remaining,
                    needed = additionalFiles,
                },
                code: "quota_files_reached");
        }
    }

    public async Task EnsureConnectionQuotaAsync(int storeId)
    {
        var maxConnections = ReadPositiveInt("MAX_CONNECTIONS");
        if (maxConnections == null) return;

        var count = await _unitOfWork.Repository<MarketplaceConnection>()
            .CountAsync(c => c.storeId == storeId);

        if (count >= maxConnections.Value)
        {
            throw ApiException.Conflict(
                $"Connection limit reached (MAX_CONNECTIONS={maxConnections.Value})",
                data: new { limit = maxConnections.Value, used = count, remaining = 0 },
                code: "quota_connections_reached");
        }
    }

    /// <summary>
    /// Quotas for cloning/moving a product into <paramref name="targetStoreId"/>
    /// (new product row + copied files). Same checks as create + upload.
    /// </summary>
    public async Task EnsureTransferQuotasAsync(
        int targetStoreId,
        int additionalFiles,
        long additionalBytes)
    {
        await EnsureProductQuotaAsync(targetStoreId);
        if (additionalFiles > 0)
            await EnsureFileQuotaAsync(targetStoreId, additionalFiles);
        if (additionalBytes > 0)
            await EnsureStorageQuotaAsync(targetStoreId, additionalBytes);
    }

    /// <summary>Selected-store file/storage usage for upload preflight (FilesManage).</summary>
    public async Task<StoreFileQuotaDto> GetStoreFileQuotaAsync(int storeId, CancellationToken ct = default)
    {
        var limits = GetLimits();
        var filesUsed = await GetStoreFileCountAsync(storeId, ct);
        var storageUsed = await GetStoreStorageBytesAsync(storeId, ct);
        return new StoreFileQuotaDto(
            Files: Metric(filesUsed, limits.MaxFiles),
            StorageBytes: Metric(storageUsed, limits.MaxStorageBytes));
    }

    private async Task<long> GetStoreStorageBytesAsync(int storeId, CancellationToken ct = default)
    {
        var productFilesBytes = await _db.files
            .AsNoTracking()
            .Where(f => f.product.storeId == storeId)
            .SumAsync(f => (long?)f.length, ct) ?? 0L;

        var brandingPrefix = $"branding/{storeId}/";
        var storeFilesBytes = await _db.storeFiles
            .AsNoTracking()
            .Where(f => f.storeId == storeId && !f.filePath.StartsWith(brandingPrefix))
            .SumAsync(f => (long?)f.length, ct) ?? 0L;

        return productFilesBytes + storeFilesBytes;
    }

    private async Task<int> GetStoreFileCountAsync(int storeId, CancellationToken ct = default)
    {
        var productFilesCount = await _db.files
            .AsNoTracking()
            .CountAsync(f => f.product.storeId == storeId, ct);

        var brandingPrefix = $"branding/{storeId}/";
        var storeFilesCount = await _db.storeFiles
            .AsNoTracking()
            .CountAsync(f => f.storeId == storeId && !f.filePath.StartsWith(brandingPrefix), ct);

        return productFilesCount + storeFilesCount;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double value = bytes;
        string[] units = ["KB", "MB", "GB", "TB"];
        var unit = 0;
        value /= 1024;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    public QuotaLimitsDto GetLimits()
    {
        var maxGb = ReadPositiveDecimal("MAX_STORAGE_GB");
        return new QuotaLimitsDto(
            MaxProducts: ReadPositiveInt("MAX_PRODUCTS"),
            MaxStorageBytes: ReadPositiveGigabytesAsBytes("MAX_STORAGE_GB"),
            MaxStorageGb: maxGb,
            MaxFiles: ReadPositiveInt("MAX_FILES"),
            MaxConnections: ReadPositiveInt("MAX_CONNECTIONS"));
    }

    public async Task<IReadOnlyList<StoreQuotaUsageDto>> GetStoreUsagesAsync(CancellationToken ct = default)
    {
        var limits = GetLimits();
        var stores = await _db.stores
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .Select(s => new { s.Id, s.name })
            .ToListAsync(ct);

        var result = new List<StoreQuotaUsageDto>(stores.Count);
        foreach (var store in stores)
        {
            result.Add(await BuildStoreUsageAsync(store.Id, store.name ?? $"Store {store.Id}", limits, ct));
        }

        return result;
    }

    public async Task<QuotaUsageSnapshotDto> GetUsageSnapshotAsync(
        bool platformLinked,
        string? planName,
        DateTimeOffset? overagePurgeAt,
        CancellationToken ct = default)
    {
        var limits = GetLimits();
        var stores = await GetStoreUsagesAsync(ct);
        var products = Aggregate(stores.Select(s => s.Products), limits.MaxProducts);
        var files = Aggregate(stores.Select(s => s.Files), limits.MaxFiles);
        var storage = Aggregate(stores.Select(s => s.StorageBytes), limits.MaxStorageBytes);
        var connections = Aggregate(stores.Select(s => s.Connections), limits.MaxConnections);
        var isOver = stores.Any(s => s.IsOverQuota);
        var overageActive = platformLinked && overagePurgeAt != null && isOver;

        return new QuotaUsageSnapshotDto(
            Limits: limits,
            Stores: stores,
            Products: products,
            Files: files,
            StorageBytes: storage,
            Connections: connections,
            IsOverQuota: isOver,
            PlatformLinked: platformLinked,
            PlanName: planName,
            OveragePurgeAt: overagePurgeAt,
            OverageActive: overageActive);
    }

    /// <summary>Heartbeat / CP payload: per-store and totals.</summary>
    public async Task<PlatformLink.PlatformUsageReportDto> BuildUsageReportAsync(CancellationToken ct = default)
    {
        var stores = await GetStoreUsagesAsync(ct);
        return new PlatformLink.PlatformUsageReportDto
        {
            Stores = stores.Select(s => new PlatformLink.PlatformStoreUsageDto
            {
                StoreId = s.StoreId,
                Products = s.Products.Used,
                Files = s.Files.Used,
                StorageBytes = s.StorageBytes.Used,
                Connections = s.Connections.Used,
            }).ToList(),
            Products = stores.Sum(s => s.Products.Used),
            Files = stores.Sum(s => s.Files.Used),
            StorageBytes = stores.Sum(s => s.StorageBytes.Used),
            Connections = stores.Sum(s => s.Connections.Used),
            IsOverQuota = stores.Any(s => s.IsOverQuota),
        };
    }

    private async Task<StoreQuotaUsageDto> BuildStoreUsageAsync(
        int storeId,
        string storeName,
        QuotaLimitsDto limits,
        CancellationToken ct)
    {
        // Active products only (matches EnsureProductQuotaAsync / query filter).
        var productsUsed = await _db.products.CountAsync(p => p.storeId == storeId, ct);

        var productFiles = await _db.files
            .AsNoTracking()
            .Where(f => f.product.storeId == storeId)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Bytes = g.Sum(f => f.length) })
            .FirstOrDefaultAsync(ct);

        var storeFiles = await _db.storeFiles
            .AsNoTracking()
            .Where(f => f.storeId == storeId && !f.filePath.StartsWith($"branding/{storeId}/"))
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Bytes = g.Sum(f => f.length) })
            .FirstOrDefaultAsync(ct);

        var filesUsed = (productFiles?.Count ?? 0) + (storeFiles?.Count ?? 0);
        var storageUsed = (productFiles?.Bytes ?? 0L) + (storeFiles?.Bytes ?? 0L);
        var connectionsUsed = await _db.marketplaceConnections.CountAsync(c => c.storeId == storeId, ct);

        var products = Metric(productsUsed, limits.MaxProducts);
        var files = Metric(filesUsed, limits.MaxFiles);
        var storage = Metric(storageUsed, limits.MaxStorageBytes);
        var connections = Metric(connectionsUsed, limits.MaxConnections);

        return new StoreQuotaUsageDto(
            StoreId: storeId,
            StoreName: storeName,
            Products: products,
            Files: files,
            StorageBytes: storage,
            Connections: connections,
            IsOverQuota: products.IsOver || files.IsOver || storage.IsOver || connections.IsOver);
    }

    private static QuotaMetricDto Metric(long used, long? limit)
    {
        if (limit is null)
            return new QuotaMetricDto(used, null, null, false);
        var remaining = Math.Max(0, limit.Value - used);
        return new QuotaMetricDto(used, limit, remaining, used > limit.Value);
    }

    private static QuotaMetricDto Metric(long used, int? limit) =>
        Metric(used, limit.HasValue ? (long?)limit.Value : null);

    private static QuotaMetricDto Aggregate(IEnumerable<QuotaMetricDto> parts, long? limit)
    {
        var used = parts.Sum(p => p.Used);
        return Metric(used, limit);
    }

    private static QuotaMetricDto Aggregate(IEnumerable<QuotaMetricDto> parts, int? limit) =>
        Aggregate(parts, limit.HasValue ? (long?)limit.Value : null);

    internal static int? ReadPositiveInt(string envName)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0
            ? n
            : null;
    }

    internal static decimal? ReadPositiveDecimal(string envName)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!decimal.TryParse(raw.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var n) || n <= 0)
            return null;
        return n;
    }

    internal static long? ReadPositiveGigabytesAsBytes(string envName)
    {
        var gb = ReadPositiveDecimal(envName);
        if (gb == null) return null;
        return (long)(gb.Value * 1024m * 1024m * 1024m);
    }
}
