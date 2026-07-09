namespace Delegat.Models.Ai;

public class AiStoreSettings
{
    public int Id { get; set; }
    public int StoreId { get; set; }

    /// <summary>Free-text description of the store, injected into the system prompt for context.</summary>
    public string StoreDescription { get; set; } = "";

    /// <summary>professional | friendly | expert</summary>
    public string CommunicationStyle { get; set; } = "professional";

    /// <summary>brief | balanced | detailed</summary>
    public string ResponseBrevity { get; set; } = "balanced";

    /// <summary>none | minimal | standard</summary>
    public string EmojiUsage { get; set; } = "none";

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
