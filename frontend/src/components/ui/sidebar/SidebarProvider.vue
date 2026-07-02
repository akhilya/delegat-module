<script setup>
import { provideSidebar } from "./sidebar";
import { cn } from "@/lib/utils";

const props = defineProps({
  defaultOpen: { type: Boolean, default: false },
  class: { type: null, required: false },
});

provideSidebar(props.defaultOpen);
</script>

<template>
  <div
    data-slot="sidebar-wrapper"
    :class="cn('flex min-h-screen w-full bg-background text-foreground', props.class)"
  >
    <slot />
  </div>
</template>
