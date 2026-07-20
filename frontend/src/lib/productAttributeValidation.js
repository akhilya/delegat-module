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
    if (attr.isRequired) {
      return z
        .string()
        .min(1, t("lib.productAttributeValidation.requiredField"))
        .refine((value) => {
          try {
            const parsed = JSON.parse(value);
            return Array.isArray(parsed) && parsed.length > 0;
          } catch {
            return value.split(",").map((part) => part.trim()).filter(Boolean).length > 0;
          }
        }, t("lib.productAttributeValidation.requiredField"));
    }
    return z.string().optional().default("");
  }

  let schema = attr.isRequired
    ? z.string().min(1, t("lib.productAttributeValidation.requiredField"))
    : z.string().optional().default("");

  if (isAttributeType(attr.type, "number")) {
    schema = schema.refine(
      (value) => value === "" || value === undefined || /^-?\d+(\.\d+)?$/.test(String(value)),
      t("lib.productAttributeValidation.enterNumber"),
    );
  }

  return schema;
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