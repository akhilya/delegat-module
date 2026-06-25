<script setup>
import { Primitive } from "reka-ui";
import { cn } from "@/lib/utils";

const props = defineProps({
  asChild: { type: Boolean, required: false },
  as: { type: null, default: "a" },
  isActive: { type: Boolean, default: false },
  class: { type: null, required: false },
});
</script>

<template>
  <Primitive
    data-slot="sidebar-menu-sub-button"
    :as="as"
    :as-child="asChild"
    :data-active="isActive"
    :class="
      cn(
        'flex h-7 min-w-0 -translate-x-px items-center gap-2 overflow-hidden rounded-md px-2 text-sidebar-foreground outline-none ring-sidebar-ring transition-colors hover:bg-sidebar-accent hover:text-sidebar-accent-foreground focus-visible:ring-2 disabled:pointer-events-none disabled:opacity-50 [&>svg]:size-4 [&>svg]:shrink-0',
        'text-xs',
        'data-[active=true]:bg-sidebar-accent data-[active=true]:text-sidebar-accent-foreground',
        props.class,
      )
    "
  >
    <slot />
  </Primitive>
</template>
