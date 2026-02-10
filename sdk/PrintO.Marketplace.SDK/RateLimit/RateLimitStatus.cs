namespace PrintO.Marketplace.SDK.RateLimit;

public record RateLimitStatus(
    int MaxRequestsPerSecond,
    int CurrentWindowRequests,
    long TotalRequestsSinceStart,
    List<QuotaInfo> Quotas
);
