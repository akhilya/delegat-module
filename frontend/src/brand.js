import { computed, reactive, readonly } from "vue";
import { t } from "@/i18n";
import { storeIconUrl } from "@/lib/storeIcon.js";

const DEFAULT_NAME = import.meta.env.VITE_BRAND_NAME || "Delegat.Module";
/** No default logo — show brand name only until an upload or BRAND_LOGO_URL seed. */
const DEFAULT_LOGO = (import.meta.env.VITE_BRAND_LOGO || "").trim();
const DEFAULT_DESCRIPTION =
  import.meta.env.VITE_BRAND_DESCRIPTION || `${DEFAULT_NAME} - ${t("common.brandDescription")}`;

/** Design.css defaults — used when colors are cleared. */
const CSS_DEFAULTS = {
  primary: "oklch(0.205 0 0)",
  primaryForeground: "oklch(0.985 0 0)",
  accent: "oklch(0.97 0 0)",
  accentForeground: "oklch(0.205 0 0)",
};

const LEGACY_LOGO = "/logo@0,25x.webp";

function normalizeLogoUrl(url) {
  if (!url || typeof url !== "string") return "";
  const trimmed = url.trim();
  if (!trimmed || trimmed === LEGACY_LOGO) return "";
  return trimmed;
}

function normalizeFaviconUrl(url) {
  const fromEnv = (import.meta.env.VITE_BRAND_FAVICON || "").trim();
  if (url === undefined || url === null) return storeIconUrl(fromEnv);
  return storeIconUrl(url) || storeIconUrl(fromEnv);
}

const state = reactive({
  name: DEFAULT_NAME,
  description: DEFAULT_DESCRIPTION,
  logoUrl: normalizeLogoUrl(DEFAULT_LOGO),
  faviconUrl: normalizeFaviconUrl(""),
  hasCustomLogo: false,
  hasCustomFavicon: false,
  primaryColor: null,
  accentColor: null,
  updatedAt: null,
  storeId: null,
  loaded: false,
});

let loadPromise = null;

/** @deprecated Prefer getBrandName() / useBrand() — kept for gradual migration. */
export const BRAND_NAME = DEFAULT_NAME;
/** @deprecated Prefer useBrand().description */
export const BRAND_DESCRIPTION = DEFAULT_DESCRIPTION;
/** @deprecated Prefer useBrand().logoUrl */
export const BRAND_LOGO = normalizeLogoUrl(DEFAULT_LOGO);
/** @deprecated Prefer useBrand().faviconUrl */
export const BRAND_FAVICON = normalizeFaviconUrl("");

export function getBrandName() {
  return state.name;
}

export function getBrandLogo() {
  return state.logoUrl;
}

export function getBrandFavicon() {
  return state.faviconUrl;
}

export function getBrandDescription() {
  return state.description || DEFAULT_DESCRIPTION;
}

export function useBrand() {
  return readonly(state);
}

export const brandName = computed(() => state.name);
export const brandLogo = computed(() => state.logoUrl);
export const brandFavicon = computed(() => state.faviconUrl);
export const brandDescription = computed(() => state.description || DEFAULT_DESCRIPTION);

/**
 * Apply a branding DTO from the API (or modal save).
 * @param {object} dto
 */
export function applyBranding(dto) {
  if (!dto || typeof dto !== "object") return;

  if (typeof dto.name === "string" && dto.name.trim()) {
    state.name = dto.name.trim();
  }
  // Keep a static meta description from env/i18n — brand description field was removed.
  state.description = `${state.name} - ${t("common.brandDescription")}`;
  if (dto.logoUrl !== undefined) {
    state.logoUrl = normalizeLogoUrl(dto.logoUrl);
  }
  if (dto.faviconUrl !== undefined) {
    state.faviconUrl = normalizeFaviconUrl(dto.faviconUrl);
  }
  state.hasCustomLogo = dto.hasCustomLogo === true;
  state.hasCustomFavicon = dto.hasCustomFavicon === true;
  state.primaryColor = normalizeHex(dto.primaryColor);
  state.accentColor = normalizeHex(dto.accentColor);
  state.updatedAt = dto.updatedAt ?? null;
  state.storeId = dto.storeId ?? null;
  state.loaded = true;

  applyDocumentChrome();
  applyThemeColors();
  syncDocumentTitle();
}

/** Optional provider for the page/detail segment of the document title (registered by router). */
let pageTitleDetailProvider = () => null;
let documentIconSuppressed = false;

/**
 * Hide the tab icon on unbranded system screens (API down, many stores).
 * @param {boolean} suppressed
 */
export function setDocumentIconSuppressed(suppressed) {
  documentIconSuppressed = Boolean(suppressed);
  applyDocumentChrome();
}

/**
 * Router registers how to resolve the non-brand part of the tab title
 * (route meta title or setPageTitle detail).
 * @param {() => string|null} fn
 */
export function setPageTitleDetailProvider(fn) {
  pageTitleDetailProvider = typeof fn === "function" ? fn : () => null;
}

/**
 * Set document.title to `{detail} - {brand}` or just `{brand}`.
 * @param {string|null|undefined} [explicitDetail] - when provided, use this instead of the provider
 */
export function syncDocumentTitle(explicitDetail) {
  if (typeof document === "undefined") return;
  const brand = state.name || DEFAULT_NAME;
  const detail =
    explicitDetail !== undefined ? explicitDetail || null : pageTitleDetailProvider();
  document.title = detail ? `${detail} - ${brand}` : brand;
}

function normalizeHex(value) {
  if (!value || typeof value !== "string") return null;
  const trimmed = value.trim();
  if (/^#[0-9A-Fa-f]{6}$/.test(trimmed)) return trimmed.toUpperCase();
  if (/^[0-9A-Fa-f]{6}$/.test(trimmed)) return `#${trimmed.toUpperCase()}`;
  return null;
}

function applyDocumentChrome() {
  if (typeof document === "undefined") return;
  const favicon = documentIconSuppressed
    ? ""
    : storeIconUrl(state.faviconUrl);
  let link = document.querySelector("link[rel='icon']");
  if (!favicon) {
    link?.remove();
  } else {
    if (!link) {
      link = document.createElement("link");
      link.setAttribute("rel", "icon");
      document.head.appendChild(link);
    }
    if (link.getAttribute("href") !== favicon) {
      link.setAttribute("href", favicon);
    }
  }

  const meta = document.querySelector('meta[name="description"]');
  if (meta) {
    meta.setAttribute("content", getBrandDescription());
  }
}

function relativeLuminance(hex) {
  const raw = hex.replace("#", "");
  const r = parseInt(raw.slice(0, 2), 16) / 255;
  const g = parseInt(raw.slice(2, 4), 16) / 255;
  const b = parseInt(raw.slice(4, 6), 16) / 255;
  const lin = (c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4);
  return 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b);
}

function foregroundFor(hex) {
  return relativeLuminance(hex) > 0.45 ? "#0A0A0A" : "#FAFAFA";
}

function applyThemeColors() {
  const root = document.documentElement;
  if (state.primaryColor) {
    root.style.setProperty("--primary", state.primaryColor);
    root.style.setProperty("--primary-foreground", foregroundFor(state.primaryColor));
  } else {
    root.style.setProperty("--primary", CSS_DEFAULTS.primary);
    root.style.setProperty("--primary-foreground", CSS_DEFAULTS.primaryForeground);
  }

  if (state.accentColor) {
    root.style.setProperty("--accent", state.accentColor);
    root.style.setProperty("--accent-foreground", foregroundFor(state.accentColor));
  } else {
    root.style.setProperty("--accent", CSS_DEFAULTS.accent);
    root.style.setProperty("--accent-foreground", CSS_DEFAULTS.accentForeground);
  }
}

/**
 * Fetch branding from API. Safe to call multiple times; dedupes in-flight.
 * @param {import('axios').AxiosInstance} http
 */
export async function loadBranding(http) {
  if (loadPromise) return loadPromise;

  loadPromise = http
    .get("/settings/branding", { cache: false })
    .then((response) => {
      applyBranding(response.data);
      return state;
    })
    .catch(() => {
      // Keep Vite/env defaults when API is offline (e.g. unavailable page).
      applyDocumentChrome();
      return state;
    })
    .finally(() => {
      loadPromise = null;
    });

  return loadPromise;
}

/** Reset in-flight load so the next loadBranding hits the network (after save / store switch). */
export function invalidateBrandingCache() {
  loadPromise = null;
}

export const DEFAULT_BRAND_FAVICON = "";
