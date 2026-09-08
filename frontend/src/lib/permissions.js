/** @param {object|null|undefined} me */
export function hasPermission(me, permission) {
  if (!me || !permission) return false;
  const list = me.permissions;
  return Array.isArray(list) && list.includes(permission);
}

export function hasAnyPermission(me, permissions = []) {
  return permissions.some((p) => hasPermission(me, p));
}

export const PERMS = {
  UsersManage: "users.manage",
  RolesManage: "roles.manage",
  PluginsManage: "plugins.manage",
  SettingsPlatform: "settings.platform",
  SettingsDump: "settings.dump",
  StoresCreate: "stores.create",
  ReferenceRead: "reference.read",
  WorkspaceView: "workspace.view",
  ApiAccess: "api.access",
  StoreMembersManage: "store.members.manage",
  StoreSettingsManage: "store.settings.manage",
  CatalogManage: "catalog.manage",
  MarketplaceManage: "marketplace.manage",
  OrdersManage: "orders.manage",
  FilesManage: "files.manage",
  OperationsManage: "operations.manage",
  AiUse: "ai.use",
};

/** Never shown as a grant checkbox (instance owner only). */
export const ADMIN_ONLY_PERMS = [PERMS.RolesManage, PERMS.SettingsDump];

/** Grant checkboxes on Administration → Users. Keep in sync with backend Permissions.Assignable. */
export const ASSIGNABLE_PERMISSIONS = Object.values(PERMS).filter(
  (permission) => !ADMIN_ONLY_PERMS.includes(permission),
);

/** SPA catalog first so new grants stay visible if GET /permissions is stale. */
export function mergeAssignableCatalog(apiList = []) {
  const known = new Set(ASSIGNABLE_PERMISSIONS);
  const extra = [];
  for (const permission of Array.isArray(apiList) ? apiList : []) {
    if (typeof permission !== "string") continue;
    if (known.has(permission) || ADMIN_ONLY_PERMS.includes(permission)) continue;
    known.add(permission);
    extra.push(permission);
  }
  return extra.length ? [...ASSIGNABLE_PERMISSIONS, ...extra] : [...ASSIGNABLE_PERMISSIONS];
}

/** Browse a surface without the write grant. Never covers plugins, AI, or the DB dump. */
export function canBrowse(me, permission) {
  if (hasPermission(me, permission)) return true;
  if (
    permission === PERMS.PluginsManage
    || permission === PERMS.AiUse
    || permission === PERMS.SettingsDump
  )
    return false;
  return hasPermission(me, PERMS.WorkspaceView);
}

export function isCabinetViewOnly(me) {
  if (!me || !Array.isArray(me.permissions)) return false;
  if (!me.permissions.includes(PERMS.WorkspaceView)) return false;
  return me.permissions.every(
    (permission) =>
      permission === PERMS.WorkspaceView || permission === PERMS.ReferenceRead,
  );
}
