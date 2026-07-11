/** Brand configuration — reads from VITE_BRAND_* env vars. */

export const BRAND_NAME = import.meta.env.VITE_BRAND_NAME || "Delegat";
export const BRAND_DESCRIPTION = import.meta.env.VITE_BRAND_DESCRIPTION || `${BRAND_NAME} — панель управления товарами и адаптерами`;
export const BRAND_LOGO = import.meta.env.VITE_BRAND_LOGO || "/logo@0,25x.webp";
export const BRAND_FAVICON = import.meta.env.VITE_BRAND_FAVICON || "/logo_sm@0,15x.webp";
