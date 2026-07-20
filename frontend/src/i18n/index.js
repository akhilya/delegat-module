import { createI18n } from "vue-i18n";
import ru from "@/locales/ru.json";
import en from "@/locales/en.json";

export const LOCALE_STORAGE_KEY = "delegat.locale";
export const SUPPORTED_LOCALES = ["ru", "en"];

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

const initialLocale = getStoredLocale();
syncDocumentLang(initialLocale);

export const i18n = createI18n({
  legacy: false,
  locale: initialLocale,
  fallbackLocale: "en",
  messages: { ru, en },
  missingWarn: false,
  fallbackWarn: false,
});

export function setLocale(locale) {
  const next = persistLocale(locale);
  i18n.global.locale.value = next;
  syncDocumentLang(next);
  return next;
}

export function getLocale() {
  return normalizeLocale(i18n.global.locale.value);
}

export function t(...args) {
  return i18n.global.t(...args);
}

/** Raw message getter (arrays/objects) — use for non-string catalog values like inputLabels lists. */
export function tm(...args) {
  return i18n.global.tm(...args);
}

export default i18n;
