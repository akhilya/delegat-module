using Delegat.Models.Marketplace.DTOs;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Distinguishes a real marketplace category title from the
/// <c>Category {id}</c> fallback used when the adapter did not send a name.
/// </summary>
public static class MarketplaceCategoryLabel
{
    public static bool IsSynthetic(string? label, string? externalCategoryId)
    {
        if (string.IsNullOrWhiteSpace(label))
            return true;

        var text = label.Trim();
        if (string.IsNullOrWhiteSpace(externalCategoryId))
            return false;

        var id = externalCategoryId.Trim();
        if (string.Equals(text, id, StringComparison.Ordinal))
            return true;

        return string.Equals(text, $"Category {id}", StringComparison.OrdinalIgnoreCase);
    }

    public static string? FromExternalCategory(ExternalCategory? category)
    {
        if (category == null)
            return null;
        if (!string.IsNullOrWhiteSpace(category.FullPath))
            return category.FullPath.Trim();
        if (!string.IsNullOrWhiteSpace(category.Name))
            return category.Name.Trim();
        return null;
    }

    public static string? FirstReal(string? externalCategoryId, params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!IsSynthetic(candidate, externalCategoryId))
                return candidate!.Trim();
        }

        return null;
    }

    /// <summary>
    /// Placeholder ids such as <c>0</c> or <c>parent:…</c> cannot be mapped or imported.
    /// Combined ids like <c>0:123</c> (Ozon type without description category) stay eligible.
    /// </summary>
    public static bool IsMappableId(string? externalCategoryId)
    {
        if (string.IsNullOrWhiteSpace(externalCategoryId))
            return false;

        var id = externalCategoryId.Trim();
        if (id == "0")
            return false;
        if (id.StartsWith("parent:", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    public static string? PreferMappableId(string? preferred, string? fallback)
    {
        if (IsMappableId(preferred))
            return preferred!.Trim();
        if (IsMappableId(fallback))
            return fallback!.Trim();
        return null;
    }
}
