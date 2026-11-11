using System.Text.Json;
using System.Text.Json.Serialization;
using Delegat.Marketplace.YandexMarket;
using Delegat.Marketplace.YandexMarket.Models;

namespace Delegat.Tests;

public class YandexParameterHelpersTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public YandexParameterHelpersTests()
    {
        YandexParameterHelpers.ParameterValueLabels.Clear();
        YandexParameterHelpers.ParameterRestrictions.Clear();
        YandexParameterHelpers.FilteringParameterIds.Clear();
        YandexParameterHelpers.AllowCustomValueParameterIds.Clear();
    }

    [Fact]
    public void ResolveParameterGroup_Filtering_UsesFilterSection()
    {
        var param = new CategoryParameterDto
        {
            Id = 17352854,
            Name = "Material",
            Filtering = true,
            RecommendationTypes = new List<string> { "ADDITIONAL", "FILTERABLE" }
        };

        Assert.Equal("Additional filter characteristics", YandexParameterHelpers.ResolveParameterGroup(param));
    }

    [Fact]
    public void ResolveParameterGroup_Main_UsesMainSection()
    {
        var param = new CategoryParameterDto
        {
            Id = 1,
            Name = "Color",
            Filtering = false,
            RecommendationTypes = new List<string> { "MAIN" }
        };

        Assert.Equal("Main characteristics", YandexParameterHelpers.ResolveParameterGroup(param));
    }

    [Fact]
    public void BuildOfferContent_RequiresParameterValuesAndCategory()
    {
        var withParams = new UpdateOfferMappingDto
        {
            Offer = new UpdateOfferDto
            {
                OfferId = "SO8",
                MarketCategoryId = 79461128,
                ParameterValues = new List<ParameterValueDto>
                {
                    new() { ParameterId = 17352854, ValueId = 25968950, Value = "Plastic" },
                    new() { ParameterId = 17352854, ValueId = 36725530, Value = "Resin" }
                }
            }
        };

        var content = YandexParameterHelpers.BuildOfferContent(withParams);
        Assert.NotNull(content);
        Assert.Equal("SO8", content!.OfferId);
        Assert.Equal(79461128, content.CategoryId);
        Assert.Equal(2, content.ParameterValues!.Count);

        var withoutCategory = new UpdateOfferMappingDto
        {
            Offer = new UpdateOfferDto
            {
                OfferId = "SO8",
                ParameterValues = withParams.Offer.ParameterValues
            }
        };
        Assert.Null(YandexParameterHelpers.BuildOfferContent(withoutCategory));

        var withoutParams = new UpdateOfferMappingDto
        {
            Offer = new UpdateOfferDto
            {
                OfferId = "SO8",
                MarketCategoryId = 79461128
            }
        };
        Assert.Null(YandexParameterHelpers.BuildOfferContent(withoutParams));
    }

    [Fact]
    public void BuildOfferContents_SkipsOffersWithoutCategoryParams()
    {
        var mappings = new List<UpdateOfferMappingDto>
        {
            new()
            {
                Offer = new UpdateOfferDto
                {
                    OfferId = "A",
                    MarketCategoryId = 1,
                    ParameterValues = new List<ParameterValueDto>
                    {
                        new() { ParameterId = 10, ValueId = 20, Value = "X" }
                    }
                }
            },
            new()
            {
                Offer = new UpdateOfferDto { OfferId = "B", MarketCategoryId = 1 }
            }
        };

        var contents = YandexParameterHelpers.BuildOfferContents(mappings);
        Assert.Single(contents);
        Assert.Equal("A", contents[0].OfferId);
    }

    [Fact]
    public void MergeParameterAttribute_MultiselectAccumulatesValueIds()
    {
        var attrs = new Dictionary<string, object>();
        YandexParameterHelpers.MergeParameterAttribute(attrs, 17352854, 25968950, null);
        YandexParameterHelpers.MergeParameterAttribute(attrs, 17352854, 36725530, null);
        YandexParameterHelpers.MergeParameterAttribute(attrs, 17352854, 25968950, null); // dup

        Assert.True(attrs.TryGetValue("17352854", out var raw));
        var list = Assert.IsType<List<object>>(raw);
        Assert.Equal(2, list.Count);
        Assert.Contains(25968950L, list.Cast<long>());
        Assert.Contains(36725530L, list.Cast<long>());
    }

    [Fact]
    public void MergeParameterAttribute_SingleValueStaysScalar()
    {
        var attrs = new Dictionary<string, object>();
        YandexParameterHelpers.MergeParameterAttribute(attrs, 200, null, "Soldiers");
        Assert.Equal("Soldiers", attrs["200"]);
    }

    [Fact]
    public void BuildEnumParameterValue_IdLabel_IncludesBothFields()
    {
        var entry = YandexParameterHelpers.BuildEnumParameterValue(
            17352854, "25968950:Plastic", out var warning);
        Assert.Null(warning);
        Assert.NotNull(entry);
        Assert.Equal(17352854, entry!.ParameterId);
        Assert.Equal(25968950, entry.ValueId);
        Assert.Equal("Plastic", entry.Value);

        var json = JsonSerializer.Serialize(entry, JsonOpts);
        Assert.Contains("\"valueId\":25968950", json);
        Assert.Contains("\"value\":", json);
        Assert.DoesNotContain("\"value\":null", json);
    }

    [Fact]
    public void BuildEnumParameterValue_UsesLabelCache()
    {
        YandexParameterHelpers.CacheParameterMetadata(new CategoryParameterDto
        {
            Id = 14805991,
            Filtering = true,
            Values = new List<ParameterValueOptionDto>
            {
                new() { Id = 14805994, Value = "Variant A" }
            }
        });

        var entry = YandexParameterHelpers.BuildEnumParameterValue(
            14805991, "14805994", out var warning);
        Assert.Null(warning);
        Assert.NotNull(entry);
        Assert.Equal("Variant A", entry!.Value);
    }

    [Fact]
    public void BuildEnumParameterValue_MissingLabel_ReturnsNullWithWarning()
    {
        var entry = YandexParameterHelpers.BuildEnumParameterValue(
            27141351, "28699195", out var warning);
        Assert.Null(entry);
        Assert.Contains("27141351", warning);
        Assert.Contains("28699195", warning);
    }

    [Fact]
    public void ResolveMappedParameterValue_SelectFreeText_SendsValueWithoutValueId()
    {
        // Manufacturer color name / allowCustomValues: free text must not be silently dropped
        // even when the graph field type is select (and the value contains ':').
        var entry = YandexParameterHelpers.ResolveMappedParameterValue(
            14871214,
            "Red Dead Redemption 2 : POP Arthur",
            "select",
            out var warning);

        Assert.Null(warning);
        Assert.NotNull(entry);
        Assert.Equal(14871214, entry!.ParameterId);
        Assert.Null(entry.ValueId);
        Assert.Equal("Red Dead Redemption 2 : POP Arthur", entry.Value);
    }

    [Fact]
    public void ResolveMappedParameterValue_SelectDictionaryId_KeepsValueIdAndValue()
    {
        var entry = YandexParameterHelpers.ResolveMappedParameterValue(
            13887626, "13898641:gray", "select", out var warning);

        Assert.Null(warning);
        Assert.NotNull(entry);
        Assert.Equal(13898641, entry!.ValueId);
        Assert.Equal("gray", entry.Value);
    }

    [Fact]
    public void ResolveMappedParameterValue_StringType_SendsPlainValue()
    {
        var entry = YandexParameterHelpers.ResolveMappedParameterValue(
            14871214, "gray", "string", out var warning);

        Assert.Null(warning);
        Assert.NotNull(entry);
        Assert.Null(entry!.ValueId);
        Assert.Equal("gray", entry.Value);
    }

    [Fact]
    public void BuildUpdateOfferMapping_UsesGraphUnitIdAndSendsOriginalNumber()
    {
        var productData = new Dictionary<string, object>
        {
            ["sku"] = "unit-test-sku",
            ["name"] = "Unit test product",
            ["1001"] = "12.75",
            ["_fieldTypes"] = new Dictionary<string, string> { ["1001"] = "number" },
            ["_fieldUnitIds"] = new Dictionary<string, string> { ["1001"] = "7002" },
        };

        var mapping = YandexMarketPlugin.BuildUpdateOfferMapping(productData, null, out var warnings);

        var parameter = Assert.Single(mapping.Offer.ParameterValues!);
        Assert.Equal(1001, parameter.ParameterId);
        Assert.Equal("12.75", parameter.Value);
        Assert.Equal(7002, parameter.UnitId);
        Assert.Empty(warnings);
        Assert.DoesNotContain("_fieldUnitIds", productData.Keys);
    }

    [Fact]
    public void BuildUpdateOfferMapping_MapsProvidedDurableVideosWithoutMixingThemIntoPictures()
    {
        var mapping = YandexMarketPlugin.BuildUpdateOfferMapping(new Dictionary<string, object>
        {
            ["sku"] = "video-disabled-sku",
            ["name"] = "Video test product",
            ["videos"] = new List<string> { "https://media.example.test/api/pub/media-export/0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" },
        }, null, out _);

        Assert.Single(mapping.Offer.Videos!);
        Assert.Empty(mapping.Offer.Pictures ?? []);
    }

    [Fact]
    public void ResolveMappedParameterValue_SelectMissingLabel_ReturnsNullWithWarning()
    {
        var entry = YandexParameterHelpers.ResolveMappedParameterValue(
            27141351, "28699195", "select", out var warning);

        Assert.Null(entry);
        Assert.Contains("27141351", warning);
        Assert.Contains("28699195", warning);
    }

    [Fact]
    public void ApplyValueRestrictions_DropsDisallowedValueId()
    {
        YandexParameterHelpers.ParameterRestrictions[100] = new List<ValueRestrictionDto>
        {
            new()
            {
                LimitingParameterId = 50,
                LimitedValues = new List<OptionValuesLimitedDto>
                {
                    new()
                    {
                        LimitingOptionValueId = 1,
                        OptionValueIds = new List<long> { 10, 11 }
                    }
                }
            }
        };

        var values = new List<ParameterValueDto>
        {
            new() { ParameterId = 50, ValueId = 1, Value = "Lim" },
            new() { ParameterId = 100, ValueId = 10, Value = "Ok" },
            new() { ParameterId = 100, ValueId = 99, Value = "Bad" }
        };

        var filtered = YandexParameterHelpers.ApplyValueRestrictions(values, out var warnings);
        Assert.Equal(2, filtered.Count);
        Assert.DoesNotContain(filtered, v => v.ValueId == 99);
        Assert.Contains(warnings, w => w.Contains("99"));
    }

    [Fact]
    public void FormatApiIssue_IncludesParameterId()
    {
        var msg = YandexParameterHelpers.FormatApiIssue(new OfferMappingErrorDto
        {
            ParameterId = 17352891,
            Message = "invalid value"
        });
        Assert.Equal("[parameterId=17352891] invalid value", msg);
    }

    [Fact]
    public void MissingFilteringParameters_ReportsAbsentIds()
    {
        YandexParameterHelpers.FilteringParameterIds[17352854] = 0;
        YandexParameterHelpers.FilteringParameterIds[14805991] = 0;

        var sent = new List<ParameterValueDto>
        {
            new() { ParameterId = 17352854, ValueId = 1, Value = "A" },
            new() { ParameterId = 14805991, ValueId = 2, Value = "B" },
            new() { ParameterId = 200, Value = "text" }
        };
        var received = new List<ParameterValueDto>
        {
            new() { ParameterId = 17352854, ValueId = 1, Value = "A" }
        };

        var missing = YandexParameterHelpers.MissingFilteringParameters(sent, received);
        Assert.Equal(new List<long> { 14805991 }, missing);
    }
}
