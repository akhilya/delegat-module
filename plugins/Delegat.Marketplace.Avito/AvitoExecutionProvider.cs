using System.Globalization;
using System.Text.Json;
using Delegat.Marketplace.SDK;

namespace Delegat.Marketplace.Avito;

public sealed partial class AvitoPlugin
{
    private static readonly Uri AvitoApiOrigin = new("https://api.avito.ru");

    public Task<IntegrationPreparationResult> PrepareAsync(
        IntegrationPreparationInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.OperationKind != IntegrationOperationKind.PriceExport)
        {
            var unsupported = input.Items.Select(item => new IntegrationIssue(
                "unsupported_operation",
                $"Avito does not support {input.OperationKind}",
                Retryable: false,
                ItemKey: item.ItemKey)).ToList();
            return Task.FromResult(new IntegrationPreparationResult([], unsupported));
        }

        var prepared = new List<IntegrationPreparedItem>(input.Items.Count);
        var issues = new List<IntegrationIssue>();

        foreach (var item in input.Items)
        {
            var rawItemId = !string.IsNullOrWhiteSpace(item.ExpectedRemoteId)
                ? item.ExpectedRemoteId.Trim()
                : TryGetString(item.Data, "itemId") ?? TryGetString(item.Data, "remoteId");

            if (string.IsNullOrWhiteSpace(rawItemId) || !long.TryParse(rawItemId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericItemId) || numericItemId <= 0)
            {
                issues.Add(new IntegrationIssue(
                    "invalid_remote_id",
                    "Avito price export requires a positive numeric Avito item ID",
                    FieldKey: "itemId",
                    Retryable: false,
                    ItemKey: item.ItemKey));
                continue;
            }

            if (!TryGetPositivePrice(item.Data, out var priceInt))
            {
                issues.Add(new IntegrationIssue(
                    "invalid_price",
                    "Avito price export requires a positive numeric price",
                    FieldKey: "price",
                    Retryable: false,
                    ItemKey: item.ItemKey));
                continue;
            }

            var payloadJson = JsonSerializer.Serialize(new { price = priceInt });
            var stageKey = $"price:{numericItemId}";
            var stage = new IntegrationPreparedStage(
                StageKey: stageKey,
                Kind: IntegrationStageKind.Price,
                RequestKey: $"{input.CommandId}:{item.ItemKey}:{stageKey}",
                PayloadJson: payloadJson,
                DependsOn: [],
                IsEffectful: true,
                Evidence: new Dictionary<string, object?> { ["itemId"] = numericItemId });

            prepared.Add(new IntegrationPreparedItem(item.ItemKey, [stage]));
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
                Issues: [new IntegrationIssue("transport_missing", "V1 transport is required for Avito execution", request.Stage.StageKey, Retryable: false, ItemKey: request.Attempt.ItemKey)]);

        if (request.Stage.Kind != IntegrationStageKind.Price ||
            !TryExtractItemId(request.Stage.StageKey, out var itemId))
        {
            return new IntegrationStageOutcome(IntegrationStageOutcomeKind.Rejected,
                Issues: [new IntegrationIssue("invalid_stage", "Avito stage must be a prepared price stage with itemId", request.Stage.StageKey, Retryable: false, ItemKey: request.Attempt.ItemKey)]);
        }

        var token = await AcquireAccessTokenAsync(request.Connection, transport, request.Attempt, ct);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new IntegrationStageOutcome(IntegrationStageOutcomeKind.Rejected,
                Issues: [new IntegrationIssue("auth_failed", "Unable to obtain Avito OAuth token", request.Stage.StageKey, Retryable: false, ItemKey: request.Attempt.ItemKey)]);
        }

        var url = new Uri(AvitoApiOrigin, $"/core/v1/items/{itemId}/update_price");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"] = $"Bearer {token}"
        };
        var resp = await transport.SendAsync(new IntegrationTransportRequest(
            "POST", url, headers, request.Stage.PayloadJson,
            new IntegrationRequestPolicy("avito:price", request.Attempt.OperationId, IsEffectful: true)), ct);

        return resp.StatusCode switch
        {
            >= 200 and < 300 => new IntegrationStageOutcome(
                IntegrationStageOutcomeKind.Applied,
                Receipt: new IntegrationProviderReceipt("avito-item", itemId),
                AppliedEvidence: new Dictionary<string, object?> { ["itemId"] = itemId, ["status"] = resp.StatusCode }),
            400 or 401 or 403 or 404 or 422 => new IntegrationStageOutcome(
                IntegrationStageOutcomeKind.Rejected,
                Issues: [new IntegrationIssue($"provider_http_{resp.StatusCode}", $"Avito rejected price update with HTTP {resp.StatusCode}", request.Stage.StageKey, Retryable: false, ItemKey: request.Attempt.ItemKey)]),
            429 or 503 => new IntegrationStageOutcome(
                IntegrationStageOutcomeKind.Pending,
                NextCheckAt: null,
                Issues: [new IntegrationIssue($"provider_http_{resp.StatusCode}", $"Avito throttled price update with HTTP {resp.StatusCode}", request.Stage.StageKey, Retryable: true, ItemKey: request.Attempt.ItemKey)]),
            _ => new IntegrationStageOutcome(
                IntegrationStageOutcomeKind.Unknown,
                Issues: [new IntegrationIssue($"provider_http_{resp.StatusCode}", $"Avito price update returned ambiguous HTTP {resp.StatusCode}", request.Stage.StageKey, Retryable: false, ItemKey: request.Attempt.ItemKey)])
        };
    }

    public async Task<IntegrationStageOutcome> ConfirmAsync(
        IntegrationConfirmationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var transport = request.Connection.Transport;
        if (transport is null || !TryExtractItemId(request.Stage.StageKey, out var itemId))
            return new IntegrationStageOutcome(IntegrationStageOutcomeKind.Unknown);

        var token = await AcquireAccessTokenAsync(request.Connection, transport, request.Attempt, ct);
        if (string.IsNullOrWhiteSpace(token))
            return new IntegrationStageOutcome(IntegrationStageOutcomeKind.Unknown);

        var resp = await transport.SendAsync(new IntegrationTransportRequest(
            "GET", new Uri(AvitoApiOrigin, "/core/v1/items"),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Authorization"] = $"Bearer {token}" },
            null,
            new IntegrationRequestPolicy("avito:items-list", request.Attempt.OperationId, IsEffectful: false)), ct);

        if (resp.StatusCode is < 200 or >= 300 || string.IsNullOrWhiteSpace(resp.Body))
            return new IntegrationStageOutcome(IntegrationStageOutcomeKind.Unknown);

        try
        {
            using var doc = JsonDocument.Parse(resp.Body);
            using var expectedDoc = JsonDocument.Parse(request.Stage.PayloadJson);
            var expectedPrice = expectedDoc.RootElement.GetProperty("price").GetInt64();
            if (doc.RootElement.TryGetProperty("resources", out var resources) && resources.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in resources.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var idEl) && idEl.ToString() == itemId &&
                        item.TryGetProperty("price", out var priceEl) && priceEl.TryGetInt64(out var actualPrice) &&
                        actualPrice == expectedPrice)
                    {
                        return new IntegrationStageOutcome(
                            IntegrationStageOutcomeKind.Applied,
                            Receipt: new IntegrationProviderReceipt("avito-item", itemId),
                            AppliedEvidence: new Dictionary<string, object?> { ["itemId"] = itemId, ["price"] = actualPrice });
                    }
                }
            }
        }
        catch (JsonException)
        {
            return new IntegrationStageOutcome(IntegrationStageOutcomeKind.Unknown);
        }

        return new IntegrationStageOutcome(IntegrationStageOutcomeKind.Unknown,
            Issues: [new IntegrationIssue("reconciliation_unconfirmed", "Avito listing read-back did not confirm the expected price", request.Stage.StageKey, Retryable: false, ItemKey: request.Attempt.ItemKey)]);
    }

    public Task<IntegrationStageOutcome> CancelAcceptedAsync(
        IntegrationConfirmationRequest request, CancellationToken ct = default) =>
        Task.FromResult(new IntegrationStageOutcome(
            IntegrationStageOutcomeKind.Unknown,
            Issues: [new IntegrationIssue("cancel_not_supported", "Avito does not support cancelling an in-flight price update", request.Stage.StageKey, Retryable: false, ItemKey: request.Attempt.ItemKey)]));

    private static async Task<string?> AcquireAccessTokenAsync(
        ConnectionContext connection, IMarketplaceTransport transport, IntegrationAttemptContext attempt, CancellationToken ct)
    {
        var existing = connection.GetCredential("accessToken");
        if (!string.IsNullOrWhiteSpace(existing))
            return existing.Trim();

        var clientId = connection.GetCredential("clientId")?.Trim();
        var clientSecret = connection.GetCredential("clientSecret")?.Trim();
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return null;

        var formBody = $"grant_type=client_credentials&client_id={Uri.EscapeDataString(clientId)}&client_secret={Uri.EscapeDataString(clientSecret)}";
        var resp = await transport.SendAsync(new IntegrationTransportRequest(
            "POST", new Uri(AvitoApiOrigin, "/token"),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = "application/x-www-form-urlencoded"
            },
            formBody,
            new IntegrationRequestPolicy("avito:auth", attempt.OperationId, IsEffectful: false)), ct);

        if (resp.StatusCode is < 200 or >= 300 || string.IsNullOrWhiteSpace(resp.Body))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(resp.Body);
            return doc.RootElement.TryGetProperty("access_token", out var tok) ? tok.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryExtractItemId(string stageKey, out string itemId)
    {
        itemId = "";
        if (!stageKey.StartsWith("price:", StringComparison.Ordinal))
            return false;
        itemId = stageKey["price:".Length..];
        return long.TryParse(itemId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0;
    }

    private static string? TryGetString(IReadOnlyDictionary<string, object?> data, string key)
    {
        foreach (var (k, v) in data)
        {
            if (k.Equals(key, StringComparison.OrdinalIgnoreCase) && v is not null)
                return v.ToString()?.Trim();
        }
        return null;
    }

    private static bool TryGetPositivePrice(IReadOnlyDictionary<string, object?> data, out long price)
    {
        price = 0;
        var raw = TryGetString(data, "price");
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var dec) || dec <= 0)
            return false;
        price = (long)Math.Round(dec, MidpointRounding.AwayFromZero);
        return price > 0;
    }
}
