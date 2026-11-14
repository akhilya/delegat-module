using System.Text.Json;
using Delegat.Enums;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Services.Marketplace;
using Delegat.Services.Marketplace.Modifiers;
using Delegat.Tests.Graph;
using Delegat.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Delegat.Tests;

public class MappingUnitRoundTripTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ExportAndImportPreserveAttributeValuesThroughNumericBlocks(bool storeAttribute, bool divide)
    {
        await using var db = TestDbFactory.CreateDb();
        var seed = new TestDataBuilder(db);
        var store = seed.AddStore();
        var category = seed.AddCategory(store.Id);
        var product = seed.AddProduct(store.Id, categoryId: category.Id);
        var graph = new GraphEvalHarness();
        if (storeAttribute)
        {
            var attribute = seed.AddStoreAttribute(store.Id, type: AttributeType.Number);
            attribute.unitCode = "mm";
            product.storeAttributeValues.Add(new StoreProductAttributeValue
            {
                storeAttributeId = attribute.Id, storeAttribute = attribute, product = product, value = "110"
            });
            graph.StoreAttribute("depth", attribute.Id, "number");
        }
        else
        {
            var attribute = seed.AddCategoryAttribute(category.Id, type: AttributeType.Number);
            attribute.unitCode = "mm";
            product.attributeValues.Add(new ProductAttributeValue
            {
                attributeId = attribute.Id, attribute = attribute, product = product, value = "110"
            });
            graph.Attribute("depth", attribute.Id, "number");
        }
        await db.SaveChangesAsync();
        graph.Modifier("convert", divide ? "divide" : "unitConversion",
                divide ? new() { ["divisor"] = 10 } : new() { ["fromUnit"] = "m", ["toUnit"] = "mm" })
            .Edge("depth", "convert")
            .Field("length", "number", configure: f =>
            {
                f.label = "Длина";
                f.description = "Длина упаковки в сантиметрах.";
                f.isRequired = true;
            })
            .WireToField("convert", "length");
        var json = JsonSerializer.Serialize(graph.Graph);
        using var services = new ServiceCollection().AddSingleton<IStorageService>(new RecordingCatalogStorage()).BuildServiceProvider();
        var service = new MappingService(db, services);
        var data = await service.BuildExportDataAsync(product, new CategoryMapping { graphJson = json });
        Assert.Equal(11m, data["length"]);
        var field = Assert.Single(await service.ProjectGraphForImportAsync(json));
        Assert.Equal(110m, ModifierPipeline.ApplyModifiersReverse("11", field.modifiersJson));
    }
}
