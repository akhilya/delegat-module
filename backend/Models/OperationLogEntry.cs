namespace PrintO.Models;

public class OperationLogEntry
{
    public long Id { get; set; }
    public string operationId { get; set; } = null!;
    public string level { get; set; } = null!;
    public string message { get; set; } = null!;
    public DateTime timestamp { get; set; }
}
