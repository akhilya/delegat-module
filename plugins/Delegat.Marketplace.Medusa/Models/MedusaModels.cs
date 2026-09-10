using System.Text.Json.Serialization;

namespace Delegat.Marketplace.Medusa.Models;

public sealed class MedusaHealthResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("store_name")]
    public string? StoreName { get; set; }

    [JsonPropertyName("medusa_version")]
    public string? MedusaVersion { get; set; }

    [JsonPropertyName("api_version")]
    public string? ApiVersion { get; set; }

    [JsonPropertyName("capabilities")]
    public List<string>? Capabilities { get; set; }
}

public sealed class MedusaCategoryDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("parent_id")]
    public string? ParentId { get; set; }

    [JsonPropertyName("parent_name")]
    public string? ParentName { get; set; }

    [JsonPropertyName("full_path")]
    public string? FullPath { get; set; }

    [JsonPropertyName("has_children")]
    public bool HasChildren { get; set; }

    [JsonPropertyName("children_count")]
    public int? ChildrenCount { get; set; }
}

public sealed class MedusaCategoriesResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("categories")]
    public List<MedusaCategoryDto>? Categories { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public sealed class MedusaFieldDto
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    [JsonPropertyName("is_required")]
    public bool IsRequired { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("group")]
    public string? Group { get; set; }

    [JsonPropertyName("sort_order")]
    public int SortOrder { get; set; }

    [JsonPropertyName("options")]
    public List<MedusaFieldOptionDto>? Options { get; set; }
}

public sealed class MedusaFieldOptionDto
{
    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";
}

public sealed class MedusaFieldsResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("fields")]
    public List<MedusaFieldDto>? Fields { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public sealed class MedusaUpsertCategoryRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("parent_id")]
    public string? ParentId { get; set; }

    [JsonPropertyName("sort_order")]
    public int SortOrder { get; set; }

    [JsonPropertyName("category_id")]
    public string? CategoryId { get; set; }

    [JsonPropertyName("fields")]
    public List<MedusaFieldDto>? Fields { get; set; }
}

public sealed class MedusaUpsertCategoryResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("category_id")]
    public string? CategoryId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public sealed class MedusaVariantPayload
{
    [JsonPropertyName("variant_id")]
    public string VariantId { get; set; } = "";

    [JsonPropertyName("sku")]
    public string? Sku { get; set; }

    [JsonPropertyName("tech_size")]
    public string? TechSize { get; set; }

    [JsonPropertyName("marketplace_size")]
    public string? MarketplaceSize { get; set; }

    [JsonPropertyName("barcode")]
    public string? Barcode { get; set; }

    [JsonPropertyName("price")]
    public decimal? Price { get; set; }

    [JsonPropertyName("sale_price")]
    public decimal? SalePrice { get; set; }

    [JsonPropertyName("quantity")]
    public int? Quantity { get; set; }

    [JsonPropertyName("attributes")]
    public Dictionary<string, string>? Attributes { get; set; }
}

public sealed class MedusaSpecDto
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";
}

public sealed class MedusaProductPayload
{
    [JsonPropertyName("delegat_product_id")]
    public string? DelegatProductId { get; set; }

    [JsonPropertyName("product_id")]
    public string? ProductId { get; set; }

    [JsonPropertyName("sku")]
    public string? Sku { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("short_description")]
    public string? ShortDescription { get; set; }

    [JsonPropertyName("price")]
    public decimal? Price { get; set; }

    [JsonPropertyName("sale_price")]
    public decimal? SalePrice { get; set; }

    [JsonPropertyName("quantity")]
    public int? Quantity { get; set; }

    [JsonPropertyName("barcode")]
    public string? Barcode { get; set; }

    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "publish";

    [JsonPropertyName("category_ids")]
    public List<string>? CategoryIds { get; set; }

    [JsonPropertyName("group_id")]
    public string? GroupId { get; set; }

    [JsonPropertyName("group_name")]
    public string? GroupName { get; set; }

    [JsonPropertyName("image_urls")]
    public List<string>? ImageUrls { get; set; }

    [JsonPropertyName("image_keys")]
    public List<string>? ImageKeys { get; set; }

    [JsonPropertyName("attributes")]
    public Dictionary<string, string>? Attributes { get; set; }

    [JsonPropertyName("specs")]
    public List<MedusaSpecDto>? Specs { get; set; }

    [JsonPropertyName("variants")]
    public List<MedusaVariantPayload>? Variants { get; set; }

    [JsonPropertyName("raw_fields")]
    public Dictionary<string, object>? RawFields { get; set; }
}

public sealed class MedusaUpsertProductResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("product_id")]
    public string? ProductId { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("variant_ids")]
    public Dictionary<string, string>? VariantIds { get; set; }
}

public sealed class MedusaBulkProductRequest
{
    [JsonPropertyName("products")]
    public List<MedusaProductPayload> Products { get; set; } = new();
}

public sealed class MedusaBulkItemResult
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("product_id")]
    public string? ProductId { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("local_product_id")]
    public int? LocalProductId { get; set; }

    [JsonPropertyName("variant_ids")]
    public Dictionary<string, string>? VariantIds { get; set; }
}

public sealed class MedusaBulkProductResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("results")]
    public List<MedusaBulkItemResult>? Results { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public sealed class MedusaPriceItem
{
    [JsonPropertyName("product_id")]
    public string ProductId { get; set; } = "";

    [JsonPropertyName("variant_id")]
    public string? VariantId { get; set; }

    [JsonPropertyName("price")]
    public decimal? Price { get; set; }

    [JsonPropertyName("sale_price")]
    public decimal? SalePrice { get; set; }
}

public sealed class MedusaStockItem
{
    [JsonPropertyName("product_id")]
    public string ProductId { get; set; } = "";

    [JsonPropertyName("variant_id")]
    public string? VariantId { get; set; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }
}

public sealed class MedusaBulkPricesRequest
{
    [JsonPropertyName("prices")]
    public List<MedusaPriceItem> Prices { get; set; } = new();
}

public sealed class MedusaBulkStocksRequest
{
    [JsonPropertyName("stocks")]
    public List<MedusaStockItem> Stocks { get; set; } = new();
}

public sealed class MedusaBulkMutationResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("results")]
    public List<MedusaBulkItemResult>? Results { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public sealed class MedusaProductGetResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("product_id")]
    public string? ProductId { get; set; }

    [JsonPropertyName("sku")]
    public string? Sku { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price")]
    public decimal? Price { get; set; }

    [JsonPropertyName("quantity")]
    public int? Quantity { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("image_urls")]
    public List<string>? ImageUrls { get; set; }

    [JsonPropertyName("category_id")]
    public string? CategoryId { get; set; }

    [JsonPropertyName("category_name")]
    public string? CategoryName { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public sealed class MedusaUpsertGroupRequest
{
    [JsonPropertyName("collection_id")]
    public string? CollectionId { get; set; }

    [JsonPropertyName("delegat_group_id")]
    public string? DelegatGroupId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("product_ids")]
    public List<string>? ProductIds { get; set; }
}

public sealed class MedusaUpsertGroupResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("collection_id")]
    public string? CollectionId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public sealed class MedusaGroupMembershipRequest
{
    [JsonPropertyName("product_ids")]
    public List<string> ProductIds { get; set; } = new();
}

public sealed class MedusaGroupMembershipResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("added")]
    public int Added { get; set; }

    [JsonPropertyName("removed")]
    public int Removed { get; set; }

    [JsonPropertyName("failed")]
    public int Failed { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public sealed class MedusaGroupDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("product_ids")]
    public List<string>? ProductIds { get; set; }
}

public sealed class MedusaGroupsResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("groups")]
    public List<MedusaGroupDto>? Groups { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public sealed class MedusaDeleteGroupResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("collection_id")]
    public string? CollectionId { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
