using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Delegat.Models;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Produces readable Russian labels for common marketplace fields while keeping
/// the original marketplace field metadata unchanged.
/// </summary>
internal static class AttributeImportNameNormalizer
{
    private static readonly Dictionary<string, string> RussianLabels = new(StringComparer.Ordinal)
    {
        ["height"] = "Высота",
        ["product height"] = "Высота товара",
        ["package height"] = "Высота упаковки",
        ["packaging height"] = "Высота упаковки",
        ["height of packaging"] = "Высота упаковки",
        ["height with packaging"] = "Высота с упаковкой",
        ["height with package"] = "Высота с упаковкой",
        ["height including packaging"] = "Высота с упаковкой",
        ["height without packaging"] = "Высота без упаковки",
        ["width"] = "Ширина",
        ["product width"] = "Ширина товара",
        ["package width"] = "Ширина упаковки",
        ["packaging width"] = "Ширина упаковки",
        ["length"] = "Длина",
        ["product length"] = "Длина товара",
        ["package length"] = "Длина упаковки",
        ["packaging length"] = "Длина упаковки",
        ["depth"] = "Глубина",
        ["product depth"] = "Глубина товара",
        ["package depth"] = "Глубина упаковки",
        ["diameter"] = "Диаметр",
        ["thickness"] = "Толщина",
        ["dimensions"] = "Габариты",
        ["overall dimensions"] = "Габариты",
        ["product dimensions"] = "Габариты товара",
        ["package dimensions"] = "Габариты упаковки",
        ["weight"] = "Вес",
        ["product weight"] = "Вес товара",
        ["item weight"] = "Вес товара",
        ["gross weight"] = "Вес брутто",
        ["net weight"] = "Вес нетто",
        ["gross product weight"] = "Вес товара брутто",
        ["net product weight"] = "Вес товара нетто",
        ["weight with packaging"] = "Вес с упаковкой",
        ["weight without packaging"] = "Вес без упаковки",
        ["color"] = "Цвет",
        ["colour"] = "Цвет",
        ["color name"] = "Цвет",
        ["brand"] = "Бренд",
        ["manufacturer"] = "Производитель",
        ["material"] = "Материал",
        ["composition"] = "Состав",
        ["country of origin"] = "Страна производства",
        ["country of manufacture"] = "Страна производства",
        ["manufacturer country"] = "Страна производства",
        ["model"] = "Модель",
        ["size"] = "Размер",
        ["volume"] = "Объём",
        ["capacity"] = "Ёмкость",
        ["voltage"] = "Напряжение",
        ["power"] = "Мощность",
        ["current"] = "Сила тока",
        ["temperature"] = "Температура",
        ["pressure"] = "Давление",
        ["frequency"] = "Частота",
        ["warranty"] = "Гарантия",
        ["warranty period"] = "Гарантийный срок",
        ["shelf life"] = "Срок годности",
        ["quantity"] = "Количество",
        ["count"] = "Количество",
        ["number of pieces"] = "Количество штук",
        ["purpose"] = "Назначение",
        ["series"] = "Серия",
        ["collection"] = "Коллекция",
        ["gender"] = "Пол",
        ["age group"] = "Возрастная группа",
        ["season"] = "Сезон",
        ["style"] = "Стиль",
        ["pattern"] = "Рисунок",
        ["shape"] = "Форма",
        ["connection type"] = "Тип подключения",
        ["battery capacity"] = "Ёмкость аккумулятора",
        ["screen diagonal"] = "Диагональ экрана",
    };

    private static readonly ConcurrentDictionary<string, Regex> UnitSuffixPatterns = new(
        StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> PackagingBaseLabels = new(StringComparer.Ordinal)
    {
        ["height"] = "Высота",
        ["высота"] = "Высота",
        ["width"] = "Ширина",
        ["ширина"] = "Ширина",
        ["length"] = "Длина",
        ["длина"] = "Длина",
        ["depth"] = "Глубина",
        ["глубина"] = "Глубина",
        ["weight"] = "Вес",
        ["вес"] = "Вес",
    };

    private static readonly Regex PackageDimensionDescriptionPattern = new(
        @"(?:\bpackag(?:e|ing)\s+(?<measure>height|width|length|depth|weight)\b|\b(?<measure>height|width|length|depth|weight)\s+of\s+(?:the\s+)?packag(?:e|ing)\b|\b(?<measure>высота|ширина|длина|глубина|вес)\s+упаковк\p{L}*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex WithPackagingDescriptionPattern = new(
        @"(?:\b(?:with|including|incl|inclusive\s+of)\b(?:\s+\p{L}+){0,3}\s+\bpackag(?:e|ing)\b|\bвключая(?:\s+\p{L}+){0,2}\s+упаковк\p{L}*|\bс(?:\s+учетом)?\s+упаковк\p{L}*|\bвместе\s+с\s+упаковк\p{L}*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex WithoutPackagingDescriptionPattern = new(
        @"(?:\b(?:without|excluding|excl|not\s+including)\b(?:\s+\p{L}+){0,3}\s+\bpackag(?:e|ing)\b|\bбез\s+упаковк\p{L}*|\bне\s+включая(?:\s+\p{L}+){0,2}\s+упаковк\p{L}*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    public static string Normalize(string? sourceName, string? unitCode, string? description = null)
    {
        var cleaned = CleanUntranslatedLabel(StripKnownUnitSuffix(sourceName, unitCode));
        var lookupKey = NormalizeLookupKey(cleaned);

        // Adapters may put a measurement qualifier in a longer description rather
        // than the short field name (for example, "Height (mm)" / "including packaging").
        var contextualName = NormalizeFromDescription(lookupKey, description);
        if (contextualName != null)
            return contextualName;

        return RussianLabels.TryGetValue(lookupKey, out var russianLabel)
            ? russianLabel
            : cleaned;
    }

    private static string? NormalizeFromDescription(string lookupKey, string? description)
    {
        if (!PackagingBaseLabels.TryGetValue(lookupKey, out var baseLabel)
            || string.IsNullOrWhiteSpace(description))
            return null;

        try
        {
            var descriptionKey = NormalizeLookupKey(description);
            var packageDimensionMatch = PackageDimensionDescriptionPattern.Match(descriptionKey);
            if (packageDimensionMatch.Success
                && PackagingBaseLabels.TryGetValue(
                    NormalizeLookupKey(packageDimensionMatch.Groups["measure"].Value),
                    out var packageDimensionBaseLabel)
                && string.Equals(baseLabel, packageDimensionBaseLabel, StringComparison.Ordinal))
                return $"{baseLabel} упаковки";

            var includesPackaging = WithPackagingDescriptionPattern.IsMatch(descriptionKey);
            var excludesPackaging = WithoutPackagingDescriptionPattern.IsMatch(descriptionKey);
            if (includesPackaging == excludesPackaging)
                return null;

            return includesPackaging
                ? $"{baseLabel} с упаковкой"
                : $"{baseLabel} без упаковки";
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private static string StripKnownUnitSuffix(string? sourceName, string? unitCode)
    {
        var label = sourceName?.Normalize(NormalizationForm.FormKC).Trim() ?? "";
        var unit = AttributeUnits.Find(unitCode);
        if (label.Length == 0 || unit == null)
            return label;

        try
        {
            var pattern = UnitSuffixPatterns.GetOrAdd(unit.Code, _ => BuildUnitSuffixPattern(unit));
            var match = pattern.Match(label);
            return match.Success
                ? match.Groups["label"].Value.Trim().TrimEnd(',', ';', ':', '-', '–', '—').TrimEnd()
                : label;
        }
        catch (RegexMatchTimeoutException)
        {
            return label;
        }
    }

    private static Regex BuildUnitSuffixPattern(AttributeUnitInfo unit)
    {
        var aliases = new[] { unit.Code, unit.Symbol, unit.NameRu, unit.NameEn }
            .Concat(unit.Aliases)
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Select(BuildAliasPattern)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(pattern => pattern.Length);
        var alternatives = string.Join("|", aliases);
        var unitExpression = $"(?:{alternatives})";

        return new Regex(
            $@"^(?<label>.+?)(?:\s*\(\s*{unitExpression}\s*\)|\s*\[\s*{unitExpression}\s*\]|\s*[,;:]\s*{unitExpression}|\s*[-–—]\s*{unitExpression}|\s+{unitExpression})\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(100));
    }

    private static string BuildAliasPattern(string alias)
    {
        var parts = Regex.Split(alias.Normalize(NormalizationForm.FormKC).Trim(), @"[\s\-‐‑‒–—]+");
        return string.Join(
            @"[\s\-‐‑‒–—]+",
            parts.Where(part => part.Length > 0).Select(Regex.Escape));
    }

    private static string CleanUntranslatedLabel(string label)
    {
        var cleaned = Regex.Replace(label.Replace('_', ' '), @"\s+", " ");
        return cleaned.Trim().TrimEnd('*', ':', ';').TrimEnd();
    }

    private static string NormalizeLookupKey(string label)
    {
        var normalized = label.Normalize(NormalizationForm.FormKC)
            .Replace('ё', 'е')
            .ToLowerInvariant();
        var result = new StringBuilder(normalized.Length);
        var pendingSpace = false;

        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && result.Length > 0)
                    result.Append(' ');
                result.Append(character);
                pendingSpace = false;
            }
            else
            {
                pendingSpace = result.Length > 0;
            }
        }

        return result.ToString();
    }
}
