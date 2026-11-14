using Delegat.Models;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services.Marketplace.Modifiers;

namespace Delegat.Services.Marketplace.Graph;

/// <summary>Refresh automatic conversion parameters from authoritative attribute definitions and graph wiring.</summary>
public static class GraphUnitMetadata
{
    public static void Reconcile(MappingGraph graph, Func<string, int, string?> attributeUnit)
    {
        if (graph.nodes == null || graph.nodes.Count == 0) return;
        var nodes = graph.nodes.GroupBy(n => n.id).ToDictionary(g => g.Key, g => g.Last());
        var incoming = (graph.edges ?? []).ToLookup(e => e.target);
        var outgoing = (graph.edges ?? []).ToLookup(e => e.source);
        var resolved = new HashSet<string>();
        var visiting = new HashSet<string>();

        bool IsMain(GraphEdge edge) => string.IsNullOrEmpty(edge.targetHandle) || edge.targetHandle == "in";
        bool IsTransparent(GraphNode node) => node.type == "reroute" ||
            (node.type == "modifier" && node.data?.modifierType is not
                ("unitConversion" or "multiply" or "divide" or "add" or "subtract"));

        string? SourceUnit(string id, HashSet<string> seen)
        {
            if (!seen.Add(id) || !nodes.TryGetValue(id, out var node)) return null;
            if (node.type is "attribute" or "storeAttribute")
                return AttributeUnits.NormalizeCode(attributeUnit(node.type, node.data?.attributeId ?? 0));
            if (node.type == "modifier" && node.data?.modifierType == "unitConversion")
            {
                Resolve(node);
                return AttributeUnits.NormalizeCode(ModifierHelpers.GetString(node.data.@params ?? new(), "toUnit", null));
            }
            var inputs = incoming[id].Where(IsMain).ToList();
            return IsTransparent(node) && inputs.Count == 1 ? SourceUnit(inputs[0].source, seen) : null;
        }

        HashSet<string> TargetUnits(string id, HashSet<string> seen)
        {
            var units = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!seen.Add(id)) return units;
            foreach (var edge in outgoing[id])
            {
                if (!nodes.TryGetValue(edge.target, out var target)) continue;
                if (edge.targetHandle?.StartsWith("f:") == true)
                {
                    var field = target.data?.fields?.FirstOrDefault(f => $"f:{f.fieldKey}" == edge.targetHandle);
                    var unit = field == null ? null : AttributeFieldConstraints.ResolveUnitCode(new ExternalField(
                        field.fieldKey ?? "", field.label ?? "", field.isRequired, field.outputType ?? "",
                        Description: field.description, Unit: field.unit, UnitCode: field.unitCode));
                    if (unit != null) units.Add(unit);
                    continue;
                }
                // Operand and auxiliary pins consume numbers, not a value in the destination's unit.
                if (!IsMain(edge)) continue;
                if (target.type == "modifier" && target.data?.modifierType == "unitConversion")
                {
                    var parameters = target.data.@params ?? new();
                    // Automatic conversions derive their input from upstream. Only a manual
                    // conversion establishes an independent constraint on the intermediate unit.
                    if (!ModifierHelpers.GetBool(parameters, "autoDetect", true))
                    {
                        var unit = AttributeUnits.NormalizeCode(ModifierHelpers.GetString(parameters, "fromUnit", null));
                        if (unit != null) units.Add(unit);
                    }
                }
                else if (IsTransparent(target))
                    units.UnionWith(TargetUnits(target.id, new HashSet<string>(seen)));
            }
            return units;
        }

        void Resolve(GraphNode node)
        {
            if (resolved.Contains(node.id) || !visiting.Add(node.id)) return;
            var parameters = node.data!.@params ??= new();
            if (ModifierHelpers.GetBool(parameters, "autoDetect", true))
            {
                var inputs = incoming[node.id].Where(IsMain).ToList();
                parameters["fromUnit"] = inputs.Count == 1 ? SourceUnit(inputs[0].source, new()) ?? "" : "";
                var units = TargetUnits(node.id, new());
                parameters["toUnit"] = units.Count == 1 ? units.Single() : "";
            }
            visiting.Remove(node.id);
            resolved.Add(node.id);
        }

        foreach (var node in graph.nodes)
            if (node.type == "modifier" && node.data?.modifierType == "unitConversion") Resolve(node);
    }
}
