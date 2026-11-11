import { canAccessPath } from "./routeAccess.js";

export const ADMIN_SECTION_DEFS = [
  { id: "stores", path: "/settings/admin/stores", labelKey: "nav.stores" },
  { id: "users", path: "/settings/admin/users", labelKey: "nav.users" },
  { id: "ai", path: "/settings/admin/ai", labelKey: "nav.ai" },
];

/** Sections are gated independently to preserve each page's existing access. */
export function getAccessibleAdminSections(me) {
  return ADMIN_SECTION_DEFS.filter((section) =>
    canAccessPath(me, section.path),
  );
}
