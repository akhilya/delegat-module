using Delegat.Models;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services.Marketplace;

namespace Delegat.Tests;

public class AttributeFieldConstraintsTests
{
    [Fact]
    public void UsableValidationPattern_DropsYandexUnitId()
    {
        Assert.Null(AttributeFieldConstraints.UsableValidationPattern("unitId=12"));
        Assert.Equal(@"^\d+$", AttributeFieldConstraints.UsableValidationPattern(@"^\d+$"));
    }

    [Fact]
    public void NormalizeMaxLength_ClipsToStoredValueCap()
    {
        Assert.Null(AttributeFieldConstraints.NormalizeMaxLength(0));
        Assert.Null(AttributeFieldConstraints.NormalizeMaxLength(-1));
        Assert.Equal(80, AttributeFieldConstraints.NormalizeMaxLength(80));
        Assert.Equal(
            ProductAttributeValue.VALUE_MAX_LENGTH,
            AttributeFieldConstraints.NormalizeMaxLength(ProductAttributeValue.VALUE_MAX_LENGTH + 50));
    }

    [Fact]
    public void NormalizeRange_DropsInvertedBounds()
    {
        var (min, max) = AttributeFieldConstraints.NormalizeRange(10, 1);
        Assert.Null(min);
        Assert.Null(max);
    }

    [Fact]
    public void ApplyTo_CopiesLimitsOntoAddForm()
    {
        var form = new CategoryAttribute.AddForm
        {
            name = "Width",
            key = "width",
            defaultValue = new string('x', 20),
        };
        AttributeFieldConstraints.ApplyTo(
            ref form,
            new ExternalField("width", "Width", false, "number", MaxLength: 8, MinValue: 1, MaxValue: 99));

        Assert.Equal(8, form.maxLength);
        Assert.Equal(1m, form.minValue);
        Assert.Equal(99m, form.maxValue);
        Assert.Equal(8, form.defaultValue!.Length);
        Assert.False(form.allowCustomValues);
    }

    [Fact]
    public void ApplyTo_CopiesAllowCustomValues()
    {
        var form = new CategoryAttribute.AddForm { name = "Color", key = "color" };
        AttributeFieldConstraints.ApplyTo(
            ref form,
            new ExternalField("color", "Color", false, "select", AllowCustomValues: true));

        Assert.True(form.allowCustomValues);
    }

    [Fact]
    public void NormalizeMaxValues_TreatsZeroAsUnlimited()
    {
        Assert.Null(AttributeFieldConstraints.NormalizeMaxValues(0));
        Assert.Null(AttributeFieldConstraints.NormalizeMaxValues(-1));
        Assert.Equal(3, AttributeFieldConstraints.NormalizeMaxValues(3));
        Assert.Equal(
            AttributeFieldConstraints.MaxValuesCap,
            AttributeFieldConstraints.NormalizeMaxValues(AttributeFieldConstraints.MaxValuesCap + 50));
    }

    [Fact]
    public void ApplyTo_CopiesMaxValues()
    {
        var form = new CategoryAttribute.AddForm { name = "Color", key = "color" };
        AttributeFieldConstraints.ApplyTo(
            ref form,
            new ExternalField("color", "Color", false, "multiselect", MaxValues: 3));

        Assert.Equal(3, form.maxValues);
    }

    [Fact]
    public void ApplyIfEmpty_FillsMaxValuesButDoesNotOverwrite()
    {
        var attribute = new CategoryAttribute { maxValues = null };
        Assert.True(AttributeFieldConstraints.ApplyIfEmpty(
            attribute,
            new ExternalField("color", "Color", false, "multiselect", MaxValues: 5)));
        Assert.Equal(5, attribute.maxValues);

        Assert.False(AttributeFieldConstraints.ApplyIfEmpty(
            attribute,
            new ExternalField("color", "Color", false, "multiselect", MaxValues: 2)));
        Assert.Equal(5, attribute.maxValues);
    }

    [Fact]
    public void ApplyIfEmpty_EnablesAllowCustomValuesButDoesNotDisable()
    {
        var attribute = new CategoryAttribute { allowCustomValues = false };
        Assert.True(AttributeFieldConstraints.ApplyIfEmpty(
            attribute,
            new ExternalField("color", "Color", false, "select", AllowCustomValues: true)));
        Assert.True(attribute.allowCustomValues);

        Assert.False(AttributeFieldConstraints.ApplyIfEmpty(
            attribute,
            new ExternalField("color", "Color", false, "select", AllowCustomValues: false)));
        Assert.True(attribute.allowCustomValues);
    }
}
