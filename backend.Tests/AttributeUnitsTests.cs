using Delegat.Enums;
using Delegat.Models;

namespace Delegat.Tests;

public class AttributeUnitsTests
{
    [Theory]
    [InlineData(" KG ", "kg")]
    [InlineData("сантиметр", "cm")]
    [InlineData("Н·м", "n_m")]
    [InlineData("μm", "um")]
    public void NormalizeCode_MapsKnownAliases(string input, string expected)
        => Assert.Equal(expected, AttributeUnits.NormalizeCode(input));

    [Fact]
    public void NormalizeCode_PreservesUnknownCodesAndEmptyValues()
    {
        Assert.Equal("market:unit-42", AttributeUnits.NormalizeCode(" market:unit-42 "));
        Assert.Null(AttributeUnits.NormalizeCode("  "));
    }

    [Fact]
    public void AreSame_UsesNormalizedCodesWithoutConvertingDifferentUnits()
    {
        Assert.True(AttributeUnits.AreSame("килограмм", "kg"));
        Assert.False(AttributeUnits.AreSame("kg", "g"));
    }

    [Theory]
    [InlineData("kg", "g", "2", "2000")]
    [InlineData("m", "cm", "1.25", "125")]
    [InlineData("l", "ml", "0.5", "500")]
    public void TryGetConversion_ConvertsWithinKnownDimensions(
        string from,
        string to,
        string input,
        string expected)
    {
        var inputValue = decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture);
        var expectedValue = decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(AttributeUnits.TryGetConversion(from, to, out var conversion));
        Assert.Equal(expectedValue, conversion.Convert(inputValue));
        Assert.Equal(inputValue, conversion.Reverse(expectedValue));
    }

    [Fact]
    public void TryGetConversion_SupportsAffineTemperatureAndRejectsUnknownOrIncompatibleUnits()
    {
        Assert.True(AttributeUnits.TryGetConversion("f", "c", out var fahrenheitToCelsius));
        Assert.InRange(Math.Abs(fahrenheitToCelsius.Convert(32m)), 0m, 0.00000000000000000000000001m);
        Assert.False(AttributeUnits.TryGetConversion("kg", "cm", out _));
        Assert.False(AttributeUnits.TryGetConversion("month", "day", out _));
        Assert.False(AttributeUnits.TryGetConversion("vendor:unit-a", "vendor:unit-b", out _));
    }

    [Theory]
    [InlineData("Gross weight (kg)", "kg")]
    [InlineData("Package width, mm", "mm")]
    [InlineData("Length in cm", "cm")]
    [InlineData("Product weight measured in kilograms", "kg")]
    [InlineData("Температура (°C)", "c")]
    public void InferCodeFromText_RecognizesKnownUnits(string text, string expected)
        => Assert.Equal(expected, AttributeUnits.InferCodeFromText(text));

    [Theory]
    [InlineData("Vitamin C")]
    [InlineData("Масса с упаковкой")]
    [InlineData("Package size: 10 cm or 4 in")]
    public void InferCodeFromText_LeavesAmbiguousTextUnchanged(string text)
        => Assert.Null(AttributeUnits.InferCodeFromText(text));

    [Fact]
    public void InferCodeFromText_UsesFieldNameAsDescriptionContext()
        => Assert.Equal("v", AttributeUnits.InferCodeFromText("V", "Voltage"));

    [Fact]
    public void AddFill_NormalizesKnownCodesAndKeepsUnknownCodes()
    {
        var known = new CategoryAttribute();
        known.AddFill(new CategoryAttribute.AddForm
        {
            name = "Weight",
            key = "weight",
            type = AttributeType.Number,
            unitCode = "КГ",
        });

        var custom = new StoreAttribute();
        custom.AddFill(new StoreAttribute.AddForm
        {
            name = "Custom",
            key = "custom",
            type = AttributeType.Number,
            unitCode = "vendor:box",
        });

        Assert.Equal("kg", known.unitCode);
        Assert.Equal("vendor:box", custom.unitCode);
    }

    [Fact]
    public void UpdateFill_ClearsUnitWhenAttributeStopsBeingNumeric()
    {
        var categoryAttribute = new CategoryAttribute
        {
            type = AttributeType.Number,
            unitCode = "kg",
        };
        categoryAttribute.UpdateFill(new CategoryAttribute.UpdateForm { type = AttributeType.Text });

        var storeAttribute = new StoreAttribute
        {
            type = AttributeType.Number,
            unitCode = "cm",
        };
        storeAttribute.UpdateFill(new StoreAttribute.UpdateForm { type = AttributeType.Text });

        Assert.Null(categoryAttribute.unitCode);
        Assert.Null(storeAttribute.unitCode);
    }
}
