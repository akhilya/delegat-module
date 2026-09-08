<script setup>
import { computed } from "vue";
import { ChevronDown } from "lucide-vue-next";
import { InsetGroup } from "@/components/ui/inset-group";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible";
import { useI18n } from "vue-i18n";

const props = defineProps({
  card: { type: Object, required: true },
  raw: { type: String, default: "" },
});

const { t } = useI18n();

const statusLabel = computed(() => {
  const status = props.card.status;
  if (!status) return "";
  const key = `operations.status.${status}`;
  const translated = t(key);
  return translated === key ? status : translated;
});

const metaParts = computed(() => {
  const parts = [];
  if (props.card.authorUserName) parts.push(props.card.authorUserName);
  if (props.card.connectionName) parts.push(props.card.connectionName);
  if (props.card.logCount != null) {
    parts.push(t("ai.operationContextLogs", { count: props.card.logCount }));
  }
  if (props.card.duration) parts.push(props.card.duration);
  return parts;
});
</script>

<template>
  <InsetGroup class="w-full max-w-full">
    <div class="px-2.5 py-2.5">
      <p class="text-xs text-muted-foreground">
        {{ t("ai.operationContextEyebrow") }}
      </p>
      <div class="mt-1 flex items-start justify-between gap-3">
        <h3 class="min-w-0 flex-1 text-sm leading-snug">
          {{ card.title || t("ai.operationContextUntitled") }}
        </h3>
        <span v-if="card.status" class="shrink-0 text-xs text-muted-foreground">
          {{ statusLabel }}
        </span>
      </div>
      <p
        v-if="card.type"
        class="mt-0.5 truncate font-mono text-[11px] text-muted-foreground"
      >
        {{ card.type }}
      </p>
      <p
        v-if="metaParts.length"
        class="mt-1.5 text-[11px] text-muted-foreground"
      >
        {{ metaParts.join(" · ") }}
      </p>
      <p
        v-if="card.summary"
        class="mt-1.5 line-clamp-2 text-xs leading-relaxed text-muted-foreground"
      >
        {{ card.summary }}
      </p>
    </div>
    <Collapsible v-if="raw">
      <CollapsibleTrigger
        class="flex w-full items-center justify-between gap-2 rounded-lg px-2.5 py-1.5 text-left text-xs text-muted-foreground outline-none transition-colors duration-[var(--duration-ui)] ease-[var(--ease-out-quart)] hover:bg-muted/70 focus-visible:ring-2 focus-visible:ring-ring/50 data-[state=open]:[&_svg]:rotate-180"
      >
        <span>{{ t("ai.operationContextShowDetails") }}</span>
        <ChevronDown
          class="size-3.5 shrink-0 transition-transform duration-[var(--duration-ui)] ease-[var(--ease-out-quart)]"
        />
      </CollapsibleTrigger>
      <CollapsibleContent class="duration-[var(--duration-overlay)]">
        <pre
          class="mx-1 mb-1 max-h-48 overflow-auto rounded-lg bg-muted/60 px-2.5 py-2 font-mono text-[10px] leading-relaxed text-muted-foreground whitespace-pre-wrap break-words"
          >{{ raw }}</pre>
      </CollapsibleContent>
    </Collapsible>
  </InsetGroup>
</template>
