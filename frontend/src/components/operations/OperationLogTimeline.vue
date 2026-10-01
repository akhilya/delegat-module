<script setup>
import { ref, watch, nextTick } from "vue";
import { useStoreDateTime } from "@/composables/useStoreDateTime";
import { t } from "@/i18n";
import { splitOperationLog } from "@/lib/operationLogText";

const props = defineProps({
  logs: { type: Array, required: true },
});

const { formatTime: formatStoreTime, loadMemberships } = useStoreDateTime();
loadMemberships();

const container = ref(null);

watch(
  () => props.logs.length,
  () => {
    nextTick(() => {
      if (container.value) {
        container.value.scrollTop = container.value.scrollHeight;
      }
    });
  },
);

function formatTime(timestamp) {
  if (!timestamp) return "";
  const formatted = formatStoreTime(timestamp);
  return formatted === "-" ? "" : formatted;
}

function dotClass(level) {
  switch (level) {
    case "success":
      return "bg-success";
    case "error":
      return "bg-error";
    case "warning":
      return "bg-warning";
    default:
      return "bg-primary";
  }
}

function logParts(message) {
  return splitOperationLog(message);
}

function textClass(level) {
  switch (level) {
    case "error":
      return "text-error";
    case "warning":
      return "text-warning-foreground";
    default:
      return "text-foreground";
  }
}
</script>

<template>
  <div ref="container" class="max-h-72 overflow-y-auto space-y-0.5 text-xs">
    <div v-if="logs.length === 0" class="text-muted-foreground text-center py-2">
      {{ t("operations.waitingForLogs") }}
    </div>
    <div
      v-for="(log, idx) in logs"
      :key="idx"
      class="flex items-start gap-2 py-0.5 px-1"
    >
      <span class="text-muted-foreground font-mono whitespace-nowrap shrink-0 tabular-nums">
        {{ formatTime(log.timestamp) }}
      </span>
      <span
        class="mt-1.5 size-1.5 rounded-full shrink-0"
        :class="dotClass(log.level)"
      />
      <span :class="textClass(log.level)" class="min-w-0 break-words whitespace-pre-wrap">
        <template v-for="(part, partIdx) in logParts(log.message)" :key="partIdx">
          <pre
            v-if="part.type === 'json'"
            class="my-1 max-h-64 overflow-auto rounded-md bg-muted/50 p-2 font-mono text-[11px] leading-relaxed whitespace-pre text-foreground"
          >{{ part.text }}</pre>
          <RouterLink
            v-else-if="part.type === 'product'"
            :to="`/products/${part.productId}/edit`"
            class="font-medium text-foreground underline decoration-border underline-offset-2 hover:decoration-foreground"
            @click.stop
          >{{ part.text }}</RouterLink>
          <template v-else>{{ part.text }}</template>
        </template>
      </span>
    </div>
    <div v-if="logs.length >= 500" class="text-muted-foreground text-center py-1">
      {{ t("operations.maxLogEntries") }}
    </div>
  </div>
</template>
