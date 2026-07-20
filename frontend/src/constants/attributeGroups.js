// Predefined attribute group identifiers, shared between the category editor and the
// product attribute form. `value` is the literal string stored on attribute records
// (kept as the original Cyrillic values for backward compatibility with existing data,
// written as Unicode escapes so this source file stays ASCII-only). `key` is the stable
// i18n key rendered via `t("attributeGroups." + key)`. Custom groups (not in this list)
// are appended after these in display order.
export const ATTRIBUTE_GROUP_OPTIONS = [
  { key: "core", value: "\u041e\u0441\u043d\u043e\u0432\u043d\u044b\u0435" }, // Basic
  { key: "brand", value: "\u0411\u0440\u0435\u043d\u0434" }, // Brand
  { key: "country", value: "\u0421\u0442\u0440\u0430\u043d\u0430" }, // Country
  { key: "material", value: "\u041c\u0430\u0442\u0435\u0440\u0438\u0430\u043b" }, // Material
  { key: "dimensions", value: "\u0420\u0430\u0437\u043c\u0435\u0440\u044b \u0438 \u0432\u0435\u0441" }, // Dimensions and weight
  { key: "appearance", value: "\u0412\u043d\u0435\u0448\u043d\u0438\u0439 \u0432\u0438\u0434" }, // Appearance
  { key: "technicalSpecs", value: "\u0422\u0435\u0445\u043d\u0438\u0447\u0435\u0441\u043a\u0438\u0435 \u0445\u0430\u0440\u0430\u043a\u0442\u0435\u0440\u0438\u0441\u0442\u0438\u043a\u0438" }, // Technical specifications
  { key: "electricalSpecs", value: "\u042d\u043b\u0435\u043a\u0442\u0440\u0438\u0447\u0435\u0441\u043a\u0438\u0435 \u0445\u0430\u0440\u0430\u043a\u0442\u0435\u0440\u0438\u0441\u0442\u0438\u043a\u0438" }, // Electrical specifications
  { key: "package", value: "\u041a\u043e\u043c\u043f\u043b\u0435\u043a\u0442\u0430\u0446\u0438\u044f" }, // Package contents
  { key: "certification", value: "\u0421\u0435\u0440\u0442\u0438\u0444\u0438\u043a\u0430\u0446\u0438\u044f" }, // Certification
  { key: "media", value: "\u041c\u0435\u0434\u0438\u0430" }, // Media
  { key: "packaging", value: "\u0423\u043f\u0430\u043a\u043e\u0432\u043a\u0430" }, // Packaging
  { key: "labeling", value: "\u041c\u0430\u0440\u043a\u0438\u0440\u043e\u0432\u043a\u0430" }, // Labeling
  { key: "other", value: "\u041f\u0440\u043e\u0447\u0435\u0435" }, // Other
];

export const ATTRIBUTE_GROUP_VALUES = ATTRIBUTE_GROUP_OPTIONS.map((option) => option.value);

export function attributeGroupKeyForValue(value) {
  return ATTRIBUTE_GROUP_OPTIONS.find((option) => option.value === value)?.key;
}
