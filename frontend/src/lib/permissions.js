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
  ApiAccess: "api.access",
  StoreMembersManage: "store.members.manage",
  StoreSettingsManage: "store.settings.manage",
  CatalogManage: "catalog.manage",
  MarketplaceManage: "marketplace.manage",
  FilesManage: "files.manage",
  OperationsManage: "operations.manage",
  AiUse: "ai.use",
};
