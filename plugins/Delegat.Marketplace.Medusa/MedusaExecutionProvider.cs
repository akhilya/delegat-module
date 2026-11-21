using System.Globalization;
using System.Text;
using System.Text.Json;
using Delegat.Marketplace.SDK;

namespace Delegat.Marketplace.Medusa;

public partial class MedusaPlugin
{
    public Task<IntegrationPreparationResult> PrepareAsync(
        IntegrationPreparationInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.OperationKind is not (IntegrationOperationKind.ProductExport
            or IntegrationOperationKind.PriceExport
            or IntegrationOperationKind.StockExport
            or IntegrationOperationKind.GroupUpdate
            or IntegrationOperationKind.OrderAction))
        {
            var unsupported = input.Items.Select(item => new IntegrationIssue(
                "unsupported_operation",
                $"Medusa does not support {input.OperationKind}",
                Retryable: false,
                ItemKey: item.ItemKey)).ToList();
            return Task.FromResult(new IntegrationPreparationResult([], unsupported));
        }

        var prepared = new List<IntegrationPreparedItem>(input.Items.Count);
        var issues = new List<IntegrationIssue>();

        foreach (var item in input.Items)
        {
            switch (input.OperationKind)
            {
                case IntegrationOperationKind.ProductExport:
                    PrepareProductItem(input.CommandId, item, prepared, issues);
                    break;
                case IntegrationOperationKind.PriceExport:
                    PreparePriceItem(input.CommandId, item, prepared, issues);
                    break;
                case IntegrationOperationKind.StockExport:
                    PrepareStockItem(input.CommandId, item, prepared, issues);
                    break;
                case IntegrationOperationKind.GroupUpdate:
                    PrepareGroupItem(input.CommandId, item, prepared, issues);
                    break;
                case IntegrationOperationKind.OrderAction:
                    PrepareOrderActionItem(input.CommandId, item, prepared, issues);
                    break;
            }
        }

        return Task.FromResult(new IntegrationPreparationResult(prepared, issues));
    }

    public async Task<IntegrationStageOutcome> ExecuteStageAsync(
        IntegrationStageRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var transport = request.Connection.Transport;
        if (transport is null)
            return new IntegrationStageOutcome(IntegrationStageOutcomeKind.Unknown,
                Issues: [new IntegrationIssue("transport_missing", "V1 transport is required for Medusa execution", request.Stage.StageKey, Retryable: false, ItemKey: request.Attempt.ItemKey)]);

        if (!TryBuildBridgeUri(request.Connection, request.Stage.Kind, out var uri, out var authHeader))
        {
            return new IntegrationStageOutcome(IntegrationStageOutcomeKind.Rejected,
                Issues: [new IntegrationIssue("invalid_connection", "Medusa apiBaseUrl or apiKey is missing or invalid", request.Stage.StageKey, Retryable: false, ItemKey: request.Attempt.ItemKey)]);
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"] = authHeader,
            ["Idempotency-Key"] = request.Attempt.RequestKey
        };

        var resp = await transport.SendAsync(new IntegrationTransportRequest(
            "POST", uri!, headers, request.Stage.PayloadJson,
            new IntegrationRequestPolicy("medusa:bridge", request.Attempt.OperationId, IsEffectful: true)), ct);

        return MapBridgeResponse(resp, request.Stage.StageKey, request.Attempt.ItemKey);
    }

    public async Task<IntegrationStageOutcome> ConfirmAsync(
        IntegrationConfirmationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var transport = request.Connection.Transport;
        if (transport is null || request.Receipt is null || string.IsNullOrWhiteSpace(request.Receipt.Value))
            return new IntegrationStageOutcome(IntegrationStageOutcomeKind.Unknown,
                Issues: [new IntegrationIssue("receipt_missing", "Medusa confirmation requires a provider receipt", request.Stage.StageKey, Retryable: false, ItemKey: request.Attempt.ItemKey)]);

        var apiBaseUrl = request.Connection.GetCredential("apiBaseUrl")?.Trim();
        var apiKey = request.Connection.GetCredential("apiKey")?.Trim();
        if (string.IsNullOrWhiteSpace(apiBaseUrl) || string.IsNullOrWhiteSpace(apiKey) ||
            !Uri.TryCreate(apiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
            return new IntegrationStageOutcome(IntegrationStageOutcomeKind.Unknown);

        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:"));
        var statusUri = new Uri(baseUri, $"admin/delegat/status/{Uri.EscapeDataString(request.Receipt.Value)}");
        var resp = await transport.SendAsync(new IntegrationTransportRequest(
            "GET", statusUri,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Authorization"] = $"Basic {basic}" },
            null,
            new IntegrationRequestPolicy("medusa:bridge:confirm", request.Attempt.OperationId, IsEffectful: false)), ct);

        if (resp.StatusCode is < 200 or >= 300 || string.IsNullOrWhiteSpace(resp.Body))
            return new IntegrationStageOutcome(IntegrationStageOutcomeKind.Unknown);

        try
        {
            using var doc = JsonDocument.Parse(resp.Body);
            var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString()?.ToLowerInvariant() : null;
            return status switch
            {
                "applied" or "completed" => new IntegrationStageOutcome(IntegrationStageOutcomeKind.Applied, Receipt: request.Receipt),
                "pending" or "processing" => new IntegrationStageOutcome(IntegrationStageOutcomeKind.Pending, Receipt: request.Receipt, NextCheckAt: null),
                "rejected" or "failed" => new IntegrationStageOutcome(IntegrationStageOutcomeKind.Rejected, Receipt: request.Receipt,
                    Issues: [new IntegrationIssue("bridge_rejected", "Medusa bridge reported stage failure", request.Stage.StageKey, Retryable: false, ItemKey: request.Attempt.ItemKey)]),
                _ => new IntegrationStageOutcome(IntegrationStageOutcomeKind.Unknown, Receipt: request.Receipt)
            };
        }
        catch (JsonException)
        {
            return new IntegrationStageOutcome(IntegrationStageOutcomeKind.Unknown, Receipt: request.Receipt);
        }
    }

    public Task<IntegrationStageOutcome> CancelAcceptedAsync(
        IntegrationConfirmationRequest request, CancellationToken ct = default) =>
        Task.FromResult(new IntegrationStageOutcome(
            IntegrationStageOutcomeKind.Unknown,
            Issues: [new IntegrationIssue("cancel_not_supported", "Medusa bridge does not support cancelling accepted mutations", request.Stage.StageKey, Retryable: false, ItemKey: request.Attempt.ItemKey)]));

    private static void PrepareProductItem(
        string commandId, IntegrationItemIntent item, List<IntegrationPreparedItem> prepared, List<IntegrationIssue> issues)
    {
        var sku = ReadStr(item.Data, "sku");
        var name = ReadStr(item.Data, "name");
        if (string.IsNullOrWhiteSpace(sku) || string.IsNullOrWhiteSpace(name))
        {
            issues.Add(new IntegrationIssue("missing_required_fields",
                "Medusa product export requires non-empty sku and name",
                FieldKey: string.IsNullOrWhiteSpace(sku) ? "sku" : "name",
                Retryable: false, ItemKey: item.ItemKey));
            return;
        }

        var stages = new List<IntegrationPreparedStage>();
        var cardPayload = JsonSerializer.Serialize(new
        {
            sku,
            name,
            remoteId = item.ExpectedRemoteId,
            description = ReadStr(item.Data, "description")
        });
        stages.Add(new IntegrationPreparedStage(
            "card", IntegrationStageKind.Card, $"{commandId}:{item.ItemKey}:card", cardPayload, [], IsEffectful: true));

        if (TryReadDecimal(item.Data, "price", out var price))
        {
            if (price < 0)
            {
                issues.Add(new IntegrationIssue("invalid_price", "Price cannot be negative", "price", "price", Retryable: false, ItemKey: item.ItemKey));
                return;
            }
            stages.Add(new IntegrationPreparedStage(
                "price", IntegrationStageKind.Price, $"{commandId}:{item.ItemKey}:price",
                JsonSerializer.Serialize(new { sku, price }), ["card"], IsEffectful: true));
        }

        if (TryReadInt(item.Data, "quantity", out var qty) || TryReadInt(item.Data, "stock", out qty))
        {
            if (qty < 0)
            {
                issues.Add(new IntegrationIssue("invalid_stock", "Stock quantity cannot be negative", "stock", "quantity", Retryable: false, ItemKey: item.ItemKey));
                return;
            }
            stages.Add(new IntegrationPreparedStage(
                "stock", IntegrationStageKind.Stock, $"{commandId}:{item.ItemKey}:stock",
                JsonSerializer.Serialize(new { sku, quantity = qty }), ["card"], IsEffectful: true));
        }

        prepared.Add(new IntegrationPreparedItem(item.ItemKey, stages));
    }

    private static void PreparePriceItem(
        string commandId, IntegrationItemIntent item, List<IntegrationPreparedItem> prepared, List<IntegrationIssue> issues)
    {
        var sku = ReadStr(item.Data, "sku") ?? item.ExpectedRemoteId;
        if (string.IsNullOrWhiteSpace(sku) || !TryReadDecimal(item.Data, "price", out var price) || price < 0)
        {
            issues.Add(new IntegrationIssue("invalid_price_payload", "Medusa price export requires sku and non-negative price",
                Retryable: false, ItemKey: item.ItemKey));
            return;
        }
        prepared.Add(new IntegrationPreparedItem(item.ItemKey, [new IntegrationPreparedStage(
            "price", IntegrationStageKind.Price, $"{commandId}:{item.ItemKey}:price",
            JsonSerializer.Serialize(new { sku, price }), [], IsEffectful: true)]));
    }

    private static void PrepareStockItem(
        string commandId, IntegrationItemIntent item, List<IntegrationPreparedItem> prepared, List<IntegrationIssue> issues)
    {
        var sku = ReadStr(item.Data, "sku") ?? item.ExpectedRemoteId;
        if (string.IsNullOrWhiteSpace(sku) || !(TryReadInt(item.Data, "quantity", out var qty) || TryReadInt(item.Data, "stock", out qty)) || qty < 0)
        {
            issues.Add(new IntegrationIssue("invalid_stock_payload", "Medusa stock export requires sku and non-negative quantity",
                Retryable: false, ItemKey: item.ItemKey));
            return;
        }
        prepared.Add(new IntegrationPreparedItem(item.ItemKey, [new IntegrationPreparedStage(
            "stock", IntegrationStageKind.Stock, $"{commandId}:{item.ItemKey}:stock",
            JsonSerializer.Serialize(new { sku, quantity = qty }), [], IsEffectful: true)]));
    }

    private static void PrepareGroupItem(
        string commandId, IntegrationItemIntent item, List<IntegrationPreparedItem> prepared, List<IntegrationIssue> issues)
    {
        var groupName = ReadStr(item.Data, "groupName") ?? ReadStr(item.Data, "name");
        if (string.IsNullOrWhiteSpace(groupName))
        {
            issues.Add(new IntegrationIssue("invalid_group_payload", "Medusa group update requires groupName",
                FieldKey: "groupName", Retryable: false, ItemKey: item.ItemKey));
            return;
        }
        prepared.Add(new IntegrationPreparedItem(item.ItemKey, [new IntegrationPreparedStage(
            "group", IntegrationStageKind.Grouping, $"{commandId}:{item.ItemKey}:group",
            JsonSerializer.Serialize(new { groupId = item.ExpectedRemoteId, name = groupName }), [], IsEffectful: true)]));
    }

    private static void PrepareOrderActionItem(
        string commandId, IntegrationItemIntent item, List<IntegrationPreparedItem> prepared, List<IntegrationIssue> issues)
    {
        var orderId = item.ExpectedRemoteId ?? ReadStr(item.Data, "orderId");
        var action = ReadStr(item.Data, "action") ?? ReadStr(item.Data, "status");
        if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(action))
        {
            issues.Add(new IntegrationIssue("invalid_order_action", "Medusa order action requires orderId and action/status",
                Retryable: false, ItemKey: item.ItemKey));
            return;
        }
        prepared.Add(new IntegrationPreparedItem(item.ItemKey, [new IntegrationPreparedStage(
            "order", IntegrationStageKind.OrderAction, $"{commandId}:{item.ItemKey}:order",
            JsonSerializer.Serialize(new { orderId, status = action }), [], IsEffectful: true)]));
    }

    private static bool TryBuildBridgeUri(
        ConnectionContext connection, IntegrationStageKind kind, out Uri? uri, out string authHeader)
    {
        uri = null;
        authHeader = "";
        var apiBaseUrl = connection.GetCredential("apiBaseUrl")?.Trim();
        var apiKey = connection.GetCredential("apiKey")?.Trim();
        if (string.IsNullOrWhiteSpace(apiBaseUrl) || string.IsNullOrWhiteSpace(apiKey) ||
            !Uri.TryCreate(apiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
            return false;

        var relative = kind switch
        {
            IntegrationStageKind.Card => "admin/delegat/products",
            IntegrationStageKind.Price => "admin/delegat/prices",
            IntegrationStageKind.Stock => "admin/delegat/stocks",
            IntegrationStageKind.Grouping => "admin/delegat/groups",
            IntegrationStageKind.OrderAction => "admin/delegat/orders/status",
            _ => null
        };
        if (relative is null)
            return false;

        uri = new Uri(baseUri, relative);
        authHeader = $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:"))}";
        return true;
    }

    private static IntegrationStageOutcome MapBridgeResponse(
        IntegrationTransportResponse resp, string stageKey, string itemKey)
    {
        if (resp.StatusCode is >= 200 and < 300)
        {
            string? receiptId = null;
            if (!string.IsNullOrWhiteSpace(resp.Body))
            {
                try
                {
                    using var doc = JsonDocument.Parse(resp.Body);
                    if (doc.RootElement.TryGetProperty("id", out var idEl))
                        receiptId = idEl.ToString();
                    else if (doc.RootElement.TryGetProperty("taskId", out var taskEl))
                        receiptId = taskEl.ToString();
                }
                catch (JsonException) { }
            }

            if (resp.StatusCode == 202)
            {
                return new IntegrationStageOutcome(
                    IntegrationStageOutcomeKind.Accepted,
                    Receipt: receiptId is null ? null : new IntegrationProviderReceipt("medusa-task", receiptId),
                    NextCheckAt: null);
            }

            return new IntegrationStageOutcome(
                IntegrationStageOutcomeKind.Applied,
                Receipt: receiptId is null ? null : new IntegrationProviderReceipt("medusa-entity", receiptId),
                AppliedEvidence: new Dictionary<string, object?> { ["status"] = resp.StatusCode, ["id"] = receiptId });
        }

        if (resp.StatusCode is 400 or 401 or 403 or 404 or 422)
        {
            return new IntegrationStageOutcome(
                IntegrationStageOutcomeKind.Rejected,
                Issues: [new IntegrationIssue($"bridge_http_{resp.StatusCode}", $"Medusa bridge rejected request with HTTP {resp.StatusCode}", stageKey, Retryable: false, ItemKey: itemKey)]);
        }

        if (resp.StatusCode is 429 or 503)
        {
            return new IntegrationStageOutcome(
                IntegrationStageOutcomeKind.Pending,
                NextCheckAt: null,
                Issues: [new IntegrationIssue($"bridge_http_{resp.StatusCode}", $"Medusa bridge throttled request with HTTP {resp.StatusCode}", stageKey, Retryable: true, ItemKey: itemKey)]);
        }

        return new IntegrationStageOutcome(
            IntegrationStageOutcomeKind.Unknown,
            Issues: [new IntegrationIssue($"bridge_http_{resp.StatusCode}", $"Medusa bridge returned ambiguous HTTP {resp.StatusCode}", stageKey, Retryable: false, ItemKey: itemKey)]);
    }

    private static string? ReadStr(IReadOnlyDictionary<string, object?> data, string key)
    {
        foreach (var (k, v) in data)
        {
            if (k.Equals(key, StringComparison.OrdinalIgnoreCase) && v is not null)
                return v.ToString()?.Trim();
        }
        return null;
    }

    private static bool TryReadDecimal(IReadOnlyDictionary<string, object?> data, string key, out decimal value)
    {
        value = 0;
        var raw = ReadStr(data, key);
        return !string.IsNullOrWhiteSpace(raw) && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadInt(IReadOnlyDictionary<string, object?> data, string key, out int value)
    {
        value = 0;
        var raw = ReadStr(data, key);
        return !string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
