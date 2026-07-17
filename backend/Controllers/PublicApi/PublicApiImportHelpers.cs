namespace Delegat.Controllers.PublicApi;

/// <summary>
/// Pure helpers for Public API import-products queue decisions.
/// </summary>
public static class PublicApiImportHelpers
{
    /// <summary>
    /// Returns whether an import item should be queued and which local category to use.
    /// Skips blank external ids and unmapped categories unless force flags allow it.
    /// </summary>
    public static bool TryResolveImportItem(
        string? externalId,
        string? externalCategoryId,
        int? targetCategoryId,
        bool itemForceImportWithoutMapping,
        bool bodyForceImportWithoutMapping,
        IReadOnlyDictionary<string, int> categoryMappings,
        out int? resolvedCategoryId)
    {
        resolvedCategoryId = null;
        if (string.IsNullOrWhiteSpace(externalId))
            return false;

        resolvedCategoryId = targetCategoryId;
        if (!resolvedCategoryId.HasValue && !string.IsNullOrEmpty(externalCategoryId))
        {
            if (categoryMappings.TryGetValue(externalCategoryId, out var mappedLocalId))
                resolvedCategoryId = mappedLocalId;
            else if (!itemForceImportWithoutMapping && !bodyForceImportWithoutMapping)
                return false;
        }

        return true;
    }
}
