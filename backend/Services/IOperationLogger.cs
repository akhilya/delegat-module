namespace PrintO.Services;

public interface IOperationLogger
{
    Task StartAsync(string operationId, int storeId, int authorUserId, string type, string title,
                    int? connectionId = null, string? connectionName = null,
                    string? batchId = null, string? batchType = null, int? totalItems = null);
    Task LogAsync(string operationId, int storeId, string level, string message);
    Task ProgressAsync(string operationId, int storeId, int total, int completed, int failed);
    Task FinishAsync(string operationId, int storeId, string status, string? summary = null);
}
