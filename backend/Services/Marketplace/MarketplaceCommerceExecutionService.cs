using System.Text.Json;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Security;
using Delegat.Marketplace.SDK;
using Delegat.Marketplace.SDK.DTOs;
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
                if (!string.IsNullOrWhiteSpace(k) && v is not null)
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

    private async Task<IntegrationAdmitResult> AdmitPreparedAsync(
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
        var prepInput = new IntegrationPreparationInput(
            operationKind,
            connCtx,
            [new IntegrationItemIntent(itemKey, data, ExpectedRemoteId: expectedRemoteId)],
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

        var inputJson = JsonSerializer.Serialize(new
        {
            connectionId = connection.Id,
            adapterKey = connection.adapterKey,
            kind = commandKind,
            itemKey,
            data
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
