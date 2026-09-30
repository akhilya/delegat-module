import { t } from "@/i18n";
import { getCachedMe } from "@/lib/meCache";

/**
 * Modifier catalog - defines all available modifiers, their display names,
 * categories, applicable field types, and parameter schemas.
 *
 * `reversible` - whether the modifier can be inverted during import.
 * Irreversible modifiers pass values through unchanged on import.
 */
export const MODIFIER_CATALOG = {
  // Text modifiers
  truncate: {
    type: "truncate",
    get displayName() { return t("marketplace.modifiers.truncate.name"); },
    aliases: ["truncate", "cut", "substr", "crop", "limit text"],
    category: "text",
    reversible: false,
    applicableTypes: ["string", "text"],
    params: [
      { key: "maxLength", get label() { return t("marketplace.modifiers.truncate.maxLength"); }, type: "number", required: true, default: 60 },
    ],
    summary: (p) => t("marketplace.modifiers.truncate.summary", { n: p.maxLength || "?" }),
  },
  split: {
    type: "split",
    get displayName() { return t("marketplace.modifiers.split.name"); },
    aliases: ["split", "explode", "separate", "delimiter"],
    category: "text",
    reversible: false,
    applicableTypes: ["string", "text"],
    params: [
      { key: "delimiter", get label() { return t("marketplace.modifiers.split.delimiter"); }, type: "text", required: true, default: "," },
      { key: "index", get label() { return t("marketplace.modifiers.split.indexPart"); }, type: "number", required: true, default: 0 },
    ],
    summary: (p) => t("marketplace.modifiers.split.summary", { d: p.delimiter || ",", i: p.index || 0 }),
  },
  replace: {
    type: "replace",
    get displayName() { return t("marketplace.modifiers.replace.name"); },
    aliases: ["replace", "substitute", "swap"],
    category: "text",
    reversible: false,
    applicableTypes: ["string", "text"],
    params: [
      { key: "search", get label() { return t("marketplace.modifiers.replace.search"); }, type: "text", required: true, default: "" },
      { key: "replacement", get label() { return t("marketplace.modifiers.replace.replacement"); }, type: "text", required: false, default: "" },
    ],
    summary: (p) => `"${p.search || ""}" → "${p.replacement || ""}"`,
  },
  regexReplace: {
    type: "regexReplace",
    get displayName() { return t("marketplace.modifiers.regexReplace.name"); },
    aliases: ["regex", "regexp", "regular expression", "pattern replace"],
    category: "text",
    reversible: false,
    applicableTypes: ["string", "text"],
    params: [
      { key: "pattern", get label() { return t("marketplace.modifiers.regexReplace.pattern"); }, type: "text", required: true, default: "" },
      { key: "replacement", get label() { return t("marketplace.modifiers.regexReplace.replacement"); }, type: "text", required: false, default: "" },
      { key: "ignoreCase", get label() { return t("marketplace.modifiers.regexReplace.ignoreCase"); }, type: "checkbox", required: false, default: false },
    ],
    summary: (p) => `/${p.pattern || ""}/ → "${p.replacement || ""}"`,
  },
  regexExtract: {
    type: "regexExtract",
    get displayName() { return t("marketplace.modifiers.regexExtract.name"); },
    aliases: ["regex find", "regex search", "extract", "match", "capture"],
    category: "text",
    reversible: false,
    applicableTypes: ["string", "text"],
    params: [
      { key: "pattern", get label() { return t("marketplace.modifiers.regexExtract.pattern"); }, type: "text", required: true, default: "" },
      { key: "group", get label() { return t("marketplace.modifiers.regexExtract.group"); }, type: "number", required: false, default: 0 },
      { key: "ignoreCase", get label() { return t("marketplace.modifiers.regexExtract.ignoreCase"); }, type: "checkbox", required: false, default: false },
    ],
    summary: (p) => `/${p.pattern || ""}/ [${p.group ?? 0}]`,
  },
  append: {
    type: "append",
    get displayName() { return t("marketplace.modifiers.append.name"); },
    aliases: ["append", "suffix", "postfix", "add after"],
    category: "text",
    reversible: false,
    applicableTypes: ["string", "text"],
    params: [
      { key: "suffix", get label() { return t("marketplace.modifiers.append.suffix"); }, type: "text", required: true, default: "" },
    ],
    summary: (p) => `+ "${p.suffix || ""}"`,
  },
  prepend: {
    type: "prepend",
    get displayName() { return t("marketplace.modifiers.prepend.name"); },
    aliases: ["prepend", "prefix", "add before"],
    category: "text",
    reversible: false,
    applicableTypes: ["string", "text"],
    params: [
      { key: "prefix", get label() { return t("marketplace.modifiers.prepend.prefix"); }, type: "text", required: true, default: "" },
    ],
    summary: (p) => `"${p.prefix || ""}" +`,
  },
  uppercase: {
    type: "uppercase",
    get displayName() { return t("marketplace.modifiers.uppercase.name"); },
    aliases: ["uppercase", "upper", "caps", "toupper"],
    category: "text",
    reversible: false,
    applicableTypes: ["string", "text"],
    params: [],
    summary: () => "",
  },
  lowercase: {
    type: "lowercase",
    get displayName() { return t("marketplace.modifiers.lowercase.name"); },
    aliases: ["lowercase", "lower", "small", "tolower"],
    category: "text",
    reversible: false,
    applicableTypes: ["string", "text"],
    params: [],
    summary: () => "",
  },
  trim: {
    type: "trim",
    get displayName() { return t("marketplace.modifiers.trim.name"); },
    aliases: ["trim", "strip", "whitespace", "spaces", "ltrim", "rtrim"],
    category: "text",
    reversible: false,
    applicableTypes: ["string", "text"],
    params: [],
    summary: () => "",
  },

  // Number modifiers
  multiply: {
    type: "multiply",
    get displayName() { return t("marketplace.modifiers.multiply.name"); },
    aliases: ["multiply", "times", "*", "product"],
    category: "number",
    reversible: true,
    applicableTypes: ["number", "integer"],
    params: [
      { key: "factor", get label() { return t("marketplace.modifiers.multiply.factor"); }, type: "number", required: true, default: 1 },
    ],
    summary: (p) => `× ${p.factor ?? "?"}`,
  },
  divide: {
    type: "divide",
    get displayName() { return t("marketplace.modifiers.divide.name"); },
    aliases: ["divide", "division", "/", "quot"],
    category: "number",
    reversible: true,
    applicableTypes: ["number", "integer"],
    params: [
      { key: "divisor", get label() { return t("marketplace.modifiers.divide.divisor"); }, type: "number", required: true, default: 1 },
    ],
    summary: (p) => `÷ ${p.divisor ?? "?"}`,
  },
  add: {
    type: "add",
    get displayName() { return t("marketplace.modifiers.add.name"); },
    aliases: ["add", "plus", "+", "sum", "increment"],
    category: "number",
    reversible: true,
    applicableTypes: ["number", "integer"],
    params: [
      { key: "addend", get label() { return t("marketplace.modifiers.add.addend"); }, type: "number", required: true, default: 0 },
    ],
    summary: (p) => `+ ${p.addend ?? "?"}`,
  },
  subtract: {
    type: "subtract",
    get displayName() { return t("marketplace.modifiers.subtract.name"); },
    aliases: ["subtract", "minus", "-", "sub", "decrement"],
    category: "number",
    reversible: true,
    applicableTypes: ["number", "integer"],
    params: [
      { key: "subtrahend", get label() { return t("marketplace.modifiers.subtract.subtrahend"); }, type: "number", required: true, default: 0 },
    ],
    summary: (p) => `− ${p.subtrahend ?? "?"}`,
  },
  round: {
    type: "round",
    get displayName() { return t("marketplace.modifiers.round.name"); },
    aliases: ["round", "math.round"],
    category: "number",
    reversible: false,
    applicableTypes: ["number", "integer"],
    params: [
      { key: "decimals", get label() { return t("marketplace.modifiers.round.decimals"); }, type: "number", required: true, default: 0 },
    ],
    summary: (p) => t("marketplace.modifiers.round.summary", { n: p.decimals ?? 0 }),
  },
  floor: {
    type: "floor",
    get displayName() { return t("marketplace.modifiers.floor.name"); },
    aliases: ["floor", "round down", "math.floor"],
    category: "number",
    reversible: false,
    applicableTypes: ["number", "integer"],
    params: [],
    summary: () => "",
  },
  ceiling: {
    type: "ceiling",
    get displayName() { return t("marketplace.modifiers.ceiling.name"); },
    aliases: ["ceil", "ceiling", "round up", "math.ceil"],
    category: "number",
    reversible: false,
    applicableTypes: ["number", "integer"],
    params: [],
    summary: () => "",
  },
  clamp: {
    type: "clamp",
    get displayName() { return t("marketplace.modifiers.clamp.name"); },
    aliases: ["clamp", "min max", "range", "between"],
    category: "number",
    reversible: false,
    applicableTypes: ["number", "integer"],
    params: [
      { key: "min", get label() { return t("marketplace.modifiers.clamp.min"); }, type: "number", required: true, default: 0 },
      { key: "max", get label() { return t("marketplace.modifiers.clamp.max"); }, type: "number", required: true, default: 100 },
    ],
    summary: (p) => `[${p.min ?? "?"}, ${p.max ?? "?"}]`,
  },

  // Select modifiers
  valueMap: {
    type: "valueMap",
    get displayName() { return t("marketplace.modifiers.valueMap.name"); },
    aliases: ["map", "mapping", "lookup", "translate", "dict", "convert", "switch", "string switch"],
    category: "text",
    reversible: true,
    applicableTypes: ["string", "text", "select", "multiselect", "any"],
    params: [
      { key: "mappings", get label() { return t("marketplace.modifiers.valueMap.mappings"); }, type: "valueMap", required: true, default: [] },
      { key: "default", get label() { return t("marketplace.modifiers.valueMap.default"); }, type: "text", required: false, default: "" },
    ],
    summary: (p) => {
      const count = Array.isArray(p.mappings) ? p.mappings.length : 0;
      return count ? String(count) : t("marketplace.modifiers.common.empty");
    },
  },
  optionMap: {
    type: "optionMap",
    get displayName() { return t("marketplace.modifiers.optionMap.name"); },
    aliases: ["option map", "enum map", "options", "translate"],
    category: "select",
    reversible: true,
    applicableTypes: ["select", "multiselect"],
    params: [
      { key: "mappings", get label() { return t("marketplace.modifiers.valueMap.mappings"); }, type: "optionMap", required: true, default: [] },
      { key: "default", get label() { return t("marketplace.modifiers.valueMap.default"); }, type: "text", required: false, default: "" },
    ],
    summary: (p) => {
      const count = Array.isArray(p.mappings) ? p.mappings.length : 0;
      return count ? t("marketplace.modifiers.optionMap.summaryCount", { count }) : t("marketplace.modifiers.common.empty");
    },
  },

  llmGenerate: {
    type: "llmGenerate",
    get displayName() { return t("marketplace.modifiers.llmGenerate.name"); },
    aliases: ["llm", "ai generate", "gpt", "generate", "ai"],
    category: "text",
    reversible: false,
    applicableTypes: ["string", "text", "select", "number", "any"],
    params: [
      { key: "prompt", get label() { return t("marketplace.modifiers.llmGenerate.prompt"); }, type: "template", required: true, default: "{{value}}" },
      {
        key: "systemPrompt",
        get label() { return t("marketplace.modifiers.llmGenerate.systemPrompt"); },
        type: "template",
        required: false,
        default:
          "You generate concise marketplace field values. Respond with plain text only - no markdown, no quotes, no preamble.",
      },
      { key: "modelId", get label() { return t("marketplace.modifiers.llmGenerate.model"); }, type: "aiModel", required: true, default: null },
      { key: "seed", label: "Seed", type: "number", required: false, default: null },
      { key: "cacheEnabled", get label() { return t("marketplace.modifiers.llmGenerate.cacheEnabled"); }, type: "checkbox", required: false, default: true },
      { key: "perProduct", get label() { return t("marketplace.modifiers.llmGenerate.perProduct"); }, type: "checkbox", required: false, default: false },
    ],
    summary: (p) => {
      const prompt = p.prompt || "";
      return prompt.length > 24 ? prompt.substring(0, 24) + "…" : prompt || t("marketplace.modifiers.llmGenerate.summaryPlaceholder");
    },
  },

  randomInt: {
    type: "randomInt",
    get displayName() { return t("marketplace.modifiers.randomInt.name"); },
    aliases: ["random int", "rand int"],
    category: "number",
    reversible: false,
    applicableTypes: ["number", "integer", "string", "any"],
    params: [
      { key: "min", get label() { return t("marketplace.modifiers.clamp.min"); }, type: "number", required: true, default: 0 },
      { key: "max", get label() { return t("marketplace.modifiers.clamp.max"); }, type: "number", required: true, default: 100 },
      { key: "seed", label: "Seed", type: "number", required: false, default: null },
    ],
    summary: (p) => `[${p.min ?? 0}, ${p.max ?? 100}]`,
  },
  randomFloat: {
    type: "randomFloat",
    get displayName() { return t("marketplace.modifiers.randomFloat.name"); },
    aliases: ["random float", "rand float"],
    category: "number",
    reversible: false,
    applicableTypes: ["number", "integer", "string", "any"],
    params: [
      { key: "min", get label() { return t("marketplace.modifiers.clamp.min"); }, type: "number", required: true, default: 0 },
      { key: "max", get label() { return t("marketplace.modifiers.clamp.max"); }, type: "number", required: true, default: 1 },
      { key: "decimals", get label() { return t("marketplace.modifiers.randomFloat.decimals"); }, type: "number", required: false, default: 2 },
      { key: "seed", label: "Seed", type: "number", required: false, default: null },
    ],
    summary: (p) => `[${p.min ?? 0}, ${p.max ?? 1}]`,
  },
  randomPick: {
    type: "randomPick",
    get displayName() { return t("marketplace.modifiers.randomPick.name"); },
    aliases: ["random pick", "choice"],
    category: "any",
    reversible: false,
    applicableTypes: null,
    params: [
      { key: "choices", get label() { return t("marketplace.modifiers.randomPick.choices"); }, type: "tagInput", required: false, default: [] },
      { key: "seed", label: "Seed", type: "number", required: false, default: null },
    ],
    summary: (p) => {
      const n = Array.isArray(p.choices) ? p.choices.length : 0;
      return n ? t("marketplace.modifiers.randomPick.summaryOf", { n }) : t("marketplace.modifiers.randomPick.summaryOfInput");
    },
  },
  randomUuid: {
    type: "randomUuid",
    get displayName() { return t("marketplace.modifiers.randomUuid.name"); },
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
    get displayName() { return t("marketplace.modifiers.toText.name"); },
    aliases: ["to text", "to string", "stringify", "tostring", "cast"],
    category: "any",
    reversible: true,
    applicableTypes: ["select", "multiselect", "number", "integer"],
    params: [
      {
        key: "useLabel",
        get label() { return t("marketplace.modifiers.toText.useLabel"); },
        type: "checkbox",
        required: false,
        default: false,
      },
    ],
    summary: (p) => (p.useLabel ? t("marketplace.modifiers.toText.summaryWithLabels") : ""),
  },

  forceType: {
    type: "forceType",
    get displayName() { return t("marketplace.modifiers.forceType.name"); },
    aliases: ["cast", "force type", "convert", "typecast"],
    category: "any",
    reversible: true,
    applicableTypes: null,
    params: [
      {
        key: "type",
        get label() { return t("marketplace.modifiers.forceType.sendAs"); },
        type: "select", required: true,
        default: "string",
        get options() {
          return [
            { value: "string", label: t("marketplace.modifiers.forceType.optionString") },
            { value: "number", label: t("marketplace.modifiers.forceType.optionNumber") },
            { value: "integer", label: t("marketplace.modifiers.forceType.optionInteger") },
            { value: "boolean", label: t("marketplace.modifiers.forceType.optionBoolean") },
          ];
        },
      },
    ],
    summary: (p) => {
      const labels = {
        string: t("marketplace.modifiers.forceType.summaryText"),
        number: t("marketplace.modifiers.forceType.summaryNumber"),
        integer: t("marketplace.modifiers.forceType.summaryInteger"),
        boolean: t("marketplace.modifiers.forceType.summaryBoolean"),
      };
      return labels[p.type] || p.type || "";
    },
  },

  // Template
  template: {
    type: "template",
    get displayName() { return t("marketplace.modifiers.template.name"); },
    aliases: ["template", "interpolate", "substitute", "format", "fields"],
    category: "text",
    reversible: false,
    applicableTypes: ["string", "text"],
    params: [
      { key: "template", get label() { return t("marketplace.modifiers.template.template"); }, type: "template", required: true, default: "{{value}}" },
    ],
    summary: (p) => {
      const tpl = p.template || "";
      return tpl.length > 30 ? tpl.substring(0, 30) + "…" : tpl;
    },
  },

  // Universal modifiers
  defaultValue: {
    type: "defaultValue",
    get displayName() { return t("marketplace.modifiers.defaultValue.name"); },
    aliases: ["default", "fallback", "null coalesce", "or else"],
    category: "any",
    reversible: false,
    applicableTypes: null, // applicable to all types
    params: [
      { key: "value", get label() { return t("marketplace.modifiers.defaultValue.value"); }, type: "text", required: true, default: "" },
    ],
    summary: (p) => `"${p.value || ""}"`,
  },
  // Array modifiers
  takeFirst: {
    type: "takeFirst",
    get displayName() { return t("marketplace.modifiers.takeFirst.name"); },
    aliases: ["first", "head", "take first"],
    category: "array",
    reversible: false,
    applicableTypes: ["multiselect", "array"],
    params: [],
    summary: () => "",
  },
  limit: {
    type: "limit",
    get displayName() { return t("marketplace.modifiers.limit.name"); },
    aliases: ["limit", "take", "top", "slice", "max count"],
    category: "array",
    reversible: false,
    applicableTypes: ["multiselect", "array"],
    params: [
      { key: "count", get label() { return t("marketplace.modifiers.limit.count"); }, type: "number", required: true, default: 5 },
    ],
    summary: (p) => t("marketplace.modifiers.limit.summary", { n: p.count ?? 5 }),
  },
  skip: {
    type: "skip",
    get displayName() { return t("marketplace.modifiers.skip.name"); },
    aliases: ["skip", "drop", "offset"],
    category: "array",
    reversible: false,
    applicableTypes: ["multiselect", "array"],
    params: [
      { key: "count", get label() { return t("marketplace.modifiers.skip.count"); }, type: "number", required: true, default: 1 },
    ],
    summary: (p) => t("marketplace.modifiers.skip.summary", { n: p.count ?? 1 }),
  },
  reverse: {
    type: "reverse",
    get displayName() { return t("marketplace.modifiers.reverse.name"); },
    aliases: ["reverse", "invert", "flip", "backwards"],
    category: "array",
    reversible: true,
    applicableTypes: ["multiselect", "array"],
    params: [],
    summary: () => "",
  },
  join: {
    type: "join",
    get displayName() { return t("marketplace.modifiers.join.name"); },
    aliases: ["join", "join array", "concat array"],
    category: "array",
    reversible: false,
    applicableTypes: ["multiselect", "array"],
    params: [
      { key: "delimiter", get label() { return t("marketplace.modifiers.split.delimiter"); }, type: "text", required: false, default: ", " },
    ],
    summary: (p) => `"${p.delimiter ?? ", "}"`,
  },
  sort: {
    type: "sort",
    get displayName() { return t("marketplace.modifiers.sort.name"); },
    aliases: ["sort", "order", "arrange", "sorted", "alphabetical"],
    category: "array",
    reversible: false,
    applicableTypes: ["multiselect", "array"],
    params: [],
    summary: () => "",
  },
  addItems: {
    type: "addItems",
    get displayName() { return t("marketplace.modifiers.addItems.name"); },
    aliases: ["add items", "push", "extend", "concat arrays"],
    category: "array",
    reversible: false,
    applicableTypes: ["multiselect", "array"],
    params: [
      { key: "items", get label() { return t("marketplace.modifiers.addItems.items"); }, type: "tagInput", required: false, default: [] },
    ],
    summary: (p) => {
      const items = Array.isArray(p.items) ? p.items : [];
      return items.length > 0 ? `+${items.length}` : "";
    },
  },
  removeItems: {
    type: "removeItems",
    get displayName() { return t("marketplace.modifiers.removeItems.name"); },
    aliases: ["remove", "filter", "exclude", "delete", "minus"],
    category: "array",
    reversible: false,
    applicableTypes: ["multiselect", "array"],
    params: [
      { key: "items", get label() { return t("marketplace.modifiers.addItems.items"); }, type: "tagInput", required: false, default: [] },
    ],
    summary: (p) => {
      const items = Array.isArray(p.items) ? p.items : [];
      return items.length > 0 ? `-${items.length}` : "";
    },
  },
};

export const MODIFIER_CATEGORIES = {
  text: { get label() { return t("marketplace.modifierCategories.text"); }, icon: "Type" },
  number: { get label() { return t("marketplace.modifierCategories.number"); }, icon: "Hash" },
  select: { get label() { return t("marketplace.modifierCategories.select"); }, icon: "List" },
  array: { get label() { return t("marketplace.modifierCategories.array"); }, icon: "List" },
  any: { get label() { return t("marketplace.modifierCategories.any"); }, icon: "Sparkles" },
  random: { get label() { return t("marketplace.modifierCategories.random"); }, icon: "Dices" },
};

/**
 * Get available modifiers for a given field type.
 * Hides llmGenerate when instance AI is off (from last /auth/me).
 */
export function getAvailableModifiers(externalFieldType) {
  return Object.values(MODIFIER_CATALOG).filter((m) => {
    if (m.type === "llmGenerate" && getCachedMe()?.aiEnabled !== true) return false;
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
