<script setup>
import { computed, watch } from "vue";
import { useStore } from "vuex";
import { X, Trash2, Activity, Loader2 } from "lucide-vue-next";
import Button from "@/components/ui/button/Button.vue";
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
import OperationItem from "./OperationItem.vue";
import { displaySonnerSuccess, displaySonnerError } from "@/store/sonnerHelper";

const store = useStore();

const panelOpen = computed(() => store.state.operations.panelOpen);
const activeOperations = computed(() => store.getters["operations/activeOperations"]);
const historyOperations = computed(() => store.getters["operations/historyOperations"]);
const hasCompleted = computed(() =>
  store.state.operations.operations.some((o) => o.status !== "running"),
);
const historyHasMore = computed(() => store.state.operations.historyHasMore);
const historyLoaded = computed(() => store.state.operations.historyLoaded);

watch(panelOpen, (open) => {
  if (open) {
    store.dispatch("operations/loadHistory");
  }
});

function close() {
  store.dispatch("operations/closePanel");
}

function clearCompleted() {
  store.dispatch("operations/clearCompleted");
}

function loadMore() {
  store.dispatch("operations/loadMoreHistory");
}

async function handleRetry(id) {
  try {
    const result = await store.dispatch("operations/retryFailed", id);
    displaySonnerSuccess(result.message || "Повторная обработка запущена");
  } catch (error) {
    displaySonnerError(
      error.response?.data?.message || "Ошибка при повторе",
    );
  }
}

function handlePause(id) {
  store.dispatch("operations/pauseOperation", id);
}

function handleResume(id) {
  store.dispatch("operations/resumeOperation", id);
}

function handleStop(id) {
  store.dispatch("operations/stopOperation", id);
}

function handleRemove(id) {
  store.dispatch("operations/clearOperation", id);
}
</script>

<template>
  <Teleport to="body">
    <!-- Backdrop -->
    <Transition name="backdrop">
      <div
        v-if="panelOpen"
        class="fixed inset-0 bg-black/20 z-[9997]"
        @click="close"
      />
    </Transition>

    <!-- Panel -->
    <Transition name="panel">
      <div
        v-if="panelOpen"
        class="fixed inset-y-0 right-0 w-[680px] max-w-[95vw] z-[9998] bg-background border-l shadow-xl flex flex-col"
      >
        <!-- Header -->
        <div class="flex items-center justify-between px-4 py-3 border-b shrink-0">
          <h2 class="text-sm font-semibold">Операции</h2>
          <div class="flex items-center gap-1">
            <AlertDialog v-if="hasCompleted">
              <AlertDialogTrigger as-child>
                <Button
                  variant="ghost"
                  size="icon"
                  class="h-7 w-7"
                  title="Очистить завершённые"
                >
                  <Trash2 class="size-3.5" />
                </Button>
              </AlertDialogTrigger>
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>Очистить завершённые операции?</AlertDialogTitle>
                  <AlertDialogDescription>
                    Все завершённые, отменённые и ошибочные операции будут удалены из списка.
                  </AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>Отмена</AlertDialogCancel>
                  <AlertDialogAction @click="clearCompleted">Очистить</AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
            <Button
              variant="ghost"
              size="icon"
              class="h-7 w-7"
              @click="close"
            >
              <X class="size-4" />
            </Button>
          </div>
        </div>

        <!-- Operations list -->
        <div class="flex-1 overflow-y-auto p-3 space-y-3">
          <!-- Active operations -->
          <template v-if="activeOperations.length > 0">
            <div class="text-xs font-medium text-muted-foreground uppercase tracking-wide px-1">
              Активные
            </div>
            <div class="space-y-2">
              <OperationItem
                v-for="op in activeOperations"
                :key="op.id"
                :operation="op"
                @retry="handleRetry"
                @pause="handlePause"
                @resume="handleResume"
                @stop="handleStop"
                @remove="handleRemove"
              />
            </div>
          </template>

          <!-- History operations -->
          <template v-if="historyOperations.length > 0">
            <div class="text-xs font-medium text-muted-foreground uppercase tracking-wide px-1">
              История
            </div>
            <div class="space-y-2">
              <OperationItem
                v-for="op in historyOperations"
                :key="op.id"
                :operation="op"
                @retry="handleRetry"
                @pause="handlePause"
                @resume="handleResume"
                @stop="handleStop"
                @remove="handleRemove"
              />
            </div>
            <div v-if="historyHasMore" class="flex justify-center pt-1">
              <Button
                variant="ghost"
                size="sm"
                class="text-xs text-muted-foreground"
                @click="loadMore"
              >
                Загрузить ещё
              </Button>
            </div>
          </template>

          <!-- Loading state -->
          <div
            v-if="!historyLoaded && activeOperations.length === 0"
            class="flex flex-col items-center justify-center h-full text-muted-foreground gap-2"
          >
            <Loader2 class="size-6 animate-spin" />
            <span class="text-sm">Загрузка...</span>
          </div>

          <!-- Empty state -->
          <div
            v-else-if="activeOperations.length === 0 && historyOperations.length === 0"
            class="flex flex-col items-center justify-center h-full text-muted-foreground gap-2"
          >
            <Activity class="size-8" />
            <span class="text-sm">Нет операций</span>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.backdrop-enter-active,
.backdrop-leave-active {
  transition: opacity 0.2s ease;
}
.backdrop-enter-from,
.backdrop-leave-to {
  opacity: 0;
}

.panel-enter-active,
.panel-leave-active {
  transition: transform 0.25s ease;
}
.panel-enter-from,
.panel-leave-to {
  transform: translateX(100%);
}
</style>
