using Delegat.Marketplace.SDK.RateLimit;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services.Marketplace;

namespace Delegat.Tests;

public class ExternalCategoryEligibilityTests
{
    [Fact]
    public async Task EnsureMappable_RejectsParentWithHasChildren()
    {
        var adapter = FakeCategoryAdapter.Create(
            root:
            [
                new ExternalCategory("200001710", "Figurines", HasChildren: true),
            ],
            children: new Dictionary<string, List<ExternalCategory>>(StringComparer.Ordinal)
            {
                ["200001710"] =
                [
                    new ExternalCategory("200001710:115944384", "Character figurines", HasChildren: false),
                ],
            },
            fieldsByCategoryId: new Dictionary<string, List<ExternalField>>(StringComparer.Ordinal)
            {
                ["200001710"] = [],
                ["200001710:115944384"] =
                [
                    new ExternalField("sku", "SKU", true, "string"),
                    new ExternalField("brand", "Brand", false, "string"),
                ],
            });

        var result = await ExternalCategoryEligibility.EnsureMappableAsync(
            adapter, new MarketplaceConnection(), "200001710");

        Assert.False(result.IsSuccess);
        Assert.Contains("leaf", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnsureMappable_RejectsWhenFieldsEmptyEvenIfUnresolved()
    {
        // Parent id that is not found in the tree, but GetCategoryFields returns empty
        // (Ozon-style silent empty for non leaf:type ids).
        var adapter = FakeCategoryAdapter.Create(
            root: [],
            children: new Dictionary<string, List<ExternalCategory>>(StringComparer.Ordinal),
            fieldsByCategoryId: new Dictionary<string, List<ExternalField>>(StringComparer.Ordinal)
            {
                ["200001710"] = [],
            });

        var result = await ExternalCategoryEligibility.EnsureMappableAsync(
            adapter, new MarketplaceConnection(), "200001710");

        Assert.False(result.IsSuccess);
        Assert.Contains("no fields", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnsureMappable_AcceptsLeafWithFields()
    {
        var leafId = "200001710:115944384";
        var adapter = FakeCategoryAdapter.Create(
            root:
            [
                new ExternalCategory("200001710", "Figurines", HasChildren: true),
            ],
            children: new Dictionary<string, List<ExternalCategory>>(StringComparer.Ordinal)
            {
                ["200001710"] =
                [
                    new ExternalCategory(leafId, "Character figurines", HasChildren: false),
                ],
            },
            fieldsByCategoryId: new Dictionary<string, List<ExternalField>>(StringComparer.Ordinal)
            {
                [leafId] =
                [
                    new ExternalField("sku", "SKU", true, "string"),
                    new ExternalField("_hasSizes", "Has sizes", false, "boolean"),
                    new ExternalField("brand", "Brand", false, "string"),
                ],
            });

        var result = await ExternalCategoryEligibility.EnsureMappableAsync(
            adapter, new MarketplaceConnection(), leafId);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Category);
        Assert.False(result.Category!.HasChildren);
        Assert.Equal(2, result.Fields.Count); // _hasSizes stripped
        Assert.DoesNotContain(result.Fields, f => f.Key == "_hasSizes");
    }

    [Fact]
    public async Task ResolveCategory_FindsNestedLeafById()
    {
        var leafId = "desc:type";
        var adapter = FakeCategoryAdapter.Create(
            root: [new ExternalCategory("root", "Root", HasChildren: true)],
            children: new Dictionary<string, List<ExternalCategory>>(StringComparer.Ordinal)
            {
                ["root"] = [new ExternalCategory(leafId, "Leaf", HasChildren: false)],
            },
            fieldsByCategoryId: new Dictionary<string, List<ExternalField>>(StringComparer.Ordinal));

        var found = await ExternalCategoryEligibility.ResolveCategoryAsync(
            adapter, new MarketplaceConnection(), leafId);

        Assert.NotNull(found);
        Assert.Equal(leafId, found!.Id);
        Assert.False(found.HasChildren);
    }

    private sealed class FakeCategoryAdapter : IMarketplaceAdapter
    {
        private const string RootKey = "__root__";
        private readonly Dictionary<string, List<ExternalCategory>> _categoriesByParent;
        private readonly Dictionary<string, List<ExternalField>> _fieldsByCategoryId;

        private FakeCategoryAdapter(
            Dictionary<string, List<ExternalCategory>> categoriesByParent,
            Dictionary<string, List<ExternalField>> fieldsByCategoryId)
        {
            _categoriesByParent = categoriesByParent;
            _fieldsByCategoryId = fieldsByCategoryId;
        }

        public static FakeCategoryAdapter Create(
            List<ExternalCategory> root,
            Dictionary<string, List<ExternalCategory>> children,
            Dictionary<string, List<ExternalField>> fieldsByCategoryId)
        {
            var map = new Dictionary<string, List<ExternalCategory>>(children, StringComparer.Ordinal)
            {
                [RootKey] = root,
            };
            return new FakeCategoryAdapter(map, fieldsByCategoryId);
        }

        public string Key => "fake";
        public string DisplayName => "Fake";
        public string? Description => null;
        public string? LogoUrl => null;
        public MarketplaceCapabilities Capabilities =>
            MarketplaceCapabilities.GetCategories | MarketplaceCapabilities.GetCategoryFields;
        public string? ProductEditUrlTemplate => null;

        public CredentialsSchema GetCredentialsSchema() => new();
        public RateLimitConfig? GetRateLimitConfig() => null;
        public Task<List<QuotaInfo>> GetQuotasAsync(MarketplaceConnection connection, CancellationToken ct = default)
            => Task.FromResult(new List<QuotaInfo>());
        public MarketplaceRateLimiter? GetRateLimiterForConnection(MarketplaceConnection connection) => null;
        public int ClearMetadataCaches(MarketplaceConnection connection) => 0;
        public Task<ConnectionTestResult> TestConnectionAsync(MarketplaceConnection connection, CancellationToken ct = default)
            => Task.FromResult(new ConnectionTestResult(true));

        public Task<List<ExternalCategory>> GetCategoriesAsync(
            MarketplaceConnection connection,
            string? parentId = null,
            string? searchQuery = null,
            CancellationToken ct = default)
        {
            var key = parentId ?? RootKey;
            if (_categoriesByParent.TryGetValue(key, out var cats))
                return Task.FromResult(cats);
            return Task.FromResult(new List<ExternalCategory>());
        }

        public Task<List<ExternalField>> GetCategoryFieldsAsync(
            MarketplaceConnection connection,
            string externalCategoryId,
            CancellationToken ct = default)
        {
            if (_fieldsByCategoryId.TryGetValue(externalCategoryId, out var fields))
                return Task.FromResult(fields);
            return Task.FromResult(new List<ExternalField>());
        }

        public Task<List<ExternalField>> GetPriceFieldsAsync(MarketplaceConnection connection, string externalCategoryId, CancellationToken ct = default)
            => Task.FromResult(new List<ExternalField>());
        public Task<CreateCategoryResult> CreateCategoryAsync(MarketplaceConnection connection, CreateCategoryRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ExternalProduct?> ImportProductAsync(MarketplaceConnection connection, string externalProductId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<List<ExternalProduct>> ImportProductsBatchAsync(MarketplaceConnection connection, List<string> externalProductIds, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<PagedResult<ExternalProduct>> ImportAllProductsAsync(MarketplaceConnection connection, int pageSize = 100, string? cursor = null, string? offerIdFilter = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ExportResult> ExportProductAsync(MarketplaceConnection connection, Dictionary<string, object> productData, string? existingExternalId = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<BulkExportResult> ExportProductsBulkAsync(MarketplaceConnection connection, List<BulkExportItem> items, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ExportResult> UpdateSkuAsync(MarketplaceConnection connection, string externalProductId, string newSku, string? currentSku = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ExportResult> ArchiveProductAsync(MarketplaceConnection connection, string externalProductId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<List<PriceInfo>> ImportPricesAsync(MarketplaceConnection connection, List<string>? externalProductIds = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ImportResult> ExportPricesAsync(MarketplaceConnection connection, List<PriceInfo> prices, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<List<StockInfo>> ImportStocksAsync(MarketplaceConnection connection, List<string>? externalProductIds = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ImportResult> ExportStocksAsync(MarketplaceConnection connection, List<StockInfo> stocks, CancellationToken ct = default)
            => throw new NotImplementedException();
        public FieldBasedGroupingConfig? GetFieldBasedGroupingConfig() => null;
        public Task<CreateGroupResult> CreateGroupAsync(MarketplaceConnection connection, List<string> productExternalIds, string? groupName = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<GroupMembershipResult> AddProductsToGroupAsync(MarketplaceConnection connection, string externalGroupId, List<string> productExternalIds, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<GroupMembershipResult> RemoveProductsFromGroupAsync(MarketplaceConnection connection, string externalGroupId, List<string> productExternalIds, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ExportResult> DeleteGroupAsync(MarketplaceConnection connection, string externalGroupId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<PagedResult<ExternalGroup>> ImportGroupsAsync(MarketplaceConnection connection, int pageSize = 100, string? cursor = null, CancellationToken ct = default)
            => throw new NotImplementedException();
    }
}
