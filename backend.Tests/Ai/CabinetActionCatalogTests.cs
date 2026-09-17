using Delegat.Services.Ai.Agent;

namespace Delegat.Tests;

public class CabinetActionCatalogTests
{
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
}
