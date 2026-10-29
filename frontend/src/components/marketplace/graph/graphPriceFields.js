/**
 * Pure helpers for keeping the price sink aligned with the adapter's current
 * price-field schema. Keeping this outside graphModel.js makes schema sync
 * testable without Vue or the application i18n runtime.
 */

export const EXTENDED_PRICE_KEYS = new Set([
  "discountedPrice",
  "sale_price",
  "salePrice",
]);

/** Extra vertical space consumed by each visible group label in the price sink. */
export const PRICE_FIELD_GROUP_HEADER_HEIGHT = 18;

/** Count group headers rendered before the first field in each group run. */
export function priceFieldGroupCount(fields) {
  let count = 0;
  let previous = "";
  for (const field of fields || []) {
    const group = String(field?.group || "").trim();
    if (group && group !== previous) count += 1;
    previous = group;
  }
  return count;
}

/**
 * Refresh schema metadata without replacing user-owned field bindings. Schema
 * order is stable and keyed connections remain attached to the same field.
 */
export function syncAdapterPriceFields(nodes, priceFieldMappings) {
  const priceNode = (nodes || []).find(
    (node) => node.type === "adapterPriceFields",
  );
  if (!priceNode) return nodes;

  const mappings = priceFieldMappings || [];
  const allowed = new Set(
    mappings.map((mapping) => mapping.externalFieldKey).filter(Boolean),
  );
  const fields = (priceNode.data?.fields || [])
    .filter(
      (field) =>
        !field?.fieldKey ||
        !EXTENDED_PRICE_KEYS.has(field.fieldKey) ||
        allowed.has(field.fieldKey),
    )
    .map((field) => ({ ...field }));
  const fieldsByKey = new Map(fields.map((field) => [field.fieldKey, field]));

  mappings.forEach((mapping, index) => {
    const key = mapping.externalFieldKey;
    if (!key) return;

    const existing = fieldsByKey.get(key);
    const metadata = {
      label: mapping.externalFieldName || key,
      outputType: mapping.externalFieldType || "string",
      isRequired: !!mapping.isRequired,
      group: mapping.group ?? null,
      description: mapping.description ?? null,
      sortOrder: Number.isFinite(mapping.sortOrder) ? mapping.sortOrder : index,
    };

    if (existing) {
      Object.assign(existing, metadata);
      return;
    }

    const field = { fieldKey: key, ...metadata };
    fields.push(field);
    fieldsByKey.set(key, field);
  });

  const schemaOrder = new Map(
    mappings
      .map((mapping, index) => ({
        key: mapping.externalFieldKey,
        order: Number.isFinite(mapping.sortOrder) ? mapping.sortOrder : index,
      }))
      .filter((mapping) => mapping.key)
      .map((mapping) => [mapping.key, mapping.order]),
  );
  fields.sort((a, b) => {
    const aOrder = schemaOrder.get(a.fieldKey);
    const bOrder = schemaOrder.get(b.fieldKey);
    if (aOrder != null && bOrder != null) return aOrder - bOrder;
    if (aOrder != null) return -1;
    if (bOrder != null) return 1;
    return 0;
  });

  priceNode.data = { ...(priceNode.data || {}), fields };
  return nodes;
}

/** Compare displayed schema fields without including user-owned bindings. */
export function samePriceFieldSchema(current, next) {
  const schema = (fields) =>
    (fields || []).map((field) => ({
      fieldKey: field.fieldKey,
      label: field.label,
      outputType: field.outputType,
      isRequired: !!field.isRequired,
      group: field.group ?? null,
      description: field.description ?? null,
      sortOrder: field.sortOrder ?? null,
    }));
  return JSON.stringify(schema(current)) === JSON.stringify(schema(next));
}
