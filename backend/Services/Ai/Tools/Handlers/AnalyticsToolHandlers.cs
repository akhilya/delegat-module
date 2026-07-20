using Microsoft.EntityFrameworkCore;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Enums;
using Delegat.Services.Marketplace.Graph;
using Delegat.Services.Ai.Tools;

namespace Delegat.Services.Ai.Tools.Handlers;

public class AnalyticsToolHandlers : ToolHandlerBase
{
    private readonly DataContext _context;
    private readonly GraphMutationService _graphMutation;
    private readonly ILogger<AnalyticsToolHandlers> _logger;

    public AnalyticsToolHandlers(
        DataContext context,
        GraphMutationService graphMutation,
        ILogger<AnalyticsToolHandlers> logger)
    {
        _context = context;
        _graphMutation = graphMutation;
        _logger = logger;
    }

    internal async Task<(object, string)> ListStoreAttributesAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var attrs = await _context.storeAttributes.AsNoTracking()
            .Where(a => a.storeId == storeId).OrderBy(a => a.sortOrder)
            .Select(a => new { a.Id, a.name, a.key, a.type }).ToListAsync(ct);
        return (new { attributes = attrs, count = attrs.Count }, $"{attrs.Count} store attributes");
    }

    internal async Task<(object, string)> GetStoreStatsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var productCount = await _context.products.AsNoTracking().CountAsync(p => p.storeId == storeId, ct);
        var categoryCount = await _context.categories.AsNoTracking().CountAsync(c => c.storeId == storeId, ct);
        var connectionCount = await _context.marketplaceConnections.AsNoTracking().CountAsync(c => c.storeId == storeId && c.isActive, ct);
        var mappingCount = await _context.categoryMappings.AsNoTracking().CountAsync(m => m.connection.storeId == storeId, ct);
        var pendingExports = await _context.pendingProductExports.AsNoTracking().CountAsync(e => e.storeId == storeId && e.status == ProductExportStatus.Pending, ct);
        var groupCount = await _context.productGroups.AsNoTracking().CountAsync(g => g.storeId == storeId, ct);

        return (new
        {
            products = productCount,
            categories = categoryCount,
            connections = connectionCount,
            mappings = mappingCount,
            pendingExports,
            groups = groupCount
        }, $"Products: {productCount}, categories: {categoryCount}, connections: {connectionCount}");
    }

    internal async Task<(object, string)> FindProductsWithoutImagesAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var products = await _context.products.AsNoTracking()
            .Where(p => p.storeId == storeId && !_context.images.Any(i => i.productId == p.Id))
            .Select(p => new { p.Id, p.name, p.sku })
            .Take(100).ToListAsync(ct);
        return (new { products, count = products.Count }, $"{products.Count} products without images");
    }

    internal async Task<(object, string)> FindProductsWithoutBarcodeAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var products = await _context.products.AsNoTracking()
            .Where(p => p.storeId == storeId && (p.barcode == null || p.barcode == ""))
            .Select(p => new { p.Id, p.name, p.sku })
            .Take(100).ToListAsync(ct);
        return (new { products, count = products.Count }, $"{products.Count} products without barcode");
    }

    internal async Task<(object, string)> FindProductsMissingFieldAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<SearchInput>(json);
        var attrId = await _context.categoryAttributes.AsNoTracking()
            .Where(a => EF.Functions.Like(a.name, $"%{p.Search}%") && a.category.storeId == storeId)
            .Select(a => (int?)a.Id).FirstOrDefaultAsync(ct);

        if (attrId == null)
            return (new { error = $"Field '{p.Search}' not found" }, "Field not found");

        var products = await _context.products.AsNoTracking()
            .Where(pr => pr.storeId == storeId &&
                !_context.productAttributeValues.Any(v => v.productId == pr.Id && v.attributeId == attrId && v.value != null && v.value != ""))
            .Select(pr => new { pr.Id, pr.name, pr.sku })
            .Take(100).ToListAsync(ct);
        return (new { products, count = products.Count }, $"{products.Count} products without field '{p.Search}'");
    }

    internal async Task<(object, string)> FindProductsWithSyncErrorsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ConnectionIdInput>(json);
        var q = _context.productMarketplaceLinks.AsNoTracking()
            .Where(l => l.connection.storeId == storeId && l.status == ProductMarketplaceStatus.SyncError);
        if (p.ConnectionId.HasValue) q = q.Where(l => l.connectionId == p.ConnectionId);
        var links = await q.Take(50)
            .Select(l => new { l.productId, l.externalProductId, l.connectionId, l.lastErrorMessage })
            .ToListAsync(ct);
        return (new { products = links, count = links.Count }, $"{links.Count} products with sync errors");
    }

    internal async Task<(object, string)> AnalyzeMappingQualityAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ConnectionIdInput>(json);
        var q = _context.categoryMappings.AsNoTracking().Where(m => m.connection.storeId == storeId);
        if (p.ConnectionId.HasValue) q = q.Where(m => m.connectionId == p.ConnectionId);

        var mappings = await q.ToListAsync(ct);
        var report = new List<object>();
        foreach (var m in mappings)
        {
            var coverage = await _graphMutation.GetCoverageAsync(m.Id, ct);
            report.Add(new
            {
                id = m.Id,
                name = m.name ?? m.externalCategoryName,
                coverage.Total, coverage.Connected, coverage.RequiredTotal, coverage.RequiredConnected,
                unmappedRequired = coverage.UnmappedRequiredFields.Select(f => f.Label).ToList()
            });
        }
        var totalFields = report.Sum(r => (int)((dynamic)r).Total);
        var connectedFields = report.Sum(r => (int)((dynamic)r).Connected);
        return (new { mappings = report, totalFields, connectedFields },
            $"Analyzed {mappings.Count} mappings: {connectedFields}/{totalFields} fields connected");
    }

    internal async Task<(object, string)> GetImageDownloadStatusAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var groups = await _context.pendingImageDownloads.AsNoTracking()
            .Where(d => d.storeId == storeId)
            .GroupBy(d => d.status)
            .Select(g => new { status = g.Key, count = g.Count() })
            .ToListAsync(ct);
        return (new { status = groups }, "Image download status");
    }
}
