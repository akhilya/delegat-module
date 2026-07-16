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
    }

    [Fact]
    public void ResolveParameterGroup_Filtering_UsesFilterSection()
    {
        var param = new CategoryParameterDto
        {
            Id = 17352854,
            Name = "Материал",
            Filtering = true,
            RecommendationTypes = new List<string> { "ADDITIONAL", "FILTERABLE" }
        };

        Assert.Equal("Доп. характеристики для фильтров", YandexParameterHelpers.ResolveParameterGroup(param));
    }

    [Fact]
    public void ResolveParameterGroup_Main_UsesMainSection()
    {
        var param = new CategoryParameterDto
        {
            Id = 1,
            Name = "Цвет",
            Filtering = false,
            RecommendationTypes = new List<string> { "MAIN" }
        };

        Assert.Equal("Основные характеристики", YandexParameterHelpers.ResolveParameterGroup(param));
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
                    new() { ParameterId = 17352854, ValueId = 25968950, Value = "Пластик" },
                    new() { ParameterId = 17352854, ValueId = 36725530, Value = "Смола" }
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
        YandexParameterHelpers.MergeParameterAttribute(attrs, 200, null, "Солдатики");
        Assert.Equal("Солдатики", attrs["200"]);
    }

    [Fact]
    public void BuildEnumParameterValue_IdLabel_IncludesBothFields()
    {
        var entry = YandexParameterHelpers.BuildEnumParameterValue(
            17352854, "25968950:Пластик", out var warning);
        Assert.Null(warning);
        Assert.NotNull(entry);
        Assert.Equal(17352854, entry!.ParameterId);
        Assert.Equal(25968950, entry.ValueId);
        Assert.Equal("Пластик", entry.Value);

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
                new() { Id = 14805994, Value = "Вариант A" }
            }
        });

        var entry = YandexParameterHelpers.BuildEnumParameterValue(
            14805991, "14805994", out var warning);
        Assert.Null(warning);
        Assert.NotNull(entry);
        Assert.Equal("Вариант A", entry!.Value);
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
