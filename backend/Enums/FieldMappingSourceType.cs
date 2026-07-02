namespace Delegat.Enums;

public enum FieldMappingSourceType
{
    ProductField = 0,
    CategoryAttribute = 1,
    StoreAttribute = 2,
    Static = 3,
    /// <summary>
    /// Product group name (for grouping fields on marketplaces, e.g. Ozon 9048)
    /// </summary>
    GroupName = 4
}
