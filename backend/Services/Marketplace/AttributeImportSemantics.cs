using System.Text.RegularExpressions;
using Delegat.Enums;
using Delegat.Models.Marketplace.DTOs;

namespace Delegat.Services.Marketplace;

/// <summary>Local attachment semantics without changing the adapter's wire schema.</summary>
internal static class AttributeImportSemantics
{
    private static readonly Regex MarketplacePrefix = Pattern(@"^(?:озон|ozon)\s*[.:]\s*");
    private static readonly Regex ReferenceSuffix = Pattern(
        @"\s*(?:[:;,\-–—]\s*(?:ссылк[аи]|url|links?|файл[ы]?|files?)|\(\s*(?:ссылк[аи]|url|links?|файл[ы]?|files?)\s*\))\s*$");
    private static readonly Regex AttachmentName = Pattern(
        @"\b(?:файл(?:ы|а|ов|у|ом|е|ами|ах)?|вложени[еяй]|files?|attachments?)\b");
    private static readonly Regex MediaName = Pattern(
        @"\b(?:видео\p{L}*|документ\p{L}*|инструкци\p{L}*|сертификат\p{L}*|деклараци\p{L}*|videos?|documents?|manuals?|certificates?)\b");
    private static readonly Regex ReferenceName = Pattern(@"\b(?:ссылк\p{L}*|url|links?)\b");
    private static readonly Regex AttachmentMetadata = Pattern(
        @"\b(?:названи\p{L}*|имя|количеств\p{L}*|формат\p{L}*|длительност\p{L}*|размер\p{L}*|names?|titles?|count|format|duration|size)\b");

    public static AttributeType ResolveType(ExternalField field)
    {
        var type = CategoryImportService.MapExternalFieldType(field.Type);
        if (type != AttributeType.Text || field.Options is { Count: > 0 })
            return type;

        // Never infer attachments from a description, a dictionary, or a numeric/boolean field.
        var name = MarketplacePrefix.Replace(field.Name.Trim(), "");
        if (AttachmentMetadata.IsMatch(name))
            return type;
        var withoutSuffix = ReferenceSuffix.Replace(name, "").Trim();
        if (AttachmentName.IsMatch(name)
            || (MediaName.IsMatch(name)
                && (ReferenceName.IsMatch(name)
                    || field.Type.Trim().ToLowerInvariant() is "url" or "uri" or "link"))
            || withoutSuffix.ToLowerInvariant() is "видео" or "видеообложка" or "video" or "video cover")
            return AttributeType.File;

        return type;
    }

    public static string NormalizeName(ExternalField field, string? unitCode)
    {
        var name = field.Name;
        if (ResolveType(field) == AttributeType.File)
            name = ReferenceSuffix.Replace(MarketplacePrefix.Replace(name.Trim(), ""), "").Trim();
        return AttributeImportNameNormalizer.Normalize(name, unitCode, field.Description);
    }

    public static bool AllowsMultipleFiles(ExternalField field)
        => ResolveType(field) == AttributeType.File && field.MaxValues > 1;

    private static Regex Pattern(string pattern) => new(
        pattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));
}
