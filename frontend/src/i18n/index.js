import { createI18n } from "vue-i18n";
import ru from "@/locales/ru.json";

export const LOCALE_STORAGE_KEY = "delegat.locale";
export const SUPPORTED_LOCALES = ["ru", "en"];

/** @type {Record<string, () => Promise<Record<string, unknown>>>} */
const localeLoaders = {
  ru: () => Promise.resolve(ru),
  en: () => import("@/locales/en.json").then((m) => m.default),
};

export function normalizeLocale(value) {
  const raw = String(value || "").toLowerCase().slice(0, 2);
  return SUPPORTED_LOCALES.includes(raw) ? raw : "ru";
}

export function getStoredLocale() {
  try {
    return normalizeLocale(localStorage.getItem(LOCALE_STORAGE_KEY));
  } catch {
    return "ru";
  }
}

export function persistLocale(locale) {
  const next = normalizeLocale(locale);
  try {
    localStorage.setItem(LOCALE_STORAGE_KEY, next);
  } catch {
    // ignore quota / private mode
  }
  return next;
}

/** BCP 47 tag for Intl formatting */
export function toBcp47(locale) {
  return normalizeLocale(locale) === "en" ? "en-US" : "ru-RU";
}

export function syncDocumentLang(locale) {
  if (typeof document !== "undefined") {
    document.documentElement.lang = normalizeLocale(locale);
  }
}

/** Locales already registered in i18n.messages (ru is always in the entry chunk). */
const loadedLocales = new Set(["ru"]);

// Start on ru (always available). Non-ru locales load via bootstrapLocale() before mount.
export const i18n = createI18n({
  legacy: false,
  locale: "ru",
  fallbackLocale: "ru",
  messages: { ru },
  missingWarn: false,
  fallbackWarn: false,
});

async function ensureLocaleLoaded(locale) {
  const next = normalizeLocale(locale);
  if (loadedLocales.has(next)) return;
  const messages = await localeLoaders[next]();
  i18n.global.setLocaleMessage(next, messages);
  loadedLocales.add(next);
}

/**
 * Switch UI locale. Loads message catalog on demand (ru ships in entry; others lazy).
 * @returns {Promise<string>}
 */
export async function setLocale(locale) {
  const next = persistLocale(locale);
  await ensureLocaleLoaded(next);
  i18n.global.locale.value = next;
  syncDocumentLang(next);
  return next;
}

/**
 * Apply stored locale before first paint (call before app.mount).
 * @returns {Promise<string>}
 */
export function bootstrapLocale() {
  const stored = getStoredLocale();
  if (stored === "ru") {
    syncDocumentLang("ru");
    return Promise.resolve("ru");
  }
  return setLocale(stored);
}

export function getLocale() {
  return normalizeLocale(i18n.global.locale.value);
}

export function t(...args) {
  return i18n.global.t(...args);
}

/** Raw message getter (arrays/objects) - use for non-string catalog values like inputLabels lists. */
export function tm(...args) {
  return i18n.global.tm(...args);
}

export default i18n;
