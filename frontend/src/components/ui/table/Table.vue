<script setup>
import { computed, ref } from "vue";
import { cn } from "@/lib/utils";

const props = defineProps({
  class: { type: null, required: false },
  /**
   * When true (default), wrap the table in an overflow scroller.
   * Set false when an ancestor already owns scroll (sticky headers);
   * the root is then the <table> itself (refs / $el).
   * Callers that need `table-fixed` or `w-full` pass them on `class`.
   * Forcing those here collapsed cells (`max-w-0` + `table-fixed`,
   * or `w-full` inside a shrink-to-fit products row).
   */
  contain: { type: Boolean, default: true },
});

const rootEl = ref(null);

const tableClass = computed(() =>
  cn(
    props.contain
      ? "w-full caption-bottom text-sm tabular-nums"
      : "caption-bottom text-sm tabular-nums",
    props.class,
  ),
);

defineExpose({ $el: rootEl });
</script>

<template>
  <component
    :is="contain ? 'div' : 'table'"
    ref="rootEl"
    :data-slot="contain ? 'table-container' : 'table'"
    :class="contain ? 'relative w-full overflow-auto' : tableClass"
  >
    <table
      v-if="contain"
      data-slot="table"
      :class="tableClass"
    >
      <slot />
    </table>
    <slot v-else />
  </component>
</template>
