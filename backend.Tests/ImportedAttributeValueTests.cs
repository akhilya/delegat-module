using System.Globalization;
using System.Text.Json;
using Delegat.Enums;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services.Marketplace;

namespace Delegat.Tests;

public class ImportedAttributeValueTests
{
    [Fact]
    public void Number_UsesInvariantDot_EvenInRussianCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
        try
        {
            Assert.Equal("1.5", ImportedAttributeValue.ToStoredString(1.5d, AttributeType.Number));
            Assert.Equal("0.15", ImportedAttributeValue.ToStoredString(0.15m, AttributeType.Number));
            Assert.Equal("1.5", ImportedAttributeValue.ToStoredString("1,5", AttributeType.Number));
            Assert.Equal("1234.56", ImportedAttributeValue.ToStoredString("1.234,56", AttributeType.Number));
            Assert.Equal("1234.56", ImportedAttributeValue.ToStoredString("1,234.56", AttributeType.Number));
            Assert.Equal("10", ImportedAttributeValue.ToStoredString(10, AttributeType.Number));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Number_FromJsonElement_StaysInvariant()
    {
        using var doc = JsonDocument.Parse("1.25");
        Assert.Equal("1.25", ImportedAttributeValue.ToStoredString(doc.RootElement.Clone(), AttributeType.Number));
    }

    [Fact]
    public void Boolean_NormalizesToTrueFalse()
    {
        Assert.Equal("true", ImportedAttributeValue.ToStoredString(true, AttributeType.Boolean));
        Assert.Equal("false", ImportedAttributeValue.ToStoredString("0", AttributeType.Boolean));
        Assert.Equal("true", ImportedAttributeValue.ToStoredString("yes", AttributeType.Boolean));
    }

    [Fact]
    public void MultiSelect_StoresJsonArray()
    {
        var stored = ImportedAttributeValue.ToStoredString(
            new List<object> { "red", "blue" },
            AttributeType.MultiSelect);
        Assert.Equal("""["red","blue"]""", stored);
    }

    [Fact]
    public void MultiSelect_DoesNotDoubleWrapAJsonArrayString()
    {
        var once = ImportedAttributeValue.ToStoredString(
            new List<string> { "другу", "любимому", "школа" },
            AttributeType.MultiSelect);
        var twice = ImportedAttributeValue.ToStoredString(once, AttributeType.MultiSelect);

        Assert.Equal("""["другу","любимому","школа"]""", once);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void MultiSelect_UnwrapsUnicodeEscapedNestedJsonArray()
    {
        var inner = JsonSerializer.Serialize(new[] { "другу", "любимому", "школа" });
        var nested = JsonSerializer.Serialize(new[] { inner });

        var stored = ImportedAttributeValue.ToStoredString(nested, AttributeType.MultiSelect);

        Assert.Equal("""["другу","любимому","школа"]""", stored);
    }

    [Fact]
    public void MultiSelect_CommaSeparatedMatchesJsonArray()
    {
        Assert.True(ImportedAttributeValue.AreEquivalent(
            "другу, любимому, школа",
            """["другу","любимому","школа"]""",
            AttributeType.MultiSelect,
            optionsJson: null));
    }

    [Fact]
    public void Select_DictionaryIdMatchesLabel()
    {
        const string options = """[{"value":"901542","label":"Серый"}]""";

        var fromId = ImportedAttributeValue.ToStoredString("901542", AttributeType.Select, options);
        var fromLabel = ImportedAttributeValue.ToStoredString("Серый", AttributeType.Select, options);

        Assert.Equal("901542", fromId);
        Assert.Equal("901542", fromLabel);
        Assert.True(ImportedAttributeValue.AreEquivalent("Серый", "901542", AttributeType.Select, options));
        Assert.Equal("Серый", ImportedAttributeValue.ToDisplayString("901542", AttributeType.Select, options));
    }

    [Fact]
    public void Select_KeepsCustomValueOutsideDictionary()
    {
        const string options = """[{"value":"901542","label":"Серый"}]""";

        var stored = ImportedAttributeValue.ToStoredString("хаки", AttributeType.Select, options);

        Assert.Equal("хаки", stored);
        Assert.Equal("хаки", ImportedAttributeValue.ToDisplayString(stored, AttributeType.Select, options));
        Assert.False(ImportedAttributeValue.AreEquivalent("хаки", "901542", AttributeType.Select, options));
    }

    [Fact]
    public void MultiSelect_CanonicalizesIdsAndDisplaysLabels()
    {
        const string options = """[{"value":"d1","label":"другу"},{"value":"d2","label":"любимому"},{"value":"d3","label":"школа"}]""";

        var stored = ImportedAttributeValue.ToStoredString(
            new List<string> { "другу", "любимому", "школа" },
            AttributeType.MultiSelect,
            options);

        Assert.Equal("""["d1","d2","d3"]""", stored);
        Assert.True(ImportedAttributeValue.AreEquivalent(
            "другу, любимому, школа", stored, AttributeType.MultiSelect, options));
        Assert.Equal(
            "другу, любимому, школа",
            ImportedAttributeValue.ToDisplayString(stored, AttributeType.MultiSelect, options));
    }

    [Fact]
    public void Select_TakesFirstListValue()
    {
        Assert.Equal("cotton", ImportedAttributeValue.ToStoredString(
            new List<string> { "cotton" },
            AttributeType.Select));
    }

    [Fact]
    public void Collect_CopiesBrandOntoVendorAndBrandKeys()
    {
        var product = new ExternalProduct(
            "1",
            "SKU",
            "Name",
            Brand: "Acme",
            Attributes: new Dictionary<string, object> { ["color"] = "red" });

        var attrs = ImportedAttributeValue.Collect(product);

        Assert.Equal("red", attrs["color"]);
        Assert.Equal("Acme", attrs["brand"]);
        Assert.Equal("Acme", attrs["vendor"]);
        Assert.Equal("Acme", attrs["manufacturer"]);
    }

    [Fact]
    public void Collect_DoesNotOverwriteExistingBrandAttribute()
    {
        var product = new ExternalProduct(
            "1",
            "SKU",
            "Name",
            Brand: "FromCard",
            Attributes: new Dictionary<string, object> { ["brand"] = "FromAttr" });

        var attrs = ImportedAttributeValue.Collect(product);
        Assert.Equal("FromAttr", attrs["brand"]);
        Assert.Equal("FromCard", attrs["vendor"]);
    }
}
