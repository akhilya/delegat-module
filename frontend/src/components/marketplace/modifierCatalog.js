/**
 * Modifier catalog — defines all available modifiers, their display names,
 * categories, applicable field types, and parameter schemas.
 */
export const MODIFIER_CATALOG = {
  // Text modifiers
  truncate: {
    type: "truncate",
    displayName: "Обрезать",
    category: "text",
    applicableTypes: ["string", "text"],
    params: [
      { key: "maxLength", label: "Макс. длина", type: "number", required: true, default: 60 },
    ],
    summary: (p) => `до ${p.maxLength || "?"} символов`,
  },
  split: {
    type: "split",
    displayName: "Разделить",
    category: "text",
    applicableTypes: ["string", "text"],
    params: [
      { key: "delimiter", label: "Разделитель", type: "text", required: true, default: "," },
      { key: "index", label: "Индекс части", type: "number", required: true, default: 0 },
    ],
    summary: (p) => `по "${p.delimiter || ","}" [${p.index || 0}]`,
  },
  replace: {
    type: "replace",
    displayName: "Заменить",
    category: "text",
    applicableTypes: ["string", "text"],
    params: [
      { key: "search", label: "Искать", type: "text", required: true, default: "" },
      { key: "replacement", label: "Заменить на", type: "text", required: false, default: "" },
    ],
    summary: (p) => `"${p.search || ""}" → "${p.replacement || ""}"`,
  },
  append: {
    type: "append",
    displayName: "Добавить в конец",
    category: "text",
    applicableTypes: ["string", "text"],
    params: [
      { key: "suffix", label: "Суффикс", type: "text", required: true, default: "" },
    ],
    summary: (p) => `+ "${p.suffix || ""}"`,
  },
  prepend: {
    type: "prepend",
    displayName: "Добавить в начало",
    category: "text",
    applicableTypes: ["string", "text"],
    params: [
      { key: "prefix", label: "Префикс", type: "text", required: true, default: "" },
    ],
    summary: (p) => `"${p.prefix || ""}" +`,
  },
  uppercase: {
    type: "uppercase",
    displayName: "В верхний регистр",
    category: "text",
    applicableTypes: ["string", "text"],
    params: [],
    summary: () => "",
  },
  lowercase: {
    type: "lowercase",
    displayName: "В нижний регистр",
    category: "text",
    applicableTypes: ["string", "text"],
    params: [],
    summary: () => "",
  },
  trim: {
    type: "trim",
    displayName: "Убрать пробелы",
    category: "text",
    applicableTypes: ["string", "text"],
    params: [],
    summary: () => "",
  },

  // Number modifiers
  multiply: {
    type: "multiply",
    displayName: "Умножить",
    category: "number",
    applicableTypes: ["number", "integer"],
    params: [
      { key: "factor", label: "Множитель", type: "number", required: true, default: 1 },
    ],
    summary: (p) => `× ${p.factor ?? "?"}`,
  },
  divide: {
    type: "divide",
    displayName: "Разделить",
    category: "number",
    applicableTypes: ["number", "integer"],
    params: [
      { key: "divisor", label: "Делитель", type: "number", required: true, default: 1 },
    ],
    summary: (p) => `÷ ${p.divisor ?? "?"}`,
  },
  add: {
    type: "add",
    displayName: "Прибавить",
    category: "number",
    applicableTypes: ["number", "integer"],
    params: [
      { key: "addend", label: "Слагаемое", type: "number", required: true, default: 0 },
    ],
    summary: (p) => `+ ${p.addend ?? "?"}`,
  },
  round: {
    type: "round",
    displayName: "Округлить",
    category: "number",
    applicableTypes: ["number", "integer"],
    params: [
      { key: "decimals", label: "Знаков после запятой", type: "number", required: true, default: 0 },
    ],
    summary: (p) => `до ${p.decimals ?? 0} знаков`,
  },
  floor: {
    type: "floor",
    displayName: "Округлить вниз",
    category: "number",
    applicableTypes: ["number", "integer"],
    params: [],
    summary: () => "",
  },
  ceiling: {
    type: "ceiling",
    displayName: "Округлить вверх",
    category: "number",
    applicableTypes: ["number", "integer"],
    params: [],
    summary: () => "",
  },
  clamp: {
    type: "clamp",
    displayName: "Ограничить диапазон",
    category: "number",
    applicableTypes: ["number", "integer"],
    params: [
      { key: "min", label: "Минимум", type: "number", required: true, default: 0 },
      { key: "max", label: "Максимум", type: "number", required: true, default: 100 },
    ],
    summary: (p) => `[${p.min ?? "?"}, ${p.max ?? "?"}]`,
  },

  // Select modifiers
  valueMap: {
    type: "valueMap",
    displayName: "Маппинг значений",
    category: "select",
    applicableTypes: ["select", "multiselect"],
    params: [
      { key: "mappings", label: "Соответствия", type: "valueMap", required: true, default: [] },
      { key: "default", label: "По умолчанию", type: "text", required: false, default: "" },
    ],
    summary: (p) => {
      const count = Array.isArray(p.mappings) ? p.mappings.length : 0;
      return `${count} правил${count === 1 ? "о" : count > 1 && count < 5 ? "а" : ""}`;
    },
  },
  optionMap: {
    type: "optionMap",
    displayName: "Маппинг по вариантам",
    category: "select",
    applicableTypes: ["select", "multiselect"],
    params: [
      { key: "mappings", label: "Соответствия", type: "optionMap", required: true, default: [] },
      { key: "default", label: "По умолчанию", type: "text", required: false, default: "" },
    ],
    summary: (p) => {
      const count = Array.isArray(p.mappings) ? p.mappings.length : 0;
      return `${count} правил${count === 1 ? "о" : count > 1 && count < 5 ? "а" : ""}`;
    },
  },

  // Template
  template: {
    type: "template",
    displayName: "Подставить поля товара",
    category: "text",
    applicableTypes: ["string", "text"],
    params: [
      { key: "template", label: "Шаблон", type: "template", required: true, default: "{{value}}" },
    ],
    summary: (p) => {
      const t = p.template || "";
      return t.length > 30 ? t.substring(0, 30) + "…" : t;
    },
  },

  // Universal modifiers
  defaultValue: {
    type: "defaultValue",
    displayName: "Значение по умолчанию",
    category: "any",
    applicableTypes: null, // applicable to all types
    params: [
      { key: "value", label: "Значение", type: "text", required: true, default: "" },
    ],
    summary: (p) => `"${p.value || ""}"`,
  },
  takeFirst: {
    type: "takeFirst",
    displayName: "Взять первый",
    category: "multiselect",
    applicableTypes: ["multiselect", "array"],
    params: [],
    summary: () => "",
  },
};

export const MODIFIER_CATEGORIES = {
  text: { label: "Текст", icon: "Type" },
  number: { label: "Число", icon: "Hash" },
  select: { label: "Выбор", icon: "List" },
  multiselect: { label: "Множественный выбор", icon: "ListChecks" },
  any: { label: "Универсальные", icon: "Sparkles" },
};

/**
 * Get available modifiers for a given field type.
 */
export function getAvailableModifiers(externalFieldType) {
  return Object.values(MODIFIER_CATALOG).filter((m) => {
    if (m.applicableTypes === null) return true;
    if (!externalFieldType) return false;
    return m.applicableTypes.includes(externalFieldType);
  });
}

/**
 * Get display label for a modifier instance.
 */
export function getModifierLabel(modifier) {
  const catalog = MODIFIER_CATALOG[modifier.type];
  if (!catalog) return modifier.type;
  const summary = catalog.summary(modifier.params || {});
  return summary ? `${catalog.displayName}: ${summary}` : catalog.displayName;
}
