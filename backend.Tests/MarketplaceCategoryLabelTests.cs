using Delegat.Models.Marketplace.DTOs;
using Delegat.Services.Marketplace;

namespace Delegat.Tests;

public class MarketplaceCategoryLabelTests
{
    [Theory]
    [InlineData(null, "Hobbies / Board games / Accessories", "Accessories")]
    [InlineData("Hobbies / Board games / Accessories", "Hobbies / Board games / Accessories", "Accessories")]
    [InlineData("Custom / Name", "Hobbies / Accessories", "Custom / Name")]
    [InlineData(null, "AC/DC accessories", "AC/DC accessories")]
    [InlineData(null, "Toys", "Toys")]
    public void ForLocalCategory_UsesLeafOnlyForAutomaticNames(string? requested, string source, string expected)
        => Assert.Equal(expected, MarketplaceCategoryLabel.ForLocalCategory("236", requested, source));

    [Theory]
    [InlineData(null, "17028973:115944384")]
    [InlineData("", "17028973:115944384")]
    [InlineData("17028973:115944384", "17028973:115944384")]
    [InlineData("Category 17028973:115944384", "17028973:115944384")]
    [InlineData("category 17028973:115944384", "17028973:115944384")]
    public void IsSynthetic_PlaceholderOrId(string? label, string id)
    {
        Assert.True(MarketplaceCategoryLabel.IsSynthetic(label, id));
    }

    [Fact]
    public void IsSynthetic_HumanPathIsReal()
    {
        Assert.False(MarketplaceCategoryLabel.IsSynthetic(
            "Apparel / T-shirts", "17028973:115944384"));
    }

    [Fact]
    public void FromExternalCategory_PrefersFullPath()
    {
        var cat = new ExternalCategory(
            "10:2",
            "T-shirt",
            FullPath: "Apparel / T-shirt");

        Assert.Equal("Apparel / T-shirt", MarketplaceCategoryLabel.FromExternalCategory(cat));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0", false)]
    [InlineData("parent:1", false)]
    [InlineData("42", true)]
    [InlineData("10:2", true)]
    [InlineData("0:123", true)]
    public void IsMappableId(string? id, bool expected)
    {
        Assert.Equal(expected, MarketplaceCategoryLabel.IsMappableId(id));
    }

    [Fact]
    public void FirstReal_SkipsPlaceholders()
    {
        var chosen = MarketplaceCategoryLabel.FirstReal(
            "10:2",
            "Category 10:2",
            "10:2",
            "Shirts");

        Assert.Equal("Shirts", chosen);
    }
}
