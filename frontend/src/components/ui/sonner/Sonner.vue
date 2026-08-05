<script setup>
import { computed } from "vue";
import { Toaster as Sonner } from "vue-sonner";
import { isDark } from "@/theme";

const props = defineProps({
  invert: { type: Boolean, required: false },
  theme: { type: String, required: false },
  position: { type: String, required: false, default: "bottom-right" },
  hotkey: { type: Array, required: false },
  richColors: { type: Boolean, required: false, default: false },
  expand: { type: Boolean, required: false },
  duration: { type: Number, required: false },
  gap: { type: Number, required: false },
  visibleToasts: { type: Number, required: false },
  closeButton: { type: Boolean, required: false },
  toastOptions: { type: Object, required: false },
  class: { type: String, required: false },
  style: { type: Object, required: false },
  offset: { type: [Object, String, Number], required: false },
  mobileOffset: { type: [Object, String, Number], required: false },
  dir: { type: String, required: false },
  swipeDirections: { type: Array, required: false },
  icons: { type: Object, required: false },
  containerAriaLabel: { type: String, required: false },
});

/** Follow app theme (DESIGN light default); override via `theme` prop if set. */
const resolvedTheme = computed(() => {
  if (props.theme) return props.theme;
  return isDark.value ? "dark" : "light";
});

const toasterStyle = computed(() => ({
  // Catalog Workbench: flat surface + shared radius (slightly tighter than cards is fine)
  "--border-radius": "var(--radius-md)",
  "--normal-bg": "var(--popover)",
  "--normal-text": "var(--popover-foreground)",
  "--normal-border": "var(--border)",
  "--success-bg": "var(--success-muted)",
  "--success-border": "var(--success)",
  "--success-text": "var(--success-foreground)",
  "--error-bg": "var(--error-muted)",
  "--error-border": "var(--destructive)",
  "--error-text": "var(--destructive)",
  "--warning-bg": "var(--warning-muted)",
  "--warning-border": "var(--warning)",
  "--warning-text": "var(--warning-foreground)",
  ...(props.style || {}),
}));
</script>

<template>
  <Sonner
    class="toaster group"
    v-bind="props"
    :theme="resolvedTheme"
    :rich-colors="props.richColors"
    :style="toasterStyle"
  />
</template>
