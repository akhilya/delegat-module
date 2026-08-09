namespace Delegat.Services;

public sealed record QuotaLimitsDto(
    int? MaxProducts,
    long? MaxStorageBytes,
    decimal? MaxStorageGb,
    int? MaxFiles,
    int? MaxConnections);

public sealed record QuotaMetricDto(
    long Used,
    long? Limit,
    long? Remaining,
    bool IsOver);

public sealed record StoreQuotaUsageDto(
    int StoreId,
    string StoreName,
    QuotaMetricDto Products,
    QuotaMetricDto Files,
    QuotaMetricDto StorageBytes,
    QuotaMetricDto Connections,
    bool IsOverQuota);

public sealed record StoreFileQuotaDto(
    QuotaMetricDto Files,
    QuotaMetricDto StorageBytes);

public sealed record QuotaUsageSnapshotDto(
    QuotaLimitsDto Limits,
    IReadOnlyList<StoreQuotaUsageDto> Stores,
    QuotaMetricDto Products,
    QuotaMetricDto Files,
    QuotaMetricDto StorageBytes,
    QuotaMetricDto Connections,
    bool IsOverQuota,
    bool PlatformLinked,
    string? PlanName,
    DateTimeOffset? OveragePurgeAt,
    bool OverageActive);

public sealed record OverageTrimItemDto(
    string Kind,
    int Id,
    int StoreId,
    string StoreName,
    string Label,
    long? Bytes,
    bool IsTrashed);

public sealed record OverageTrimPreviewDto(
    bool IsOverQuota,
    DateTimeOffset? OveragePurgeAt,
    bool CanAutoTrim,
    IReadOnlyList<OverageTrimItemDto> Products,
    IReadOnlyList<OverageTrimItemDto> Connections,
    IReadOnlyList<OverageTrimItemDto> Files,
    int ProductCount,
    int ConnectionCount,
    int FileCount,
    long FileBytes);

public sealed record OverageTrimResultDto(
    int ProductsRemoved,
    int ConnectionsRemoved,
    int FilesRemoved,
    long BytesFreed);
