using System.Text.Json;
using Delegat.Enums;
using Delegat.Infrastructure.Interfaces;
using Delegat.Models;
using Delegat.Models.Products;

namespace Delegat.Tests;

public sealed class ProductDetailMediaTests
{
    [Fact]
    public void ScalarDtosDoNotAdvertiseAttachmentValues()
    {
        var categoryValue = new ProductAttributeValue
        {
            attribute = new CategoryAttribute { type = AttributeType.Number }, value = "42"
        };
        var categoryDto = ((IMapToDTO<ProductAttributeValue.ValueDTO>)categoryValue).MapToDTO(null!);
        Assert.Equal("42", categoryDto.value);
        Assert.Null(categoryDto.fileIds);
        var storeValue = new StoreProductAttributeValue
        {
            storeAttribute = new StoreAttribute { type = AttributeType.Number }, value = "12.5"
        };
        var storeDto = ((IMapToDTO<StoreProductAttributeValue.ValueDTO>)storeValue).MapToDTO(null!);
        Assert.Equal("12.5", storeDto.value);
        Assert.Null(storeDto.fileIds);
    }

    [Theory]
    [InlineData("91246", AttributeType.Select, 1)]
    [InlineData("[\"91246\",\"91247\"]", AttributeType.MultiSelect, 2)]
    public void DetailIncludesOnlySelectedLabelsAndMarksDictionaryPartial(string value, AttributeType type, int count)
    {
        var attribute = new CategoryAttribute
        {
            Id = 1, type = type, name = "Country", key = "country",
            options = """[{"value":"91246","label":"Russia"},{"value":"91247","label":"China"},{"value":"other","label":"Not selected"}]"""
        };
        var product = new Product
        {
            category = new Category { Id = 1, attributes = [attribute] },
            attributeValues = [new ProductAttributeValue { attributeId = 1, attribute = attribute, value = value }]
        };
        using var dto = JsonDocument.Parse(JsonSerializer.Serialize(((IMapToDTO<object>)product).MapToDTO(null!)));
        var definition = dto.RootElement.GetProperty("category").GetProperty("attributes")[0];
        Assert.True(definition.GetProperty("optionsPartial").GetBoolean());
        using var selected = JsonDocument.Parse(definition.GetProperty("options").GetString()!);
        Assert.Equal(count, selected.RootElement.GetArrayLength());
        Assert.DoesNotContain("Not selected", selected.RootElement.ToString());
        Assert.Equal("Russia", selected.RootElement[0].GetProperty("label").GetString());
        Assert.Equal(attribute.options, attribute.ToAttributeDTO().options);
        Assert.False(attribute.ToAttributeDTO().optionsPartial);
    }

    [Fact]
    public void DetailKeepsVideosOutOfLegacyImagesAndPreservesMediaOrder()
    {
        var image = new Models.File { Id = 1, contentType = "image/jpeg", filePath = "photo.jpg" };
        var video = new Models.File { Id = 2, contentType = "video/mp4", filePath = "clip.mp4" };
        var product = new Product
        {
            files = [image, video],
            images = [new ImageReference { fileId = 2, file = video, index = 0 }, new ImageReference { fileId = 1, file = image, index = 1 }]
        };
        using var dto = JsonDocument.Parse(JsonSerializer.Serialize(((IMapToDTO<object>)product).MapToDTO(null!)));
        Assert.Equal(1, dto.RootElement.GetProperty("images").GetArrayLength());
        Assert.Equal("video", dto.RootElement.GetProperty("media")[0].GetProperty("kind").GetString());
        Assert.Equal("image", dto.RootElement.GetProperty("media")[1].GetProperty("kind").GetString());
    }
}
