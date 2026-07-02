using System.Text.Json.Serialization;

namespace Delegat.Marketplace.Ozon.Models;

#region Request Models

/// <summary>
/// Request for /v1/description-category/tree
/// </summary>
public class CategoryTreeRequest
{
    [JsonPropertyName("language")]
    public string Language { get; set; } = "RU";
}

/// <summary>
/// Request for /v1/description-category/attribute
/// </summary>
public class CategoryAttributeRequest
{
    [JsonPropertyName("description_category_id")]
    public long DescriptionCategoryId { get; set; }

    [JsonPropertyName("type_id")]
    public long TypeId { get; set; }

    [JsonPropertyName("language")]
    public string Language { get; set; } = "RU";
}

/// <summary>
/// Request for /v1/description-category/attribute/values
/// </summary>
public class AttributeValuesRequest
{
    [JsonPropertyName("description_category_id")]
    public long DescriptionCategoryId { get; set; }

    [JsonPropertyName("type_id")]
    public long TypeId { get; set; }

    [JsonPropertyName("attribute_id")]
    public long AttributeId { get; set; }

    [JsonPropertyName("language")]
    public string Language { get; set; } = "RU";

    [JsonPropertyName("last_value_id")]
    public long LastValueId { get; set; } = 0;

    [JsonPropertyName("limit")]
    public long Limit { get; set; } = 1000;
}

#endregion

#region Response Models

/// <summary>
/// Response from /v1/description-category/tree
/// </summary>
public class CategoryTreeResponse
{
    [JsonPropertyName("result")]
    public List<OzonCategoryNode> Result { get; set; } = new();
}

/// <summary>
/// Category node in the tree (can be a category or type)
/// </summary>
public class OzonCategoryNode
{
    [JsonPropertyName("description_category_id")]
    public long DescriptionCategoryId { get; set; }

    [JsonPropertyName("category_name")]
    public string? CategoryName { get; set; }

    [JsonPropertyName("type_id")]
    public long TypeId { get; set; }

    [JsonPropertyName("type_name")]
    public string? TypeName { get; set; }

    [JsonPropertyName("disabled")]
    public bool Disabled { get; set; }

    [JsonPropertyName("children")]
    public List<OzonCategoryNode> Children { get; set; } = new();

    /// <summary>
    /// Gets display name (category_name for categories, type_name for types)
    /// </summary>
    public string DisplayName => !string.IsNullOrEmpty(CategoryName) ? CategoryName : TypeName ?? "";

    /// <summary>
    /// Indicates if this is a leaf type node (not a category)
    /// </summary>
    public bool IsTypeNode => !string.IsNullOrEmpty(TypeName) && TypeId > 0;
}

/// <summary>
/// Response from /v1/description-category/attribute
/// </summary>
public class CategoryAttributeResponse
{
    [JsonPropertyName("result")]
    public List<OzonAttribute> Result { get; set; } = new();
}

/// <summary>
/// Attribute definition for a category
/// </summary>
public class OzonAttribute
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = ""; // String, Integer, Decimal, Boolean, URL, etc.

    [JsonPropertyName("is_collection")]
    public bool IsCollection { get; set; }

    [JsonPropertyName("is_required")]
    public bool IsRequired { get; set; }

    [JsonPropertyName("is_aspect")]
    public bool IsAspect { get; set; }

    [JsonPropertyName("max_value_count")]
    public int MaxValueCount { get; set; }

    [JsonPropertyName("group_id")]
    public long GroupId { get; set; }

    [JsonPropertyName("group_name")]
    public string? GroupName { get; set; }

    [JsonPropertyName("dictionary_id")]
    public long DictionaryId { get; set; }

    [JsonPropertyName("category_dependent")]
    public bool CategoryDependent { get; set; }
}

/// <summary>
/// Response from /v1/description-category/attribute/values
/// </summary>
public class AttributeValuesResponse
{
    [JsonPropertyName("result")]
    public List<OzonAttributeValue> Result { get; set; } = new();

    [JsonPropertyName("has_next")]
    public bool HasNext { get; set; }
}

/// <summary>
/// Dictionary value for an attribute
/// </summary>
public class OzonAttributeValue
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("info")]
    public string? Info { get; set; }

    [JsonPropertyName("picture")]
    public string? Picture { get; set; }
}

#endregion
