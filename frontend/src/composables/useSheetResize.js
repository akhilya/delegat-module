import { computed, onUnmounted, ref, unref } from "vue";

export const SHEET_RESIZE_MIN = 300;
export const SHEET_RESIZE_MAX = 1000;

export const SHEET_WIDTH_STORAGE = {
  ai: "ai-panel-width",
  operations: "ops-sheet-width",
  orders: "orders-sheet-width",
  notes: "notes-sheet-width",
  logs: "logs-sheet-width",
  mappingPreview: "mapping-preview-sheet-width",
  sidebar: "sidebar-width",
};

export const SHEET_WIDTH_DEFAULTS = {
  ai: 400,
  operations: 672,
  orders: 512,
  notes: 512,
  logs: 672,
  mappingPreview: 672,
  sidebar: 256,
};

/** Desktop nav: narrower than a sheet so the catalog keeps the page. */
export const SIDEBAR_RESIZE_MIN = 220;
export const SIDEBAR_RESIZE_MAX = 480;

export function clampSheetWidth(
  width,
  { min = SHEET_RESIZE_MIN, max = SHEET_RESIZE_MAX } = {},
) {
  const numeric = Number(width);
  const fallback = min;
  const value = Number.isFinite(numeric) ? numeric : fallback;
  const viewportMax =
    typeof window === "undefined"
      ? max
      : Math.min(max, Math.max(min, window.innerWidth - 16));
  return Math.min(viewportMax, Math.max(min, value));
}

export function readStoredSheetWidth(key, fallback) {
  if (!key || typeof localStorage === "undefined") return fallback;
  const n = parseInt(localStorage.getItem(key) || "", 10);
  return Number.isFinite(n) ? n : fallback;
}

/**
 * Drag-to-resize width for inset right/left sheets. Pointer tracks 1:1;
 * persist on pointerup when `storageKey` is set.
 *
 * @param {object} options
 * @param {import("vue").MaybeRef<boolean>} [options.enabled]
 * @param {import("vue").MaybeRef<number | null | undefined>} [options.width] controlled width
 * @param {(w: number) => void} [options.setWidth]
 * @param {string} [options.storageKey]
 * @param {number} [options.defaultWidth]
 * @param {number} [options.minWidth]
 * @param {number} [options.maxWidth]
 * @param {import("vue").MaybeRef<string>} [options.side]
 */
export function useSheetResize(options = {}) {
  const {
    enabled = true,
    width: controlledWidth,
    setWidth,
    storageKey,
    defaultWidth = SHEET_WIDTH_DEFAULTS.ai,
    minWidth = SHEET_RESIZE_MIN,
    maxWidth = SHEET_RESIZE_MAX,
    side = "right",
  } = options;

  const isResizing = ref(false);
  const internalWidth = ref(
    clampSheetWidth(readStoredSheetWidth(storageKey, defaultWidth), {
      min: minWidth,
      max: maxWidth,
    }),
  );
  let dragCleanup = null;
  let frame = 0;
  let pendingWidth = null;

  const liveWidth = computed(() => {
    const controlled = unref(controlledWidth);
    const raw = controlled != null ? controlled : internalWidth.value;
    return clampSheetWidth(raw, { min: minWidth, max: maxWidth });
  });

  function applyWidthNow(next) {
    const clamped = clampSheetWidth(next, { min: minWidth, max: maxWidth });
    if (typeof setWidth === "function") setWidth(clamped);
    if (unref(controlledWidth) == null) internalWidth.value = clamped;
  }

  function flushPendingWidth() {
    if (frame) {
      cancelAnimationFrame(frame);
      frame = 0;
    }
    if (pendingWidth == null) return;
    const next = pendingWidth;
    pendingWidth = null;
    applyWidthNow(next);
  }

  function applyWidth(next) {
    if (!isResizing.value) {
      applyWidthNow(next);
      return;
    }
    pendingWidth = next;
    if (frame) return;
    frame = requestAnimationFrame(() => {
      frame = 0;
      if (pendingWidth == null) return;
      const value = pendingWidth;
      pendingWidth = null;
      applyWidthNow(value);
    });
  }

  function persist() {
    if (!storageKey || typeof localStorage === "undefined") return;
    localStorage.setItem(storageKey, String(liveWidth.value));
  }

  function endDrag() {
    if (!isResizing.value && !dragCleanup) return;
    flushPendingWidth();
    isResizing.value = false;
    dragCleanup?.();
    dragCleanup = null;
    persist();
  }

  function startResize(e) {
    if (!unref(enabled)) return;
    if (e.pointerType === "mouse" && e.button !== 0) return;
    e.preventDefault();
    const handle = e.currentTarget;
    if (handle?.setPointerCapture && e.pointerId != null) {
      handle.setPointerCapture(e.pointerId);
    }

    isResizing.value = true;
    const startX = e.clientX;
    const startWidth = liveWidth.value;
    const dragSide = unref(side) || "right";
    const prevCursor = document.body.style.cursor;
    const prevSelect = document.body.style.userSelect;
    document.body.style.cursor = "col-resize";
    document.body.style.userSelect = "none";

    function onMove(ev) {
      const delta =
        dragSide === "left" ? ev.clientX - startX : startX - ev.clientX;
      applyWidth(startWidth + delta);
    }

    function onUp() {
      document.body.style.cursor = prevCursor;
      document.body.style.userSelect = prevSelect;
      window.removeEventListener("pointermove", onMove);
      window.removeEventListener("pointerup", onUp);
      window.removeEventListener("pointercancel", onUp);
      flushPendingWidth();
      isResizing.value = false;
      dragCleanup = null;
      persist();
    }

    dragCleanup = () => {
      document.body.style.cursor = prevCursor;
      document.body.style.userSelect = prevSelect;
      window.removeEventListener("pointermove", onMove);
      window.removeEventListener("pointerup", onUp);
      window.removeEventListener("pointercancel", onUp);
    };

    window.addEventListener("pointermove", onMove);
    window.addEventListener("pointerup", onUp);
    window.addEventListener("pointercancel", onUp);
  }

  onUnmounted(() => {
    endDrag();
  });

  return {
    width: liveWidth,
    isResizing,
    startResize,
  };
}
