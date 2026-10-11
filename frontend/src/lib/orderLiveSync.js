/** How often an open orders screen asks the marketplace for new orders. */
export const ORDER_LIVE_SYNC_MS = 20_000;

/**
 * Skip a live pull when it would fight another load, a pinned search,
 * or a list the operator has already paged further into.
 */
export function shouldPullLiveOrders({
  bootstrapping = false,
  busy = false,
  search = "",
  pagedFurther = false,
  hidden = false,
} = {}) {
  if (bootstrapping || busy || pagedFurther || hidden) return false;
  return String(search || "").trim().length === 0;
}

/**
 * Repeat `pull` while the tab is visible. Returns a stop function.
 * No-ops where `window` is missing (unit tests, SSR).
 */
export function startOrderLiveSync(pull) {
  if (typeof window === "undefined" || typeof pull !== "function") {
    return () => {};
  }

  const tick = () => {
    if (document.visibilityState === "hidden") return;
    pull();
  };
  const onVisibility = () => {
    if (document.visibilityState === "visible") tick();
  };
  const timer = window.setInterval(tick, ORDER_LIVE_SYNC_MS);
  document.addEventListener("visibilitychange", onVisibility);
  return () => {
    window.clearInterval(timer);
    document.removeEventListener("visibilitychange", onVisibility);
  };
}
