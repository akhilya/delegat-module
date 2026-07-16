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
            "Лимит товаров",
            "integer",
            "Максимальное количество товаров в одном магазине. Пусто - без ограничения или значение из MAX_PRODUCTS.",
            () => Environment.GetEnvironmentVariable("MAX_PRODUCTS")),
        new(
            AppSettingKeys.MaxStorageGb,
            "Лимит хранилища, GB",
            "decimal",
            "Максимальный общий объём файлов магазина и товаров. Пусто - без ограничения или значение из MAX_STORAGE_GB.",
            () => Environment.GetEnvironmentVariable("MAX_STORAGE_GB")),
        new(
            AppSettingKeys.MaxFiles,
            "Лимит файлов",
            "integer",
            "Максимальное количество файлов магазина и товаров. Пусто - без ограничения или значение из MAX_FILES.",
            () => Environment.GetEnvironmentVariable("MAX_FILES")),
        new(
            AppSettingKeys.MaxConnections,
            "Лимит подключений",
            "integer",
            "Максимальное количество активных адаптеров магазина. Пусто - без ограничения или значение из MAX_CONNECTIONS.",
            () => Environment.GetEnvironmentVariable("MAX_CONNECTIONS")),
        new(
            AppSettingKeys.GlobalRateLimit,
            "Глобальный rate limit",
            "integer",
            "Количество запросов в минуту на IP. Текущее middleware применяет значение после перезапуска приложения.",
            () => "300"),
        new(
            AppSettingKeys.AuthRateLimit,
            "Auth rate limit",
            "integer",
            "Количество auth-запросов в минуту на IP. Текущее middleware применяет значение после перезапуска приложения.",
            () => "10"),
        new(
            AppSettingKeys.AdapterHttpLogRetentionDays,
            "Срок хранения логов адаптеров, дни",
            "integer",
            "Сколько дней хранить исходящие HTTP-логи адаптеров (1–30). По умолчанию 7.",
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
            throw ApiException.NotFound("Настройка не найдена");

        if (!setting.isEditable)
            throw ApiException.Forbidden("Настройку нельзя редактировать");

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
                throw ApiException.BadRequest("Значение должно быть положительным целым числом");

            return integer.ToString();
        }

        if (setting.valueType == "decimal")
        {
            if (!TryParsePositiveDecimal(trimmed, out var number))
                throw ApiException.BadRequest("Значение должно быть положительным числом");

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
