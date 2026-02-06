<script setup>
import { ProgressIndicator, ProgressRoot } from "reka-ui";
import { cn } from "@/lib/utils";
import { computed } from "vue";

const props = defineProps({
  modelValue: { type: Number, default: 0 },
  max: { type: Number, default: 100 },
  class: { type: null, required: false },
});

const percentage = computed(() => {
  return Math.min(100, Math.max(0, (props.modelValue / props.max) * 100));
});
</script>

<template>
  <ProgressRoot
    :model-value="modelValue"
    :max="max"
    :class="
      cn(
        'relative h-2 w-full overflow-hidden rounded-full bg-primary/20',
        props.class
      )
    "
  >
    <ProgressIndicator
      class="h-full bg-primary transition-all duration-300 ease-in-out"
      :style="{ width: `${percentage}%` }"
    />
  </ProgressRoot>
</template>
