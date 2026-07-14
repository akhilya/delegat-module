const STORAGE_KEY = "sidebar-nav-groups-open";

function readState() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return {};
    const parsed = JSON.parse(raw);
    return parsed && typeof parsed === "object" ? parsed : {};
  } catch {
    return {};
  }
}

/** @returns {boolean|undefined} */
export function getSidebarGroupOpen(groupId) {
  const value = readState()[groupId];
  return typeof value === "boolean" ? value : undefined;
}

/** @param {string} groupId @param {boolean} isOpen */
export function setSidebarGroupOpen(groupId, isOpen) {
  try {
    const state = readState();
    state[groupId] = isOpen;
    localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
  } catch {
    // Ignore quota / private mode errors.
  }
}