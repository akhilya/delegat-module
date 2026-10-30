<script setup>
import { computed, onBeforeUnmount, ref, watch } from "vue";
import { useStore } from "vuex";
import { Check, ChevronDown, X } from "lucide-vue-next";
import { Button } from "@/components/ui/button";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible";
import { InsetGroup } from "@/components/ui/inset-group";
import {
  PLAN_STEP_PREVIEW,
  planHiddenStepCount,
  planHasContent,
  planStepPresentation,
  planStepsNeedCollapse,
} from "@/lib/ai/planSteps";
import { useI18n } from "vue-i18n";
import { fetchWithAuth } from "@/utils/fetchWithAuth";
import {
  operationIdsFromToolCalls,
  summarizePlanOperations,
} from "@/lib/ai/planOperations";

const props = defineProps({
  plan: { type: Object, required: true },
  confirmed: { type: Boolean, default: false },
  cancelled: { type: Boolean, default: false },
  superseded: { type: Boolean, default: false },
  executed: { type: Boolean, default: false },
  failed: { type: Boolean, default: false },
  confirming: { type: Boolean, default: false },
  confirmationBlocked: { type: Boolean, default: false },
  toolCalls: { type: Array, default: () => [] },
  ready: { type: Boolean, default: true },
});
defineEmits(["confirm", "cancel"]);

const { t, locale } = useI18n();
const store = useStore();
const expanded = ref(false);
const productDetailsExpanded = ref(false);
const operationStatuses = ref({});
let pollTimer = null;
let pollStopped = false;

const stepPresentation = computed(() => planStepPresentation(props.plan));
const steps = computed(() => stepPresentation.value.otherSteps);
const productChanges = computed(() => stepPresentation.value.productChanges);
const productChangeDetails = computed(() =>
  productChanges.value.filter((change) => change.description.trim()),
);
const bulkPriceChanges = computed(() =>
  productChanges.value.filter((change) => change.kind === "bulk-price"),
);
const individualProductChangeCount = computed(
  () =>
    productChanges.value.filter((change) => change.kind === "product").length,
);
const productDetailsNeedCollapse = computed(
  () =>
    productChangeDetails.value.length > 1 || bulkPriceChanges.value.length > 0,
);
const needsCollapse = computed(() => planStepsNeedCollapse(steps.value.length));
const hiddenCount = computed(() => planHiddenStepCount(steps.value.length));
const head = computed(() =>
  needsCollapse.value ? steps.value.slice(0, PLAN_STEP_PREVIEW) : steps.value,
);
const rest = computed(() =>
  needsCollapse.value ? steps.value.slice(PLAN_STEP_PREVIEW) : [],
);
const operationIds = computed(() => operationIdsFromToolCalls(props.toolCalls));
const operationSummary = computed(() =>
  summarizePlanOperations(operationIds.value, operationStatuses.value),
);

function formatPriceDelta(value) {
  const amount = new Intl.NumberFormat(locale.value, {
    maximumFractionDigits: 2,
  }).format(Math.abs(value));
  if (value > 0) return `+${amount}`;
  if (value < 0) return `−${amount}`;
  return amount;
}

async function pollOperations() {
  if (pollStopped || !operationIds.value.length) return;
  await Promise.all(
    operationIds.value.map(async (id) => {
      try {
        const response = await fetchWithAuth(
          `/api/operations/${encodeURIComponent(id)}/status`,
        );
        if (response.status === 404) {
          operationStatuses.value[id] = { status: "missing" };
        } else if (response.ok) {
          operationStatuses.value[id] = await response.json();
        }
      } catch {
        // Retry transient failures while this card remains mounted.
      }
    }),
  );
  if (!pollStopped && operationSummary.value.tracking) {
    pollTimer = setTimeout(pollOperations, 2500);
  }
}

watch(
  () => operationIds.value.join("|"),
  () => {
    clearTimeout(pollTimer);
    pollStopped = false;
    operationStatuses.value = {};
    if (operationIds.value.length) pollOperations();
  },
  { immediate: true },
);

onBeforeUnmount(() => {
  pollStopped = true;
  clearTimeout(pollTimer);
});

function openOperations() {
  store.dispatch("operations/openPanel");
}

const pending = computed(
  () =>
    !props.confirmed &&
    !props.cancelled &&
    !props.superseded &&
    !props.confirmationBlocked,
);

const statusLabel = computed(() => {
  if (props.confirmationBlocked) return t("ai.planConfirmationUnavailable");
  if (operationSummary.value.active && operationIds.value.length)
    return t("ai.planBackgroundRunning");
  if (operationSummary.value.failed) return t("ai.planFailed");
  if (props.superseded) return t("ai.planSuperseded");
  if (props.cancelled) return t("ai.planCancelled");
  if (props.failed) return t("ai.planFailed");
  if (props.executed) return t("ai.planExecuted");
  return t("ai.planExecuting");
});
</script>

<template>
  <InsetGroup class="w-full gap-0">
    <div class="px-3 pt-2.5 pb-2">
      <p class="text-xs text-muted-foreground">{{ t("ai.planTitle") }}</p>
      <p
        v-if="planHasContent(plan) && plan.summary"
        class="mt-0.5 text-[15px] font-medium leading-snug tracking-tight"
      >
        {{ plan.summary }}
      </p>
      <p
        v-if="plan.affectedCount"
        class="mt-1 text-xs tabular-nums text-muted-foreground"
      >
        {{ t("ai.planAffected") }}
        <span class="text-foreground">{{ plan.affectedCount }}</span>
      </p>
    </div>

    <div v-if="productChanges.length" class="mx-1 border-t border-border/70">
      <div class="flex items-center justify-between gap-3 px-2 py-2">
        <span class="text-sm font-medium">{{
          t("ai.planProductChanges")
        }}</span>
        <span
          v-if="individualProductChangeCount > 1"
          class="shrink-0 text-xs tabular-nums text-muted-foreground"
        >
          {{
            t("ai.planProductChangeCount", {
              count: individualProductChangeCount,
            })
          }}
        </span>
      </div>

      <div
        v-for="(change, i) in bulkPriceChanges"
        :key="'price-' + i"
        class="grid grid-cols-[1fr_auto] gap-x-3 gap-y-0.5 px-2 pb-2 text-sm"
      >
        <span>{{ t("ai.planPriceDelta") }}</span>
        <span class="text-right font-medium tabular-nums">
          {{ formatPriceDelta(change.priceDelta) }}
        </span>
        <span
          v-if="
            change.targetCount !== null &&
            (!plan.affectedCount || change.targetCount !== plan.affectedCount)
          "
          class="col-span-2 text-xs text-muted-foreground"
        >
          {{ t("ai.planProductTargets", { count: change.targetCount }) }}
        </span>
      </div>

      <ol
        v-if="productChangeDetails.length && !productDetailsNeedCollapse"
        class="border-t border-border/70"
      >
        <li class="px-2 py-2 text-sm leading-snug">
          {{ productChangeDetails[0].description }}
        </li>
      </ol>

      <Collapsible
        v-else-if="productChangeDetails.length"
        v-model:open="productDetailsExpanded"
      >
        <CollapsibleContent>
          <ol class="border-t border-border/70">
            <li
              v-for="(change, i) in productChangeDetails"
              :key="'product-detail-' + i"
              class="border-b border-border/70 px-2 py-2 text-sm leading-snug last:border-b-0"
            >
              {{ change.description }}
            </li>
          </ol>
        </CollapsibleContent>
        <CollapsibleTrigger
          class="flex min-h-9 w-full items-center gap-2 border-t border-border/70 px-2 py-2 text-left text-xs font-medium text-muted-foreground outline-none transition-colors duration-[var(--duration-ui)] ease-[var(--ease-out-quart)] hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring/50 data-[state=open]:[&_svg]:rotate-180"
        >
          <ChevronDown
            class="size-3.5 shrink-0 transition-transform duration-[var(--duration-ui)] ease-[var(--ease-out-quart)] motion-reduce:transition-none"
          />
          <span>
            {{
              productDetailsExpanded
                ? t("ai.planProductDetailsHide")
                : t("ai.planProductDetails", {
                    count: productChangeDetails.length,
                  })
            }}
          </span>
        </CollapsibleTrigger>
      </Collapsible>
    </div>

    <ol v-if="steps.length" class="mx-1">
      <li
        v-for="(step, i) in head"
        :key="'head-' + i"
        class="flex items-start gap-2.5 border-t border-border/70 px-2 py-2"
      >
        <span
          class="mt-0.5 flex size-5 shrink-0 items-center justify-center rounded-full bg-muted text-[11px] font-medium tabular-nums text-muted-foreground"
        >
          {{ i + 1 }}
        </span>
        <p class="min-w-0 text-sm leading-snug">{{ step.description }}</p>
      </li>
    </ol>

    <Collapsible v-if="needsCollapse" v-model:open="expanded">
      <CollapsibleContent>
        <ol class="mx-1">
          <li
            v-for="(step, i) in rest"
            :key="'rest-' + i"
            class="flex items-start gap-2.5 border-t border-border/70 px-2 py-2"
          >
            <span
              class="mt-0.5 flex size-5 shrink-0 items-center justify-center rounded-full bg-muted text-[11px] font-medium tabular-nums text-muted-foreground"
            >
              {{ PLAN_STEP_PREVIEW + i + 1 }}
            </span>
            <p class="min-w-0 text-sm leading-snug">{{ step.description }}</p>
          </li>
        </ol>
      </CollapsibleContent>
      <CollapsibleTrigger
        class="mx-1 flex w-[calc(100%-0.5rem)] items-center gap-2.5 border-t border-border/70 px-2 py-2 text-left text-xs font-medium text-muted-foreground outline-none transition-colors duration-[var(--duration-ui)] ease-[var(--ease-out-quart)] hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring/50 data-[state=open]:[&_svg]:rotate-180"
      >
        <ChevronDown
          class="size-3.5 shrink-0 transition-transform duration-[var(--duration-ui)] ease-[var(--ease-out-quart)] motion-reduce:transition-none"
        />
        <span>
          {{
            expanded
              ? t("ai.planShowLess")
              : t("ai.planShowMore", { n: hiddenCount })
          }}
        </span>
      </CollapsibleTrigger>
    </Collapsible>

    <div v-if="pending" class="flex flex-col gap-1 px-2 pt-1 pb-2">
      <Button
        class="h-9 w-full"
        :disabled="!ready || confirming"
        @click="$emit('confirm')"
      >
        {{
          confirming
            ? t("ai.planConfirming")
            : ready
              ? t("ai.planExecute")
              : t("ai.planPreparing")
        }}
      </Button>
      <Button
        variant="ghost"
        class="h-9 w-full text-muted-foreground"
        :disabled="confirming"
        @click="$emit('cancel')"
      >
        {{ t("common.cancel") }}
      </Button>
    </div>
    <div
      v-else
      class="flex items-center gap-2 border-t border-border/70 px-3 py-2.5 text-xs text-muted-foreground"
    >
      <Check
        v-if="confirmed && !cancelled && !failed && !operationSummary.failed"
        class="size-3.5"
      />
      <X
        v-else
        class="size-3.5"
        :class="
          (confirmationBlocked || failed || operationSummary.failed) &&
          'text-destructive'
        "
      />
      <span
        :class="
          (confirmationBlocked || failed || operationSummary.failed) &&
          'text-destructive'
        "
        >{{ statusLabel }}</span
      >
    </div>
    <div
      v-if="operationIds.length"
      class="flex items-center justify-between gap-3 border-t border-border/70 px-3 py-2 text-xs text-muted-foreground"
    >
      <span v-if="operationSummary.progress" class="tabular-nums">
        {{ t("ai.planBackgroundProgress", operationSummary.progress) }}
      </span>
      <span v-else>{{
        t("ai.planBackgroundCount", { count: operationIds.length })
      }}</span>
      <Button
        variant="ghost"
        size="sm"
        class="h-7 px-2"
        @click="openOperations"
      >
        {{ t("ai.planOpenOperations") }}
      </Button>
    </div>
  </InsetGroup>
</template>
