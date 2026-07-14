/**
 * Type-aware read-only cell rendering for attribute values in data tables.
 */
import { h, type VNode } from "vue";
import { Check, X } from "lucide-vue-next";

import BarcodeCell from "@/components/products/BarcodeCell.vue";
import { isAttributeType } from "@/lib/productAttributeValidation.js";

export interface AttributeTableColumnDef {
  type: number | string;
  options?: string | null;
  isMultiline?: boolean;
}

function parseSelectOptions(
  options?: string | null,
): { value: string; label: string }[] {
  if (!options) return [];
  try {
    const parsed = JSON.parse(options);
    if (Array.isArray(parsed)) {
      return parsed.map((option) => ({
        value: String(option.value ?? option.label ?? option),
        label: String(option.label ?? option.value ?? option),
      }));
    }
  } catch {
    // Not JSON — fall through to comma-separated parsing.
  }
  return options
    .split(",")
    .map((part) => part.trim())
    .filter(Boolean)
    .map((label) => ({ value: label, label }));
}

function parseMultiSelectValue(value: string): string[] {
  try {
    const parsed = JSON.parse(value);
    if (Array.isArray(parsed)) {
      return parsed.map((item) => String(item));
    }
    return [String(parsed)];
  } catch {
    return value
      .split(",")
      .map((part) => part.trim())
      .filter(Boolean);
  }
}

function resolveSelectLabel(
  value: string,
  options?: string | null,
): string {
  const match = parseSelectOptions(options).find(
    (option) => option.value === value,
  );
  return match?.label ?? value;
}

function renderEmptyCell(): VNode {
  return h("span", { class: "text-muted-foreground text-sm" }, "—");
}

/** Renders a table cell for a single attribute value based on its type definition. */
export function renderAttributeValueCell(
  value: string | null | undefined,
  attr: AttributeTableColumnDef,
): VNode {
  if (value === null || value === undefined || value === "") {
    return renderEmptyCell();
  }

  if (isAttributeType(attr.type, "barcode")) {
    return h(BarcodeCell, { barcode: value });
  }

  if (isAttributeType(attr.type, "boolean")) {
    const normalized = value.toLowerCase();
    const isTrue = normalized === "true" || normalized === "1";
    return isTrue
      ? h(Check, { class: "size-5 text-muted-foreground" })
      : h(X, { class: "size-5 text-muted-foreground" });
  }

  if (isAttributeType(attr.type, "multiselect")) {
    const items = parseMultiSelectValue(value);
    if (items.length === 0) {
      return renderEmptyCell();
    }
    return h(
      "div",
      { class: "flex flex-wrap gap-1 max-w-full" },
      items.map((item) =>
        h(
          "span",
          {
            class:
              "inline-flex items-center rounded-md bg-muted px-1.5 py-0.5 text-xs text-foreground/90",
          },
          item,
        ),
      ),
    );
  }

  if (isAttributeType(attr.type, "select")) {
    return h(
      "span",
      { class: "text-sm" },
      resolveSelectLabel(value, attr.options),
    );
  }

  if (isAttributeType(attr.type, "number")) {
    return h("span", { class: "text-sm tabular-nums" }, value);
  }

  if (attr.isMultiline) {
    return h(
      "span",
      {
        class: "text-sm line-clamp-2 whitespace-pre-wrap break-words",
        title: value,
      },
      value,
    );
  }

  return h("span", { class: "text-sm break-words" }, value);
}

/** Suggested column width for an attribute type in the products table. */
export function getAttributeColumnSize(type: number | string): number {
  if (isAttributeType(type, "boolean")) return 80;
  if (isAttributeType(type, "barcode")) return 110;
  if (isAttributeType(type, "multiselect")) return 160;
  return 120;
}