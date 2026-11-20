using Delegat.Enums;
using Delegat.Services.Ai.Agent;

namespace Delegat.Tests;

public class CabinetActionCatalogTests
{
    [Theory]
    [InlineData("список доступных типов атрибутов")]
    [InlineData("supported attribute types for a category")]
    public void Search_AttributeTypeDiscovery_ReturnsDefinitionActionsWithEveryEnumValue(string query)
    {
        var hits = new CabinetActionCatalog().Search(query);

        var define = Assert.Single(hits, h => h.Method == "POST"
            && h.Path == "/categories/{categoryId}/attributes");
        AssertEnumGuidance(define);
        var update = Assert.Single(hits, h => h.Method == "PATCH"
            && h.Path == "/categories/{categoryId}/attributes/{attributeId}");
        AssertEnumGuidance(update);
        return;

        static void AssertEnumGuidance(CabinetActionCatalog.ActionHit hit)
        {
            Assert.Contains("Allowed type values:", hit.Note);
            foreach (var name in Enum.GetNames<AttributeType>())
                Assert.Contains($"{name}={(int)Enum.Parse<AttributeType>(name)}", hit.Note);
        }
    }

    [Theory]
    [InlineData("перегенерируй штрихкоды этих товаров")]
    [InlineData("обновить товар, изменить штрихкод карточки")]
    [InlineData("regenerate barcodes for selected products")]
    [InlineData("fill missing product barcodes")]
    [InlineData("generateBarcodes")]
    [InlineData("изменить остатки выбранных товаров")]
    [InlineData("set storageQuantity for selected products")]
    public void Search_ConcreteCatalogFields_OffersNativeBulkFirst(string query)
    {
        var hit = Assert.Single(new CabinetActionCatalog().Search(query, limit: 1));
        Assert.Equal("/products/bulk", hit.Path);
        Assert.Contains("generateBarcodes", hit.Note);
        Assert.Contains("barcode: \"\", generateBarcodes: true", hit.Note);
        Assert.Contains("NOT a per-product values map", hit.Note);
        Assert.Contains("failedCount", hit.Note);
    }

    [Theory]
    [InlineData("астрономия созвездия")]
    [InlineData("quasar telescope")]
    public void Search_UnrelatedRequest_DoesNotManufactureAnAction(string query)
        => Assert.Empty(new CabinetActionCatalog().Search(query));

    [Fact]
    public void BulkContract_AdvertisesOnlyRealNestedDtoFields()
    {
        var fields = AssistantBodySchema.UpdateFields("PATCH", "/products/bulk");
        Assert.Contains("generateBarcodes", fields);
        Assert.Contains("barcode", fields);
        Assert.Contains("storageQuantity", fields);
        Assert.DoesNotContain("productIds", fields);
        Assert.DoesNotContain("id", fields);
        var hit = Assert.Single(new CabinetActionCatalog().Search("generateBarcodes", 1));
        Assert.Contains("Accepted updates fields", hit.Note);
        Assert.Contains("distinct externally supplied barcodes need individual PATCH", hit.Note);
        Assert.Contains("unique attribute values cannot be shared", hit.Note);
        var shape = AssistantBodySchema.Match("PATCH", "/products/bulk")!.Body.Properties["updates"];
        foreach (var field in new[] { "name", "description", "storageQuantity", "price", "oldPrice", "minPrice", "netPrice",
                     "currency", "barcode", "generateBarcodes", "priceDelta", "attributeValues", "storeAttributeValues" })
        {
            Assert.True(shape.Properties.ContainsKey(field), $"Advertised bulk field {field} is absent from the controller DTO");
            Assert.Contains(field, AssistantWriteContract.ProductsBulk);
        }
    }

    [Fact]
    public void ProductFileDownload_IsNotDiscoverableAsCabinetAction()
    {
        Assert.False(CabinetActionCatalog.IsSearchableAction("GET", "/files/{id}/download"));
        Assert.False(CabinetActionCatalog.IsSearchableAction("GET", "/store-storage/files/{id}/download"));
        Assert.True(CabinetActionCatalog.IsSearchableAction("GET", "/products/42/files"));
    }

    [Fact]
    public void Search_ExportInRussian_FindsExportBatch()
    {
        var catalog = new CabinetActionCatalog();
        var hits = catalog.Search("выгрузить все товары в магазин medusa без фото");

        Assert.Contains(hits, h => h.Path.Contains("export-batch", StringComparison.Ordinal));
        Assert.Contains(hits, h => h.Note.Contains("allMatching", StringComparison.Ordinal));
    }

    [Fact]
    public void Search_Tools_DoesNotLookLikeProductExport()
    {
        var catalog = new CabinetActionCatalog();
        var hits = catalog.Search("инструменты выгрузка цен");
        Assert.Contains(hits, h => h.Path.Equals("/tools", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(hits, h => h.Note.Contains("Not product export", StringComparison.Ordinal));
    }

    [Fact]
    public void Search_MedusaCollectionTitles_FindsGraphBlocks()
    {
        var catalog = new CabinetActionCatalog();
        var hits = catalog.Search("изменить маппинг групп medusa название коллекции valueMap");

        Assert.Contains(hits, h => h.Path.Contains("graph/connect", StringComparison.Ordinal));
        Assert.Contains(hits, h => h.Path.Contains("graph/modifiers", StringComparison.Ordinal));
        Assert.Contains(hits, h => h.Note.Contains("valueMap", StringComparison.Ordinal));
        Assert.Contains(hits, h => h.Note.Contains("groupName", StringComparison.Ordinal));
    }

    [Fact]
    public void Search_BlockSizesAndPins_FindsCatalog()
    {
        var catalog = new CabinetActionCatalog();
        var hits = catalog.Search("блоки размеры пины входы выходы типы");

        Assert.Contains(hits, h => h.Path.Contains("mapping-graph/blocks", StringComparison.Ordinal));
        Assert.Contains(hits, h => h.Note.Contains("width/height", StringComparison.Ordinal));
    }

    [Fact]
    public void Search_MergeIntoCard_FindsGroupTitles()
    {
        var catalog = new CabinetActionCatalog();
        var hits = catalog.Search("объединить в карточку соответствия групп medusa");

        Assert.Contains(hits, h => h.Path.Contains("group-titles", StringComparison.Ordinal));
        Assert.Contains(hits, h => h.Note.Contains("__group__", StringComparison.Ordinal));
    }

    [Fact]
    public void Search_FinishFieldMapping_FindsConnectNotGroupTitles()
    {
        var catalog = new CabinetActionCatalog();
        var hits = catalog.Search("доделать маппинг связать основные параметры");

        Assert.Contains(hits, h => h.Path.Contains("graph/connect", StringComparison.Ordinal));
        Assert.DoesNotContain(hits, h => h.Path.Contains("group-titles", StringComparison.Ordinal));
    }

    [Fact]
    public void Search_UnitReadAndEdit_UsesRussianAliasesAndRealBodyFields()
    {
        var catalog = new CabinetActionCatalog();

        var read = Assert.Single(catalog.Search("покажи единицы измерения характеристик категории", limit: 1));
        Assert.Equal("GET", read.Method);
        Assert.Equal("/categories/{categoryId}/attributes/summary", read.Path);
        Assert.Contains("unitCode", read.Note);
        Assert.Contains("without option dictionaries", read.Note);

        var storeRead = Assert.Single(catalog.Search("покажи единицы измерения общих характеристик", limit: 1));
        Assert.Equal("GET", storeRead.Method);
        Assert.Equal("/store-attributes/summary", storeRead.Path);

        var edit = Assert.Single(catalog.Search("измени единицу измерения характеристики категории", limit: 1));
        Assert.Equal("PATCH", edit.Method);
        Assert.Equal("/categories/{categoryId}/attributes/{attributeId}", edit.Path);
        Assert.Contains("Accepted body fields:", edit.Note);
        Assert.Contains("unitCode", edit.Note);
        Assert.Contains("does not convert stored product values", edit.Note);

        var update = Assert.Single(catalog.Search("обнови единицу измерения характеристики категории", limit: 1));
        Assert.Equal("PATCH", update.Method);
        Assert.Equal("/categories/{categoryId}/attributes/{attributeId}", update.Path);

        var storeEdit = Assert.Single(catalog.Search("измени единицу измерения глобальной характеристики", limit: 1));
        Assert.Equal("PATCH", storeEdit.Method);
        Assert.Equal("/store-attributes/{attributeId}", storeEdit.Path);
        Assert.Contains("unitCode", storeEdit.Note);
    }

    [Fact]
    public void Search_RaisePrices_FindsBulkDeltaAndShopExport()
    {
        var catalog = new CabinetActionCatalog();
        var hits = catalog.Search("повысь все цены на 500 рублей обновить на яндексе вб и озоне");

        Assert.Contains(hits, h => h.Path.Equals("/products/bulk", StringComparison.OrdinalIgnoreCase)
            && h.Note.Contains("priceDelta", StringComparison.Ordinal));
        Assert.Contains(hits, h => h.Path.Contains("export-all-prices/run", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("обновить карточки в магазине")]
    [InlineData("refresh product listings in shop")]
    [InlineData("update product listings on marketplace")]
    [InlineData("повторно отправить товары на маркетплейс")]
    public void Search_RefreshListings_OffersBatchExportFirst(string query)
    {
        var hit = Assert.Single(new CabinetActionCatalog().Search(query, limit: 1));
        Assert.Equal("/marketplace/connections/{id}/products/export-batch", hit.Path);
    }

    [Fact]
    public void Search_UpdatePrices_DoesNotBecomeProductExport()
    {
        var hit = Assert.Single(new CabinetActionCatalog().Search("обновить цены в магазине", limit: 1));
        Assert.Equal("/tools/export-all-prices/run", hit.Path);
    }

    [Fact]
    public void Search_MappingEdit_DoesNotBecomeProductExport()
    {
        var hit = Assert.Single(new CabinetActionCatalog().Search("доделать маппинг связать основные параметры", limit: 1));
        Assert.Equal("/marketplace/connections/{id}/mappings/{mappingId}/graph/connect", hit.Path);
    }

    [Fact]
    public void Search_DoesNotTreatGenericRouteIdsOrUsageNotesAsActionNames()
    {
        var catalog = new CabinetActionCatalog();
        Assert.Empty(catalog.Search("id"));
        Assert.Empty(catalog.Search("pending"));
    }
}
