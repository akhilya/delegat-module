<script setup>
import { ref, computed, watch, onMounted, onUnmounted } from "vue";
import { useStore } from "vuex";
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
} from "lucide-vue-next";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
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

const props = defineProps({
  operation: { type: Object, required: true },
});

const emit = defineEmits(["retry", "remove", "pause", "resume", "stop"]);

const { formatTime, loadMemberships } = useStoreDateTime();
loadMemberships();

const store = useStore();
const expanded = ref(false);
const loadingLogs = ref(false);
const now = ref(Date.now());
let timer = null;

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

onMounted(() => {
  timer = setInterval(() => { now.value = Date.now(); }, 1000);
});
onUnmounted(() => {
  if (timer) clearInterval(timer);
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
  props.operation.type !== "file-upload" && props.operation.type !== "file-delete",
);
const canStop = computed(() => isRunning.value || isPaused.value);
const canExpand = computed(() => !props.operation._fromHistory || props.operation._canExpand !== false);

function formatDuration(ms) {
  if (ms < 1000) return `${ms}мс`;
  const totalSec = Math.floor(ms / 1000);
  const min = Math.floor(totalSec / 60);
  const sec = totalSec % 60;
  if (min === 0) return `${sec}с`;
  return `${min}м ${sec}с`;
}

const elapsed = computed(() => {
  const start = new Date(props.operation.startedAt).getTime();
  const end = props.operation.finishedAt
    ? new Date(props.operation.finishedAt).getTime()
    : now.value;
  return formatDuration(Math.max(0, end - start));
});

const statusLabel = computed(() => {
  switch (props.operation.status) {
    case "running": return "выполняется";
    case "paused": return "пауза";
    case "completed": return "завершено";
    case "failed": return "ошибка";
    case "partial": return "частично";
    case "cancelled": return "остановлено";
    default: return "";
  }
});
</script>

<template>
  <Collapsible v-model:open="expanded" :disabled="!canExpand">
    <Card class="gap-0 overflow-hidden py-0">
      <CollapsibleTrigger as-child :disabled="!canExpand">
        <Button
          variant="ghost"
          class="w-full h-auto justify-start gap-2 rounded-none p-3 text-left font-normal hover:bg-muted/30"
          :class="!canExpand ? 'cursor-default hover:bg-transparent' : ''"
        >
          <component :is="typeIcon" class="size-4 text-muted-foreground shrink-0" />
          <span class="text-sm font-medium truncate flex-1">
            {{ operation.title }}
          </span>
          <span class="flex items-center gap-1 text-xs text-muted-foreground shrink-0 tabular-nums">
            <Clock class="size-3" />
            {{ elapsed }}
          </span>
          <Badge :class="statusBadgeClass">
            {{ statusLabel }}
          </Badge>
          <component
            :is="statusIcon"
            class="size-4 shrink-0"
            :class="[
              statusClass,
              operation.status === 'running' ? 'animate-spin' : '',
            ]"
          />
          <component
            v-if="canExpand"
            :is="expanded ? ChevronUp : ChevronDown"
            class="size-3.5 text-muted-foreground shrink-0"
          />
        </Button>
      </CollapsibleTrigger>

      <div v-if="!isFinished && operation.progress && operation.progress.total > 0" class="px-3 pb-1.5">
        <div class="flex items-center gap-2">
          <Progress :model-value="progressPercent" class="h-1.5 flex-1" />
          <span class="text-[10px] text-muted-foreground tabular-nums shrink-0">
            {{ Math.round(progressPercent) }}%
          </span>
        </div>
      </div>

      <CollapsibleContent>
        <div class="px-3 pb-3 space-y-2">
          <div v-if="!isFinished && operation.progress" class="grid grid-cols-4 gap-1.5 text-center text-xs">
            <div class="bg-muted/50 rounded p-1.5">
              <div class="font-medium">{{ operation.progress.total }}</div>
              <div class="text-muted-foreground">Всего</div>
            </div>
            <div class="bg-warning/10 rounded p-1.5">
              <div class="font-medium text-warning-foreground">
                {{ operation.progress.pending }}
              </div>
              <div class="text-muted-foreground">В очереди</div>
            </div>
            <div class="bg-success/10 rounded p-1.5">
              <div class="font-medium text-success-foreground">
                {{ operation.progress.completed }}
              </div>
              <div class="text-muted-foreground">Готово</div>
            </div>
            <div class="bg-error/10 rounded p-1.5">
              <div class="font-medium text-error-foreground">
                {{ operation.progress.failed }}
              </div>
              <div class="text-muted-foreground">Ошибки</div>
            </div>
          </div>

          <div v-if="loadingLogs" class="flex items-center gap-2 text-xs text-muted-foreground py-2">
            <Loader2 class="size-3 animate-spin" />
            Загрузка логов...
          </div>
          <OperationLogTimeline v-else :logs="operation.logs" />

          <div v-if="operation.authorUserName" class="text-xs text-muted-foreground">
            Запустил: {{ operation.authorUserName }}
          </div>

          <div class="flex items-center gap-1.5 pt-1.5 border-t">
            <Button
              v-if="isRunning && supportsPause"
              variant="outline"
              size="sm"
              class="h-7 text-xs"
              @click="emit('pause', operation.id)"
            >
              <Pause class="size-3 mr-1" />
              Пауза
            </Button>
            <Button
              v-if="isPaused && supportsPause"
              variant="outline"
              size="sm"
              class="h-7 text-xs"
              @click="emit('resume', operation.id)"
            >
              <Play class="size-3 mr-1" />
              Продолжить
            </Button>
            <Button
              v-if="canStop"
              variant="outline"
              size="sm"
              class="h-7 text-xs text-error hover:text-error"
              @click="emit('stop', operation.id)"
            >
              <Square class="size-3 mr-1" />
              Остановить
            </Button>
            <Button
              v-if="isFinished && operation.canRetry && operation.progress?.failed > 0"
              variant="outline"
              size="sm"
              class="h-7 text-xs"
              @click="emit('retry', operation.id)"
            >
              <RefreshCw class="size-3 mr-1" />
              Повторить ({{ operation.progress.failed }})
            </Button>
            <div class="flex-1" />
            <span class="text-[10px] text-muted-foreground">
              {{ formatTime(operation.startedAt) }}
              <template v-if="operation.finishedAt">
                → {{ formatTime(operation.finishedAt) }}
              </template>
            </span>
            <AlertDialog>
              <AlertDialogTrigger as-child>
                <Button variant="ghost" size="icon" class="h-7 w-7">
                  <X class="size-3.5" />
                </Button>
              </AlertDialogTrigger>
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>Удалить операцию?</AlertDialogTitle>
                  <AlertDialogDescription>
                    {{ operation.title }}
                  </AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>Отмена</AlertDialogCancel>
                  <AlertDialogAction @click="emit('remove', operation.id)">Удалить</AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
          </div>
        </div>
      </CollapsibleContent>
    </Card>
  </Collapsible>
</template>