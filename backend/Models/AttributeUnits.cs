using System.Text;
using System.Text.RegularExpressions;

namespace Delegat.Models;

/// <summary>Known unit metadata.</summary>
public sealed record AttributeUnitInfo(
    string Code,
    string Dimension,
    string NameRu,
    string NameEn,
    string Symbol,
    string[] Aliases);

/// <summary>Affine conversion from one unit to another: target = value * Scale + Offset.</summary>
public readonly record struct AttributeUnitConversion(decimal Scale, decimal Offset)
{
    public decimal Convert(decimal value) => value * Scale + Offset;
    public decimal Reverse(decimal value) => Scale == 0 ? value : (value - Offset) / Scale;
}

/// <summary>
/// Shared unit catalog and comparison rules for product attributes and marketplace fields.
/// Unknown/custom labels are preserved as normalized codes and remain dimension-unknown.
/// </summary>
public static class AttributeUnits
{
    private static readonly AttributeUnitInfo[] Items =
    [
        U("mg", "mass", "миллиграмм", "milligram", "мг", "milligram", "milligrams"),
        U("g", "mass", "грамм", "gram", "г", "гр", "gram", "grams"),
        U("kg", "mass", "килограмм", "kilogram", "кг", "килограмм", "килограмма", "килограммов", "килограммах", "килограммы", "kilograms", "kilogrammes", "kgs"),
        U("t", "mass", "тонна", "tonne", "т", "тонн", "тонны", "ton", "tons"),
        U("oz", "mass", "унция", "ounce", "oz", "ounces"),
        U("lb", "mass", "фунт", "pound", "lb", "lbs", "pounds"),
        U("ct", "mass", "карат", "carat", "ct", "карата", "carats"),
        U("mm", "length", "миллиметр", "millimeter", "мм", "millimeter", "millimeters"),
        U("um", "length", "микрометр", "micrometer", "мкм", "μm", "µm", "micrometre", "micrometers"),
        U("nm", "length", "нанометр", "nanometer", "нм", "nanometre", "nanometers"),
        U("cm", "length", "сантиметр", "centimeter", "см", "сантиметр", "centimeters"),
        U("dm", "length", "дециметр", "decimeter", "дм", "decimetre", "decimeters"),
        U("m", "length", "метр", "meter", "м", "метра", "метров", "meters"),
        U("km", "length", "километр", "kilometer", "км", "kilometer", "kilometers"),
        U("in", "length", "дюйм", "inch", "in", "дюймы", "дюйма", "\"", "inches"),
        U("ft", "length", "фут", "foot", "ft", "фута", "футы", "feet"),
        U("yd", "length", "ярд", "yard", "yd", "ярда", "yards"),
        U("m2", "area", "квадратный метр", "square meter", "м²", "м2", "sq m", "sqm"),
        U("km2", "area", "квадратный километр", "square kilometer", "км²", "км2", "sq km"),
        U("cm2", "area", "квадратный сантиметр", "square centimeter", "см²", "см2", "sq cm"),
        U("mm2", "area", "квадратный миллиметр", "square millimeter", "мм²", "мм2", "sq mm"),
        U("ha", "area", "гектар", "hectare", "га", "гектаров", "hectares"),
        U("ml", "volume", "миллилитр", "milliliter", "мл", "millilitre", "milliliters"),
        U("ul", "volume", "микролитр", "microliter", "мкл", "μl", "µl", "microlitre"),
        U("cl", "volume", "сантилитр", "centiliter", "сл", "centilitre"),
        U("dl", "volume", "децилитр", "deciliter", "дл", "decilitre"),
        U("l", "volume", "литр", "liter", "л", "литра", "литров", "litre", "liters"),
        U("m3", "volume", "кубический метр", "cubic meter", "м³", "м3", "куб. м", "cbm"),
        U("cm3", "volume", "кубический сантиметр", "cubic centimeter", "см³", "см3", "cc"),
        U("pcs", "count", "штука", "piece", "шт", "шт.", "штуки", "штук", "pc", "pcs", "piece", "pieces", "ea"),
        U("pair", "count", "пара", "pair", "пар", "пары", "pairs"),
        U("set", "count", "комплект", "set", "компл", "компл.", "комплектов", "sets"),
        U("pack", "count", "упаковка", "pack", "упак", "упаковок", "package", "packages"),
        U("roll", "count", "рулон", "roll", "рул", "рулонов", "rolls"),
        U("sheet", "count", "лист", "sheet", "листов", "sheets"),
        U("s", "time", "секунда", "second", "с", "сек", "сек.", "секунд", "seconds"),
        U("min", "time", "минута", "minute", "мин", "мин.", "минут", "minutes"),
        U("h", "time", "час", "hour", "ч", "часа", "часов", "hr", "hours"),
        U("day", "time", "сутки", "day", "день", "дн", "дн.", "дней", "days"),
        U("week", "time", "неделя", "week", "нед", "нед.", "недели", "weeks"),
        U("month", "time", "месяц", "month", "мес", "мес.", "месяцев", "months"),
        U("year", "time", "год", "year", "года", "лет", "years"),
        U("c", "temperature", "градус Цельсия", "degree Celsius", "°C", "C", "°С", "градус", "градуса", "celsius"),
        U("f", "temperature", "градус Фаренгейта", "degree Fahrenheit", "°F", "F", "fahrenheit"),
        U("k", "temperature", "кельвин", "kelvin", "K", "кельвинов"),
        U("v", "voltage", "вольт", "volt", "В", "в", "вольта", "volts"),
        U("mv", "voltage", "милливольт", "millivolt", "мВ", "милливольта", "millivolts"),
        U("a", "current", "ампер", "ampere", "А", "а", "ампера", "ампер", "amps"),
        U("ma", "current", "миллиампер", "milliampere", "мА", "миллиампера", "milliamps"),
        U("w", "power", "ватт", "watt", "Вт", "вт", "ватта", "watts"),
        U("kw", "power", "киловатт", "kilowatt", "кВт", "киловатта", "kilowatts"),
        U("wh", "energy", "ватт-час", "watt-hour", "Вт·ч", "втч", "ватт час", "watt hour"),
        U("kwh", "energy", "киловатт-час", "kilowatt-hour", "кВт·ч", "квтч", "киловатт час"),
        U("j", "energy", "джоуль", "joule", "Дж", "дж", "joules"),
        U("kj", "energy", "килоджоуль", "kilojoule", "кДж", "кдж", "kilojoules"),
        U("cal", "energy", "калория", "calorie", "кал", "калории", "calories"),
        U("kcal", "energy", "килокалория", "kilocalorie", "ккал", "килокалорий", "kilocalories"),
        U("mah", "electric_charge", "миллиампер-час", "milliampere-hour", "мА·ч", "мач", "mah"),
        U("ah", "electric_charge", "ампер-час", "ampere-hour", "А·ч", "ач", "amp-hour"),
        U("ohm", "resistance", "ом", "ohm", "Ом", "омов", "Ω", "ohms"),
        U("hz", "frequency", "герц", "hertz", "Гц", "гц", "герца", "hertz"),
        U("khz", "frequency", "килогерц", "kilohertz", "кГц", "кгц"),
        U("mhz", "frequency", "мегагерц", "megahertz", "МГц", "мгц"),
        U("ghz", "frequency", "гигагерц", "gigahertz", "ГГц", "ггц"),
        U("pa", "pressure", "паскаль", "pascal", "Па", "па"),
        U("kpa", "pressure", "килопаскаль", "kilopascal", "кПа", "кпа"),
        U("mpa", "pressure", "мегапаскаль", "megapascal", "МПа", "мпа"),
        U("mmhg", "pressure", "миллиметр ртутного столба", "millimeter of mercury", "мм рт. ст.", "mm hg", "mmhg"),
        U("bar", "pressure", "бар", "bar", "бара", "бары"),
        U("atm", "pressure", "атмосфера", "atmosphere", "атм", "атмосфер", "atmospheres"),
        U("n", "force", "ньютон", "newton", "Н", "н", "ньютона", "newtons"),
        U("n_m", "torque", "ньютон-метр", "newton meter", "Н·м", "newton-metre"),
        U("mps", "speed", "метр в секунду", "meter per second", "м/с", "m/s"),
        U("kmh", "speed", "километр в час", "kilometer per hour", "км/ч", "km/h"),
        U("rpm", "rotational_speed", "оборот в минуту", "revolutions per minute", "об/мин", "rpm"),
        U("lpm", "volume_flow", "литр в минуту", "liter per minute", "л/мин", "l/min"),
        U("m3h", "volume_flow", "кубический метр в час", "cubic meter per hour", "м³/ч", "м3/ч", "m3/h"),
        U("lm", "luminous_flux", "люмен", "lumen", "лм", "люмена", "lumens"),
        U("lx", "illuminance", "люкс", "lux", "лк", "люкса"),
        U("deg", "angle", "градус", "degree", "°", "град", "град.", "degrees"),
        U("rad", "angle", "радиан", "radian", "рад", "radians"),
        U("pct", "ratio", "процент", "percent", "%", "проц", "проц.", "процентов"),
        U("kg_m3", "density", "килограмм на кубический метр", "kilogram per cubic meter", "кг/м³", "кг/м3", "kg/m3"),
        U("g_cm3", "density", "грамм на кубический сантиметр", "gram per cubic centimeter", "г/см³", "г/см3", "g/cm3"),
        U("bit", "digital_size", "бит", "bit", "біт", "bits"),
        U("byte", "digital_size", "байт", "byte", "Б", "байта", "байтов", "bytes"),
        U("kb", "digital_size", "килобайт", "kilobyte", "КБ", "кб", "kilobytes"),
        U("mb", "digital_size", "мегабайт", "megabyte", "МБ", "мб", "megabytes"),
        U("gb", "digital_size", "гигабайт", "gigabyte", "ГБ", "гб", "gigabytes"),
        U("tb", "digital_size", "терабайт", "terabyte", "ТБ", "тб", "terabytes"),
    ];

    private static readonly Dictionary<string, AttributeUnitInfo> ByAlias = BuildAliases();
    private static readonly Dictionary<string, (decimal Scale, decimal Offset)> ToBase =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Mass → grams
            ["mg"] = (0.001m, 0m), ["g"] = (1m, 0m), ["kg"] = (1000m, 0m),
            ["t"] = (1_000_000m, 0m), ["oz"] = (28.349523125m, 0m),
            ["lb"] = (453.59237m, 0m), ["ct"] = (0.2m, 0m),
            // Length → meters
            ["nm"] = (0.000000001m, 0m), ["um"] = (0.000001m, 0m),
            ["mm"] = (0.001m, 0m), ["cm"] = (0.01m, 0m), ["dm"] = (0.1m, 0m),
            ["m"] = (1m, 0m), ["km"] = (1000m, 0m), ["in"] = (0.0254m, 0m),
            ["ft"] = (0.3048m, 0m), ["yd"] = (0.9144m, 0m),
            // Area → square meters
            ["mm2"] = (0.000001m, 0m), ["cm2"] = (0.0001m, 0m),
            ["m2"] = (1m, 0m), ["km2"] = (1_000_000m, 0m), ["ha"] = (10_000m, 0m),
            // Volume → cubic meters
            ["ul"] = (0.000000001m, 0m), ["ml"] = (0.000001m, 0m),
            ["cl"] = (0.00001m, 0m), ["dl"] = (0.0001m, 0m), ["l"] = (0.001m, 0m),
            ["cm3"] = (0.000001m, 0m), ["m3"] = (1m, 0m),
            // Time → seconds (calendar months and years are intentionally excluded)
            ["s"] = (1m, 0m), ["min"] = (60m, 0m), ["h"] = (3600m, 0m),
            ["day"] = (86400m, 0m), ["week"] = (604800m, 0m),
            // Temperature → degrees Celsius
            ["c"] = (1m, 0m), ["f"] = (5m / 9m, -160m / 9m), ["k"] = (1m, -273.15m),
            // Electrical units
            ["v"] = (1m, 0m), ["mv"] = (0.001m, 0m),
            ["a"] = (1m, 0m), ["ma"] = (0.001m, 0m),
            ["w"] = (1m, 0m), ["kw"] = (1000m, 0m),
            ["j"] = (1m, 0m), ["kj"] = (1000m, 0m), ["wh"] = (3600m, 0m),
            ["kwh"] = (3_600_000m, 0m), ["cal"] = (4.184m, 0m), ["kcal"] = (4184m, 0m),
            ["ah"] = (1m, 0m), ["mah"] = (0.001m, 0m),
            // Pressure → pascals
            ["pa"] = (1m, 0m), ["kpa"] = (1000m, 0m), ["mpa"] = (1_000_000m, 0m),
            ["mmhg"] = (133.322387415m, 0m), ["bar"] = (100_000m, 0m),
            ["atm"] = (101_325m, 0m),
            // Force, speed, angle, ratio, density, and digital size
            ["n"] = (1m, 0m), ["mps"] = (1m, 0m), ["kmh"] = (1m / 3.6m, 0m),
            ["rad"] = (1m, 0m), ["deg"] = (0.0174532925199432957692369077m, 0m),
            ["pct"] = (0.01m, 0m), ["kg_m3"] = (1m, 0m), ["g_cm3"] = (1000m, 0m),
            ["bit"] = (1m, 0m), ["byte"] = (8m, 0m), ["kb"] = (8000m, 0m),
            ["mb"] = (8_000_000m, 0m), ["gb"] = (8_000_000_000m, 0m),
            ["tb"] = (8_000_000_000_000m, 0m),
        };
    private static readonly string[] TextAliases = BuildTextAliases();
    private static readonly HashSet<string> ContextRequiredAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "in", "\"", "pack", "package", "packages", "set", "sets", "pair", "pairs",
        "roll", "rolls", "sheet", "sheets", "day", "days", "week", "weeks",
        "month", "months", "year", "years", "bar", "bit", "bits", "byte", "bytes",
        "piece", "pieces",
    };
    private static readonly Regex MeasurementCueRegex = new(
        @"(?<![\p{L}\p{N}])(?:weight|mass|gross|net|width|height|length|depth|diameter|thickness|dimension|size|volume|capacity|speed|velocity|temperature|pressure|power|voltage|current|frequency|energy|duration|time|amount|quantity|count|unit|measure|вес|масс|ширин|высот|длин|глуб|диаметр|толщин|габарит|размер|об[её]м|ёмкост|скорост|температур|давлен|мощност|напряжен|ток|частот|энерг|времен|врем|количеств|единиц|измерен|расход)[\p{L}]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    public static IReadOnlyList<AttributeUnitInfo> All => Items;

    public static string? NormalizeCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        var lookup = NormalizeAlias(trimmed);
        return ByAlias.TryGetValue(lookup, out var unit)
            ? unit.Code
            : trimmed.Length <= 64 ? trimmed : trimmed[..64];
    }

    public static AttributeUnitInfo? Find(string? value)
    {
        var code = NormalizeCode(value);
        return code != null && ByAlias.TryGetValue(NormalizeAlias(code), out var unit) ? unit : null;
    }

    /// <summary>
    /// Infer one known unit from a field label or description. Ambiguous text and
    /// unrecognized unit names are intentionally left unset.
    /// </summary>
    public static string? InferCodeFromText(string? text, string? context = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var normalizedText = NormalizeSearchText(text);
        var normalizedContext = NormalizeSearchText(context ?? "");
        var matches = new List<TextUnitMatch>();

        try
        {
            foreach (var alias in TextAliases)
            {
                var normalizedAlias = NormalizeSearchText(alias.Trim());
                if (normalizedAlias.Length == 0
                    || !ByAlias.TryGetValue(NormalizeAlias(alias), out var unit))
                    continue;

                var start = 0;
                while ((start = normalizedText.IndexOf(
                    normalizedAlias,
                    start,
                    StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    var candidate = new TextUnitMatch(start, normalizedAlias.Length, alias, unit);
                    start++;

                    if (!HasTextBoundaries(normalizedText, candidate)
                        || !IsReliableTextMatch(normalizedText, candidate, normalizedContext)
                        || matches.Any(existing => Overlaps(existing, candidate)))
                    {
                        continue;
                    }

                    matches.Add(candidate);
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }

        var foundCodes = matches.Select(match => match.Unit.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return foundCodes.Length == 1 ? foundCodes[0] : null;
    }

    /// <summary>Different known units are never silently treated as equal.</summary>
    public static bool AreSame(string? left, string? right)
        => string.Equals(NormalizeCode(left), NormalizeCode(right), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolve a reversible numeric conversion between known units. Equal custom units
    /// are identity-compatible; unknown units never convert to a different code.
    /// </summary>
    public static bool TryGetConversion(
        string? fromUnit,
        string? toUnit,
        out AttributeUnitConversion conversion)
    {
        var from = NormalizeCode(fromUnit);
        var to = NormalizeCode(toUnit);
        if (from == null || to == null)
        {
            conversion = default;
            return false;
        }

        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            conversion = new AttributeUnitConversion(1m, 0m);
            return true;
        }

        var fromInfo = Find(from);
        var toInfo = Find(to);
        if (fromInfo == null || toInfo == null
            || !string.Equals(fromInfo.Dimension, toInfo.Dimension, StringComparison.OrdinalIgnoreCase)
            || !ToBase.TryGetValue(fromInfo.Code, out var fromTransform)
            || !ToBase.TryGetValue(toInfo.Code, out var toTransform)
            || toTransform.Scale == 0)
        {
            conversion = default;
            return false;
        }

        conversion = new AttributeUnitConversion(
            fromTransform.Scale / toTransform.Scale,
            (fromTransform.Offset - toTransform.Offset) / toTransform.Scale);
        return true;
    }

    private static AttributeUnitInfo U(string code, string dimension, string ru, string en, string symbol, params string[] aliases)
        => new(code, dimension, ru, en, symbol, aliases);

    private static Dictionary<string, AttributeUnitInfo> BuildAliases()
    {
        var result = new Dictionary<string, AttributeUnitInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var unit in Items)
        {
            result[NormalizeAlias(unit.Code)] = unit;
            result[NormalizeAlias(unit.Symbol)] = unit;
            result[NormalizeAlias(unit.NameRu)] = unit;
            result[NormalizeAlias(unit.NameEn)] = unit;
            foreach (var alias in unit.Aliases)
                result[NormalizeAlias(alias)] = unit;
        }
        return result;
    }

    private static string NormalizeAlias(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant()
            .Replace('ё', 'е').Replace('μ', 'u').Replace('µ', 'u');
        return string.Concat(normalized.Where(c => !char.IsWhiteSpace(c) && c != '-'));
    }

    private static string[] BuildTextAliases()
        => Items
            .SelectMany(unit => new[] { unit.Code, unit.Symbol, unit.NameRu, unit.NameEn }.Concat(unit.Aliases))
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(alias => NormalizeSearchText(alias).Length)
            .ToArray();

    private static bool HasTextBoundaries(string text, TextUnitMatch match)
    {
        var alias = NormalizeSearchText(match.Alias.Trim());
        if (char.IsLetterOrDigit(alias[0]) && match.Index > 0)
        {
            var previous = text[match.Index - 1];
            if (char.IsLetter(previous))
                return false;

            if (char.IsDigit(previous))
            {
                var beforeNumber = match.Index - 1;
                while (beforeNumber >= 0 && char.IsDigit(text[beforeNumber]))
                    beforeNumber--;
                if (beforeNumber >= 0 && char.IsLetter(text[beforeNumber]))
                    return false;
            }
        }

        var end = match.Index + match.Length;
        if (char.IsLetterOrDigit(alias[^1]) && end < text.Length
            && char.IsLetterOrDigit(text[end]))
        {
            return false;
        }

        return true;
    }

    private static bool IsReliableTextMatch(
        string text,
        TextUnitMatch match,
        string context)
    {
        var normalizedAlias = NormalizeAlias(match.Alias);
        // A quote commonly appears in prose and article titles. Treat it as the
        // inch symbol only when it directly adjoins a numeric measurement.
        if (normalizedAlias == "\"")
            return IsAdjacentToNumber(text, match);

        var requiresContext = ContextRequiredAliases.Contains(normalizedAlias)
            || (normalizedAlias.Length <= 2 && normalizedAlias.All(char.IsLetter));
        if (!requiresContext)
            return true;

        if (IsAdjacentToNumber(text, match) || IsWrappedInBrackets(text, match))
            return true;

        var prefix = string.IsNullOrEmpty(context)
            ? text[..match.Index]
            : $"{context} {text[..match.Index]}";
        if (!MeasurementCueRegex.IsMatch(prefix))
            return false;

        // A standalone "in" is also a common English preposition, and one-letter
        // symbols can be ordinary words (for example Russian "с" meaning "with").
        return (normalizedAlias != "in" && normalizedAlias.Length > 1)
            || IsTrailingToken(text, match);
    }

    private static bool IsAdjacentToNumber(string text, TextUnitMatch match)
    {
        var before = match.Index - 1;
        while (before >= 0 && char.IsWhiteSpace(text[before]))
            before--;
        if (before >= 0 && char.IsDigit(text[before]))
            return true;

        var after = match.Index + match.Length;
        while (after < text.Length && char.IsWhiteSpace(text[after]))
            after++;
        return after < text.Length && char.IsDigit(text[after]);
    }

    private static bool IsWrappedInBrackets(string text, TextUnitMatch match)
    {
        var before = match.Index - 1;
        while (before >= 0 && char.IsWhiteSpace(text[before]))
            before--;
        var after = match.Index + match.Length;
        while (after < text.Length && char.IsWhiteSpace(text[after]))
            after++;

        return before >= 0 && after < text.Length
            && ((text[before] == '(' && text[after] == ')')
                || (text[before] == '[' && text[after] == ']'));
    }

    private static bool IsTrailingToken(string text, TextUnitMatch match)
    {
        var after = match.Index + match.Length;
        while (after < text.Length
            && (char.IsWhiteSpace(text[after]) || text[after] is '.' or ',' or ';' or ':' or ')' or ']'))
        {
            after++;
        }

        return after == text.Length;
    }

    private static bool Overlaps(TextUnitMatch left, TextUnitMatch right)
        => left.Index < right.Index + right.Length && right.Index < left.Index + left.Length;

    private static string NormalizeSearchText(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormKC)
            .Replace('ё', 'е').Replace('Ё', 'Е')
            .Replace('μ', 'u').Replace('µ', 'u').Replace('Μ', 'u');
        var result = new StringBuilder(normalized.Length);
        var pendingSpace = false;

        foreach (var character in normalized)
        {
            if (char.IsWhiteSpace(character) || character is '-' or '‐' or '–' or '—')
            {
                pendingSpace = result.Length > 0;
                continue;
            }

            if (pendingSpace)
                result.Append(' ');
            result.Append(character);
            pendingSpace = false;
        }

        return result.ToString();
    }

    private sealed record TextUnitMatch(int Index, int Length, string Alias, AttributeUnitInfo Unit);
}
