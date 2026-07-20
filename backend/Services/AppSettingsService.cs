using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using System.Globalization;

namespace Delegat.Services;

public class AppSettingsService
{
    private readonly DataContext _context;

    private static readonly IReadOnlyList<AppSettingDefinition> Definitions =
    [
        new(
            AppSettingKeys.MaxProducts,
            "Product limit",
            "integer",
            "Maximum number of products in a single store. Empty - no limit or the value from MAX_PRODUCTS.",
            () => Environment.GetEnvironmentVariable("MAX_PRODUCTS")),
        new(
            AppSettingKeys.MaxStorageGb,
            "Storage limit, GB",
            "decimal",
            "Maximum total size of store and product files. Empty - no limit or the value from MAX_STORAGE_GB.",
            () => Environment.GetEnvironmentVariable("MAX_STORAGE_GB")),
        new(
            AppSettingKeys.MaxFiles,
            "File limit",
            "integer",
            "Maximum number of store and product files. Empty - no limit or the value from MAX_FILES.",
            () => Environment.GetEnvironmentVariable("MAX_FILES")),
        new(
            AppSettingKeys.MaxConnections,
            "Connection limit",
            "integer",
            "Maximum number of active adapters per store. Empty - no limit or the value from MAX_CONNECTIONS.",
            () => Environment.GetEnvironmentVariable("MAX_CONNECTIONS")),
        new(
            AppSettingKeys.GlobalRateLimit,
            "Global rate limit",
            "integer",
            "Number of requests per minute per IP. The current middleware applies the value after the app restarts.",
            () => "300"),
        new(
            AppSettingKeys.AuthRateLimit,
            "Auth rate limit",
            "integer",
            "Number of auth requests per minute per IP. The current middleware applies the value after the app restarts.",
            () => "10"),
        new(
            AppSettingKeys.AdapterHttpLogRetentionDays,
            "Adapter log retention, days",
            "integer",
            "How many days to keep outgoing adapter HTTP logs (1-30). Defaults to 7.",
            () => "7"),
    ];

    public AppSettingsService(DataContext context)
    {
        _context = context;
    }

    public async Task<List<AppSettingDto>> GetSettingsAsync(CancellationToken ct = default)
    {
        await EnsureDefaultsAsync(ct);

        var settings = await _context.appSettings
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .ToListAsync(ct);

        return settings
            .Select(setting =>
            {
                var definition = GetDefinition(setting.key);
                var fallback = definition?.fallbackValue() ?? null;
                return new AppSettingDto(
                    setting.key,
                    setting.label,
                    setting.value,
                    string.IsNullOrWhiteSpace(setting.value) ? fallback : setting.value,
                    setting.valueType,
                    setting.description,
                    setting.isEditable,
                    fallback,
                    setting.updatedAt);
            })
            .ToList();
    }

    public async Task<AppSettingDto> UpdateSettingAsync(string key, string? value, int? userId, CancellationToken ct = default)
    {
        await EnsureDefaultsAsync(ct);

        var setting = await _context.appSettings.FirstOrDefaultAsync(s => s.key == key, ct);
        if (setting == null)
            throw ApiException.NotFound("Setting not found");

        if (!setting.isEditable)
            throw ApiException.Forbidden("Setting cannot be edited");

        var normalizedValue = NormalizeValue(setting, value);
        setting.value = normalizedValue;
        setting.updatedAt = DateTime.UtcNow;
        setting.updatedByUserId = userId;

        await _context.SaveChangesAsync(ct);

        var definition = GetDefinition(setting.key);
        var fallback = definition?.fallbackValue() ?? null;
        return new AppSettingDto(
            setting.key,
            setting.label,
            setting.value,
            string.IsNullOrWhiteSpace(setting.value) ? fallback : setting.value,
            setting.valueType,
            setting.description,
            setting.isEditable,
            fallback,
            setting.updatedAt);
    }

    public async Task<int?> GetPositiveIntAsync(string key, CancellationToken ct = default)
    {
        var value = await GetEffectiveValueAsync(key, ct);
        return int.TryParse(value, out var number) && number > 0 ? number : null;
    }

    public async Task<long?> GetPositiveGigabytesAsBytesAsync(string key, CancellationToken ct = default)
    {
        var value = await GetEffectiveValueAsync(key, ct);
        return TryParsePositiveDecimal(value, out var gigabytes)
            ? (long)(gigabytes * 1024 * 1024 * 1024)
            : null;
    }

    private async Task<string?> GetEffectiveValueAsync(string key, CancellationToken ct)
    {
        await EnsureDefaultsAsync(ct);

        var settingValue = await _context.appSettings
            .AsNoTracking()
            .Where(s => s.key == key)
            .Select(s => s.value)
            .FirstOrDefaultAsync(ct);

        if (!string.IsNullOrWhiteSpace(settingValue))
            return settingValue;

        return GetDefinition(key)?.fallbackValue();
    }

    private async Task EnsureDefaultsAsync(CancellationToken ct)
    {
        var existingKeys = await _context.appSettings
            .Select(s => s.key)
            .ToListAsync(ct);

        var existing = existingKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasChanges = false;

        foreach (var definition in Definitions)
        {
            if (existing.Contains(definition.key)) continue;

            _context.appSettings.Add(new AppSetting
            {
                key = definition.key,
                label = definition.label,
                value = null,
                valueType = definition.valueType,
                description = definition.description,
                isEditable = true,
                updatedAt = DateTime.UtcNow
            });
            hasChanges = true;
        }

        if (hasChanges)
            await _context.SaveChangesAsync(ct);
    }

    private static string? NormalizeValue(AppSetting setting, string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        if (setting.valueType == "integer")
        {
            if (!int.TryParse(trimmed, out var integer) || integer <= 0)
                throw ApiException.BadRequest("Value must be a positive integer");

            return integer.ToString();
        }

        if (setting.valueType == "decimal")
        {
            if (!TryParsePositiveDecimal(trimmed, out var number))
                throw ApiException.BadRequest("Value must be a positive number");

            return number.ToString(CultureInfo.InvariantCulture);
        }

        return trimmed;
    }

    private static AppSettingDefinition? GetDefinition(string key) =>
        Definitions.FirstOrDefault(d => d.key.Equals(key, StringComparison.OrdinalIgnoreCase));

    private static bool TryParsePositiveDecimal(string? value, out double number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var normalized = value.Trim().Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out number) && number > 0;
    }

    private record AppSettingDefinition(
        string key,
        string label,
        string valueType,
        string description,
        Func<string?> fallbackValue);
}

public static class AppSettingKeys
{
    public const string MaxProducts = "MAX_PRODUCTS";
    public const string MaxStorageGb = "MAX_STORAGE_GB";
    public const string MaxFiles = "MAX_FILES";
    public const string MaxConnections = "MAX_CONNECTIONS";
    public const string GlobalRateLimit = "GLOBAL_RATE_LIMIT";
    public const string AuthRateLimit = "AUTH_RATE_LIMIT";
    public const string AdapterHttpLogRetentionDays = "ADAPTER_HTTP_LOG_RETENTION_DAYS";
}

public record AppSettingDto(
    string key,
    string label,
    string? value,
    string? effectiveValue,
    string valueType,
    string? description,
    bool isEditable,
    string? fallbackValue,
    DateTime updatedAt);
