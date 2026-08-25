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
  StoresCreate: "stores.create",
  ReferenceRead: "reference.read",
  WorkspaceView: "workspace.view",
  ApiAccess: "api.access",
  StoreMembersManage: "store.members.manage",
  StoreSettingsManage: "store.settings.manage",
  CatalogManage: "catalog.manage",
  MarketplaceManage: "marketplace.manage",
  FilesManage: "files.manage",
  OperationsManage: "operations.manage",
  AiUse: "ai.use",
};

/** Browse a surface without the write grant. Never covers plugins or AI. */
export function canBrowse(me, permission) {
  if (hasPermission(me, permission)) return true;
  if (permission === PERMS.PluginsManage || permission === PERMS.AiUse)
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
