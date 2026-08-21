/**
 * One context menu at a time. Reka/Radix keeps each ContextMenuRoot
 * independent, and a right-click (button 2) on another trigger does not
 * dismiss the open menu — so a table with a menu per row can stack them.
 */

export const OPEN_CONTEXT_MENU_SELECTOR =
  '[data-slot="context-menu-content"][data-state="open"]';

/**
 * Keep a menu that already contains the click (re-click inside the panel);
 * close every other open panel.
 * @param {Element | null | undefined} menuEl
 * @param {EventTarget | null | undefined} eventTarget
 */
export function shouldCloseContextMenu(menuEl, eventTarget) {
  if (!menuEl) return false;
  if (eventTarget != null && typeof menuEl.contains === "function") {
    return !menuEl.contains(eventTarget);
  }
  return true;
}

/**
 * @param {EventTarget | null | undefined} eventTarget
 * @param {{ queryOpen?: () => Iterable<Element>, dismiss?: (el: Element) => void }} [options]
 * @returns {number} menus dismissed
 */
export function closeForeignContextMenus(eventTarget, options = {}) {
  const queryOpen =
    options.queryOpen ??
    (() =>
      typeof document === "undefined"
        ? []
        : document.querySelectorAll(OPEN_CONTEXT_MENU_SELECTOR));
  const dismiss =
    options.dismiss ??
    ((el) => {
      el.dispatchEvent(
        new KeyboardEvent("keydown", {
          key: "Escape",
          code: "Escape",
          keyCode: 27,
          which: 27,
          bubbles: true,
          cancelable: true,
        }),
      );
    });

  let closed = 0;
  for (const el of queryOpen()) {
    if (!shouldCloseContextMenu(el, eventTarget)) continue;
    dismiss(el);
    closed += 1;
  }
  return closed;
}

/** @param {Event} event */
export function handleContextMenuCapture(event) {
  closeForeignContextMenus(event?.target);
}

let installed = false;

export function installContextMenuExclusive() {
  if (installed || typeof document === "undefined") return;
  installed = true;
  document.addEventListener("contextmenu", handleContextMenuCapture, true);
}

/** @internal tests */
export function _resetContextMenuExclusiveForTests() {
  if (typeof document !== "undefined" && installed) {
    document.removeEventListener("contextmenu", handleContextMenuCapture, true);
  }
  installed = false;
}
