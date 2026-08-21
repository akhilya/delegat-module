/** Above this count, option lists stay as JSON (import/export) instead of a textarea. */
export const LARGE_OPTIONS_THRESHOLD = 50;

/**
 * Store-attribute types. Labels come from
 * `pages.storeAttributesPage.types.{i18nKey}`.
 */
export const STORE_ATTRIBUTE_TYPES = {
  0: { i18nKey: "text", value: 0 },
  1: { i18nKey: "number", value: 1 },
  2: { i18nKey: "boolean", value: 2 },
  3: { i18nKey: "select", value: 3 },
  4: { i18nKey: "multiSelect", value: 4 },
  5: { i18nKey: "barcode", value: 5 },
  Text: { i18nKey: "text", value: 0 },
  Number: { i18nKey: "number", value: 1 },
  Boolean: { i18nKey: "boolean", value: 2 },
  Select: { i18nKey: "select", value: 3 },
  MultiSelect: { i18nKey: "multiSelect", value: 4 },
  Barcode: { i18nKey: "barcode", value: 5 },
};

export const STORE_ATTRIBUTE_TYPE_VALUES = [0, 1, 2, 3, 4, 5];

/**
 * @param {unknown} type
 * @returns {number}
 */
export function resolveStoreAttributeTypeValue(type) {
  if (typeof type === "number" && Number.isFinite(type)) return type;
  const mapped = STORE_ATTRIBUTE_TYPES[type];
  if (mapped) return mapped.value;
  const n = Number(type);
  return Number.isFinite(n) ? n : 0;
}

/**
 * @param {unknown} type
 */
export function isOptionsAttributeType(type) {
  const value = resolveStoreAttributeTypeValue(type);
  return value === 3 || value === 4;
}

/**
 * @param {unknown} type
 */
export function isTextAttributeType(type) {
  return resolveStoreAttributeTypeValue(type) === 0;
}

/**
 * Snake_case key from a display name. Cyrillic is transliterated.
 * @param {string} name
 * @returns {string}
 */
export function generateAttributeKey(name) {
  if (!name) return "";
  return name
    .toLowerCase()
    .replace(/[^a-z\u0400-\u04FF0-9\s]/gi, "")
    .replace(/\s+/g, "_")
    .replace(/[\u0400-\u04FF]/gi, (char) => {
      const translitMap = {
        "\u0430": "a",
        "\u0431": "b",
        "\u0432": "v",
        "\u0433": "g",
        "\u0434": "d",
        "\u0435": "e",
        "\u0451": "yo",
        "\u0436": "zh",
        "\u0437": "z",
        "\u0438": "i",
        "\u0439": "y",
        "\u043a": "k",
        "\u043b": "l",
        "\u043c": "m",
        "\u043d": "n",
        "\u043e": "o",
        "\u043f": "p",
        "\u0440": "r",
        "\u0441": "s",
        "\u0442": "t",
        "\u0443": "u",
        "\u0444": "f",
        "\u0445": "h",
        "\u0446": "ts",
        "\u0447": "ch",
        "\u0448": "sh",
        "\u0449": "sch",
        "\u044a": "",
        "\u044b": "y",
        "\u044c": "",
        "\u044d": "e",
        "\u044e": "yu",
        "\u044f": "ya",
      };
      return translitMap[char.toLowerCase()] || char;
    });
}

/**
 * @param {unknown} optionsStr
 * @returns {{ value: string, label: string }[]}
 */
export function parseAttributeOptions(optionsStr) {
  if (!optionsStr) return [];

  try {
    const parsed = JSON.parse(optionsStr);
    if (Array.isArray(parsed)) {
      return parsed
        .map((item) => {
          if (typeof item === "string") {
            return { value: item, label: item };
          }
          const value = item?.value ?? item?.label ?? "";
          const label = item?.label ?? item?.value ?? "";
          return { value: String(value), label: String(label) };
        })
        .filter((option) => option.value);
    }
    if (Array.isArray(parsed?.options)) {
      return parseAttributeOptions(JSON.stringify(parsed.options));
    }
  } catch {
    // plain text / csv
  }

  const text = String(optionsStr).trim();
  if (!text) return [];
  const items = text.includes("\n") ? text.split(/\r?\n/) : text.split(",");

  return items
    .map((item) => item.trim())
    .filter(Boolean)
    .map((item) => {
      if (item.includes(":")) {
        const [value, ...labelParts] = item.split(":");
        return {
          value: value.trim(),
          label: labelParts.join(":").trim() || value.trim(),
        };
      }
      return { value: item, label: item };
    });
}

/**
 * @param {unknown} optionsStr
 * @returns {number}
 */
export function countAttributeOptions(optionsStr) {
  return parseAttributeOptions(optionsStr).length;
}

/**
 * @param {{ value: string, label: string }[]} parsed
 * @returns {string}
 */
export function formatAttributeOptionsList(parsed) {
  return parsed
    .map((option) =>
      option.value !== option.label
        ? `${option.value}:${option.label}`
        : option.label,
    )
    .join(", ");
}

/**
 * @param {unknown} optionsStr
 * @returns {string}
 */
export function formatAttributeOptionsForEdit(optionsStr) {
  if (!optionsStr) return "";
  const parsed = parseAttributeOptions(optionsStr);
  if (parsed.length > LARGE_OPTIONS_THRESHOLD) return "";
  return formatAttributeOptionsList(parsed);
}

/**
 * @param {string} raw
 * @returns {string|null}
 */
export function serializeAttributeOptions(raw) {
  const parsed = parseAttributeOptions(String(raw ?? "").trim());
  if (parsed.length === 0) return null;
  return JSON.stringify(parsed);
}

/**
 * Caption under a global-attribute row (type · key · N options).
 * @param {object} attribute
 * @param {{ typeLabel?: string, optionsWord?: string }} labels
 */
export function storeAttributeRowCaption(attribute, labels = {}) {
  const typeLabel = labels.typeLabel || "";
  const optionsWord = labels.optionsWord || "options";
  const parts = [typeLabel, attribute?.key].filter(Boolean);
  const count = countAttributeOptions(attribute?.options);
  if (count > 0) {
    parts.push(`${count} ${optionsWord}`);
  }
  return parts.join(" · ");
}

export function emptyStoreAttributeForm() {
  return {
    name: "",
    key: "",
    type: 0,
    isRequired: false,
    isUnique: false,
    isMultiline: false,
    showOnProductPage: true,
    options: "",
    defaultValue: "",
    description: "",
    descriptionInTooltip: false,
  };
}

/**
 * @param {object} attribute
 */
export function formFromStoreAttribute(attribute) {
  const type = resolveStoreAttributeTypeValue(attribute?.type);
  const optionsRaw = attribute?.options || "";
  const optionsCount = countAttributeOptions(optionsRaw);
  const large = optionsCount > LARGE_OPTIONS_THRESHOLD;
  return {
    form: {
      name: attribute?.name || "",
      key: attribute?.key || "",
      type,
      isRequired: !!attribute?.isRequired,
      isUnique: attribute?.isUnique === true,
      isMultiline: attribute?.isMultiline ?? false,
      showOnProductPage:
        !!attribute?.isRequired || attribute?.showOnProductPage !== false,
      options: large ? "" : formatAttributeOptionsForEdit(optionsRaw),
      defaultValue: attribute?.defaultValue || "",
      description: attribute?.description || "",
      descriptionInTooltip: attribute?.descriptionInTooltip === true,
    },
    optionsJson: large && optionsRaw ? optionsRaw : null,
    optionsCount,
  };
}
