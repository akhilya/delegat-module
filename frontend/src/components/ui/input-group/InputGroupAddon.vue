<script setup>
import { cn } from "@/lib/utils";
import { inputGroupAddonVariants } from ".";

const props = defineProps({
  align: { type: String, required: false, default: "inline-start" },
  class: { type: null, required: false },
});

function handleInputGroupAddonClick(e) {
  const target = e.target;
  if (target instanceof Element && target.closest("button")) return;
  const currentTarget = e.currentTarget;
  if (!(currentTarget instanceof HTMLElement)) return;
  currentTarget.parentElement?.querySelector("input, textarea")?.focus();
}
</script>

<template>
  <div
    role="group"
    data-slot="input-group-addon"
    :data-align="props.align"
    :class="cn(inputGroupAddonVariants({ align: props.align }), props.class)"
    @click="handleInputGroupAddonClick"
  >
    <slot />
  </div>
</template>
