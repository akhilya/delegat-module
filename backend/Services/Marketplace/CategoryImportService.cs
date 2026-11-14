using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Delegat.Enums;
using Delegat.Infrastructure;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services.Marketplace.Graph;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Service for importing categories from marketplaces.
/// </summary>
public class CategoryImportService
{
    private readonly DataContext _context;
    private readonly IMarketplaceAdapterRegistry _adapterRegistry;

    /// <summary>
    /// Initializes a new instance of <see cref="CategoryImportService"/>.
    /// </summary>
    /// <param name="context">Database context.</param>
    /// <param name="adapterRegistry">Marketplace adapter registry.</param>
    public CategoryImportService(DataContext context, IMarketplaceAdapterRegistry adapterRegistry)
    {
        _context = context;
        _adapterRegistry = adapterRegistry;
    }

    /// <summary>
    /// Import a category from a marketplace.
    /// Creates a local category, attributes, and field mappings.
    /// </summary>
    /// <param name="connection">Marketplace connection.</param>
    /// <param name="externalCategoryId">Category ID on the marketplace.</param>
    /// <param name="localCategoryName">Local category name (falls back to marketplace name if null).</param>
    /// <param name="externalCategoryName">Category name on the marketplace (passed from frontend).</param>
    /// <param name="localCategoryId">Optional existing local category to update instead of creating a new one.</param>
    /// <param name="selectedFieldKeys">Optional subset of marketplace field keys to import.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="attributeDecisions">Explicit choices for likely duplicate local attributes.</param>
    /// <param name="requireDuplicateDecisions">Require a choice when likely duplicates exist; false only for jobs queued before decisions were introduced.</param>
    /// <returns>Result of the category import operation.</returns>
    public async Task<CategoryImportResult> ImportExternalCategoryAsync(
        MarketplaceConnection connection,
        string externalCategoryId,
        string? localCategoryName = null,
        string? externalCategoryName = null,
        int? localCategoryId = null,
        List<string>? selectedFieldKeys = null,
        CancellationToken ct = default,
        IReadOnlyList<CategoryImportAttributeDecision>? attributeDecisions = null,
        bool requireDuplicateDecisions = true)
    {
        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
        {
            return CategoryImportResult.Failure($"Adapter '{connection.adapterKey}' not found");
        }

        // Check adapter capabilities
        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.GetCategoryFields))
        {
            return CategoryImportResult.Failure("Adapter does not support retrieving category fields");
        }

        var eligibility = await ExternalCategoryEligibility.EnsureMappableAsync(
            adapter, connection, externalCategoryId, ct);
        if (!eligibility.IsSuccess)
            return CategoryImportResult.Failure(eligibility.ErrorMessage!);

        // Leaf-validated fields from the marketplace (meta keys already stripped)
        var fields = eligibility.Fields.ToList();

        var marketplaceName = MarketplaceCategoryLabel.FirstReal(
            externalCategoryId,
            externalCategoryName,
            MarketplaceCategoryLabel.FromExternalCategory(eligibility.Category));
        if (marketplaceName == null)
        {
            var resolved = await ExternalCategoryEligibility.ResolveCategoryAsync(
                adapter, connection, externalCategoryId, ct);
            marketplaceName = MarketplaceCategoryLabel.FirstReal(
                externalCategoryId,
                MarketplaceCategoryLabel.FromExternalCategory(resolved));
        }

        // Filter fields - exclude base product fields (sku, barcode, name, description, price, stock, images)
        // and grouping fields (e.g. Ozon 9048), since they don't need attribute creation
        var fieldsForAttributes = fields.Where(f => !IsBaseOrSpecialField(f)).ToList();

        // Filter by selectedFieldKeys if provided
        var unselectedAttributeFieldKeys = new HashSet<string>(StringComparer.Ordinal);
        if (selectedFieldKeys != null)
        {
            var selectedKeys = selectedFieldKeys.ToHashSet(StringComparer.Ordinal);
            unselectedAttributeFieldKeys = fieldsForAttributes
                .Where(field => !selectedKeys.Contains(field.Key))
                .Select(field => field.Key)
                .ToHashSet(StringComparer.Ordinal);
            fieldsForAttributes = fieldsForAttributes
                .Where(f => selectedKeys.Contains(f.Key))
                .ToList();
        }

        var decisionsByFieldKey = new Dictionary<string, CategoryImportAttributeDecision>(StringComparer.Ordinal);
        foreach (var decision in attributeDecisions ?? [])
        {
            if (decision == null
                || string.IsNullOrWhiteSpace(decision.fieldKey)
                || !decisionsByFieldKey.TryAdd(decision.fieldKey, decision))
            {
                return CategoryImportResult.Failure("Duplicate attribute decisions are invalid");
            }
        }

        var selectedAttributeFieldKeys = fieldsForAttributes
            .Select(field => field.Key)
            .ToHashSet(StringComparer.Ordinal);
        if (decisionsByFieldKey.Keys.Any(key => !selectedAttributeFieldKeys.Contains(key)))
            return CategoryImportResult.Failure("A duplicate attribute decision refers to an unselected field");
        if (!localCategoryId.HasValue && decisionsByFieldKey.Count > 0)
            return CategoryImportResult.Failure("Duplicate attribute decisions require an existing category");

        // Load existing category or create new one
        Category category;
        var attributeMap = new Dictionary<string, int>(); // externalFieldKey -> categoryAttributeId
        int sortOrder = 0;
        bool isExistingCategory = false;

        if (localCategoryId.HasValue)
        {
            // Load existing category with attributes
            var existingCat = await _context.categories
                .Include(c => c.attributes)
                .FirstOrDefaultAsync(c => c.Id == localCategoryId.Value, ct);

            if (existingCat == null)
                return CategoryImportResult.Failure("The specified local category was not found");

            category = existingCat;

            isExistingCategory = true;

            // Pre-populate attributeMap from existing attributes (sanitized key -> attribute ID)
            if (category.attributes != null)
            {
                foreach (var attr in category.attributes)
                {
                    var key = attr.key ?? attr.Id.ToString();
                    if (!attributeMap.ContainsKey(key))
                        attributeMap[key] = attr.Id;
                }
                sortOrder = category.attributes.Any()
                    ? category.attributes.Max(a => a.sortOrder) + 1
                    : 0;
            }

            localCategoryName = category.name;
        }
        else
        {
            localCategoryName = StringClip.ClipRequired(
                MarketplaceCategoryLabel.ForLocalCategory(
                    externalCategoryId, localCategoryName, marketplaceName),
                Category.CATEGORY_NAME_MAX_LENGTH,
                $"Category {externalCategoryId}");

            // Create the local category
            category = new Category();
            category.AddFill(new Category.AddForm
            {
                name = localCategoryName,
                storeId = connection.storeId
            });

            _context.categories.Add(category);
            await _context.SaveChangesAsync(ct);
        }

        // Resolve every duplicate choice against the category as it existed before this import.
        // This prevents an earlier newly-created field from becoming a false candidate for a
        // later field, and validates the whole request before tracking any new attributes.
        var existingAttributes = category.attributes?.ToList() ?? [];
        var duplicateCandidatesByFieldKey = new Dictionary<string, AttributeDuplicateCandidate[]>(StringComparer.Ordinal);
        var unitCodesByFieldKey = new Dictionary<string, string?>(StringComparer.Ordinal);
        var ambiguousFieldKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fieldsForAttributes)
        {
            var unitCode = AttributeImportSemantics.ResolveType(field) == AttributeType.Number
                ? AttributeFieldConstraints.ResolveUnitCode(field)
                : null;
            unitCodesByFieldKey[field.Key] = unitCode;
            var duplicateCandidates = AttributeDuplicateMatcher.FindCandidates(
                field,
                AttributeImportSemantics.ResolveType(field),
                unitCode,
                existingAttributes, allowConvertibleUnits: true).ToArray();
            duplicateCandidatesByFieldKey[field.Key] = duplicateCandidates;

            decisionsByFieldKey.TryGetValue(field.Key, out var decision);
            if (decision == null && duplicateCandidates.Length > 0)
            {
                if (requireDuplicateDecisions)
                {
                    return CategoryImportResult.Failure(
                        $"Choose whether to reuse, create, or skip an attribute for field '{field.Key}'");
                }

                // Old queued imports cannot ask for a choice. Leave close candidates unwired.
                if (duplicateCandidates.Length > 1 && duplicateCandidates[0].Score - duplicateCandidates[1].Score < 5)
                {
                    ambiguousFieldKeys.Add(field.Key);
                    continue;
                }

                // Preserve unambiguous already-enqueued legacy imports from before the choice UI existed.
                decision = new CategoryImportAttributeDecision
                {
                    fieldKey = field.Key,
                    action = "reuse",
                    existingAttributeId = duplicateCandidates[0].Attribute.Id,
                };
                decisionsByFieldKey[field.Key] = decision;
            }

            if (decision == null)
                continue;

            var action = decision.action?.Trim().ToLowerInvariant();
            var isValid = action switch
            {
                "reuse" => decision.existingAttributeId.HasValue
                    && duplicateCandidates.Any(candidate => candidate.Attribute.Id == decision.existingAttributeId.Value),
                "create" or "skip" => decision.existingAttributeId == null,
                _ => false,
            };
            if (!isValid)
            {
                return CategoryImportResult.Failure(
                    $"The duplicate attribute decision for field '{field.Key}' is invalid or no longer compatible");
            }
        }

        externalCategoryName = StringClip.ClipRequired(
            MarketplaceCategoryLabel.FirstReal(
                externalCategoryId, marketplaceName, localCategoryName),
            CategoryMapping.EXTERNAL_CATEGORY_NAME_MAX_LENGTH,
            localCategoryName);
        var mappingName = StringClip.ClipOptional(
            MarketplaceCategoryLabel.FirstReal(
                externalCategoryId, marketplaceName, localCategoryName),
            CategoryMapping.NAME_MAX_LENGTH);

        // Create category attributes only from additional marketplace fields
        // (excluding base product fields: sku, barcode, name, description, price, stock)
        int attributesCreated = 0;
        var pendingAttributes = new List<(ExternalField Field, CategoryAttribute Attribute, string SanitizedKey)>();
        var skippedAttributeFieldKeys = new HashSet<string>(ambiguousFieldKeys, StringComparer.Ordinal);

        foreach (var field in fieldsForAttributes.OrderBy(f => f.SortOrder))
        {
            if (ambiguousFieldKeys.Contains(field.Key))
                continue;
            var sanitizedKey = SanitizeKey(field.Key);
            var unitCode = unitCodesByFieldKey[field.Key];
            var importedName = AttributeImportSemantics.NormalizeName(field, unitCode);
            var importType = AttributeImportSemantics.ResolveType(field);

            var duplicateCandidates = duplicateCandidatesByFieldKey[field.Key];
            decisionsByFieldKey.TryGetValue(field.Key, out var decision);

            if (decision != null)
            {
                switch (decision.action.Trim().ToLowerInvariant())
                {
                    case "reuse":
                    {
                        var selectedCandidate = duplicateCandidates.FirstOrDefault(candidate =>
                            candidate.Attribute.Id == decision.existingAttributeId);
                        if (selectedCandidate == null)
                        {
                            return CategoryImportResult.Failure(
                                $"The selected existing attribute is no longer a compatible duplicate for field '{field.Key}'");
                        }

                        attributeMap[field.Key] = selectedCandidate.Attribute.Id;
                        attributeMap[sanitizedKey] = selectedCandidate.Attribute.Id;
                        continue;
                    }
                    case "create" when decision.existingAttributeId == null:
                        break;
                    case "skip" when decision.existingAttributeId == null:
                        skippedAttributeFieldKeys.Add(field.Key);
                        continue;
                    default:
                        return CategoryImportResult.Failure(
                            $"The duplicate attribute decision for field '{field.Key}' is invalid");
                }
            }

            // Determine the default value for certain field types
            string? defaultValue = null;
            if (importType != AttributeType.File && field.Options != null && field.Options.Count > 0)
            {
                defaultValue = DetermineDefaultValue(field);
            }

            var addForm = new CategoryAttribute.AddForm
            {
                name = StringClip.ClipRequired(
                    importedName,
                    CategoryAttribute.ATTRIBUTE_NAME_MAX_LENGTH,
                    sanitizedKey),
                key = sanitizedKey,
                type = importType,
                allowMultipleFiles = AttributeImportSemantics.AllowsMultipleFiles(field),
                unitCode = importType == AttributeType.Number
                    ? AttributeUnits.NormalizeCode(field.UnitCode ?? field.Unit)
                    : null,
                isRequired = false,
                sortOrder = sortOrder++,
                options = importType != AttributeType.File && field.Options != null ? SerializeOptions(field.Options) : null,
                defaultValue = StringClip.ClipOptional(
                    defaultValue,
                    CategoryAttribute.ATTRIBUTE_DEFAULT_MAX_LENGTH),
                description = StringClip.ClipOptional(
                    field.Description,
                    CategoryAttribute.ATTRIBUTE_DESCRIPTION_MAX_LENGTH),
                descriptionInTooltip = !string.IsNullOrWhiteSpace(field.Description),
                group = StringClip.ClipOptional(
                    adapter.DisplayName,
                    CategoryAttribute.ATTRIBUTE_GROUP_MAX_LENGTH),
                groupIcon = StringClip.ClipOptional(
                    AttributeGroupIconValue.ForAdapter(adapter.Key),
                    CategoryAttribute.ATTRIBUTE_GROUP_ICON_MAX_LENGTH),
                categoryId = category.Id,
                showOnProductPage = true,
            };
            AttributeFieldConstraints.ApplyTo(ref addForm, field);

            var attribute = new CategoryAttribute();
            attribute.AddFill(addForm);

            _context.categoryAttributes.Add(attribute);
            category.attributes ??= new List<CategoryAttribute>();
            category.attributes.Add(attribute);
            pendingAttributes.Add((field, attribute, sanitizedKey));
        }

        if (pendingAttributes.Count > 0)
            await _context.SaveChangesAsync(ct);

        foreach (var (field, attribute, sanitizedKey) in pendingAttributes)
        {
            attributeMap[field.Key] = attribute.Id;
            if (sanitizedKey != field.Key)
                attributeMap[sanitizedKey] = attribute.Id;
            attributesCreated++;
        }

        // Reuse only the mapping for this exact external category. One local category
        // may map to several marketplace types (per-link categoryMappingId).
        var categoryMapping = await _context.categoryMappings
            .FirstOrDefaultAsync(m =>
                m.localCategoryId == category.Id
                && m.connectionId == connection.Id
                && m.externalCategoryId == externalCategoryId, ct);

        // Remove meta-field from processing (plugin may still emit _hasSizes)
        fields = fields.Where(f => f.Key != "_hasSizes").ToList();

        bool reusingMapping = categoryMapping != null;
        int updatedFieldMappings = 0;

        if (categoryMapping != null)
        {
            categoryMapping.externalCategoryName = externalCategoryName ?? localCategoryName;
            if (string.IsNullOrWhiteSpace(categoryMapping.name)
                || MarketplaceCategoryLabel.IsSynthetic(categoryMapping.name, externalCategoryId))
            {
                categoryMapping.name = mappingName;
            }
        }
        else
        {
            categoryMapping = new CategoryMapping();
            categoryMapping.AddFill(new CategoryMapping.AddForm
            {
                connectionId = connection.Id,
                localCategoryId = category.Id,
                externalCategoryId = externalCategoryId,
                externalCategoryName = externalCategoryName ?? localCategoryName,
                name = mappingName
            });
            _context.categoryMappings.Add(categoryMapping);
        }

        // The node graph is the single source of truth - generate it for a new mapping or additively
        // sync new marketplace fields into an existing one (never prunes user config).
        var specs = fields
            .OrderBy(f => f.SortOrder)
            .Select(field =>
            {
                var (sourceType, sourceField, sourceAttributeId) =
                    unselectedAttributeFieldKeys.Contains(field.Key)
                    || skippedAttributeFieldKeys.Contains(field.Key)
                        ? (FieldMappingSourceType.Static, null, (int?)null)
                        : FieldAutoMapping.Resolve(field, category.attributes?.ToList() ?? [],
                            attributeMap.TryGetValue(field.Key, out var id) ? id : null);
                return new FieldSpec
                {
                    externalFieldKey = field.Key,
                    externalFieldName = field.Name,
                    externalFieldGroup = field.Group,
                    modifiersJson = FieldAutoMapping.Conversion(field, category.attributes?.FirstOrDefault(a => a.Id == sourceAttributeId)),
                    externalFieldType = field.Type,
                    externalFieldUnit = AttributeFieldConstraints.ResolveUnitLabel(field),
                    externalFieldUnitCode = AttributeFieldConstraints.ResolveUnitCode(field),
                    externalFieldUnitId = field.UnitId,
                    // Product-field-fed columns are not required (data comes from the local product).
                    isRequired = sourceType != FieldMappingSourceType.ProductField && field.IsRequired,
                    isEnabled = true,
                    sortOrder = field.SortOrder,
                    sourceType = sourceType,
                    sourceField = sourceField,
                    sourceAttributeId = sourceAttributeId,
                };
            })
            .ToList();

        // Every mapping carries a grouping field (synthetic for endpoint-based adapters)
        GraphGenerator.EnsureGroupingField(specs, adapter);

        var existingKeys = MappingService.SchemaFields(categoryMapping.graphJson)
            .Select(f => f.fieldKey)
            .Where(k => !string.IsNullOrEmpty(k))
            .ToHashSet();
        int newFieldMappings = specs.Count(s => !existingKeys.Contains(s.externalFieldKey));

        List<FieldSpec> priceSpecs = new();
        if (adapter.Capabilities.HasFlag(MarketplaceCapabilities.ExportPrices))
        {
            try
            {
                var priceFields = await adapter.GetPriceFieldsAsync(connection, externalCategoryId, ct);
                var extendedPrice = await _context.stores.AsNoTracking()
                    .Where(s => s.Id == connection.storeId)
                    .Select(s => s.extendedPriceParameters)
                    .FirstOrDefaultAsync(ct);
                priceSpecs = BuildPriceFieldSpecs(priceFields, attributeMap, extendedPriceParameters: extendedPrice);
            }
            catch
            {
                // Non-critical - card mapping still works without price block
            }
        }

        categoryMapping.graphJson = reusingMapping
            ? GraphGenerator.SyncPriceFields(
                GraphGenerator.SyncAdapterFields(categoryMapping.graphJson, specs),
                priceSpecs)
            : GraphGenerator.BuildGraphJson(specs, priceSpecs);

        await _context.SaveChangesAsync(ct);

        var message = reusingMapping
            ? $"Added {attributesCreated} attributes and {newFieldMappings} fields to mapping '{localCategoryName}'"
              + (updatedFieldMappings > 0 ? $", updated {updatedFieldMappings} mappings" : "")
            : isExistingCategory
                ? $"Added {attributesCreated} attributes to category '{localCategoryName}' (+ {fields.Count - fieldsForAttributes.Count} base product fields)"
                : $"Category '{localCategoryName}' imported successfully with {attributesCreated} attributes (+ {fields.Count - fieldsForAttributes.Count} base product fields)";

        return CategoryImportResult.Success(
            categoryMapping.Id,
            category.Id,
            attributesCreated,
            message
        );
    }

    /// <summary>
    /// Determine automatic field mapping based on field key/name patterns.
    /// </summary>
    internal static (FieldMappingSourceType sourceType, string? sourceField, int? sourceAttributeId)
        DetermineAutoMapping(ExternalField field, Dictionary<string, int> attributeMap)
    {
        var keyLower = field.Key.ToLowerInvariant();
        var nameLower = field.Name.ToLowerInvariant();

        // Dictionary and aspect characteristics must not be inferred as product fields.
        // Grouping is a separate adapter capability and remains eligible below.
        var characteristic = IsCharacteristicField(field);
        var fieldType = MapExternalFieldType(field.Type);
        var textCompatible = fieldType is AttributeType.Text or AttributeType.Barcode;
        var numberCompatible = fieldType is AttributeType.Number or AttributeType.Text;
        if (!characteristic && textCompatible && IsSkuField(keyLower, nameLower))
            return (FieldMappingSourceType.ProductField, "sku", null);

        if (!characteristic && textCompatible && IsBarcodeField(field))
            return (FieldMappingSourceType.ProductField, "barcode", null);

        if (!characteristic && fieldType == AttributeType.Text && IsNameField(keyLower, nameLower))
            return (FieldMappingSourceType.ProductField, "name", null);

        if (!characteristic && fieldType == AttributeType.Text && IsDescriptionField(keyLower, nameLower))
            return (FieldMappingSourceType.ProductField, "description", null);

        if (!characteristic && numberCompatible && IsPriceField(keyLower, nameLower))
            return (FieldMappingSourceType.ProductField, "price", null);

        if (!characteristic && numberCompatible && IsStockField(keyLower, nameLower))
            return (FieldMappingSourceType.ProductField, "storageQuantity", null);

        // Product grouping fields are mapped to the group name
        if (IsGroupingField(field.Key, field.Name))
            return (FieldMappingSourceType.GroupName, null, null);

        // Image fields are mapped to the product's images
        if (!characteristic && IsImageField(field.Key, field.Name))
            return (FieldMappingSourceType.ProductField, "images", null);

        // WB rejects this characteristic (HTTP 400) and reads packaging weight only
        // from dimensions.weightBrutto, in kilograms. Leave the row unwired.
        if (IsWbPackagingWeightCharacteristic(field.Key, field.Name))
            return (FieldMappingSourceType.Static, null, null);

        // By default, map to the created category attribute
        if (attributeMap.TryGetValue(field.Key, out var attributeId))
            return (FieldMappingSourceType.CategoryAttribute, null, attributeId);

        // If nothing matched - a static empty value
        return (FieldMappingSourceType.Static, null, null);
    }

    /// <summary>
    /// Check whether a field is a base product field (SKU, barcode, name, description, price, stock, images, grouping).
    /// These fields don't need attribute creation - they are handled by product fields or special mappings.
    /// </summary>
    internal static bool IsBaseOrSpecialField(ExternalField field) =>
        DetermineAutoMapping(field, new Dictionary<string, int>()).sourceType != FieldMappingSourceType.Static;

    private static bool IsCharacteristicField(ExternalField field)
    {
        if (field.IsAspect || field.Options is { Count: > 0 } ||
            field.Type.Trim().ToLowerInvariant() is "select" or "multiselect" or "enum" or "dictionary")
            return true;

        // A product-field word inside a characteristic name is not enough evidence
        // to bind a base field (for example, "SKU color" or "description length").
        var tokens = (field.Key + " " + field.Name).ToLowerInvariant()
            .Split([' ', '_', '-', ':', '.', '/', '(', ')'], StringSplitOptions.RemoveEmptyEntries);
        return tokens.Any(token => token is "color" or "colour" or "size" or "length" or "width" or "height"
            or "цвет" or "размер" or "длина" or "ширина" or "высота"
            || token.StartsWith("characteristic", StringComparison.Ordinal)
            || token.StartsWith("характеристик", StringComparison.Ordinal));
    }

    /// <summary>
    /// Wildberries characteristic «Вес товара с упаковкой». Since 2025-03-31 the
    /// cards API returns HTTP 400 if this object is in <c>characteristics</c>.
    /// The value belongs on the card <c>weight</c> field (kilograms).
    /// Weight without packaging is a different characteristic and stays mapped.
    /// </summary>
    internal static bool IsWbPackagingWeightCharacteristic(string key, string? name)
    {
        if (key.Equals("charc:88952", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.IsNullOrWhiteSpace(name) || !key.StartsWith("charc:", StringComparison.OrdinalIgnoreCase))
            return false;
        return PackagingWeightName(name);
    }

    internal static bool PackagingWeightName(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("без упаков") || n.Contains("without packaging"))
            return false;
        return (n.Contains("вес") && n.Contains("упаков"))
            || (n.Contains("weight") && n.Contains("packag"));
    }

    /// <summary>
    /// Adapter barcode / EAN / GTIN fields map to the product barcode, not a category attribute.
    /// </summary>
    internal static bool IsBarcodeField(ExternalField field)
    {
        if (string.Equals(field.Type, "barcode", StringComparison.OrdinalIgnoreCase))
            return true;
        return IsBarcodeField(field.Key.ToLowerInvariant(), field.Name.ToLowerInvariant());
    }

    internal static bool IsBarcodeField(string key, string name)
    {
        var exact = new[] { "barcode", "barcodes", "ean", "ean8", "ean13", "gtin", "upc" };
        if (exact.Any(p => key == p || name == p))
            return true;

        var contains = new[]
        {
            "barcode",
            "gtin",
            "\u0448\u0442\u0440\u0438\u0445\u043a\u043e\u0434",
            "\u0448\u0442\u0440\u0438\u0445-\u043a\u043e\u0434",
        };
        return contains.Any(p => key.Contains(p) || name.Contains(p));
    }

    internal static bool IsSkuField(string key, string name)
    {
        // Ozon attribute 9024 = SKU/Article
        if (key == "9024") return true;


        var patterns = new[] { "sku", "\u0430\u0440\u0442\u0438\u043a\u0443\u043b", "article", "vendor_code", "vendorcode" };
        return patterns.Any(p => key.Contains(p) || name.Contains(p));
    }

    internal static bool IsNameField(string key, string name)
    {
        // Ozon attribute 4180 = Product name
        if (key == "4180") return true;

        // Use Contains for Russian patterns, but only exact ones - "product name", not just "name"
        // (otherwise "Color name" 10097, "Group name" 22390, etc. would also match)
        var exactPatterns = new[] { "name", "title" };
        var containsPatterns = new[] { "\u043d\u0430\u0437\u0432\u0430\u043d\u0438\u0435\u0020\u0442\u043e\u0432\u0430\u0440\u0430", "\u043d\u0430\u0438\u043c\u0435\u043d\u043e\u0432\u0430\u043d\u0438\u0435" };
        var compactKey = key.Replace("-", "", StringComparison.Ordinal);

        return compactKey is "productname" or "itemname" ||
               exactPatterns.Any(p => key == p || name == p || key.StartsWith(p + "_") || key.EndsWith("_" + p)) ||
               containsPatterns.Any(p => name.Contains(p));
    }

    internal static bool IsDescriptionField(string key, string name)
    {
        // Ozon attribute 4191 = Annotation (Description)
        if (key == "4191") return true;

        // "annotation" and "description" are the same thing
        var patterns = new[] { "description", "\u043e\u043f\u0438\u0441\u0430\u043d\u0438\u0435", "\u0430\u043d\u043d\u043e\u0442\u0430\u0446\u0438\u044f", "annotation" };
        return key == "desc" || name == "desc" || patterns.Any(p => key.Contains(p) || name.Contains(p));
    }

    internal static bool IsPriceField(string key, string name)
    {
        var patterns = new[] { "price", "\u0446\u0435\u043d\u0430", "\u0441\u0442\u043e\u0438\u043c\u043e\u0441\u0442\u044c", "cost" };
        return patterns.Any(p => key == p || name == p || key.StartsWith(p + "_"));
    }

    internal static List<FieldSpec> BuildPriceFieldSpecs(
        IReadOnlyCollection<ExternalField> priceFields,
        Dictionary<string, int> attributeMap,
        bool skipAutoMapping = false,
        bool extendedPriceParameters = false)
    {
        var visible = extendedPriceParameters
            ? priceFields
            : priceFields.Where(f => !IsStoreExtendedPriceKey(f.Key));

        return visible
            .OrderBy(f => f.SortOrder)
            .Select((field, index) =>
            {
                var (sourceType, sourceField, sourceAttributeId) = skipAutoMapping
                    ? (FieldMappingSourceType.Static, (string?)null, (int?)null)
                    : DeterminePriceAutoMapping(field, attributeMap, extendedPriceParameters);
                return new FieldSpec
                {
                    externalFieldKey = field.Key,
                    externalFieldName = field.Name,
                    externalFieldType = field.Type,
                    externalFieldUnit = AttributeFieldConstraints.ResolveUnitLabel(field),
                    externalFieldUnitCode = AttributeFieldConstraints.ResolveUnitCode(field),
                    externalFieldUnitId = field.UnitId,
                    externalFieldGroup = field.Group,
                    externalFieldDescription = field.Description,
                    isRequired = sourceType != FieldMappingSourceType.ProductField && field.IsRequired,
                    isEnabled = true,
                    sortOrder = index,
                    sourceType = sourceType,
                    sourceField = sourceField,
                    sourceAttributeId = sourceAttributeId,
                    staticValue = sourceType == FieldMappingSourceType.Static && field.Key.Equals("discount", StringComparison.OrdinalIgnoreCase)
                        ? "0"
                        : null,
                };
            })
            .ToList();
    }

    /// <summary>
    /// Price-API keys that exist only when the store has extended price settings
    /// (the product form's discounted price). Marketplace base, old price, and discount stay.
    /// </summary>
    internal static bool IsStoreExtendedPriceKey(string? key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        return key.Equals("discountedPrice", StringComparison.OrdinalIgnoreCase)
            || key.Equals("sale_price", StringComparison.OrdinalIgnoreCase)
            || key.Equals("salePrice", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Price-API keys with a fixed semantic target - AI must not guess substitutes.</summary>
    internal static bool IsStrictPriceSemanticKey(string key)
    {
        return key.Equals("price", StringComparison.OrdinalIgnoreCase)
            || key.Equals("oldPrice", StringComparison.OrdinalIgnoreCase)
            || key.Equals("minPrice", StringComparison.OrdinalIgnoreCase)
            || key.Equals("netPrice", StringComparison.OrdinalIgnoreCase)
            || key.Equals("discount", StringComparison.OrdinalIgnoreCase)
            || key.Equals("discountedPrice", StringComparison.OrdinalIgnoreCase)
            || key.Equals("sale_price", StringComparison.OrdinalIgnoreCase)
            || key.Equals("salePrice", StringComparison.OrdinalIgnoreCase);
    }

    internal static (FieldMappingSourceType sourceType, string? sourceField, int? sourceAttributeId)
        DeterminePriceAutoMapping(ExternalField field, Dictionary<string, int> attributeMap, bool extendedPriceParameters = false)
    {
        var keyLower = field.Key.ToLowerInvariant();

        if (keyLower == "price")
            return (FieldMappingSourceType.ProductField, "price", null);

        if (extendedPriceParameters)
        {
            return keyLower switch
            {
                "oldprice" => (FieldMappingSourceType.ProductField, "oldPrice", null),
                "minprice" => (FieldMappingSourceType.ProductField, "minPrice", null),
                "netprice" => (FieldMappingSourceType.ProductField, "netPrice", null),
                "discountedprice" or "sale_price" or "saleprice" => (FieldMappingSourceType.ProductField, "discountedPrice", null),
                "discount" => (FieldMappingSourceType.ProductField, "discount", null),
                "currency" or "currencyid" => (FieldMappingSourceType.ProductField, "currency", null),
                _ => (FieldMappingSourceType.Static, null, null)
            };
        }

        if (keyLower == "oldprice" && TryResolveAttribute(attributeMap, "old_price", "oldprice", "\u0441\u0442\u0430\u0440\u0430\u044f\u0020\u0446\u0435\u043d\u0430", out var oldId))
            return (FieldMappingSourceType.CategoryAttribute, null, oldId);

        if (keyLower == "minprice" && TryResolveAttribute(attributeMap, "min_price", "minprice", "\u043c\u0438\u043d\u0438\u043c\u0430\u043b\u044c\u043d\u0430\u044f\u0020\u0446\u0435\u043d\u0430", out var minId))
            return (FieldMappingSourceType.CategoryAttribute, null, minId);

        if (keyLower == "discount" && TryResolveAttribute(attributeMap, "discount", "discount_percent", "\u0441\u043a\u0438\u0434\u043a\u0430", out var discId))
            return (FieldMappingSourceType.CategoryAttribute, null, discId);

        if (keyLower == "netprice" && TryResolveAttribute(attributeMap, "net_price", "netprice", "\u0441\u0435\u0431\u0435\u0441\u0442\u043e\u0438\u043c\u043e\u0441\u0442\u044c", out var netId))
            return (FieldMappingSourceType.CategoryAttribute, null, netId);

        return (FieldMappingSourceType.Static, null, null);
    }

    /// <summary>
    /// Validates AI/deterministic price bindings. Rejects cross-wiring (e.g. minPrice → old_price attribute).
    /// </summary>
    internal static bool IsValidPriceBinding(
        ExternalField fieldDef,
        string? sourceType,
        string? sourceField,
        int? sourceAttributeId,
        Dictionary<string, int> attributeMap)
    {
        if (sourceType == null) return true;

        if (IsStoreExtendedPriceKey(fieldDef.Key))
        {
            return sourceType == "productField"
                && string.Equals(sourceField, "discountedPrice", StringComparison.OrdinalIgnoreCase);
        }

        if (!IsStrictPriceSemanticKey(fieldDef.Key))
            return true;

        var (expectedType, expectedField, expectedAttrId) = DeterminePriceAutoMapping(fieldDef, attributeMap, extendedPriceParameters: false);

        if (fieldDef.Key.Equals("discount", StringComparison.OrdinalIgnoreCase))
        {
            return sourceType == "static"
                || (sourceType == "attribute" && expectedAttrId != null && sourceAttributeId == expectedAttrId);
        }

        if (expectedType == FieldMappingSourceType.Static)
            return false;

        return expectedType switch
        {
            FieldMappingSourceType.ProductField =>
                sourceType == "productField"
                && string.Equals(sourceField, expectedField, StringComparison.OrdinalIgnoreCase),
            FieldMappingSourceType.CategoryAttribute =>
                sourceType == "attribute" && sourceAttributeId == expectedAttrId,
            _ => false,
        };
    }

    private static bool TryResolveAttribute(
        Dictionary<string, int> attributeMap,
        string primaryKey,
        string alternateKey,
        string? alternateName,
        out int attributeId)
    {
        if (attributeMap.TryGetValue(primaryKey, out attributeId))
            return true;
        if (attributeMap.TryGetValue(alternateKey, out attributeId))
            return true;
        if (alternateName != null)
        {
            foreach (var kvp in attributeMap)
            {
                if (kvp.Key.Equals(alternateName, StringComparison.OrdinalIgnoreCase))
                {
                    attributeId = kvp.Value;
                    return true;
                }
            }
        }
        attributeId = 0;
        return false;
    }

    internal static bool IsStockField(string key, string name)
    {
        var keyLower = key.ToLowerInvariant();
        var nameLower = name.ToLowerInvariant();

        // Exact match - these words clearly mean stock quantity
        var exactPatterns = new[] { "stock", "\u043e\u0441\u0442\u0430\u0442\u043e\u043a", "\u043e\u0441\u0442\u0430\u0442\u043a\u0438", "quantity", "count" };
        if (exactPatterns.Any(p => keyLower == p || nameLower == p))
            return true;

        // Prefix/suffix patterns for stock
        if (keyLower.StartsWith("stock_") || keyLower.EndsWith("_stock"))
            return true;

        // Do NOT use Contains for the generic word "quantity" - otherwise fields like "Figurine quantity" would match
        // Only match if it's explicitly a stock field
        return false;
    }

    /// <summary>
    /// Determine a default value for a field based on its name and available options.
    /// For example, for "Brand" fields looks for "No brand" options.
    /// </summary>
    internal static string? DetermineDefaultValue(ExternalField field)
    {
        if (field.Options == null || field.Options.Count == 0)
            return null;

        var nameLower = field.Name.ToLowerInvariant();

        // For "Brand" fields, look for special values
        if (nameLower.Contains("\u0431\u0440\u0435\u043d\u0434") || nameLower.Contains("brand"))
        {
            var noBrandPatterns = new[]
            {
                "\u043d\u0435\u0442\u0020\u0431\u0440\u0435\u043d\u0434\u0430",
                "\u0431\u0435\u0437\u0020\u0431\u0440\u0435\u043d\u0434\u0430",
                "\u043d\u0435\u0020\u0443\u043a\u0430\u0437\u0430\u043d",
                "\u043e\u0442\u0441\u0443\u0442\u0441\u0442\u0432\u0443\u0435\u0442",
                "no brand",
                "unbranded"
            };

            var noBrandOption = field.Options.FirstOrDefault(o =>
                noBrandPatterns.Any(p => o.Label.ToLowerInvariant().Contains(p)));

            if (noBrandOption != null)
                return noBrandOption.Value; // Return dictionary_value_id
        }

        // For "Country" fields, look for "Russia" as the default value
        if (nameLower.Contains("\u0441\u0442\u0440\u0430\u043d\u0430") || nameLower.Contains("country"))
        {
            var russiaOption = field.Options.FirstOrDefault(o =>
                o.Label.ToLowerInvariant().Contains("\u0440\u043e\u0441\u0441\u0438\u044f") ||
                o.Label.ToLowerInvariant() == "russia");

            if (russiaOption != null)
                return russiaOption.Value;
        }

        return null;
    }

    /// <summary>
    /// Check whether the field is a product grouping field.
    /// Such fields should not create attributes; they map to group name instead.
    /// </summary>
    internal static bool IsGroupingField(string key, string name)
    {
        // Ozon 9048 = "Model name (for merging into one card)"
        // Ozon 22390 = "Group name"
        if (key == "9048" || key == "22390") return true;

        var nameLower = name.ToLowerInvariant();

        // Patterns for detecting grouping fields
        if (nameLower.Contains("\u0434\u043b\u044f\u0020\u043e\u0431\u044a\u0435\u0434\u0438\u043d\u0435\u043d\u0438\u044f") ||
            nameLower.Contains("\u043e\u0431\u044a\u0435\u0434\u0438\u043d\u0435\u043d\u0438\u0435\u0020\u0432\u0020\u043a\u0430\u0440\u0442\u043e\u0447\u043a\u0443") ||
            nameLower.Contains("\u0433\u0440\u0443\u043f\u043f\u0438\u0440\u043e\u0432\u043a\u0430\u0020\u0442\u043e\u0432\u0430\u0440\u043e\u0432") ||
            nameLower.Contains("\u043d\u0430\u0437\u0432\u0430\u043d\u0438\u0435\u0020\u0433\u0440\u0443\u043f\u043f\u044b"))
            return true;

        return false;
    }

    /// <summary>
    /// Check whether the field is an image field.
    /// Image fields should not create attributes; they are handled separately.
    /// </summary>
    internal static bool IsImageField(string key, string name)
    {
        var keyLower = key.ToLowerInvariant();
        var nameLower = name.ToLowerInvariant();

        // Standard image field keys
        if (keyLower == "images" || keyLower == "primary_image" ||
            keyLower == "image_url" || keyLower == "image_urls" ||
            keyLower == "images360" || keyLower == "color_image")
            return true;

        // Name patterns for images
        var patterns = new[] { "\u0438\u0437\u043e\u0431\u0440\u0430\u0436\u0435\u043d", "\u0444\u043e\u0442\u043e", "\u043a\u0430\u0440\u0442\u0438\u043d\u043a", "image", "photo", "picture" };
        return patterns.Any(p => nameLower.Contains(p));
    }

    /// <summary>
    /// Determine attribute group by keywords in the name.
    /// Used as a fallback when the marketplace does not provide a group.
    /// </summary>
    private string? DetermineGroupByKeywords(string name)
    {
        var nameLower = name.ToLowerInvariant();

        // Size and weight
        var sizeWeightPatterns = new[] { "weight", "height", "width", "depth", "length", "size", "dimension" };
        if (sizeWeightPatterns.Any(p => nameLower.Contains(p)))
            return "Size and weight";

        // Appearance
        var appearancePatterns = new[] { "color", "colour", "material", "shade", "tone", "coating", "texture" };
        if (appearancePatterns.Any(p => nameLower.Contains(p)))
            return "Appearance";

        // Media (usually filtered by IsImageField, but just in case)
        var mediaPatterns = new[] { "image", "photo", "video", "picture", "media", "preview" };
        if (mediaPatterns.Any(p => nameLower.Contains(p)))
            return "Media";

        // Packaging
        var packagePatterns = new[] { "package", "box", "kit", "set", "pack", "bundle" };
        if (packagePatterns.Any(p => nameLower.Contains(p)))
            return "Packaging";

        // Labeling
        var labelPatterns = new[] { "ean", "barcode", "sku", "code", "gtin", "upc", "isbn", "label" };
        if (labelPatterns.Any(p => nameLower.Contains(p)))
            return "Labeling";

        // Brand and manufacturer
        var brandPatterns = new[] { "brand", "manufacturer", "producer", "trademark" };
        if (brandPatterns.Any(p => nameLower.Contains(p)))
            return "Brand";

        // Country
        var countryPatterns = new[] { "country", "origin" };
        if (countryPatterns.Any(p => nameLower.Contains(p)))
            return "Country";

        // Could not determine the group - return null (displayed as "Other" in the UI)
        return null;
    }

    /// <summary>
    /// Sanitize an attribute key by removing special characters.
    /// </summary>
    internal static string SanitizeKey(string key)
    {
        // Keep only letters, digits, and underscores
        var sanitized = new string(key.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());

        // Limit the length
        if (sanitized.Length > CategoryAttribute.ATTRIBUTE_KEY_MAX_LENGTH)
            sanitized = sanitized.Substring(0, CategoryAttribute.ATTRIBUTE_KEY_MAX_LENGTH);

        // If empty - generate a random one
        if (string.IsNullOrEmpty(sanitized))
            sanitized = $"attr_{Guid.NewGuid().ToString("N").Substring(0, 8)}";

        return sanitized;
    }

    /// <summary>
    /// Map an external field type string to a local <see cref="AttributeType"/> enum value.
    /// </summary>
    internal static AttributeType MapExternalFieldType(string externalType)
    {
        return externalType.Trim().ToLowerInvariant() switch
        {
            "number" or "integer" or "decimal" or "float" => AttributeType.Number,
            "boolean" or "bool" => AttributeType.Boolean,
            "select" or "enum" => AttributeType.Select,
            "multiselect" or "multi" or "array" => AttributeType.MultiSelect,
            "barcode" => AttributeType.Barcode,
            "file" or "files" or "attachment" or "attachments" => AttributeType.File,
            _ => AttributeType.Text
        };
    }

    /// <summary>
    /// Serialize field options to JSON format.
    /// Format: [{"value":"123","label":"Option 1"}, ...].
    /// </summary>
    private string SerializeOptions(List<ExternalFieldOption> options)
    {
        var optionsList = options.Select(o => new { value = o.Value, label = o.Label }).ToList();
        return JsonSerializer.Serialize(optionsList);
    }
}

/// <summary>
/// Result of a category import operation.
/// </summary>
public class CategoryImportResult
{
    public bool IsSuccess { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int? CategoryMappingId { get; private set; }
    public int? LocalCategoryId { get; private set; }
    public int AttributesCreated { get; private set; }
    public string? Message { get; private set; }

    /// <summary>
    /// Create a successful result.
    /// </summary>
    public static CategoryImportResult Success(int categoryMappingId, int localCategoryId, int attributesCreated, string message)
    {
        return new CategoryImportResult
        {
            IsSuccess = true,
            CategoryMappingId = categoryMappingId,
            LocalCategoryId = localCategoryId,
            AttributesCreated = attributesCreated,
            Message = message
        };
    }

    /// <summary>
    /// Create a failure result with an error message.
    /// </summary>
    public static CategoryImportResult Failure(string errorMessage)
    {
        return new CategoryImportResult
        {
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
    }
}
