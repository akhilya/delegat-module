using System.Globalization;
using Delegat.Enums;
using Delegat.Marketplace.SDK.RateLimit;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services;
using Delegat.Services.Marketplace;
using Delegat.Services.Marketplace.Graph;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class ProductSyncImportAttributesTests
{
    [Fact]
    public async Task ImportAttributes_StoresNumbersWithInvariantDot()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
        try
        {
            var (svc, db, connection, product, weightAttr) = await SeedMappedProductAsync();

            await svc.ImportProductAttributesAsync(
                product.Id,
                connection.Id,
                product.categoryId!.Value,
                new Dictionary<string, object> { ["weight"] = 1.5d },
                CancellationToken.None);

            var stored = await db.productAttributeValues.SingleAsync();
            Assert.Equal(weightAttr.Id, stored.attributeId);
            Assert.Equal("1.5", stored.value);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task ImportAttributes_MatchesPublicOptionNameToCharcKey()
    {
        var (svc, db, connection, category, _) = await SeedMappedCategoryAsync(includeBrand: false);
        var weight = await db.categoryAttributes.SingleAsync(a => a.key == "weight");
        var color = new CategoryAttribute
        {
            Id = weight.Id + 10,
            categoryId = category.Id,
            name = "Color",
            key = "color",
            type = AttributeType.Text,
        };
        db.categoryAttributes.Add(color);
        var mapping = await db.categoryMappings.SingleAsync();
        mapping.graphJson = GraphGenerator.BuildGraphJson(
        [
            new FieldSpec
            {
                externalFieldKey = "weight",
                externalFieldName = "Weight",
                externalFieldType = "number",
                sourceType = FieldMappingSourceType.CategoryAttribute,
                sourceAttributeId = weight.Id,
            },
            new FieldSpec
            {
                externalFieldKey = "charc:9",
                externalFieldName = "\u0426\u0432\u0435\u0442",
                externalFieldType = "string",
                sourceType = FieldMappingSourceType.CategoryAttribute,
                sourceAttributeId = color.Id,
            },
        ]);
        var product = new TestDataBuilder(db).AddProduct(connection.storeId, "Local", "ART-1", category.Id);
        await db.SaveChangesAsync();

        await svc.ImportProductAttributesAsync(
            product.Id,
            connection.Id,
            category.Id,
            new Dictionary<string, object> { ["\u0426\u0432\u0435\u0442"] = "Grey" },
            CancellationToken.None);

        var stored = await db.productAttributeValues.SingleAsync();
        Assert.Equal(color.Id, stored.attributeId);
        Assert.Equal("Grey", stored.value);
    }

    [Fact]
    public async Task ImportProduct_FillsBrandWhenMarketplaceOmitsAttributeKey()
    {
        var (svc, db, connection, category, brandAttr) = await SeedMappedCategoryAsync();
        var adapter = new ImportAdapter(new ExternalProduct(
            "ext-1",
            "SKU-1",
            "Imported",
            Brand: "Acme"));
        svc = CreateService(db, adapter);

        var result = await svc.ImportProductAsync(
            connection.Id,
            "ext-1",
            targetCategoryId: category.Id);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var stored = await db.productAttributeValues.SingleAsync();
        Assert.Equal(brandAttr.Id, stored.attributeId);
        Assert.Equal("Acme", stored.value);
    }

    [Fact]
    public async Task Preview_DoesNotFlagEquivalentMultiselectEncodings()
    {
        var (svc, db, connection, product, attr) = await SeedMappedSelectProductAsync(
            AttributeType.MultiSelect,
            """[{"value":"d1","label":"другу"},{"value":"d2","label":"любимому"},{"value":"d3","label":"школа"}]""",
            "tags");

        db.productAttributeValues.Add(new ProductAttributeValue
        {
            productId = product.Id,
            attributeId = attr.Id,
            value = "другу, любимому, школа"
        });
        await db.SaveChangesAsync();

        var changes = await svc.GetAttributeChangesDetailedAsync(
            product.Id,
            connection.Id,
            product.categoryId!.Value,
            new Dictionary<string, object>
            {
                ["tags"] = new List<string> { "другу", "любимому", "школа" }
            },
            CancellationToken.None);

        Assert.Empty(changes);
    }

    [Fact]
    public async Task Preview_TreatsDictionaryIdAndLabelAsSame()
    {
        var (svc, db, connection, product, attr) = await SeedMappedSelectProductAsync(
            AttributeType.Select,
            """[{"value":"901542","label":"Серый"}]""",
            "color");

        db.productAttributeValues.Add(new ProductAttributeValue
        {
            productId = product.Id,
            attributeId = attr.Id,
            value = "Серый"
        });
        await db.SaveChangesAsync();

        var changes = await svc.GetAttributeChangesDetailedAsync(
            product.Id,
            connection.Id,
            product.categoryId!.Value,
            new Dictionary<string, object> { ["color"] = "901542" },
            CancellationToken.None);

        Assert.Empty(changes);
    }

    [Fact]
    public async Task Preview_ShowsDictionaryLabelsNotIds()
    {
        var (svc, db, connection, product, attr) = await SeedMappedSelectProductAsync(
            AttributeType.Select,
            """[{"value":"901542","label":"Серый"}]""",
            "color");

        db.productAttributeValues.Add(new ProductAttributeValue
        {
            productId = product.Id,
            attributeId = attr.Id,
            value = "Синий"
        });
        await db.SaveChangesAsync();

        var changes = await svc.GetAttributeChangesDetailedAsync(
            product.Id,
            connection.Id,
            product.categoryId!.Value,
            new Dictionary<string, object> { ["color"] = "901542" },
            CancellationToken.None);

        var change = Assert.Single(changes);
        Assert.Equal("Синий", change.OldValue);
        Assert.Equal("Серый", change.NewValue);
    }

    private static async Task<(
        ProductSyncService svc,
        DataContext db,
        MarketplaceConnection connection,
        Delegat.Models.Products.Product product,
        Delegat.Models.CategoryAttribute attr)> SeedMappedSelectProductAsync(
        AttributeType type,
        string optionsJson,
        string key)
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var category = data.AddCategory(store.Id, "Local");
        var attr = data.AddCategoryAttribute(
            category.Id, key, type, options: optionsJson, key: key);
        var connection = data.AddConnection(store.Id, adapterKey: "fake");
        var mapping = data.AddMapping(connection.Id, category.Id, "10");
        mapping.graphJson = GraphGenerator.BuildGraphJson(
        [
            new FieldSpec
            {
                externalFieldKey = key,
                externalFieldName = key,
                externalFieldType = type == AttributeType.MultiSelect ? "multiselect" : "select",
                sourceType = FieldMappingSourceType.CategoryAttribute,
                sourceAttributeId = attr.Id,
            }
        ]);
        var product = data.AddProduct(store.Id, "Local", "ART-1", category.Id);
        await db.SaveChangesAsync();
        return (CreateService(db, new ImportAdapter(null)), db, connection, product, attr);
    }

    private static async Task<(
        ProductSyncService svc,
        DataContext db,
        MarketplaceConnection connection,
        Delegat.Models.Products.Product product,
        Delegat.Models.CategoryAttribute weightAttr)> SeedMappedProductAsync()
    {
        var (svc, db, connection, category, _) = await SeedMappedCategoryAsync(includeBrand: false);
        var data = new TestDataBuilder(db);
        var product = data.AddProduct(connection.storeId, "Local", "ART-1", category.Id);
        await db.SaveChangesAsync();
        var weightAttr = await db.categoryAttributes.SingleAsync(a => a.key == "weight");
        return (svc, db, connection, product, weightAttr);
    }

    private static async Task<(
        ProductSyncService svc,
        DataContext db,
        MarketplaceConnection connection,
        Delegat.Models.Category category,
        Delegat.Models.CategoryAttribute brandAttr)> SeedMappedCategoryAsync(bool includeBrand = true)
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var category = data.AddCategory(store.Id, "Local");
        var weightAttr = data.AddCategoryAttribute(category.Id, "Weight", AttributeType.Number, key: "weight");
        var brandAttr = includeBrand
            ? data.AddCategoryAttribute(category.Id, "Brand", AttributeType.Text, key: "brand")
            : weightAttr;
        var connection = data.AddConnection(store.Id, adapterKey: "fake");
        var mapping = data.AddMapping(connection.Id, category.Id, "10");
        var specs = new List<FieldSpec>
        {
            new()
            {
                externalFieldKey = "weight",
                externalFieldName = "Weight",
                externalFieldType = "number",
                sourceType = FieldMappingSourceType.CategoryAttribute,
                sourceAttributeId = weightAttr.Id,
            },
        };
        if (includeBrand)
        {
            specs.Add(new FieldSpec
            {
                externalFieldKey = "brand",
                externalFieldName = "Brand",
                externalFieldType = "string",
                sourceType = FieldMappingSourceType.CategoryAttribute,
                sourceAttributeId = brandAttr.Id,
            });
        }

        mapping.graphJson = GraphGenerator.BuildGraphJson(specs);
        await db.SaveChangesAsync();
        return (CreateService(db, new ImportAdapter(null)), db, connection, category, brandAttr);
    }

    private static ProductSyncService CreateService(DataContext db, IMarketplaceAdapter adapter)
    {
        return new ProductSyncService(
            db,
            new SingleAdapterRegistry(adapter),
            mappingService: null!,
            storageService: null!,
            NullLogger<ProductSyncService>.Instance,
            new NoopOperationLogger(),
            new QuotaService(TestDbFactory.CreateUnitOfWork(db), db));
    }

    private sealed class SingleAdapterRegistry(IMarketplaceAdapter adapter) : IMarketplaceAdapterRegistry
    {
        public IReadOnlyList<IMarketplaceAdapter> GetAll() => [adapter];
        public IMarketplaceAdapter? GetByKey(string key) =>
            key == adapter.Key ? adapter : null;
        public bool Exists(string key) => key == adapter.Key;
        public IReadOnlyList<MarketplaceInfo> GetAvailableMarketplaces() => [];
    }

    private sealed class NoopOperationLogger : IOperationLogger
    {
        public Task StartAsync(string operationId, int storeId, int authorUserId, string type, string title,
            int? connectionId = null, string? connectionName = null,
            string? batchId = null, string? batchType = null, int? totalItems = null)
            => Task.CompletedTask;
        public Task LogAsync(string operationId, int storeId, string level, string message) => Task.CompletedTask;
        public Task ProgressAsync(string operationId, int storeId, int total, int completed, int failed) => Task.CompletedTask;
        public Task FinishAsync(string operationId, int storeId, string status, string? summary = null) => Task.CompletedTask;
        public Task UpdateStatusAsync(string operationId, int storeId, string status) => Task.CompletedTask;
    }

    private sealed class ImportAdapter(ExternalProduct? product) : IMarketplaceAdapter
    {
        public string Key => "fake";
        public string DisplayName => "Fake";
        public string? Description => null;
        public string? LogoUrl => null;
        public MarketplaceCapabilities Capabilities => MarketplaceCapabilities.ImportProduct;
        public string? ProductEditUrlTemplate => null;

        public CredentialsSchema GetCredentialsSchema() => new();
        public RateLimitConfig? GetRateLimitConfig() => null;
        public Task<List<QuotaInfo>> GetQuotasAsync(MarketplaceConnection connection, CancellationToken ct = default)
            => Task.FromResult(new List<QuotaInfo>());
        public MarketplaceRateLimiter? GetRateLimiterForConnection(MarketplaceConnection connection) => null;
        public int ClearMetadataCaches(MarketplaceConnection connection) => 0;
        public Task<ConnectionTestResult> TestConnectionAsync(MarketplaceConnection connection, CancellationToken ct = default)
            => Task.FromResult(new ConnectionTestResult(true));
        public Task<List<ExternalCategory>> GetCategoriesAsync(MarketplaceConnection connection, string? parentId = null, string? searchQuery = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<List<ExternalField>> GetCategoryFieldsAsync(MarketplaceConnection connection, string externalCategoryId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<List<ExternalField>> GetPriceFieldsAsync(MarketplaceConnection connection, string externalCategoryId, CancellationToken ct = default)
            => Task.FromResult(new List<ExternalField>());
        public Task<CreateCategoryResult> CreateCategoryAsync(MarketplaceConnection connection, CreateCategoryRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ExternalProduct?> ImportProductAsync(MarketplaceConnection connection, string externalProductId, CancellationToken ct = default)
            => Task.FromResult(product);
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
