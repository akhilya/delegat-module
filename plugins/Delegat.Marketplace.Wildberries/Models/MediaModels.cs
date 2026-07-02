using System.Text.Json.Serialization;

namespace Delegat.Marketplace.Wildberries.Models;

public class WbMediaSaveRequest
{
    [JsonPropertyName("nmId")]
    public int NmId { get; set; }

    [JsonPropertyName("data")]
    public List<string> Data { get; set; } = new();
}

public class WbMediaSaveResponse
{
    [JsonPropertyName("data")]
    public object? Data { get; set; }

    [JsonPropertyName("error")]
    public bool Error { get; set; }

    [JsonPropertyName("errorText")]
    public string? ErrorText { get; set; }
}
