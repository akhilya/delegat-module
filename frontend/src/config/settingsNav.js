import {
  PAGE_SETTINGS,
  PAGE_SETTINGS_USER,
  PAGE_ADMIN_STORES,
  PAGE_ADMIN_USERS,
  PAGE_ADMIN_TOKENS,
  PAGE_ADMIN_PLUGINS,
  PAGE_ADMIN_ERROR_LOGS,
  PAGE_ADMIN_AI,
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
} from "lucide-vue-next";

/** @typedef {{ id: string, label: string, to: string, icon: import('vue').Component, visible?: (ctx: NavContext) => boolean }} SettingsNavItem */
/** @typedef {{ me: object|null, canManageStore: boolean, isAdmin: boolean }} NavContext */

export const SETTINGS_ROUTE_LABELS = {
  [PAGE_SETTINGS]: "Общие",
  [PAGE_SETTINGS_USER]: "Пользователь",
  [PAGE_ADMIN_STORES]: "Магазины",
  [PAGE_ADMIN_USERS]: "Пользователи",
  [PAGE_ADMIN_TOKENS]: "Токены",
  [PAGE_ADMIN_PLUGINS]: "Плагины",
  [PAGE_ADMIN_ERROR_LOGS]: "Логи ошибок",
  [PAGE_ADMIN_AI]: "ИИ",
};

const ADMIN_PREFIX = "/settings/admin";

/**
 * @param {NavContext} ctx
 * @returns {SettingsNavItem[]}
 */
export function getPersonalSettingsItems(ctx) {
  const items = [
    { id: "user", label: "Пользователь", to: PAGE_SETTINGS_USER, icon: User },
  ];
  if (ctx.canManageStore && !ctx.isAdmin) {
    items.push({ id: "stores", label: "Магазины", to: PAGE_ADMIN_STORES, icon: Store });
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
    { id: "overview", label: "Общие", to: PAGE_SETTINGS, icon: Settings2 },
    { id: "stores", label: "Магазины", to: PAGE_ADMIN_STORES, icon: Store },
    { id: "users", label: "Пользователи", to: PAGE_ADMIN_USERS, icon: Users },
    { id: "tokens", label: "Токены", to: PAGE_ADMIN_TOKENS, icon: Key },
    { id: "plugins", label: "Плагины", to: PAGE_ADMIN_PLUGINS, icon: Plug },
    { id: "logs", label: "Логи ошибок", to: PAGE_ADMIN_ERROR_LOGS, icon: AlertTriangle },
    { id: "ai", label: "ИИ", to: PAGE_ADMIN_AI, icon: Sparkles },
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
  return path === PAGE_SETTINGS || path.startsWith(ADMIN_PREFIX);
}

export function resolveSettingsBreadcrumb(path) {
  if (SETTINGS_ROUTE_LABELS[path]) return SETTINGS_ROUTE_LABELS[path];
  if (path.startsWith(`${PAGE_ADMIN_STORES}/`)) return "Магазин";
  return null;
}