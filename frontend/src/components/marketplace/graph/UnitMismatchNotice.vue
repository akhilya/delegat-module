<script setup>
import { inject } from "vue";
import { AlertTriangle } from "lucide-vue-next";
import { Button } from "@/components/ui/button";
import { t } from "@/i18n";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";

const props = defineProps({
  warning: { type: Object, default: null },
});

const fixUnits = inject("mappingUnitFix", null);

function unitLabel(value) {
  return value || t("marketplace.graph.unitNotSpecified");
}

function onClick() {
  if (props.warning?.convertible) fixUnits?.(props.warning);
}
</script>

<template>
  <TooltipProvider v-if="warning" :delay-duration="150">
    <Tooltip>
      <TooltipTrigger as-child>
        <Button
          variant="ghost"
          size="icon-xs"
          class="shrink-0 rounded-sm text-warning-foreground hover:bg-warning-muted"
          :class="warning.convertible ? 'cursor-pointer' : 'cursor-help'"
          :aria-label="t('marketplace.graph.unitMismatchTitle')"
          @click.stop="onClick"
        >
          <AlertTriangle class="size-3.5" aria-hidden="true" />
        </Button>
      </TooltipTrigger>
      <TooltipContent
        side="top"
        :side-offset="6"
        class="max-w-72 space-y-1.5 text-xs"
      >
        <p class="font-medium">
          {{ t("marketplace.graph.unitMismatchTitle") }}
        </p>
        <p>
          {{
            t("marketplace.graph.unitMismatchWarning", {
              source: unitLabel(warning.sourceLabel),
              target: unitLabel(warning.targetLabel),
            })
          }}
        </p>
        <template v-if="warning.convertible">
          <p class="font-medium">
            {{
              t("marketplace.graph.unitMismatchFixHint", {
                source: unitLabel(warning.sourceLabel),
                target: unitLabel(warning.targetLabel),
              })
            }}
          </p>
        </template>
        <template v-else>
          <p>{{ t("marketplace.graph.unitMismatchNoConversion") }}</p>
          <p class="opacity-80">
            {{ t("marketplace.graph.unitMismatchExport") }}
          </p>
        </template>
      </TooltipContent>
    </Tooltip>
  </TooltipProvider>
</template>
