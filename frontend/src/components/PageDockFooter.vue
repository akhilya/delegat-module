<script setup>
/**
 * Catalog / mailbox pagination bar. Stays under the last row when the
 * table fits; teleports to #page-footer-dock when the page scroller overflows.
 * Hairline top border only — no ring, no rounding.
 */
import { nextTick, onBeforeUnmount, onMounted, ref } from "vue";
import { shouldPinPageFooter } from "@/lib/pageDockFooter";

const pinned = ref(false);
const rootEl = ref(null);
let resizeObserver = null;

function pageScrollEl() {
  return document.querySelector('[data-slot="page-scroll"]');
}

function updatePinned() {
  const scroller = pageScrollEl();
  const footer = rootEl.value;
  if (!scroller || !footer) {
    pinned.value = false;
    return;
  }
  const leftoverBelowFooter = pinned.value
    ? 0
    : scroller.getBoundingClientRect().bottom - footer.getBoundingClientRect().bottom;
  pinned.value = shouldPinPageFooter({
    scrollHeight: scroller.scrollHeight,
    clientHeight: scroller.clientHeight,
    leftoverBelowFooter,
    pinned: pinned.value,
  });
}

function observe() {
  resizeObserver?.disconnect();
  const scroller = pageScrollEl();
  if (!scroller) return;
  resizeObserver = new ResizeObserver(updatePinned);
  resizeObserver.observe(scroller);
  const page = scroller.firstElementChild;
  if (page) resizeObserver.observe(page);
  const dock = document.getElementById("page-footer-dock");
  if (dock) resizeObserver.observe(dock);
  if (rootEl.value) resizeObserver.observe(rootEl.value);
  updatePinned();
}

onMounted(() => {
  window.addEventListener("resize", updatePinned);
  nextTick(observe);
});

onBeforeUnmount(() => {
  window.removeEventListener("resize", updatePinned);
  resizeObserver?.disconnect();
});
</script>

<template>
  <Teleport defer :disabled="!pinned" to="#page-footer-dock">
    <div
      ref="rootEl"
      data-slot="page-dock-footer"
      :class="pinned ? 'adaptive-padding' : ''"
    >
      <div
        class="flex items-center justify-between gap-3 border-t bg-background py-2 text-sm"
      >
        <slot />
      </div>
    </div>
  </Teleport>
</template>
