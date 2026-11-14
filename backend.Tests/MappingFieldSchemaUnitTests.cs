using System.Text.Json;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services.Marketplace;

namespace Delegat.Tests;

public class MappingFieldSchemaUnitTests
{
    [Theory]
    [InlineData("Вес с упаковкой", "Укажите вес товара в граммах.", "g")]
    [InlineData("Длина", "Габарит упаковки в сантиметрах.", "cm")]
    [InlineData("Ширина", "Значение в миллиметрах.", "mm")]
    [InlineData("Вес", "Вес в килограммах с учётом упаковки.", "kg")]
    [InlineData("Voltage", "V", "v")]
    public void EditorSchemaInfersUnitsFromDescription(string name, string description, string expected)
    {
        var field = new ExternalField("measurement", name, false, "number", Description: description);
        var dto = JsonSerializer.SerializeToElement(MappingFieldSchema.ToDto(field));
        Assert.Equal(expected, dto.GetProperty("unitCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(dto.GetProperty("unit").GetString()));
        Assert.Equal(description, dto.GetProperty("description").GetString());
    }

    [Theory]
    [InlineData("kg", null, "kg")]
    [InlineData("", "кг", "kg")]
    [InlineData(null, "vendor:unit", "vendor:unit")]
    public void ExplicitUnitsTakePriorityOverDescription(string? code, string? unit, string expected)
    {
        var field = new ExternalField("weight", "Вес", false, "number",
            Description: "Вес в граммах.", Unit: unit, UnitCode: code);
        var dto = JsonSerializer.SerializeToElement(MappingFieldSchema.ToDto(field));
        Assert.Equal(expected, dto.GetProperty("unitCode").GetString());
    }

    [Theory]
    [InlineData("number", "Размер в сантиметрах или миллиметрах.")]
    [InlineData("string", "Длина в сантиметрах.")]
    public void DoesNotGuessAmbiguousUnitsOrUnitsForTextFields(string type, string description)
    {
        var dto = JsonSerializer.SerializeToElement(MappingFieldSchema.ToDto(
            new ExternalField("value", "Значение", false, type, Description: description)));
        Assert.Equal(JsonValueKind.Null, dto.GetProperty("unitCode").ValueKind);
    }
}
