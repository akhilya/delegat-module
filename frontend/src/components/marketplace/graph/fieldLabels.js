import { i18n } from "@/i18n";

/** Local product / stock / price source keys from available-sources. */
export function localizeStandardSourceName(key, fallback) {
  if (!key) return fallback || "";
  const path = `marketplace.standardSources.${key}`;
  return i18n.global.te(path) ? i18n.global.t(path) : fallback || key;
}

/**
 * Marketplace adapter field keys that plugins hardcode in English
 * (Ozon/WB/Yandex standard fields) while category attrs from the API are localized.
 */
export function localizeExternalFieldName(key, fallback) {
  if (!key) return fallback || "";
  const path = `marketplace.externalFieldNames.${key}`;
  return i18n.global.te(path) ? i18n.global.t(path) : fallback || key;
}

export function localizeExternalFieldDescription(key, fallback) {
  if (!key) return fallback || null;
  const path = `marketplace.externalFieldDescriptions.${key}`;
  return i18n.global.te(path) ? i18n.global.t(path) : fallback || null;
}
