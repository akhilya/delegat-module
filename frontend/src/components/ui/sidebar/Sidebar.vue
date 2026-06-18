<script setup>
import { cn } from "@/lib/utils";
import { useSidebar } from "./sidebar";

const props = defineProps({
  class: { type: null, required: false },
});

const { open, setOpen } = useSidebar();
</script>

<template>
  <div
    v-if="open"
    class="fixed inset-0 z-40 bg-background/80 backdrop-blur-sm md:hidden"
    @click="setOpen(false)"
  />
  <aside
    data-slot="sidebar"
    :data-state="open ? 'open' : 'closed'"
    :class="
      cn(
        'fixed left-0 top-0 z-50 flex h-screen w-64 -translate-x-full flex-col border-r bg-sidebar text-sidebar-foreground shadow-lg transition-transform md:sticky md:z-0 md:translate-x-0 md:shadow-none',
        open ? 'translate-x-0' : '',
        props.class,
      )
    "
  >
    <slot />
  </aside>
</template>
