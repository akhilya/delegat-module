<script setup>
import { Primitive } from "reka-ui";
import { cn } from "@/lib/utils";

const props = defineProps({
  asChild: { type: Boolean, required: false },
  as: { type: null, default: "button" },
  isActive: { type: Boolean, default: false },
  size: { type: String, default: "default" },
  class: { type: null, required: false },
});
</script>

<template>
  <Primitive
    data-slot="sidebar-menu-button"
    :as="as"
    :as-child="asChild"
    :data-active="isActive"
    :class="
      cn(
        'flex w-full items-center gap-2 overflow-hidden rounded-md px-2 text-left text-sm outline-none transition-colors hover:bg-sidebar-accent hover:text-sidebar-accent-foreground focus-visible:ring-2 focus-visible:ring-sidebar-ring disabled:pointer-events-none disabled:opacity-50 data-[active=true]:bg-sidebar-accent data-[active=true]:font-medium data-[active=true]:text-sidebar-accent-foreground [&>svg]:size-4 [&>svg]:shrink-0',
        size === 'lg' ? 'h-11' : 'h-8',
        props.class,
      )
    "
  >
    <slot />
  </Primitive>
</template>
