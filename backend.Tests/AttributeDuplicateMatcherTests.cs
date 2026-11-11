using Delegat.Enums;
using Delegat.Models;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services.Marketplace;

namespace Delegat.Tests;

public class AttributeDuplicateMatcherTests
{
    [Fact]
    public void FindCandidates_MatchesNameDescriptionTypeAndExactUnit()
    {
        var field = new ExternalField(
            "product_width",
            "Product width (mm)",
            false,
            "number",
            Description: "Width of the product in millimeters");
        var candidate = Attribute(
            1,
            "Ширина товара",
            AttributeType.Number,
            "mm",
            "Width of the product in millimeters");
        var otherUnit = Attribute(2, "Ширина товара", AttributeType.Number, "cm");
        var otherType = Attribute(3, "Ширина товара", AttributeType.Text, "mm");

        var matches = AttributeDuplicateMatcher.FindCandidates(
            field,
            AttributeType.Number,
            "mm",
            [candidate, otherUnit, otherType]);

        var match = Assert.Single(matches);
        Assert.Equal(candidate.Id, match.Attribute.Id);
        Assert.Contains("description", match.MatchedBy);
    }

    [Theory]
    [InlineData("Height", "Height with packaging")]
    [InlineData("Высота без упаковки", "Высота с упаковкой")]
    [InlineData("Gross weight", "Net weight")]
    [InlineData("Вес брутто", "Вес")]
    public void FindCandidates_DoesNotMixSemanticallyDistinctAttributes(
        string marketplaceName,
        string localName)
    {
        var field = new ExternalField("size", marketplaceName, false, "string");
        var matches = AttributeDuplicateMatcher.FindCandidates(
            field,
            AttributeType.Text,
            null,
            [Attribute(1, localName, AttributeType.Text)]);

        Assert.Empty(matches);
    }

    [Fact]
    public void FindCandidates_DoesNotTreatMissingUnitAsCompatibleWithKnownUnit()
    {
        var field = new ExternalField("weight", "Weight", false, "number", UnitCode: "kg");
        var matches = AttributeDuplicateMatcher.FindCandidates(
            field,
            AttributeType.Number,
            "kg",
            [Attribute(1, "Weight", AttributeType.Number, null)]);

        Assert.Empty(matches);
    }

    private static CategoryAttribute Attribute(
        int id,
        string name,
        AttributeType type,
        string? unitCode = null,
        string? description = null)
        => new()
        {
            Id = id,
            name = name,
            key = $"attribute_{id}",
            type = type,
            unitCode = unitCode,
            description = description,
        };
}
