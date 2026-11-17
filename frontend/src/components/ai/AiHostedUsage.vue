<script setup>
import { computed } from "vue";
import { useI18n } from "vue-i18n";
import { AlertTriangle } from "lucide-vue-next";
import { Button } from "@/components/ui/button";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover";
import { Progress } from "@/components/ui/progress";
import { InsetGroup } from "@/components/ui/inset-group";

const props = defineProps({
  status: { type: Object, default: null },
  inline: { type: Boolean, default: false },
});
const { t, locale } = useI18n();
const visible = computed(
  () => props.status?.configured && props.status?.entitled,
);
const percentage = computed(() => {
  const value = Number(props.status?.usagePercent);
  return Number.isFinite(value) ? Math.min(100, Math.max(0, value)) : 0;
});
const percentText = computed(() =>
  new Intl.NumberFormat(locale.value, {
    style: "percent",
    maximumFractionDigits: 1,
  }).format(percentage.value / 100),
);
const paused = computed(() => !!props.status?.usageUnavailable);
const exhausted = computed(() => !!props.status?.quotaExhausted);
const label = computed(() =>
  paused.value
    ? t("ai.hostedUsageUnavailable")
    : exhausted.value
      ? t("ai.hostedUsageExhausted")
      : t("ai.hostedUsageTrigger", { percent: percentText.value }),
);
</script>

<template>
  <component :is="inline ? 'section' : Popover" v-if="visible">
    <PopoverTrigger v-if="!inline" as-child>
      <Button
        type="button"
        variant="ghost"
        size="sm"
        class="h-8 gap-1.5 px-2 text-xs tabular-nums"
        :class="
          paused || exhausted
            ? 'text-warning-foreground'
            : 'text-muted-foreground'
        "
        :aria-label="label"
        :title="label"
        data-testid="hosted-ai-usage-trigger"
      >
        <AlertTriangle
          v-if="paused || exhausted"
          class="size-3.5"
          aria-hidden="true"
        />
        <svg
          v-else
          class="size-3.5 -rotate-90"
          viewBox="0 0 20 20"
          fill="none"
          aria-hidden="true"
        >
          <circle
            cx="10"
            cy="10"
            r="7"
            stroke="currentColor"
            stroke-width="2.5"
            class="opacity-20"
          />
          <circle
            cx="10"
            cy="10"
            r="7"
            pathLength="100"
            stroke="currentColor"
            stroke-width="2.5"
            :stroke-dasharray="`${percentage} 100`"
          />
        </svg>
        <span>{{ paused ? t("ai.hostedUsagePaused") : percentText }}</span>
      </Button>
    </PopoverTrigger>
    <component
      :is="inline ? InsetGroup : PopoverContent"
      v-bind="inline ? {} : { side: 'top', align: 'start' }"
      :class="inline ? 'gap-3 p-4' : 'w-72 space-y-3'"
      :style="inline ? undefined : { backgroundColor: 'var(--popover)' }"
    >
      <div class="space-y-1">
        <h3 class="text-sm font-medium">
          {{ t("ai.hostedUsageTitle") }}
        </h3>
        <p class="text-xs text-muted-foreground">
          {{ t("ai.hostedUsageScope") }}
        </p>
      </div>
      <div class="space-y-2">
        <div class="flex items-center justify-between gap-3 text-xs">
          <span>{{ t("ai.hostedUsageMonthly") }}</span>
          <span class="tabular-nums">{{ percentText }}</span>
        </div>
        <Progress
          :model-value="percentage"
          class="h-1.5 [&>div]:transition-none"
          :aria-label="t('ai.hostedUsageMonthly')"
        />
      </div>
      <p v-if="paused" class="text-xs text-warning-foreground" role="status">
        {{ t("ai.hostedUsageUnavailable") }}
      </p>
      <p
        v-else-if="exhausted"
        class="text-xs text-warning-foreground"
        role="status"
      >
        {{ t("ai.hostedUsageExhausted") }}
      </p>
    </component>
  </component>
</template>
