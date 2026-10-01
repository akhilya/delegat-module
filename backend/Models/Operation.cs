namespace Delegat.Models;

public class Operation
{
    public int Id { get; set; }
    public string operationId { get; set; } = null!;
    public int storeId { get; set; }
    public int authorUserId { get; set; }

    /// <summary>
    /// Set when a Public API key started the operation. The cabinet shows API plus this key, not the user who created the key.
    /// </summary>
    public int? authorApiKeyId { get; set; }
    public string type { get; set; } = null!;
    public string title { get; set; } = null!;
    public string status { get; set; } = "running";
    public int? connectionId { get; set; }
    public string? connectionName { get; set; }
    public string? batchId { get; set; }
    public string? batchType { get; set; }
    public int? totalItems { get; set; }
    public int? completedItems { get; set; }
    public int? failedItems { get; set; }
    public string? summary { get; set; }
    public DateTime createdAt { get; set; }
    public DateTime? finishedAt { get; set; }
}
