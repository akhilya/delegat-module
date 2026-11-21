using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Delegat.Marketplace.Medusa.Models;
using Delegat.Marketplace.SDK;

namespace Delegat.Marketplace.Medusa;

public partial class MedusaPlugin
{
    private static readonly JsonSerializerOptions V1JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

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

        if (!TryBuildBridgeUri(request.Connection, request.Stage.Kind, request.Stage.StageKey, out var uri, out var authHeader))
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

        return MapBridgeResponse(resp, request.Stage.Kind, request.Stage.StageKey, request.Attempt.ItemKey);
    }

    public Task<IntegrationStageOutcome> ConfirmAsync(
        IntegrationConfirmationRequest request, CancellationToken ct = default) =>
        // Medusa Delegat bridge has no verified async receipt polling endpoint; keep ambiguous effects honest.
        Task.FromResult(new IntegrationStageOutcome(
            IntegrationStageOutcomeKind.Unknown,
            Receipt: request.Receipt,
            Issues: [new IntegrationIssue("reconciliation_unsupported", "Medusa bridge does not expose a verified async receipt confirmation endpoint", request.Stage.StageKey, Retryable: false, ItemKey: request.Attempt.ItemKey)]));

    public Task<IntegrationStageOutcome> CancelAcceptedAsync(
        IntegrationConfirmationRequest request, CancellationToken ct = default) =>
        Task.FromResult(new IntegrationStageOutcome(
            IntegrationStageOutcomeKind.Unknown,
            Issues: [new IntegrationIssue("cancel_not_supported", "Medusa bridge does not support cancelling accepted mutations", request.Stage.StageKey, Retryable: false, ItemKey: request.Attempt.ItemKey)]));

    private void PrepareProductItem(
        string commandId, IntegrationItemIntent item, List<IntegrationPreparedItem> prepared, List<IntegrationIssue> issues)
    {
        var nonNullData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in item.Data)
            if (v is not null)
                nonNullData[k] = v;

        // Reuse the existing normalized Medusa product mapper so variants, images,
        // categories, groups, attributes, prices, and stocks are never silently dropped.
        var payload = BuildProductPayload(nonNullData, item.ExpectedRemoteId);
        if (string.IsNullOrWhiteSpace(payload.Sku) || string.IsNullOrWhiteSpace(payload.Name))
        {
            issues.Add(new IntegrationIssue("missing_required_fields",
                "Medusa product export requires non-empty sku and name",
                FieldKey: string.IsNullOrWhiteSpace(payload.Sku) ? "sku" : "name",
                Retryable: false, ItemKey: item.ItemKey));
            return;
        }
        if (payload.Price is < 0)
        {
            issues.Add(new IntegrationIssue("invalid_price", "Price cannot be negative", "card", "price", Retryable: false, ItemKey: item.ItemKey));
            return;
        }
        if (payload.Quantity is < 0)
        {
            issues.Add(new IntegrationIssue("invalid_stock", "Stock quantity cannot be negative", "card", "quantity", Retryable: false, ItemKey: item.ItemKey));
            return;
        }

        var cardJson = JsonSerializer.Serialize(payload, V1JsonOptions);
        var cardStage = new IntegrationPreparedStage(
            "card", IntegrationStageKind.Card, $"{commandId}:{item.ItemKey}:card", cardJson, [], IsEffectful: true);

        prepared.Add(new IntegrationPreparedItem(item.ItemKey, [cardStage]));
    }

    private static void PreparePriceItem(
        string commandId, IntegrationItemIntent item, List<IntegrationPreparedItem> prepared, List<IntegrationIssue> issues)
    {
        var productId = item.ExpectedRemoteId ?? ReadStr(item.Data, "productId") ?? ReadStr(item.Data, "sku");
        if (string.IsNullOrWhiteSpace(productId) || !TryReadDecimal(item.Data, "price", out var price) || price < 0)
        {
            issues.Add(new IntegrationIssue("invalid_price_payload", "Medusa price export requires productId/sku and non-negative price",
                Retryable: false, ItemKey: item.ItemKey));
            return;
        }
        var envelope = new MedusaBulkPricesRequest
        {
            Prices = [new MedusaPriceItem { ProductId = productId, Price = price }]
        };
        prepared.Add(new IntegrationPreparedItem(item.ItemKey, [new IntegrationPreparedStage(
            "price", IntegrationStageKind.Price, $"{commandId}:{item.ItemKey}:price",
            JsonSerializer.Serialize(envelope, V1JsonOptions), [], IsEffectful: true)]));
    }

    private static void PrepareStockItem(
        string commandId, IntegrationItemIntent item, List<IntegrationPreparedItem> prepared, List<IntegrationIssue> issues)
    {
        var productId = item.ExpectedRemoteId ?? ReadStr(item.Data, "productId") ?? ReadStr(item.Data, "sku");
        if (string.IsNullOrWhiteSpace(productId) || !(TryReadInt(item.Data, "quantity", out var qty) || TryReadInt(item.Data, "stock", out qty)) || qty < 0)
        {
            issues.Add(new IntegrationIssue("invalid_stock_payload", "Medusa stock export requires productId/sku and non-negative quantity",
                Retryable: false, ItemKey: item.ItemKey));
            return;
        }
        var envelope = new MedusaBulkStocksRequest
        {
            Stocks = [new MedusaStockItem { ProductId = productId, Quantity = qty }]
        };
        prepared.Add(new IntegrationPreparedItem(item.ItemKey, [new IntegrationPreparedStage(
            "stock", IntegrationStageKind.Stock, $"{commandId}:{item.ItemKey}:stock",
            JsonSerializer.Serialize(envelope, V1JsonOptions), [], IsEffectful: true)]));
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
        var req = new MedusaUpsertGroupRequest
        {
            CollectionId = item.ExpectedRemoteId,
            Name = groupName
        };
        prepared.Add(new IntegrationPreparedItem(item.ItemKey, [new IntegrationPreparedStage(
            "group", IntegrationStageKind.Grouping, $"{commandId}:{item.ItemKey}:group",
            JsonSerializer.Serialize(req, V1JsonOptions), [], IsEffectful: true)]));
    }

    private static void PrepareOrderActionItem(
        string commandId, IntegrationItemIntent item, List<IntegrationPreparedItem> prepared, List<IntegrationIssue> issues)
    {
        var orderId = item.ExpectedRemoteId ?? ReadStr(item.Data, "orderId");
        var action = (ReadStr(item.Data, "action") ?? ReadStr(item.Data, "status"))?.Trim();
        if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(action))
        {
            issues.Add(new IntegrationIssue("invalid_order_action", "Medusa order action requires orderId and action",
                Retryable: false, ItemKey: item.ItemKey));
            return;
        }

        if (action.Equals("cancel", StringComparison.OrdinalIgnoreCase))
        {
            bool? refundDecision = null;
            foreach (var (k, v) in item.Data)
            {
                if (!k.Trim().Equals("refund", StringComparison.OrdinalIgnoreCase) || v is null)
                    continue;
                var raw = v.ToString()?.Trim();
                var parsed = !string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(raw, "0", StringComparison.Ordinal)
                    && !string.Equals(raw, "no", StringComparison.OrdinalIgnoreCase);
                if (refundDecision.HasValue && refundDecision.Value != parsed)
                {
                    issues.Add(new IntegrationIssue("conflicting_order_extra",
                        "Medusa order cancel received conflicting refund flags",
                        FieldKey: "refund", Retryable: false, ItemKey: item.ItemKey));
                    return;
                }
                refundDecision = parsed;
            }
            var refund = refundDecision ?? true;
            var reason = ReadStr(item.Data, "reason");
            var note = ReadStr(item.Data, "note");
            prepared.Add(new IntegrationPreparedItem(item.ItemKey, [new IntegrationPreparedStage(
                $"order:cancel:{orderId}", IntegrationStageKind.OrderAction, $"{commandId}:{item.ItemKey}:order:cancel",
                JsonSerializer.Serialize(new { refund, reason, note }, V1JsonOptions), [], IsEffectful: true)]));
            return;
        }

        prepared.Add(new IntegrationPreparedItem(item.ItemKey, [new IntegrationPreparedStage(
            $"order:{orderId}", IntegrationStageKind.OrderAction, $"{commandId}:{item.ItemKey}:order",
            JsonSerializer.Serialize(new { action }, V1JsonOptions), [], IsEffectful: true)]));
    }

    private static bool TryBuildBridgeUri(
        ConnectionContext connection, IntegrationStageKind kind, string stageKey, out Uri? uri, out string authHeader)
    {
        uri = null;
        authHeader = "";
        var apiBaseUrl = connection.GetCredential("apiBaseUrl")?.Trim();
        var apiKey = connection.GetCredential("apiKey")?.Trim();
        if (string.IsNullOrWhiteSpace(apiBaseUrl) || string.IsNullOrWhiteSpace(apiKey) ||
            !Uri.TryCreate(apiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
            return false;

        // Resolve endpoints relative to the configured Medusa bridge root (e.g. https://shop/admin/delegat/).
        string? relative = kind switch
        {
            IntegrationStageKind.Card => "products/upsert",
            IntegrationStageKind.Price => "prices/bulk",
            IntegrationStageKind.Stock => "stocks/bulk",
            IntegrationStageKind.Grouping => "groups/upsert",
            IntegrationStageKind.OrderAction when stageKey.StartsWith("order:cancel:", StringComparison.Ordinal) =>
                $"orders/{Uri.EscapeDataString(stageKey["order:cancel:".Length..])}/cancel",
            IntegrationStageKind.OrderAction when stageKey.StartsWith("order:", StringComparison.Ordinal) =>
                $"orders/{Uri.EscapeDataString(stageKey["order:".Length..])}/status",
            _ => null
        };
        if (relative is null)
            return false;

        uri = new Uri(baseUri, relative);
        authHeader = $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:"))}";
        return true;
    }

    private static IntegrationStageOutcome MapBridgeResponse(
        IntegrationTransportResponse resp, IntegrationStageKind kind, string stageKey, string itemKey)
    {
        if (resp.StatusCode == 202)
        {
            string? acceptedId = null;
            if (!string.IsNullOrWhiteSpace(resp.Body))
            {
                try
                {
                    using var doc = JsonDocument.Parse(resp.Body);
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        acceptedId = root.TryGetProperty("job_id", out var jid) ? jid.ToString()
                            : root.TryGetProperty("task_id", out var tid) ? tid.ToString()
                            : root.TryGetProperty("product_id", out var pid) ? pid.ToString()
                            : root.TryGetProperty("id", out var idEl) ? idEl.ToString()
                            : root.TryGetProperty("group_id", out var gid) ? gid.ToString()
                            : null;
                    }
                }
                catch (JsonException) { }
            }

            return new IntegrationStageOutcome(
                IntegrationStageOutcomeKind.Unknown,
                Receipt: string.IsNullOrWhiteSpace(acceptedId) ? null : new IntegrationProviderReceipt("medusa-accepted", acceptedId),
                Issues: [new IntegrationIssue("bridge_async_reconciliation_unsupported", "Medusa bridge returned HTTP 202 acceptance without a verified completion check", stageKey, Retryable: false, ItemKey: itemKey)]);
        }

        if (resp.StatusCode is >= 200 and < 300)
        {
            if (string.IsNullOrWhiteSpace(resp.Body))
                return new IntegrationStageOutcome(IntegrationStageOutcomeKind.Unknown,
                    Issues: [new IntegrationIssue("bridge_empty_body", "Medusa bridge returned 2xx with empty body", stageKey, Retryable: false, ItemKey: itemKey)]);

            try
            {
                using var doc = JsonDocument.Parse(resp.Body);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return new IntegrationStageOutcome(IntegrationStageOutcomeKind.Unknown,
                        Issues: [new IntegrationIssue("bridge_unexpected_json", "Medusa bridge returned non-object JSON", stageKey, Retryable: false, ItemKey: itemKey)]);

                // Validate explicit top-level success flag when present.
                if (root.TryGetProperty("success", out var okEl) && okEl.ValueKind == JsonValueKind.False)
                {
                    var errMsg = root.TryGetProperty("error", out var err) ? err.ToString() : "Medusa bridge reported success=false";
                    return new IntegrationStageOutcome(IntegrationStageOutcomeKind.Rejected,
                        Issues: [new IntegrationIssue("bridge_semantic_rejected", errMsg, stageKey, Retryable: false, ItemKey: itemKey)]);
                }

                // Validate bulk results array when present (prices/bulk, stocks/bulk).
                if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
                {
                    if (results.GetArrayLength() == 0)
                        return new IntegrationStageOutcome(IntegrationStageOutcomeKind.Unknown,
                            Issues: [new IntegrationIssue("bridge_missing_result", "Medusa bulk response contained zero item results", stageKey, Retryable: false, ItemKey: itemKey)]);
                    foreach (var entry in results.EnumerateArray())
                    {
                        if (entry.ValueKind != JsonValueKind.Object)
                            return new IntegrationStageOutcome(IntegrationStageOutcomeKind.Unknown,
                                Issues: [new IntegrationIssue("bridge_unexpected_result_entry", "Medusa bulk response contained a non-object result entry", stageKey, Retryable: false, ItemKey: itemKey)]);
                        if (entry.TryGetProperty("success", out var itemOk) && itemOk.ValueKind == JsonValueKind.False)
                        {
                            var msg = entry.TryGetProperty("error", out var ie) ? ie.ToString() : "Medusa item mutation rejected";
                            return new IntegrationStageOutcome(IntegrationStageOutcomeKind.Rejected,
                                Issues: [new IntegrationIssue("bridge_item_rejected", msg, stageKey, Retryable: false, ItemKey: itemKey)]);
                        }
                    }
                    return new IntegrationStageOutcome(IntegrationStageOutcomeKind.Applied,
                        AppliedEvidence: new Dictionary<string, object?> { ["status"] = resp.StatusCode });
                }

                // For single upsert responses, require explicit success=true or a returned entity ID.
                string? id = root.TryGetProperty("product_id", out var pid) ? pid.ToString()
                    : root.TryGetProperty("id", out var idEl) ? idEl.ToString()
                    : root.TryGetProperty("group_id", out var gid) ? gid.ToString()
                    : null;
                var explicitSuccess = root.TryGetProperty("success", out var ok2) && ok2.ValueKind == JsonValueKind.True;
                if (!explicitSuccess && string.IsNullOrWhiteSpace(id))
                {
                    return new IntegrationStageOutcome(IntegrationStageOutcomeKind.Unknown,
                        Issues: [new IntegrationIssue("bridge_unconfirmed_body", "Medusa 2xx response lacked success=true or entity id", stageKey, Retryable: false, ItemKey: itemKey)]);
                }

                return new IntegrationStageOutcome(
                    IntegrationStageOutcomeKind.Applied,
                    Receipt: id is null ? null : new IntegrationProviderReceipt("medusa-entity", id),
                    AppliedEvidence: new Dictionary<string, object?> { ["status"] = resp.StatusCode, ["id"] = id });
            }
            catch (JsonException)
            {
                return new IntegrationStageOutcome(IntegrationStageOutcomeKind.Unknown,
                    Issues: [new IntegrationIssue("bridge_malformed_json", "Medusa 2xx response was not valid JSON", stageKey, Retryable: false, ItemKey: itemKey)]);
            }
        }

        if (resp.StatusCode is 400 or 401 or 403 or 404 or 422)
        {
            return new IntegrationStageOutcome(
                IntegrationStageOutcomeKind.Rejected,
                Issues: [new IntegrationIssue($"bridge_http_{resp.StatusCode}", $"Medusa bridge rejected request with HTTP {resp.StatusCode}", stageKey, Retryable: false, ItemKey: itemKey)]);
        }

        if (resp.StatusCode is 429 or 503)
        {
            // Proven before-effect throttle: return Retryable Rejected so worker re-dispatches cleanly.
            return new IntegrationStageOutcome(
                IntegrationStageOutcomeKind.Rejected,
                Issues: [new IntegrationIssue($"bridge_http_{resp.StatusCode}", $"Medusa bridge throttled request with HTTP {resp.StatusCode}", stageKey, Retryable: true, ItemKey: itemKey)]);
        }

        return new IntegrationStageOutcome(
            IntegrationStageOutcomeKind.Unknown,
            Issues: [new IntegrationIssue($"bridge_http_{resp.StatusCode}", $"Medusa bridge returned ambiguous HTTP {resp.StatusCode}", stageKey, Retryable: false, ItemKey: itemKey)]);
    }

    private static string? ReadStr(IReadOnlyDictionary<string, object?> data, string key)
    {
        foreach (var (k, v) in data)
            if (k.Trim().Equals(key, StringComparison.OrdinalIgnoreCase) && v is not null)
                return v.ToString()?.Trim();
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
