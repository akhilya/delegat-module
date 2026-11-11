using System.Text;
using System.Text.RegularExpressions;
using Delegat.Enums;
using Delegat.Models;
using Delegat.Models.Marketplace.DTOs;

namespace Delegat.Services.Marketplace;

/// <summary>A user's explicit choice for a marketplace field that may duplicate a local attribute.</summary>
public sealed class CategoryImportAttributeDecision
{
    public string fieldKey { get; set; } = string.Empty;
    /// <summary>One of: reuse, create, skip.</summary>
    public string action { get; set; } = string.Empty;
    public int? existingAttributeId { get; set; }
}

public sealed record AttributeDuplicateCandidate(
    CategoryAttribute Attribute,
    int Score,
    IReadOnlyList<string> MatchedBy);

/// <summary>
/// Finds likely duplicate attribute definitions without making an import decision.
/// Type and unit must match exactly; semantic qualifiers prevent gross/net and
/// packaged/unpackaged attributes from being grouped together.
/// </summary>
public static class AttributeDuplicateMatcher
{
    private static readonly Regex TokenRegex = new(
        @"[\p{L}\p{N}]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex WithoutPackagingRegex = new(
        @"(?:without\s+(?:the\s+)?packag(?:e|ing)|unpackaged|без\s+упаковк[а-яё]*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex PackagingRegex = new(
        @"(?:packag(?:e|ing)|упаковк[а-яё]*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex GrossRegex = new(
        @"(?:gross|brutto|брутто)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex NetRegex = new(
        @"(?:net|нетто)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    public static IReadOnlyList<AttributeDuplicateCandidate> FindCandidates(
        ExternalField field,
        AttributeType fieldType,
        string? fieldUnitCode,
        IEnumerable<CategoryAttribute> attributes)
    {
        var importedName = AttributeImportNameNormalizer.Normalize(
            field.Name, fieldUnitCode, field.Description);
        var fieldSemantics = SemanticMarkers(field.Name, field.Description, importedName);
        var fieldDescription = NormalizeText(field.Description);
        var fieldKey = NormalizeText(CategoryImportService.SanitizeKey(field.Key));

        return attributes
            .Where(attribute => attribute.type == fieldType
                && AttributeUnits.AreSame(attribute.unitCode, fieldUnitCode)
                && SemanticMarkers(attribute.name, attribute.description).SetEquals(fieldSemantics))
            .Select(attribute => ScoreCandidate(
                attribute,
                importedName,
                fieldDescription,
                fieldKey))
            .Where(candidate => candidate.Score >= 68)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Attribute.name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Attribute.Id)
            .ToList();
    }

    private static AttributeDuplicateCandidate ScoreCandidate(
        CategoryAttribute attribute,
        string importedName,
        string fieldDescription,
        string fieldKey)
    {
        var nameScore = Similarity(importedName, attribute.name);
        var descriptionScore = Similarity(fieldDescription, attribute.description);
        var exactKey = string.Equals(
            fieldKey,
            NormalizeText(attribute.key),
            StringComparison.Ordinal);

        var reasons = new List<string>();
        if (nameScore >= 0.72) reasons.Add("name");
        if (descriptionScore >= 0.72) reasons.Add("description");
        if (exactKey) reasons.Add("key");

        // A matching machine key is supporting evidence, never enough on its own.
        var score = Math.Max(nameScore, descriptionScore * 0.9);
        if (exactKey && nameScore >= 0.25)
            score = Math.Max(score, 0.72);
        if (nameScore >= 0.42 && descriptionScore >= 0.55)
            score = Math.Max(score, (nameScore * 0.65) + (descriptionScore * 0.35));

        return new AttributeDuplicateCandidate(
            attribute,
            (int)Math.Round(score * 100),
            reasons);
    }

    private static HashSet<string> SemanticMarkers(params string?[] values)
    {
        var text = string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
        var markers = new HashSet<string>(StringComparer.Ordinal);
        var hasPackaging = PackagingRegex.IsMatch(text);
        if (hasPackaging)
            markers.Add(WithoutPackagingRegex.IsMatch(text) ? "packaging:without" : "packaging:with");
        if (GrossRegex.IsMatch(text)) markers.Add("weight:gross");
        if (NetRegex.IsMatch(text)) markers.Add("weight:net");
        return markers;
    }

    private static double Similarity(string? left, string? right)
    {
        var leftText = NormalizeText(left);
        var rightText = NormalizeText(right);
        if (leftText.Length == 0 || rightText.Length == 0) return 0;
        if (string.Equals(leftText, rightText, StringComparison.Ordinal)) return 1;

        var leftTokens = Tokens(leftText);
        var rightTokens = Tokens(rightText);
        var union = leftTokens.Union(rightTokens, StringComparer.Ordinal).Count();
        var tokenScore = union == 0
            ? 0
            : leftTokens.Intersect(rightTokens, StringComparer.Ordinal).Count() / (double)union;
        var leftBigrams = Bigrams(leftText);
        var rightBigrams = Bigrams(rightText);
        var bigramUnion = leftBigrams.Count + rightBigrams.Count;
        var bigramScore = bigramUnion == 0
            ? 0
            : 2d * leftBigrams.Intersect(rightBigrams, StringComparer.Ordinal).Count() / bigramUnion;
        return (tokenScore * 0.6) + (bigramScore * 0.4);
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        try
        {
            var normalized = value.Normalize(NormalizationForm.FormKC)
                .Replace('ё', 'е').Replace('Ё', 'Е').ToLowerInvariant();
            return string.Join(" ", TokenRegex.Matches(normalized)
                .Select(match => match.Value));
        }
        catch (RegexMatchTimeoutException)
        {
            return string.Empty;
        }
    }

    private static HashSet<string> Tokens(string value)
        => value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> Bigrams(string value)
    {
        var compact = value.Replace(" ", string.Empty, StringComparison.Ordinal);
        var result = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < compact.Length; i++)
            result.Add(compact.Substring(i, 2));
        return result;
    }
}
