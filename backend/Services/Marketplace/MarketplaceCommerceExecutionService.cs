using System.Text.Json;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Security;
using Delegat.Marketplace.SDK;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Models.Integration;
using Delegat.Models.Marketplace;
using Delegat.Services.Integration;
using Delegat.Services.Plugins;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Distinguishes confirmed order projection updates from unconfirmed/pending
/// or ambiguous integration outcomes (P5).
/// </summary>
public enum OrderActionProjectionState
{
    ConfirmedApplied,
    PendingConfirmation,
    UnknownEffect,
    Rejected
}

/// <summary>
/// Result of evaluating an order action outcome against stored order snapshots.
/// </summary>
public sealed record OrderActionProjectionResult(
    OrderActionProjectionState State,
    bool SnapshotAdvanced,
    ExternalOrder? Order = null);

/// <summary>
/// Routes commerce order, shipment, packing, and label mutations through the
/// durable integration coordinator when <see cref="IntegrationExecutionOptions.Enabled"/>
/// is active (P5). Keeps preflight refusal before admission and separates
/// optimistic order display from confirmed remote effects.
/// </summary>
public sealed class MarketplaceCommerceExecutionService
{
    private readonly IntegrationCoordinator _coordinator;
    private readonly IntegrationPreparationService _preparation;
    private readonly Func<string, IMarketplaceExecutionProvider?> _providerResolver;
    private readonly IntegrationExecutionOptions _options;
    private readonly MarketplaceCommerceService _commerce;
    private readonly EncryptionService? _encryption;

    public MarketplaceCommerceExecutionService(
        IntegrationCoordinator coordinator,
        IntegrationPreparationService preparation,
        PluginManager plugins,
        IntegrationExecutionOptions options,
        MarketplaceCommerceService commerce,
        EncryptionService? encryption = null)
        : this(
            coordinator,
            preparation,
            key => plugins.GetPlugin(key) as IMarketplaceExecutionProvider,
            options,
            commerce,
            encryption)
    {
    }

    internal MarketplaceCommerceExecutionService(
        IntegrationCoordinator coordinator,
        IntegrationPreparationService preparation,
        Func<string, IMarketplaceExecutionProvider?> providerResolver,
        IntegrationExecutionOptions options,
        MarketplaceCommerceService commerce,
        EncryptionService? encryption = null)
    {
        _coordinator = coordinator;
        _preparation = preparation;
        _providerResolver = providerResolver;
        _options = options;
        _commerce = commerce;
        _encryption = encryption;
    }

    public bool IsV1Enabled => _options.Enabled;

    /// <summary>
    /// Bounded wait for a newly admitted command to reach a terminal state so
    /// callers without <c>Prefer: respond-async</c> can receive the original
    /// synchronous DTO when execution finishes quickly. Caller cancellation
    /// stops waiting without cancelling the durable command.
    /// </summary>
    public async Task<(Delegat.Models.Integration.IntegrationCommand Command, List<Delegat.Models.Integration.IntegrationStage> Stages)?> WaitForCommandSnapshotAsync(
        string commandId,
        CancellationToken ct)
    {
        var waitMs = Math.Max(0, _options.PublicApiSyncWaitMs);
        var deadline = Environment.TickCount64 + waitMs;

        (Delegat.Models.Integration.IntegrationCommand Command, List<Delegat.Models.Integration.IntegrationStage> Stages)? snapshot = null;
        try
        {
            using var initialCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (waitMs > 0)
                initialCts.CancelAfter(waitMs);
            snapshot = await _coordinator.GetCommandSnapshotAsync(commandId, initialCts.Token);
        }
        catch (OperationCanceledException)
        {
            return snapshot;
        }

        if (snapshot is null || IntegrationCommandState.IsTerminal(snapshot.Value.Command.state) || waitMs == 0)
            return snapshot;

        var pollMs = Math.Clamp(_options.PublicApiSyncPollIntervalMs, 10, 500);
        while (Environment.TickCount64 < deadline && !ct.IsCancellationRequested)
        {
            var remainingMs = (int)Math.Max(1, deadline - Environment.TickCount64);
            var delayMs = Math.Min(pollMs, remainingMs);
            try
            {
                await Task.Delay(delayMs, ct);
                var readBudgetMs = (int)(deadline - Environment.TickCount64);
                if (readBudgetMs <= 0)
                    break;
                using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                pollCts.CancelAfter(readBudgetMs);
                snapshot = await _coordinator.GetCommandSnapshotAsync(commandId, pollCts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (snapshot is null || IntegrationCommandState.IsTerminal(snapshot.Value.Command.state))
                return snapshot;
        }

        return snapshot;
    }

    /// <summary>
    /// Admits an order status action through the durable coordinator after
    /// running provider preflight preparation.
    /// </summary>
    public Task<IntegrationAdmitResult> AdmitOrderActionAsync(
        MarketplaceConnection connection,
        string orderId,
        OrderStatusUpdate body,
        int authorUserId,
        int? authorApiKeyId,
        string? requestKey,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(body);

        if (string.IsNullOrWhiteSpace(orderId))
            throw ApiException.BadRequest("Order ID is required", code: "order_id_required");
        if (string.IsNullOrWhiteSpace(body.Action))
            throw ApiException.BadRequest("Order action is required", code: "order_action_required");

        var data = new Dictionary<string, object?>
        {
            ["orderId"] = orderId.Trim(),
            ["action"] = body.Action.Trim()
        };
        if (!string.IsNullOrWhiteSpace(body.Status))
            data["status"] = body.Status.Trim();
        if (!string.IsNullOrWhiteSpace(body.Reason))
            data["reason"] = body.Reason.Trim();
        if (!string.IsNullOrWhiteSpace(body.TrackingNumber))
            data["trackingNumber"] = body.TrackingNumber.Trim();
        if (!string.IsNullOrWhiteSpace(body.DeliveryService))
            data["deliveryService"] = body.DeliveryService.Trim();
        if (body.Extra is { Count: > 0 })
        {
            foreach (var (k, v) in body.Extra)
            {
                if (string.IsNullOrWhiteSpace(k) || v is null)
                    continue;
                var key = k.Trim();
                if (key.Equals("orderId", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(v.Trim(), orderId.Trim(), StringComparison.Ordinal))
                        throw ApiException.BadRequest("Conflicting orderId in extra metadata", code: "order_status_failed");
                    continue;
                }
                if (key.Equals("action", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(v.Trim(), body.Action.Trim(), StringComparison.OrdinalIgnoreCase))
                        throw ApiException.BadRequest("Conflicting action in extra metadata", code: "order_status_failed");
                    continue;
                }
                data[k] = v;
            }
        }

        return AdmitPreparedAsync(
            connection,
            IntegrationOperationKind.OrderAction,
            "order_action",
            orderId.Trim(),
            data,
            expectedRemoteId: orderId.Trim(),
            title: $"Order {orderId.Trim()}: {body.Action.Trim()}",
            authorUserId,
            authorApiKeyId,
            requestKey,
            ct);
    }

    /// <summary>
    /// Admits a shipment, box, membership, or async label action as an independent
    /// durable command so supply creation, order membership, packing, and label
    /// retrieval recover independently without repeating accepted effects.
    /// </summary>
    public Task<IntegrationAdmitResult> AdmitShipmentActionAsync(
        MarketplaceConnection connection,
        string action,
        string itemKey,
        Dictionary<string, object?> data,
        string title,
        int authorUserId,
        int? authorApiKeyId,
        string? requestKey,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(data);

        if (string.IsNullOrWhiteSpace(action))
            throw ApiException.BadRequest("Shipment action is required", code: "shipment_action_required");

        data["action"] = action.Trim();
        return AdmitPreparedAsync(
            connection,
            IntegrationOperationKind.ShipmentAction,
            $"shipment_{action.Trim().ToLowerInvariant()}",
            itemKey,
            data,
            expectedRemoteId: null,
            title: title,
            authorUserId,
            authorApiKeyId,
            requestKey,
            ct);
    }

    /// <summary>
    /// Admits a promotion membership update (<c>add</c>, <c>remove</c>, or <c>removeAll</c>)
    /// through the durable coordinator using provider P4 promotion recipes (P6).
    /// </summary>
    public Task<IntegrationAdmitResult> AdmitPromotionActionAsync(
        MarketplaceConnection connection,
        string promotionId,
        PromotionProductsUpdate body,
        int authorUserId,
        int? authorApiKeyId,
        string? requestKey,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(body);

        if (string.IsNullOrWhiteSpace(promotionId))
            throw ApiException.BadRequest("Promotion ID is required", code: "promotion_id_required");

        var addItems = body.Add?
            .Where(a => !string.IsNullOrWhiteSpace(a.ExternalProductId))
            .GroupBy(a => a.ExternalProductId.Trim(), StringComparer.Ordinal)
            .Select(g => g.Last())
            .ToList() ?? [];
        var removeIds = body.RemoveExternalProductIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? [];

        if (!body.RemoveAll && addItems.Count == 0 && removeIds.Count == 0)
            throw ApiException.BadRequest("Promotion update requires at least one product to add, remove, or removeAll", code: "promotion_update_empty");

        var promoId = promotionId.Trim();
        var adapterKey = (connection.adapterKey ?? "").Trim().ToLowerInvariant();
        var intents = new List<IntegrationItemIntent>();

        // Bridge coupon/promotion endpoints (WooCommerce, OpenCart) accept a single delta/removeAll intent per promotion.
        if (adapterKey is "woocommerce" or "opencart")
        {
            var data = new Dictionary<string, object?>
            {
                ["promotionId"] = promoId,
                ["add"] = addItems.Select(a => new Dictionary<string, object?>
                {
                    ["externalProductId"] = a.ExternalProductId.Trim(),
                    ["actionPrice"] = a.ActionPrice,
                    ["stock"] = a.Stock
                }).ToList(),
                ["removeExternalProductIds"] = removeIds,
                ["removeAll"] = body.RemoveAll
            };
            intents.Add(new IntegrationItemIntent(promoId, data, ExpectedRemoteId: promoId));
        }
        else
        {
            var addIdSet = new HashSet<string>(addItems.Select(a => a.ExternalProductId.Trim()), StringComparer.Ordinal);
            if (removeIds.Any(addIdSet.Contains))
                throw ApiException.BadRequest("Cannot both add and remove the same product in one promotion request", code: "promotion_conflicting_product");

            if (body.RemoveAll)
            {
                intents.Add(new IntegrationItemIntent(
                    $"{promoId}:removeAll",
                    new Dictionary<string, object?>
                    {
                        ["promotionId"] = promoId,
                        ["actionId"] = promoId,
                        ["action"] = "removeAll",
                        ["removeAll"] = true
                    },
                    ExpectedRemoteId: promoId));
            }
            var addAction = adapterKey == "ozon" ? "activate" : "add";
            var removeAction = adapterKey == "ozon" ? "deactivate" : "remove";
            foreach (var add in addItems)
            {
                var prodId = add.ExternalProductId.Trim();
                var data = new Dictionary<string, object?>
                {
                    ["promotionId"] = promoId,
                    ["actionId"] = promoId,
                    ["action"] = addAction,
                    ["productId"] = prodId
                };
                if (add.ActionPrice.HasValue)
                {
                    data["actionPrice"] = add.ActionPrice.Value;
                    data["promoPrice"] = add.ActionPrice.Value;
                }
                if (add.Stock.HasValue)
                    data["stock"] = add.Stock.Value;
                // Stable promotion+product entity key ensures cross-command ordering between add and remove.
                intents.Add(new IntegrationItemIntent($"{promoId}:{prodId}", data, ExpectedRemoteId: prodId));
            }
            foreach (var remId in removeIds)
            {
                intents.Add(new IntegrationItemIntent(
                    $"{promoId}:{remId}",
                    new Dictionary<string, object?>
                    {
                        ["promotionId"] = promoId,
                        ["actionId"] = promoId,
                        ["action"] = removeAction,
                        ["productId"] = remId
                    },
                    ExpectedRemoteId: remId));
            }
        }

        return AdmitPreparedItemsAsync(
            connection,
            IntegrationOperationKind.PromotionAction,
            "promotion_action",
            intents,
            title: $"Promotion {promoId}: update products",
            authorUserId,
            authorApiKeyId,
            requestKey,
            legacySingleItemPayload: null,
            ct);
    }

    /// <summary>
    /// Admits a marketplace product group upsert through the durable coordinator (P6).
    /// </summary>
    public Task<IntegrationAdmitResult> AdmitGroupUpdateAsync(
        MarketplaceConnection connection,
        string groupKey,
        Dictionary<string, object?> data,
        string title,
        int authorUserId,
        int? authorApiKeyId,
        string? requestKey,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(data);
        if (string.IsNullOrWhiteSpace(groupKey))
            throw ApiException.BadRequest("Group key is required", code: "group_key_required");

        return AdmitPreparedAsync(
            connection,
            IntegrationOperationKind.GroupUpdate,
            "group_update",
            groupKey.Trim(),
            data,
            expectedRemoteId: null,
            title: title,
            authorUserId,
            authorApiKeyId,
            requestKey,
            ct);
    }

    /// <summary>
    /// Advances the stored order snapshot ONLY when the remote effect is
    /// confirmed <see cref="IntegrationStageOutcomeKind.Applied"/>. Pending,
    /// accepted-not-confirmed, unknown, and rejected outcomes never overwrite
    /// the stored snapshot as confirmed.
    /// </summary>
    public async Task<OrderActionProjectionResult> ApplyConfirmedOrderActionAsync(
        MarketplaceConnection connection,
        IMarketplaceAdapter adapter,
        string orderId,
        string action,
        IntegrationStageOutcome outcome,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(outcome);

        if (outcome.Kind == IntegrationStageOutcomeKind.Applied)
        {
            var result = new OrderStatusUpdateResult { Success = true };
            await _commerce.FinishSellerStatusAsync(connection, adapter, orderId, action, result, ct);
            return new OrderActionProjectionResult(
                OrderActionProjectionState.ConfirmedApplied,
                SnapshotAdvanced: true,
                Order: result.Order);
        }

        var state = outcome.Kind switch
        {
            IntegrationStageOutcomeKind.Accepted or IntegrationStageOutcomeKind.Pending
                => OrderActionProjectionState.PendingConfirmation,
            IntegrationStageOutcomeKind.Rejected
                => OrderActionProjectionState.Rejected,
            _ => OrderActionProjectionState.UnknownEffect
        };

        return new OrderActionProjectionResult(state, SnapshotAdvanced: false);
    }

    /// <summary>
    /// Builds a decrypted <see cref="ConnectionContext"/> for provider preparation.
    /// </summary>
    public ConnectionContext BuildConnectionContext(MarketplaceConnection connection)
    {
        var plain = ConnectionCredentialsHelper.DecryptCredentialsJson(connection.credentialsJson, _encryption);
        var creds = ConnectionCredentialsHelper.ParseCredentialsObject(plain);
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(connection.settingsJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(connection.settingsJson);
                if (parsed != null)
                {
                    foreach (var (k, v) in parsed)
                        settings[k] = v.ToString();
                }
            }
            catch (JsonException) { }
        }

        return new ConnectionContext
        {
            ConnectionId = connection.Id,
            StoreId = connection.storeId,
            AdapterKey = connection.adapterKey,
            Credentials = creds,
            Settings = settings
        };
    }

    private Task<IntegrationAdmitResult> AdmitPreparedAsync(
        MarketplaceConnection connection,
        IntegrationOperationKind operationKind,
        string commandKind,
        string itemKey,
        Dictionary<string, object?> data,
        string? expectedRemoteId,
        string title,
        int authorUserId,
        int? authorApiKeyId,
        string? requestKey,
        CancellationToken ct) =>
        AdmitPreparedItemsAsync(
            connection,
            operationKind,
            commandKind,
            [new IntegrationItemIntent(itemKey, data, ExpectedRemoteId: expectedRemoteId)],
            title,
            authorUserId,
            authorApiKeyId,
            requestKey,
            legacySingleItemPayload: (itemKey, data),
            ct);

    private async Task<IntegrationAdmitResult> AdmitPreparedItemsAsync(
        MarketplaceConnection connection,
        IntegrationOperationKind operationKind,
        string commandKind,
        IReadOnlyList<IntegrationItemIntent> intents,
        string title,
        int authorUserId,
        int? authorApiKeyId,
        string? requestKey,
        (string ItemKey, Dictionary<string, object?> Data)? legacySingleItemPayload,
        CancellationToken ct)
    {
        if (!_options.Enabled)
            throw new IntegrationExecutionDisabledException("Integration execution V1 is not enabled on this instance");

        var provider = _providerResolver(connection.adapterKey);
        if (provider is null)
        {
            throw ApiException.BadRequest(
                $"Adapter '{connection.adapterKey}' does not implement V1 execution preparation",
                code: "adapter_execution_unsupported");
        }

        var commandIdSeed = string.IsNullOrWhiteSpace(requestKey)
            ? Guid.NewGuid().ToString("N")
            : requestKey.Trim();
        var connCtx = BuildConnectionContext(connection);
        if (!HostIntegrationExecutionBindingResolver.TryBuildRouteBinding(connection, connCtx.Credentials, out var routeBinding)
            || routeBinding is null)
        {
            throw ApiException.BadRequest(
                $"Connection '{connection.name}' does not have a valid HTTPS V1 execution route binding",
                code: "invalid_connection_binding");
        }

        var bindingFingerprint = HostIntegrationExecutionBindingResolver.ComputeBindingFingerprint(
            connection, connCtx.Credentials, routeBinding);

        var prepInput = new IntegrationPreparationInput(
            operationKind,
            connCtx,
            intents,
            null,
            commandIdSeed);

        var prepared = await _preparation.PrepareAsync(provider, prepInput, ct);
        if (prepared.HasBlockingIssues)
        {
            var first = prepared.Issues[0];
            throw ApiException.BadRequest(first.Message, code: first.Code);
        }

        if (prepared.Stages.Count == 0)
        {
            throw ApiException.BadRequest(
                $"No executable stages prepared for {commandKind}",
                code: "integration_empty_plan");
        }

        var inputJson = legacySingleItemPayload is { } single
            ? JsonSerializer.Serialize(new
            {
                connectionId = connection.Id,
                adapterKey = connection.adapterKey,
                bindingFingerprint,
                kind = commandKind,
                itemKey = single.ItemKey,
                data = single.Data
            })
            : JsonSerializer.Serialize(new
            {
                connectionId = connection.Id,
                adapterKey = connection.adapterKey,
                bindingFingerprint,
                kind = commandKind,
                items = intents.Select(i => new { i.ItemKey, i.ExpectedRemoteId, i.Data })
            });

        try
        {
            return await _coordinator.AdmitAsync(
                new IntegrationAdmitRequest(
                    Kind: commandKind,
                    StoreId: connection.storeId,
                    AuthorUserId: authorUserId,
                    AuthorApiKeyId: authorApiKeyId,
                    ConnectionId: connection.Id,
                    RequestKey: requestKey,
                    InputJson: inputJson,
                    Title: title,
                    Stages: prepared.Stages),
                v1Enabled: _options.Enabled,
                ct);
        }
        catch (IntegrationRequestConflictException ex)
        {
            throw ApiException.Conflict(ex.Message, code: "integration_request_conflict");
        }
    }
}
