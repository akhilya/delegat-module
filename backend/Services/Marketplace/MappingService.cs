using Delegat.Enums;
using Delegat.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Models.Products;
using Delegat.Services;
using Delegat.Services.Marketplace.Graph;
using Delegat.Services.Marketplace.Modifiers;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Service for building export data based on field mappings
/// </summary>
public class MappingService
{
    private readonly DataContext _context;
    private readonly IServiceProvider _serviceProvider;
    private readonly GenerativeMappingService? _generativeService;

    public MappingService(
        DataContext context,
        IServiceProvider serviceProvider,
        GenerativeMappingService? generativeService = null)
    {
        _context = context;
        _serviceProvider = serviceProvider;
        _generativeService = generativeService;
    }

    /// <summary>
    /// Build export data for a product based on field mappings
    /// </summary>
    /// <param name="product">Product with loaded relations</param>
    /// <param name="categoryMapping">Category mapping with loaded fieldMappings</param>
    /// <returns>Dictionary: marketplace field key -> value</returns>
    public Task<Dictionary<string, object?>> BuildExportDataAsync(
        Product product,
        CategoryMapping categoryMapping,
        CancellationToken ct = default)
    {
        // Export is graph-only: the whole-category node graph is the single source of truth.
        return Task.FromResult(BuildExportDataFromGraph(product, categoryMapping));
    }

    /// <summary>
    /// Build export data by evaluating the whole-category visual node graph.
    /// Reuses the same source resolvers, modifier implementations, and coercion as the
    /// legacy per-field path, so an auto-migrated single-line graph produces identical output.
    /// </summary>
    /// <summary>
    /// Evaluate a category's node graph for a product, returning outputs, effective field types,
    /// and per-node values (for the editor's per-node preview).
    /// </summary>
    public GraphEvalResult EvaluateGraph(Product product, string? graphJson, int? mappingId = null)
        => EvaluateGraphAsync(product, graphJson, mappingId).GetAwaiter().GetResult();

    public async Task<GraphEvalResult> EvaluateGraphAsync(
        Product product,
        string? graphJson,
        int? mappingId = null,
        bool waitForGenerative = true,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(graphJson))
            return new GraphEvalResult();

        MappingGraph? graph;
        try
        {
            graph = JsonSerializer.Deserialize<MappingGraph>(graphJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            graph = null;
        }

        if (graph == null)
            return new GraphEvalResult();

        var modifierContext = new ModifierContext
        {
            ResolveReference = (reference) => ResolveFieldReference(product, reference)
        };

        var baseCtx = BuildGraphContext(product, modifierContext);
        GenerativeMappingService.GenerativeEnsureResult? genResult = null;

        if (_generativeService != null && mappingId.HasValue)
        {
            genResult = await _generativeService.EnsureGenerativeOutputsAsync(
                product, graph, mappingId.Value, baseCtx, waitForGenerative, ct);
        }

        var evalCtx = new GraphEvaluationContext
        {
            ResolveCategoryAttribute = baseCtx.ResolveCategoryAttribute,
            ResolveStoreAttribute = baseCtx.ResolveStoreAttribute,
            ResolveProductField = baseCtx.ResolveProductField,
            ResolveGroupName = baseCtx.ResolveGroupName,
            ResolveStoreFileUrl = baseCtx.ResolveStoreFileUrl,
            ResolveStoreFileContent = baseCtx.ResolveStoreFileContent,
            ResolveProductFileUrlByName = baseCtx.ResolveProductFileUrlByName,
            ResolveProductFileContentByName = baseCtx.ResolveProductFileContentByName,
            ResolveProductFilesByExtension = baseCtx.ResolveProductFilesByExtension,
            ResolveCategoryAttributeOptions = baseCtx.ResolveCategoryAttributeOptions,
            ResolveStoreAttributeOptions = baseCtx.ResolveStoreAttributeOptions,
            Coerce = baseCtx.Coerce,
            ModifierContext = baseCtx.ModifierContext,
            GenerativeOutputs = genResult?.Outputs,
            MappingId = mappingId,
            ProductId = product.Id,
        };

        var result = GraphEvaluator.Evaluate(graph, evalCtx);
        if (genResult != null)
            result.GenerativeStatuses = genResult.Statuses;
        return result;
    }

    public Dictionary<string, object?> BuildExportDataFromGraph(Product product, CategoryMapping categoryMapping)
    {
        var result = new Dictionary<string, object?>();
        var eval = EvaluateGraph(product, categoryMapping.graphJson, categoryMapping.Id);

        var cardKeys = new HashSet<string>(
            SchemaFields(categoryMapping.graphJson)
                .Where(f => !string.IsNullOrEmpty(f.fieldKey))
                .Select(f => f.fieldKey!),
            StringComparer.OrdinalIgnoreCase);

        foreach (var kv in eval.Outputs)
        {
            if (cardKeys.Contains(kv.Key) || IsPriceOnlyFieldKey(kv.Key, categoryMapping.graphJson))
                result[kv.Key] = kv.Value;
        }

        if (!result.ContainsKey("images"))
        {
            var imageUrls = GetProductImageUrlsAsync(product).GetAwaiter().GetResult();
            if (imageUrls.Count > 0)
                result["_imageUrls"] = imageUrls;
        }

        result["_fieldTypes"] = eval.FieldTypes;
        return result;
    }

    /// <summary>Parse the stored graph JSON, or null if empty/invalid.</summary>
    private static MappingGraph? ParseGraph(string? graphJson)
    {
        if (string.IsNullOrWhiteSpace(graphJson))
            return null;
        try
        {
            return JsonSerializer.Deserialize<MappingGraph>(graphJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The marketplace field schema declared on the graph's adapter sink.</summary>
    public static List<AdapterField> SchemaFields(string? graphJson)
    {
        var adapter = ParseGraph(graphJson)?.nodes?.FirstOrDefault(n => n.type == "adapterFields");
        return adapter?.data?.fields ?? new List<AdapterField>();
    }

    /// <summary>Price-API field schema declared on the graph's price sink.</summary>
    public static List<AdapterField> SchemaPriceFields(string? graphJson)
    {
        var price = ParseGraph(graphJson)?.nodes?.FirstOrDefault(n => n.type == "adapterPriceFields");
        return price?.data?.fields ?? new List<AdapterField>();
    }

    private static bool IsPriceOnlyFieldKey(string key, string? graphJson)
    {
        return SchemaPriceFields(graphJson)
            .Any(f => string.Equals(f.fieldKey, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Build <see cref="Models.Marketplace.DTOs.PriceInfo"/> by evaluating the price sink of the mapping graph.
    /// Returns null when the price node is absent or required price is missing.
    /// </summary>
    public PriceInfo? BuildPriceInfoFromGraph(
        Product product,
        CategoryMapping categoryMapping,
        string externalProductId,
        string? externalSku = null,
        int? stock = null)
    {
        if (SchemaPriceFields(categoryMapping.graphJson).Count == 0)
            return null;

        var eval = EvaluateGraph(product, categoryMapping.graphJson, categoryMapping.Id);
        var outputs = eval.Outputs;

        decimal? GetDecimal(string fieldKey) =>
            TryParseDecimal(outputs.GetValueOrDefault(fieldKey), out var v) ? v : null;

        int? GetInt(string fieldKey) =>
            TryParseInt(outputs.GetValueOrDefault(fieldKey), out var v) ? v : null;

        bool? GetBool(string fieldKey)
        {
            var raw = outputs.GetValueOrDefault(fieldKey);
            if (raw is bool b) return b;
            if (raw is string s)
            {
                var t = s.Trim().ToLowerInvariant();
                if (t is "true" or "1" or "да" or "yes") return true;
                if (t is "false" or "0" or "нет" or "no") return false;
            }
            return null;
        }

        string? GetString(string fieldKey)
        {
            var raw = outputs.GetValueOrDefault(fieldKey);
            return raw == null ? null : raw.ToString();
        }

        // Platform convention: price/oldPrice. Yandex Market price block uses value/discountBase.
        var price = GetDecimal("price") ?? GetDecimal("value");
        if (!price.HasValue)
            return null;

        var oldPrice = GetDecimal("oldPrice") ?? GetDecimal("discountBase");
        // Equal or empty strike-through is not a real discount — leave null so adapters omit it.
        if (oldPrice.HasValue && oldPrice.Value <= price.Value)
            oldPrice = null;

        return new PriceInfo(
            ExternalProductId: externalProductId,
            Price: price,
            OldPrice: oldPrice,
            Stock: stock,
            DiscountedPrice: GetDecimal("discountedPrice"),
            MinPrice: GetDecimal("minPrice"),
            ExternalSku: externalSku,
            Discount: GetInt("discount"),
            NetPrice: GetDecimal("netPrice"),
            AutoActionEnabled: GetString("autoActionEnabled"),
            PriceStrategyEnabled: GetString("priceStrategyEnabled"),
            MinPriceForAutoActionsEnabled: GetBool("minPriceForAutoActionsEnabled"),
            Currency: GetString("currencyId") ?? GetString("currency")
        );
    }

    private static bool TryParseDecimal(object? value, out decimal result)
    {
        switch (value)
        {
            case decimal d:
                result = d;
                return true;
            case int i:
                result = i;
                return true;
            case long l:
                result = l;
                return true;
            case double db:
                result = (decimal)db;
                return true;
        }
        return decimal.TryParse(value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryParseInt(object? value, out int result)
    {
        switch (value)
        {
            case int i:
                result = i;
                return true;
            case long l when l >= int.MinValue && l <= int.MaxValue:
                result = (int)l;
                return true;
            case decimal d when d >= int.MinValue && d <= int.MaxValue:
                result = (int)Math.Round(d);
                return true;
        }
        return int.TryParse(value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out result);
    }

    /// <summary>
    /// Project the graph to per-field import descriptors (the old FieldMapping shape) for the reverse
    /// (import) path — a C# port of the frontend graphModel.analyzeFields projections. Only IMPORTABLE
    /// lanes are returned: a direct attribute/storeAttribute binding, or a single-input modifier/reroute
    /// chain back to an attribute. Fan-in / ops / productField / static / groupName lanes are export-only
    /// and omitted (so the import path naturally skips them).
    /// </summary>
    public static List<ImportField> ProjectGraphForImport(string? graphJson)
    {
        var result = new List<ImportField>();
        var graph = ParseGraph(graphJson);
        if (graph?.nodes == null)
            return result;

        var nodesById = new Dictionary<string, GraphNode>();
        foreach (var n in graph.nodes)
            if (!string.IsNullOrEmpty(n.id))
                nodesById[n.id] = n;

        var incoming = new Dictionary<string, List<GraphEdge>>();
        foreach (var e in graph.edges ?? new List<GraphEdge>())
            if (!string.IsNullOrEmpty(e.target))
                (incoming.TryGetValue(e.target, out var l) ? l : incoming[e.target] = new()).Add(e);

        var adapter = graph.nodes.FirstOrDefault(n => n.type == "adapterFields");
        if (adapter == null)
            return result;

        foreach (var field in adapter.data?.fields ?? new List<AdapterField>())
        {
            if (string.IsNullOrEmpty(field.fieldKey))
                continue;

            // Merged duplicates: only the primary field participates in import —
            // reversing both fields into the same local source would be last-write-wins
            if (!string.IsNullOrEmpty(field.aliasOf))
                continue;

            // Direct source binding (chosen in the adapter settings).
            if (field.source?.type is string st)
            {
                if (st is "attribute" or "storeAttribute")
                    result.Add(new ImportField
                    {
                        externalFieldKey = field.fieldKey!,
                        externalFieldName = field.label,
                        sourceType = st == "attribute"
                            ? FieldMappingSourceType.CategoryAttribute
                            : FieldMappingSourceType.StoreAttribute,
                        sourceAttributeId = field.source.attributeId,
                        modifiersJson = null,
                        isRequired = field.isRequired,
                    });
                continue; // productField / groupName ⇒ export-only
            }

            // Wired lane: trace the edge feeding this field's pin back to an attribute.
            var inEdge = incoming.TryGetValue(adapter.id, out var adEdges)
                ? adEdges.FirstOrDefault(e => e.targetHandle == $"f:{field.fieldKey}")
                : null;
            if (inEdge == null)
                continue;

            var mods = new List<object>();
            var seen = new HashSet<string>();
            var cur = nodesById.GetValueOrDefault(inEdge.source);
            var ok = true;
            string? srcType = null;
            int? attrId = null;

            while (cur != null && !seen.Contains(cur.id))
            {
                seen.Add(cur.id);
                var ins = incoming.GetValueOrDefault(cur.id) ?? new List<GraphEdge>();
                if (cur.type == "modifier")
                {
                    if (ins.Count != 1) { ok = false; break; }
                    mods.Insert(0, new
                    {
                        type = cur.data?.modifierType,
                        @params = cur.data?.@params ?? new Dictionary<string, object>(),
                        enabled = true,
                    });
                    cur = nodesById.GetValueOrDefault(ins[0].source);
                }
                else if (cur.type == "reroute")
                {
                    if (ins.Count != 1) { ok = false; break; }
                    cur = nodesById.GetValueOrDefault(ins[0].source); // transparent pass-through
                }
                else if (cur.type is "attribute" or "storeAttribute")
                {
                    if (cur.data?.attributeId != null)
                    {
                        if (cur.data.importable == false)
                            ok = false;
                        else
                        {
                            srcType = cur.type;
                            attrId = cur.data.attributeId;
                        }
                    }
                    else ok = false;
                    break;
                }
                else { ok = false; break; }
            }

            if (ok && srcType != null && attrId != null)
                result.Add(new ImportField
                {
                    externalFieldKey = field.fieldKey!,
                    externalFieldName = field.label,
                    sourceType = srcType == "attribute"
                        ? FieldMappingSourceType.CategoryAttribute
                        : FieldMappingSourceType.StoreAttribute,
                    sourceAttributeId = attrId,
                    modifiersJson = mods.Count > 0 ? JsonSerializer.Serialize(mods) : null,
                    isRequired = field.isRequired,
                });
        }

        return result;
    }

    /// <summary>
    /// Build the evaluation context (source resolvers + coercion) for a concrete product.
    /// Keeps data-access concerns in this service and the evaluator pure/testable.
    /// </summary>
    public GraphEvaluationContext BuildGraphContext(Product product, ModifierContext? modifierContext = null)
    {
        // Store-file lookups are shared across all products in a bulk export ⇒ memoize per id.
        var urlCache = new Dictionary<int, object?>();
        var contentCache = new Dictionary<int, object?>();
        // Product-file lookups are per-product ⇒ caches live for this product's evaluation only.
        var pfUrlCache = new Dictionary<string, object?>();
        var pfContentCache = new Dictionary<string, object?>();
        var pfExtCache = new Dictionary<string, object?>();

        return new GraphEvaluationContext
        {
            ResolveCategoryAttribute = (id) => GetCategoryAttributeValue(product, id),
            ResolveStoreAttribute = (id) => GetStoreAttributeValue(product, id),
            ResolveProductField = (field) => GetProductFieldValue(product, field),
            ResolveGroupName = () => GetGroupNameValue(product),
            ResolveStoreFileUrl = (id) => GetStoreFileUrl(product, id, urlCache),
            ResolveStoreFileContent = (id) => GetStoreFileContent(product, id, contentCache),
            ResolveProductFileUrlByName = (name) => GetProductFileUrlByName(product, name, pfUrlCache),
            ResolveProductFileContentByName = (name) => GetProductFileContentByName(product, name, pfContentCache),
            ResolveProductFilesByExtension = (ext) => GetProductFilesByExtension(product, ext, pfExtCache),
            ResolveCategoryAttributeOptions = (id) => GetCategoryAttributeOptions(product, id),
            ResolveStoreAttributeOptions = (id) => GetStoreAttributeOptions(product, id),
            Coerce = (value, type) => CoerceToFieldType(value, type),
            ModifierContext = modifierContext
        };
    }

    /// <summary>Find a store shared-storage file by id, scoped to the product's store.</summary>
    private StoreFile? FindStoreFile(Product product, int id) =>
        id <= 0
            ? null
            : _context.storeFiles.FirstOrDefault(f => f.Id == id && f.storeId == product.storeId);

    /// <summary>Resolve a "storeFileUrl" node to a public (presigned) URL for the file.</summary>
    private object? GetStoreFileUrl(Product product, int id, Dictionary<int, object?> cache)
    {
        if (cache.TryGetValue(id, out var cached))
            return cached;

        object? url = null;
        var file = FindStoreFile(product, id);
        if (file != null)
        {
            var storageService = _serviceProvider.GetRequiredService<IStorageService>();
            url = storageService.GetPublicPresignedUrlAsync(file.filePath, 3600).GetAwaiter().GetResult();
        }

        cache[id] = url;
        return url;
    }

    /// <summary>Resolve a "storeFileContent" node to the file's text content (e.g. rich-JSON).</summary>
    private object? GetStoreFileContent(Product product, int id, Dictionary<int, object?> cache)
    {
        if (cache.TryGetValue(id, out var cached))
            return cached;

        object? content = null;
        var file = FindStoreFile(product, id);
        if (file != null)
        {
            var storageService = _serviceProvider.GetRequiredService<IStorageService>();
            using var stream = storageService.GetStreamAsync(file.filePath).GetAwaiter().GetResult();
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                content = reader.ReadToEnd();
            }
        }

        cache[id] = content;
        return content;
    }

    private static string ProductFileName(Delegat.Models.File f) =>
        f.displayName ?? System.IO.Path.GetFileName(f.filePath);

    private static string ProductFileExt(Delegat.Models.File f) =>
        System.IO.Path.GetExtension(f.displayName ?? f.filePath).TrimStart('.').ToLowerInvariant();

    /// <summary>First product file whose name (display name or filename) matches, case-insensitive.</summary>
    private static Delegat.Models.File? FindProductFileByName(Product product, string name)
    {
        if (string.IsNullOrEmpty(name) || product.files == null)
            return null;
        return product.files.FirstOrDefault(f =>
            string.Equals(ProductFileName(f), name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(f.displayName, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>"productFileByName" → public (presigned) URL of the matching product file.</summary>
    private object? GetProductFileUrlByName(Product product, string? name, Dictionary<string, object?> cache)
    {
        var key = name ?? "";
        if (cache.TryGetValue(key, out var cached))
            return cached;

        object? url = null;
        var file = FindProductFileByName(product, key);
        if (file != null)
        {
            var storageService = _serviceProvider.GetRequiredService<IStorageService>();
            url = storageService.GetPublicPresignedUrlAsync(file.filePath, 3600).GetAwaiter().GetResult();
        }

        cache[key] = url;
        return url;
    }

    /// <summary>"productFileContentByName" → text content of the matching product file.</summary>
    private object? GetProductFileContentByName(Product product, string? name, Dictionary<string, object?> cache)
    {
        var key = name ?? "";
        if (cache.TryGetValue(key, out var cached))
            return cached;

        object? content = null;
        var file = FindProductFileByName(product, key);
        if (file != null)
        {
            var storageService = _serviceProvider.GetRequiredService<IStorageService>();
            using var stream = storageService.GetStreamAsync(file.filePath).GetAwaiter().GetResult();
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                content = reader.ReadToEnd();
            }
        }

        cache[key] = content;
        return content;
    }

    /// <summary>"productFilesByExtension" → URLs of every product file with the given extension.</summary>
    private object? GetProductFilesByExtension(Product product, string? ext, Dictionary<string, object?> cache)
    {
        var key = (ext ?? "").TrimStart('.').ToLowerInvariant();
        if (cache.TryGetValue(key, out var cached))
            return cached;

        var urls = new List<string>();
        if (!string.IsNullOrEmpty(key) && product.files != null)
        {
            var storageService = _serviceProvider.GetRequiredService<IStorageService>();
            foreach (var f in product.files)
            {
                if (!string.Equals(ProductFileExt(f), key, StringComparison.OrdinalIgnoreCase))
                    continue;
                var url = storageService.GetPublicPresignedUrlAsync(f.filePath, 3600).GetAwaiter().GetResult();
                if (!string.IsNullOrEmpty(url))
                    urls.Add(url);
            }
        }

        cache[key] = urls;
        return urls;
    }

    /// <summary>
    /// Resolve a template reference like "Name", "attr:5", "storeAttr:3" to a product value.
    /// </summary>
    public object? ResolveFieldReference(Product product, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;

        // attr:ID — category attribute
        if (reference.StartsWith("attr:", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(reference[5..], out var attrId))
                return GetCategoryAttributeValue(product, attrId);
            return null;
        }

        // storeAttr:ID — store attribute
        if (reference.StartsWith("storeAttr:", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(reference[10..], out var storeAttrId))
                return GetStoreAttributeValue(product, storeAttrId);
            return null;
        }

        // Plain field name — product field (case-insensitive)
        return GetProductFieldValue(product, reference);
    }

    /// <summary>
    /// Get value of a standard product field
    /// </summary>
    private string GetStoreBaseCurrency(int storeId) =>
        _context.stores.AsNoTracking()
            .Where(s => s.Id == storeId)
            .Select(s => s.baseCurrency)
            .FirstOrDefault()
        ?? throw new InvalidOperationException($"Store {storeId} has no base currency configured");

    private object? GetProductFieldValue(Product product, string? fieldName)
    {
        if (string.IsNullOrEmpty(fieldName))
            return null;

        return fieldName.ToLowerInvariant() switch
        {
            "sku" => product.sku,
            "barcode" => product.barcode,
            "name" => product.name,
            "description" => product.description,
            "price" => product.price,
            "oldprice" => product.oldPrice,
            "minprice" => product.minPrice,
            "netprice" => product.netPrice,
            "discountedprice" => product.discountedPrice,
            "discount" => product.discount,
            "currency" or "currencyid" => product.currency ?? GetStoreBaseCurrency(product.storeId),
            "storagequantity" or "stock" or "quantity" => product.storageQuantity,
            "categoryid" => product.categoryId,
            "categoryname" => product.category?.name,
            "groupname" => GetGroupNameValue(product),
            "images" => GetProductImageUrlsAsync(product).GetAwaiter().GetResult(),
            _ => null
        };
    }

    /// <summary>
    /// Get the list of product image URLs in the correct order
    /// </summary>
    private async Task<List<string>> GetProductImageUrlsAsync(Product product)
    {
        var urls = new List<string>();

        if (product.images == null || !product.images.Any())
            return urls;

        // Get images ordered by index
        var orderedImages = product.images
            .OrderBy(i => i.index)
            .ToList();

        foreach (var imageRef in orderedImages)
        {
            // Use the file navigation property directly if available
            var file = imageRef.file ?? product.files?.FirstOrDefault(f => f.Id == imageRef.fileId);
            if (file != null)
            {
                var storageService = _serviceProvider.GetRequiredService<IStorageService>();
                var url = await storageService.GetPublicPresignedUrlAsync(file.filePath, 3600);
                if (!string.IsNullOrEmpty(url))
                {
                    urls.Add(url);
                }
            }
        }

        return urls;
    }

    /// <summary>
    /// Get category attribute value
    /// </summary>
    private object? GetCategoryAttributeValue(Product product, int? attributeId)
    {
        if (!attributeId.HasValue)
            return null;

        var attributeValue = product.attributeValues?
            .FirstOrDefault(av => av.attributeId == attributeId.Value);

        return attributeValue?.value;
    }

    /// <summary>
    /// Get store attribute value
    /// </summary>
    private object? GetStoreAttributeValue(Product product, int? attributeId)
    {
        if (!attributeId.HasValue)
            return null;

        var attributeValue = product.storeAttributeValues?
            .FirstOrDefault(sav => sav.storeAttributeId == attributeId.Value);

        return attributeValue?.value;
    }

    /// <summary>
    /// Get the product group name.
    /// Used for grouping fields on marketplaces (e.g. Ozon field 9048).
    /// </summary>
    private object? GetGroupNameValue(Product product)
    {
        // Get the first product group (a product can belong to multiple groups)
        var groupMember = product.groupMemberships?.FirstOrDefault();
        if (groupMember?.group != null)
        {
            return groupMember.group.name;
        }

        return null;
    }

    /// <summary>
    /// Coerce a value to match the expected field type.
    /// </summary>
    private static object? CoerceToFieldType(object? value, string? fieldType)
    {
        if (value == null || string.IsNullOrEmpty(fieldType))
            return value;

        return fieldType.ToLowerInvariant() switch
        {
            "string" or "text" => value is System.Collections.IEnumerable && value is not string
                ? value
                : value.ToString(),
            "number" => ToDecimalOrPassthrough(value),
            "integer" => ToLongOrPassthrough(value),
            "boolean" => ToBoolOrPassthrough(value),
            _ => value // select, multiselect, array — no coercion
        };
    }

    private static object? ToDecimalOrPassthrough(object? value)
    {
        if (value is decimal) return value;
        if (decimal.TryParse(value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            return d;
        return value;
    }

    private static object? ToLongOrPassthrough(object? value)
    {
        if (value is long) return value;
        if (value is int i) return (long)i;
        if (long.TryParse(value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var l))
            return l;
        // Try parsing as decimal first, then truncate (e.g. "123.45" → 123)
        if (decimal.TryParse(value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            return (long)Math.Truncate(d);
        return value;
    }

    private static object? ToBoolOrPassthrough(object? value)
    {
        if (value is bool) return value;
        var str = value?.ToString()?.Trim().ToLowerInvariant();
        return str switch
        {
            "true" or "1" or "yes" or "да" => true,
            "false" or "0" or "no" or "нет" => false,
            _ => value
        };
    }

    /// <summary>
    /// Get the effective field type — checks for a forceType modifier override, falls back to defaultType.
    /// </summary>
    private static string? GetEffectiveFieldType(string? modifiersJson, string? defaultType)
    {
        if (string.IsNullOrWhiteSpace(modifiersJson))
            return defaultType;

        try
        {
            using var doc = JsonDocument.Parse(modifiersJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return defaultType;

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                // Skip disabled modifiers
                if (element.TryGetProperty("enabled", out var enabledProp)
                    && enabledProp.ValueKind == JsonValueKind.False)
                    continue;

                if (element.TryGetProperty("type", out var typeProp) &&
                    typeProp.GetString()?.Equals("forceType", StringComparison.OrdinalIgnoreCase) == true)
                {
                    if (element.TryGetProperty("params", out var paramsProp) &&
                        paramsProp.TryGetProperty("type", out var forceTypeProp))
                    {
                        var forceType = forceTypeProp.GetString();
                        if (!string.IsNullOrEmpty(forceType))
                            return forceType;
                    }
                }
            }
        }
        catch (JsonException) { }

        return defaultType;
    }

    /// <summary>
    /// Resolve options for a category attribute by id (graph source nodes).
    /// </summary>
    private List<Modifiers.SourceOption>? GetCategoryAttributeOptions(Product product, int attributeId)
    {
        var optionsJson = product.attributeValues?
            .FirstOrDefault(av => av.attributeId == attributeId)
            ?.attribute?.options;
        return ParseOptions(optionsJson);
    }

    /// <summary>
    /// Resolve options for a store attribute by id (graph source nodes).
    /// </summary>
    private List<Modifiers.SourceOption>? GetStoreAttributeOptions(Product product, int attributeId)
    {
        var optionsJson = product.storeAttributeValues?
            .FirstOrDefault(sav => sav.storeAttributeId == attributeId)
            ?.storeAttribute?.options;
        return ParseOptions(optionsJson);
    }

    /// <summary>
    /// Parse an attribute options JSON (or comma-separated "value:label") into SourceOptions.
    /// </summary>
    private static List<Modifiers.SourceOption>? ParseOptions(string? optionsJson)
    {
        if (string.IsNullOrEmpty(optionsJson))
            return null;

        try
        {
            return JsonSerializer.Deserialize<List<Modifiers.SourceOption>>(optionsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            // Fallback: comma-separated "value:label" format
            try
            {
                return optionsJson.Split(',')
                    .Select(o => o.Trim())
                    .Where(o => !string.IsNullOrEmpty(o))
                    .Select(o =>
                    {
                        var parts = o.Split(':', 2);
                        return new Modifiers.SourceOption
                        {
                            value = parts[0].Trim(),
                            label = parts.Length > 1 ? parts[1].Trim() : parts[0].Trim()
                        };
                    })
                    .ToList();
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// Apply a transformation to a value.
    /// Format: "multiply:1000", "append:_suffix", "prepend:prefix_", "replace:old:new", "uppercase", "lowercase"
    /// </summary>
    private object? ApplyTransform(object value, string transformExpression)
    {
        var parts = transformExpression.Split(':', 2);
        var operation = parts[0].ToLowerInvariant();
        var argument = parts.Length > 1 ? parts[1] : null;

        try
        {
            return operation switch
            {
                "multiply" when decimal.TryParse(argument, out var factor) =>
                    Convert.ToDecimal(value) * factor,

                "divide" when decimal.TryParse(argument, out var divisor) && divisor != 0 =>
                    Convert.ToDecimal(value) / divisor,

                "add" when decimal.TryParse(argument, out var addend) =>
                    Convert.ToDecimal(value) + addend,

                "append" => value.ToString() + argument,

                "prepend" => argument + value.ToString(),

                "replace" when argument != null =>
                    ApplyReplace(value.ToString() ?? "", argument),

                "uppercase" => value.ToString()?.ToUpperInvariant(),

                "lowercase" => value.ToString()?.ToLowerInvariant(),

                "trim" => value.ToString()?.Trim(),

                "round" when int.TryParse(argument, out var decimals) =>
                    Math.Round(Convert.ToDecimal(value), decimals),

                "floor" => Math.Floor(Convert.ToDecimal(value)),

                "ceiling" => Math.Ceiling(Convert.ToDecimal(value)),

                "truncate" when int.TryParse(argument, out var maxLength) =>
                    TruncateString(value.ToString(), maxLength),

                _ => value
            };
        }
        catch
        {
            // If the transformation failed, return the original value
            return value;
        }
    }

    /// <summary>
    /// Apply a replace transformation.
    /// Argument format: "old:new"
    /// </summary>
    private string ApplyReplace(string value, string argument)
    {
        var replaceParts = argument.Split(':', 2);
        if (replaceParts.Length == 2)
        {
            return value.Replace(replaceParts[0], replaceParts[1]);
        }
        return value;
    }

    /// <summary>
    /// Truncate a string to the specified length
    /// </summary>
    private string? TruncateString(string? value, int maxLength)
    {
        if (value == null || value.Length <= maxLength)
            return value;
        return value.Substring(0, maxLength);
    }

    public static List<ProductFieldInfo> GetBaseProductFields() =>
    [
        new("sku", "SKU / Артикул", "string"),
        new("barcode", "Штрихкод (EAN)", "string"),
        new("name", "Название", "string"),
        new("description", "Описание", "string"),
        new("categoryName", "Название категории", "string"),
        new("groupName", "Название группы товара", "string"),
        new("images", "Изображения", "array"),
    ];

    public static List<ProductFieldInfo> GetStockProductFields() =>
    [
        new("storageQuantity", "Количество на складе", "number"),
    ];

    public static List<ProductFieldInfo> GetPriceProductFields(bool extendedPriceParameters = false)
    {
        var fields = new List<ProductFieldInfo>
        {
            new("price", "Цена", "number"),
        };

        if (extendedPriceParameters)
        {
            fields.AddRange(
            [
                new ProductFieldInfo("oldPrice", "Старая цена", "number"),
                new ProductFieldInfo("minPrice", "Минимальная цена", "number"),
                new ProductFieldInfo("netPrice", "Себестоимость", "number"),
                new ProductFieldInfo("discountedPrice", "Цена со скидкой", "number"),
                new ProductFieldInfo("discount", "Скидка %", "number"),
                new ProductFieldInfo("currency", "Валюта", "string"),
            ]);
        }

        return fields;
    }

    /// <summary>All mappable product fields (base + stock + price).</summary>
    public static List<ProductFieldInfo> GetAvailableProductFields(bool extendedPriceParameters = false)
        => GetBaseProductFields()
            .Concat(GetStockProductFields())
            .Concat(GetPriceProductFields(extendedPriceParameters))
            .ToList();
}

/// <summary>
/// Information about an available product field for mapping
/// </summary>
public record ProductFieldInfo(
    string Key,
    string DisplayName,
    string Type
);

/// <summary>
/// One importable field projected from the graph (the per-field shape the reverse/import path needs).
/// </summary>
public class ImportField
{
    public required string externalFieldKey { get; set; }
    public string? externalFieldName { get; set; }
    public FieldMappingSourceType sourceType { get; set; }
    public int? sourceAttributeId { get; set; }
    public string? modifiersJson { get; set; }
    public bool isRequired { get; set; }
}
