/**
 * Adapter brand icons — URL helpers + sidebar component factory.
 * Tint logic: AdapterIcon.vue (keep alpha, paint currentColor).
 */
import { defineComponent, h } from "vue";
import AdapterIcon from "@/components/marketplace/AdapterIcon.vue";
import { getAdapterIconUrl } from "@/lib/adapterIconAssets";

export { ADAPTER_ICON_URLS, getAdapterIconUrl } from "@/lib/adapterIconAssets";

/**
 * @param {string|null|undefined} adapterKey
 * @param {object} [Fallback]
 */
export function adapterIconComponent(adapterKey, Fallback) {
  if (!getAdapterIconUrl(adapterKey)) {
    return Fallback;
  }
  const key = String(adapterKey).trim().toLowerCase();
  return defineComponent({
    name: `AdapterIconNav_${key.replace(/[^a-z0-9]+/gi, "_")}`,
    setup() {
      return () =>
        h(AdapterIcon, {
          adapterKey: key,
          class: "size-4",
          fallback: false,
        });
    },
  });
}
