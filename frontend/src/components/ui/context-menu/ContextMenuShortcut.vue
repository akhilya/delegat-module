<script setup>
import { cn } from "@/lib/utils";
import { MenuKbd } from "@/components/ui/kbd";

const props = defineProps({
  class: { type: null, required: false },
  /** When set, renders MenuKbd (preferred). Otherwise use the default slot. */
  keys: { type: Array, required: false },
});
</script>

<template>
  <span
    data-slot="context-menu-shortcut"
    :class="
      cn('text-muted-foreground ml-auto flex items-center text-xs tracking-widest', props.class)
    "
  >
    <MenuKbd v-if="keys?.length" :keys="keys" />
    <slot v-else />
  </span>
</template>
