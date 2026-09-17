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
}
