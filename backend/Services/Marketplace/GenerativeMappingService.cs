using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Delegat.Models.Marketplace;
using Delegat.Models.Products;
using Delegat.Services.Ai;
using Delegat.Services.Marketplace.Graph;
using Delegat.Services.Marketplace.Modifiers;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services.Marketplace;

public class GenerativeMappingService
{
    private static readonly Regex PlaceholderRegex = new(@"\{\{(.+?)\}\}", RegexOptions.Compiled);

    private readonly DataContext _context;
    private readonly AiModelConfigService _modelService;
    private readonly AiApiClient _apiClient;
    private readonly ILogger<GenerativeMappingService> _logger;

    public GenerativeMappingService(
        DataContext context,
        AiModelConfigService modelService,
        AiApiClient apiClient,
        ILogger<GenerativeMappingService> logger)
    {
        _context = context;
        _modelService = modelService;
        _apiClient = apiClient;
        _logger = logger;
    }

    public sealed class GenerativeEnsureResult
    {
        public Dictionary<string, string> Outputs { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, GenerativeNodeStatus> Statuses { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// Resolve or generate outputs for all llmGenerate nodes before graph evaluation.
    /// </summary>
    public async Task<GenerativeEnsureResult> EnsureGenerativeOutputsAsync(
        Product product,
        MappingGraph graph,
        int mappingId,
        GraphEvaluationContext evalCtx,
        bool waitForCompletion = true,
        CancellationToken ct = default)
    {
        var result = new GenerativeEnsureResult();
        var llmNodes = graph.nodes?
            .Where(n => n.type == "modifier"
                        && string.Equals(n.data?.modifierType, "llmGenerate", StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [];

        if (llmNodes.Count == 0)
            return result;

        // Pass-through eval to collect upstream input values at each llmGenerate node.
        var passCtx = new GraphEvaluationContext
        {
            ResolveCategoryAttribute = evalCtx.ResolveCategoryAttribute,
            ResolveStoreAttribute = evalCtx.ResolveStoreAttribute,
            ResolveProductField = evalCtx.ResolveProductField,
            ResolveGroupName = evalCtx.ResolveGroupName,
            ResolveStoreFileUrl = evalCtx.ResolveStoreFileUrl,
            ResolveStoreFileContent = evalCtx.ResolveStoreFileContent,
            ResolveProductFileUrlByName = evalCtx.ResolveProductFileUrlByName,
            ResolveProductFileContentByName = evalCtx.ResolveProductFileContentByName,
            ResolveProductFilesByExtension = evalCtx.ResolveProductFilesByExtension,
            ResolveCategoryAttributeOptions = evalCtx.ResolveCategoryAttributeOptions,
            ResolveStoreAttributeOptions = evalCtx.ResolveStoreAttributeOptions,
            Coerce = evalCtx.Coerce,
            ModifierContext = evalCtx.ModifierContext,
            MappingId = mappingId,
            ProductId = product.Id,
            GenerativePassThrough = true,
        };

        var passEval = GraphEvaluator.Evaluate(graph, passCtx);
        var modifierCtx = evalCtx.ModifierContext;

        foreach (var node in llmNodes)
        {
            var nodeId = node.id;
            var p = node.data?.@params ?? new Dictionary<string, object>();
            var promptTemplate = GetString(p, "prompt") ?? "";
            var modelConfigId = GetInt(p, "modelId", 0);
            var seed = p.ContainsKey("seed") ? GetNullableInt(p, "seed") : null;
            var cacheEnabled = !p.ContainsKey("cacheEnabled") || GetBool(p, "cacheEnabled", true);
            var perProduct = GetBool(p, "perProduct", false);

            var inputValue = passEval.NodeValues.GetValueOrDefault(nodeId);
            passEval.FieldPinValues.TryGetValue(nodeId, out var fieldPinValues);
            var resolvedPrompt = ResolvePrompt(promptTemplate, inputValue, modifierCtx, fieldPinValues);
            var promptHash = Sha256(promptTemplate);
            var inputHash = Sha256(resolvedPrompt);
            var cacheProductId = perProduct ? product.Id : (int?)null;

            if (cacheEnabled)
            {
                var cached = await FindCacheEntryAsync(
                    mappingId, nodeId, cacheProductId, promptHash, seed, modelConfigId, inputHash, ct);

                if (cached?.status == "ready" && !string.IsNullOrEmpty(cached.output))
                {
                    result.Outputs[nodeId] = cached.output;
                    result.Statuses[nodeId] = new GenerativeNodeStatus
                    {
                        Status = "ready",
                        Output = cached.output,
                        OperationId = cached.operationId,
                    };
                    continue;
                }

                if (cached?.status == "pending" && !waitForCompletion)
                {
                    result.Statuses[nodeId] = new GenerativeNodeStatus
                    {
                        Status = "pending",
                        OperationId = cached.operationId,
                    };
                    continue;
                }
            }

            if (!waitForCompletion)
            {
                result.Statuses[nodeId] = new GenerativeNodeStatus { Status = "pending" };
                continue;
            }

            try
            {
                var model = await _modelService.ResolveEnabledModelAsync(
                    modelConfigId > 0 ? modelConfigId : null, ct);

                if (model == null)
                    throw new InvalidOperationException("No connected or enabled AI models");

                var systemTemplate = GetString(p, "systemPrompt");
                var resolvedSystem = string.IsNullOrWhiteSpace(systemTemplate)
                    ? null
                    : ResolvePrompt(systemTemplate, inputValue, modifierCtx, fieldPinValues);
                var output = await GenerateTextAsync(model, resolvedPrompt, resolvedSystem, seed, ct);

                if (cacheEnabled)
                {
                    await UpsertCacheAsync(
                        mappingId, nodeId, cacheProductId, promptHash, seed, modelConfigId, inputHash,
                        output, "ready", null, ct);
                }

                result.Outputs[nodeId] = output;
                result.Statuses[nodeId] = new GenerativeNodeStatus
                {
                    Status = "ready",
                    Output = output,
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Generative mapping failed for node {NodeId} mapping {MappingId}",
                    nodeId, mappingId);

                if (cacheEnabled)
                {
                    await UpsertCacheAsync(
                        mappingId, nodeId, cacheProductId, promptHash, seed, modelConfigId, inputHash,
                        null, "failed", ex.Message, ct);
                }

                result.Statuses[nodeId] = new GenerativeNodeStatus
                {
                    Status = "failed",
                    ErrorMessage = ex.Message,
                };
            }
        }

        return result;
    }

    private async Task<string> GenerateTextAsync(
        Models.Ai.AiModelConfig model,
        string prompt,
        string? systemPrompt,
        int? seed,
        CancellationToken ct)
    {
        var decrypted = _modelService.DecryptForUse(model);
        var request = new AiRequest
        {
            max_tokens = Math.Min(decrypted.maxTokens, 4096),
            system = systemPrompt
                   ?? "You generate concise marketplace field values. Respond with plain text only — no markdown, no quotes, no preamble.",
            messages =
            [
                new AiMessage { role = "user", content = prompt }
            ],
        };

        if (seed.HasValue)
            request.user = $"seed-{seed.Value}";

        var response = await _apiClient.SendAsync(decrypted, request, ct);
        var text = response.content?
            .FirstOrDefault(b => b.type == "text" && !string.IsNullOrWhiteSpace(b.text))
            ?.text?.Trim();

        return string.IsNullOrEmpty(text) ? "" : text;
    }

    private static string ResolvePrompt(
        string template,
        object? inputValue,
        ModifierContext? ctx,
        IReadOnlyDictionary<string, object?>? fieldPinValues = null)
    {
        if (string.IsNullOrEmpty(template))
            return inputValue?.ToString() ?? "";

        return PlaceholderRegex.Replace(template, match =>
        {
            var key = match.Groups[1].Value.Trim();
            if (key.Equals("value", StringComparison.OrdinalIgnoreCase))
                return inputValue?.ToString() ?? "";

            if (key.StartsWith("pin:", StringComparison.OrdinalIgnoreCase))
            {
                var pinId = key["pin:".Length..];
                return fieldPinValues?.GetValueOrDefault(pinId)?.ToString() ?? "";
            }

            var resolved = ctx?.ResolveReference?.Invoke(key);
            return resolved?.ToString() ?? "";
        });
    }

    private async Task<MappingGenerativeCache?> FindCacheEntryAsync(
        int mappingId, string nodeId, int? productId,
        string promptHash, int? seed, int modelConfigId, string inputHash,
        CancellationToken ct)
    {
        return await _context.mappingGenerativeCaches.AsNoTracking()
            .Where(c => c.mappingId == mappingId
                        && c.nodeId == nodeId
                        && c.productId == productId
                        && c.promptHash == promptHash
                        && c.seed == seed
                        && c.modelConfigId == modelConfigId
                        && c.inputHash == inputHash)
            .OrderByDescending(c => c.createdAt)
            .FirstOrDefaultAsync(ct);
    }

    private async Task UpsertCacheAsync(
        int mappingId, string nodeId, int? productId,
        string promptHash, int? seed, int modelConfigId, string inputHash,
        string? output, string status, string? errorMessage,
        CancellationToken ct)
    {
        var existing = await _context.mappingGenerativeCaches
            .Where(c => c.mappingId == mappingId
                        && c.nodeId == nodeId
                        && c.productId == productId
                        && c.promptHash == promptHash
                        && c.seed == seed
                        && c.modelConfigId == modelConfigId
                        && c.inputHash == inputHash)
            .OrderByDescending(c => c.createdAt)
            .FirstOrDefaultAsync(ct);

        if (existing != null)
        {
            existing.output = output;
            existing.status = status;
            existing.errorMessage = errorMessage;
            existing.completedAt = DateTime.UtcNow;
        }
        else
        {
            _context.mappingGenerativeCaches.Add(new MappingGenerativeCache
            {
                mappingId = mappingId,
                nodeId = nodeId,
                productId = productId,
                promptHash = promptHash,
                seed = seed,
                modelConfigId = modelConfigId,
                inputHash = inputHash,
                output = output,
                status = status,
                errorMessage = errorMessage,
                completedAt = status is "ready" or "failed" ? DateTime.UtcNow : null,
            });
        }

        await _context.SaveChangesAsync(ct);
    }

    private static string Sha256(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? ""));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string? GetString(Dictionary<string, object> p, string key)
    {
        if (!p.TryGetValue(key, out var val)) return null;
        if (val is JsonElement je)
            return je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString();
        return val?.ToString();
    }

    private static int GetInt(Dictionary<string, object> p, string key, int def)
    {
        if (!p.TryGetValue(key, out var val)) return def;
        if (val is JsonElement je && je.TryGetInt32(out var i)) return i;
        if (val is int i2) return i2;
        return int.TryParse(val?.ToString(), out var parsed) ? parsed : def;
    }

    private static int? GetNullableInt(Dictionary<string, object> p, string key)
    {
        if (!p.TryGetValue(key, out var val) || val == null) return null;
        if (val is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Null) return null;
            if (je.TryGetInt32(out var i)) return i;
        }
        if (val is int i2) return i2;
        return int.TryParse(val.ToString(), out var parsed) ? parsed : null;
    }

    private static bool GetBool(Dictionary<string, object> p, string key, bool def)
    {
        if (!p.TryGetValue(key, out var val)) return def;
        if (val is bool b) return b;
        if (val is JsonElement je && je.ValueKind == JsonValueKind.True) return true;
        if (val is JsonElement je2 && je2.ValueKind == JsonValueKind.False) return false;
        var s = val?.ToString()?.Trim().ToLowerInvariant();
        return s switch
        {
            "true" or "1" or "yes" or "\u0434\u0430" => true,
            "false" or "0" or "no" or "\u043d\u0435\u0442" => false,
            _ => def,
        };
    }
}