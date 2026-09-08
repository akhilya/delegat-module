import { computed, ref } from "vue";
import {
  DEFAULT_PRODUCT_IMAGE_SIZE,
  parseProductImageSize,
  productImageSizeSpec,
  readStoredProductImageSize,
  writeStoredProductImageSize,
} from "@/lib/productImageSize";

export const productImageSizeId = ref(
  typeof localStorage === "undefined"
    ? DEFAULT_PRODUCT_IMAGE_SIZE
    : readStoredProductImageSize(),
);

export function useProductImageSize() {
  const spec = computed(() => productImageSizeSpec(productImageSizeId.value));

  function setSize(id) {
    const next = parseProductImageSize(id);
    productImageSizeId.value = next;
    writeStoredProductImageSize(next);
  }

  return { sizeId: productImageSizeId, spec, setSize };
}
