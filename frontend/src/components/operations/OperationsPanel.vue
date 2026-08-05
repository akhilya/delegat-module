<script setup>
import { Spinner } from "@/components/ui/spinner";
import { computed, watch } from "vue";
import { useStore } from "vuex";
import {
  X,
  Trash2,
  Activity,
} from "lucide-vue-next";
import { Button } from "@/components/ui/button";
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
import { EmptyState } from "@/components/ui/empty-state";
import { displaySonnerSuccess, displaySonnerError } from "@/store/sonnerHelper";
import { t } from "@/i18n";

const store = useStore();

const panelOpen = computed(() => store.state.operations.panelOpen);
const activeOperations = computed(() => store.getters["operations/activeOperations"]);
const historyOperations = computed(() => store.getters["operations/historyOperations"]);
const hasCompleted = computed(() =>
  store.state.operations.operations.some((o) => o.status !== "running"),
);
const historyHasMore = computed(() => store.state.operations.historyHasMore);
const historyLoaded = computed(() => store.state.operations.historyLoaded);
const activeCount = computed(() => activeOperations.value.length);

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
    displaySonnerSuccess(result.message || t("operations.retryStarted"));
  } catch (error) {
    displaySonnerError(
      error.response?.data?.message || t("operations.retryError"),
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
    <Transition name="backdrop">
      <div
        v-if="panelOpen"
        class="fixed inset-0 bg-black/20 z-[9997]"
        @click="close"
      />
    </Transition>

    <Transition name="panel">
      <aside
        v-if="panelOpen"
        class="fixed inset-y-0 right-0 w-[720px] max-w-[95vw] z-[9998] bg-background border-l shadow-xl flex flex-col"
        role="complementary"
        :aria-label="t('operations.panelTitle')"
      >
        <header class="flex items-center gap-2 px-4 py-3 border-b shrink-0 bg-background">
          <div class="min-w-0 flex-1">
            <h2 class="text-sm font-semibold tracking-tight">
              {{ t("operations.panelTitle") }}
            </h2>
            <p v-if="activeCount > 0" class="text-[11px] text-muted-foreground mt-0.5">
              {{ t("operations.activeCountLabel", { count: activeCount }) }}
            </p>
          </div>
          <AlertDialog v-if="hasCompleted">
            <AlertDialogTrigger as-child>
              <Button
                variant="ghost"
                size="icon"
                class="h-8 w-8 shrink-0"
                :title="t('operations.clearCompletedTitle')"
              >
                <Trash2 class="size-3.5" />
              </Button>
            </AlertDialogTrigger>
            <AlertDialogContent>
              <AlertDialogHeader>
                <AlertDialogTitle>{{ t("operations.clearCompletedConfirmTitle") }}</AlertDialogTitle>
                <AlertDialogDescription>
                  {{ t("operations.clearCompletedConfirmDescription") }}
                </AlertDialogDescription>
              </AlertDialogHeader>
              <AlertDialogFooter>
                <AlertDialogCancel>{{ t("common.cancel") }}</AlertDialogCancel>
                <AlertDialogAction variant="destructive" @click="clearCompleted">
                  {{ t("operations.clearCompletedConfirm") }}
                </AlertDialogAction>
              </AlertDialogFooter>
            </AlertDialogContent>
          </AlertDialog>
          <Button
            variant="ghost"
            size="icon"
            class="h-8 w-8 shrink-0"
            @click="close"
          >
            <X class="size-4" />
          </Button>
        </header>

        <div class="flex min-h-0 flex-1 flex-col overflow-hidden">
          <div
            v-if="activeOperations.length > 0 || historyOperations.length > 0"
            class="min-h-0 flex-1 space-y-4 overflow-y-auto px-3 py-3"
          >
          <section v-if="activeOperations.length > 0" class="space-y-2">
            <div class="flex items-center gap-2 px-1">
              <span class="text-[10px] font-medium uppercase tracking-[0.08em] text-muted-foreground">
                {{ t("operations.active") }}
              </span>
              <span class="h-px flex-1 bg-border/80" />
              <span class="text-[10px] tabular-nums text-muted-foreground">
                {{ activeOperations.length }}
              </span>
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
          </section>

          <section v-if="historyOperations.length > 0" class="space-y-2">
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
            <div v-if="historyHasMore" class="flex justify-center pt-1">
              <Button
                variant="ghost"
                size="sm"
                class="text-xs text-muted-foreground"
                @click="loadMore"
              >
                {{ t("operations.loadMore") }}
              </Button>
            </div>
          </section>
          </div>

          <div
            v-else-if="!historyLoaded && activeOperations.length === 0"
            class="flex min-h-0 flex-1 flex-col items-center justify-center gap-2 text-muted-foreground"
          >
            <Spinner size="lg" />
            <span class="text-sm">{{ t("operations.loading") }}</span>
          </div>

          <EmptyState
            v-else
            :framed="false"
            size="sm"
            :title="t('operations.emptyTitle')"
            :description="t('operations.emptyDescription')"
          >
            <template #icon>
              <Activity />
            </template>
          </EmptyState>
        </div>
      </aside>
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

@media (prefers-reduced-motion: reduce) {
  .backdrop-enter-active,
  .backdrop-leave-active,
  .panel-enter-active,
  .panel-leave-active {
    transition-duration: 0.01ms;
  }
}
</style>
