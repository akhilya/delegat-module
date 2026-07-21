<script setup>
import { computed, ref } from "vue";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Check, Copy } from "lucide-vue-next";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { highlightJsonHtml, prettyJson } from "./logFormat.js";
import { useI18n } from "vue-i18n";

const { t } = useI18n();

const props = defineProps({
  label: { type: String, default: "" },
  /** Raw payload - string or already-parsed object from the API */
  text: { type: [String, Object, Number, Boolean], default: "" },
  /** When true, pretty-print and syntax-highlight as JSON */
  json: { type: Boolean, default: false },
  maxHeightClass: { type: String, default: "max-h-80" },
  tone: {
    type: String,
    default: "default", // default | destructive
  },
  class: { type: null, required: false },
});

const copied = ref(false);
let copyTimer = null;

const hasContent = computed(() => {
  if (props.text == null || props.text === "") return false;
  return true;
});

const displayText = computed(() => {
  if (!hasContent.value) return "";
  return props.json ? prettyJson(props.text) : String(props.text);
});

const html = computed(() => {
  if (!props.json || !hasContent.value) return "";
  return highlightJsonHtml(props.text);
});

async function copy() {
  try {
    await navigator.clipboard.writeText(displayText.value || "");
    copied.value = true;
    displaySonnerSuccess(t("common.copied"));
    clearTimeout(copyTimer);
    copyTimer = window.setTimeout(() => {
      copied.value = false;
    }, 1500);
  } catch {
    displaySonnerError(t("common.copyFailed"));
  }
}
</script>

<template>
  <div :class="cn('space-y-1.5', $props.class)">
    <div class="flex items-center justify-between gap-2">
      <span
        :class="
          cn(
            'text-xs font-medium text-muted-foreground',
            tone === 'destructive' && 'text-destructive',
          )
        "
      >
        {{ label }}
      </span>
      <Button
        v-if="hasContent"
        variant="ghost"
        size="sm"
        class="h-7 px-2"
        @click="copy"
      >
        <Check v-if="copied" class="mr-1 size-3.5" />
        <Copy v-else class="mr-1 size-3.5" />
        {{ t("common.copy") }}
      </Button>
    </div>
    <pre
      v-if="json && html"
      :class="
        cn(
          'log-json overflow-auto rounded-md border p-3 font-mono text-xs leading-relaxed whitespace-pre-wrap break-all',
          maxHeightClass,
          tone === 'destructive'
            ? 'border-destructive/30 bg-destructive/5'
            : 'bg-muted/40',
        )
      "
      v-html="html"
    />
    <pre
      v-else
      :class="
        cn(
          'overflow-auto rounded-md border p-3 font-mono text-xs leading-relaxed whitespace-pre-wrap break-all',
          maxHeightClass,
          tone === 'destructive'
            ? 'border-destructive/30 bg-destructive/5'
            : 'bg-muted/40',
        )
      "
    >{{ displayText || "-" }}</pre>
  </div>
</template>

<style scoped>
.log-json :deep(.log-json__key) {
  color: var(--foreground);
  font-weight: 600;
}
.log-json :deep(.log-json__string) {
  color: var(--success-foreground);
}
.log-json :deep(.log-json__number) {
  color: var(--chart-1);
}
.log-json :deep(.log-json__literal) {
  color: var(--chart-2);
}
.log-json :deep(.log-json__punct) {
  color: var(--muted-foreground);
}
.log-json :deep(.log-json__trunc) {
  color: var(--muted-foreground);
  font-style: italic;
}
</style>
