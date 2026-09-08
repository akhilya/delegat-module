/**
 * Shared catalog / orders / scrape product-thumb size (small is the historical 44px).
 */

export const PRODUCT_IMAGE_SIZE_STORAGE_KEY = "delegat.productImageSize";
export const DEFAULT_PRODUCT_IMAGE_SIZE = "sm";
export const PRODUCT_IMAGE_SIZE_IDS = ["sm", "md", "lg"];

export const PRODUCT_IMAGE_SIZES = {
  sm: {
    id: "sm",
    px: 44,
    stackPeek: 10,
    boxClass: "size-11",
    thumbClass: "size-11 shrink-0 rounded-lg shadow-sm ring-2 ring-background",
    imageOnlyClass: "size-11 shrink-0 rounded-lg shadow-sm",
    identityClass: "h-11",
    rowClass: "h-[4.25rem]",
  },
  md: {
    id: "md",
    px: 64,
    stackPeek: 14,
    boxClass: "size-16",
    thumbClass: "size-16 shrink-0 rounded-lg shadow-sm ring-2 ring-background",
    imageOnlyClass: "size-16 shrink-0 rounded-lg shadow-sm",
    identityClass: "h-16",
    rowClass: "h-[5.5rem]",
  },
  lg: {
    id: "lg",
    px: 96,
    stackPeek: 22,
    boxClass: "size-24",
    thumbClass: "size-24 shrink-0 rounded-lg shadow-sm ring-2 ring-background",
    imageOnlyClass: "size-24 shrink-0 rounded-lg shadow-sm",
    identityClass: "h-24",
    rowClass: "h-[7.5rem]",
  },
};

export function parseProductImageSize(raw) {
  const id = String(raw ?? "").trim();
  return Object.hasOwn(PRODUCT_IMAGE_SIZES, id)
    ? id
    : DEFAULT_PRODUCT_IMAGE_SIZE;
}

export function productImageSizeSpec(id) {
  return PRODUCT_IMAGE_SIZES[parseProductImageSize(id)];
}

export function readStoredProductImageSize() {
  if (typeof localStorage === "undefined") return DEFAULT_PRODUCT_IMAGE_SIZE;
  try {
    return parseProductImageSize(
      localStorage.getItem(PRODUCT_IMAGE_SIZE_STORAGE_KEY),
    );
  } catch {
    return DEFAULT_PRODUCT_IMAGE_SIZE;
  }
}

export function writeStoredProductImageSize(id) {
  const next = parseProductImageSize(id);
  if (typeof localStorage === "undefined") return next;
  try {
    localStorage.setItem(PRODUCT_IMAGE_SIZE_STORAGE_KEY, next);
  } catch {
    /* private mode / quota */
  }
  return next;
}
