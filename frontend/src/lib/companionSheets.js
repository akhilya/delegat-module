/** Inset from the viewport edge (matches sheet `right-2` / 0.5rem). */
export const COMPANION_INSET = 8;
/** Gap between stacked companion sheets. */
export const COMPANION_GAP = 8;
/** Separation between the open companion stack and desktop notifications. */
export const COMPANION_TOAST_GAP = 16;
/** Closed-state notification inset (Tailwind `right-4`). */
export const COMPANION_TOAST_CLOSED_RIGHT = 16;
/** Side-by-side companions from `lg` (1024px). Narrower: one sheet, full width. */
export const COMPANION_STACK_MIN_WIDTH = 1024;

/**
 * Right-to-left stack: operations sit on the viewport edge, then docs, then
 * the assistant (so a second sheet opens to the right of the assistant).
 */
export const COMPANION_STACK = ["operations", "docs", "ai"];

export function companionCanStack(viewportWidth) {
  const w = Number(viewportWidth);
  if (!Number.isFinite(w)) return true;
  return w >= COMPANION_STACK_MIN_WIDTH;
}

/** Distance from the inset right edge to this sheet’s right edge. */
export function companionEdgeOffset(id, snapshot, { stack = true } = {}) {
  if (!stack) return 0;
  let offset = 0;
  for (const key of COMPANION_STACK) {
    if (key === id) return offset;
    const item = snapshot[key];
    if (item?.open) offset += item.width + COMPANION_GAP;
  }
  return 0;
}

/** Width of the open stack including inset and inter-sheet gaps. */
export function companionStackExtent(snapshot) {
  let extent = 0;
  let n = 0;
  for (const key of COMPANION_STACK) {
    const item = snapshot[key];
    if (!item?.open) continue;
    if (n > 0) extent += COMPANION_GAP;
    extent += item.width;
    n += 1;
  }
  return n ? COMPANION_INSET + extent : 0;
}

export function companionToastRight(snapshot, { stack = true } = {}) {
  if (!stack) return COMPANION_TOAST_CLOSED_RIGHT;
  const extent = companionStackExtent(snapshot);
  if (!extent) return COMPANION_TOAST_CLOSED_RIGHT;
  return extent + COMPANION_TOAST_GAP;
}

/** Prefer the rightmost open companion when the viewport can no longer stack. */
export function companionKeepWhenCollapsing(snapshot) {
  for (const key of COMPANION_STACK) {
    if (snapshot[key]?.open) return key;
  }
  return null;
}
