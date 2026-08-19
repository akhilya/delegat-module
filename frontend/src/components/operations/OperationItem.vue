<script setup>
import { Spinner } from "@/components/ui/spinner";
import { ref, computed, watch, onMounted, onUnmounted } from "vue";
import { useStore } from "vuex";
import { useSharedNow } from "@/composables/useSharedNow";
import {
  Upload,
  Download,
  DollarSign,
  Package,
  FileSpreadsheet,
  CheckCircle,
  XCircle,
  AlertTriangle,
  RefreshCw,
  Ban,
  ImageDown,
  ArrowRightLeft,
  Trash2,
  Sparkles,
  Pencil,
  Cable,
  Wand2,
  Search,
  Copy,
  Check,
  FolderTree,
} from "lucide-vue-next";
import { Button } from "@/components/ui/button";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible";
import {
  InsetGroup,
  InsetGroupItem,
} from "@/components/ui/inset-group";
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
import { artifactDownloadUrl, triggerBrowserDownload } from "@/lib/artifactDownload";
import { hasPermission, PERMS } from "@/lib/permissions";
import { canStopOperation } from "@/lib/operationsControl";

const props = defineProps({
  operation: { type: Object, required: true },
});

const emit = defineEmits(["retry", "remove", "stop"]);

const { formatTime, loadMemberships } = useStoreDateTime();
loadMemberships();

const store = useStore();
const expanded = ref(false);
const loadingLogs = ref(false);
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
    case "product-bulk-update":
      return Pencil;
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
    case "catalog-match":
      return Cable;
    case "catalog-scrape":
      return Search;
    case "category-import":
      return FolderTree;
    case "tool":
      return Wand2;
    default:
      return Upload;
  }
});

const finishedStatusIcon = computed(() => {
  switch (props.operation.status) {
    case "completed":
      return CheckCircle;
    case "failed":
      return XCircle;
    case "partial":
      return AlertTriangle;
    case "cancelled":
      return Ban;
    default:
      return null;
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

const progressPercent = computed(() => {
  const p = props.operation.progress;
  if (!p || p.total === 0) return 0;
  return ((p.completed + p.failed) / p.total) * 100;
});

const isFinished = computed(() =>
  ["completed", "failed", "partial", "cancelled"].includes(props.operation.status),
);

const isRunning = computed(() => props.operation.status === "running");

const canStop = computed(() =>
  canStopOperation(props.operation.type, props.operation.status),
);

const showProgress = computed(() =>
  !isFinished.value
  && props.operation.progress
  && props.operation.progress.total > 0,
);

const canRetry = computed(() =>
  isFinished.value
  && props.operation.canRetry
  && (props.operation.progress?.failed ?? 0) > 0,
);

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

const subtitleRest = computed(() => {
  const parts = [];
  if (props.operation.authorUserName) parts.push(props.operation.authorUserName);
  if (props.operation.connectionName) parts.push(props.operation.connectionName);
  parts.push(elapsed.value);
  const p = props.operation.progress;
  if (p && p.total > 0) {
    parts.push(`${(p.completed || 0) + (p.failed || 0)}/${p.total}`);
  }
  return parts;
});

const canDownloadArtifact = computed(() =>
  props.operation.type === "folder-archive" && props.operation.status === "completed",
);

const hasLogs = computed(() => (props.operation.logs?.length || 0) > 0);
const logsLoaded = computed(() => !props.operation._fromHistory || props.operation._logsLoaded);
const showEmptyLogs = computed(() =>
  expanded.value && logsLoaded.value && !loadingLogs.value && !hasLogs.value,
);
const emptyLogsTitle = computed(() =>
  isFinished.value
    ? t("operations.logsUnavailableTitle")
    : t("operations.logsEmptyRunningTitle"),
);
const emptyLogsDescription = computed(() =>
  isFinished.value
    ? t("operations.logsUnavailableDescription")
    : t("operations.logsEmptyRunningDescription"),
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

function downloadArtifact() {
  if (!canDownloadArtifact.value) return;
  triggerBrowserDownload(
    artifactDownloadUrl(http.defaults.baseURL, props.operation.id),
  );
}

function onStop() {
  emit("stop", props.operation.id);
}
</script>

<template>
  <InsetGroup>
    <Collapsible v-model:open="expanded" class="w-full">
      <CollapsibleTrigger as-child>
        <InsetGroupItem>
          <template #leading>
            <component :is="typeIcon" class="size-4 text-muted-foreground" />
          </template>
          <p class="truncate">{{ operation.title }}</p>
          <p
            v-if="subtitleRest.length"
            class="mt-0.5 truncate text-xs font-normal text-muted-foreground"
          >
            <template v-for="(part, idx) in subtitleRest" :key="idx">
              <span v-if="idx > 0" class="text-muted-foreground/50"> · </span>
              <span class="tabular-nums">{{ part }}</span>
            </template>
          </p>
          <p
            v-if="isFinished && operation.summary"
            class="mt-0.5 line-clamp-2 text-xs font-normal leading-snug text-muted-foreground"
          >
            {{ operation.summary }}
          </p>
          <div
            v-if="showProgress"
            class="mt-1.5 flex items-center gap-2"
          >
            <Progress :model-value="progressPercent" class="h-1 flex-1" />
            <span class="shrink-0 text-xs tabular-nums text-muted-foreground">
              {{ Math.round(progressPercent) }}%
            </span>
          </div>
          <template #trailing>
            <div class="flex items-center gap-1" @click.stop>
              <Button
                v-if="canStop"
                variant="ghost"
                size="sm"
                class="h-8 px-2 text-destructive hover:bg-destructive/10 hover:text-destructive"
                @click="onStop"
              >
                {{ t("operations.stop") }}
              </Button>
              <Spinner
                v-if="isRunning"
                size="xs"
                class="text-primary"
                :label="statusLabel"
                :title="statusLabel"
              />
              <component
                :is="finishedStatusIcon"
                v-else-if="finishedStatusIcon"
                class="size-4"
                :class="statusClass"
                :title="statusLabel"
                :aria-label="statusLabel"
              />
            </div>
          </template>
        </InsetGroupItem>
      </CollapsibleTrigger>

      <CollapsibleContent>
        <div class="border-t border-border/60 px-1 pb-1 pt-1.5">
          <div
            v-if="loadingLogs"
            class="flex items-center gap-2 px-2 py-1.5 text-xs text-muted-foreground"
          >
            <Spinner size="xs" />
            {{ t("operations.loadingLogs") }}
          </div>
          <OperationLogTimeline v-else-if="hasLogs" :logs="operation.logs" />
          <div
            v-else-if="showEmptyLogs"
            class="px-2 py-1 text-xs text-muted-foreground"
          >
            <p class="font-medium text-foreground">{{ emptyLogsTitle }}</p>
            <p class="mt-0.5 leading-snug">{{ emptyLogsDescription }}</p>
          </div>

          <div class="mt-1 flex flex-col">
            <Button
              variant="ghost"
              size="xs"
              class="h-7 w-full justify-start text-muted-foreground"
              :disabled="copyingLogs"
              @click="copyLogs"
            >
              <Check v-if="logsCopied" class="size-3.5 text-success" />
              <Copy v-else class="size-3.5" />
              {{ logsCopied ? t("operations.logsCopiedShort") : t("operations.copyLogs") }}
            </Button>
            <Button
              v-if="showExplainAi"
              variant="ghost"
              size="xs"
              class="h-7 w-full justify-start text-muted-foreground"
              :disabled="explainingWithAi || store.state.aiChat.isLoading"
              @click="explainWithAi"
            >
              <Spinner v-if="explainingWithAi" size="xs" />
              <Sparkles v-else class="size-3.5" />
              {{ t("operations.explainWithAi") }}
            </Button>
            <Button
              v-if="canRetry"
              variant="ghost"
              size="xs"
              class="h-7 w-full justify-start text-muted-foreground"
              @click="emit('retry', operation.id)"
            >
              <RefreshCw class="size-3.5" />
              {{ t("operations.retry", { count: operation.progress.failed }) }}
            </Button>
            <Button
              v-if="canDownloadArtifact"
              variant="ghost"
              size="xs"
              class="h-7 w-full justify-start text-muted-foreground"
              @click="downloadArtifact"
            >
              <Download class="size-3.5" />
              {{ t("operations.downloadArtifact") }}
            </Button>
            <AlertDialog>
              <AlertDialogTrigger as-child>
                <Button
                  variant="ghost"
                  size="xs"
                  class="h-7 w-full justify-start text-destructive hover:bg-destructive/10 hover:text-destructive"
                >
                  <Trash2 class="size-3.5" />
                  {{ t("operations.deleteConfirm") }}
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
                  <AlertDialogAction variant="destructive" @click="emit('remove', operation.id)">
                    {{ t("operations.deleteConfirm") }}
                  </AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
          </div>
        </div>
      </CollapsibleContent>
    </Collapsible>
  </InsetGroup>
</template>
