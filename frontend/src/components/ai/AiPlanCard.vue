<script setup>
import { computed, onBeforeUnmount, ref, watch } from "vue";
import { useStore } from "vuex";
import { AlertTriangle, Check, ChevronDown, Loader2, X } from "lucide-vue-next";
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
  planStepList,
  planStepDescriptionNeedsCollapse,
  planStepPreview,
  planStepsNeedCollapse,
} from "@/lib/ai/planSteps";
import { useI18n } from "vue-i18n";
import { fetchWithAuth } from "@/utils/fetchWithAuth";
import {
  operationIdsFromToolCalls,
  summarizePlanOperations,
} from "@/lib/ai/planOperations";
import { toolResultFailed } from "@/lib/ai/persist";

const props = defineProps({
  plan: { type: Object, required: true },
  confirmed: { type: Boolean, default: false },
  cancelled: { type: Boolean, default: false },
  superseded: { type: Boolean, default: false },
  executed: { type: Boolean, default: false },
  stopped: { type: Boolean, default: false },
  failed: { type: Boolean, default: false },
  confirming: { type: Boolean, default: false },
  confirmationBlocked: { type: Boolean, default: false },
  toolCalls: { type: Array, default: () => [] },
  ready: { type: Boolean, default: true },
});
defineEmits(["confirm", "cancel"]);
const { t } = useI18n();
const store = useStore();
const expanded = ref(false);
const expandedDescriptions = ref({});
const operationStatuses = ref({});
let pollTimer;
let pollGeneration = 0;
let pollController;

// The displayed order is the execution order, including interleaved catalog writes.
const steps = computed(() => planStepList(props.plan));
const needsCollapse = computed(() => planStepsNeedCollapse(steps.value.length));
const visibleSteps = computed(() =>
  expanded.value ? steps.value : steps.value.slice(0, PLAN_STEP_PREVIEW),
);
const operationIds = computed(() => operationIdsFromToolCalls(props.toolCalls));
const operationSummary = computed(() =>
  summarizePlanOperations(operationIds.value, operationStatuses.value),
);

async function pollOperations(generation, ids, signal) {
  await Promise.all(
    ids.map(async (id) => {
      try {
        const response = await fetchWithAuth(
          `/api/operations/${encodeURIComponent(id)}/status`,
          { signal },
        );
        const status =
          response.status === 404
            ? { status: "missing" }
            : response.ok
              ? await response.json()
              : { status: "unknown" };
        if (generation === pollGeneration && !signal.aborted)
          operationStatuses.value[id] = status;
      } catch {
        if (generation === pollGeneration && !signal.aborted)
          operationStatuses.value[id] = { status: "unknown" };
      }
    }),
  );
  if (
    generation === pollGeneration &&
    !signal.aborted &&
    operationSummary.value.tracking
  )
    pollTimer = setTimeout(() => pollOperations(generation, ids, signal), 2500);
}
watch(
  () => `${props.plan.proposalId}:${operationIds.value.join("|")}`,
  () => {
    clearTimeout(pollTimer);
    pollController?.abort();
    const generation = ++pollGeneration;
    operationStatuses.value = {};
    pollController = new AbortController();
    if (operationIds.value.length)
      pollOperations(
        generation,
        [...operationIds.value],
        pollController.signal,
      );
  },
  { immediate: true },
);
onBeforeUnmount(() => {
  ++pollGeneration;
  pollController?.abort();
  clearTimeout(pollTimer);
});

const pending = computed(
  () =>
    !props.confirmed &&
    !props.cancelled &&
    !props.superseded &&
    !props.executed &&
    !props.stopped &&
    !props.failed &&
    !props.confirmationBlocked,
);
const unknown = computed(
  () =>
    operationSummary.value.unknown ||
    (props.confirmed && !props.executed && props.ready),
);
const partial = computed(
  () =>
    operationSummary.value.partial ||
    (props.failed &&
      props.toolCalls.some(
        (tool) =>
          tool.result != null && !toolResultFailed(tool.result, tool.summary),
      )),
);
const status = computed(() => {
  if (props.stopped) return "generationStopped";
  if (props.cancelled) return "planCancelled";
  if (props.superseded) return "planSuperseded";
  if (props.confirming) return "planConfirming";
  if (props.confirmationBlocked) return "planConfirmationUnavailable";
  if (operationSummary.value.active) return "planBackgroundRunning";
  if (partial.value) return "planPartial";
  if (props.failed || operationSummary.value.failed) return "planFailed";
  if (unknown.value) return "planOutcomeUnknown";
  if (props.executed) return "planExecuted";
  if (props.confirmed) return "planExecuting";
  return "planAwaitingConfirmation";
});
const busy = computed(() =>
  ["planConfirming", "planExecuting", "planBackgroundRunning"].includes(
    status.value,
  ),
);
const warning = computed(() =>
  ["planPartial", "planOutcomeUnknown", "planConfirmationUnavailable"].includes(
    status.value,
  ),
);
</script>

<template>
  <InsetGroup class="w-full min-w-0 gap-0" data-testid="ai-plan">
    <div class="px-3 pt-3 pb-2">
      <div
        class="flex flex-wrap items-center justify-between gap-x-3 gap-y-1 text-xs text-muted-foreground"
      >
        <span>{{ t("ai.planTitle") }}</span>
        <span
          role="status"
          class="inline-flex items-center gap-1.5"
          :class="
            status === 'planFailed'
              ? 'text-destructive'
              : warning
                ? 'text-warning-foreground'
                : ''
          "
        >
          <Loader2
            v-if="busy"
            class="size-3.5 motion-safe:animate-spin"
            aria-hidden="true"
          />
          <Check
            v-else-if="status === 'planExecuted'"
            class="size-3.5"
            aria-hidden="true"
          />
          <AlertTriangle
            v-else-if="warning || status === 'planFailed'"
            class="size-3.5"
            aria-hidden="true"
          />
          <X
            v-else-if="cancelled || superseded"
            class="size-3.5"
            aria-hidden="true"
          />
          {{ t(`ai.${status}`) }}
        </span>
      </div>
      <p
        v-if="plan.summary"
        class="mt-2 text-sm font-medium leading-snug break-words"
      >
        {{ plan.summary }}
      </p>
      <p
        v-if="plan.affectedCount"
        class="mt-1 text-xs tabular-nums text-muted-foreground"
      >
        {{ t("ai.planAffected") }} {{ plan.affectedCount }}
      </p>
    </div>

    <ol v-if="steps.length" class="mx-3 border-t border-border/70 py-1">
      <li
        v-for="(step, i) in visibleSteps"
        :key="i"
        class="flex items-start gap-2.5 py-2"
      >
        <span
          class="w-4 shrink-0 pt-0.5 text-xs tabular-nums text-muted-foreground"
          aria-hidden="true"
          >{{ i + 1 }}.</span
        >
        <Collapsible
          v-if="planStepDescriptionNeedsCollapse(step)"
          v-model:open="expandedDescriptions[i]"
          class="min-w-0 grow"
        >
          <CollapsibleTrigger
            class="flex min-h-8 w-full items-start justify-between gap-2 rounded-sm text-left text-sm leading-relaxed outline-none focus-visible:ring-2 focus-visible:ring-ring data-[state=open]:[&_svg]:rotate-180"
          >
            <span class="min-w-0 break-words">{{
              expandedDescriptions[i]
                ? t("ai.planStepDetails")
                : planStepPreview(step)
            }}</span>
            <ChevronDown
              class="mt-1 size-3.5 shrink-0 text-muted-foreground"
              aria-hidden="true"
            />
          </CollapsibleTrigger>
          <CollapsibleContent
            class="pb-1 text-sm leading-relaxed break-words"
            >{{ step.description }}</CollapsibleContent
          >
        </Collapsible>
        <p v-else class="min-w-0 text-sm leading-relaxed break-words">
          {{ step.description }}
        </p>
      </li>
    </ol>
    <Button
      v-if="needsCollapse"
      variant="ghost"
      size="sm"
      class="mx-2 mb-1 justify-start text-muted-foreground"
      :aria-expanded="expanded"
      @click="expanded = !expanded"
    >
      <ChevronDown
        class="size-3.5"
        :class="expanded && 'rotate-180'"
        aria-hidden="true"
      />
      {{
        expanded
          ? t("ai.planShowLess")
          : t("ai.planShowMore", { n: planHiddenStepCount(steps.length) })
      }}
    </Button>

    <p
      v-if="status === 'planOutcomeUnknown'"
      class="px-3 pb-3 text-xs leading-relaxed text-muted-foreground"
    >
      {{ t("ai.planOutcomeUnknownHint") }}
    </p>
    <div
      v-if="pending"
      class="flex flex-wrap items-center gap-2 px-3 pt-1 pb-3"
    >
      <Button
        size="sm"
        :disabled="!ready || confirming"
        @click="$emit('confirm', plan.proposalId)"
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
        size="sm"
        class="text-muted-foreground"
        :disabled="!ready || confirming"
        @click="$emit('cancel', plan.proposalId)"
        >{{ t("common.cancel") }}</Button
      >
    </div>
    <div
      v-if="operationIds.length"
      class="mx-3 flex flex-wrap items-center justify-between gap-2 border-t border-border/70 py-2 text-xs text-muted-foreground"
    >
      <span v-if="operationSummary.progress" class="tabular-nums">{{
        t("ai.planBackgroundProgress", operationSummary.progress)
      }}</span>
      <span v-else>{{
        t("ai.planBackgroundCount", { count: operationIds.length })
      }}</span>
      <Button
        variant="ghost"
        size="sm"
        @click="store.dispatch('operations/openPanel')"
        >{{ t("ai.planOpenOperations") }}</Button
      >
    </div>
  </InsetGroup>
</template>
