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

export function tabIndicatorStyle(box, { motion = true, hidden = false } = {}) {
  if (!box) {
    return { opacity: "0" };
  }
  return {
    opacity: hidden ? "0" : "1",
    width: `${box.width}px`,
    height: `${box.height}px`,
    transform: `translate3d(${box.x}px, ${box.y}px, 0)`,
    transition: motion
      ? `${INDICATOR_MOTION}, opacity var(--duration-ui) var(--ease-out-quart)`
      : "none",
  };
}

const SIDEBAR_NAV_EDGE_EPS = 4;
let lastSidebarNavActiveTop = null;

export function rememberSidebarNavActiveTop(top) {
  if (Number.isFinite(top)) lastSidebarNavActiveTop = top;
}

export function peekSidebarNavActiveTop() {
  return lastSidebarNavActiveTop;
}

export function resetSidebarNavActiveTop() {
  lastSidebarNavActiveTop = null;
}

/**
 * Where a group pill should start when the previous active row was in
 * another group. From below → last row (slide up). From above → first row.
 */
export function indicatorEnterBox(list, target, { fromBottom, items } = {}) {
  if (!target) return null;
  const nodes = Array.isArray(items) ? items : [];
  const edge = fromBottom ? nodes[nodes.length - 1] : nodes[0];
  if (!edge) {
    return {
      x: target.x,
      y: fromBottom ? target.y : 0,
      width: target.width,
      height: target.height,
    };
  }
  const edgeBox = tabIndicatorBox(list, edge);
  if (!edgeBox) {
    return {
      x: target.x,
      y: fromBottom ? target.y : 0,
      width: target.width,
      height: target.height,
    };
  }
  return {
    x: target.x,
    y: edgeBox.y,
    width: target.width,
    height: target.height,
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
  edgeEnter = false,
} = {}) {
  const listRef = ref(null);
  const box = ref(null);
  const visible = ref(false);
  const motion = ref(false);
  let resizeObserver = null;
  let mutationObserver = null;
  let enterFrame = 0;

  function listEl() {
    return resolveTabsListElement(listRef.value);
  }

  function cancelEnter() {
    if (!enterFrame) return;
    cancelAnimationFrame(enterFrame);
    enterFrame = 0;
  }

  function measure() {
    const list = listEl();
    if (!list || typeof list.querySelector !== "function") {
      visible.value = false;
      if (!edgeEnter && box.value != null) box.value = null;
      return;
    }
    const active = list.querySelector(itemSelector);
    const next = tabIndicatorBox(list, active);
    if (!edgeEnter) {
      if (boxesEqual(box.value, next)) return;
      box.value = next;
      return;
    }

    if (!next) {
      cancelEnter();
      visible.value = false;
      return;
    }

    const nextTop =
      typeof active.getBoundingClientRect === "function"
        ? active.getBoundingClientRect().top
        : next.y;
    const wasHidden = !visible.value;
    const prevTop = peekSidebarNavActiveTop();
    visible.value = true;

    if (
      wasHidden &&
      motion.value &&
      !prefersReducedMotion() &&
      Number.isFinite(prevTop) &&
      Math.abs(prevTop - nextTop) > SIDEBAR_NAV_EDGE_EPS
    ) {
      const start = indicatorEnterBox(list, next, {
        fromBottom: prevTop > nextTop,
        items:
          typeof list.querySelectorAll === "function"
            ? [...list.querySelectorAll(observeSelector)]
            : [],
      });
      rememberSidebarNavActiveTop(nextTop);
      if (start && !boxesEqual(start, next)) {
        cancelEnter();
        motion.value = false;
        box.value = start;
        enterFrame = requestAnimationFrame(() => {
          enterFrame = requestAnimationFrame(() => {
            enterFrame = 0;
            motion.value = true;
            box.value = next;
          });
        });
        return;
      }
    }

    rememberSidebarNavActiveTop(nextTop);
    if (boxesEqual(box.value, next)) return;
    box.value = next;
  }

  function prefersReducedMotion() {
    return (
      typeof globalThis.matchMedia === "function" &&
      globalThis.matchMedia("(prefers-reduced-motion: reduce)").matches
    );
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
    cancelEnter();
    resizeObserver?.disconnect();
    mutationObserver?.disconnect();
  });

  const indicatorStyle = computed(() =>
    tabIndicatorStyle(box.value, {
      motion: motion.value,
      hidden: edgeEnter && !visible.value,
    }),
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
