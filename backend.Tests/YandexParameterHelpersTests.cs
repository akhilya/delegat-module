using Delegat.Marketplace.YandexMarket;
using Delegat.Marketplace.YandexMarket.Models;

namespace Delegat.Tests;

public class YandexParameterHelpersTests
{
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
                    new() { ParameterId = 17352854, ValueId = 25968950 },
                    new() { ParameterId = 17352854, ValueId = 36725530 }
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
                        new() { ParameterId = 10, ValueId = 20 }
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
}
