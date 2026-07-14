using System.Text.Json.Serialization;

namespace Delegat.Marketplace.YandexMarket.Models;

public class GetOfferMappingsRequest
{
    [JsonPropertyName("offerIds")]
    public List<string>? OfferIds { get; set; }

    [JsonPropertyName("cardStatuses")]
    public List<string>? CardStatuses { get; set; }
}

public class OfferMappingsResultDto
{
    [JsonPropertyName("offerMappings")]
    public List<GetOfferMappingDto>? OfferMappings { get; set; }

    [JsonPropertyName("paging")]
    public ForwardPagingDto? Paging { get; set; }
}

public class ForwardPagingDto
{
    [JsonPropertyName("nextPageToken")]
    public string? NextPageToken { get; set; }
}

public class GetOfferMappingDto
{
    [JsonPropertyName("offer")]
    public GetOfferDto? Offer { get; set; }

    [JsonPropertyName("mapping")]
    public GetMappingDto? Mapping { get; set; }
}

public class GetOfferDto
{
    [JsonPropertyName("offerId")]
    public string OfferId { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("vendor")]
    public string? Vendor { get; set; }

    [JsonPropertyName("barcodes")]
    public List<string>? Barcodes { get; set; }

    [JsonPropertyName("pictures")]
    public List<string>? Pictures { get; set; }

    [JsonPropertyName("groupId")]
    public string? GroupId { get; set; }

    [JsonPropertyName("marketCategoryId")]
    public long? MarketCategoryId { get; set; }

    [JsonPropertyName("parameterValues")]
    public List<ParameterValueDto>? ParameterValues { get; set; }

    [JsonPropertyName("weightDimensions")]
    public WeightDimensionsDto? WeightDimensions { get; set; }

    [JsonPropertyName("basicPrice")]
    public PriceWithDiscountDto? BasicPrice { get; set; }
}

public class GetMappingDto
{
    [JsonPropertyName("marketSku")]
    public long? MarketSku { get; set; }

    [JsonPropertyName("marketSkuName")]
    public string? MarketSkuName { get; set; }
}

public class ParameterValueDto
{
    [JsonPropertyName("parameterId")]
    public long ParameterId { get; set; }

    [JsonPropertyName("valueId")]
    public long? ValueId { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("unitId")]
    public long? UnitId { get; set; }
}

public class WeightDimensionsDto
{
    [JsonPropertyName("length")]
    public decimal? Length { get; set; }

    [JsonPropertyName("width")]
    public decimal? Width { get; set; }

    [JsonPropertyName("height")]
    public decimal? Height { get; set; }

    [JsonPropertyName("weight")]
    public decimal? Weight { get; set; }
}

public class PriceWithDiscountDto
{
    [JsonPropertyName("value")]
    public decimal? Value { get; set; }

    [JsonPropertyName("discountBase")]
    public decimal? DiscountBase { get; set; }

    [JsonPropertyName("currencyId")]
    public string? CurrencyId { get; set; }
}

public class UpdateOfferMappingsRequest
{
    [JsonPropertyName("offerMappings")]
    public List<UpdateOfferMappingDto> OfferMappings { get; set; } = new();
}

public class UpdateOfferMappingDto
{
    [JsonPropertyName("offer")]
    public UpdateOfferDto Offer { get; set; } = new();

    [JsonPropertyName("mapping")]
    public UpdateMappingDto? Mapping { get; set; }
}

public class UpdateOfferDto
{
    [JsonPropertyName("offerId")]
    public string OfferId { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("vendor")]
    public string? Vendor { get; set; }

    [JsonPropertyName("barcodes")]
    public List<string>? Barcodes { get; set; }

    [JsonPropertyName("pictures")]
    public List<string>? Pictures { get; set; }

    [JsonPropertyName("groupId")]
    public string? GroupId { get; set; }

    [JsonPropertyName("marketCategoryId")]
    public long? MarketCategoryId { get; set; }

    [JsonPropertyName("parameterValues")]
    public List<ParameterValueDto>? ParameterValues { get; set; }

    [JsonPropertyName("weightDimensions")]
    public WeightDimensionsDto? WeightDimensions { get; set; }

    [JsonPropertyName("basicPrice")]
    public PriceWithDiscountDto? BasicPrice { get; set; }
}

public class UpdateMappingDto
{
    [JsonPropertyName("marketSku")]
    public long? MarketSku { get; set; }
}

public class UpdateOfferMappingsResultDto
{
    [JsonPropertyName("offerMappings")]
    public List<UpdateOfferMappingResultDto>? OfferMappings { get; set; }
}

public class UpdateOfferMappingResultDto
{
    [JsonPropertyName("offerId")]
    public string? OfferId { get; set; }

    [JsonPropertyName("errors")]
    public List<OfferMappingErrorDto>? Errors { get; set; }
}

public class OfferMappingErrorDto
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("parameterId")]
    public long? ParameterId { get; set; }
}

public class ArchiveOffersRequest
{
    [JsonPropertyName("offerIds")]
    public List<string> OfferIds { get; set; } = new();
}