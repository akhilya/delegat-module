using System.Text.Json.Serialization;

namespace PrintO.Marketplace.Ozon.Models;

/// <summary>
/// Response from POST /v4/product/info/limit
/// </summary>
public class UploadQuotaResponse
{
    [JsonPropertyName("daily_create")]
    public QuotaDetail DailyCreate { get; set; } = new();

    [JsonPropertyName("daily_update")]
    public QuotaDetail DailyUpdate { get; set; } = new();

    [JsonPropertyName("total")]
    public QuotaDetail Total { get; set; } = new();
}

public class QuotaDetail
{
    [JsonPropertyName("limit")]
    public long Limit { get; set; }

    [JsonPropertyName("usage")]
    public long Usage { get; set; }

    [JsonPropertyName("reset_at")]
    public DateTime? ResetAt { get; set; }
}
