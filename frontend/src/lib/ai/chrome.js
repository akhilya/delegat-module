/**
 * Whether the app shell should mount the AI chat panel and FAB.
 * App.vue lives across sign-in / setup (bare layout) and the dashboard; the
 * flag must be re-read when leaving those routes, not only on first mount.
 */
export function resolveAiChromeEnabled({ isBareLayout, me }) {
  if (isBareLayout) return false;
  return me?.aiEnabled === true;
}
