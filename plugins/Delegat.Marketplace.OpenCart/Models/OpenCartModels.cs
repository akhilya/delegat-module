using System.Text.Json;
using System.Text.Json.Serialization;

namespace Delegat.Marketplace.OpenCart.Models;

public sealed class OpenCartInfoResponse
{
    public string? StoreName { get; set; }
    public string? Version { get; set; }
    public int? StoreId { get; set; }
    public int? LanguageId { get; set; }
    public string? ApiVersion { get; set; }
    public List<string>? Capabilities { get; set; }
}

public sealed class OpenCartCategoryDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ParentId { get; set; }
    public string? ParentName { get; set; }
    public string? FullPath { get; set; }
    public bool HasChildren { get; set; }
    public int? ChildrenCount { get; set; }
    public List<OpenCartCategoryDto>? Children { get; set; }
}

public sealed class OpenCartFieldDto
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsRequired { get; set; }
    public string Type { get; set; } = "string";
    public List<OpenCartFieldOptionDto>? Options { get; set; }
    public string? Description { get; set; }
    public string? Unit { get; set; }
    public int? MaxLength { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public string? ValidationPattern { get; set; }
    public string? Group { get; set; }
    public int SortOrder { get; set; }
}

public sealed class OpenCartFieldOptionDto
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
}

public sealed class OpenCartCreateCategoryRequest
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? ParentId { get; set; }
    public int? StoreId { get; set; }
    public int? LanguageId { get; set; }
    public int SortOrder { get; set; }
    public List<OpenCartCreateCategoryField> Fields { get; set; } = new();
}

public sealed class OpenCartCreateCategoryField
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "string";
    public bool IsRequired { get; set; }
    public string? Description { get; set; }
    public string? Group { get; set; }
    public int SortOrder { get; set; }
    public List<OpenCartFieldOptionDto>? Options { get; set; }
}

public sealed class OpenCartCreateCategoryResponse
{
    public bool Success { get; set; } = true;
    public string? CategoryId { get; set; }
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
    public List<OpenCartFieldDto>? Fields { get; set; }
    public Dictionary<string, object>? Details { get; set; }
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class OpenCartProductPayload
{
    public string? ProductId { get; set; }
    public string? Sku { get; set; }
    public string? Model { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public string? MetaKeyword { get; set; }
    public string? Tag { get; set; }
    public string? Manufacturer { get; set; }
    public string? Barcode { get; set; }
    public bool? Status { get; set; }
    public decimal? Price { get; set; }
    public decimal? SpecialPrice { get; set; }
    public int? Quantity { get; set; }
    public int? Minimum { get; set; }
    public int? SortOrder { get; set; }
    public int? StockStatusId { get; set; }
    public int? TaxClassId { get; set; }
    public bool? Shipping { get; set; }
    public decimal? Length { get; set; }
    public decimal? Width { get; set; }
    public decimal? Height { get; set; }
    public decimal? Weight { get; set; }
    public string? LengthClassId { get; set; }
    public string? WeightClassId { get; set; }
    public int? StoreId { get; set; }
    public int? LanguageId { get; set; }
    public List<string> CategoryIds { get; set; } = new();
    public List<string> ImageUrls { get; set; } = new();
    public Dictionary<string, object?> Attributes { get; set; } = new();
    public Dictionary<string, object?> Options { get; set; } = new();
    public Dictionary<string, object?> RawFields { get; set; } = new();
    public List<OpenCartVariantPayload> Variants { get; set; } = new();
}

public sealed class OpenCartVariantPayload
{
    public string? VariantId { get; set; }
    public string? TechSize { get; set; }
    public string? MarketplaceSize { get; set; }
    public string? Barcode { get; set; }
    public decimal? Price { get; set; }
    public int? Quantity { get; set; }
}

public sealed class OpenCartProductResponse
{
    public bool Success { get; set; } = true;
    public string? ProductId { get; set; }
    public string? Sku { get; set; }
    public string? Message { get; set; }
    public Dictionary<string, object>? Details { get; set; }
}

public sealed class OpenCartProductsResponse
{
    public List<OpenCartProductDto> Items { get; set; } = new();
    public bool HasMore { get; set; }
    public string? NextCursor { get; set; }
}

public sealed class OpenCartProductDto
{
    public string ExternalId { get; set; } = "";
    public string? Sku { get; set; }
    public string? Model { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public decimal? Price { get; set; }
    public int? Quantity { get; set; }
    public string? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public List<string>? ImageUrls { get; set; }
}

public sealed class OpenCartBulkProductRequest
{
    public List<OpenCartBulkProductItem> Items { get; set; } = new();
}

public sealed class OpenCartBulkProductItem
{
    public int LocalProductId { get; set; }
    public string? ExistingExternalId { get; set; }
    public OpenCartProductPayload Product { get; set; } = new();
}

public sealed class OpenCartBulkProductResponse
{
    public List<OpenCartBulkProductResult> Items { get; set; } = new();
}

public sealed class OpenCartBulkProductResult
{
    public int LocalProductId { get; set; }
    public bool Success { get; set; }
    public string? ProductId { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object>? Details { get; set; }
}

public sealed class OpenCartPricePayload
{
    public string ExternalProductId { get; set; } = "";
    public decimal? Price { get; set; }
    public decimal? SpecialPrice { get; set; }
    public string Currency { get; set; } = "RUB";
}

public sealed class OpenCartStockPayload
{
    public string ExternalProductId { get; set; } = "";
    public int Quantity { get; set; }
    public string? WarehouseId { get; set; }
}

public sealed class OpenCartBulkOperationRequest<T>
{
    public List<T> Items { get; set; } = new();
}

public sealed class OpenCartBulkOperationResponse
{
    public int TotalCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public List<OpenCartOperationError>? Errors { get; set; }
}

public sealed class OpenCartOperationError
{
    public string? ExternalId { get; set; }
    public string Message { get; set; } = "";
    public string? Details { get; set; }
}
