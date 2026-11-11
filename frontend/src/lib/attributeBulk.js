export const ATTRIBUTE_BULK_LIMIT = 1000;
export const ATTRIBUTE_BULK_FIELDS = [
  "isRequired",
  "isUnique",
  "showOnProductPage",
  "descriptionInTooltip",
];

export function attributeBulkPatch(settings) {
  return Object.fromEntries(
    ATTRIBUTE_BULK_FIELDS.filter(
      (field) => settings[field] === "true" || settings[field] === "false",
    ).map((field) => [field, settings[field] === "true"]),
  );
}

export function retainAttributeSelection(selectedIds, attributes) {
  const available = new Set(attributes.map((attribute) => attribute.id));
  return [...new Set(selectedIds)].filter((id) => available.has(id));
}
