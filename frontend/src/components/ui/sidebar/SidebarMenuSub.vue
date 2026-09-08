<script setup>
import { cn } from "@/lib/utils";
import {
  SIDEBAR_NAV_INDICATOR_ITEMS,
  SIDEBAR_NAV_INDICATOR_SELECTOR,
  useSlidingIndicator,
} from "@/composables/useTabsIndicator";

const props = defineProps({ class: { type: null, required: false } });
const { setListRef, indicatorStyle } = useSlidingIndicator({
  itemSelector: SIDEBAR_NAV_INDICATOR_SELECTOR,
  observeSelector: SIDEBAR_NAV_INDICATOR_ITEMS,
  attributeFilter: ["data-active"],
  edgeEnter: true,
});
</script>

<template>
  <ul
    :ref="setListRef"
    data-slot="sidebar-menu-sub"
    :class="
      cn(
        'relative isolate flex min-w-0 flex-col gap-0.5 border-t border-sidebar-border/80 pt-0.5 group-data-[collapsible=icon]:hidden',
        props.class,
      )
    "
  >
    <span
      data-slot="sidebar-nav-indicator"
      aria-hidden="true"
      class="pointer-events-none absolute top-0 left-0 z-0 rounded-lg bg-primary"
      :style="indicatorStyle"
    />
    <slot />
  </ul>
</template>
