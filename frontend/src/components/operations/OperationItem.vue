<script setup>
import { ref, computed, watch, onMounted, onUnmounted } from "vue";
import { useStore } from "vuex";
import { useSharedNow } from "@/composables/useSharedNow";
import {
  Upload,
  Download,
  DollarSign,
  Package,
  FileSpreadsheet,
  Loader2,
  CheckCircle,
  XCircle,
  AlertTriangle,
  ChevronDown,
  ChevronUp,
  RefreshCw,
  X,
  Clock,
  Pause,
  Play,
  Square,
  Ban,
  ImageDown,
  ArrowRightLeft,
  Trash2,
  Sparkles,
  User,
  Cable,
  Copy,
  Check,
} from "lucide-vue-next";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible";
import Progress from "@/components/ui/progress/Progress.vue";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/components/ui/alert-dialog";
import OperationLogTimeline from "./OperationLogTimeline.vue";
import { useStoreDateTime } from "@/composables/useStoreDateTime";
import { parseServerUtc } from "@/utils/dateTime";
import { t } from "@/i18n";
import { http, loadMe } from "@/main";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { hasPermission, PERMS } from "@/lib/permissions";

const props = defineProps({
  operation: { type: Object, required: true },
});

const emit = defineEmits(["retry", "remove", "pause", "resume", "stop"]);

const { formatTime, loadMemberships } = useStoreDateTime();
loadMemberships();

const store = useStore();
const expanded = ref(false);
const loadingLogs = ref(false);
const downloadingArtifact = ref(false);
const copyingLogs = ref(false);
const logsCopied = ref(false);
const explainingWithAi = ref(false);
const aiAvailable = ref(false);
const now = useSharedNow();
let copyTimer = null;

watch(expanded, async (isExpanded) => {
  if (isExpanded && props.operation._fromHistory && !props.operation._logsLoaded) {
    loadingLogs.value = true;
    const safety = setTimeout(() => { loadingLogs.value = false; }, 15000);
    try {
      await store.dispatch("operations/loadOperationLogs", props.operation.id);
    } finally {
      clearTimeout(safety);
      loadingLogs.value = false;
    }
  }
});

onMounted(async () => {
  try {
    const me = await loadMe();
    aiAvailable.value = me?.aiEnabled === true && hasPermission(me, PERMS.AiUse);
    if (aiAvailable.value) {
      store.dispatch("aiModels/getPickerModels", { onError: () => {} });
    }
  } catch {
    aiAvailable.value = false;
  }
});
onUnmounted(() => {
  if (copyTimer) clearTimeout(copyTimer);
});

const typeIcon = computed(() => {
  switch (props.operation.type) {
    case "batch-export":
    case "single-export":
      return Upload;
    case "batch-import":
    case "single-import":
      return Download;
    case "price-export":
    case "price-import":
      return DollarSign;
    case "stock-export":
      return Package;
    case "excel-import":
      return FileSpreadsheet;
    case "image-download":
      return ImageDown;
    case "product-transfer":
      return ArrowRightLeft;
    case "file-upload":
      return Upload;
    case "file-delete":
      return Trash2;
    case "folder-archive":
      return Download;
    case "folder-delete":
      return Trash2;
    case "ai-mapping":
      return Sparkles;
    default:
      return Upload;
  }
});

const statusIcon = computed(() => {
  switch (props.operation.status) {
    case "running":
      return Loader2;
    case "paused":
      return Pause;
    case "completed":
      return CheckCircle;
    case "failed":
      return XCircle;
    case "partial":
      return AlertTriangle;
    case "cancelled":
      return Ban;
    default:
      return Loader2;
  }
});

const statusClass = computed(() => {
  switch (props.operation.status) {
    case "running":
      return "text-primary";
    case "paused":
      return "text-warning";
    case "completed":
      return "text-success";
    case "failed":
      return "text-error";
    case "partial":
      return "text-warning";
    case "cancelled":
      return "text-muted-foreground";
    default:
      return "text-muted-foreground";
  }
});

const statusBadgeClass = computed(() => {
  switch (props.operation.status) {
    case "running":
      return "bg-primary/10 text-primary border-transparent text-[10px] px-1.5 py-0";
    case "paused":
    case "partial":
      return "bg-warning/10 text-warning border-transparent text-[10px] px-1.5 py-0";
    case "completed":
      return "bg-success/10 text-success border-transparent text-[10px] px-1.5 py-0";
    case "failed":
      return "bg-error/10 text-error border-transparent text-[10px] px-1.5 py-0";
    default:
      return "bg-muted text-muted-foreground border-transparent text-[10px] px-1.5 py-0";
  }
});

const progressPercent = computed(() => {
  const p = props.operation.progress;
  if (!p || p.total === 0) return 0;
  return ((p.completed + p.failed) / p.total) * 100;
});

const isFinished = computed(() =>
  ["completed", "failed", "partial", "cancelled"].includes(props.operation.status),
);

const isRunning = computed(() => props.operation.status === "running");
const isPaused = computed(() => props.operation.status === "paused");
const supportsPause = computed(() =>
  !["file-upload", "file-delete", "folder-archive", "folder-delete"].includes(props.operation.type),
);
const canStop = computed(() => isRunning.value || isPaused.value);
const canExpand = computed(() => true);

function formatDuration(ms) {
  if (ms < 1000) return t("operations.durationMs", { ms });
  const totalSec = Math.floor(ms / 1000);
  const min = Math.floor(totalSec / 60);
  const sec = totalSec % 60;
  if (min === 0) return t("operations.durationSec", { sec });
  return t("operations.durationMinSec", { min, sec });
}

const elapsed = computed(() => {
  const start = parseServerUtc(props.operation.startedAt)?.getTime();
  if (start == null) return formatDuration(0);
  const end = props.operation.finishedAt
    ? parseServerUtc(props.operation.finishedAt)?.getTime()
    : now.value;
  if (end == null) return formatDuration(0);
  return formatDuration(Math.max(0, end - start));
});

const statusLabel = computed(() => {
  const status = props.operation.status;
  if (!["running", "paused", "completed", "failed", "partial", "cancelled"].includes(status)) return "";
  return t(`operations.status.${status}`);
});

const canDownloadArtifact = computed(() =>
  props.operation.type === "folder-archive" && props.operation.status === "completed",
);

const hasLogs = computed(() => (props.operation.logs?.length || 0) > 0);
const logsLoaded = computed(() => !props.operation._fromHistory || props.operation._logsLoaded);
const showEmptyLogs = computed(() =>
  expanded.value && logsLoaded.value && !loadingLogs.value && !hasLogs.value,
);

const hasAiModels = computed(() => {
  const picker = store.state.aiModels?.pickerModels ?? [];
  const all = store.state.aiModels?.models ?? [];
  const src = picker.length ? picker : all;
  return src.some((m) => m.isEnabled !== false && (m.id ?? m.Id) != null);
});

const showExplainAi = computed(() => aiAvailable.value);

const MAX_LOG_CHARS = 48000;

async function ensureLogsLoaded() {
  if (!props.operation._fromHistory || props.operation._logsLoaded) return;
  loadingLogs.value = true;
  try {
    await store.dispatch("operations/loadOperationLogs", props.operation.id);
  } finally {
    loadingLogs.value = false;
  }
}

function buildOperationContextText() {
  const op = props.operation;
  const lines = [
    "=== OPERATION ===",
    `Id: ${op.id}`,
    `Type: ${op.type || "-"}`,
    `Title: ${op.title || "-"}`,
    `Status: ${op.status || "-"}`,
    `Author: ${op.authorUserName || "-"}`,
    `AuthorUserId: ${op.authorUserId ?? "-"}`,
    `Connection: ${op.connectionName || "-"}`,
    `ConnectionId: ${op.connectionId ?? "-"}`,
    `BatchId: ${op.batchId || "-"}`,
    `BatchType: ${op.batchType || "-"}`,
    `StartedAt: ${op.startedAt || "-"}`,
    `FinishedAt: ${op.finishedAt || "-"}`,
    `Duration: ${elapsed.value}`,
  ];

  if (op.progress) {
    lines.push(
      `Progress: total=${op.progress.total ?? 0}, completed=${op.progress.completed ?? 0}, failed=${op.progress.failed ?? 0}, pending=${op.progress.pending ?? 0}`,
    );
  }
  if (op.summary) {
    lines.push(`Summary: ${op.summary}`);
  }

  lines.push("", "=== LOGS ===");
  const logs = op.logs || [];
  if (logs.length === 0) {
    lines.push("(no log entries available - may have been cleaned up)");
  } else {
    for (const log of logs) {
      const ts = log.timestamp ? formatTime(log.timestamp) : "";
      lines.push(`[${ts}] [${log.level || "info"}] ${log.message || ""}`);
    }
  }

  let text = lines.join("\n");
  if (text.length > MAX_LOG_CHARS) {
    text = `${text.slice(0, MAX_LOG_CHARS)}\n…(truncated)`;
  }
  return text;
}

async function copyLogs() {
  if (copyingLogs.value) return;
  copyingLogs.value = true;
  try {
    await ensureLogsLoaded();
    await navigator.clipboard.writeText(buildOperationContextText());
    logsCopied.value = true;
    displaySonnerSuccess(t("operations.logsCopied"));
    if (copyTimer) clearTimeout(copyTimer);
    copyTimer = setTimeout(() => { logsCopied.value = false; }, 2000);
  } catch (error) {
    displaySonnerError(error);
  } finally {
    copyingLogs.value = false;
  }
}

async function explainWithAi() {
  if (explainingWithAi.value || !showExplainAi.value) return;
  explainingWithAi.value = true;
  try {
    await ensureLogsLoaded();

    if (!hasAiModels.value) {
      await store.dispatch("aiModels/getPickerModels", { onError: () => {} });
    }
    if (!hasAiModels.value) {
      displaySonnerError(t("operations.aiNoModels"));
      return;
    }

    const prompt = `${t("operations.aiExplainPrompt")}\n\n${buildOperationContextText()}`;

    store.commit("aiChat/setPageContext", {
      page: "operation",
      operationId: props.operation.id,
      operationTitle: props.operation.title,
      operationType: props.operation.type,
      connectionId: props.operation.connectionId ?? null,
      connectionName: props.operation.connectionName ?? null,
    });

    store.dispatch("operations/closePanel");
    store.commit("aiChat/setOpen", true);
    await store.dispatch("aiChat/startNewConversation");
    await store.dispatch("aiChat/sendMessage", {
      content: prompt,
      contextCard: {
        kind: "operation",
        title: props.operation.title || props.operation.type || "",
        type: props.operation.type || "",
        status: props.operation.status || "",
        authorUserName: props.operation.authorUserName || null,
        connectionName: props.operation.connectionName || null,
        logCount: (props.operation.logs || []).length,
        summary: props.operation.summary || null,
        duration: elapsed.value,
      },
    });
  } catch (error) {
    displaySonnerError(error);
  } finally {
    explainingWithAi.value = false;
  }
}

async function downloadArtifact() {
  if (downloadingArtifact.value || !canDownloadArtifact.value) return;
  downloadingArtifact.value = true;
  try {
    const response = await http({
      url: `/operations/${props.operation.id}/artifact`,
      method: "GET",
      responseType: "blob",
    });
    const disposition = response.headers?.["content-disposition"] || "";
    const match = disposition.match(/filename\*=UTF-8''(.+)/);
    const filename = match ? decodeURIComponent(match[1]) : "archive.zip";
    const url = URL.createObjectURL(response.data);
    const a = document.createElement("a");
    a.href = url;
    a.download = filename;
    a.click();
    URL.revokeObjectURL(url);
  } catch (error) {
    displaySonnerError(error);
  } finally {
    downloadingArtifact.value = false;
  }
}
</script>

<template>
  <Collapsible v-model:open="expanded" :disabled="!canExpand">
    <div class="overflow-hidden rounded-lg border bg-card">
      <CollapsibleTrigger as-child :disabled="!canExpand">
        <button
          type="button"
          class="w-full px-3 py-2.5 text-left hover:bg-muted/40 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-inset"
        >
          <div class="flex items-start gap-2.5">
            <component
              :is="typeIcon"
              class="size-4 text-muted-foreground shrink-0 mt-0.5"
            />
            <div class="min-w-0 flex-1 space-y-1">
              <div class="flex items-center gap-2">
                <span class="text-sm font-medium truncate flex-1">
                  {{ operation.title }}
                </span>
                <Badge :class="statusBadgeClass" class="shrink-0">
                  {{ statusLabel }}
                </Badge>
                <component
                  :is="statusIcon"
                  class="size-3.5 shrink-0"
                  :class="[
                    statusClass,
                    operation.status === 'running' ? 'animate-spin' : '',
                  ]"
                />
                <component
                  :is="expanded ? ChevronUp : ChevronDown"
                  class="size-3.5 text-muted-foreground shrink-0"
                />
              </div>

              <!-- Always-visible identity / timing meta (independent of logs) -->
              <div class="flex flex-wrap items-center gap-x-2.5 gap-y-0.5 text-[11px] text-muted-foreground">
                <span
                  v-if="operation.authorUserName"
                  class="inline-flex items-center gap-1 min-w-0 max-w-full"
                >
                  <User class="size-3 shrink-0 opacity-70" />
                  <span class="truncate">{{ operation.authorUserName }}</span>
                </span>
                <span
                  v-if="operation.connectionName"
                  class="inline-flex items-center gap-1 min-w-0 max-w-[40%]"
                >
                  <Cable class="size-3 shrink-0 opacity-70" />
                  <span class="truncate">{{ operation.connectionName }}</span>
                </span>
                <span class="inline-flex items-center gap-1 tabular-nums shrink-0">
                  <Clock class="size-3 shrink-0 opacity-70" />
                  {{ elapsed }}
                </span>
                <span class="tabular-nums shrink-0 opacity-80">
                  {{ formatTime(operation.startedAt) }}
                  <template v-if="operation.finishedAt">
                    → {{ formatTime(operation.finishedAt) }}
                  </template>
                </span>
              </div>

              <p
                v-if="isFinished && operation.summary"
                class="text-[11px] text-muted-foreground/90 line-clamp-2 leading-snug"
              >
                {{ operation.summary }}
              </p>
            </div>
          </div>
        </button>
      </CollapsibleTrigger>

      <div
        v-if="!isFinished && operation.progress && operation.progress.total > 0"
        class="px-3 pb-2"
      >
        <div class="flex items-center gap-2">
          <Progress :model-value="progressPercent" class="h-1 flex-1" />
          <span class="text-[10px] text-muted-foreground tabular-nums shrink-0">
            {{ Math.round(progressPercent) }}%
          </span>
        </div>
      </div>

      <CollapsibleContent>
        <div class="px-3 pb-3 space-y-2.5 border-t border-border/60 pt-2.5">
          <div
            v-if="!isFinished && operation.progress"
            class="grid grid-cols-4 gap-1.5 text-center text-[11px]"
          >
            <div class="bg-muted/50 rounded-md px-1.5 py-1.5">
              <div class="font-medium tabular-nums">{{ operation.progress.total }}</div>
              <div class="text-muted-foreground">{{ t("operations.total") }}</div>
            </div>
            <div class="bg-warning/10 rounded-md px-1.5 py-1.5">
              <div class="font-medium text-warning-foreground tabular-nums">
                {{ operation.progress.pending }}
              </div>
              <div class="text-muted-foreground">{{ t("operations.pending") }}</div>
            </div>
            <div class="bg-success/10 rounded-md px-1.5 py-1.5">
              <div class="font-medium text-success-foreground tabular-nums">
                {{ operation.progress.completed }}
              </div>
              <div class="text-muted-foreground">{{ t("operations.completed") }}</div>
            </div>
            <div class="bg-error/10 rounded-md px-1.5 py-1.5">
              <div class="font-medium text-error-foreground tabular-nums">
                {{ operation.progress.failed }}
              </div>
              <div class="text-muted-foreground">{{ t("operations.errors") }}</div>
            </div>
          </div>

          <div v-if="loadingLogs" class="flex items-center gap-2 text-xs text-muted-foreground py-2">
            <Loader2 class="size-3 animate-spin" />
            {{ t("operations.loadingLogs") }}
          </div>
          <OperationLogTimeline v-else-if="hasLogs" :logs="operation.logs" />
          <div
            v-else-if="showEmptyLogs"
            class="rounded-md border border-dashed bg-muted/20 px-3 py-2.5 text-xs text-muted-foreground"
          >
            <p class="font-medium text-foreground/80">{{ t("operations.logsUnavailableTitle") }}</p>
            <p class="mt-0.5 leading-snug">{{ t("operations.logsUnavailableDescription") }}</p>
          </div>

          <div class="flex flex-wrap items-center gap-1.5 pt-1">
            <Button
              v-if="isRunning && supportsPause"
              variant="outline"
              size="sm"
              class="h-7 text-xs"
              @click.stop="emit('pause', operation.id)"
            >
              <Pause class="size-3 mr-1" />
              {{ t("operations.pause") }}
            </Button>
            <Button
              v-if="isPaused && supportsPause"
              variant="outline"
              size="sm"
              class="h-7 text-xs"
              @click.stop="emit('resume', operation.id)"
            >
              <Play class="size-3 mr-1" />
              {{ t("operations.resume") }}
            </Button>
            <Button
              v-if="canStop"
              variant="outline"
              size="sm"
              class="h-7 text-xs text-error hover:text-error"
              @click.stop="emit('stop', operation.id)"
            >
              <Square class="size-3 mr-1" />
              {{ t("operations.stop") }}
            </Button>
            <Button
              v-if="isFinished && operation.canRetry && operation.progress?.failed > 0"
              variant="outline"
              size="sm"
              class="h-7 text-xs"
              @click.stop="emit('retry', operation.id)"
            >
              <RefreshCw class="size-3 mr-1" />
              {{ t("operations.retry", { count: operation.progress.failed }) }}
            </Button>
            <Button
              v-if="canDownloadArtifact"
              variant="outline"
              size="sm"
              class="h-7 text-xs"
              :disabled="downloadingArtifact"
              @click.stop="downloadArtifact"
            >
              <Download class="size-3 mr-1" :class="downloadingArtifact ? 'animate-pulse' : ''" />
              {{ t("operations.downloadArtifact") }}
            </Button>
            <Button
              variant="outline"
              size="sm"
              class="h-7 text-xs"
              :disabled="copyingLogs"
              @click.stop="copyLogs"
            >
              <Check v-if="logsCopied" class="size-3 mr-1 text-success" />
              <Copy v-else class="size-3 mr-1" />
              {{ logsCopied ? t("operations.logsCopiedShort") : t("operations.copyLogs") }}
            </Button>
            <Button
              v-if="showExplainAi"
              variant="outline"
              size="sm"
              class="h-7 text-xs"
              :disabled="explainingWithAi || store.state.aiChat.isLoading"
              @click.stop="explainWithAi"
            >
              <Loader2 v-if="explainingWithAi" class="size-3 mr-1 animate-spin" />
              <Sparkles v-else class="size-3 mr-1" />
              {{ t("operations.explainWithAi") }}
            </Button>
            <div class="flex-1" />
            <AlertDialog>
              <AlertDialogTrigger as-child>
                <Button variant="ghost" size="icon" class="h-7 w-7" @click.stop>
                  <X class="size-3.5" />
                </Button>
              </AlertDialogTrigger>
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>{{ t("operations.deleteTitle") }}</AlertDialogTitle>
                  <AlertDialogDescription>
                    {{ operation.title }}
                  </AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>{{ t("common.cancel") }}</AlertDialogCancel>
                  <AlertDialogAction @click="emit('remove', operation.id)">
                    {{ t("operations.deleteConfirm") }}
                  </AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
          </div>
        </div>
      </CollapsibleContent>
    </div>
  </Collapsible>
</template>
