<script setup>
import { Primitive } from "reka-ui";
import { cn } from "@/lib/utils";
import { markerVariants } from ".";

const props = defineProps({
  variant: { type: null, required: false },
  class: {
    type: [Boolean, null, String, Object, Array],
    required: false,
    skipCheck: true,
  },
  asChild: { type: Boolean, required: false },
  as: { type: null, required: false, default: "div" },
});
</script>

<template>
  <Primitive
    data-slot="marker"
    :data-variant="variant"
    :as="as"
    :as-child="asChild"
    :class="cn(markerVariants({ variant }), props.class)"
  >
    <slot />
  </Primitive>
</template>
