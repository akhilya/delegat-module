using Delegat.Services.Ai.Agent;

namespace Delegat.Tests;

public class CabinetActionCatalogTests
{
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
