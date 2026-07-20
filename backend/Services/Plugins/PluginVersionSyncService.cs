using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services.Marketplace;
using Delegat.Services.Marketplace.Graph;

namespace Delegat.Services.Plugins;

/// <summary>
/// Result of an additive adapter-field schema sync for a single category mapping.
/// Existing graph connections and user config are never removed.
/// </summary>
public sealed class MappingFieldSyncResult
{
    public int CardFieldsAdded { get; set; }
    public int PriceFieldsAdded { get; set; }
    public int TotalFromApi { get; set; }
    public int ExistingCount { get; set; }
    public List<string> AddedFieldKeys { get; set; } = [];
    public List<string> AddedPriceFieldKeys { get; set; } = [];
    public int TotalAdded => CardFieldsAdded + PriceFieldsAdded;
}

/// <summary>
/// Detects plugin version changes and adds missing fields to existing category mappings.
/// </summary>
public class PluginVersionSyncService
{
    private readonly DataContext _context;
    private readonly PluginManager _pluginManager;
    private readonly IMarketplaceAdapterRegistry _adapterRegistry;
    private readonly ILogger<PluginVersionSyncService> _logger;

    public PluginVersionSyncService(
        DataContext context,
        PluginManager pluginManager,
        IMarketplaceAdapterRegistry adapterRegistry,
        ILogger<PluginVersionSyncService> logger)
    {
        _context = context;
        _pluginManager = pluginManager;
        _adapterRegistry = adapterRegistry;
        _logger = logger;
    }

    /// <summary>
    /// Fetch the current adapter schema for one mapping and append any new fields to graphJson.
    /// Does not remove or overwrite existing field bindings.
    /// </summary>
    public async Task<MappingFieldSyncResult> SyncMappingFieldsAsync(
        int connectionId,
        int mappingId,
        CancellationToken ct = default)
    {
        var mapping = await _context.categoryMappings
            .Include(m => m.localCategory!)
                .ThenInclude(c => c.attributes)
            .FirstOrDefaultAsync(m => m.Id == mappingId && m.connectionId == connectionId, ct);

        if (mapping == null)
            throw new InvalidOperationException("Mapping not found");

        var connection = await _context.marketplaceConnections
            .FirstOrDefaultAsync(c => c.Id == connectionId, ct);

        if (connection == null)
            throw new InvalidOperationException("Connection not found");

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            throw new InvalidOperationException($"Adapter {connection.adapterKey} not found");

        return await SyncCategoryMappingFieldsAsync(mapping, connection, adapter, ct);
    }

    /// <summary>
    /// Check all loaded plugins for version changes and sync missing fields.
    /// </summary>
    public async Task SyncAllPluginVersionsAsync(CancellationToken ct = default)
    {
        // Load all stored plugin versions
        var storedVersions = await _context.systemVersions
            .Where(sv => sv.key.StartsWith("plugin:"))
            .ToDictionaryAsync(sv => sv.key, sv => sv, ct);

        foreach (var loaded in _pluginManager.GetAllLoadedPlugins())
        {
            var dbKey = $"plugin:{loaded.Key}";
            storedVersions.TryGetValue(dbKey, out var storedRecord);

            if (storedRecord != null && storedRecord.version == loaded.Version)
            {
                _logger.LogDebug("Plugin {Key} version unchanged ({Version}), skipping.",
                    loaded.Key, loaded.Version);
                continue;
            }

            var oldVersion = storedRecord?.version;
            _logger.LogInformation("Plugin {Key} version changed: {Old} → {New}",
                loaded.Key, oldVersion ?? "null", loaded.Version);

            try
            {
                await SyncPluginFieldsAsync(loaded.Key, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync fields for plugin {Key}", loaded.Key);
                // Don't block other plugins
            }

            // Upsert version record
            if (storedRecord != null)
            {
                storedRecord.version = loaded.Version;
                storedRecord.appliedAt = DateTime.UtcNow;
            }
            else
            {
                _context.systemVersions.Add(new SystemVersion
                {
                    key = dbKey,
                    version = loaded.Version,
                    appliedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync(ct);
        }
    }

    private async Task SyncPluginFieldsAsync(string adapterKey, CancellationToken ct)
    {
        var adapter = _adapterRegistry.GetByKey(adapterKey);
        if (adapter == null)
        {
            _logger.LogWarning("Adapter {Key} not found in registry, skipping sync.", adapterKey);
            return;
        }

        // Find all active connections using this adapter
        var connections = await _context.marketplaceConnections
            .Where(c => c.adapterKey == adapterKey && c.isActive)
            .ToListAsync(ct);

        if (connections.Count == 0)
        {
            _logger.LogDebug("No active connections for adapter {Key}, skipping.", adapterKey);
            return;
        }

        int totalMappings = 0;
        int totalFieldsAdded = 0;

        foreach (var connection in connections)
        {
            // Load category mappings with local category attributes (schema lives in graphJson)
            var mappings = await _context.categoryMappings
                .Include(m => m.localCategory)
                    .ThenInclude(c => c.attributes)
                .Where(m => m.connectionId == connection.Id)
                .ToListAsync(ct);

            _logger.LogInformation(
                "Connection {ConnId} ({ConnName}): found {Count} category mappings.",
                connection.Id, connection.name, mappings.Count);

            foreach (var mapping in mappings)
            {
                try
                {
                    var result = await SyncCategoryMappingFieldsAsync(mapping, connection, adapter, ct);
                    if (result.TotalAdded > 0)
                    {
                        totalFieldsAdded += result.TotalAdded;
                        totalMappings++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to sync mapping {MappingId} ({Category}), continuing.",
                        mapping.Id, mapping.externalCategoryName);
                }
            }
        }

        _logger.LogInformation("Plugin {Key}: synced {Mappings} mappings, added {Fields} fields.",
            adapterKey, totalMappings, totalFieldsAdded);
    }

    private async Task<MappingFieldSyncResult> SyncCategoryMappingFieldsAsync(
        CategoryMapping mapping,
        MarketplaceConnection connection,
        IMarketplaceAdapter adapter,
        CancellationToken ct)
    {
        var result = new MappingFieldSyncResult();

        // Get current fields from marketplace
        List<ExternalField> fields;
        try
        {
            fields = await adapter.GetCategoryFieldsAsync(connection, mapping.externalCategoryId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch fields for category {CatId} on connection {ConnId}.",
                mapping.externalCategoryId, connection.Id);
            throw new InvalidOperationException(
                "Failed to fetch category fields from the marketplace. Check the connection and try again.", ex);
        }

        fields = fields.Where(f => f.Key != "_hasSizes").ToList();

        // Already-mapped field keys come from the graph's adapter schema (single source of truth).
        var existingKeys = MappingService.SchemaFields(mapping.graphJson)
            .Select(f => f.fieldKey)
            .Where(k => !string.IsNullOrEmpty(k))
            .ToHashSet(StringComparer.Ordinal);

        result.TotalFromApi = fields.Count;
        result.ExistingCount = existingKeys.Count;

        // Build attribute map from local category
        var attributeMap = new Dictionary<string, int>();
        if (mapping.localCategory?.attributes != null)
        {
            foreach (var attr in mapping.localCategory.attributes)
            {
                var key = attr.key ?? attr.Id.ToString();
                if (!attributeMap.ContainsKey(key))
                    attributeMap[key] = attr.Id;
            }
        }

        var newFields = fields.Where(f => !existingKeys.Contains(f.Key)).ToList();
        _logger.LogInformation(
            "Mapping {Id} ({Category}): {Total} fields from API, {Existing} already mapped, {New} new.",
            mapping.Id, mapping.externalCategoryName,
            fields.Count, existingKeys.Count, newFields.Count);

        var cardFieldsAdded = newFields.Count;
        var addedFieldKeys = newFields.Select(f => f.Key).ToList();

        if (cardFieldsAdded > 0)
        {
            // Additively sync the new fields into the mapping's node graph.
            var specs = fields.OrderBy(f => f.SortOrder).Select(field =>
            {
                var (sourceType, sourceField, sourceAttributeId) =
                    CategoryImportService.DetermineAutoMapping(field, attributeMap);
                return new FieldSpec
                {
                    externalFieldKey = field.Key,
                    externalFieldName = field.Name,
                    externalFieldType = field.Type,
                    isRequired = sourceType != Enums.FieldMappingSourceType.ProductField && field.IsRequired,
                    isEnabled = true,
                    sortOrder = field.SortOrder,
                    sourceType = sourceType,
                    sourceField = sourceField,
                    sourceAttributeId = sourceAttributeId,
                };
            }).ToList();

            mapping.graphJson = GraphGenerator.SyncAdapterFields(mapping.graphJson, specs);
        }

        var priceFieldsAdded = 0;
        var addedPriceFieldKeys = new List<string>();

        if (adapter.Capabilities.HasFlag(MarketplaceCapabilities.ExportPrices))
        {
            try
            {
                var priceFields = await adapter.GetPriceFieldsAsync(connection, mapping.externalCategoryId, ct);
                var existingPriceKeys = MappingService.SchemaPriceFields(mapping.graphJson)
                    .Select(f => f.fieldKey)
                    .Where(k => !string.IsNullOrEmpty(k))
                    .ToHashSet(StringComparer.Ordinal);
                var newPriceFields = priceFields.Where(f => !existingPriceKeys.Contains(f.Key)).ToList();
                priceFieldsAdded = newPriceFields.Count;
                addedPriceFieldKeys = newPriceFields.Select(f => f.Key).ToList();
                if (priceFieldsAdded > 0)
                {
                    var extendedPrice = await _context.stores.AsNoTracking()
                        .Where(s => s.Id == connection.storeId)
                        .Select(s => s.extendedPriceParameters)
                        .FirstOrDefaultAsync(ct);
                    var priceSpecs = CategoryImportService.BuildPriceFieldSpecs(
                        priceFields, attributeMap, extendedPriceParameters: extendedPrice);
                    mapping.graphJson = GraphGenerator.SyncPriceFields(mapping.graphJson, priceSpecs);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to sync price fields for mapping {Id}.", mapping.Id);
            }
        }

        result.CardFieldsAdded = cardFieldsAdded;
        result.PriceFieldsAdded = priceFieldsAdded;
        result.AddedFieldKeys = addedFieldKeys;
        result.AddedPriceFieldKeys = addedPriceFieldKeys;

        if (result.TotalAdded > 0)
        {
            await _context.SaveChangesAsync(ct);
            _logger.LogInformation("Mapping {Id} ({Category}): +{Count} fields.",
                mapping.Id, mapping.externalCategoryName, result.TotalAdded);
        }

        return result;
    }
}
