/**
 * Product table column definitions, presets, and dynamic column builders.
 */
import { ColumnDef } from "@tanstack/vue-table";
import { h } from "vue";

import { t } from "@/i18n";
import ProductImageCell from "./ProductImageCell.vue";
import ProductIdentityCell from "./ProductIdentityCell.vue";
import BarcodeCell from "./BarcodeCell.vue";
import { Checkbox } from "@/components/ui/checkbox";
import {
  getAttributeColumnSize,
  renderAttributeValueCell,
} from "@/lib/attributeTableCell";
import { formatDateTimeCell } from "@/utils/dateTime";
import ProductAdaptersCell from "./ProductAdaptersCell.vue";
import { formatMoney } from "@/lib/currency";

/** Creates a copy icon SVG DOM element. */
function createCopyIconEl(size = 12): SVGElement {
  const ns = "http://www.w3.org/2000/svg";
  const svg = document.createElementNS(ns, "svg");
  svg.setAttribute("width", String(size));
  svg.setAttribute("height", String(size));
  svg.setAttribute("viewBox", "0 0 24 24");
  svg.setAttribute("fill", "none");
  svg.setAttribute("stroke", "currentColor");
  svg.setAttribute("stroke-width", "2");
  svg.setAttribute("stroke-linecap", "round");
  svg.setAttribute("stroke-linejoin", "round");
  const rect = document.createElementNS(ns, "rect");
  rect.setAttribute("x", "9");
  rect.setAttribute("y", "9");
  rect.setAttribute("width", "13");
  rect.setAttribute("height", "13");
  rect.setAttribute("rx", "2");
  rect.setAttribute("ry", "2");
  const path = document.createElementNS(ns, "path");
  path.setAttribute(
    "d",
    "M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1",
  );
  svg.appendChild(rect);
  svg.appendChild(path);
  return svg;
}

/** Creates a check icon SVG DOM element. */
function createCheckIconEl(size = 12): SVGElement {
  const ns = "http://www.w3.org/2000/svg";
  const svg = document.createElementNS(ns, "svg");
  svg.setAttribute("width", String(size));
  svg.setAttribute("height", String(size));
  svg.setAttribute("viewBox", "0 0 24 24");
  svg.setAttribute("fill", "none");
  svg.setAttribute("stroke", "currentColor");
  svg.setAttribute("stroke-width", "2");
  svg.setAttribute("stroke-linecap", "round");
  svg.setAttribute("stroke-linejoin", "round");
  const path = document.createElementNS(ns, "path");
  path.setAttribute("d", "M20 6 9 17l-5-5");
  svg.appendChild(path);
  return svg;
}

/** Renders a copy icon (Lucide copy) via h(). */
function renderCopyIcon(size = 12) {
  return h(
    "svg",
    {
      xmlns: "http://www.w3.org/2000/svg",
      width: String(size),
      height: String(size),
      viewBox: "0 0 24 24",
      fill: "none",
      stroke: "currentColor",
      "stroke-width": "2",
      "stroke-linecap": "round",
      "stroke-linejoin": "round",
    },
    [
      h("rect", {
        x: "9",
        y: "9",
        width: "13",
        height: "13",
        rx: "2",
        ry: "2",
      }),
      h("path", {
        d: "M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1",
      }),
    ],
  );
}

/** Compact SKU for the identity-cell subtitle (copy still works). */
function renderCaptionSku(sku: string) {
  return renderCopyableSku(
    sku,
    "!px-0 !py-0 !text-xs text-muted-foreground group-hover:bg-transparent",
  );
}

function renderIdentityCell(
  row: Product,
  options: { image?: boolean; sku?: boolean; category?: boolean } = {},
) {
  const showImage = options.image !== false;
  const showSku = options.sku === true;
  const showCategory = options.category === true;
  const category = showCategory ? row.categoryName?.trim() : "";
  const caption = !showSku && category ? category : "";

  const slots: Record<string, () => unknown> = {};
  if (showSku) {
    slots.caption = () => {
      if (!category) return renderCaptionSku(row.sku);
      return h(
        "span",
        { class: "inline-flex min-w-0 max-w-full items-center gap-1.5" },
        [
          renderCaptionSku(row.sku),
          h("span", { class: "shrink-0 text-muted-foreground/50" }, "·"),
          h("span", { class: "min-w-0 truncate" }, category),
        ],
      );
    };
  }

  return h(
    ProductIdentityCell,
    {
      name: row.name,
      caption,
      thumbnailUrl: row.primaryImageThumbnail ?? null,
      imageUrl: row.primaryImage ?? null,
      showImage,
    },
    Object.keys(slots).length ? slots : undefined,
  );
}

/** Renders a clickable SKU badge that copies to clipboard with visual feedback. */
function renderCopyableSku(sku: string, extraClass = "") {
  const baseClass =
    "font-mono text-sm group-hover:bg-muted rounded-md px-2 py-0.5 cursor-pointer transition-colors inline-flex items-center gap-1.5";
  return h(
    "span",
    {
      class: `${baseClass} ${extraClass}`.trim(),
      onClick: (e: MouseEvent) => {
        e.stopPropagation();
        navigator.clipboard.writeText(sku);
        const el = e.currentTarget as HTMLElement;
        const iconEl = el.querySelector(".sku-copy-icon") as HTMLElement;
        if (iconEl) {
          // Swap to check icon
          iconEl.replaceChildren(createCheckIconEl());
          iconEl.classList.remove(
            "opacity-0",
            "group-hover:opacity-50",
            "text-muted-foreground",
          );
          iconEl.classList.add("opacity-100", "text-success-foreground");
          setTimeout(() => {
            // Restore copy icon
            iconEl.replaceChildren(createCopyIconEl());
            iconEl.classList.add(
              "opacity-0",
              "group-hover:opacity-50",
              "text-muted-foreground",
            );
            iconEl.classList.remove("opacity-100", "text-success-foreground");
          }, 800);
        }
      },
      title: t("products.columns.copySkuTooltip"),
    },
    [
      sku,
      h(
        "span",
        {
          class:
            "sku-copy-icon opacity-0 group-hover:opacity-50 transition-opacity shrink-0 text-muted-foreground",
        },
        [renderCopyIcon()],
      ),
    ],
  );
}

/** Renders a date value as two lines: date on top, time below in muted text. */
function renderDateCell(val: string | null | undefined, timezone = "UTC") {
  if (!val) return h("span", { class: "text-muted-foreground text-sm" }, "-");
  const { date, time } = formatDateTimeCell(val, timezone);
  return h("div", { class: "flex flex-col" }, [
    h("span", { class: "text-sm tabular-nums" }, date),
    h("span", { class: "text-xs tabular-nums text-muted-foreground" }, time),
  ]);
}

/** A single store attribute value assigned to a product. */
export interface StoreAttributeValue {
  id: number;
  storeAttributeId: number;
  attributeKey: string;
  attributeName: string;
  value: string;
}

/** A store-level attribute definition. */
export interface StoreAttribute {
  id: number;
  key: string;
  name: string;
  type: number | string;
  options?: string | null;
  isMultiline?: boolean;
}

/** Product row data model used by the products table. */
export interface Product {
  id: number;
  sku: string;
  barcode: string | null;
  name: string;
  primaryImage: any;
  primaryImageThumbnail?: string;
  categoryId: number | null;
  categoryName: string | null;
  storageQuantity: number | null;
  price: number | null;
  createdAt: string;
  updatedAt: string;
  storeAttributeValues?: StoreAttributeValue[];
}

/** Extended column definition with metadata for the column selector UI. */
export interface ProductColumnDef extends ColumnDef<Product> {
  id: string;
  /** Display name shown in the column selector. */
  label?: string;
  /** Whether this column can be hidden via the column selector. */
  hideable?: boolean;
}

/** Base column definitions for the products table (select, position, image, sku, etc.). */
export const columns: ProductColumnDef[] = [
  {
    id: "select",
    size: 36,
    enableHiding: false,
    header: ({ table }) =>
      h(Checkbox, {
        modelValue:
          table.getIsAllPageRowsSelected() ||
          (table.getIsSomePageRowsSelected() && "indeterminate"),
        "onUpdate:modelValue": (value: boolean) =>
          table.toggleAllPageRowsSelected(!!value),
        "aria-label": "Select all",
        class: "translate-y-0.5 ml-2",
      }),
    cell: ({ row }) =>
      h(Checkbox, {
        modelValue: row.getIsSelected(),
        "onUpdate:modelValue": (value: boolean) => row.toggleSelected(!!value),
        "aria-label": "Select row",
        class: "translate-y-0.5 ml-2",
      }),
  },
  {
    id: "position",
    label: "№",
    hideable: true,
    header: "№",
    size: 45,
    cell: ({ row, table }) => {
      const startIndex = (table.options.meta as any)?.startIndex ?? 0;
      return h(
        "span",
        { class: "content-center text-xs text-foreground/60" },
        startIndex + row.index + 1,
      );
    },
  },
  {
    id: "image",
    label: t("products.columns.imageLabel"),
    hideable: true,
    header: "",
    cell: ({ row }) => {
      return h(ProductImageCell, {
        followTableSize: true,
        thumbnailUrl: row.original.primaryImageThumbnail ?? null,
        imageUrl: row.original.primaryImage ?? null,
      });
    },
  },
  {
    id: "sku",
    label: t("products.columns.skuLabel"),
    hideable: true,
    header: t("products.columns.skuLabel"),
    accessorKey: "sku",
    size: 80,
    cell: ({ row }) => renderCopyableSku(row.original.sku),
  },
  {
    id: "barcode",
    label: t("products.columns.barcodeLabel"),
    hideable: true,
    header: t("products.columns.barcodeLabel"),
    size: 110,
    cell: ({ row }) => {
      const barcode = row.original.barcode;
      if (!barcode) {
        return h("span", { class: "text-muted-foreground text-sm" }, "-");
      }
      return h(BarcodeCell, { barcode });
    },
  },
  {
    id: "category",
    label: t("products.columns.categoryLabel"),
    hideable: true,
    header: t("products.columns.categoryLabel"),
    cell: ({ row }) => {
      const categoryName = row.original.categoryName;
      if (!categoryName) {
        return h("span", { class: "text-muted-foreground text-sm" }, "-");
      }
      return h("span", { class: "text-sm" }, categoryName);
    },
  },
  {
    id: "storageQuantity",
    label: t("products.columns.storageQuantityLabel"),
    hideable: true,
    accessorKey: "storageQuantity",
    header: t("products.columns.storageQuantityLabel"),
    cell: ({ row }) => {
      const value = row.original.storageQuantity;
      if (value === null || value === undefined) {
        return h("span", { class: "text-muted-foreground" }, "-");
      }
      return h("span", {}, value);
    },
  },
  {
    id: "price",
    label: t("products.columns.priceLabel"),
    hideable: true,
    accessorKey: "price",
    header: t("products.columns.priceLabel"),
    cell: ({ row }) => {
      const value = row.original.price;
      if (value === null || value === undefined) {
        return h("span", { class: "text-muted-foreground" }, "-");
      }
      return h("span", {}, String(value));
    },
  },
  {
    id: "name",
    label: t("common.name"),
    hideable: true,
    accessorKey: "name",
    header: t("common.name"),
    cell: ({ row }) => {
      return h(
        "p",
        { class: "truncate text-sm font-medium tracking-tight leading-none" },
        row.getValue("name"),
      );
    },
  },
  {
    id: "adapters",
    label: t("products.columns.adaptersLabel"),
    hideable: true,
    header: t("products.columns.adaptersLabel"),
    size: 140,
    enableSorting: false,
    cell: ({ row, table }) =>
      h(ProductAdaptersCell, {
        product: row.original,
        connections: table.options.meta?.connections ?? [],
        disabled: table.options.meta?.adaptersDisabled === true,
        onChanged: table.options.meta?.onAdaptersChanged,
      }),
  },
  {
    id: "createdAt",
    label: t("products.columns.createdAtLabel"),
    hideable: true,
    header: t("products.columns.createdAtLabel"),
    size: 130,
    cell: ({ row }) => renderDateCell(row.original.createdAt),
  },
  {
    id: "updatedAt",
    label: t("products.columns.updatedAtLabel"),
    hideable: true,
    header: t("products.columns.updatedAtLabel"),
    size: 130,
    cell: ({ row }) => renderDateCell(row.original.updatedAt),
  },
];

/** Default visibility state for each column (true = visible, false = hidden). */
export const defaultVisibleColumns: Record<string, boolean> = {
  select: true,
  position: true,
  image: true,
  sku: true,
  barcode: false, // hidden by default, can be enabled in column selector
  category: true,
  storageQuantity: true,
  price: false, // hidden by default
  name: true,
  adapters: true,
  createdAt: false, // hidden by default
  updatedAt: false, // hidden by default
};

/** Returns the list of columns that can be toggled on/off in the column selector. */
export function getHideableColumns(): { id: string; label: string }[] {
  return columns
    .filter((col) => col.hideable)
    .map((col) => ({ id: col.id, label: col.label || col.id }));
}

/** Creates a dynamic table column definition for a given store attribute. */
export function createStoreAttributeColumn(
  attr: StoreAttribute,
): ProductColumnDef {
  return {
    id: `storeAttr_${attr.id}`,
    label: attr.name,
    hideable: true,
    header: attr.name,
    size: getAttributeColumnSize(attr.type),
    cell: ({ row }) => {
      const attrValue = row.original.storeAttributeValues?.find(
        (v) => v.storeAttributeId === attr.id,
      );
      return renderAttributeValueCell(attrValue?.value, attr);
    },
  };
}

/** Builds the full column list by inserting dynamic store attribute columns before the edit action column. */
export function buildColumnsWithStoreAttributes(
  storeAttributes: StoreAttribute[],
): ProductColumnDef[] {
  // Find the editAction column index
  const editActionIndex = columns.findIndex((col) => col.id === "editAction");

  // Create dynamic columns for store attributes
  const dynamicColumns = storeAttributes.map((attr) =>
    createStoreAttributeColumn(attr),
  );

  // Insert dynamic columns before editAction
  if (editActionIndex !== -1) {
    return [
      ...columns.slice(0, editActionIndex),
      ...dynamicColumns,
      ...columns.slice(editActionIndex),
    ];
  }

  // Fallback: append at end
  return [...columns, ...dynamicColumns];
}

/** Returns default column visibility map including store attribute columns (hidden by default). */
export function getDefaultVisibilityWithStoreAttributes(
  storeAttributes: StoreAttribute[],
): Record<string, boolean> {
  const visibility = { ...defaultVisibleColumns };
  storeAttributes.forEach((attr) => {
    visibility[`storeAttr_${attr.id}`] = false; // hidden by default
  });
  return visibility;
}

// Combined Column Presets

/** Identifier for a combined column preset (merges multiple columns into one). */
export type CombinedColumnPreset =
  "none" | "product" | "product_sku" | "name_sku" | "name_cat_sku";

/** Configuration for a combined column preset. */
export interface CombinedColumnConfig {
  id: CombinedColumnPreset;
  label: string;
  description: string;
  hidesColumns: string[];
}

/** Available combined column presets for the column selector. */
export const combinedColumnPresets: CombinedColumnConfig[] = [
  {
    id: "none",
    label: t("products.columns.presets.none.label"),
    description: t("products.columns.presets.none.description"),
    hidesColumns: [],
  },
  {
    id: "product",
    label: t("products.columns.presets.product.label"),
    description: t("products.columns.presets.product.description"),
    hidesColumns: ["image", "name", "category"],
  },
  {
    id: "product_sku",
    label: t("products.columns.presets.productSku.label"),
    description: t("products.columns.presets.productSku.description"),
    hidesColumns: ["image", "name", "sku", "category"],
  },
  {
    id: "name_sku",
    label: t("products.columns.presets.nameSku.label"),
    description: t("products.columns.presets.nameSku.description"),
    hidesColumns: ["name", "sku"],
  },
  {
    id: "name_cat_sku",
    label: t("products.columns.presets.nameCategorySku.label"),
    description: t("products.columns.presets.nameCategorySku.description"),
    hidesColumns: ["name", "category", "sku"],
  },
];

/** Combined column render definitions keyed by preset ID. */
const combinedColumns: Record<CombinedColumnPreset, ProductColumnDef | null> = {
  none: null,

  product: {
    id: "combined_product",
    label: t("products.columns.presets.product.label"),
    hideable: false,
    header: t("products.columns.presets.product.label"),
    size: 320,
    cell: ({ row }) =>
      renderIdentityCell(row.original, { image: true, category: true }),
  },

  product_sku: {
    id: "combined_product_sku",
    label: t("products.columns.presets.productSku.label"),
    hideable: false,
    header: t("products.columns.presets.product.label"),
    size: 360,
    cell: ({ row }) =>
      renderIdentityCell(row.original, {
        image: true,
        sku: true,
        category: true,
      }),
  },

  name_sku: {
    id: "combined_name_sku",
    label: t("products.columns.presets.nameSku.label"),
    hideable: false,
    header: t("common.name"),
    size: 300,
    cell: ({ row }) =>
      renderIdentityCell(row.original, { image: false, sku: true }),
  },

  name_cat_sku: {
    id: "combined_name_cat_sku",
    label: t("products.columns.presets.nameCategorySku.label"),
    hideable: false,
    header: t("products.columns.presets.product.label"),
    size: 320,
    cell: ({ row }) =>
      renderIdentityCell(row.original, {
        image: false,
        sku: true,
        category: true,
      }),
  },
};

function withPriceCurrency(
  col: ProductColumnDef,
  baseCurrency?: string | null,
): ProductColumnDef {
  if (col.id !== "price" || !baseCurrency) return col;
  return {
    ...col,
    cell: ({ row }) => {
      const value = row.original.price;
      if (value === null || value === undefined) {
        return h("span", { class: "text-muted-foreground" }, "-");
      }
      return h("span", {}, formatMoney(value, baseCurrency));
    },
  };
}

/** Builds the full column array applying a combined preset and appending dynamic store attribute columns. */
export function buildColumnsWithPreset(
  storeAttributes: StoreAttribute[],
  preset: CombinedColumnPreset = "none",
  baseCurrency?: string | null,
): ProductColumnDef[] {
  const presetConfig = combinedColumnPresets.find((p) => p.id === preset);
  const combinedColumn = combinedColumns[preset];

  // Filter out columns that are replaced by the combined column
  let baseColumns = columns
    .filter((col) => {
      if (!presetConfig) return true;
      return !presetConfig.hidesColumns.includes(col.id);
    })
    .map((col) => withPriceCurrency(col, baseCurrency));

  // Insert combined column after 'position' if it exists
  if (combinedColumn) {
    const positionIndex = baseColumns.findIndex((col) => col.id === "position");
    const insertIndex = positionIndex !== -1 ? positionIndex + 1 : 1;
    baseColumns = [
      ...baseColumns.slice(0, insertIndex),
      combinedColumn,
      ...baseColumns.slice(insertIndex),
    ];
  }

  // Add store attribute columns
  const dynamicColumns = storeAttributes.map((attr) =>
    createStoreAttributeColumn(attr),
  );
  const editActionIndex = baseColumns.findIndex(
    (col) => col.id === "editAction",
  );

  if (editActionIndex !== -1) {
    return [
      ...baseColumns.slice(0, editActionIndex),
      ...dynamicColumns,
      ...baseColumns.slice(editActionIndex),
    ];
  }

  return [...baseColumns, ...dynamicColumns];
}
