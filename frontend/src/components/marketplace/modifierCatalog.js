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
    aliases: ["truncate", "cut", "substr", "crop", "limit text"],
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
    aliases: ["split", "explode", "separate", "delimiter", "разделить"],
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
    aliases: ["replace", "substitute", "regex", "regexp", "swap"],
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
    aliases: ["append", "suffix", "postfix", "add after", "после"],
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
    aliases: ["prepend", "prefix", "add before", "перед"],
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
    aliases: ["uppercase", "upper", "caps", "toupper", "заглавные"],
    category: "text",
    reversible: false,
    applicableTypes: ["string", "text"],
    params: [],
    summary: () => "",
  },
  lowercase: {
    type: "lowercase",
    displayName: "В нижний регистр",
    aliases: ["lowercase", "lower", "small", "tolower", "строчные"],
    category: "text",
    reversible: false,
    applicableTypes: ["string", "text"],
    params: [],
    summary: () => "",
  },
  trim: {
    type: "trim",
    displayName: "Убрать пробелы",
    aliases: ["trim", "strip", "whitespace", "spaces", "пробел", "ltrim", "rtrim"],
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
    aliases: ["multiply", "times", "*", "product", "умнож"],
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
    aliases: ["divide", "division", "/", "quot", "делить"],
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
    aliases: ["add", "plus", "+", "sum", "increment", "сложить"],
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
    aliases: ["round", "math.round"],
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
    aliases: ["floor", "round down", "вниз", "math.floor"],
    category: "number",
    reversible: false,
    applicableTypes: ["number", "integer"],
    params: [],
    summary: () => "",
  },
  ceiling: {
    type: "ceiling",
    displayName: "Округлить вверх",
    aliases: ["ceil", "ceiling", "round up", "вверх", "math.ceil"],
    category: "number",
    reversible: false,
    applicableTypes: ["number", "integer"],
    params: [],
    summary: () => "",
  },
  clamp: {
    type: "clamp",
    displayName: "Ограничить диапазон",
    aliases: ["clamp", "min max", "range", "between", "диапазон"],
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
    displayName: "Маппинг значений (→ valueId)",
    aliases: ["map", "mapping", "lookup", "translate", "dict", "словарь", "convert"],
    category: "select",
    reversible: true,
    applicableTypes: ["select", "multiselect"],
    params: [
      { key: "mappings", label: "Соответствия", type: "valueMap", required: true, default: [] },
      { key: "default", label: "По умолчанию", type: "text", required: false, default: "" },
    ],
    summary: (p) => {
      const count = Array.isArray(p.mappings) ? p.mappings.length : 0;
      return count ? `${count} → valueId` : "пусто";
    },
  },
  optionMap: {
    type: "optionMap",
    displayName: "Маппинг значений по вариантам",
    aliases: ["option map", "enum map", "варианты", "options", "translate"],
    category: "select",
    reversible: true,
    applicableTypes: ["select", "multiselect"],
    params: [
      { key: "mappings", label: "Соответствия", type: "optionMap", required: true, default: [] },
      { key: "default", label: "По умолчанию", type: "text", required: false, default: "" },
    ],
    summary: (p) => {
      const count = Array.isArray(p.mappings) ? p.mappings.length : 0;
      return count ? `${count} вариантов` : "пусто";
    },
  },

  llmGenerate: {
    type: "llmGenerate",
    displayName: "AI-генерация",
    aliases: ["llm", "ai generate", "gpt", "генерация", "ai"],
    category: "text",
    reversible: false,
    applicableTypes: ["string", "text", "select", "number", "any"],
    params: [
      { key: "prompt", label: "Промпт", type: "template", required: true, default: "{{value}}" },
      {
        key: "systemPrompt",
        label: "Системный промпт",
        type: "template",
        required: false,
        default:
          "You generate concise marketplace field values. Respond with plain text only — no markdown, no quotes, no preamble.",
      },
      { key: "modelId", label: "Модель", type: "aiModel", required: true, default: null },
      { key: "seed", label: "Seed", type: "number", required: false, default: null },
      { key: "cacheEnabled", label: "Кэшировать", type: "checkbox", required: false, default: true },
      { key: "perProduct", label: "Уникально для каждого товара", type: "checkbox", required: false, default: false },
    ],
    summary: (p) => {
      const prompt = p.prompt || "";
      return prompt.length > 24 ? prompt.substring(0, 24) + "…" : prompt || "промпт";
    },
  },

  randomInt: {
    type: "randomInt",
    displayName: "Случайное целое",
    aliases: ["random int", "rand int", "случайное число"],
    category: "number",
    reversible: false,
    applicableTypes: ["number", "integer", "string", "any"],
    params: [
      { key: "min", label: "Минимум", type: "number", required: true, default: 0 },
      { key: "max", label: "Максимум", type: "number", required: true, default: 100 },
      { key: "seed", label: "Seed", type: "number", required: false, default: null },
    ],
    summary: (p) => `[${p.min ?? 0}, ${p.max ?? 100}]`,
  },
  randomFloat: {
    type: "randomFloat",
    displayName: "Случайное дробное",
    aliases: ["random float", "rand float"],
    category: "number",
    reversible: false,
    applicableTypes: ["number", "integer", "string", "any"],
    params: [
      { key: "min", label: "Минимум", type: "number", required: true, default: 0 },
      { key: "max", label: "Максимум", type: "number", required: true, default: 1 },
      { key: "decimals", label: "Знаков", type: "number", required: false, default: 2 },
      { key: "seed", label: "Seed", type: "number", required: false, default: null },
    ],
    summary: (p) => `[${p.min ?? 0}, ${p.max ?? 1}]`,
  },
  randomPick: {
    type: "randomPick",
    displayName: "Случайный выбор",
    aliases: ["random pick", "choice", "выбор"],
    category: "any",
    reversible: false,
    applicableTypes: null,
    params: [
      { key: "choices", label: "Варианты", type: "tagInput", required: false, default: [] },
      { key: "seed", label: "Seed", type: "number", required: false, default: null },
    ],
    summary: (p) => {
      const n = Array.isArray(p.choices) ? p.choices.length : 0;
      return n ? `из ${n}` : "из входа";
    },
  },
  randomUuid: {
    type: "randomUuid",
    displayName: "Случайный UUID",
    aliases: ["uuid", "guid", "random uuid"],
    category: "any",
    reversible: false,
    applicableTypes: ["string", "text", "any"],
    params: [
      { key: "seed", label: "Seed", type: "number", required: false, default: null },
    ],
    summary: () => "UUID",
  },

  toText: {
    type: "toText",
    displayName: "Преобразовать в текст",
    aliases: ["to text", "to string", "stringify", "tostring", "cast", "в текст"],
    category: "any",
    reversible: true,
    applicableTypes: ["select", "multiselect", "number", "integer"],
    params: [
      {
        key: "useLabel",
        label: "Словесное преобразование (лейбл вместо кода)",
        type: "checkbox",
        required: false,
        default: false,
      },
    ],
    summary: (p) => (p.useLabel ? "с лейблами" : ""),
  },

  forceType: {
    type: "forceType",
    displayName: "Принудительный тип",
    aliases: ["cast", "force type", "convert", "typecast", "тип"],
    category: "any",
    reversible: true,
    applicableTypes: null,
    params: [
      {
        key: "type", label: "Отправить как", type: "select", required: true,
        default: "string",
        options: [
          { value: "string", label: "Текст" },
          { value: "number", label: "Число" },
          { value: "integer", label: "Целое число" },
          { value: "boolean", label: "Логическое" },
        ],
      },
    ],
    summary: (p) => {
      const labels = { string: "текст", number: "число", integer: "целое", boolean: "логич." };
      return labels[p.type] || p.type || "";
    },
  },

  // Template
  template: {
    type: "template",
    displayName: "Подставить поля товара",
    aliases: ["template", "interpolate", "substitute", "format", "шаблон", "fields", "подстав"],
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
    aliases: ["default", "fallback", "null coalesce", "если пусто", "or else", "если нет"],
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
    aliases: ["first", "head", "take first", "первый"],
    category: "array",
    reversible: false,
    applicableTypes: ["multiselect", "array"],
    params: [],
    summary: () => "",
  },
  limit: {
    type: "limit",
    displayName: "Ограничить количество",
    aliases: ["limit", "take", "top", "slice", "max count"],
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
    aliases: ["skip", "drop", "offset", "пропуст"],
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
    aliases: ["reverse", "invert", "flip", "перевернуть", "backwards"],
    category: "array",
    reversible: true,
    applicableTypes: ["multiselect", "array"],
    params: [],
    summary: () => "",
  },
  join: {
    type: "join",
    displayName: "Объединить в строку",
    aliases: ["join", "join array", "concat array", "соединить"],
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
    aliases: ["sort", "order", "arrange", "sorted", "alphabetical", "упорядочить"],
    category: "array",
    reversible: false,
    applicableTypes: ["multiselect", "array"],
    params: [],
    summary: () => "",
  },
  addItems: {
    type: "addItems",
    displayName: "Добавить элементы",
    aliases: ["add items", "push", "extend", "concat arrays", "добавить элементы"],
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
    aliases: ["remove", "filter", "exclude", "delete", "убрать", "удалить", "minus"],
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
  random: { label: "Случайные", icon: "Dices" },
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
