import { computed, onMounted, onUnmounted, ref, watch } from "vue";

const INDICATOR_MOTION =
  "transform var(--duration-ui) var(--ease-out-quart), width var(--duration-ui) var(--ease-out-quart), height var(--duration-ui) var(--ease-out-quart)";

export const TABS_INDICATOR_SELECTOR =
  '[data-slot="tabs-trigger"][data-state="active"]';
export const TABS_INDICATOR_ITEMS = '[data-slot="tabs-trigger"]';

export const SIDEBAR_NAV_INDICATOR_SELECTOR =
  '[data-slot="sidebar-menu-sub-button"][data-active="true"]';
export const SIDEBAR_NAV_INDICATOR_ITEMS =
  '[data-slot="sidebar-menu-sub-button"]';

export function resolveTabsListElement(refValue) {
  if (!refValue) return null;
  if (typeof Element !== "undefined" && refValue instanceof Element) {
    return refValue;
  }
  const el = refValue.$el;
  if (typeof Element !== "undefined" && el instanceof Element) return el;
  return el ?? null;
}

export function tabIndicatorBox(list, active) {
  if (!list || !active) return null;
  if (
    typeof active.getBoundingClientRect === "function" &&
    typeof list.getBoundingClientRect === "function"
  ) {
    const listRect = list.getBoundingClientRect();
    const activeRect = active.getBoundingClientRect();
    const width = Number(activeRect.width) || 0;
    const height = Number(activeRect.height) || 0;
    if (width <= 0 || height <= 0) return null;
    return {
      x:
        activeRect.left -
        listRect.left -
        (Number(list.clientLeft) || 0) +
        (Number(list.scrollLeft) || 0),
      y:
        activeRect.top -
        listRect.top -
        (Number(list.clientTop) || 0) +
        (Number(list.scrollTop) || 0),
      width,
      height,
    };
  }
  const width = Number(active.offsetWidth) || 0;
  const height = Number(active.offsetHeight) || 0;
  if (width <= 0 || height <= 0) return null;
  return {
    x: Number(active.offsetLeft) || 0,
    y: Number(active.offsetTop) || 0,
    width,
    height,
  };
}

export function tabIndicatorStyle(box, { motion = true } = {}) {
  if (!box) {
    return { opacity: "0" };
  }
  return {
    opacity: "1",
    width: `${box.width}px`,
    height: `${box.height}px`,
    transform: `translate3d(${box.x}px, ${box.y}px, 0)`,
    transition: motion ? INDICATOR_MOTION : "none",
  };
}

function boxesEqual(a, b) {
  if (a === b) return true;
  if (!a || !b) return false;
  return (
    a.x === b.x && a.y === b.y && a.width === b.width && a.height === b.height
  );
}

/**
 * Sliding active pill. Measures the active item; first paint has no motion
 * so the pill does not fly in on mount.
 */
export function useSlidingIndicator({
  itemSelector = TABS_INDICATOR_SELECTOR,
  observeSelector = TABS_INDICATOR_ITEMS,
  attributeFilter = ["data-state", "data-active"],
} = {}) {
  const listRef = ref(null);
  const box = ref(null);
  const motion = ref(false);
  let resizeObserver = null;
  let mutationObserver = null;

  function listEl() {
    return resolveTabsListElement(listRef.value);
  }

  function measure() {
    const list = listEl();
    if (!list || typeof list.querySelector !== "function") {
      if (box.value != null) box.value = null;
      return;
    }
    const active = list.querySelector(itemSelector);
    const next = tabIndicatorBox(list, active);
    if (boxesEqual(box.value, next)) return;
    box.value = next;
  }

  function bindObservers() {
    resizeObserver?.disconnect();
    mutationObserver?.disconnect();
    const list = listEl();
    if (!list || typeof ResizeObserver === "undefined") return;
    resizeObserver = new ResizeObserver(measure);
    resizeObserver.observe(list);
    for (const trigger of list.querySelectorAll(observeSelector)) {
      resizeObserver.observe(trigger);
    }
    if (typeof MutationObserver === "undefined") return;
    mutationObserver = new MutationObserver(measure);
    mutationObserver.observe(list, {
      attributes: true,
      subtree: true,
      childList: true,
      attributeFilter,
    });
  }

  function setListRef(el) {
    listRef.value = el;
  }

  onMounted(() => {
    measure();
    bindObservers();
    requestAnimationFrame(() => {
      motion.value = true;
    });
  });

  watch(listRef, () => {
    measure();
    bindObservers();
  });

  onUnmounted(() => {
    resizeObserver?.disconnect();
    mutationObserver?.disconnect();
  });

  const indicatorStyle = computed(() =>
    tabIndicatorStyle(box.value, { motion: motion.value }),
  );

  return { setListRef, indicatorStyle, measure };
}

/** Sliding active pill for shadcn TabsList. */
export function useTabsIndicator() {
  return useSlidingIndicator({
    itemSelector: TABS_INDICATOR_SELECTOR,
    observeSelector: TABS_INDICATOR_ITEMS,
    attributeFilter: ["data-state"],
  });
}
