using System.Text.Json;
using System.Text.Json.Serialization;

namespace Delegat.Services.Ai;

/// <summary>
/// One canonical live-turn event. Producers create events with type + data only; the
/// live turn stamps trusted RunId/MessageId/DbId/Phase/ProposalId/Sequence before the
/// event is buffered or delivered. Serialized shape keeps the existing {type, data}
/// contract - the identity fields are additive and ignored by older clients.
/// </summary>
public sealed record AiTurnEvent(string Type, JsonElement Data)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string? RunId { get; init; }
    public string? MessageId { get; init; }
    public long? Sequence { get; init; }
    public int? DbId { get; init; }
    // chat, execution, review
    public string? Phase { get; init; }
    // execution target, NOT the review's parent linkage
    public string? ProposalId { get; init; }

    public static AiTurnEvent Create(string type, object data) =>
        new(type, JsonSerializer.SerializeToElement(data, JsonOptions));

    public string ToSse() => $"data: {JsonSerializer.Serialize(this, JsonOptions)}\n\n";
}
