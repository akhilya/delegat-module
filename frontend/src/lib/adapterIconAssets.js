/**
 * Adapter brand icon assets (mirrored from each plugin icon.webp into
 * frontend/public/adapter-icons/{adapterKey}.webp).
 *
 * Source of truth: plugins/Delegat.Marketplace.<Name>/icon.webp.
 * Assets are white (any RGB) glyphs on a transparent alpha — UI tints via
 * SVG feFlood currentColor + feComposite "in" (keeps alpha, replaces RGB).
 */

/** @type {Record<string, string>} */
export const ADAPTER_ICON_URLS = {
  ozon: "/adapter-icons/ozon.webp",
  wildberries: "/adapter-icons/wildberries.webp",
  yandex: "/adapter-icons/yandex.webp",
  woocommerce: "/adapter-icons/woocommerce.webp",
  opencart: "/adapter-icons/opencart.webp",
  medusa: "/adapter-icons/medusa.webp",
  avito: "/adapter-icons/avito.webp",
  "chestny-znak": "/adapter-icons/chestny-znak.webp",
};

/**
 * @param {string|null|undefined} adapterKey
 * @returns {string|null}
 */
export function getAdapterIconUrl(adapterKey) {
  if (!adapterKey) return null;
  const key = String(adapterKey).trim().toLowerCase();
  return ADAPTER_ICON_URLS[key] ?? null;
}
