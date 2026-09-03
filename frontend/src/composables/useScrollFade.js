export const SCROLL_FADE_SIZE = 40;
export const SCROLL_FADE_REVEAL = 24;
const EDGE_EPS = 1;

/**
 * Fade lengths for a vertical scroller. At the top/bottom extreme the matching
 * edge is 0 so the mask does not tint content that is fully in view.
 */
export function scrollFadeEdges({
  scrollTop = 0,
  scrollHeight = 0,
  clientHeight = 0,
  size = SCROLL_FADE_SIZE,
  reveal = SCROLL_FADE_REVEAL,
} = {}) {
  const max = Math.max(0, Number(scrollHeight) - Number(clientHeight));
  if (max <= EDGE_EPS) return { top: 0, bottom: 0 };

  const topOffset = Math.max(0, Number(scrollTop) || 0);
  const bottomOffset = Math.max(0, max - topOffset);
  const span = Math.max(1, Number(reveal) || SCROLL_FADE_REVEAL);
  const depth = Math.max(0, Number(size) || SCROLL_FADE_SIZE);

  return {
    top: topOffset <= EDGE_EPS ? 0 : Math.min(depth, (topOffset / span) * depth),
    bottom:
      bottomOffset <= EDGE_EPS
        ? 0
        : Math.min(depth, (bottomOffset / span) * depth),
  };
}

export function applyScrollFadeVars(el, edges) {
  if (!el?.style) return;
  el.style.setProperty("--scroll-fade-t", `${edges.top}px`);
  el.style.setProperty("--scroll-fade-b", `${edges.bottom}px`);
}

/**
 * Drive shadcn `scroll-fade` mask variables from scroll position.
 * CSS scroll-timeline often stays on the static both-edges fallback.
 */
export function useScrollFade(elRef) {
  let frame = 0;
  let resizeObserver = null;
  let mutationObserver = null;

  function measure() {
    frame = 0;
    const el = elRef?.value;
    if (!el) return;
    applyScrollFadeVars(
      el,
      scrollFadeEdges({
        scrollTop: el.scrollTop,
        scrollHeight: el.scrollHeight,
        clientHeight: el.clientHeight,
      }),
    );
  }

  function onScroll() {
    if (frame) return;
    frame = requestAnimationFrame(measure);
  }

  function unbind(el) {
    if (frame) cancelAnimationFrame(frame);
    frame = 0;
    el?.removeEventListener("scroll", onScroll);
    resizeObserver?.disconnect();
    resizeObserver = null;
    mutationObserver?.disconnect();
    mutationObserver = null;
  }

  function bind(el) {
    if (!el) return;
    el.addEventListener("scroll", onScroll, { passive: true });
    if (typeof ResizeObserver !== "undefined") {
      resizeObserver = new ResizeObserver(onScroll);
      resizeObserver.observe(el);
    }
    if (typeof MutationObserver !== "undefined") {
      mutationObserver = new MutationObserver(onScroll);
      mutationObserver.observe(el, {
        childList: true,
        subtree: true,
        characterData: true,
      });
    }
    measure();
  }

  return { bind, unbind, measure, onScroll };
}
