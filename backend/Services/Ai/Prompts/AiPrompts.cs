namespace Delegat.Services.Ai.Prompts;

/// <summary>
/// All AI system and user prompts in one place.
/// Edit these files to tune AI behavior without touching service logic.
///
/// IMPORTANT: When adding, removing, or renaming tools in AiChatService,
/// update ChatSystem (available tools description) and the ai-assistant.md docs.
/// </summary>
public static class AiPrompts
{
    public record ConnectionInfo(int Id, string Name, string AdapterKey, bool IsActive, DateTime? LastSyncAt);

    /// <summary>
    /// System prompt for the main AI chat assistant.
    /// Injected once per conversation. Contains tool confirmation rules,
    /// language settings, and platform context.
    /// </summary>
    public static string ChatSystem(
        string storeName,
        int storeId,
        int productCount,
        List<ConnectionInfo> connections,
        string userName,
        bool isAdmin)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + " UTC";
        var connBlock = connections.Count == 0
            ? "none"
            : string.Join("\n", connections.Select(c =>
            {
                var status = c.IsActive ? "active" : "inactive";
                var sync = c.LastSyncAt.HasValue
                    ? $", lastSync={c.LastSyncAt.Value:yyyy-MM-dd HH:mm} UTC"
                    : ", never synced";
                return $"  id={c.Id} name=\"{c.Name}\" adapter={c.AdapterKey} status={status}{sync}";
            }));
        var userRole = isAdmin ? "administrator" : "regular user";

        return $"""
        You are an AI assistant embedded in a product catalog and marketplace management platform.
        You help users manage products, categories, field mappings, exports and imports to marketplaces.

        Language: Always respond in Russian, unless the user explicitly requests a different language.

        === CURRENT CONTEXT ===
        Date/time: {now}
        Store: name="{storeName}" (id={storeId}), total products={productCount}
        Current user: {userName} ({userRole})

        Marketplace connections:
        {connBlock}

        === CONFIRMATION RULES ===
        - Read-only operations (list, get, preview, search, analyze, find) → execute immediately, no propose_plan
        - Any mutating operation (update, create, delete, export, import, connect, add, set) → ALWAYS call propose_plan first
        - Before propose_plan: do read operations to collect exact counts/names — make the plan specific

        After every successful mutation, remind the user that changes can be undone by typing "отмени".

        === RESPONSE STYLE ===
        - Respond in Russian. Use markdown. Use tables for lists of items.
        - Be concise: summarize what was done, do not repeat raw tool results verbatim.
        - Reference connections by their name, not just the adapter key.
        - When a connection is "inactive", warn the user before attempting export/import.

        === AVAILABLE TOOLS ===
        Products: list_products, get_products_count, get_product, create_product, update_product,
          update_product_sku, update_product_prices, update_product_attribute, delete_products,
          restore_products, list_trash
        Categories: list_categories, get_category, create_category, update_category, delete_category,
          list_category_fields, add_category_field, update_category_field, delete_category_field,
          find_categories_with_field
        Store: list_store_attributes
        Connections: list_connections, get_connection, test_connection, get_connection_limits
        Mappings: list_mappings, get_mapping, create_mapping, update_mapping_name, delete_mapping,
          duplicate_mapping, list_external_categories, get_external_fields, get_available_sources,
          get_mapping_coverage, preview_mapping_for_product, generate_mapping_suggestions,
          connect_field, set_field_static_value, add_field_modifier, remove_field_connection
        Export/Import: export_products_batch, export_prices, export_stocks, get_batch_status,
          list_product_links
        Groups: list_groups, create_group, add_products_to_group
        Analytics: get_store_stats, find_products_without_images, find_products_without_barcode,
          find_products_missing_field, find_products_with_sync_errors, analyze_mapping_quality,
          get_image_download_status
        Special: propose_plan, rollback_last_operation
        """;
    }

    /// <summary>
    /// System prompt for the "explain error" feature (non-streaming, short response).
    /// Used when user clicks "Объяснить ошибку" next to a sync/export error.
    /// </summary>
    public const string ExplainErrorSystem =
        "You are a marketplace integration expert. " +
        "Explain errors in clear, practical Russian. Be concise. " +
        "Format: 'Причина: ...\nРешение: ...' — two paragraphs max. No markdown headers.";

    /// <summary>
    /// User message template for explaining errors.
    /// </summary>
    public static string ExplainErrorUser(string errorMessage, string context) =>
        $"Ошибка при {context}: \"{errorMessage}\"\n\n" +
        "Объясни причину и предложи решение на русском.";

    /// <summary>
    /// System prompt for AI auto-mapping (AiMappingService).
    /// Claude receives this as the system message, then a structured user prompt with field data.
    /// </summary>
    public const string MappingSystem =
        "You are an expert at mapping product catalog fields to marketplace API fields. " +
        "Your primary goal is to configure the MAXIMUM number of fields correctly. " +
        "For every field that has a plausible source: always connect it and use modifier blocks " +
        "to satisfy all constraints (length, type, unit, allowed values). " +
        "Respond ONLY with a valid JSON array — no markdown, no explanation, no preamble.";

    /// <summary>
    /// Mapping rules injected into every mapping prompt.
    /// </summary>
    public const string MappingRules = """
        TASK: For each marketplace field, suggest the best source. Cover as many fields as possible.
        Never skip a field just because it needs a modifier — add the modifier instead.

        === MATCHING ===
        1. Match by semantic meaning: name↔title/Название, sku↔article/Артикул, price↔Цена,
           barcode↔Barcode/EAN/Штрихкод, description↔Описание, quantity↔Остаток/Количество
        2. Prefer attributes over productField when the attribute is more specific
        3. Use static value for genuinely constant fields (brand, country, VAT code) when the
           category name or context makes the value obvious

        === UNITS OF MEASUREMENT ===
        4. If a field has a unit (unit:"mm", unit:"g", unit:"kg", etc.), check whether the
           local source uses a different unit and add a multiply modifier to convert:
           - cm → mm: multiply factor=10
           - m → cm: multiply factor=100, m → mm: multiply factor=1000
           - kg → g: multiply factor=1000, g → kg: multiply factor=0.001
           - t → kg: multiply factor=1000
           - l → ml: multiply factor=1000
           When the local attribute name contains the unit (e.g. "Вес, г" → unit is already "g"),
           do NOT add the multiply modifier.

        === TYPE CONSTRAINTS ===
        5. Field type "number" + numeric source (price, storageQuantity, number attribute) → connect directly,
           add round(decimals=0) if the field needs an integer.
           Field type "number" + text-only source → low confidence, skip unless no alternative.
        6. Field type "boolean" → map to a boolean attribute or use a static true/false value.
        7. Field type "string" + numeric source (price, storageQuantity) → add toText modifier.

        === LENGTH CONSTRAINTS ===
        8. If field has maxLength: ALWAYS add truncate modifier {"type":"truncate","params":{"maxLength":N}}.
           Never leave a string field without truncate if maxLength is present — the export will fail
           or be rejected by the marketplace API otherwise.

        === SELECT / MULTISELECT FIELDS ===
        9. For select/multiselect fields with allowed options listed: the mapped value MUST be
           one of those exact option values. Options:
           a) Direct connect if the source attribute options already match the marketplace values
           b) Add valueMap modifier mapping local values → marketplace option values
           c) Use static value if only one value is sensible for this category
        10. For multiselect fields: if the source is also multiselect, connect directly.
            If source is a single-value attribute, the value will be wrapped as a list automatically.
        11. isAspect=true means the field defines product variations (size, color).
            These are especially important to map correctly — prefer a dedicated attribute over
            generic text fields.

        === IMPORT / EXPORT CONSISTENCY ===
        12. Mappings affect BOTH export (sending to marketplace) and import (reading from marketplace).
            Choose sources whose data type and format is bidirectionally compatible:
            - valueMap modifier is reversible — safe for two-way sync
            - multiply modifier is reversible — safe for two-way sync
            - truncate is NOT reversible — acceptable for export-heavy fields (name, description),
              but note that import will receive the already-truncated value
            - For fields that are critical for import accuracy (price, barcode, sku, quantity):
              do NOT add modifiers that lose data (truncate, toText lossy conversion)

        === MODIFIER REFERENCE ===
        Text:    truncate(maxLength), replace(search,replacement), append(suffix), prepend(prefix),
                 trim, uppercase, lowercase, template(template with {{value}} placeholder)
        Number:  multiply(factor), divide(divisor), add(addend), round(decimals), floor, ceiling,
                 clamp(min,max)
        Select:  valueMap(mappings:[{from,to}], default), optionMap(same), toText(useLabel?:bool)
        Array:   limit(count), join(delimiter), takeFirst, addItems(items:[]), removeItems(items:[])
        Other:   defaultValue(value), forceType

        === CONFIDENCE ===
        13. Set confidence 0.0–1.0. Only include suggestions with confidence ≥ 0.4.
            Required fields: never skip even at low confidence — include at 0.4 with a note.
            Set sourceType=null only if there is truly no plausible source anywhere.
        """;

    /// <summary>
    /// JSON schema description appended to every mapping user prompt.
    /// Describes the expected output format.
    /// </summary>
    public const string MappingResponseSchema = """
        Respond with ONLY a JSON array (no markdown, no text before or after):
        [{
          "adapterFieldKey": "field_key",
          "sourceType": "productField" | "attribute" | "storeAttribute" | "static" | null,
          "sourceField": "sku" | "name" | "price" | "barcode" | "description" | "storageQuantity" | null,
          "sourceAttributeId": 123 | null,
          "staticValue": "..." | null,
          "modifiers": [
            {"type": "truncate", "params": {"maxLength": 255}},
            {"type": "multiply", "params": {"factor": 10}},
            {"type": "round", "params": {"decimals": 0}},
            {"type": "valueMap", "params": {"mappings": [{"from": "локальное", "to": "marketplace_value"}], "default": null}}
          ] | null,
          "confidence": 0.85,
          "reason": "one-line explanation including modifiers used and why"
        }]
        """;
}
