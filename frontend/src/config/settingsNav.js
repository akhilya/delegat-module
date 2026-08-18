import {
  PAGE_SETTINGS,
  PAGE_SETTINGS_USER,
  PAGE_ADMIN_STORES,
  PAGE_ADMIN_USERS,
  PAGE_ADMIN_API,
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
  KeyRound,
  Plug,
  AlertTriangle,
  Sparkles,
  ScrollText,
} from "lucide-vue-next";
import { t } from "@/i18n";
import { canAccessPath } from "@/lib/routeAccess";
import { SETTINGS_NAV_DEFS } from "@/lib/settingsNavCatalog";

/** @typedef {{ id: string, label?: string, labelKey?: string, to: string, icon: import('vue').Component }} SettingsNavItem */
/** @typedef {{ me: object|null, quotaOverAttention?: boolean }} NavContext */

export const SETTINGS_ROUTE_LABEL_KEYS = {
  [PAGE_SETTINGS]: "nav.overview",
  [PAGE_SETTINGS_USER]: "nav.user",
  [PAGE_ADMIN_STORES]: "nav.stores",
  [PAGE_ADMIN_USERS]: "nav.users",
  [PAGE_ADMIN_API]: "nav.api",
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

const SETTINGS_NAV_ICONS = {
  user: User,
  overview: Settings2,
  stores: Store,
  users: Users,
  api: KeyRound,
  plugins: Plug,
  logs: AlertTriangle,
  "adapter-logs": ScrollText,
  ai: Sparkles,
};

/**
 * One settings list, gated the same way as routes (`canAccessPath`).
 * @param {NavContext} ctx
 * @returns {SettingsNavItem[]}
 */
export function getSettingsNavItems(ctx) {
  return SETTINGS_NAV_DEFS.filter((def) => canAccessPath(ctx.me, def.path)).map(
    (def) => ({
      id: def.id,
      labelKey: SETTINGS_ROUTE_LABEL_KEYS[def.path],
      to: def.path,
      icon: SETTINGS_NAV_ICONS[def.id],
      ...(def.id === "overview"
        ? {
            attention: ctx.quotaOverAttention === true,
            attentionTooltipKey: "nav.quotaOverageAttention",
          }
        : {}),
    }),
  );
}

/** @param {NavContext} ctx */
export function getSettingsSidebarSubItems(ctx) {
  return getSettingsNavItems(ctx);
}

export function isSettingsAdminPath(path) {
  return (
    path === PAGE_SETTINGS ||
    path === PAGE_ADAPTER_LOGS ||
    path.startsWith(ADMIN_PREFIX)
  );
}

export function resolveSettingsBreadcrumb(path) {
  if (SETTINGS_ROUTE_LABEL_KEYS[path])
    return t(SETTINGS_ROUTE_LABEL_KEYS[path]);
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
