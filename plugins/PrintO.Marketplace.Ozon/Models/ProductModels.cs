using System.Text.Json.Serialization;

namespace PrintO.Marketplace.Ozon.Models;

#region Request Models

/// <summary>
/// Request for /v3/product/list
/// </summary>
public class ProductListRequest
{
    [JsonPropertyName("filter")]
    public ProductListFilter? Filter { get; set; }

    [JsonPropertyName("last_id")]
    public string LastId { get; set; } = "";

    [JsonPropertyName("limit")]
    public int Limit { get; set; } = 100;
}

public class ProductListFilter
{
    [JsonPropertyName("offer_id")]
    public List<string>? OfferId { get; set; }

    [JsonPropertyName("product_id")]
    public List<string>? ProductId { get; set; }

    [JsonPropertyName("visibility")]
    public string Visibility { get; set; } = "ALL";
}

/// <summary>
/// Request for /v3/product/info/list
/// </summary>
public class ProductInfoListRequest
{
    [JsonPropertyName("offer_id")]
    public List<string>? OfferId { get; set; }

    [JsonPropertyName("product_id")]
    public List<string>? ProductId { get; set; }

    [JsonPropertyName("sku")]
    public List<string>? Sku { get; set; }
}

/// <summary>
/// Request for /v4/product/info/attributes
/// </summary>
public class ProductAttributesRequest
{
    [JsonPropertyName("filter")]
    public ProductAttributesFilter? Filter { get; set; }

    [JsonPropertyName("last_id")]
    public string LastId { get; set; } = "";

    [JsonPropertyName("limit")]
    public int Limit { get; set; } = 100;

    [JsonPropertyName("sort_dir")]
    public string SortDir { get; set; } = "ASC";
}

public class ProductAttributesFilter
{
    [JsonPropertyName("offer_id")]
    public List<string>? OfferId { get; set; }

    [JsonPropertyName("product_id")]
    public List<string>? ProductId { get; set; }

    [JsonPropertyName("visibility")]
    public string Visibility { get; set; } = "ALL";
}

/// <summary>
/// Request for /v3/product/import
/// </summary>
public class ProductImportRequest
{
    [JsonPropertyName("items")]
    public List<ProductImportItem> Items { get; set; } = new();
}

public class ProductImportItem
{
    [JsonPropertyName("attributes")]
    public List<ProductAttribute> Attributes { get; set; } = new();

    [JsonPropertyName("barcode")]
    public string? Barcode { get; set; }

    [JsonPropertyName("description_category_id")]
    public long DescriptionCategoryId { get; set; }

    [JsonPropertyName("type_id")]
    public long TypeId { get; set; }

    [JsonPropertyName("color_image")]
    public string? ColorImage { get; set; }

    [JsonPropertyName("complex_attributes")]
    public List<ComplexAttribute>? ComplexAttributes { get; set; }

    [JsonPropertyName("currency_code")]
    public string CurrencyCode { get; set; } = "RUB";

    [JsonPropertyName("depth")]
    public int? Depth { get; set; }

    [JsonPropertyName("dimension_unit")]
    public string DimensionUnit { get; set; } = "mm";

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("images")]
    public List<string>? Images { get; set; }

    [JsonPropertyName("images360")]
    public List<string>? Images360 { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("offer_id")]
    public string OfferId { get; set; } = "";

    [JsonPropertyName("old_price")]
    public string? OldPrice { get; set; }

    [JsonPropertyName("pdf_list")]
    public List<OzonPdf>? PdfList { get; set; }

    [JsonPropertyName("premium_price")]
    public string? PremiumPrice { get; set; }

    [JsonPropertyName("price")]
    public string Price { get; set; } = "0";

    [JsonPropertyName("primary_image")]
    public string? PrimaryImage { get; set; }

    [JsonPropertyName("vat")]
    public string Vat { get; set; } = "0";

    [JsonPropertyName("weight")]
    public int? Weight { get; set; }

    [JsonPropertyName("weight_unit")]
    public string WeightUnit { get; set; } = "g";

    [JsonPropertyName("width")]
    public int? Width { get; set; }
}

public class ProductAttribute
{
    [JsonPropertyName("complex_id")]
    public int ComplexId { get; set; } = 0;

    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("values")]
    public List<AttributeValue> Values { get; set; } = new();
}

public class AttributeValue
{
    [JsonPropertyName("dictionary_value_id")]
    public long DictionaryValueId { get; set; }

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";
}

public class ComplexAttribute
{
    [JsonPropertyName("attributes")]
    public List<ProductAttribute> Attributes { get; set; } = new();
}

public class OzonPdf
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("src_url")]
    public string SrcUrl { get; set; } = "";
}

/// <summary>
/// Request for /v1/product/import/info
/// </summary>
public class ImportInfoRequest
{
    [JsonPropertyName("task_id")]
    public long TaskId { get; set; }
}

/// <summary>
/// Request for /v1/product/update/offer-id
/// </summary>
public class UpdateOfferIdRequest
{
    [JsonPropertyName("update_offer_id")]
    public List<UpdateOfferIdItem> UpdateOfferId { get; set; } = new();
}

public class UpdateOfferIdItem
{
    [JsonPropertyName("new_offer_id")]
    public string NewOfferId { get; set; } = "";

    [JsonPropertyName("offer_id")]
    public string OfferId { get; set; } = "";
}

#endregion

#region Response Models

/// <summary>
/// Response from /v3/product/list
/// </summary>
public class ProductListResponse
{
    [JsonPropertyName("result")]
    public ProductListResult Result { get; set; } = new();
}

public class ProductListResult
{
    [JsonPropertyName("items")]
    public List<ProductListItem> Items { get; set; } = new();

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("last_id")]
    public string LastId { get; set; } = "";
}

public class ProductListItem
{
    [JsonPropertyName("product_id")]
    public long ProductId { get; set; }

    [JsonPropertyName("offer_id")]
    public string OfferId { get; set; } = "";

    [JsonPropertyName("is_fbo_visible")]
    public bool IsFboVisible { get; set; }

    [JsonPropertyName("is_fbs_visible")]
    public bool IsFbsVisible { get; set; }

    [JsonPropertyName("archived")]
    public bool Archived { get; set; }

    [JsonPropertyName("is_discounted")]
    public bool IsDiscounted { get; set; }
}

/// <summary>
/// Response from /v3/product/info/list
/// </summary>
/// <summary>
/// Response from /v3/product/info/list
/// Note: This endpoint returns items directly without a "result" wrapper
/// </summary>
public class ProductInfoListResponse
{
    [JsonPropertyName("items")]
    public List<ProductInfoItem> Items { get; set; } = new();
}

public class ProductInfoItem
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("offer_id")]
    public string OfferId { get; set; } = "";

    [JsonPropertyName("barcode")]
    public string? Barcode { get; set; }

    [JsonPropertyName("barcodes")]
    public List<string>? Barcodes { get; set; }

    [JsonPropertyName("buybox_price")]
    public string? BuyboxPrice { get; set; }

    [JsonPropertyName("description_category_id")]
    public long DescriptionCategoryId { get; set; }

    [JsonPropertyName("type_id")]
    public long TypeId { get; set; }

    /// <summary>
    /// Старый формат категории (для товаров до миграции на description_category_id)
    /// </summary>
    [JsonPropertyName("category_id")]
    public long CategoryId { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("images")]
    public List<string>? Images { get; set; }

    [JsonPropertyName("marketing_price")]
    public string? MarketingPrice { get; set; }

    [JsonPropertyName("min_price")]
    public string? MinPrice { get; set; }

    [JsonPropertyName("old_price")]
    public string? OldPrice { get; set; }

    [JsonPropertyName("premium_price")]
    public string? PremiumPrice { get; set; }

    [JsonPropertyName("price")]
    public string? Price { get; set; }

    [JsonPropertyName("recommended_price")]
    public string? RecommendedPrice { get; set; }

    [JsonPropertyName("min_ozon_price")]
    public string? MinOzonPrice { get; set; }

    [JsonPropertyName("sources")]
    public List<ProductSource>? Sources { get; set; }

    [JsonPropertyName("stocks")]
    public ProductStocks? Stocks { get; set; }

    [JsonPropertyName("errors")]
    public List<ProductError>? Errors { get; set; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("vat")]
    public string? Vat { get; set; }

    [JsonPropertyName("visible")]
    public bool Visible { get; set; }

    [JsonPropertyName("visibility_details")]
    public VisibilityDetails? VisibilityDetails { get; set; }

    [JsonPropertyName("price_indexes")]
    public PriceIndexes? PriceIndexes { get; set; }

    [JsonPropertyName("commissions")]
    public List<Commission>? Commissions { get; set; }

    [JsonPropertyName("volume_weight")]
    public decimal? VolumeWeight { get; set; }

    [JsonPropertyName("is_prepayment")]
    public bool IsPrepayment { get; set; }

    [JsonPropertyName("is_prepayment_allowed")]
    public bool IsPrepaymentAllowed { get; set; }

    [JsonPropertyName("images360")]
    public List<string>? Images360 { get; set; }

    [JsonPropertyName("is_kgt")]
    public bool IsKgt { get; set; }

    [JsonPropertyName("color_image")]
    public List<string>? ColorImage { get; set; }

    [JsonPropertyName("primary_image")]
    public List<string>? PrimaryImage { get; set; }

    [JsonPropertyName("status")]
    public ProductStatus? Status { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("service_type")]
    public string? ServiceType { get; set; }

    [JsonPropertyName("fbo_sku")]
    public long FboSku { get; set; }

    [JsonPropertyName("fbs_sku")]
    public long FbsSku { get; set; }

    [JsonPropertyName("currency_code")]
    public string CurrencyCode { get; set; } = "RUB";

    [JsonPropertyName("is_archived")]
    public bool IsArchived { get; set; }

    [JsonPropertyName("is_autoarchived")]
    public bool IsAutoarchived { get; set; }
}

public class ProductSource
{
    [JsonPropertyName("is_enabled")]
    public bool IsEnabled { get; set; }

    [JsonPropertyName("sku")]
    public long Sku { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "";
}

public class ProductStocks
{
    [JsonPropertyName("coming")]
    public int Coming { get; set; }

    [JsonPropertyName("present")]
    public int Present { get; set; }

    [JsonPropertyName("reserved")]
    public int Reserved { get; set; }
}

public class ProductError
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("state")]
    public string State { get; set; } = "";

    [JsonPropertyName("level")]
    public string Level { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("field")]
    public string Field { get; set; } = "";

    [JsonPropertyName("attribute_id")]
    public long AttributeId { get; set; }

    [JsonPropertyName("attribute_name")]
    public string AttributeName { get; set; } = "";

    [JsonPropertyName("optional_description_elements")]
    public object? OptionalDescriptionElements { get; set; }
}

public class VisibilityDetails
{
    [JsonPropertyName("has_price")]
    public bool HasPrice { get; set; }

    [JsonPropertyName("has_stock")]
    public bool HasStock { get; set; }

    [JsonPropertyName("active_product")]
    public bool ActiveProduct { get; set; }

    [JsonPropertyName("reasons")]
    public object? Reasons { get; set; }
}

public class PriceIndexes
{
    [JsonPropertyName("price_index")]
    public string? PriceIndex { get; set; }

    [JsonPropertyName("external_index_data")]
    public ExternalIndexData? ExternalIndexData { get; set; }

    [JsonPropertyName("ozon_index_data")]
    public OzonIndexData? OzonIndexData { get; set; }

    [JsonPropertyName("self_marketplaces_index_data")]
    public SelfMarketplacesIndexData? SelfMarketplacesIndexData { get; set; }
}

public class ExternalIndexData
{
    [JsonPropertyName("minimal_price")]
    public string? MinimalPrice { get; set; }

    [JsonPropertyName("minimal_price_currency")]
    public string? MinimalPriceCurrency { get; set; }

    [JsonPropertyName("price_index_value")]
    public decimal? PriceIndexValue { get; set; }
}

public class OzonIndexData
{
    [JsonPropertyName("minimal_price")]
    public string? MinimalPrice { get; set; }

    [JsonPropertyName("minimal_price_currency")]
    public string? MinimalPriceCurrency { get; set; }

    [JsonPropertyName("price_index_value")]
    public decimal? PriceIndexValue { get; set; }
}

public class SelfMarketplacesIndexData
{
    [JsonPropertyName("minimal_price")]
    public string? MinimalPrice { get; set; }

    [JsonPropertyName("minimal_price_currency")]
    public string? MinimalPriceCurrency { get; set; }

    [JsonPropertyName("price_index_value")]
    public decimal? PriceIndexValue { get; set; }
}

public class Commission
{
    [JsonPropertyName("percent")]
    public decimal Percent { get; set; }

    [JsonPropertyName("min_value")]
    public decimal MinValue { get; set; }

    [JsonPropertyName("value")]
    public decimal Value { get; set; }

    [JsonPropertyName("sale_schema")]
    public string SaleSchema { get; set; } = "";

    [JsonPropertyName("delivery_amount")]
    public decimal DeliveryAmount { get; set; }

    [JsonPropertyName("return_amount")]
    public decimal ReturnAmount { get; set; }
}

public class ProductStatus
{
    [JsonPropertyName("state")]
    public string State { get; set; } = "";

    [JsonPropertyName("state_failed")]
    public string StateFailed { get; set; } = "";

    [JsonPropertyName("moderate_status")]
    public string ModerateStatus { get; set; } = "";

    [JsonPropertyName("decline_reasons")]
    public List<string>? DeclineReasons { get; set; }

    [JsonPropertyName("validation_state")]
    public string ValidationState { get; set; } = "";

    [JsonPropertyName("state_name")]
    public string StateName { get; set; } = "";

    [JsonPropertyName("state_description")]
    public string StateDescription { get; set; } = "";

    [JsonPropertyName("is_failed")]
    public bool IsFailed { get; set; }

    [JsonPropertyName("is_created")]
    public bool IsCreated { get; set; }

    [JsonPropertyName("state_tooltip")]
    public string? StateTooltip { get; set; }

    [JsonPropertyName("item_errors")]
    public List<ProductError>? ItemErrors { get; set; }

    [JsonPropertyName("state_updated_at")]
    public string? StateUpdatedAt { get; set; }
}

/// <summary>
/// Response from /v4/product/info/attributes
/// </summary>
public class ProductAttributesResponse
{
    [JsonPropertyName("result")]
    public List<ProductAttributesItem> Result { get; set; } = new();

    [JsonPropertyName("last_id")]
    public string LastId { get; set; } = "";

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

public class ProductAttributesItem
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("barcode")]
    public string? Barcode { get; set; }

    [JsonPropertyName("description_category_id")]
    public long DescriptionCategoryId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("offer_id")]
    public string OfferId { get; set; } = "";

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("depth")]
    public int Depth { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("dimension_unit")]
    public string DimensionUnit { get; set; } = "";

    [JsonPropertyName("weight")]
    public int Weight { get; set; }

    [JsonPropertyName("weight_unit")]
    public string WeightUnit { get; set; } = "";

    [JsonPropertyName("images")]
    public List<string>? Images { get; set; }

    [JsonPropertyName("images360")]
    public List<string>? Images360 { get; set; }

    [JsonPropertyName("pdf_list")]
    public List<OzonPdf>? PdfList { get; set; }

    [JsonPropertyName("attributes")]
    public List<ProductAttributeInfo> Attributes { get; set; } = new();

    [JsonPropertyName("complex_attributes")]
    public List<ComplexAttributeInfo>? ComplexAttributes { get; set; }

    [JsonPropertyName("color_image")]
    public string? ColorImage { get; set; }

    [JsonPropertyName("last_id")]
    public string? LastId { get; set; }

    [JsonPropertyName("type_id")]
    public long TypeId { get; set; }
}

public class ProductImage
{
    [JsonPropertyName("file_name")]
    public string FileName { get; set; } = "";

    [JsonPropertyName("default")]
    public bool Default { get; set; }

    [JsonPropertyName("index")]
    public int Index { get; set; }
}

public class ProductAttributeInfo
{
    [JsonPropertyName("id")]  // Ozon API v4 returns "id", not "attribute_id"
    public long AttributeId { get; set; }

    [JsonPropertyName("complex_id")]
    public int ComplexId { get; set; }

    [JsonPropertyName("values")]
    public List<AttributeValueInfo> Values { get; set; } = new();
}

public class AttributeValueInfo
{
    [JsonPropertyName("dictionary_value_id")]
    public long DictionaryValueId { get; set; }

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";
}

public class ComplexAttributeInfo
{
    [JsonPropertyName("attributes")]
    public List<ProductAttributeInfo> Attributes { get; set; } = new();
}

/// <summary>
/// Response from /v3/product/import
/// </summary>
public class ProductImportResponse
{
    [JsonPropertyName("result")]
    public ProductImportResult Result { get; set; } = new();
}

public class ProductImportResult
{
    [JsonPropertyName("task_id")]
    public long TaskId { get; set; }
}

/// <summary>
/// Response from /v1/product/import/info
/// </summary>
public class ImportInfoResponse
{
    [JsonPropertyName("result")]
    public ImportInfoResult Result { get; set; } = new();
}

public class ImportInfoResult
{
    [JsonPropertyName("items")]
    public List<ImportInfoItem> Items { get; set; } = new();

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

public class ImportInfoItem
{
    [JsonPropertyName("offer_id")]
    public string OfferId { get; set; } = "";

    [JsonPropertyName("product_id")]
    public long ProductId { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = ""; // pending, imported, failed

    [JsonPropertyName("errors")]
    public List<ImportItemError>? Errors { get; set; }
}

public class ImportItemError
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("state")]
    public string State { get; set; } = "";

    [JsonPropertyName("level")]
    public string Level { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("field")]
    public string Field { get; set; } = "";

    [JsonPropertyName("attribute_id")]
    public long AttributeId { get; set; }

    [JsonPropertyName("attribute_name")]
    public string AttributeName { get; set; } = "";
}

/// <summary>
/// Response from /v1/product/update/offer-id
/// </summary>
public class UpdateOfferIdResponse
{
    [JsonPropertyName("result")]
    public List<UpdateOfferIdResult> Result { get; set; } = new();
}

public class UpdateOfferIdResult
{
    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = new();
}

#endregion
