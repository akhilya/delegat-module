using System.Text.Json.Serialization;

namespace PrintO.Marketplace.Wildberries.Models;

#region Parent Categories

public class WbParentCategoriesResponse
{
    [JsonPropertyName("data")]
    public List<WbParentCategory>? Data { get; set; }

    [JsonPropertyName("error")]
    public bool Error { get; set; }

    [JsonPropertyName("errorText")]
    public string? ErrorText { get; set; }
}

public class WbParentCategory
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("isVisible")]
    public bool IsVisible { get; set; }
}

#endregion

#region Subjects

public class WbSubjectsResponse
{
    [JsonPropertyName("data")]
    public List<WbSubject>? Data { get; set; }

    [JsonPropertyName("error")]
    public bool Error { get; set; }

    [JsonPropertyName("errorText")]
    public string? ErrorText { get; set; }
}

public class WbSubject
{
    [JsonPropertyName("subjectID")]
    public int SubjectId { get; set; }

    [JsonPropertyName("parentID")]
    public int ParentId { get; set; }

    [JsonPropertyName("subjectName")]
    public string SubjectName { get; set; } = "";

    [JsonPropertyName("parentName")]
    public string ParentName { get; set; } = "";
}

#endregion

#region Characteristics

public class WbCharacteristicsResponse
{
    [JsonPropertyName("data")]
    public List<WbCharacteristic>? Data { get; set; }

    [JsonPropertyName("error")]
    public bool Error { get; set; }

    [JsonPropertyName("errorText")]
    public string? ErrorText { get; set; }
}

public class WbCharacteristic
{
    [JsonPropertyName("charcID")]
    public int CharcId { get; set; }

    [JsonPropertyName("subjectName")]
    public string? SubjectName { get; set; }

    [JsonPropertyName("subjectID")]
    public int SubjectId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("unitName")]
    public string? UnitName { get; set; }

    [JsonPropertyName("maxCount")]
    public int MaxCount { get; set; }

    [JsonPropertyName("popular")]
    public bool Popular { get; set; }

    [JsonPropertyName("charcType")]
    public int CharcType { get; set; }
}

#endregion

#region Directory (Colors, Kinds, Countries, Seasons)

public class WbDirectoryResponse
{
    [JsonPropertyName("data")]
    public List<WbDirectoryItem>? Data { get; set; }

    [JsonPropertyName("error")]
    public bool Error { get; set; }

    [JsonPropertyName("errorText")]
    public string? ErrorText { get; set; }
}

public class WbDirectoryItem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("parentName")]
    public string? ParentName { get; set; }
}

#endregion
