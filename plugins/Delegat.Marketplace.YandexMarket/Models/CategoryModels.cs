using System.Text.Json.Serialization;

namespace Delegat.Marketplace.YandexMarket.Models;

public class CategoriesTreeRequest
{
    [JsonPropertyName("language")]
    public string Language { get; set; } = "RU";
}

public class CategoryDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("children")]
    public List<CategoryDto>? Children { get; set; }
}

public class CategoryContentParametersDto
{
    [JsonPropertyName("categoryId")]
    public long CategoryId { get; set; }

    [JsonPropertyName("parameters")]
    public List<CategoryParameterDto>? Parameters { get; set; }
}

public class CategoryParameterDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "TEXT";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("distinctive")]
    public bool Distinctive { get; set; }

    [JsonPropertyName("multivalue")]
    public bool Multivalue { get; set; }

    [JsonPropertyName("allowCustomValues")]
    public bool AllowCustomValues { get; set; }

    [JsonPropertyName("values")]
    public List<ParameterValueOptionDto>? Values { get; set; }

    [JsonPropertyName("constraints")]
    public ParameterConstraintsDto? Constraints { get; set; }

    [JsonPropertyName("unit")]
    public CategoryParameterUnitDto? Unit { get; set; }
}

public class ParameterValueOptionDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class ParameterConstraintsDto
{
    [JsonPropertyName("minValue")]
    public decimal? MinValue { get; set; }

    [JsonPropertyName("maxValue")]
    public decimal? MaxValue { get; set; }

    [JsonPropertyName("maxLength")]
    public int? MaxLength { get; set; }
}

public class CategoryParameterUnitDto
{
    [JsonPropertyName("defaultUnitId")]
    public long? DefaultUnitId { get; set; }

    [JsonPropertyName("units")]
    public List<UnitDto>? Units { get; set; }
}

public class UnitDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("fullName")]
    public string? FullName { get; set; }
}