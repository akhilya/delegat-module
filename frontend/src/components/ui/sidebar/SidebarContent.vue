<script setup>
import { onBeforeUnmount, ref, watch } from "vue";
import { cn } from "@/lib/utils";
import { useScrollFade } from "@/composables/useScrollFade";

const props = defineProps({
  class: { type: null, required: false },
});

const root = ref(null);
const { bind, unbind } = useScrollFade(root);

watch(
  root,
  (el, prev) => {
    unbind(prev);
    bind(el);
  },
  { flush: "post" },
);

onBeforeUnmount(() => unbind(root.value));
</script>

<template>
  <div
    ref="root"
    data-slot="sidebar-content"
    data-sidebar="content"
    :class="
      cn(
        'flex min-h-0 flex-1 flex-col gap-2 overflow-y-auto scroll-fade no-scrollbar group-data-[collapsible=icon]:overflow-hidden group-data-[collapsible=icon]:scroll-fade-none',
        props.class,
      )
    "
  >
    <slot />
  </div>
</template>
