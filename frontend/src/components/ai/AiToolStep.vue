<script setup>
import { computed } from "vue";
import {
  BookOpen,
  Boxes,
  ChevronDown,
  ClipboardList,
  Database,
  ExternalLink,
  Files,
  KeyRound,
  Search,
} from "lucide-vue-next";
import AiRunStatus from "./AiRunStatus.vue";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { sqlResultParts, stripDisplayEmoji } from "@/lib/ai/display";
import { useI18n } from "vue-i18n";

const props = defineProps({
  tool: { type: Object, required: true },
});

const { t } = useI18n();

const ICONS = {
  sql_readonly: Database,
  platform_request: Boxes,
  search_catalog: Search,
  search_knowledge: BookOpen,
  match_files: Files,
  navigate_to: ExternalLink,
  ask_form: KeyRound,
  propose_plan: ClipboardList,
};

const running = computed(() => props.tool.status === "running");
const icon = computed(() => ICONS[props.tool.name] || Boxes);
const label = computed(() => {
  const key = `ai.toolNames.${props.tool.name}`;
  const translated = t(key);
  return translated === key ? props.tool.name : translated;
});
const summary = computed(() => stripDisplayEmoji(props.tool.summary || ""));
const sql = computed(() =>
  props.tool.name === "sql_readonly" ? sqlResultParts(props.tool) : null,
);
const sqlLine = computed(() => (sql.value?.sql || "").replace(/\s+/g, " "));
const inputPreview = computed(() => {
  if (sql.value?.sql) return "";
  const raw = props.tool.input || "";
  if (!raw) return "";
  const clipped = raw.length > 400 ? `${raw.slice(0, 400)}…` : raw;
  return stripDisplayEmoji(clipped);
});
const expandable = computed(
  () =>
    !!(
      sql.value?.sql ||
      sql.value?.rows?.length ||
      inputPreview.value ||
      props.tool.result?.url
    ),
);
</script>

<template>
  <Collapsible v-if="expandable">
    <CollapsibleTrigger
      class="flex w-full items-center gap-2 rounded-md py-0.5 text-left text-xs outline-none transition-colors duration-[var(--duration-ui)] ease-[var(--ease-out-quart)] hover:bg-muted/70 focus-visible:ring-2 focus-visible:ring-ring/50 data-[state=open]:[&_svg.tool-chevron]:rotate-180"
    >
      <component :is="icon" class="size-3.5 shrink-0 text-muted-foreground" />
      <span class="min-w-0 flex-1 truncate">
        {{ label
        }}<template v-if="sqlLine">
          <span class="text-muted-foreground"> · </span>
          <span class="font-mono text-[11px] text-muted-foreground">{{
            sqlLine
          }}</span>
        </template>
      </span>
      <AiRunStatus :running="running" />
      <ChevronDown
        class="tool-chevron size-3 shrink-0 text-muted-foreground transition-transform duration-[var(--duration-ui)] ease-[var(--ease-out-quart)]"
      />
    </CollapsibleTrigger>
    <CollapsibleContent class="duration-[var(--duration-ui)]">
      <div class="pb-1 pl-5">
        <pre
          v-if="sql?.sql"
          class="mb-1 max-h-28 overflow-auto rounded-md bg-muted/60 px-2 py-1.5 font-mono text-[11px] leading-relaxed text-foreground/90 whitespace-pre-wrap break-all"
          >{{ sql.sql }}</pre>
        <p
          v-if="summary"
          class="mb-1 truncate text-[11px] text-muted-foreground"
        >
          {{ summary }}
        </p>
        <div
          v-if="sql?.rows?.length"
          class="max-h-32 overflow-auto rounded-md bg-muted/60"
        >
          <Table
            :contain="false"
            class="w-max min-w-full border-collapse text-[11px]"
          >
            <TableHeader>
              <TableRow class="border-0 transition-none hover:bg-transparent">
                <TableHead
                  v-for="col in sql.columns"
                  :key="col"
                  class="h-auto px-2 py-1 text-left text-[11px] font-medium text-muted-foreground"
                >
                  {{ col }}
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow
                v-for="(row, ri) in sql.rows"
                :key="ri"
                class="border-0 transition-none hover:bg-transparent"
              >
                <TableCell
                  v-for="col in sql.columns"
                  :key="col"
                  class="whitespace-nowrap px-2 py-0.5 text-[11px] text-foreground/90"
                >
                  {{ row[col] }}
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </div>
        <pre
          v-else-if="inputPreview"
          class="max-h-24 overflow-auto rounded-md bg-muted/60 px-2 py-1.5 font-mono text-[11px] leading-relaxed text-muted-foreground whitespace-pre-wrap break-all"
          >{{ inputPreview }}</pre>
        <RouterLink
          v-if="tool.result?.url"
          :to="tool.result.url"
          class="mt-1 inline-flex items-center gap-1 text-[11px] text-foreground underline-offset-4 hover:underline"
        >
          <ExternalLink class="size-3" />
          {{ tool.result.label || tool.result.url }}
        </RouterLink>
      </div>
    </CollapsibleContent>
  </Collapsible>
  <div v-else class="flex items-center gap-2 py-0.5 text-xs">
    <component :is="icon" class="size-3.5 shrink-0 text-muted-foreground" />
    <span class="min-w-0 flex-1 truncate">
      {{ label
      }}<template v-if="sqlLine">
        <span class="text-muted-foreground"> · </span>
        <span class="font-mono text-[11px] text-muted-foreground">{{
          sqlLine
        }}</span>
      </template>
    </span>
    <AiRunStatus :running="running" />
  </div>
</template>
