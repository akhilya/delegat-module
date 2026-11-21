using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Middleware;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Models.Marketplace;
using Delegat.Services.Marketplace;

namespace Delegat.Services.Tools;

public sealed class PlatformToolJobService
{
    private readonly DataContext _db;
    private readonly IMarketplaceAdapterRegistry _adapters;
    private readonly PriceSyncService _prices;
    private readonly IOperationLogger _operations;
    private readonly MarketplaceActionExecutor _marketplaceActions;
    private readonly MarketplaceCommerceExecutionService? _commerceExecution;

    public PlatformToolJobService(
        DataContext db,
        IMarketplaceAdapterRegistry adapters,
        PriceSyncService prices,
        IOperationLogger operations,
        MarketplaceActionExecutor marketplaceActions,
        MarketplaceCommerceExecutionService? commerceExecution = null)
    {
        _db = db;
        _adapters = adapters;
        _prices = prices;
        _operations = operations;
        _marketplaceActions = marketplaceActions;
        _commerceExecution = commerceExecution;
    }

    public async Task RunAsync(
        ToolCatalogEntry entry,
        MarketplaceConnection? connection,
        int storeId,
        string operationId,
        int userId,
        IReadOnlyDictionary<string, string?>? parameters,
        CancellationToken ct)
    {
        var targets = await ResolveTargetsAsync(entry, connection, storeId, ct);
        if (targets.Count == 0)
        {
            throw ApiException.BadRequest(
                "No active connection supports this tool",
                code: "connection_required");
        }

        await _operations.ProgressAsync(operationId, storeId, targets.Count, 0, 0);

        if (PlatformTools.IsMarketplaceAction(entry.Id))
            await RunMarketplaceActionAsync(entry, targets, storeId, operationId, userId, parameters, ct);
        else if (entry.Id == PlatformTools.ExportAllPricesId)
            await ExportPricesAsync(targets, storeId, operationId, ct);
        else if (entry.Id == PlatformTools.ClearPromotionProductsId)
            await ClearPromotionsAsync(targets, storeId, operationId, ct);
        else
            throw ApiException.BadRequest("Platform job tools are not registered yet", code: "tool_not_a_job");
    }

    private async Task RunMarketplaceActionAsync(
        ToolCatalogEntry entry,
        List<(MarketplaceConnection Connection, IMarketplaceAdapter Adapter)> targets,
        int storeId,
        string operationId,
        int userId,
        IReadOnlyDictionary<string, string?>? rawParameters,
        CancellationToken ct)
    {
        if (targets.Count != 1)
            throw ApiException.BadRequest("Select one marketplace connection", code: "connection_required");
        var values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in rawParameters ?? new Dictionary<string, string?>())
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (!int.TryParse(value, out var parsed))
                throw ApiException.BadRequest($"Parameter '{key}' must be a number", code: "invalid_tool_parameter");
            values[key] = parsed;
        }

        var summary = await _marketplaceActions.ExecuteAsync(
            entry.Id, targets[0].Connection, storeId, userId, values, operationId, ct);
        await _operations.ProgressAsync(operationId, storeId, 1, 1, 0);
        await _operations.LogAsync(operationId, storeId, "success", summary);
        await _operations.FinishAsync(operationId, storeId, "completed", summary);
    }

    private async Task<List<(MarketplaceConnection Connection, IMarketplaceAdapter Adapter)>> ResolveTargetsAsync(
        ToolCatalogEntry entry,
        MarketplaceConnection? selected,
        int storeId,
        CancellationToken ct)
    {
        var required = PlatformTools.RequiredCapabilityFor(entry.Id)
            ?? throw ApiException.BadRequest("Unknown platform job", code: "tool_not_a_job");

        if (selected != null)
        {
            CatalogMatchService.EnsureConnectionActive(selected);
            var adapter = RequireAdapter(selected, required);
            return [(selected, adapter)];
        }

        var connections = await _db.marketplaceConnections.AsNoTracking()
            .Where(c => c.storeId == storeId && c.isActive)
            .OrderBy(c => c.name)
            .ToListAsync(ct);

        var targets = new List<(MarketplaceConnection, IMarketplaceAdapter)>();
        foreach (var connection in connections)
        {
            var adapter = _adapters.GetByKey(connection.adapterKey);
            if (adapter == null)
                continue;
            if (!adapter.Capabilities.HasFlag(required))
                continue;
            targets.Add((connection, adapter));
        }

        return targets;
    }

    private IMarketplaceAdapter RequireAdapter(MarketplaceConnection connection, MarketplaceCapabilities required)
    {
        var adapter = _adapters.GetByKey(connection.adapterKey)
            ?? throw ApiException.BadRequest($"Unknown adapter: {connection.adapterKey}", code: "adapter_not_found");
        if (!adapter.Capabilities.HasFlag(required))
        {
            throw ApiException.BadRequest(
                $"Adapter '{connection.adapterKey}' does not support {required}",
                code: "adapter_capability_missing");
        }

        return adapter;
    }

    private async Task ExportPricesAsync(
        List<(MarketplaceConnection Connection, IMarketplaceAdapter Adapter)> targets,
        int storeId,
        string operationId,
        CancellationToken ct)
    {
        var completed = 0;
        var failed = 0;
        foreach (var (connection, _) in targets)
        {
            await _operations.LogAsync(
                operationId,
                storeId,
                "info",
                $"Exporting prices on {connection.name} ({connection.adapterKey})");
            var result = await _prices.ExportPricesAsync(connection.Id, null, ct, operationId, storeId);
            if (result.IsSuccess)
            {
                completed++;
                await _operations.LogAsync(
                    operationId,
                    storeId,
                    "success",
                    result.Message ?? $"Exported prices on {connection.name}");
            }
            else
            {
                failed++;
                await _operations.LogAsync(
                    operationId,
                    storeId,
                    "error",
                    result.ErrorMessage ?? $"Price export failed on {connection.name}");
            }

            await _operations.ProgressAsync(operationId, storeId, targets.Count, completed + failed, failed);
        }

        var status = failed == 0 ? "completed" : (completed == 0 ? "failed" : "completed");
        var summary = failed == 0
            ? $"Exported prices on {completed} connection(s)"
            : $"Price export finished with {failed} failed of {targets.Count} connection(s)";
        await _operations.FinishAsync(operationId, storeId, status, summary);
    }

    private async Task ClearPromotionsAsync(
        List<(MarketplaceConnection Connection, IMarketplaceAdapter Adapter)> targets,
        int storeId,
        string operationId,
        CancellationToken ct)
    {
        var completed = 0;
        var failedConnections = 0;
        var skippedConnections = 0;
        foreach (var (connection, adapter) in targets)
        {
            await _operations.LogAsync(
                operationId,
                storeId,
                "info",
                $"Clearing promotions on {connection.name} ({connection.adapterKey})");

            Func<string, PromotionProductsUpdate, CancellationToken, Task<PromotionProductsResult>> updatePromotion =
                _commerceExecution is { IsV1Enabled: true }
                && _commerceExecution.CanExecute(connection.adapterKey, Delegat.Marketplace.SDK.IntegrationOperationKind.PromotionAction)
                    ? async (id, update, token) =>
                    {
                        var admitted = await _commerceExecution.AdmitPromotionActionAsync(
                            connection,
                            id,
                            update,
                            authorUserId: 0,
                            authorApiKeyId: null,
                            requestKey: $"tool-clear-promo:{operationId}:{connection.Id}:{id}",
                            token);
                        var snapshot = await _commerceExecution.WaitForCommandSnapshotAsync(admitted.CommandId, token);
                        if (snapshot is not { } s || !Delegat.Models.Integration.IntegrationCommandState.IsTerminal(s.Command.state))
                        {
                            return new PromotionProductsResult
                            {
                                Success = false,
                                ErrorMessage = $"Promotion clear queued in durable operation {admitted.OperationId} (pending confirmation)"
                            };
                        }
                        return s.Command.state == Delegat.Models.Integration.IntegrationCommandState.Completed
                            ? new PromotionProductsResult
                            {
                                Success = true,
                                Removed = update.RemoveExternalProductIds?.Count ?? 0
                            }
                            : new PromotionProductsResult
                            {
                                Success = false,
                                ErrorMessage = s.Command.summary ?? "Promotion update failed"
                            };
                    }
                    : (id, update, token) => adapter.UpdatePromotionProductsAsync(connection, id, update, token);

            var summary = await PromotionMembershipClearer.ClearAsync(
                (query, token) => adapter.GetPromotionsAsync(connection, query, token),
                updatePromotion,
                (level, message) => _operations.LogAsync(operationId, storeId, level, message),
                ct);

            if (summary.Incomplete)
            {
                failedConnections++;
                await _operations.LogAsync(
                    operationId,
                    storeId,
                    "error",
                    $"{connection.name}: promotion listing incomplete ({summary.IncompleteReason ?? "unknown"}); processed {summary.Promotions} promotions, removed {summary.Removed}, skipped {summary.Skipped}, failed {summary.Failed}");
            }
            else if (summary.Failed > 0 && summary.Promotions > 0 && summary.Failed == summary.Promotions)
            {
                failedConnections++;
                await _operations.LogAsync(
                    operationId,
                    storeId,
                    "error",
                    $"{connection.name}: every promotion failed ({summary.Failed})");
            }
            else if (summary.Skipped > 0 && summary.Failed == 0 && summary.Removed == 0)
            {
                skippedConnections++;
                await _operations.LogAsync(
                    operationId,
                    storeId,
                    "warning",
                    $"{connection.name}: marketplace cannot remove products from promotions ({summary.Skipped} skipped)");
            }
            else
            {
                completed++;
                await _operations.LogAsync(
                    operationId,
                    storeId,
                    "success",
                    $"{connection.name}: {summary.Promotions} promotions, removed {summary.Removed}, skipped {summary.Skipped}, failed {summary.Failed}");
            }

            await _operations.ProgressAsync(
                operationId,
                storeId,
                targets.Count,
                completed + failedConnections + skippedConnections,
                failedConnections);
        }

        var status = failedConnections == 0 && (completed > 0 || skippedConnections == 0)
            ? "completed"
            : completed > 0
                ? "partial"
                : "failed";
        var finish = $"Cleared promotions on {completed} connection(s)";
        if (skippedConnections > 0)
            finish += $", skipped {skippedConnections}";
        if (failedConnections > 0)
            finish += $", failed/incomplete {failedConnections}";
        await _operations.FinishAsync(operationId, storeId, status, finish);
    }
}
