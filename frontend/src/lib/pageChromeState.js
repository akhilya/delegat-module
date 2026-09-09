import { ref } from "vue";

/** Live last-segment label from `setPageTitle` (product SKU, store name, …). */
export const pageTitleDetail = ref(null);

/**
 * Extra parent label when the document title is not the middle crumb
 * (connection name on nested adapter / order mailbox pages).
 */
export const breadcrumbParentLabel = ref(null);

export function setBreadcrumbParentLabel(label) {
  breadcrumbParentLabel.value = label || null;
}

export function clearPageChromeState() {
  pageTitleDetail.value = null;
  breadcrumbParentLabel.value = null;
}
