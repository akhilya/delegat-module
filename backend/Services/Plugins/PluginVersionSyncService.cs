using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PrintO.Models;
using PrintO.Models.Marketplace;
using PrintO.Models.Marketplace.DTOs;
using PrintO.Services.Marketplace;

namespace PrintO.Services.Plugins;

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
            // Load category mappings with field mappings and local category attributes
            var mappings = await _context.categoryMappings
                .Include(m => m.fieldMappings)
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
                    var added = await SyncCategoryMappingFieldsAsync(mapping, connection, adapter, ct);
                    if (added > 0)
                    {
                        totalFieldsAdded += added;
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

    private async Task<int> SyncCategoryMappingFieldsAsync(
        CategoryMapping mapping,
        MarketplaceConnection connection,
        IMarketplaceAdapter adapter,
        CancellationToken ct)
    {
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
            return 0;
        }

        // Build set of already-mapped field keys
        var existingKeys = mapping.fieldMappings
            .Select(fm => fm.externalFieldKey)
            .ToHashSet();

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

        // Start sort order after existing field mappings
        var maxSortOrder = mapping.fieldMappings.Any()
            ? mapping.fieldMappings.Max(fm => fm.sortOrder)
            : -1;

        var newFields = fields.Where(f => !existingKeys.Contains(f.Key)).ToList();
        _logger.LogInformation(
            "Mapping {Id} ({Category}): {Total} fields from API, {Existing} already mapped, {New} new.",
            mapping.Id, mapping.externalCategoryName,
            fields.Count, existingKeys.Count, newFields.Count);

        int count = 0;

        foreach (var field in fields.OrderBy(f => f.SortOrder))
        {
            if (existingKeys.Contains(field.Key))
                continue;

            // Use the same auto-mapping logic as CategoryImportService
            var (sourceType, sourceField, sourceAttributeId) =
                CategoryImportService.DetermineAutoMapping(field, attributeMap);

            var isRequired = sourceType == Enums.FieldMappingSourceType.ProductField
                ? false
                : field.IsRequired;

            var fieldMapping = new FieldMapping();
            fieldMapping.AddFill(new FieldMapping.AddForm
            {
                categoryMappingId = mapping.Id,
                externalFieldKey = field.Key,
                externalFieldName = field.Name,
                isRequired = isRequired,
                isEnabled = true,
                sourceType = sourceType,
                sourceField = sourceField,
                sourceAttributeId = sourceAttributeId,
                externalFieldType = field.Type,
                externalFieldOptions = field.Options != null && field.Options.Count > 0
                    ? JsonSerializer.Serialize(field.Options.Select(o => new { value = o.Value, label = o.Label }))
                    : null,
                sortOrder = ++maxSortOrder
            });

            _context.fieldMappings.Add(fieldMapping);
            count++;
        }

        if (count > 0)
        {
            await _context.SaveChangesAsync(ct);
            _logger.LogInformation("Mapping {Id} ({Category}): +{Count} fields.",
                mapping.Id, mapping.externalCategoryName, count);
        }

        return count;
    }
}
