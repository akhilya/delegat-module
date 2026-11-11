<script setup>
import { AlertTriangle } from "lucide-vue-next";
import { t } from "@/i18n";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover";

defineProps({
  warning: { type: Object, default: null },
});

function unitLabel(value) {
  return value || t("marketplace.graph.unitNotSpecified");
}
</script>

<template>
  <Popover v-if="warning" class="inline-flex w-auto" :modal="false">
    <PopoverTrigger as-child>
      <button
        type="button"
        class="inline-flex size-6 shrink-0 items-center justify-center rounded-sm text-warning-foreground hover:bg-warning-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        :aria-label="t('marketplace.graph.unitMismatchTitle')"
        :title="t('marketplace.graph.unitMismatchTitle')"
        @click.stop
      >
        <AlertTriangle class="size-3.5" aria-hidden="true" />
      </button>
    </PopoverTrigger>
    <PopoverContent side="top" :side-offset="6" class="w-72 space-y-2 text-xs">
      <p class="font-medium">{{ t("marketplace.graph.unitMismatchTitle") }}</p>
      <p class="text-muted-foreground">
        {{
          t("marketplace.graph.unitMismatchWarning", {
            source: unitLabel(warning.sourceLabel),
            target: unitLabel(warning.targetLabel),
          })
        }}
      </p>
      <p>{{ t("marketplace.graph.unitMismatchNoConversion") }}</p>
      <p class="text-muted-foreground">
        {{ t("marketplace.graph.unitMismatchExport") }}
      </p>
    </PopoverContent>
  </Popover>
</template>
