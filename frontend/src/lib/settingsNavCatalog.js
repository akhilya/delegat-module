import { canAccessPath } from "./routeAccess.js";

/** Canonical settings destinations. Icons/labels live in `config/settingsNav.js`. */
export const SETTINGS_NAV_DEFS = [
  { id: "user", path: "/settings/user" },
  { id: "admin", path: "/settings/admin" },
  { id: "api", path: "/settings/admin/api" },
  { id: "plugins", path: "/settings/admin/plugins" },
  { id: "logs", path: "/settings/admin/error-logs" },
  { id: "adapter-logs", path: "/settings/adapter-logs" },
];

/**
 * @param {object|null} me
 * @returns {string[]}
 */
export function visibleSettingsNavIds(me) {
  return SETTINGS_NAV_DEFS.filter((def) => canAccessPath(me, def.path)).map(
    (def) => def.id,
  );
}
