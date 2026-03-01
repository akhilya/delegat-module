using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrintO.Marketplace.Wildberries.Models;

#region Cards List

public class WbCardsListRequest
{
    [JsonPropertyName("settings")]
    public WbCardsListSettings Settings { get; set; } = new();
}

public class WbCardsListSettings
{
    [JsonPropertyName("sort")]
    public WbCardsListSort? Sort { get; set; }

    [JsonPropertyName("filter")]
    public WbCardsListFilter? Filter { get; set; }

    [JsonPropertyName("cursor")]
    public WbCardsListCursor Cursor { get; set; } = new();
}

public class WbCardsListSort
{
    [JsonPropertyName("ascending")]
    public bool Ascending { get; set; }
}

public class WbCardsListFilter
{
    [JsonPropertyName("withPhoto")]
    public int WithPhoto { get; set; } = -1;

    [JsonPropertyName("textSearch")]
    public string? TextSearch { get; set; }

    [JsonPropertyName("allowedCategoriesOnly")]
    public bool AllowedCategoriesOnly { get; set; }

    [JsonPropertyName("objectIDs")]
    public List<int>? ObjectIds { get; set; }

    [JsonPropertyName("brands")]
    public List<string>? Brands { get; set; }

    [JsonPropertyName("imtID")]
    public int? ImtId { get; set; }
}

public class WbCardsListCursor
{
    [JsonPropertyName("limit")]
    public int Limit { get; set; } = 100;

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("nmID")]
    public int? NmId { get; set; }
}

public class WbCardsListResponse
{
    [JsonPropertyName("cards")]
    public List<WbCard>? Cards { get; set; }

    [JsonPropertyName("cursor")]
    public WbCardsListCursorResponse? Cursor { get; set; }
}

public class WbCardsListCursorResponse
{
    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("nmID")]
    public int NmId { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

#endregion

#region Card

public class WbCard
{
    [JsonPropertyName("nmID")]
    public int NmId { get; set; }

    [JsonPropertyName("imtID")]
    public int ImtId { get; set; }

    [JsonPropertyName("nmUUID")]
    public string? NmUuid { get; set; }

    [JsonPropertyName("subjectID")]
    public int SubjectId { get; set; }

    [JsonPropertyName("subjectName")]
    public string? SubjectName { get; set; }

    [JsonPropertyName("vendorCode")]
    public string VendorCode { get; set; } = "";

    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("photos")]
    public List<WbPhoto>? Photos { get; set; }

    [JsonPropertyName("video")]
    public string? Video { get; set; }

    [JsonPropertyName("dimensions")]
    public WbDimensions? Dimensions { get; set; }

    [JsonPropertyName("characteristics")]
    public List<WbCardCharacteristic>? Characteristics { get; set; }

    [JsonPropertyName("sizes")]
    public List<WbCardSize>? Sizes { get; set; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }
}

public class WbPhoto
{
    [JsonPropertyName("big")]
    public string? Big { get; set; }

    [JsonPropertyName("c246x328")]
    public string? C246x328 { get; set; }

    [JsonPropertyName("c516x688")]
    public string? C516x688 { get; set; }

    [JsonPropertyName("square")]
    public string? Square { get; set; }

    [JsonPropertyName("tm")]
    public string? Tm { get; set; }
}

public class WbDimensions
{
    [JsonPropertyName("length")]
    public int Length { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("weightBrutto")]
    public double WeightBrutto { get; set; }

    [JsonPropertyName("isValid")]
    public bool? IsValid { get; set; }
}

public class WbCardCharacteristic
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("value")]
    public JsonElement? Value { get; set; }
}

public class WbCardSize
{
    [JsonPropertyName("chrtID")]
    public int ChrtId { get; set; }

    [JsonPropertyName("techSize")]
    public string? TechSize { get; set; }

    [JsonPropertyName("wbSize")]
    public string? WbSize { get; set; }

    [JsonPropertyName("skus")]
    public List<string>? Skus { get; set; }

    [JsonPropertyName("price")]
    public int? Price { get; set; }
}

#endregion

#region Card Create

public class WbCardCreateGroup
{
    [JsonPropertyName("subjectID")]
    public int SubjectId { get; set; }

    [JsonPropertyName("variants")]
    public List<WbCardVariant> Variants { get; set; } = new();
}

public class WbCardVariant
{
    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("vendorCode")]
    public string VendorCode { get; set; } = "";

    [JsonPropertyName("dimensions")]
    public WbDimensions? Dimensions { get; set; }

    [JsonPropertyName("sizes")]
    public List<WbCardCreateSize>? Sizes { get; set; }

    [JsonPropertyName("characteristics")]
    public List<WbCardCreateCharacteristic>? Characteristics { get; set; }
}

public class WbCardCreateSize
{
    [JsonPropertyName("techSize")]
    public string? TechSize { get; set; }

    [JsonPropertyName("wbSize")]
    public string? WbSize { get; set; }

    [JsonPropertyName("price")]
    public int Price { get; set; }

    [JsonPropertyName("skus")]
    public List<string>? Skus { get; set; }
}

public class WbCardCreateCharacteristic
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("value")]
    public object? Value { get; set; }
}

public class WbCardCreateResponse
{
    [JsonPropertyName("data")]
    public object? Data { get; set; }

    [JsonPropertyName("error")]
    public bool Error { get; set; }

    [JsonPropertyName("errorText")]
    public string? ErrorText { get; set; }

    [JsonPropertyName("additionalErrors")]
    public object? AdditionalErrors { get; set; }
}

#endregion

#region Card Update

public class WbCardUpdateItem
{
    [JsonPropertyName("nmID")]
    public int NmId { get; set; }

    [JsonPropertyName("vendorCode")]
    public string VendorCode { get; set; } = "";

    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("dimensions")]
    public WbDimensions? Dimensions { get; set; }

    [JsonPropertyName("characteristics")]
    public List<WbCardCreateCharacteristic>? Characteristics { get; set; }

    [JsonPropertyName("sizes")]
    public List<WbCardUpdateSize>? Sizes { get; set; }
}

public class WbCardUpdateSize
{
    [JsonPropertyName("chrtID")]
    public int? ChrtId { get; set; }

    [JsonPropertyName("techSize")]
    public string? TechSize { get; set; }

    [JsonPropertyName("wbSize")]
    public string? WbSize { get; set; }

    [JsonPropertyName("price")]
    public int? Price { get; set; }

    [JsonPropertyName("skus")]
    public List<string>? Skus { get; set; }
}

#endregion

#region Card Errors

public class WbCardErrorListRequest
{
    [JsonPropertyName("cursor")]
    public WbCardErrorCursor? Cursor { get; set; }

    [JsonPropertyName("order")]
    public WbCardErrorOrder? Order { get; set; }
}

public class WbCardErrorCursor
{
    [JsonPropertyName("limit")]
    public int Limit { get; set; } = 100;

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("batchUUID")]
    public string? BatchUuid { get; set; }
}

public class WbCardErrorOrder
{
    [JsonPropertyName("ascending")]
    public bool Ascending { get; set; } = true;
}

public class WbCardErrorListResponse
{
    [JsonPropertyName("data")]
    public WbCardErrorData? Data { get; set; }

    [JsonPropertyName("error")]
    public bool Error { get; set; }

    [JsonPropertyName("errorText")]
    public string? ErrorText { get; set; }
}

public class WbCardErrorData
{
    [JsonPropertyName("items")]
    public List<WbCardErrorBatch>? Items { get; set; }

    [JsonPropertyName("cursor")]
    public WbCardErrorCursorResponse? Cursor { get; set; }
}

public class WbCardErrorBatch
{
    [JsonPropertyName("batchUUID")]
    public string? BatchUuid { get; set; }

    [JsonPropertyName("vendorCodes")]
    public List<string>? VendorCodes { get; set; }

    [JsonPropertyName("errors")]
    public Dictionary<string, List<string>>? Errors { get; set; }
}

public class WbCardErrorCursorResponse
{
    [JsonPropertyName("next")]
    public bool Next { get; set; }

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("batchUUID")]
    public string? BatchUuid { get; set; }
}

#endregion

#region Card Add to Merge Group

public class WbCardAddToMergeRequest
{
    [JsonPropertyName("imtID")]
    public int ImtId { get; set; }

    [JsonPropertyName("cardsToAdd")]
    public List<WbCardVariant> CardsToAdd { get; set; } = new();
}

#endregion

#region Card Limits

public class WbCardLimitsResponse
{
    [JsonPropertyName("data")]
    public WbCardLimitsData? Data { get; set; }

    [JsonPropertyName("error")]
    public bool Error { get; set; }

    [JsonPropertyName("errorText")]
    public string? ErrorText { get; set; }
}

public class WbCardLimitsData
{
    [JsonPropertyName("freeLimits")]
    public int FreeLimits { get; set; }

    [JsonPropertyName("paidLimits")]
    public int PaidLimits { get; set; }
}

#endregion
