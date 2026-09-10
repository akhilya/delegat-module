using Delegat.Enums;
using Delegat.Models;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services.Marketplace;

namespace Delegat.Tests;

public class LocalCategoryAdapterFieldsTests
{
    [Fact]
    public void ShouldExposeLocalAttributes_OnlyMedusa()
    {
        Assert.True(LocalCategoryAdapterFields.ShouldExposeLocalAttributes("medusa"));
        Assert.True(LocalCategoryAdapterFields.ShouldExposeLocalAttributes("Medusa"));
        Assert.False(LocalCategoryAdapterFields.ShouldExposeLocalAttributes("ozon"));
        Assert.False(LocalCategoryAdapterFields.ShouldExposeLocalAttributes("woocommerce"));
    }

    [Fact]
    public void Merge_AddsLocalCategoryAttributesNotAlreadyOnAdapter()
    {
        var adapter = new List<ExternalField>
        {
            new("sku", "SKU", true, "string", SortOrder: 1),
            new("name", "Name", true, "string", SortOrder: 2),
        };
        var attributes = new[]
        {
            new CategoryAttribute { key = "sku", name = "Артикул", type = AttributeType.Text, sortOrder = 1 },
            new CategoryAttribute { key = "material", name = "Материал", type = AttributeType.Text, sortOrder = 10, group = "Specs" },
            new CategoryAttribute { key = "scale", name = "Масштаб", type = AttributeType.Select, sortOrder = 11, options = "[\"1:18\",\"1:35\"]" },
        };

        var merged = LocalCategoryAdapterFields.Merge(adapter, attributes);

        Assert.Equal(4, merged.Count);
        Assert.Contains(merged, f => f.Key == "sku" && f.IsRequired);
        var material = Assert.Single(merged, f => f.Key == "material");
        Assert.Equal("Материал", material.Name);
        Assert.Equal("Specs", material.Group);
        Assert.False(material.IsRequired);
        var scale = Assert.Single(merged, f => f.Key == "scale");
        Assert.Equal("select", scale.Type);
        Assert.Equal(2, scale.Options!.Count);
    }
}
