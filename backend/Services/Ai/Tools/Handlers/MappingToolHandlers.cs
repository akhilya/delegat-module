using Microsoft.EntityFrameworkCore;
using Delegat.Enums;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services.Marketplace;
using Delegat.Services.Marketplace.Graph;
using Delegat.Services.Ai.Tools;

namespace Delegat.Services.Ai.Tools.Handlers;

public class MappingToolHandlers : ToolHandlerBase
{
    private readonly DataContext _context;
    private readonly AiOperationService _operationService;
    private readonly GraphMutationService _graphMutation;
    private readonly MappingIntelligenceService _intelligence;
    private readonly IMarketplaceAdapterRegistry _adapterRegistry;
    private readonly ILogger<MappingToolHandlers> _logger;

    public MappingToolHandlers(
        DataContext context,
        AiOperationService operationService,
        GraphMutationService graphMutation,
        MappingIntelligenceService intelligence,
        IMarketplaceAdapterRegistry adapterRegistry,
        ILogger<MappingToolHandlers> logger)
    {
        _context = context;
        _operationService = operationService;
        _graphMutation = graphMutation;
        _intelligence = intelligence;
        _adapterRegistry = adapterRegistry;
        _logger = logger;
    }

    internal async Task<(object, string)> ListMappingsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ConnectionIdInput>(json);
        var q = _context.categoryMappings.AsNoTracking()
            .Where(m => m.connection.storeId == storeId);
        if (p.ConnectionId.HasValue) q = q.Where(m => m.connectionId == p.ConnectionId);

        var mappings = await q
            .Select(m => new
            {
                m.Id, m.name, m.localCategoryId, m.externalCategoryId, m.externalCategoryName,
                m.connectionId, connectionName = m.connection.name
            })
            .ToListAsync(ct);
        return (new { mappings, count = mappings.Count }, $"{mappings.Count} mappings");
    }

    internal async Task<(object, string)> GetMappingAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<MappingQueryInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Mapping not found" }, "Not found");

        var coverageReport = await _graphMutation.GetCoverageReportAsync(p.Id, ct);
        var fields = await _graphMutation.GetFieldStatesAsync(p.Id, ct);
        var schemaContext = await _intelligence.GetSchemaContextAsync(mapping, ct);
        var productSample = await _intelligence.GetProductSampleAsync(
            mapping.localCategoryId, p.ProductId, ct);
        var adapterFields = await GetAdapterFieldDefsAsync(mapping, ct);
        var alignmentHints = await _intelligence.BuildAlignmentHintsAsync(
            mapping, adapterFields, p.ProductId, ct);
        var card = coverageReport.Card;
        var price = coverageReport.Price;
        return (new
        {
            mapping.Id, mapping.name, mapping.localCategoryId, mapping.externalCategoryId,
            mapping.externalCategoryName,
            coverage = new
            {
                card = new
                {
                    total = card.Total,
                    connected = card.Connected,
                    requiredTotal = card.RequiredTotal,
                    requiredConnected = card.RequiredConnected,
                },
                price = price == null ? null : new
                {
                    total = price.Total,
                    connected = price.Connected,
                    requiredTotal = price.RequiredTotal,
                    requiredConnected = price.RequiredConnected,
                },
            },
            fields,
            schemaContext,
            productSample,
            optionAlignmentHints = alignmentHints,
        }, $"Mapping {mapping.name ?? mapping.externalCategoryName}: card {card.Connected}/{card.Total}" +
           (price != null ? $", prices {price.Connected}/{price.Total}" : "") +
           $" (required card {card.RequiredConnected}/{card.RequiredTotal})");
    }

    internal async Task<(object, string)> CreateMappingAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<CreateMappingInput>(json);
        var conn = await _context.marketplaceConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == p.ConnectionId && c.storeId == storeId, ct);
        if (conn == null) return (new { error = "Connection not found" }, "Not found");

        var adapter = _adapterRegistry.GetByKey(conn.adapterKey);
        if (adapter == null) return (new { error = $"Unknown adapter: {conn.adapterKey}" }, "Bad request");

        var eligibility = await ExternalCategoryEligibility.EnsureMappableAsync(
            adapter, conn, p.ExternalCategoryId, ct);
        if (!eligibility.IsSuccess)
            return (new { error = eligibility.ErrorMessage }, "Category not mappable");

        var mapping = new CategoryMapping
        {
            connectionId = p.ConnectionId,
            localCategoryId = p.LocalCategoryId,
            externalCategoryId = p.ExternalCategoryId,
            externalCategoryName = p.ExternalCategoryName ?? eligibility.Category?.Name,
            name = p.Name
        };
        _context.categoryMappings.Add(mapping);
        await _context.SaveChangesAsync(ct);

        return (new { id = mapping.Id }, $"Mapping created (id={mapping.Id}). To fill it, call get_mapping and connect fields via connect_field / set_field_static_value.");
    }

    internal async Task<(object, string)> UpdateMappingNameAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<UpdateNameInput>(json);
        var mapping = await _context.categoryMappings
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Not found" }, "Not found");
        mapping.name = p.Name;
        await _context.SaveChangesAsync(ct);
        return (new { success = true }, $"Mapping name updated: \"{p.Name}\"");
    }

    internal async Task<(object, string)> DeleteMappingAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var mapping = await _context.categoryMappings
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Not found" }, "Not found");
        _context.categoryMappings.Remove(mapping);
        await _context.SaveChangesAsync(ct);
        return (new { success = true }, $"Mapping deleted");
    }

    internal async Task<(object, string)> DuplicateMappingAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Not found" }, "Not found");
        var copy = new CategoryMapping
        {
            connectionId = mapping.connectionId,
            localCategoryId = mapping.localCategoryId,
            externalCategoryId = mapping.externalCategoryId,
            externalCategoryName = mapping.externalCategoryName,
            name = (mapping.name ?? mapping.externalCategoryName) + " (copy)",
            graphJson = mapping.graphJson
        };
        _context.categoryMappings.Add(copy);
        await _context.SaveChangesAsync(ct);
        return (new { id = copy.Id }, $"Mapping duplicated (id={copy.Id})");
    }

    internal async Task<(object, string)> ListExternalCategoriesAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ExternalCategoriesInput>(json);
        var conn = await _context.marketplaceConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == p.ConnectionId && c.storeId == storeId, ct);
        if (conn == null) return (new { error = "Connection not found" }, "Not found");
        var adapter = _adapterRegistry.GetByKey(conn.adapterKey)!;
        var cats = await adapter.GetCategoriesAsync(conn, p.ParentId, p.Search, ct);
        return (new { categories = cats.Take(50) }, $"{cats.Count} categories");
    }

    internal async Task<(object, string)> GetExternalFieldsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ExternalFieldsInput>(json);

        int connectionId;
        string externalCategoryId;
        if (p.MappingId.HasValue)
        {
            // Resolve connection + external category from the mapping - the AI on a mapping
            // page knows mappingId but not the marketplace category id
            var mapping = await _context.categoryMappings.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
            if (mapping == null) return (new { error = "Mapping not found" }, "Not found");
            connectionId = mapping.connectionId;
            externalCategoryId = mapping.externalCategoryId;
        }
        else
        {
            if (!p.ConnectionId.HasValue || string.IsNullOrEmpty(p.ExternalCategoryId))
                return (new { error = "Provide mappingId OR connectionId+externalCategoryId" }, "No parameters");
            connectionId = p.ConnectionId.Value;
            externalCategoryId = p.ExternalCategoryId;
        }

        var conn = await _context.marketplaceConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == connectionId && c.storeId == storeId, ct);
        if (conn == null) return (new { error = "Connection not found" }, "Not found");
        var adapter = _adapterRegistry.GetByKey(conn.adapterKey)!;
        var fields = await adapter.GetCategoryFieldsAsync(conn, externalCategoryId, ct);

        var groupingKey = ProductSyncService.ResolveGroupingFieldKey(adapter);

        // Endpoint-based adapters group via API - the mapping's grouping field is synthetic
        // and doesn't exist in the live schema, so append it for the model
        if (groupingKey == ProductSyncService.SyntheticGroupFieldKey && fields.All(f => f.Key != groupingKey))
        {
            fields = fields.Concat([new ExternalField(
                Key: ProductSyncService.SyntheticGroupFieldKey,
                Name: "Merge into a card (group)",
                IsRequired: false,
                Type: "string",
                Description: "Products with the same computed value are merged into one card. Bound to groupName by default.")]).ToList();
        }

        var projected = fields.Select(f => new
        {
            f.Key, f.Name, f.Type, f.IsRequired, f.Description,
            f.Unit, f.MaxLength, f.IsAspect,
            // Large dictionaries (WB colors/brands: thousands of values) would blow up
            // the context - show a sample and point the model at search_field_options
            options = f.Options == null ? null
                : f.Options.Take(15).Select(o => new { o.Value, o.Label }).ToList(),
            optionsTotal = f.Options?.Count,
            optionsNote = f.Options?.Count > 15
                ? "shown 15 of " + f.Options.Count + " - use search_field_options to find a specific value"
                : null,
            isGroupingField = groupingKey != null && f.Key == groupingKey ? (bool?)true : null,
        }).ToList();

        return (new
        {
            sinkType = "card",
            fields = projected,
            total = projected.Count, required = fields.Count(f => f.IsRequired)
        }, $"{projected.Count} card fields ({fields.Count(f => f.IsRequired)} required)");
    }

    internal async Task<(object, string)> GetPriceFieldsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ExternalFieldsInput>(json);

        int connectionId;
        string externalCategoryId;
        if (p.MappingId.HasValue)
        {
            var mapping = await _context.categoryMappings.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
            if (mapping == null) return (new { error = "Mapping not found" }, "Not found");
            connectionId = mapping.connectionId;
            externalCategoryId = mapping.externalCategoryId;
        }
        else
        {
            if (!p.ConnectionId.HasValue || string.IsNullOrEmpty(p.ExternalCategoryId))
                return (new { error = "Provide mappingId OR connectionId+externalCategoryId" }, "No parameters");
            connectionId = p.ConnectionId.Value;
            externalCategoryId = p.ExternalCategoryId;
        }

        var conn = await _context.marketplaceConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == connectionId && c.storeId == storeId, ct);
        if (conn == null) return (new { error = "Connection not found" }, "Not found");
        var adapter = _adapterRegistry.GetByKey(conn.adapterKey)!;
        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ExportPrices))
            return (new { sinkType = "price", fields = Array.Empty<object>(), total = 0 },
                "Adapter does not support a separate price API");

        var fields = await adapter.GetPriceFieldsAsync(conn, externalCategoryId, ct);
        var projected = fields.Select(f => new
        {
            f.Key, f.Name, f.Type, f.IsRequired, f.Description,
            f.Group, f.SortOrder,
            options = f.Options?.Take(15).Select(o => new { o.Value, o.Label }).ToList(),
            optionsTotal = f.Options?.Count,
        }).ToList();

        return (new
        {
            sinkType = "price",
            fields = projected,
            total = projected.Count,
            required = fields.Count(f => f.IsRequired),
        }, $"{projected.Count} price fields ({fields.Count(f => f.IsRequired)} required)");
    }

    internal async Task<(object, string)> SearchFieldOptionsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<SearchFieldOptionsInput>(json);

        CategoryMapping? mapping = null;
        if (p.MappingId.HasValue)
        {
            mapping = await _context.categoryMappings.AsNoTracking()
                .Include(m => m.connection)
                .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
            if (mapping == null) return (new { error = "Mapping not found" }, "Not found");
        }
        else if (!p.ConnectionId.HasValue || string.IsNullOrEmpty(p.ExternalCategoryId))
        {
            return (new { error = "Provide mappingId OR connectionId+externalCategoryId" }, "No parameters");
        }

        List<ExternalField> defs;
        if (mapping != null)
        {
            defs = await GetAdapterFieldDefsAsync(mapping, ct);
        }
        else
        {
            var conn = await _context.marketplaceConnections.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == p.ConnectionId && c.storeId == storeId, ct);
            if (conn == null) return (new { error = "Connection not found" }, "Not found");
            var adapter = _adapterRegistry.GetByKey(conn.adapterKey)!;
            defs = await adapter.GetCategoryFieldsAsync(conn, p.ExternalCategoryId!, ct);
        }

        var field = defs.FirstOrDefault(d => d.Key == p.AdapterFieldKey);
        if (field == null)
            return (new { error = $"Field '{p.AdapterFieldKey}' not found on the adapter" }, "Field not found");
        if (field.Options is not { Count: > 0 })
            return (new { options = Array.Empty<object>(), totalOptions = 0 },
                $"Field '{field.Name}' has no predefined options - the value is free-form");

        var query = p.Query ?? "";
        var matches = string.IsNullOrWhiteSpace(query)
            ? field.Options.ToList()
            : field.Options.Where(o =>
                    (o.Label?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    o.Value.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        var limit = Math.Min(p.Limit ?? 20, 50);
        return (new
        {
            options = matches.Take(limit).Select(o => new { o.Value, o.Label }).ToList(),
            totalMatches = matches.Count,
            totalOptions = field.Options.Count,
        }, $"Found {matches.Count} options for \"{query}\" (field has {field.Options.Count} total)");
    }

    internal async Task<(object, string)> MergeAdapterFieldsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<MergeFieldsInput>(json);
        if (p.AliasFieldKeys.Count == 0) return (new { error = "aliasFieldKeys not provided" }, "No fields");
        var mapping = await _context.categoryMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Not found" }, "Not found");

        var prevGraph = mapping.graphJson ?? "";
        try
        {
            await _graphMutation.MergeFieldsAsync(p.MappingId, p.PrimaryFieldKey, p.AliasFieldKeys, ct);
        }
        catch (InvalidOperationException ex)
        {
            return (new { error = ex.Message }, ex.Message);
        }
        await _operationService.RecordAsync(storeId, userId, "merge_fields",
            $"Fields [{string.Join(", ", p.AliasFieldKeys)}] merged with '{p.PrimaryFieldKey}'",
            AiOperationService.BuildGraphRollback(p.MappingId, prevGraph), ct: ct);
        return (new { success = true },
            $"Merged: {string.Join(", ", p.AliasFieldKeys)} → '{p.PrimaryFieldKey}' (export to both keys, import only via primary)");
    }

    internal async Task<(object, string)> UnmergeAdapterFieldsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<UnmergeFieldsInput>(json);
        if (p.FieldKeys.Count == 0) return (new { error = "fieldKeys not provided" }, "No fields");
        var mapping = await _context.categoryMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Not found" }, "Not found");

        var prevGraph = mapping.graphJson ?? "";
        try
        {
            await _graphMutation.UnmergeFieldsAsync(p.MappingId, p.FieldKeys, ct);
        }
        catch (InvalidOperationException ex)
        {
            return (new { error = ex.Message }, ex.Message);
        }
        await _operationService.RecordAsync(storeId, userId, "unmerge_fields",
            $"Fields unmerged [{string.Join(", ", p.FieldKeys)}]",
            AiOperationService.BuildGraphRollback(p.MappingId, prevGraph), ct: ct);
        return (new { success = true }, $"Unmerged: {string.Join(", ", p.FieldKeys)}");
    }

    internal async Task<(object, string)> GetAvailableSourcesAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Not found" }, "Not found");

        var catAttrs = await _context.categoryAttributes.AsNoTracking()
            .Where(a => a.categoryId == mapping.localCategoryId)
            .Select(a => new { a.Id, a.name, a.key, a.type, a.options }).ToListAsync(ct);
        var catAttrsProjected = catAttrs.Select(a => new
        {
            a.Id,
            a.name,
            a.key,
            a.type,
            options = Delegat.Services.Marketplace.Mapping.OptionAlignmentHelper
                .ParseAttributeOptions(a.options)
                .Take(25)
                .Select(o => new { o.Value, o.Label }),
        }).ToList();
        var storeAttrs = await _context.storeAttributes.AsNoTracking()
            .Where(a => a.storeId == storeId)
            .Select(a => new { a.Id, a.name, a.key, a.type }).ToListAsync(ct);

        return (new
        {
            productFields = new[] { "sku", "name", "price", "barcode", "description", "storageQuantity" },
            categoryAttributes = catAttrsProjected,
            storeAttributes = storeAttrs
        }, $"Sources: {catAttrs.Count} category attributes, {storeAttrs.Count} store attributes");
    }

    internal async Task<(object, string)> GetMappingCoverageAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Not found" }, "Not found");
        var report = await _graphMutation.GetCoverageReportAsync(p.Id, ct);
        return (report, $"Coverage - card: {report.Card.Connected}/{report.Card.Total}" +
            (report.Price != null ? $", prices: {report.Price.Connected}/{report.Price.Total}" : "") +
            $"; required card: {report.Card.RequiredConnected}/{report.Card.RequiredTotal}");
    }

    internal async Task<(object, string)> PreviewMappingAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<PreviewMappingInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Not found" }, "Not found");
        // Preview is complex - return basic info
        return (new { mappingId = p.MappingId, productId = p.ProductId, note = "Use the editor UI for preview" },
            "Preview is available in the mapping editor");
    }

    internal async Task<(object, string)> GetOptionAlignmentHintsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<MappingQueryInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Not found" }, "Not found");

        var adapterFields = await GetAdapterFieldDefsAsync(mapping, ct);
        var hints = await _intelligence.BuildAlignmentHintsAsync(mapping, adapterFields, p.ProductId, ct);
        var schemaContext = await _intelligence.GetSchemaContextAsync(mapping, ct);
        var productSample = await _intelligence.GetProductSampleAsync(mapping.localCategoryId, p.ProductId, ct);

        return (new { schemaContext, productSample, hints },
            $"{hints.Count} optionMap hints" +
            (schemaContext.SchemaSource != null ? $" (schema from {schemaContext.SchemaSource.ConnectionName})" : ""));
    }

    internal async Task<(object, string)> ConnectFieldAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ConnectFieldInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Not found" }, "Not found");

        // Field def lets GraphMutationService append missing-but-valid fields to the graph
        var fieldDef = await ResolveFieldDefAsync(mapping, p.AdapterFieldKey, p.Sink, ct);

        var prevGraph = mapping.graphJson ?? "";
        await _graphMutation.ConnectFieldAsync(p.MappingId, p.AdapterFieldKey, p.SourceType, p.SourceField, p.SourceAttributeId, p.Sink, fieldDef, ct);
        await _operationService.RecordAsync(storeId, userId, "connect_field",
            $"Field '{p.AdapterFieldKey}' connected to {p.SourceType}:{p.SourceField ?? p.SourceAttributeId?.ToString()}",
            AiOperationService.BuildGraphRollback(p.MappingId, prevGraph), ct: ct);
        return (new { success = true }, $"Field '{p.AdapterFieldKey}' connected");
    }

    internal async Task<(object, string)> ConnectFieldsBulkAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ConnectFieldsBulkInput>(json);
        if (p.Connections is not { Count: > 0 })
            return (new { error = "Connection list is empty" }, "List is empty");

        var mapping = await _context.categoryMappings.AsNoTracking()
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Mapping not found" }, "Not found");

        var prevGraph = mapping.graphJson ?? "";
        var applied = 0; var errors = new List<string>();

        foreach (var conn in p.Connections)
        {
            try
            {
                var fieldDef = await ResolveFieldDefAsync(mapping, conn.AdapterFieldKey, conn.Sink, ct);
                await _graphMutation.ConnectFieldAsync(p.MappingId, conn.AdapterFieldKey,
                    conn.SourceType, conn.SourceField, conn.SourceAttributeId, conn.Sink, fieldDef, ct);
                applied++;
            }
            catch (Exception ex) { errors.Add($"'{conn.AdapterFieldKey}': {ex.Message}"); }
        }

        await _operationService.RecordAsync(storeId, userId, "connect_field",
            $"Connected {applied} fields of mapping {p.MappingId}",
            AiOperationService.BuildGraphRollback(p.MappingId, prevGraph), ct: ct);

        var summary = $"Fields connected: {applied}";
        if (errors.Count > 0) summary += $", errors: {errors.Count}";
        return (new { applied, errors }, summary);
    }

    internal async Task<(object, string)> SetFieldStaticAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<SetStaticInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Not found" }, "Not found");

        // Field def: enables option validation ("Default value") and graph auto-add
        var fieldDef = await ResolveFieldDefAsync(mapping, p.AdapterFieldKey, p.Sink, ct);

        var prevGraph = mapping.graphJson ?? "";
        try
        {
            await _graphMutation.SetStaticValueAsync(p.MappingId, p.AdapterFieldKey, p.Value, p.Sink, fieldDef, ct);
        }
        catch (InvalidOperationException ex)
        {
            // e.g. value not in the allowed options - return the option list so the model can retry
            return (new { error = ex.Message }, ex.Message);
        }
        await _operationService.RecordAsync(storeId, userId, "set_static_value",
            $"Field '{p.AdapterFieldKey}' = '{p.Value}'",
            AiOperationService.BuildGraphRollback(p.MappingId, prevGraph), ct: ct);
        return (new { success = true }, $"Field '{p.AdapterFieldKey}' = '{p.Value}'");
    }

    internal async Task<(object, string)> SetFieldsStaticBulkAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<SetFieldsStaticBulkInput>(json);
        if (p.Fields is not { Count: > 0 })
            return (new { error = "Field list is empty" }, "List is empty");

        var mapping = await _context.categoryMappings.AsNoTracking()
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Mapping not found" }, "Not found");

        var defs = await GetAdapterFieldDefsAsync(mapping, ct);
        var prevGraph = mapping.graphJson ?? "";
        var applied = 0; var errors = new List<string>();

        foreach (var field in p.Fields)
        {
            try
            {
                var fieldDef = await ResolveFieldDefAsync(mapping, field.AdapterFieldKey, field.Sink, ct);
                await _graphMutation.SetStaticValueAsync(p.MappingId, field.AdapterFieldKey, field.Value, field.Sink, fieldDef, ct);
                applied++;
            }
            catch (Exception ex) { errors.Add($"'{field.AdapterFieldKey}': {ex.Message}"); }
        }

        await _operationService.RecordAsync(storeId, userId, "set_static_value",
            $"Set {applied} static values for mapping {p.MappingId}",
            AiOperationService.BuildGraphRollback(p.MappingId, prevGraph), ct: ct);

        var summary = $"Values set: {applied}";
        if (errors.Count > 0) summary += $", errors: {errors.Count} ({string.Join("; ", errors)})";
        return (new { applied, errors }, summary);
    }

    internal async Task<(object, string)> AddFieldModifierAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<AddModifierInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Not found" }, "Not found");

        var fieldDef = await ResolveFieldDefAsync(mapping, p.AdapterFieldKey, p.Sink, ct);

        var prevGraph = mapping.graphJson ?? "";
        try
        {
            await _graphMutation.AddModifierAsync(p.MappingId, p.AdapterFieldKey, p.ModifierType, p.Params, p.Sink, fieldDef, ct);
        }
        catch (InvalidOperationException ex)
        {
            // e.g. unknown modifier type - the message lists valid types so the model can retry
            return (new { error = ex.Message }, ex.Message);
        }
        await _operationService.RecordAsync(storeId, userId, "add_modifier",
            $"Modifier '{p.ModifierType}' added to field '{p.AdapterFieldKey}'",
            AiOperationService.BuildGraphRollback(p.MappingId, prevGraph), ct: ct);
        return (new { success = true }, $"Modifier '{p.ModifierType}' added");
    }

    internal async Task<(object, string)> RemoveFieldConnectionAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<FieldKeyInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Not found" }, "Not found");

        var prevGraph = mapping.graphJson ?? "";
        await _graphMutation.DisconnectFieldAsync(p.MappingId, p.FieldKey, p.Sink, ct);
        await _operationService.RecordAsync(storeId, userId, "disconnect_field",
            $"Field '{p.FieldKey}' disconnected in mapping {p.MappingId}",
            AiOperationService.BuildGraphRollback(p.MappingId, prevGraph), ct: ct);
        return (new { success = true }, $"Field '{p.FieldKey}' disconnected");
    }

    internal async Task<(object, string)> FindProductsWithoutMappingAsync(
        string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ConnectionMappingQueryInput>(json);
        var (conn, connError) = await ResolveConnectionAsync(storeId, p, ct);
        if (conn == null) return (new { error = connError }, connError!);

        var limit = Math.Clamp(p.Limit ?? 50, 1, 200);

        var mappingCategoryIds = await _context.categoryMappings.AsNoTracking()
            .Where(m => m.connectionId == conn.Id)
            .Select(m => m.localCategoryId)
            .Distinct()
            .ToListAsync(ct);

        if (mappingCategoryIds.Count == 0)
            return (new { error = "The connection has no category mappings" },
                "No mappings for the connection");

        var mappingsByCategory = await _context.categoryMappings.AsNoTracking()
            .Where(m => m.connectionId == conn.Id)
            .Select(m => new { m.Id, m.localCategoryId, name = m.name ?? m.externalCategoryName })
            .ToListAsync(ct);
        var mappingsLookup = mappingsByCategory
            .GroupBy(m => m.localCategoryId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var baseQuery =
            from pr in _context.products.AsNoTracking()
            where pr.storeId == storeId && pr.deletedAt == null
                  && pr.categoryId.HasValue
                  && mappingCategoryIds.Contains(pr.categoryId.Value)
            join link in _context.productMarketplaceLinks.AsNoTracking()
                    .Where(l => l.connectionId == conn.Id)
                on pr.Id equals link.productId into links
            from link in links.DefaultIfEmpty()
            where link == null || link.categoryMappingId == null
            select new
            {
                productId = pr.Id,
                name = pr.name,
                sku = pr.sku,
                categoryId = pr.categoryId,
                categoryName = pr.category!.name,
                hasLink = link != null,
                selectedMappingId = link != null ? link.categoryMappingId : (int?)null,
            };

        var totalWithout = await baseQuery.CountAsync(ct);
        var products = await baseQuery
            .OrderBy(p => p.name)
            .Take(limit)
            .ToListAsync(ct);

        var projected = products.Select(pr =>
        {
            var available = pr.categoryId.HasValue && mappingsLookup.TryGetValue(pr.categoryId.Value, out var ms)
                ? ms.Select(m => new { id = m.Id, name = m.name }).ToList()
                : null;
            return new
            {
                pr.productId,
                pr.name,
                pr.sku,
                pr.categoryId,
                pr.categoryName,
                pr.hasLink,
                pr.selectedMappingId,
                availableMappings = available,
            };
        }).ToList();

        return (new
        {
            connectionId = conn.Id,
            connectionName = conn.name,
            adapterKey = conn.adapterKey,
            totalWithoutMapping = totalWithout,
            shown = projected.Count,
            products = projected,
        }, totalWithout == 0
            ? $"All products with category mappings are already assigned to \"{conn.name}\""
            : $"{totalWithout} products without an assigned mapping for \"{conn.name}\" (showing {projected.Count})");
    }

    internal async Task<(object, string)> GetProductMappingSummaryAsync(
        string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ConnectionMappingQueryInput>(json);
        MarketplaceConnection? conn = null;
        if (p.ConnectionId.HasValue || !string.IsNullOrWhiteSpace(p.ConnectionName) || !string.IsNullOrWhiteSpace(p.AdapterKey))
        {
            var (resolved, err) = await ResolveConnectionAsync(storeId, p, ct);
            if (resolved == null) return (new { error = err }, err!);
            conn = resolved;
        }

        var connections = conn != null
            ? new List<MarketplaceConnection> { conn }
            : await _context.marketplaceConnections.AsNoTracking()
                .Where(c => c.storeId == storeId)
                .ToListAsync(ct);

        var summaries = new List<object>();
        int? singleWithout = null;
        int? singleWith = null;
        foreach (var connection in connections)
        {
            var mappingCategoryIds = await _context.categoryMappings.AsNoTracking()
                .Where(m => m.connectionId == connection.Id)
                .Select(m => m.localCategoryId)
                .Distinct()
                .ToListAsync(ct);

            var eligibleProducts = mappingCategoryIds.Count == 0
                ? 0
                : await _context.products.AsNoTracking()
                    .CountAsync(pr => pr.storeId == storeId && pr.deletedAt == null
                        && pr.categoryId.HasValue
                        && mappingCategoryIds.Contains(pr.categoryId.Value), ct);

            var links = await _context.productMarketplaceLinks.AsNoTracking()
                .Where(l => l.connectionId == connection.Id)
                .Select(l => new { l.productId, l.categoryMappingId })
                .ToListAsync(ct);

            var assigned = links.Where(l => l.categoryMappingId != null).ToList();
            var assignedProductIds = assigned.Select(l => l.productId).ToHashSet();

            var withoutMapping = eligibleProducts == 0
                ? 0
                : await (
                    from pr in _context.products.AsNoTracking()
                    where pr.storeId == storeId && pr.deletedAt == null
                          && pr.categoryId.HasValue
                          && mappingCategoryIds.Contains(pr.categoryId.Value)
                    join link in _context.productMarketplaceLinks.AsNoTracking()
                            .Where(l => l.connectionId == connection.Id)
                        on pr.Id equals link.productId into linksJoin
                    from link in linksJoin.DefaultIfEmpty()
                    where link == null || link.categoryMappingId == null
                    select pr.Id
                ).CountAsync(ct);

            var mappingNames = await _context.categoryMappings.AsNoTracking()
                .Where(m => m.connectionId == connection.Id)
                .Select(m => new { m.Id, name = m.name ?? m.externalCategoryName })
                .ToDictionaryAsync(m => m.Id, m => m.name, ct);

            var perMapping = assigned
                .GroupBy(l => l.categoryMappingId!.Value)
                .Select(g => new
                {
                    mappingId = g.Key,
                    mappingName = mappingNames.GetValueOrDefault(g.Key, $"#{g.Key}"),
                    productCount = g.Count(),
                })
                .OrderByDescending(x => x.productCount)
                .ToList();

            if (conn != null && connection.Id == conn.Id)
            {
                singleWithout = withoutMapping;
                singleWith = assigned.Count;
            }

            summaries.Add(new
            {
                connectionId = connection.Id,
                connectionName = connection.name,
                adapterKey = connection.adapterKey,
                eligibleProducts,
                withMapping = assigned.Count,
                withoutMapping,
                linksTotal = links.Count,
                perMapping,
            });
        }

        var headline = conn != null && singleWithout.HasValue
            ? $"\"{conn.name}\": {singleWithout} without a mapping, {singleWith} with a mapping"
            : $"Summary for {connections.Count} connections";

        return (new { connections = summaries }, headline);
    }

    internal async Task<(object, string)> AssignProductMappingsAsync(
        string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<AssignProductMappingsInput>(json);
        if (p.MappingId <= 0)
            return (new { error = "Provide mappingId" }, "No mappingId");

        var mapping = await _context.categoryMappings.AsNoTracking()
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null)
            return (new { error = "Mapping not found" }, "Mapping not found");

        var query = new ConnectionMappingQueryInput
        {
            ConnectionId = p.ConnectionId ?? mapping.connectionId,
            ConnectionName = p.ConnectionName,
        };
        var (conn, connError) = await ResolveConnectionAsync(storeId, query, ct);
        if (conn == null) return (new { error = connError }, connError!);
        if (conn.Id != mapping.connectionId)
            return (new { error = "Mapping belongs to a different connection" }, "Wrong connection");

        List<int> productIds;
        if (p.ProductIds is { Count: > 0 })
        {
            productIds = p.ProductIds.Distinct().ToList();
        }
        else if (p.ApplyToAllWithoutMapping)
        {
            productIds = await (
                from pr in _context.products.AsNoTracking()
                where pr.storeId == storeId && pr.deletedAt == null
                      && pr.categoryId == mapping.localCategoryId
                join link in _context.productMarketplaceLinks.AsNoTracking()
                        .Where(l => l.connectionId == conn.Id)
                    on pr.Id equals link.productId into links
                from link in links.DefaultIfEmpty()
                where link == null || link.categoryMappingId == null
                select pr.Id
            ).ToListAsync(ct);
        }
        else
        {
            return (new { error = "Provide productIds or applyToAllWithoutMapping=true" },
                "No target products");
        }

        if (productIds.Count == 0)
            return (new { updated = 0, skipped = 0 }, "No products to assign a mapping to");

        var products = await _context.products.AsNoTracking()
            .Where(pr => productIds.Contains(pr.Id) && pr.storeId == storeId && pr.deletedAt == null)
            .Select(pr => new { pr.Id, pr.categoryId })
            .ToListAsync(ct);

        var validIds = products
            .Where(pr => pr.categoryId == mapping.localCategoryId)
            .Select(pr => pr.Id)
            .ToHashSet();
        var skipped = productIds.Count - validIds.Count;

        if (validIds.Count == 0)
            return (new { error = "No product matches the mapping's category", skipped },
                "Products of a different category");

        var existingLinks = await _context.productMarketplaceLinks
            .Where(l => validIds.Contains(l.productId) && l.connectionId == conn.Id)
            .ToListAsync(ct);
        var linkByProduct = existingLinks.ToDictionary(l => l.productId);

        var updated = 0;
        foreach (var productId in validIds)
        {
            if (linkByProduct.TryGetValue(productId, out var link))
            {
                if (link.categoryMappingId == mapping.Id) continue;
                link.categoryMappingId = mapping.Id;
            }
            else
            {
                var newLink = new ProductMarketplaceLink();
                newLink.AddFill(new ProductMarketplaceLink.AddForm
                {
                    productId = productId,
                    connectionId = conn.Id,
                    externalProductId = $"pending:{productId}",
                    status = ProductMarketplaceStatus.NotSynced,
                    categoryMappingId = mapping.Id,
                });
                _context.productMarketplaceLinks.Add(newLink);
            }
            updated++;
        }

        await _context.SaveChangesAsync(ct);

        var mappingLabel = mapping.name ?? mapping.externalCategoryName ?? $"#{mapping.Id}";
        await _operationService.RecordAsync(storeId, userId, "assign_product_mappings",
            $"Mapping \"{mappingLabel}\" assigned to {updated} products ({conn.name})",
            new { mappingId = mapping.Id, connectionId = conn.Id, productIds = validIds.ToList() },
            canRollback: false, ct: ct);

        return (new
        {
            updated,
            skipped,
            mappingId = mapping.Id,
            mappingName = mappingLabel,
            connectionId = conn.Id,
            connectionName = conn.name,
        }, $"Mapping \"{mappingLabel}\" assigned to {updated} products" +
           (skipped > 0 ? $", skipped {skipped}" : ""));
    }

    private async Task<(MarketplaceConnection? conn, string? error)> ResolveConnectionAsync(
        int storeId, ConnectionMappingQueryInput p, CancellationToken ct)
    {
        if (p.ConnectionId is > 0)
        {
            var byId = await _context.marketplaceConnections.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == p.ConnectionId && c.storeId == storeId, ct);
            return byId == null
                ? (null, "Connection not found")
                : (byId, null);
        }

        var search = (p.ConnectionName ?? p.AdapterKey ?? "").Trim();
        if (search.Length == 0)
            return (null, "Provide connectionId or connectionName (e.g. \"Ozon\")");

        var matches = await _context.marketplaceConnections.AsNoTracking()
            .Where(c => c.storeId == storeId &&
                (EF.Functions.Like(c.name, $"%{search}%") ||
                 EF.Functions.Like(c.adapterKey, $"%{search}%")))
            .Select(c => new { c.Id, c.name, c.adapterKey })
            .ToListAsync(ct);

        if (matches.Count == 0)
            return (null, $"Connection \"{search}\" not found - call list_connections");
        if (matches.Count > 1)
            return (null,
                $"Found {matches.Count} connections: {string.Join(", ", matches.Select(m => $"{m.name} (#{m.Id})"))}. Specify connectionId.");

        var conn = await _context.marketplaceConnections.AsNoTracking()
            .FirstAsync(c => c.Id == matches[0].Id, ct);
        return (conn, null);
    }

    // Fetches adapter field definitions for a mapping (mapping must have connection loaded).
    // Returns empty on adapter failure - callers degrade gracefully.
    private async Task<List<ExternalField>> GetAdapterFieldDefsAsync(CategoryMapping mapping, CancellationToken ct)
    {
        try
        {
            var adapter = _adapterRegistry.GetByKey(mapping.connection.adapterKey);
            if (adapter == null) return new();
            return await adapter.GetCategoryFieldsAsync(mapping.connection, mapping.externalCategoryId, ct);
        }
        catch { return new(); }
    }

    private async Task<List<ExternalField>> GetPriceFieldDefsAsync(CategoryMapping mapping, CancellationToken ct)
    {
        try
        {
            var adapter = _adapterRegistry.GetByKey(mapping.connection.adapterKey);
            if (adapter == null || !adapter.Capabilities.HasFlag(MarketplaceCapabilities.ExportPrices))
                return new();
            return await adapter.GetPriceFieldsAsync(mapping.connection, mapping.externalCategoryId, ct);
        }
        catch { return new(); }
    }

    private async Task<ExternalField?> ResolveFieldDefAsync(
        CategoryMapping mapping, string fieldKey, string? sink, CancellationToken ct)
    {
        if (string.Equals(sink, "price", StringComparison.OrdinalIgnoreCase))
        {
            var priceDefs = await GetPriceFieldDefsAsync(mapping, ct);
            return priceDefs.FirstOrDefault(d => d.Key == fieldKey);
        }
        var defs = await GetAdapterFieldDefsAsync(mapping, ct);
        var cardDef = defs.FirstOrDefault(d => d.Key == fieldKey);
        if (cardDef != null) return cardDef;
        // Auto-resolve price-only keys when sink not specified
        var priceOnly = await GetPriceFieldDefsAsync(mapping, ct);
        return priceOnly.FirstOrDefault(d => d.Key == fieldKey);
    }
}

// ---- Mapping-specific DTOs ----

internal class CreateMappingInput { public int ConnectionId { get; set; } public int LocalCategoryId { get; set; } public string ExternalCategoryId { get; set; } = ""; public string? ExternalCategoryName { get; set; } public string? Name { get; set; } }
internal class UpdateNameInput { public int Id { get; set; } public string Name { get; set; } = ""; }
internal class ExternalCategoriesInput { public int ConnectionId { get; set; } public string? ParentId { get; set; } public string? Search { get; set; } }
internal class ExternalFieldsInput { public int? ConnectionId { get; set; } public string? ExternalCategoryId { get; set; } public int? MappingId { get; set; } }
internal class SearchFieldOptionsInput { public string AdapterFieldKey { get; set; } = ""; public string? Query { get; set; } public int? MappingId { get; set; } public int? ConnectionId { get; set; } public string? ExternalCategoryId { get; set; } public int? Limit { get; set; } }
internal class MergeFieldsInput { public int MappingId { get; set; } public string PrimaryFieldKey { get; set; } = ""; public List<string> AliasFieldKeys { get; set; } = []; }
internal class UnmergeFieldsInput { public int MappingId { get; set; } public List<string> FieldKeys { get; set; } = []; }
internal class MappingQueryInput 
{ 
    public int Id { get; set; } 
    public int? ProductId { get; set; } 
}
internal class PreviewMappingInput { public int MappingId { get; set; } public int ProductId { get; set; } }
internal class ConnectFieldInput { public int MappingId { get; set; } public string AdapterFieldKey { get; set; } = ""; public string SourceType { get; set; } = ""; public string? SourceField { get; set; } public int? SourceAttributeId { get; set; } public string? Sink { get; set; } }
internal class ConnectFieldsBulkInput { public int MappingId { get; set; } public List<ConnectFieldInput> Connections { get; set; } = []; }
internal class SetStaticInput { public int MappingId { get; set; } public string AdapterFieldKey { get; set; } = ""; public string Value { get; set; } = ""; public string? Sink { get; set; } }
internal class SetFieldsStaticBulkInput { public int MappingId { get; set; } public List<SetStaticInput> Fields { get; set; } = []; }
internal class AddModifierInput { public int MappingId { get; set; } public string AdapterFieldKey { get; set; } = ""; public string ModifierType { get; set; } = ""; public Dictionary<string, object>? Params { get; set; } public string? Sink { get; set; } }
internal class FieldKeyInput { public int MappingId { get; set; } public string FieldKey { get; set; } = ""; public string? Sink { get; set; } }
internal class ConnectionMappingQueryInput
{
    public int? ConnectionId { get; set; }
    public string? ConnectionName { get; set; }
    public string? AdapterKey { get; set; }
    public int? Limit { get; set; }
}
internal class AssignProductMappingsInput
{
    public int? ConnectionId { get; set; }
    public string? ConnectionName { get; set; }
    public int MappingId { get; set; }
    public List<int>? ProductIds { get; set; }
    public bool ApplyToAllWithoutMapping { get; set; }
}
