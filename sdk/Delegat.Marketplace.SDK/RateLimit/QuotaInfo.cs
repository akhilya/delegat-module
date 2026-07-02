namespace Delegat.Marketplace.SDK.RateLimit;

public record QuotaInfo(
    string Key,
    string DisplayName,
    long Limit,
    long Usage,
    DateTime? ResetsAt
);
