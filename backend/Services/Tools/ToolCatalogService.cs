using Microsoft.EntityFrameworkCore;
using Delegat;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Marketplace.SDK;
using Delegat.Models.Marketplace;
using Delegat.Services.Marketplace;
using Delegat.Services;

namespace Delegat.Services.Tools;

public class ToolCatalogService
{
    private readonly IMarketplaceAdapterRegistry _adapters;
    private readonly DataContext _db;
    private readonly IOperationLogger _operations;
    private readonly IPublicApiSettingsService _publicApi;

    public ToolCatalogService(
        IMarketplaceAdapterRegistry adapters,
        DataContext db,
        IOperationLogger operations,
        IPublicApiSettingsService publicApi)
    {
        _adapters = adapters;
        _db = db;
        _operations = operations;
        _publicApi = publicApi;
    }

    public async Task<IReadOnlyList<ToolCatalogEntry>> ListAsync(
        IReadOnlySet<string> userPermissions,
        int? storeId,
        CancellationToken ct = default)
    {
        var connected = await ConnectedAdapterKeysAsync(storeId, ct);
        var entries = ToolCatalogComposer.Compose(
            PlatformTools.All,
            AdapterContributions(),
            userPermissions,
            connected);
        return ToolCatalogComposer.ApplyInstanceFlags(entries, _publicApi.IsPublicApiEnabled);
    }

    public async Task<ToolCatalogEntry> GetAsync(
        string id,
        IReadOnlySet<string> userPermissions,
        int? storeId,
        CancellationToken ct = default)
    {
        var list = await ListAsync(userPermissions, storeId, ct);
        var entry = list.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            throw ApiException.NotFound("Tool not found", code: "tool_not_found");
        return entry;
    }

    public async Task<ToolRunResult> RunJobAsync(
        string id,
        int? connectionId,
        IReadOnlyDictionary<string, string?> parameters,
        IReadOnlySet<string> userPermissions,
        int userId,
        int? storeId,
        CancellationToken ct = default)
    {
        var entry = await GetAsync(id, userPermissions, storeId, ct);
        if (entry.Mode != ToolModes.Job)
            throw ApiException.BadRequest("This tool is a guided page, not a job", code: "tool_not_a_job");
        if (storeId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");
        if (!entry.Available)
            throw ApiException.BadRequest("Tool is not available", code: "tool_unavailable");

        MarketplaceConnection? connection = null;
        IMarketplaceAdapter? adapter = null;
        string localToolId = id;

        if (entry.Source == ToolSources.Adapter)
        {
            if (!ToolCatalogComposer.TryParseAdapterToolId(id, out var adapterKey, out localToolId))
                throw ApiException.NotFound("Tool not found", code: "tool_not_found");

            adapter = _adapters.GetByKey(adapterKey)
                ?? throw ApiException.BadRequest($"Unknown adapter: {adapterKey}");

            if (entry.RequiresConnection)
            {
                if (connectionId == null)
                    throw ApiException.BadRequest("Connection is required", code: "connection_required");
                connection = await _db.marketplaceConnections
                    .FirstOrDefaultAsync(
                        c => c.Id == connectionId.Value
                            && c.storeId == storeId.Value
                            && c.adapterKey == adapterKey,
                        ct)
                    ?? throw ApiException.NotFound("Connection not found", code: "connection_not_found");
            }
        }
        else
        {
            throw ApiException.BadRequest("Platform job tools are not registered yet", code: "tool_not_a_job");
        }

        var operationId = Guid.NewGuid().ToString("N");
        await _operations.StartAsync(
            operationId,
            storeId.Value,
            userId,
            "tool",
            entry.Title ?? entry.Id,
            connectionId: connection?.Id,
            connectionName: connection?.name,
            batchId: operationId,
            batchType: "tool",
            totalItems: 1);

        try
        {
            var result = await adapter!.RunToolAsync(connection, localToolId, parameters, ct);
            var status = result.Success ? "completed" : "failed";
            var summary = result.Message
                ?? (result.Success ? "Finished" : "Failed");
            if (result.Table != null)
                summary = $"{summary} ({result.Table.Rows.Count} rows)";
            await _operations.ProgressAsync(
                operationId, storeId.Value, 1, result.Success ? 1 : 0, result.Success ? 0 : 1);
            await _operations.FinishAsync(operationId, storeId.Value, status, summary);
            if (result.Success)
            {
                await _operations.LogAsync(operationId, storeId.Value, "success", summary);
            }
            else
            {
                await _operations.LogAsync(operationId, storeId.Value, "error", summary);
            }

            return new ToolRunResult
            {
                OperationId = operationId,
                Success = result.Success,
                Message = result.Message,
                Table = result.Table,
            };
        }
        catch (NotSupportedException ex)
        {
            await _operations.LogAsync(operationId, storeId.Value, "error", ex.Message);
            await _operations.FinishAsync(operationId, storeId.Value, "failed", ex.Message);
            throw ApiException.BadRequest(ex.Message, code: "tool_not_supported");
        }
        catch (Exception ex)
        {
            await _operations.LogAsync(operationId, storeId.Value, "error", ex.Message);
            await _operations.FinishAsync(operationId, storeId.Value, "failed", ex.Message);
            throw;
        }
    }

    private List<(string AdapterKey, IReadOnlyList<AdapterToolDescriptor> Tools)> AdapterContributions()
    {
        var list = new List<(string, IReadOnlyList<AdapterToolDescriptor>)>();
        foreach (var adapter in _adapters.GetAll())
        {
            IReadOnlyList<AdapterToolDescriptor> tools;
            try
            {
                tools = adapter.GetTools();
            }
            catch
            {
                continue;
            }

            if (tools.Count == 0)
                continue;
            list.Add((adapter.Key, tools));
        }

        return list;
    }

    private async Task<HashSet<string>> ConnectedAdapterKeysAsync(int? storeId, CancellationToken ct)
    {
        if (storeId == null)
            return [];
        var keys = await _db.marketplaceConnections.AsNoTracking()
            .Where(c => c.storeId == storeId.Value)
            .Select(c => c.adapterKey)
            .Distinct()
            .ToListAsync(ct);
        return keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class ToolRunResult
{
    public required string OperationId { get; init; }
    public bool Success { get; init; }
    public string? Message { get; init; }
    public AdapterToolTableResult? Table { get; init; }
}
