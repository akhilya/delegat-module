/** Rules for Administration → Users role/permission editing. */

export function canMutateUserAccess({ canManageRoles, editingSelf }) {
  return Boolean(canManageRoles) && !editingSelf;
}

export function isOwnerRoleLocked({ editingSelf, canToggleOwner, targetIsAdmin }) {
  if (editingSelf) return true;
  if (!canToggleOwner) return true;
  if (targetIsAdmin) return true;
  return false;
}

export function isUserAccessRowLocked({ canManageRoles, editingSelf, saving }) {
  return Boolean(saving) || !canMutateUserAccess({ canManageRoles, editingSelf });
}
