using System.Text.Json.Serialization;

namespace Delegat.Marketplace.YandexMarket.Models;

public class ApiResponse<T>
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "OK";

    [JsonPropertyName("result")]
    public T? Result { get; set; }

    [JsonPropertyName("errors")]
    public List<ApiErrorDto>? Errors { get; set; }
}

public class ApiErrorDto
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public class CampaignsResultDto
{
    [JsonPropertyName("campaigns")]
    public List<CampaignDto> Campaigns { get; set; } = new();
}

public class CampaignDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("domain")]
    public string? Domain { get; set; }

    [JsonPropertyName("placementType")]
    public string? PlacementType { get; set; }

    [JsonPropertyName("business")]
    public BusinessDto? Business { get; set; }
}

public class BusinessDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class AuthTokenResultDto
{
    [JsonPropertyName("apiKey")]
    public AuthTokenInfoDto? ApiKey { get; set; }
}

public class AuthTokenInfoDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("scopes")]
    public List<string>? Scopes { get; set; }
}