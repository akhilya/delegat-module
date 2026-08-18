import { z } from "zod";
import { t } from "@/i18n";

export const TYPE_TEXT = [0, "Text", "text"];
export const TYPE_NUMBER = [1, "Number", "number"];
export const TYPE_BOOLEAN = [2, "Boolean", "boolean"];
export const TYPE_SELECT = [3, "Select", "select"];
export const TYPE_MULTISELECT = [4, "MultiSelect", "multiSelect", "multiselect"];
export const TYPE_BARCODE = [5, "Barcode", "barcode"];

const TYPE_MAP = {
  text: TYPE_TEXT,
  number: TYPE_NUMBER,
  boolean: TYPE_BOOLEAN,
  select: TYPE_SELECT,
  multiselect: TYPE_MULTISELECT,
  barcode: TYPE_BARCODE,
};

export function isAttributeType(attrType, typeName) {
  return TYPE_MAP[typeName]?.includes(attrType) ?? false;
}

function buildSingleAttributeSchema(attr) {
  if (isAttributeType(attr.type, "boolean")) {
    return z.enum(["true", "false"]).default("false");
  }

  if (isAttributeType(attr.type, "multiselect")) {
    let schema = attr.isRequired
      ? z
          .string()
          .min(1, t("lib.productAttributeValidation.requiredField"))
          .refine(
            (value) => countMultiselectValues(value) > 0,
            t("lib.productAttributeValidation.requiredField"),
          )
      : z.string().optional().default("");

    const maxValues = positiveInt(attr.maxValues);
    if (maxValues) {
      schema = schema.refine(
        (value) => countMultiselectValues(value) <= maxValues,
        t("lib.productAttributeValidation.tooManyValues", { max: maxValues }),
      );
    }
    return schema;
  }

  let schema = attr.isRequired
    ? z.string().min(1, t("lib.productAttributeValidation.requiredField"))
    : z.string().optional().default("");

  const maxLen = positiveInt(attr.maxLength);
  if (
    maxLen &&
    !isAttributeType(attr.type, "boolean") &&
    !isAttributeType(attr.type, "multiselect")
  ) {
    schema = schema.refine(
      (value) => value === "" || value === undefined || String(value).length <= maxLen,
      t("lib.productAttributeValidation.tooLong", { max: maxLen }),
    );
  }

  if (isAttributeType(attr.type, "number")) {
    schema = schema.refine(
      (value) => value === "" || value === undefined || /^-?\d+(\.\d+)?$/.test(String(value)),
      t("lib.productAttributeValidation.enterNumber"),
    );
    const min = optionalNumber(attr.minValue);
    const max = optionalNumber(attr.maxValue);
    if (min != null || max != null) {
      schema = schema.refine((value) => {
        if (value === "" || value === undefined) return true;
        const n = Number(value);
        if (!Number.isFinite(n)) return true;
        if (min != null && n < min) return false;
        if (max != null && n > max) return false;
        return true;
      }, numberRangeMessage(min, max));
    }
  }

  const pattern = typeof attr.validationPattern === "string"
    ? attr.validationPattern.trim()
    : "";
  if (pattern && !isAttributeType(attr.type, "boolean")) {
    try {
      const re = new RegExp(pattern);
      schema = schema.refine(
        (value) => value === "" || value === undefined || re.test(String(value)),
        t("lib.productAttributeValidation.patternMismatch"),
      );
    } catch {
      // Marketplace regex that is not valid in JS — skip client-side pattern check.
    }
  }

  return schema;
}

function positiveInt(value) {
  const n = Number(value);
  return Number.isInteger(n) && n > 0 ? n : null;
}

/** Number of selected values in a stored multiselect string (JSON array or CSV). */
export function countMultiselectValues(raw) {
  if (raw === "" || raw == null) return 0;
  try {
    const parsed = JSON.parse(raw);
    if (Array.isArray(parsed)) return parsed.length;
  } catch {
    /* CSV fallback below */
  }
  return String(raw)
    .split(",")
    .map((part) => part.trim())
    .filter(Boolean).length;
}

function optionalNumber(value) {
  if (value == null || value === "") return null;
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
}

function numberRangeMessage(min, max) {
  if (min != null && max != null) {
    return t("lib.productAttributeValidation.numberRange", { min, max });
  }
  if (min != null) {
    return t("lib.productAttributeValidation.minValue", { min });
  }
  return t("lib.productAttributeValidation.maxValue", { max });
}

export function buildAttributesSchema(attributes) {
  const visible = (attributes || []).filter((attr) => attr.showOnProductPage !== false);
  if (visible.length === 0) {
    return z.record(z.string()).optional().default({});
  }

  const shape = {};
  for (const attr of visible) {
    shape[String(attr.id)] = buildSingleAttributeSchema(attr);
  }
  return z.object(shape);
}

/** True when a required attribute value is missing / empty (booleans always count as filled). */
export function isEmptyAttributeValue(attr, raw) {
  if (isAttributeType(attr.type, "boolean")) return false;
  if (raw === "" || raw == null) return true;
  if (isAttributeType(attr.type, "multiselect")) {
    return countMultiselectValues(raw) === 0;
  }
  return false;
}

/** Required visible attributes that still have an empty value in the form record. */
export function listMissingRequiredAttributes(attributes, formRecord) {
  const values = formRecord || {};
  const missing = [];
  for (const attr of attributes || []) {
    if (attr.showOnProductPage === false) continue;
    if (!attr.isRequired) continue;
    const id = String(attr.id);
    const raw = values[id] ?? values[attr.id];
    if (isEmptyAttributeValue(attr, raw)) missing.push(attr);
  }
  return missing;
}

export function attributesToFormRecord(attributes, existingValues = [], idKey = "attributeId") {
  const valueMap = {};

  if (Array.isArray(existingValues)) {
    for (const item of existingValues) {
      valueMap[item[idKey]] = item.value;
    }
  } else if (existingValues && typeof existingValues === "object") {
    for (const [id, value] of Object.entries(existingValues)) {
      valueMap[Number(id)] = value;
    }
  }

  const record = {};
  for (const attr of (attributes || []).filter((item) => item.showOnProductPage !== false)) {
    const existing = valueMap[attr.id];
    const hasExistingValue = existing !== undefined
      && existing !== null
      && (existing !== "" || isAttributeType(attr.type, "boolean"));

    if (hasExistingValue) {
      record[String(attr.id)] = String(existing);
    } else if (attr.defaultValue) {
      record[String(attr.id)] = String(attr.defaultValue);
    } else if (isAttributeType(attr.type, "boolean")) {
      record[String(attr.id)] = "false";
    } else {
      record[String(attr.id)] = "";
    }
  }

  return record;
}

function isPresentAttributeValue(value) {
  return value !== "" && value !== null && value !== undefined;
}

export function formRecordToAttributeValues(record, idKey = "attributeId") {
  const idField = idKey === "attributeId" ? "attributeId" : "storeAttributeId";

  return Object.entries(record || {})
    .filter(([, value]) => isPresentAttributeValue(value))
    .map(([id, value]) => ({
      [idField]: Number(id),
      value: String(value),
    }));
}

export function mergeCategoryAttributesIntoCache(cache, formRecord) {
  const next = { ...cache };

  for (const [id, value] of Object.entries(formRecord || {})) {
    const attributeId = Number(id);
    if (isPresentAttributeValue(value)) {
      next[attributeId] = String(value);
    } else {
      delete next[attributeId];
    }
  }

  return next;
}

export function categoryAttributeCacheToValues(cache) {
  return Object.entries(cache || {})
    .filter(([, value]) => isPresentAttributeValue(value))
    .map(([id, value]) => ({
      attributeId: Number(id),
      value: String(value),
    }));
}