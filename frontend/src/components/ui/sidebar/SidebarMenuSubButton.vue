<script setup>
import { Primitive } from "reka-ui";
import { cn } from "@/lib/utils";

const props = defineProps({
  as: { type: null, default: "a" },
  asChild: { type: Boolean, required: false },
  size: { type: String, default: "md" },
  isActive: { type: Boolean, default: false },
  class: { type: null, required: false },
});
</script>

<template>
  <Primitive
    data-slot="sidebar-menu-sub-button"
    data-sidebar="menu-sub-button"
    :as="as"
    :as-child="asChild"
    :data-size="size"
    :data-active="isActive"
    :class="
      cn(
        'relative z-[1] flex h-8 min-w-0 items-center gap-2 overflow-hidden rounded-lg px-2 text-sm text-sidebar-foreground outline-hidden ring-sidebar-ring transition-colors duration-[var(--duration-ui)] ease-[var(--ease-out-quart)] hover:bg-sidebar-accent hover:text-sidebar-accent-foreground active:bg-sidebar-accent active:text-sidebar-accent-foreground focus-visible:ring-2 disabled:pointer-events-none disabled:opacity-50 aria-disabled:pointer-events-none aria-disabled:opacity-50 [&>span:last-child]:truncate [&>svg]:size-4 [&>svg]:shrink-0',
        'data-[active=true]:font-semibold data-[active=true]:text-primary-foreground data-[active=true]:[&>svg]:text-primary-foreground data-[active=true]:hover:bg-transparent data-[active=true]:hover:text-primary-foreground',
        'group-data-[collapsible=icon]:hidden',
        props.class,
      )
    "
  >
    <slot />
  </Primitive>
</template>