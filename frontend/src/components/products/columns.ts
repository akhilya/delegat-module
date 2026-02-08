/**
 * Product table column definitions, presets, and dynamic column builders.
 */
import { ColumnDef } from "@tanstack/vue-table";
import { h } from "vue";

import ImageFile from "../imageFile.vue";
import ProductRowActionButtons from "./productRowActionButtons.vue";
import { Checkbox } from "@/components/ui/checkbox";

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
  attributeType: number;
}

/** Product row data model used by the products table. */
export interface Product {
  id: number;
  sku: string;
  name: string;
  primaryImage: any;
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
    label: "Изображение",
    hideable: true,
    header: "",
    cell: ({ row }) =>
      h(ImageFile, {
        class: "size-32",
        file: { fullPath: row.original.primaryImage },
      }),
  },
  {
    id: "sku",
    label: "Артикул",
    hideable: true,
    header: "Артикул",
    accessorKey: "sku",
    size: 80,
    cell: ({ row }) =>
      h("span", { class: "font-mono text-sm" }, row.original.sku),
  },
  {
    id: "category",
    label: "Категория",
    hideable: true,
    header: "Категория",
    cell: ({ row }) => {
      const categoryName = row.original.categoryName;
      if (!categoryName) {
        return h("span", { class: "text-muted-foreground text-sm" }, "—");
      }
      return h("span", { class: "text-sm" }, categoryName);
    },
  },
  {
    id: "storageQuantity",
    label: "На складе",
    hideable: true,
    accessorKey: "storageQuantity",
    header: "На складе",
    cell: ({ row }) => {
      const value = row.original.storageQuantity;
      if (value === null || value === undefined) {
        return h("span", { class: "text-muted-foreground" }, "—");
      }
      return h("span", {}, value);
    },
  },
  {
    id: "price",
    label: "Цена",
    hideable: true,
    accessorKey: "price",
    header: "Цена",
    cell: ({ row }) => {
      const value = row.original.price;
      if (value === null || value === undefined) {
        return h("span", { class: "text-muted-foreground" }, "—");
      }
      return h("span", {}, `${value} ₽`);
    },
  },
  {
    id: "name",
    label: "Название",
    hideable: true,
    accessorKey: "name",
    header: "Название",
    cell: ({ row }) => {
      return h("div", { class: "flex flex-col overflow-hidden" }, [
        h(
          "p",
          { class: "break-words font-medium" },
          row.getValue("name"),
        ),
      ]);
    },
  },
  {
    id: "createdAt",
    label: "Создан",
    hideable: true,
    header: "Создан",
    size: 130,
    cell: ({ row }) => {
      const val = row.original.createdAt;
      if (!val) return h("span", { class: "text-muted-foreground text-sm" }, "—");
      const d = new Date(val);
      return h("span", { class: "text-sm tabular-nums" }, d.toLocaleString("ru-RU", { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" }));
    },
  },
  {
    id: "updatedAt",
    label: "Изменён",
    hideable: true,
    header: "Изменён",
    size: 130,
    cell: ({ row }) => {
      const val = row.original.updatedAt;
      if (!val) return h("span", { class: "text-muted-foreground text-sm" }, "—");
      const d = new Date(val);
      return h("span", { class: "text-sm tabular-nums" }, d.toLocaleString("ru-RU", { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" }));
    },
  },
  {
    id: "editAction",
    enableHiding: false,
    cell: ({ row }) => {
      return h(ProductRowActionButtons, { id: row.original.id });
    },
  },
];

/** Default visibility state for each column (true = visible, false = hidden). */
export const defaultVisibleColumns: Record<string, boolean> = {
  select: true,
  position: true,
  image: true,
  sku: true,
  category: true,
  storageQuantity: true,
  price: false, // hidden by default
  name: true,
  createdAt: false, // hidden by default
  updatedAt: false, // hidden by default
  editAction: true,
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
    cell: ({ row }) => {
      const attrValue = row.original.storeAttributeValues?.find(
        (v) => v.storeAttributeId === attr.id,
      );
      if (!attrValue?.value) {
        return h("span", { class: "text-muted-foreground text-sm" }, "—");
      }
      return h("span", { class: "text-sm" }, attrValue.value);
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
  | "none"
  | "product"
  | "product_sku"
  | "name_sku"
  | "name_cat_sku";

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
    label: "Раздельные колонки",
    description: "Показывать все поля отдельно",
    hidesColumns: [],
  },
  {
    id: "product",
    label: "Товар",
    description: "Изображение + Название + Категория",
    hidesColumns: ["image", "name", "category"],
  },
  {
    id: "product_sku",
    label: "Товар + SKU",
    description: "Изображение + Название + SKU + Категория",
    hidesColumns: ["image", "name", "sku", "category"],
  },
  {
    id: "name_sku",
    label: "Название + SKU",
    description: "Название с артикулом",
    hidesColumns: ["name", "sku"],
  },
  {
    id: "name_cat_sku",
    label: "Название + Категория + SKU",
    description: "Текстовая информация о товаре",
    hidesColumns: ["name", "category", "sku"],
  },
];

/** Combined column render definitions keyed by preset ID. */
const combinedColumns: Record<CombinedColumnPreset, ProductColumnDef | null> = {
  none: null,

  product: {
    id: "combined_product",
    label: "Товар",
    hideable: false,
    header: "Товар",
    size: 320,
    cell: ({ row }) => {
      return h("div", { class: "flex gap-3 items-start py-1" }, [
        h(ImageFile, {
          class: "size-14 shrink-0 rounded",
          file: { fullPath: row.original.primaryImage },
        }),
        h("div", { class: "flex flex-col min-w-0 gap-0.5 overflow-hidden" }, [
          h(
            "p",
            { class: "font-medium text-sm leading-tight break-words" },
            row.original.name,
          ),
          row.original.categoryName
            ? h(
                "span",
                { class: "text-xs text-muted-foreground break-words" },
                row.original.categoryName,
              )
            : null,
        ]),
      ]);
    },
  },

  product_sku: {
    id: "combined_product_sku",
    label: "Товар + SKU",
    hideable: false,
    header: "Товар",
    size: 360,
    cell: ({ row }) => {
      return h("div", { class: "flex gap-3 items-start py-1" }, [
        h(ImageFile, {
          class: "size-14 shrink-0 rounded",
          file: { fullPath: row.original.primaryImage },
        }),
        h("div", { class: "flex flex-col min-w-0 gap-0.5 overflow-hidden" }, [
          h(
            "p",
            { class: "font-medium text-sm leading-tight break-words" },
            row.original.name,
          ),
          h(
            "span",
            { class: "font-mono text-xs text-muted-foreground break-all" },
            row.original.sku,
          ),
          row.original.categoryName
            ? h(
                "span",
                { class: "text-xs text-muted-foreground break-words" },
                row.original.categoryName,
              )
            : null,
        ]),
      ]);
    },
  },

  name_sku: {
    id: "combined_name_sku",
    label: "Название + SKU",
    hideable: false,
    header: "Название",
    size: 300,
    cell: ({ row }) => {
      return h("div", { class: "flex flex-col gap-0.5 overflow-hidden" }, [
        h(
          "p",
          { class: "font-medium text-sm leading-tight break-words" },
          row.original.name,
        ),
        h(
          "span",
          { class: "font-mono text-xs text-muted-foreground break-all" },
          row.original.sku,
        ),
      ]);
    },
  },

  name_cat_sku: {
    id: "combined_name_cat_sku",
    label: "Название + Категория + SKU",
    hideable: false,
    header: "Товар",
    size: 320,
    cell: ({ row }) => {
      return h("div", { class: "flex flex-col gap-0.5 overflow-hidden" }, [
        h(
          "p",
          { class: "font-medium text-sm leading-tight break-words" },
          row.original.name,
        ),
        row.original.categoryName
          ? h(
              "span",
              { class: "text-xs text-muted-foreground break-words" },
              row.original.categoryName,
            )
          : null,
        h(
          "span",
          { class: "font-mono text-xs text-muted-foreground break-all" },
          row.original.sku,
        ),
      ]);
    },
  },
};

/** Builds the full column array applying a combined preset and appending dynamic store attribute columns. */
export function buildColumnsWithPreset(
  storeAttributes: StoreAttribute[],
  preset: CombinedColumnPreset = "none",
): ProductColumnDef[] {
  const presetConfig = combinedColumnPresets.find((p) => p.id === preset);
  const combinedColumn = combinedColumns[preset];

  // Filter out columns that are replaced by the combined column
  let baseColumns = columns.filter((col) => {
    if (!presetConfig) return true;
    return !presetConfig.hidesColumns.includes(col.id);
  });

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
