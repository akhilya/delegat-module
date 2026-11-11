<script setup>
import { cn } from "@/lib/utils";
import { buttonGroupVariants } from ".";

const props = defineProps({
  class: { type: null, required: false },
  joined: { type: Boolean, default: false },
  orientation: {
    type: String,
    required: false,
    default: "horizontal",
  },
});
</script>

<template>
  <div
    role="group"
    data-slot="button-group"
    :data-interaction="props.joined ? 'joined' : undefined"
    :data-orientation="props.orientation"
    :class="
      cn(buttonGroupVariants({ orientation: props.orientation }), props.class)
    "
  >
    <slot />
  </div>
</template>
