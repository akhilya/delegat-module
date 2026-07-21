import {
  PAGE_SETTINGS,
  PAGE_SETTINGS_USER,
  PAGE_ADMIN_STORES,
  PAGE_ADMIN_USERS,
  PAGE_ADMIN_TOKENS,
  PAGE_ADMIN_PLUGINS,
  PAGE_ADMIN_ERROR_LOGS,
  PAGE_ADMIN_AI,
  PAGE_ADAPTER_LOGS,
} from "@/router";
import {
  Settings2,
  User,
  Store,
  Users,
  Key,
  Plug,
  AlertTriangle,
  Sparkles,
  ScrollText,
} from "lucide-vue-next";
import { t } from "@/i18n";

/** @typedef {{ id: string, label?: string, labelKey?: string, to: string, icon: import('vue').Component, visible?: (ctx: NavContext) => boolean }} SettingsNavItem */
/** @typedef {{ me: object|null, canManageStore: boolean, isAdmin: boolean }} NavContext */

export const SETTINGS_ROUTE_LABEL_KEYS = {
  [PAGE_SETTINGS]: "nav.overview",
  [PAGE_SETTINGS_USER]: "nav.user",
  [PAGE_ADMIN_STORES]: "nav.stores",
  [PAGE_ADMIN_USERS]: "nav.users",
  [PAGE_ADMIN_TOKENS]: "nav.tokens",
  [PAGE_ADMIN_PLUGINS]: "nav.plugins",
  [PAGE_ADMIN_ERROR_LOGS]: "nav.errorLogs",
  [PAGE_ADMIN_AI]: "nav.ai",
  [PAGE_ADAPTER_LOGS]: "nav.adapterLogs",
};

/** @deprecated use SETTINGS_ROUTE_LABEL_KEYS + t() */
export const SETTINGS_ROUTE_LABELS = new Proxy(
  {},
  {
    get(_target, prop) {
      const key = SETTINGS_ROUTE_LABEL_KEYS[prop];
      return key ? t(key) : undefined;
    },
  },
);

const ADMIN_PREFIX = "/settings/admin";

/**
 * @param {NavContext} ctx
 * @returns {SettingsNavItem[]}
 */
export function getPersonalSettingsItems(ctx) {
  const items = [
    { id: "user", labelKey: "nav.user", to: PAGE_SETTINGS_USER, icon: User },
  ];
  if (ctx.canManageStore && !ctx.isAdmin) {
    items.push({ id: "stores", labelKey: "nav.stores", to: PAGE_ADMIN_STORES, icon: Store });
  }
  return items;
}

/**
 * @param {NavContext} ctx
 * @returns {SettingsNavItem[]}
 */
export function getAdminSettingsItems(ctx) {
  if (!ctx.isAdmin) return [];
  return [
    { id: "overview", labelKey: "nav.overview", to: PAGE_SETTINGS, icon: Settings2 },
    { id: "stores", labelKey: "nav.stores", to: PAGE_ADMIN_STORES, icon: Store },
    { id: "users", labelKey: "nav.users", to: PAGE_ADMIN_USERS, icon: Users },
    { id: "tokens", labelKey: "nav.tokens", to: PAGE_ADMIN_TOKENS, icon: Key },
    { id: "plugins", labelKey: "nav.plugins", to: PAGE_ADMIN_PLUGINS, icon: Plug },
    { id: "logs", labelKey: "nav.errorLogs", to: PAGE_ADMIN_ERROR_LOGS, icon: AlertTriangle },
    { id: "adapter-logs", labelKey: "nav.adapterLogs", to: PAGE_ADAPTER_LOGS, icon: ScrollText },
    ...(ctx.me?.aiEnabled !== false
      ? [{ id: "ai", labelKey: "nav.ai", to: PAGE_ADMIN_AI, icon: Sparkles }]
      : []),
  ];
}

/**
 * Flat list for app sidebar settings sub-menu.
 * @param {NavContext} ctx
 */
export function getSettingsSidebarSubItems(ctx) {
  return [...getPersonalSettingsItems(ctx), ...getAdminSettingsItems(ctx)];
}

export function isSettingsAdminPath(path) {
  return path === PAGE_SETTINGS
    || path === PAGE_ADAPTER_LOGS
    || path.startsWith(ADMIN_PREFIX);
}

export function resolveSettingsBreadcrumb(path) {
  if (SETTINGS_ROUTE_LABEL_KEYS[path]) return t(SETTINGS_ROUTE_LABEL_KEYS[path]);
  if (path.startsWith(`${PAGE_ADMIN_STORES}/`)) return t("routes.store");
  return null;
}

export function resolveSettingsLabel(itemOrPath) {
  if (typeof itemOrPath === "string") {
    const key = SETTINGS_ROUTE_LABEL_KEYS[itemOrPath];
    return key ? t(key) : itemOrPath;
  }
  if (itemOrPath?.labelKey) return t(itemOrPath.labelKey);
  return itemOrPath?.label ?? "";
}
