/**
 * Pin-type helpers and op-node specs for the mapping graph.
 * No i18n / Vue so node:test can import this module.
 */

export const PIN_COLORS = {
  string: "#3b82f6",
  select: "#3b82f6",
  number: "#10b981",
  integer: "#10b981",
  boolean: "#f59e0b",
  multiselect: "#8b5cf6",
  array: "#8b5cf6",
  any: "#9ca3af",
};

export const pinColor = (type) => PIN_COLORS[type] || PIN_COLORS.any;

/** Catalog categories without pulling modifierCatalog.js (i18n). */
export const MODIFIER_CATEGORIES = {
  truncate: "text",
  split: "text",
  replace: "text",
  regexReplace: "text",
  regexExtract: "text",
  append: "text",
  prepend: "text",
  uppercase: "text",
  lowercase: "text",
  trim: "text",
  unitConversion: "number",
  multiply: "number",
  divide: "number",
  add: "number",
  subtract: "number",
  round: "number",
  floor: "number",
  ceiling: "number",
  clamp: "number",
  valueMap: "text",
  optionMap: "select",
  llmGenerate: "text",
  randomInt: "number",
  randomFloat: "number",
  randomPick: "any",
  randomUuid: "any",
  toText: "any",
  forceType: "any",
  template: "text",
  defaultValue: "any",
  takeFirst: "array",
  limit: "array",
  skip: "array",
  reverse: "array",
  join: "array",
  sort: "array",
  addItems: "array",
  removeItems: "array",
};

/**
 * Output pin type of a modifier node given its input type.
 * forceType resolves to its `type` param (handled by the node component).
 */
export function modifierOutputType(modifierType, inputType) {
  switch (modifierType) {
    case "toText":
    case "join":
    case "template":
    case "takeFirst":
      return "string";
    case "forceType":
      return inputType || "any";
  }
  const category = MODIFIER_CATEGORIES[modifierType];
  if (category === "number") return "number";
  if (category === "text") return "string";
  if (category === "array") return "multiselect";
  if (category === "select") return inputType || "select";
  return inputType || "any";
}

export const TYPE_SYNONYMS = {
  string: "string",
  text: "string",
  select: "string",
  number: "number",
  integer: "number",
  array: "array",
  multiselect: "array",
  boolean: "boolean",
};

/** Lenient pin-type compatibility (equal, either "any", or same synonym group). */
export function typeCompatible(a, b) {
  if (!a || !b || a === "any" || b === "any") return true;
  return (TYPE_SYNONYMS[a] || a) === (TYPE_SYNONYMS[b] || b);
}

export const OP_SPECS = {
  formatString: {
    aliases: ["format", "sprintf", "template", "format string"],
    icon: "Braces",
    inputs: "multi",
    output: "string",
    accepts: null,
    params: [{ key: "template", type: "text", default: "{0}x{1}" }],
  },
  join: {
    aliases: ["join", "concat", "merge", "combine"],
    icon: "Braces",
    inputs: "multi",
    output: "string",
    accepts: null,
    params: [{ key: "delimiter", type: "text", default: "x" }],
  },
  makeArray: {
    aliases: ["array", "create", "new array", "make array", "collection"],
    icon: "List",
    inputs: "multi",
    output: "array",
    accepts: null,
    params: [],
  },
  arrayAppend: {
    aliases: ["push", "append", "add", "insert"],
    icon: "List",
    inputs: 2,
    output: "array",
    accepts: null,
    params: [],
  },
  splitToArray: {
    aliases: ["split", "explode", "parse", "separate"],
    icon: "List",
    inputs: "single",
    output: "array",
    accepts: ["string", "text", "select"],
    params: [{ key: "delimiter", type: "text", default: "," }],
  },
  arrayIndex: {
    aliases: ["index", "get", "nth", "at", "element"],
    icon: "List",
    inputs: "single",
    output: "string",
    accepts: ["array", "multiselect"],
    params: [{ key: "index", type: "number", default: 0 }],
  },
  arrayLength: {
    aliases: ["length", "count", "size", "len"],
    icon: "List",
    inputs: "single",
    output: "number",
    accepts: ["array", "multiselect"],
    params: [],
  },
  ifElse: {
    aliases: ["if", "condition", "ternary", "when"],
    icon: "GitBranch",
    category: "logic",
    inputs: 3,
    output: "any",
    accepts: null,
    params: [],
  },
  not: {
    aliases: ["not", "negate", "invert", "!"],
    icon: "Binary",
    category: "logic",
    inputs: "single",
    output: "boolean",
    accepts: null,
    params: [],
  },
  and: {
    aliases: ["and", "&&", "all", "both"],
    icon: "Binary",
    category: "logic",
    inputs: "multi",
    output: "boolean",
    accepts: null,
    params: [],
  },
  or: {
    aliases: ["or", "||", "any", "OR", "either"],
    icon: "Binary",
    category: "logic",
    inputs: "multi",
    output: "boolean",
    accepts: null,
    params: [],
  },
  equals: {
    aliases: ["equals", "equal", "==", "compare", "eq"],
    icon: "Equal",
    category: "logic",
    inputs: 2,
    output: "boolean",
    accepts: null,
    params: [],
  },
  notEquals: {
    aliases: ["not equal", "!=", "different", "neq"],
    icon: "Equal",
    category: "logic",
    inputs: 2,
    output: "boolean",
    accepts: null,
    params: [],
  },
  greaterThan: {
    aliases: ["greater", ">", "more than", "gt"],
    icon: "Binary",
    category: "logic",
    inputs: 2,
    output: "boolean",
    accepts: ["number", "integer"],
    params: [],
  },
  lessThan: {
    aliases: ["less", "<", "less than", "lt"],
    icon: "Binary",
    category: "logic",
    inputs: 2,
    output: "boolean",
    accepts: ["number", "integer"],
    params: [],
  },
  contains: {
    aliases: ["contains", "includes", "indexOf", "has", "search", "include"],
    icon: "Binary",
    category: "logic",
    inputs: 2,
    output: "boolean",
    accepts: null,
    params: [],
  },
  isEmpty: {
    aliases: ["empty", "null", "nil", "undefined", "is empty", "isnull"],
    icon: "Binary",
    category: "logic",
    inputs: "single",
    output: "boolean",
    accepts: null,
    params: [],
  },
  isValid: {
    aliases: ["filled", "not empty", "has value", "not null", "isnotnull"],
    icon: "Binary",
    category: "logic",
    inputs: "single",
    output: "boolean",
    accepts: null,
    params: [],
  },
};

export const OP_NODE_TYPES = Object.keys(OP_SPECS);
