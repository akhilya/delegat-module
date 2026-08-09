using System.Text.Json.Serialization;

namespace Delegat.Services.PlatformLink;

public sealed class PlatformCurrencyDto
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }
}

public sealed class PlatformTimezoneDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";
}

public sealed class PlatformErrorReportDto
{
    [JsonPropertyName("instanceId")]
    public string InstanceId { get; set; } = "";

    [JsonPropertyName("occurredAt")]
    public DateTime OccurredAt { get; set; }

    [JsonPropertyName("statusCode")]
    public int StatusCode { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("method")]
    public string? Method { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("exceptionType")]
    public string? ExceptionType { get; set; }

    [JsonPropertyName("fingerprint")]
    public string? Fingerprint { get; set; }

    [JsonPropertyName("platformVersion")]
    public string? PlatformVersion { get; set; }
}

public sealed class PlatformHeartbeatDto
{
    [JsonPropertyName("instanceId")]
    public string InstanceId { get; set; } = "";

    [JsonPropertyName("occurredAt")]
    public DateTime OccurredAt { get; set; }

    [JsonPropertyName("platformVersion")]
    public string? PlatformVersion { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "ok";

    [JsonPropertyName("usage")]
    public PlatformUsageReportDto? Usage { get; set; }
}

public sealed class PlatformUsageReportDto
{
    [JsonPropertyName("products")]
    public long Products { get; set; }

    [JsonPropertyName("files")]
    public long Files { get; set; }

    [JsonPropertyName("storageBytes")]
    public long StorageBytes { get; set; }

    [JsonPropertyName("connections")]
    public long Connections { get; set; }

    [JsonPropertyName("isOverQuota")]
    public bool IsOverQuota { get; set; }

    [JsonPropertyName("stores")]
    public List<PlatformStoreUsageDto> Stores { get; set; } = new();
}

public sealed class PlatformStoreUsageDto
{
    [JsonPropertyName("storeId")]
    public int StoreId { get; set; }

    [JsonPropertyName("products")]
    public long Products { get; set; }

    [JsonPropertyName("files")]
    public long Files { get; set; }

    [JsonPropertyName("storageBytes")]
    public long StorageBytes { get; set; }

    [JsonPropertyName("connections")]
    public long Connections { get; set; }
}

public sealed class PlatformEntitlementsDto
{
    [JsonPropertyName("linked")]
    public bool Linked { get; set; } = true;

    [JsonPropertyName("planName")]
    public string? PlanName { get; set; }

    [JsonPropertyName("planSlug")]
    public string? PlanSlug { get; set; }

    [JsonPropertyName("subscriptionStatus")]
    public string? SubscriptionStatus { get; set; }

    [JsonPropertyName("overagePurgeAt")]
    public DateTimeOffset? OveragePurgeAt { get; set; }

    [JsonPropertyName("maxProducts")]
    public int? MaxProducts { get; set; }

    [JsonPropertyName("maxStorageGb")]
    public decimal? MaxStorageGb { get; set; }

    [JsonPropertyName("maxFiles")]
    public int? MaxFiles { get; set; }

    [JsonPropertyName("maxConnections")]
    public int? MaxConnections { get; set; }
}
