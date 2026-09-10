/**
 * Product Adapters tab: left-click on a connection row opens the same
 * cursor-positioned context menu as right-click.
 * The ⋮ menu and the sizes disclosure stay on their own buttons.
 */

/**
 * @param {Pick<MouseEvent, 'button' | 'target'>} event
 */
export function shouldOpenAdapterCardContextMenu(event) {
  if (!event) return false;
  if (event.button != null && event.button !== 0) return false;
  const target = event.target;
  if (!target || typeof target.closest !== "function") return false;
  return !target.closest("a, button, input, textarea, select");
}

/**
 * @param {MouseEvent} event
 * @returns {boolean} whether a contextmenu was dispatched
 */
export function dispatchAdapterCardContextMenu(event) {
  if (!shouldOpenAdapterCardContextMenu(event)) return false;
  event.preventDefault?.();
  const target = event.target;
  if (typeof target.dispatchEvent !== "function") return false;
  const view = typeof window !== "undefined" ? window : undefined;
  target.dispatchEvent(
    new MouseEvent("contextmenu", {
      bubbles: true,
      cancelable: true,
      view,
      clientX: event.clientX,
      clientY: event.clientY,
    }),
  );
  return true;
}
