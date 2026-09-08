using System.Text.Json;
using Delegat.Infrastructure.Json;
using Delegat.Models;
using Delegat.Models.Products;

namespace Delegat.Tests;

public class FlexibleJsonStringConverterTests
{
    [Fact]
    public void UserValueForm_AcceptsNumberBoolAndArray()
    {
        var json = """
            {"attributeId":147,"value":30}
            """;
        var number = JsonSerializer.Deserialize<ProductAttributeValue.UserValueForm>(json);
        Assert.Equal("30", number.value);

        var flag = JsonSerializer.Deserialize<ProductAttributeValue.UserValueForm>(
            """{"attributeId":188,"value":true}""");
        Assert.Equal("true", flag.value);

        var multi = JsonSerializer.Deserialize<ProductAttributeValue.UserValueForm>(
            """{"attributeId":152,"value":["Пластик"]}""");
        Assert.Equal("""["Пластик"]""", multi.value);

        var text = JsonSerializer.Deserialize<ProductAttributeValue.UserValueForm>(
            """{"attributeId":146,"value":"TestBrand"}""");
        Assert.Equal("TestBrand", text.value);
    }

    [Fact]
    public void ProductCreateBody_CoercesNestedCharacteristicValues()
    {
        var json = """
            {"name":"X","sku":"SKU-1","price":990,"attributeValues":[{"attributeId":147,"value":0.8},{"attributeId":152,"value":["Пластик"]}]}
            """;
        var form = JsonSerializer.Deserialize<Product.UserAddForm>(json);
        Assert.Equal("0.8", form.attributeValues![0].value);
        Assert.Equal("""["Пластик"]""", form.attributeValues[1].value);
    }
}

