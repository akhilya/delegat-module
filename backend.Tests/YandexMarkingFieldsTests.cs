using System.Text.Json;
using System.Text.Json.Serialization;
using Delegat.Marketplace.SDK;
using Delegat.Marketplace.YandexMarket;
using Delegat.Marketplace.YandexMarket.Models;

namespace Delegat.Tests;

/// <summary>
/// TN VED / IKPU commodity codes and CIS_REQUIRED cargo type on Yandex offer mappings.
/// </summary>
public class YandexMarkingFieldsTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void BuildCommodityCodes_BothTypes_ReturnsCustomsThenIkpu()
    {
        var codes = YandexParameterHelpers.BuildCommodityCodes("8517610008", "12345678901234567");

        Assert.NotNull(codes);
        Assert.Equal(2, codes!.Count);
        Assert.Equal("8517610008", codes[0].Code);
        Assert.Equal(YandexParameterHelpers.CustomsCommodityCodeType, codes[0].Type);
        Assert.Equal("12345678901234567", codes[1].Code);
        Assert.Equal(YandexParameterHelpers.IkpuCodeType, codes[1].Type);
    }

    [Fact]
    public void BuildCommodityCodes_WhitespaceOnly_ReturnsNull()
    {
        Assert.Null(YandexParameterHelpers.BuildCommodityCodes("  ", null));
        Assert.Null(YandexParameterHelpers.BuildCommodityCodes(null, "   "));
    }

    [Fact]
    public void ApplyMarkingFields_MergesBothCodes_AndSetsCargoTypesOnlyWhenTrue()
    {
        var offer = new UpdateOfferDto { OfferId = "SKU-1" };

        YandexParameterHelpers.ApplyMarkingFields(
            offer,
            commodityCode: "8517610008",
            ikpuCode: "12345678901234567",
            cisRequired: true);

        Assert.NotNull(offer.CommodityCodes);
        Assert.Equal(2, offer.CommodityCodes!.Count);
        Assert.Equal(
            new[] { "CUSTOMS_COMMODITY_CODE", "IKPU_CODE" },
            offer.CommodityCodes.Select(c => c.Type).ToArray());
        Assert.Equal(new[] { YandexParameterHelpers.CisRequiredCargoType }, offer.CargoTypes);

        var json = JsonSerializer.Serialize(offer, JsonOpts);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var codes = root.GetProperty("commodityCodes").EnumerateArray().ToList();
        Assert.Equal(2, codes.Count);
        Assert.Equal("8517610008", codes[0].GetProperty("code").GetString());
        Assert.Equal("CUSTOMS_COMMODITY_CODE", codes[0].GetProperty("type").GetString());
        Assert.Equal("IKPU_CODE", codes[1].GetProperty("type").GetString());
        Assert.Equal(
            new[] { "CIS_REQUIRED" },
            root.GetProperty("cargoTypes").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Fact]
    public void ApplyMarkingFields_CisRequiredFalseOrNull_OmitsCargoTypes()
    {
        var whenFalse = new UpdateOfferDto { OfferId = "SKU-1" };
        YandexParameterHelpers.ApplyMarkingFields(whenFalse, "8517610008", null, cisRequired: false);
        Assert.Null(whenFalse.CargoTypes);
        Assert.Single(whenFalse.CommodityCodes!);

        var jsonFalse = JsonSerializer.Serialize(whenFalse, JsonOpts);
        using (var doc = JsonDocument.Parse(jsonFalse))
        {
            Assert.False(doc.RootElement.TryGetProperty("cargoTypes", out _));
        }

        var whenNull = new UpdateOfferDto { OfferId = "SKU-2" };
        YandexParameterHelpers.ApplyMarkingFields(whenNull, null, null, cisRequired: null);
        Assert.Null(whenNull.CargoTypes);
        Assert.Null(whenNull.CommodityCodes);

        var jsonNull = JsonSerializer.Serialize(whenNull, JsonOpts);
        using (var doc = JsonDocument.Parse(jsonNull))
        {
            Assert.False(doc.RootElement.TryGetProperty("cargoTypes", out _));
            Assert.False(doc.RootElement.TryGetProperty("commodityCodes", out _));
        }
    }

    [Fact]
    public void MergeMarkingAttributes_SplitsCodesAndCisFlag()
    {
        var attrs = new Dictionary<string, object>();

        YandexParameterHelpers.MergeMarkingAttributes(
            attrs,
            new List<CommodityCodeDto>
            {
                new() { Code = "8517610008", Type = "CUSTOMS_COMMODITY_CODE" },
                new() { Code = "12345678901234567", Type = "IKPU_CODE" }
            },
            new List<string> { "CIS_REQUIRED" });

        Assert.Equal("8517610008", attrs["commodityCode"]);
        Assert.Equal("12345678901234567", attrs["ikpuCode"]);
        Assert.Equal(true, attrs["cisRequired"]);
    }

    [Fact]
    public void MergeMarkingAttributes_WithoutCis_DoesNotSetFlag()
    {
        var attrs = new Dictionary<string, object>();

        YandexParameterHelpers.MergeMarkingAttributes(
            attrs,
            new List<CommodityCodeDto>
            {
                new() { Code = "8517610008", Type = "CUSTOMS_COMMODITY_CODE" }
            },
            cargoTypes: null);

        Assert.Equal("8517610008", attrs["commodityCode"]);
        Assert.False(attrs.ContainsKey("ikpuCode"));
        Assert.False(attrs.ContainsKey("cisRequired"));
    }

    [Fact]
    public async Task GetCategoryFields_ExposesLabelingFields()
    {
        var plugin = new YandexMarketPlugin();
        var fields = await plugin.GetCategoryFieldsAsync(new ConnectionContext(), "0");
        var byKey = fields.ToDictionary(f => f.Key, StringComparer.Ordinal);

        Assert.Equal("Labeling", byKey["commodityCode"].Group);
        Assert.Equal("Labeling", byKey["ikpuCode"].Group);
        Assert.Equal("Labeling", byKey["cisRequired"].Group);
        Assert.Equal("boolean", byKey["cisRequired"].Type);
        Assert.Equal(17, byKey["ikpuCode"].MaxLength);
    }
}
