using System.Linq.Expressions;
using Delegat.Enums;

namespace Delegat.Models;

/// <summary>List-only metadata. SQL projections deliberately exclude dictionaries and editor settings.</summary>
public sealed class AttributeDefinitionSummary
{
    public int id { get; set; }
    public string name { get; set; } = "";
    public string key { get; set; } = "";
    public AttributeType type { get; set; }
    public string? unitCode { get; set; }
    public bool allowMultipleFiles { get; set; }
    public bool isRequired { get; set; }
    public bool isUnique { get; set; }
    public bool showOnProductPage { get; set; }
    public int sortOrder { get; set; }
    public string? group { get; set; }
    public string? groupIcon { get; set; }

    public static readonly Expression<Func<CategoryAttribute, AttributeDefinitionSummary>> FromCategory = a => new()
    {
        id = a.Id, name = a.name, key = a.key, type = a.type, unitCode = a.unitCode, allowMultipleFiles = a.type == AttributeType.File && a.allowMultipleFiles,
        isRequired = a.isRequired, isUnique = a.isUnique,
        showOnProductPage = a.isRequired || a.showOnProductPage, sortOrder = a.sortOrder,
        group = a.group, groupIcon = a.groupIcon,
    };

    public static readonly Expression<Func<StoreAttribute, AttributeDefinitionSummary>> FromStore = a => new()
    {
        id = a.Id, name = a.name, key = a.key, type = a.type, unitCode = a.unitCode, allowMultipleFiles = a.type == AttributeType.File && a.allowMultipleFiles,
        isRequired = a.isRequired, isUnique = a.isUnique,
        showOnProductPage = a.isRequired || a.showOnProductPage, sortOrder = a.sortOrder,
    };
}
