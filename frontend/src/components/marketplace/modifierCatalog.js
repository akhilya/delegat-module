/**
 * Modifier catalog — defines all available modifiers, their display names,
 * categories, applicable field types, and parameter schemas.
 *
 * `reversible` — whether the modifier can be inverted during import.
 * Irreversible modifiers pass values through unchanged on import.
 */
export const MODIFIER_CATALOG = {
  // Text modifiers
  truncate: {
    type: "truncate",
    displayName: "Обрезать",
    category: "text",
    reversible: false,
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
    reversible: false,
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
    reversible: false,
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
    reversible: false,
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
    reversible: false,
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
    reversible: false,
    applicableTypes: ["string", "text"],
    params: [],
    summary: () => "",
  },
  lowercase: {
    type: "lowercase",
    displayName: "В нижний регистр",
    category: "text",
    reversible: false,
    applicableTypes: ["string", "text"],
    params: [],
    summary: () => "",
  },
  trim: {
    type: "trim",
    displayName: "Убрать пробелы",
    category: "text",
    reversible: false,
    applicableTypes: ["string", "text"],
    params: [],
    summary: () => "",
  },

  // Number modifiers
  multiply: {
    type: "multiply",
    displayName: "Умножить",
    category: "number",
    reversible: true,
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
    reversible: true,
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
    reversible: true,
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
    reversible: false,
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
    reversible: false,
    applicableTypes: ["number", "integer"],
    params: [],
    summary: () => "",
  },
  ceiling: {
    type: "ceiling",
    displayName: "Округлить вверх",
    category: "number",
    reversible: false,
    applicableTypes: ["number", "integer"],
    params: [],
    summary: () => "",
  },
  clamp: {
    type: "clamp",
    displayName: "Ограничить диапазон",
    category: "number",
    reversible: false,
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
    reversible: true,
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
    reversible: true,
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
    reversible: false,
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
    reversible: false,
    applicableTypes: null, // applicable to all types
    params: [
      { key: "value", label: "Значение", type: "text", required: true, default: "" },
    ],
    summary: (p) => `"${p.value || ""}"`,
  },
  // Array modifiers
  takeFirst: {
    type: "takeFirst",
    displayName: "Взять первый",
    category: "array",
    reversible: false,
    applicableTypes: ["multiselect", "array"],
    params: [],
    summary: () => "",
  },
  limit: {
    type: "limit",
    displayName: "Ограничить количество",
    category: "array",
    reversible: false,
    applicableTypes: ["multiselect", "array"],
    params: [
      { key: "count", label: "Количество", type: "number", required: true, default: 5 },
    ],
    summary: (p) => `до ${p.count ?? 5}`,
  },
  skip: {
    type: "skip",
    displayName: "Пропустить первые",
    category: "array",
    reversible: false,
    applicableTypes: ["multiselect", "array"],
    params: [
      { key: "count", label: "Пропустить", type: "number", required: true, default: 1 },
    ],
    summary: (p) => `${p.count ?? 1} шт.`,
  },
  reverse: {
    type: "reverse",
    displayName: "Перевернуть порядок",
    category: "array",
    reversible: true,
    applicableTypes: ["multiselect", "array"],
    params: [],
    summary: () => "",
  },
  join: {
    type: "join",
    displayName: "Объединить в строку",
    category: "array",
    reversible: false,
    applicableTypes: ["multiselect", "array"],
    params: [
      { key: "delimiter", label: "Разделитель", type: "text", required: false, default: ", " },
    ],
    summary: (p) => `"${p.delimiter ?? ", "}"`,
  },
  sort: {
    type: "sort",
    displayName: "Сортировать",
    category: "array",
    reversible: false,
    applicableTypes: ["multiselect", "array"],
    params: [],
    summary: () => "",
  },
  addItems: {
    type: "addItems",
    displayName: "Добавить элементы",
    category: "array",
    reversible: false,
    applicableTypes: ["multiselect", "array"],
    params: [
      { key: "items", label: "Элементы", type: "tagInput", required: false, default: [] },
    ],
    summary: (p) => {
      const items = Array.isArray(p.items) ? p.items : [];
      return items.length > 0 ? `+${items.length}` : "";
    },
  },
  removeItems: {
    type: "removeItems",
    displayName: "Удалить элементы",
    category: "array",
    reversible: false,
    applicableTypes: ["multiselect", "array"],
    params: [
      { key: "items", label: "Элементы", type: "tagInput", required: false, default: [] },
    ],
    summary: (p) => {
      const items = Array.isArray(p.items) ? p.items : [];
      return items.length > 0 ? `-${items.length}` : "";
    },
  },
};

export const MODIFIER_CATEGORIES = {
  text: { label: "Текст", icon: "Type" },
  number: { label: "Число", icon: "Hash" },
  select: { label: "Выбор", icon: "List" },
  array: { label: "Массив", icon: "List" },
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
