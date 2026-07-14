/**
 * @param {import('vue-router').RouteLocationNormalizedLoaded} route
 * @param {string} to
 * @returns {number} -1 if no match, otherwise specificity (higher = better)
 */
export function getNavRouteMatchScore(route, to) {
  const path = route.path;
  const query = route.query ?? {};
  const [targetPath, targetQueryString] = to.split("?");

  if (targetQueryString) {
    if (path !== targetPath) return -1;
    const targetParams = new URLSearchParams(targetQueryString);
    for (const [key, value] of targetParams.entries()) {
      if (String(query[key] ?? "") !== value) return -1;
    }
    return 3000 + targetPath.length;
  }

  if (path === targetPath) {
    if (targetPath === "/products" && (query.trash === "1" || query.trash === 1)) {
      return -1;
    }
    return 2000 + targetPath.length;
  }

  if (targetPath === "/adapter") {
    return -1;
  }

  if (targetPath.startsWith("/adapter/")) {
    if (path === targetPath || path.startsWith(`${targetPath}/`)) {
      return 1000 + targetPath.length;
    }
    return -1;
  }

  if (targetPath.startsWith("/settings")) {
    if (path === targetPath || path.startsWith(`${targetPath}/`)) {
      return 800 + targetPath.length;
    }
    return -1;
  }

  if (path.startsWith(`${targetPath}/`)) {
    return 500 + targetPath.length;
  }

  return -1;
}

/**
 * Pick a single active item among siblings (longest / most specific match wins).
 * @param {import('vue-router').RouteLocationNormalizedLoaded} route
 * @param {Array<{ id: string, type?: string, to?: string }>} items
 * @returns {string|null}
 */
export function resolveActiveNavItemId(route, items) {
  let bestId = null;
  let bestScore = -1;

  for (const item of items) {
    if (item.type === "action" || !item.to) continue;
    const score = getNavRouteMatchScore(route, item.to);
    if (score > bestScore) {
      bestScore = score;
      bestId = item.id;
    }
  }

  return bestId;
}

/**
 * @param {import('vue-router').RouteLocationNormalizedLoaded|string} routeOrPath
 * @param {string} to
 */
export function isActiveRoute(routeOrPath, to) {
  const route =
    typeof routeOrPath === "string"
      ? { path: routeOrPath, fullPath: routeOrPath, query: {} }
      : routeOrPath;
  return getNavRouteMatchScore(route, to) >= 0;
}