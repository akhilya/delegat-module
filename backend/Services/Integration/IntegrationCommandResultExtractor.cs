using System.Text.Json;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Models.Integration;

namespace Delegat.Services.Integration;

/// <summary>
/// Extracts safe, typed integration outcomes and created resource identities
/// (shipment IDs, shipping-unit box IDs, order/promotion counts) from durable
/// <see cref="IntegrationCommand"/> and <see cref="IntegrationStage"/> evidence
/// for Public API polling and bounded synchronous responses.
/// </summary>
public static class IntegrationCommandResultExtractor
{
    public static object BuildPublicOperationIntegration(
        IntegrationCommand command,
        IReadOnlyList<IntegrationStage> stages)
    {
        var shipmentId = ExtractShipmentId(stages);
        var shipmentName = ExtractShipmentName(command, stages);
        var boxIds = ExtractBoxIds(stages);
        var (added, removed) = ExtractPromotionCounts(stages);

        object? typedResult = null;
        if (command.state is IntegrationCommandState.Completed or IntegrationCommandState.Partial)
        {
            if (command.kind.StartsWith("shipment_", StringComparison.OrdinalIgnoreCase))
            {
                typedResult = new
                {
                    shipmentId,
                    shipment = string.IsNullOrWhiteSpace(shipmentId)
                        ? null
                        : new ExternalShipment
                        {
                            ExternalId = shipmentId,
                            Name = shipmentName ?? "",
                            Status = MarketplaceShipmentStatuses.Open
                        },
                    boxIds,
                    boxes = boxIds.Select(id => new ExternalShipmentBox { ExternalId = id }).ToList()
                };
            }
            else if (command.kind == "promotion_action")
            {
                typedResult = new PromotionProductsResult
                {
                    Success = command.state == IntegrationCommandState.Completed,
                    Added = added,
                    Removed = removed
                };
            }
            else if (command.kind == "order_action")
            {
                typedResult = new OrderStatusUpdateResult
                {
                    Success = command.state == IntegrationCommandState.Completed
                };
            }
        }

        return new
        {
            commandId = command.commandId,
            kind = command.kind,
            state = command.state,
            stopRequested = command.stopRequested,
            summary = command.summary,
            finishedAt = command.finishedAt,
            result = typedResult,
            stages = stages.Select(s => new
            {
                itemKey = s.itemKey,
                stageKey = s.stageKey,
                kind = s.kind,
                state = s.state,
                evidence = TryParseJsonElement(s.evidenceJson),
                issues = TryParseJsonElement(s.issuesJson)
            }).ToList()
        };
    }

    public static ShipmentMutationResult BuildShipmentMutationResult(
        IntegrationCommand command,
        IReadOnlyList<IntegrationStage> stages)
    {
        var shipmentId = ExtractShipmentId(stages);
        var shipmentName = ExtractShipmentName(command, stages);
        return new ShipmentMutationResult
        {
            Success = command.state == IntegrationCommandState.Completed,
            ErrorMessage = command.state == IntegrationCommandState.Completed ? null : command.summary,
            Shipment = string.IsNullOrWhiteSpace(shipmentId)
                ? null
                : new ExternalShipment
                {
                    ExternalId = shipmentId,
                    Name = shipmentName ?? "",
                    Status = MarketplaceShipmentStatuses.Open
                }
        };
    }

    public static CreateShipmentBoxesResult BuildCreateShipmentBoxesResult(
        IntegrationCommand command,
        IReadOnlyList<IntegrationStage> stages)
    {
        var boxIds = ExtractBoxIds(stages);
        return new CreateShipmentBoxesResult
        {
            Success = command.state == IntegrationCommandState.Completed,
            ErrorMessage = command.state == IntegrationCommandState.Completed ? null : command.summary,
            Boxes = boxIds.Select(id => new ExternalShipmentBox { ExternalId = id }).ToList()
        };
    }

    public static OrderStatusUpdateResult BuildOrderStatusUpdateResult(
        IntegrationCommand command,
        IReadOnlyList<IntegrationStage> stages)
    {
        return new OrderStatusUpdateResult
        {
            Success = command.state == IntegrationCommandState.Completed,
            ErrorMessage = command.state == IntegrationCommandState.Completed ? null : command.summary
        };
    }

    public static PromotionProductsResult BuildPromotionProductsResult(
        IntegrationCommand command,
        IReadOnlyList<IntegrationStage> stages)
    {
        var (added, removed) = ExtractPromotionCounts(stages);
        return new PromotionProductsResult
        {
            Success = command.state == IntegrationCommandState.Completed,
            Added = added,
            Removed = removed,
            ErrorMessage = command.state == IntegrationCommandState.Completed ? null : command.summary
        };
    }

    public static string? ExtractShipmentId(IReadOnlyList<IntegrationStage> stages)
    {
        foreach (var stage in stages)
        {
            if (string.IsNullOrWhiteSpace(stage.evidenceJson))
                continue;
            try
            {
                using var doc = JsonDocument.Parse(stage.evidenceJson);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var prop in new[] { "shipmentId", "supplyId", "externalId", "id" })
                {
                    if (TryReadNonEmptyString(root, prop, out var value))
                        return value;
                }

                if (root.TryGetProperty("shipment", out var shipment) && shipment.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in new[] { "externalId", "id" })
                    {
                        if (TryReadNonEmptyString(shipment, prop, out var value))
                            return value;
                    }
                }
            }
            catch (JsonException)
            {
            }
        }
        return null;
    }

    public static List<string> ExtractBoxIds(IReadOnlyList<IntegrationStage> stages)
    {
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && seen.Add(value.Trim()))
                ids.Add(value.Trim());
        }

        foreach (var stage in stages)
        {
            if (string.IsNullOrWhiteSpace(stage.evidenceJson))
                continue;
            try
            {
                using var doc = JsonDocument.Parse(stage.evidenceJson);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var prop in new[] { "boxIds", "trbxIds" })
                {
                    if (root.TryGetProperty(prop, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in arr.EnumerateArray())
                            Add(ReadScalarString(el));
                    }
                }

                if (root.TryGetProperty("boxes", out var boxes) && boxes.ValueKind == JsonValueKind.Array)
                {
                    foreach (var box in boxes.EnumerateArray())
                    {
                        if (box.ValueKind == JsonValueKind.String)
                        {
                            Add(box.GetString());
                        }
                        else if (box.ValueKind == JsonValueKind.Object)
                        {
                            if (TryReadNonEmptyString(box, "externalId", out var extId))
                                Add(extId);
                            else if (TryReadNonEmptyString(box, "id", out var id))
                                Add(id);
                            else if (TryReadNonEmptyString(box, "trbxId", out var trbxId))
                                Add(trbxId);
                        }
                    }
                }

                if (TryReadNonEmptyString(root, "boxId", out var singleBoxId))
                    Add(singleBoxId);
                if (TryReadNonEmptyString(root, "trbxId", out var singleTrbxId))
                    Add(singleTrbxId);
            }
            catch (JsonException)
            {
            }
        }
        return ids;
    }

    private static string? ExtractShipmentName(IntegrationCommand command, IReadOnlyList<IntegrationStage> stages)
    {
        foreach (var stage in stages)
        {
            if (string.IsNullOrWhiteSpace(stage.evidenceJson))
                continue;
            try
            {
                using var doc = JsonDocument.Parse(stage.evidenceJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (TryReadNonEmptyString(doc.RootElement, "name", out var name))
                        return name;
                    if (doc.RootElement.TryGetProperty("shipment", out var shipment)
                        && shipment.ValueKind == JsonValueKind.Object
                        && TryReadNonEmptyString(shipment, "name", out var nestedName))
                    {
                        return nestedName;
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        if (!string.IsNullOrWhiteSpace(command.inputJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(command.inputJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("data", out var data)
                    && data.ValueKind == JsonValueKind.Object
                    && TryReadNonEmptyString(data, "name", out var inputName))
                {
                    return inputName;
                }
            }
            catch (JsonException)
            {
            }
        }

        return null;
    }

    private static (int Added, int Removed) ExtractPromotionCounts(IReadOnlyList<IntegrationStage> stages)
    {
        var added = 0;
        var removed = 0;
        var explicitCounts = false;

        foreach (var stage in stages)
        {
            if (stage.state is not (IntegrationStageState.Applied or IntegrationStageState.PartiallyApplied))
                continue;

            if (!string.IsNullOrWhiteSpace(stage.evidenceJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(stage.evidenceJson);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        if (doc.RootElement.TryGetProperty("added", out var a) && a.TryGetInt32(out var ai))
                        {
                            added += ai;
                            explicitCounts = true;
                        }
                        if (doc.RootElement.TryGetProperty("removed", out var r) && r.TryGetInt32(out var ri))
                        {
                            removed += ri;
                            explicitCounts = true;
                        }
                    }
                }
                catch (JsonException)
                {
                }
            }

            if (!explicitCounts)
            {
                if (stage.stageKey.Contains("remove", StringComparison.OrdinalIgnoreCase)
                    || stage.stageKey.Contains("deactivate", StringComparison.OrdinalIgnoreCase))
                {
                    removed++;
                }
                else
                {
                    added++;
                }
            }
        }

        return (added, removed);
    }

    private static bool TryReadNonEmptyString(JsonElement obj, string name, out string? value)
    {
        value = null;
        foreach (var prop in obj.EnumerateObject())
        {
            if (!prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;
            var str = ReadScalarString(prop.Value);
            if (!string.IsNullOrWhiteSpace(str))
            {
                value = str.Trim();
                return true;
            }
        }
        return false;
    }

    private static string? ReadScalarString(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetRawText(),
        _ => null
    };

    private static JsonElement? TryParseJsonElement(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
