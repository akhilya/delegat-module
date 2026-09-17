<script setup>
import { computed, ref, watch } from "vue";
import { ChevronDown } from "lucide-vue-next";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible";
import AiPythonRunBlock from "./AiPythonRunBlock.vue";
import AiRunStatus from "./AiRunStatus.vue";
import AiToolStep from "./AiToolStep.vue";
import {
  groupProcessItems,
  processTools,
  runningProcessTool,
  sqlQueryFromInput,
} from "@/lib/ai/display";
import { renderAiMarkdown } from "@/lib/ai/markdown";
import { MarkdownProse } from "@/components/ui/markdown-prose";
import { useI18n } from "vue-i18n";
import "./aiMessage.css";

const props = defineProps({
  process: { type: Array, default: () => [] },
  done: { type: Boolean, default: false },
  enter: { type: Boolean, default: false },
});

const { t } = useI18n();
const groups = computed(() => groupProcessItems(props.process));
const tools = computed(() => processTools(props.process));
const runningTool = computed(() => runningProcessTool(props.process));
const detailsOpen = ref(false);

watch(
  () => props.done,
  (done, was) => {
    if (done && was === false) detailsOpen.value = false;
  },
);

const headline = computed(() => {
  const running = runningTool.value;
  if (running) {
    const key = `ai.toolNames.${running.name}`;
    const translated = t(key);
    const name = translated === key ? running.name : translated;
    if (running.name === "sql_readonly") {
      const sql = sqlQueryFromInput(running.input).replace(/\s+/g, " ");
      if (sql) return `${name} · ${sql}`;
    }
    return name;
  }
  return t("ai.processUsed", tools.value.length);
});

function html(text) {
  return renderAiMarkdown(text);
}
</script>

<template>
  <div v-if="groups.length" class="flex w-full min-w-0 flex-col">
    <Collapsible
      v-if="tools.length"
      v-model:open="detailsOpen"
      class="min-w-0"
    >
      <CollapsibleTrigger
        class="flex w-full items-center gap-1.5 rounded-md py-0.5 text-left text-xs text-muted-foreground outline-none transition-colors duration-[var(--duration-ui)] ease-[var(--ease-out-quart)] hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring/50 data-[state=open]:[&_svg.process-chevron]:rotate-180"
        :class="props.enter && 'ai-block-in'"
      >
        <ChevronDown
          class="process-chevron size-3.5 shrink-0 transition-transform duration-[var(--duration-ui)] ease-[var(--ease-out-quart)]"
        />
        <span class="min-w-0 flex-1 truncate">{{ headline }}</span>
        <AiRunStatus v-if="runningTool" :running="true" />
      </CollapsibleTrigger>
      <CollapsibleContent class="duration-[var(--duration-ui)]">
        <div class="flex min-w-0 flex-col gap-1 pt-1">
          <template v-for="(group, idx) in groups" :key="group.type + idx">
            <MarkdownProse
              v-if="group.type === 'text'"
              density="muted"
              :html="html(group.text)"
              class="min-w-0 text-xs leading-relaxed text-muted-foreground"
            />
            <AiPythonRunBlock
              v-else-if="group.type === 'python'"
              :tool="group.tool"
            />
            <TransitionGroup
              v-else
              name="ai-tool"
              tag="div"
              class="flex min-w-0 flex-col"
            >
              <AiToolStep
                v-for="(tool, ti) in group.tools"
                :key="tool.id || tool.name + ti"
                :tool="tool"
              />
            </TransitionGroup>
          </template>
        </div>
      </CollapsibleContent>
    </Collapsible>

    <template v-else>
      <MarkdownProse
        v-for="(group, idx) in groups"
        :key="'think-' + idx"
        density="muted"
        :html="html(group.text)"
        class="min-w-0 text-[13px] leading-relaxed text-muted-foreground"
        :class="props.enter && 'ai-block-in'"
      />
    </template>
  </div>
</template>
