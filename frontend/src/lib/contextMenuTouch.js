/**
 * Reka ContextMenuTrigger only opens on touch after pressOpenDelay (700ms)
 * and cancels that timer on pointerup. A tap therefore never opens the menu
 * and falls through to the row click (navigate). On touch/pen, a tap on the
 * trigger synthesizes `contextmenu` and swallows the following click.
 */

const NESTED_CONTROL =
  'a, button, input, textarea, select, [role="checkbox"], [role="menuitem"]';

/**
 * @param {Pick<PointerEvent, 'pointerType'> | null | undefined} event
 */
export function isTouchLikePointer(event) {
  if (!event) return false;
  const type = event.pointerType;
  return type === "touch" || type === "pen";
}

/**
 * @param {{ x: number, y: number } | null | undefined} start
 * @param {{ x: number, y: number } | null | undefined} end
 * @param {number} [thresholdPx]
 */
export function touchMovedTooFar(start, end, thresholdPx = 10) {
  if (!start || !end) return true;
  const dx = (end.x ?? 0) - (start.x ?? 0);
  const dy = (end.y ?? 0) - (start.y ?? 0);
  return dx * dx + dy * dy > thresholdPx * thresholdPx;
}

/**
 * @param {Pick<PointerEvent, 'pointerType' | 'button' | 'clientX' | 'clientY' | 'target'> | null | undefined} event
 * @param {{ x: number, y: number } | null | undefined} origin
 */
export function shouldOpenContextMenuOnTouchRelease(event, origin) {
  if (!isTouchLikePointer(event)) return false;
  if (event.button != null && event.button !== 0) return false;
  if (
    origin &&
    touchMovedTooFar(origin, { x: event.clientX, y: event.clientY })
  ) {
    return false;
  }
  const target = event.target;
  if (!target || typeof target.closest !== "function") return false;
  if (target.closest(NESTED_CONTROL)) return false;
  return true;
}

/**
 * @param {Pick<PointerEvent, 'target' | 'clientX' | 'clientY'>} event
 */
export function dispatchContextMenuAtPointer(event) {
  const target = event?.target;
  if (!target || typeof target.dispatchEvent !== "function") return false;
  const view = typeof window !== "undefined" ? window : undefined;
  const init = {
    bubbles: true,
    cancelable: true,
    view,
    clientX: event.clientX,
    clientY: event.clientY,
  };
  const MouseEventCtor = globalThis.MouseEvent;
  const EventCtor = globalThis.Event;
  const menuEvent =
    typeof MouseEventCtor === "function"
      ? new MouseEventCtor("contextmenu", init)
      : typeof EventCtor === "function"
        ? new EventCtor("contextmenu", init)
        : { type: "contextmenu", ...init };
  target.dispatchEvent(menuEvent);
  return true;
}

/**
 * The click from the same tap would navigate (row) or dismiss the menu.
 * @param {number} [timeoutMs]
 */
export function suppressNextClick(timeoutMs = 400) {
  if (typeof document === "undefined") return () => {};
  let cleaned = false;
  const onClick = (event) => {
    event.preventDefault();
    event.stopPropagation();
    event.stopImmediatePropagation();
    cleanup();
  };
  const cleanup = () => {
    if (cleaned) return;
    cleaned = true;
    document.removeEventListener("click", onClick, true);
    clearTimeout(timer);
  };
  document.addEventListener("click", onClick, true);
  const timer = setTimeout(cleanup, timeoutMs);
  return cleanup;
}

/**
 * @param {PointerEvent} event
 * @param {{ x: number, y: number } | null | undefined} origin
 */
export function openContextMenuFromTouchRelease(event, origin) {
  if (!shouldOpenContextMenuOnTouchRelease(event, origin)) return false;
  suppressNextClick();
  return dispatchContextMenuAtPointer(event);
}
