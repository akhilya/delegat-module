<script setup>
import { computed } from "vue";
import { Toaster as Sonner } from "vue-sonner";
import { AlertCircle, AlertTriangle, CheckCircle, X } from "lucide-vue-next";
import { useI18n } from "vue-i18n";
import { Spinner } from "@/components/ui/spinner";
import { isDark } from "@/theme";
import { useCompanionSheets } from "@/composables/useCompanionSheets";

const { t } = useI18n();
const { fabRight } = useCompanionSheets();

const props = defineProps({
  invert: { type: Boolean, required: false },
  theme: { type: String, required: false },
  position: { type: String, required: false, default: "bottom-right" },
  hotkey: { type: Array, required: false },
  richColors: { type: Boolean, required: false, default: false },
  expand: { type: Boolean, required: false },
  duration: { type: Number, required: false, default: 5000 },
  gap: { type: Number, required: false, default: 8 },
  visibleToasts: { type: Number, required: false, default: 3 },
  closeButton: { type: Boolean, required: false, default: true },
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
  "--companion-fab-right": `${fabRight.value}px`,
  "--border-radius": "var(--radius)",
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

const resolvedIcons = computed(() => ({
  success: CheckCircle,
  error: AlertCircle,
  warning: AlertTriangle,
  close: X,
  ...props.icons,
}));

const resolvedToastOptions = computed(() => ({
  closeButtonAriaLabel: t("common.close"),
  ...props.toastOptions,
}));
</script>

<template>
  <Sonner
    class="toaster group"
    v-bind="props"
    :theme="resolvedTheme"
    :rich-colors="props.richColors"
    :style="toasterStyle"
    :icons="resolvedIcons"
    :toast-options="resolvedToastOptions"
    :container-aria-label="props.containerAriaLabel || t('notifications.label')"
  >
    <template #loading-icon><Spinner /></template>
  </Sonner>
</template>
