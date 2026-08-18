<script setup>
import { cn } from "@/lib/utils";
const props = defineProps({ class: { type: null, required: false } });
</script>

<template>
  <ul
    data-slot="sidebar-menu-sub"
    :class="cn('flex min-w-0 flex-col gap-0.5 border-t border-sidebar-border/80 pt-0.5 group-data-[collapsible=icon]:hidden', props.class)"
  >
    <slot />
  </ul>
</template>
