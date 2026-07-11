/**
 * @param {import('vue-router').RouteLocationNormalizedLoaded|string} routeOrPath
 * @param {string} to
 */
export function isActiveRoute(routeOrPath, to) {
  const path = typeof routeOrPath === "string" ? routeOrPath : routeOrPath.path;
  const fullPath = typeof routeOrPath === "string" ? routeOrPath : routeOrPath.fullPath;
  const [targetPath, query] = to.split("?");
  if (query) {
    return path === targetPath && fullPath.includes(query);
  }
  if (path === targetPath) return true;
  if (targetPath === "/settings") {
    return path === "/settings";
  }
  if (targetPath === "/adapter") {
    return path === "/adapter";
  }
  if (targetPath.startsWith("/adapter/")) {
    return path === targetPath || path.startsWith(`${targetPath}/`);
  }
  return path.startsWith(`${targetPath}/`);
}