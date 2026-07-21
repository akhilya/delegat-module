<script setup>
import { computed } from "vue";
import { Activity, ChevronDown } from "lucide-vue-next";
import { Badge } from "@/components/ui/badge";
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

const statusBadgeClass = computed(() => {
  switch (props.card.status) {
    case "running":
      return "bg-primary/10 text-primary border-transparent";
    case "paused":
    case "partial":
      return "bg-warning/10 text-warning border-transparent";
    case "completed":
      return "bg-success/10 text-success border-transparent";
    case "failed":
      return "bg-error/10 text-error border-transparent";
    case "cancelled":
      return "bg-muted text-muted-foreground border-transparent";
    default:
      return "bg-muted text-muted-foreground border-transparent";
  }
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
  <div
    class="w-full max-w-full overflow-hidden rounded-xl border border-border/80 bg-muted/40 px-3.5 py-3 text-left"
  >
    <div class="flex items-center gap-1.5 text-[10px] font-medium uppercase tracking-[0.08em] text-muted-foreground">
      <Activity class="size-3 shrink-0 opacity-70" />
      <span>{{ t("ai.operationContextEyebrow") }}</span>
    </div>

    <div class="mt-2 flex items-start gap-2">
      <h3 class="min-w-0 flex-1 text-sm font-medium leading-snug text-foreground">
        {{ card.title || t("ai.operationContextUntitled") }}
      </h3>
      <Badge
        v-if="card.status"
        :class="statusBadgeClass"
        class="shrink-0 text-[10px] px-1.5 py-0"
      >
        {{ statusLabel }}
      </Badge>
    </div>

    <div
      v-if="card.type"
      class="mt-0.5 truncate font-mono text-[11px] text-muted-foreground/80"
    >
      {{ card.type }}
    </div>

    <div
      v-if="metaParts.length"
      class="mt-2 flex flex-wrap items-center gap-x-1.5 gap-y-1 text-[11px] text-muted-foreground"
    >
      <template v-for="(part, i) in metaParts" :key="i">
        <span v-if="i > 0" class="text-border" aria-hidden="true">·</span>
        <span class="truncate">{{ part }}</span>
      </template>
    </div>

    <p
      v-if="card.summary"
      class="mt-2 line-clamp-2 text-xs leading-relaxed text-muted-foreground"
    >
      {{ card.summary }}
    </p>

    <details v-if="raw" class="group mt-2.5 border-t border-border/60 pt-2">
      <summary
        class="flex cursor-pointer list-none items-center gap-1 text-[11px] text-muted-foreground transition-colors hover:text-foreground [&::-webkit-details-marker]:hidden"
      >
        <ChevronDown
          class="size-3 shrink-0 transition-transform group-open:rotate-180"
        />
        <span>{{ t("ai.operationContextShowDetails") }}</span>
      </summary>
      <pre
        class="mt-2 max-h-48 overflow-auto rounded-md bg-background/80 px-2.5 py-2 font-mono text-[10px] leading-relaxed text-muted-foreground whitespace-pre-wrap break-words"
      >{{ raw }}</pre>
    </details>
  </div>
</template>
