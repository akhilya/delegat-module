<script setup>
import { computed, ref, watch } from "vue";
import { Braces, ChevronDown } from "lucide-vue-next";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible";
import AiRunStatus from "./AiRunStatus.vue";
import { pythonRunParts, stripDisplayEmoji } from "@/lib/ai/display";
import { useI18n } from "vue-i18n";

const props = defineProps({
  tool: { type: Object, required: true },
});

const { t } = useI18n();

const running = computed(() => props.tool.status === "running");
const parts = computed(() => pythonRunParts(props.tool));
const failed = computed(() => {
  if (running.value) return false;
  if (parts.value.error) return true;
  if (parts.value.exitCode != null && Number(parts.value.exitCode) !== 0)
    return true;
  return /HTTP\s+[45]\d\d/i.test(props.tool.summary || "");
});
const statusLabel = computed(() => {
  if (props.tool.status === "stopped") return t("ai.generationStopped");
  if (running.value) return t("ai.pythonRunning");
  if (failed.value) {
    return (
      stripDisplayEmoji(parts.value.error || props.tool.summary || "") ||
      t("ai.pythonFailed")
    );
  }
  return stripDisplayEmoji(props.tool.summary || "") || t("ai.pythonDone");
});
const output = computed(() => {
  const { stdout, stderr, error } = parts.value;
  const body = [stdout, stderr, running.value ? "" : error]
    .map((chunk) => stripDisplayEmoji(chunk).trim())
    .filter(Boolean)
    .join("\n");
  if (body) return body;
  if (!running.value && failed.value)
    return stripDisplayEmoji(props.tool.summary || "");
  return "";
});
const showOutput = computed(
  () =>
    !!(output.value || failed.value || (!running.value && !parts.value.code)),
);
const outputOpen = ref(!!running.value);

watch(running, (isRunning, was) => {
  if (isRunning) outputOpen.value = true;
  else if (was) outputOpen.value = false;
});
</script>

<template>
  <div class="min-w-0">
    <div class="flex items-center gap-2 py-0.5 text-xs">
      <Braces class="size-3.5 shrink-0 text-muted-foreground" />
      <span class="min-w-0 flex-1 truncate">{{ t("ai.pythonRun") }}</span>
      <span
        class="max-w-[45%] truncate text-[11px]"
        :class="failed ? 'text-destructive' : 'text-muted-foreground'"
      >
        {{ statusLabel }}
      </span>
      <AiRunStatus
        :running="running"
        :stopped="tool.status === 'stopped'"
        :failed="failed"
      />
    </div>

    <Collapsible v-if="parts.code">
      <CollapsibleTrigger
        class="flex w-full items-center justify-between gap-2 rounded-md py-0.5 pl-5 text-left text-[11px] text-muted-foreground outline-none transition-colors duration-[var(--duration-ui)] ease-[var(--ease-out-quart)] hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring/50 data-[state=open]:[&_svg]:rotate-180"
      >
        <span>{{ t("ai.pythonScript") }}</span>
        <ChevronDown
          class="size-3 shrink-0 transition-transform duration-[var(--duration-ui)] ease-[var(--ease-out-quart)]"
        />
      </CollapsibleTrigger>
      <CollapsibleContent class="duration-[var(--duration-ui)]">
        <pre
          class="mb-1 ml-5 max-h-32 overflow-auto rounded-md bg-muted/60 px-2 py-1.5 font-mono text-[11px] leading-relaxed text-foreground/80 whitespace-pre-wrap break-all"
          >{{ parts.code }}</pre>
      </CollapsibleContent>
    </Collapsible>

    <Collapsible v-if="showOutput" v-model:open="outputOpen">
      <CollapsibleTrigger
        class="flex w-full items-center justify-between gap-2 rounded-md py-0.5 pl-5 text-left text-[11px] outline-none transition-colors duration-[var(--duration-ui)] ease-[var(--ease-out-quart)] hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring/50 data-[state=open]:[&_svg]:rotate-180"
        :class="failed ? 'text-destructive' : 'text-muted-foreground'"
      >
        <span>{{ t("ai.pythonOutput") }}</span>
        <ChevronDown
          class="size-3 shrink-0 transition-transform duration-[var(--duration-ui)] ease-[var(--ease-out-quart)]"
        />
      </CollapsibleTrigger>
      <CollapsibleContent class="duration-[var(--duration-ui)]">
        <div class="mb-1 ml-5 rounded-md bg-muted/80 px-2 py-1.5">
          <pre
            v-if="output"
            class="max-h-32 overflow-auto font-mono text-[11px] leading-relaxed whitespace-pre-wrap break-words"
            >{{ output }}</pre>
          <p v-else class="text-[11px] text-muted-foreground">
            {{ t("ai.pythonNoOutput") }}
          </p>
        </div>
      </CollapsibleContent>
    </Collapsible>
  </div>
</template>
