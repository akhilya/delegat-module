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
    public void ApplyTo_NormalizesNumericUnitMetadata()
    {
        var form = new CategoryAttribute.AddForm
        {
            name = "Width",
            key = "width",
            type = Delegat.Enums.AttributeType.Number,
        };

        AttributeFieldConstraints.ApplyTo(
            ref form,
            new ExternalField("width", "Width", false, "number", Unit: "сантиметр"));

        Assert.Equal("cm", form.unitCode);
    }

    [Fact]
    public void ResolveUnitCode_DoesNotTreatQuotedArticleTitleAsInches()
    {
        var field = new ExternalField(
            "purchase_multiple",
            "\u041A\u0440\u0430\u0442\u043D\u043E\u0441\u0442\u044C \u043F\u043E\u043A\u0443\u043F\u043A\u0438",
            false,
            "number",
            Description: "\u041A\u043E\u043B\u0438\u0447\u0435\u0441\u0442\u0432\u043E \u0442\u043E\u0432\u0430\u0440\u043E\u0432 \u0432 \u0441\u0442\u0430\u0442\u044C\u0435 \"\u041A\u0440\u0430\u0442\u043D\u043E\u0441\u0442\u044C \u043F\u043E\u043A\u0443\u043F\u043A\u0438\".");

        Assert.Null(AttributeFieldConstraints.ResolveUnitCode(field));
    }

    [Fact]
    public void ResolveUnitCode_RecognizesInchesWhenQuoteFollowsNumber()
    {
        var field = new ExternalField(
            "screen_size", "Screen size", false, "number", Description: "Enter a size such as 4\"");

        Assert.Equal("in", AttributeFieldConstraints.ResolveUnitCode(field));
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
    public void ApplyIfEmpty_FillsMissingNumericUnitButPreservesExistingAndCustomValues()
    {
        var attribute = new CategoryAttribute { type = Delegat.Enums.AttributeType.Number };
        Assert.True(AttributeFieldConstraints.ApplyIfEmpty(
            attribute,
            new ExternalField("weight", "Weight", false, "number", Unit: "кг")));
        Assert.Equal("kg", attribute.unitCode);

        Assert.False(AttributeFieldConstraints.ApplyIfEmpty(
            attribute,
            new ExternalField("weight", "Weight", false, "number", Unit: "г")));
        Assert.Equal("kg", attribute.unitCode);

        var nonNumeric = new CategoryAttribute { type = Delegat.Enums.AttributeType.Text };
        Assert.False(AttributeFieldConstraints.ApplyIfEmpty(
            nonNumeric,
            new ExternalField("name", "Name", false, "string", Unit: "kg")));
        Assert.Null(nonNumeric.unitCode);

        var inferred = new CategoryAttribute { type = Delegat.Enums.AttributeType.Number };
        Assert.True(AttributeFieldConstraints.ApplyIfEmpty(
            inferred,
            new ExternalField("width", "Width (mm)", false, "number")));
        Assert.Equal("mm", inferred.unitCode);
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
