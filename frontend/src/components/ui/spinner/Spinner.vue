<script setup>
import { Loading03Icon } from "@/lib/hugeicons";
import { cn } from "@/lib/utils";
import { spinnerVariants } from ".";

const props = defineProps({
  /**
   * Canonical loader scale (Catalog Workbench):
   * xs  12px — dense chips / nested status
   * sm  14px — compact toolbars
   * default 16px — buttons, menus, form actions
   * lg  20px — panel / list placeholders
   * xl  24px — overlays / mid-block
   * 2xl 32px — EmptyState / full-region loading
   */
  size: {
    type: String,
    default: "default",
    validator: (v) =>
      ["xs", "sm", "default", "lg", "xl", "2xl"].includes(v),
  },
  /** Accessible name (default: Loading). */
  label: { type: String, default: "Loading" },
  class: { type: null, required: false },
});
</script>

<template>
  <Loading03Icon
    role="status"
    data-slot="spinner"
    :data-size="size"
    :aria-label="label"
    :class="cn(spinnerVariants({ size }), props.class)"
  />
</template>
