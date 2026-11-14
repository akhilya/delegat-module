using System.Text.Json;
using Delegat.Marketplace.Wildberries;

namespace Delegat.Tests;

public class WildberriesCharacteristicValueTests
{
    [Theory]
    [InlineData("select")]
    [InlineData("multiselect")]
    [InlineData("string")]
    [InlineData("array")]
    [InlineData(null)]
    public void StoredJsonArrayBecomesIndividualColorsWithoutToText(string? fieldType)
    {
        const string key = "charc:14177449";
        // Default serializer escapes Cyrillic, matching stored PIM values.
        var data = new Dictionary<string, object> { [key] = JsonSerializer.Serialize(new[] { "черный", "белый" }) };
        if (fieldType != null) data["_fieldTypes"] = new Dictionary<string, string> { [key] = fieldType };
        var characteristic = Assert.Single(WildberriesPlugin.BuildCharacteristics(data)!);
        Assert.Equal(new[] { "черный", "белый" }, Assert.IsAssignableFrom<IEnumerable<string>>(characteristic.Value));
    }

    [Fact]
    public void JsonNumericDirectoryValuesStayStrings()
    {
        var data = new Dictionary<string, object>
        {
            ["charc:15000001"] = JsonSerializer.SerializeToElement(new[] { 9503003500L }),
            ["_fieldTypes"] = new Dictionary<string, string> { ["charc:15000001"] = "select" }
        };
        var characteristic = Assert.Single(WildberriesPlugin.BuildCharacteristics(data)!);
        Assert.Equal(new[] { "9503003500" }, Assert.IsAssignableFrom<IEnumerable<string>>(characteristic.Value));
    }
}
