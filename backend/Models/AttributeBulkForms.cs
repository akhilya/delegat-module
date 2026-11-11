using Delegat.Infrastructure.Middleware;

namespace Delegat.Models;

/// <summary>Settings shared by category and global attribute definitions.</summary>
public interface IAttributeDefinitionSettings
{
    bool isRequired { get; set; }
    bool isUnique { get; set; }
    bool showOnProductPage { get; set; }
    bool descriptionInTooltip { get; set; }
    bool allowMultipleFiles { get; set; }
}

/// <summary>An explicit, bounded selection; never expands to the entire catalog.</summary>
public class AttributeBulkSelectionForm
{
    public const int MaxSelection = 1000;
    public int[] attributeIds { get; set; } = [];

    public int[] GetValidatedIds()
    {
        if (attributeIds == null || attributeIds.Length == 0 ||
            attributeIds.Length > MaxSelection || attributeIds.Any(id => id <= 0))
            throw ApiException.BadRequest(code: "attributes_selection_invalid");

        return attributeIds.Distinct().ToArray();
    }
}

/// <summary>Only supplied settings change; dictionaries, constraints and values are preserved.</summary>
public class AttributeBulkUpdateForm : AttributeBulkSelectionForm
{
    public bool? isRequired { get; set; }
    public bool? isUnique { get; set; }
    public bool? showOnProductPage { get; set; }
    public bool? descriptionInTooltip { get; set; }

    public void ValidateChanges()
    {
        if (!isRequired.HasValue && !isUnique.HasValue && !showOnProductPage.HasValue &&
            !descriptionInTooltip.HasValue)
            throw ApiException.BadRequest(code: "attributes_changes_required");
    }

    public void ApplyTo(IAttributeDefinitionSettings attribute)
    {
        attribute.isRequired = isRequired ?? attribute.isRequired;
        attribute.isUnique = isUnique ?? attribute.isUnique;
        attribute.showOnProductPage = attribute.isRequired ||
            (showOnProductPage ?? attribute.showOnProductPage);
        attribute.descriptionInTooltip = descriptionInTooltip ?? attribute.descriptionInTooltip;
    }
}
