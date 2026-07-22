/**
 * Fill marketplace product edit URL templates like
 * `https://…/business/{businessId}/assortment?offerId={externalId}`.
 * Returns null if any `{placeholder}` remains unresolved.
 */
export function fillProductEditUrl(template, vars = {}) {
  if (!template || typeof template !== "string") return null;

  const values = {};
  for (const [key, value] of Object.entries(vars || {})) {
    if (value == null) continue;
    const text = String(value).trim();
    if (!text) continue;
    values[key] = text;
  }

  let url = template;
  for (const [key, value] of Object.entries(values)) {
    url = url.replaceAll(`{${key}}`, encodeURIComponent(value));
  }

  if (/\{[a-zA-Z0-9_]+\}/.test(url)) return null;
  return url;
}
