import { computed, reactive, readonly } from "vue";
import { t } from "@/i18n";

const DEFAULT_NAME = import.meta.env.VITE_BRAND_NAME || "Delegat";
/** No default logo — show brand name only until an upload or BRAND_LOGO_URL seed. */
const DEFAULT_LOGO = (import.meta.env.VITE_BRAND_LOGO || "").trim();
/** Control Plane square favicon (copied into frontend/public). */
const DEFAULT_FAVICON =
  (import.meta.env.VITE_BRAND_FAVICON || "").trim() || "/logo-square-favicon.webp";
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
const LEGACY_FAVICON = "/logo_sm@0,15x.webp";

function normalizeLogoUrl(url) {
  if (!url || typeof url !== "string") return "";
  const trimmed = url.trim();
  if (!trimmed || trimmed === LEGACY_LOGO) return "";
  return trimmed;
}

function normalizeFaviconUrl(url) {
  if (!url || typeof url !== "string") return DEFAULT_FAVICON;
  const trimmed = url.trim();
  if (!trimmed || trimmed === LEGACY_FAVICON) return DEFAULT_FAVICON;
  return trimmed;
}

const state = reactive({
  name: DEFAULT_NAME,
  description: DEFAULT_DESCRIPTION,
  logoUrl: normalizeLogoUrl(DEFAULT_LOGO),
  faviconUrl: normalizeFaviconUrl(DEFAULT_FAVICON),
  hasCustomLogo: false,
  hasCustomFavicon: false,
  primaryColor: null,
  accentColor: null,
  updatedAt: null,
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
export const BRAND_FAVICON = normalizeFaviconUrl(DEFAULT_FAVICON);

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

  const previousName = state.name;

  if (typeof dto.name === "string" && dto.name.trim()) {
    state.name = dto.name.trim();
  }
  if (dto.description !== undefined) {
    state.description = dto.description?.trim()
      ? dto.description.trim()
      : `${state.name} - ${t("common.brandDescription")}`;
  }
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
  state.loaded = true;

  applyDocumentChrome();
  applyThemeColors();

  if (previousName !== state.name && typeof document !== "undefined") {
    if (document.title === previousName) {
      document.title = state.name;
    } else if (document.title.endsWith(` - ${previousName}`)) {
      document.title =
        `${document.title.slice(0, -(previousName.length + 3))} - ${state.name}`;
    }
  }
}

function normalizeHex(value) {
  if (!value || typeof value !== "string") return null;
  const trimmed = value.trim();
  if (/^#[0-9A-Fa-f]{6}$/.test(trimmed)) return trimmed.toUpperCase();
  if (/^[0-9A-Fa-f]{6}$/.test(trimmed)) return `#${trimmed.toUpperCase()}`;
  return null;
}

function applyDocumentChrome() {
  const favicon = state.faviconUrl || DEFAULT_FAVICON;
  let link = document.querySelector("link[rel='icon']");
  if (!link) {
    link = document.createElement("link");
    link.setAttribute("rel", "icon");
    document.head.appendChild(link);
  }
  if (link.getAttribute("href") !== favicon) {
    link.setAttribute("href", favicon);
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

/** Reset in-flight load so the next loadBranding hits the network (after save). */
export function invalidateBrandingCache() {
  loadPromise = null;
}

export const DEFAULT_BRAND_FAVICON = DEFAULT_FAVICON;
