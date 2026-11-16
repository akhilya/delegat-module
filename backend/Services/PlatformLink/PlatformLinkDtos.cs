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

public sealed class PlatformMarketplaceDocDto
{
    [JsonPropertyName("adapter")]
    public string Adapter { get; set; } = "";

    [JsonPropertyName("sourceUrl")]
    public string SourceUrl { get; set; } = "";

    [JsonPropertyName("fetchedAt")]
    public DateTimeOffset FetchedAt { get; set; }

    [JsonPropertyName("excerpt")]
    public string Excerpt { get; set; } = "";
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

public sealed class PlatformHostedAiConfigDto
{
    [JsonPropertyName("configured")]
    public bool Configured { get; set; }

    [JsonPropertyName("entitled")]
    public bool Entitled { get; set; }

    [JsonPropertyName("available")]
    public bool Available { get; set; }

    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    [JsonPropertyName("modelId")]
    public string? ModelId { get; set; }

    [JsonPropertyName("apiUrl")]
    public string? ApiUrl { get; set; }

    [JsonPropertyName("maxOutputTokens")]
    public int MaxOutputTokens { get; set; }

    [JsonPropertyName("quotaTokens")]
    public long QuotaTokens { get; set; }

    [JsonPropertyName("usedTokens")]
    public long UsedTokens { get; set; }

    [JsonPropertyName("reservedTokens")]
    public long ReservedTokens { get; set; }

    [JsonPropertyName("remainingTokens")]
    public long RemainingTokens { get; set; }

    [JsonPropertyName("periodStart")]
    public DateTimeOffset PeriodStart { get; set; }

    [JsonPropertyName("periodEnd")]
    public DateTimeOffset PeriodEnd { get; set; }

    [JsonPropertyName("usageUnavailable")]
    public bool UsageUnavailable { get; set; }
}

public sealed class PlatformOzonGrantClaimDto
{
    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("accessTokenExpiresAt")]
    public DateTimeOffset AccessTokenExpiresAt { get; set; }

    [JsonPropertyName("sellerId")]
    public string? SellerId { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("storeHint")]
    public int? StoreHint { get; set; }
}

public sealed record PlatformOzonTokenDto(
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("refreshToken")] string? RefreshToken,
    [property: JsonPropertyName("expiresIn")] int ExpiresIn);

/// <summary>Result of the CP refresh proxy; Error carries Ozon's error code on failure.</summary>
public sealed record PlatformOzonRefreshResult(
    bool Success,
    string? Error,
    string? AccessToken,
    string? RefreshToken,
    int ExpiresIn);

/// <summary>
/// Identity the Control Plane hands back for a redeemed console SSO code:
/// the platform account the cabinet sign-in should map to.
/// </summary>
public sealed class PlatformInstanceSsoDto
{
    [JsonPropertyName("email")]
    public string? Email { get; set; }
}
