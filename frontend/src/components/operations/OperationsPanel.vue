<script setup>
import { computed, watch } from "vue";
import { useStore } from "vuex";
import { Trash2, Activity } from "lucide-vue-next";
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
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetHeaderClose,
  SheetTitle,
} from "@/components/ui/sheet";
import {
  InsetGroupLabel,
} from "@/components/ui/inset-group";
import { EmptyState } from "@/components/ui/empty-state";
import OperationItem from "./OperationItem.vue";
import { displaySonnerSuccess, displaySonnerError } from "@/store/sonnerHelper";
import { t } from "@/i18n";
import { SHEET_WIDTH_DEFAULTS, SHEET_WIDTH_STORAGE } from "@/composables/useSheetResize";
import { useCompanionSheets } from "@/composables/useCompanionSheets";
import { OPERATION_TERMINAL_STATUSES } from "@/lib/operationsControl";

const store = useStore();
const { edgeOffset } = useCompanionSheets();
const opsEdgeOffset = edgeOffset("operations");

const panelOpen = computed(() => store.state.operations.panelOpen);
const activeOperations = computed(() => store.getters["operations/activeOperations"]);
const historyOperations = computed(() => store.getters["operations/historyOperations"]);
const hasCompleted = computed(() =>
  store.state.operations.operations.some((o) => OPERATION_TERMINAL_STATUSES.has(o.status)),
);
const historyHasMore = computed(() => store.state.operations.historyHasMore);
const historyLoaded = computed(() => store.state.operations.historyLoaded);
const activeCount = computed(() => activeOperations.value.length);
const hasAny = computed(
  () => activeOperations.value.length > 0 || historyOperations.value.length > 0,
);

watch(panelOpen, (open) => {
  if (open) {
    store.dispatch("operations/loadHistory");
  }
});

function onOpenChange(open) {
  if (open) return;
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

function handleStop(id) {
  store.dispatch("operations/stopOperation", id);
}

function handleRemove(id) {
  store.dispatch("operations/clearOperation", id);
}
</script>

<template>
  <Sheet :open="panelOpen" :modal="false" @update:open="onOpenChange">
    <SheetContent
      side="right"
      inset
      resizable
      :overlay="false"
      :show-close="false"
      class="gap-0 p-0"
      :default-width="SHEET_WIDTH_DEFAULTS.operations"
      :storage-key="SHEET_WIDTH_STORAGE.operations"
      :width="store.state.operations.panelWidth"
      :edge-offset="opsEdgeOffset"
      :aria-label="t('operations.panelTitle')"
      @update:width="(w) => store.dispatch('operations/setPanelWidth', w)"
    >
      <SheetHeader variant="bar">
        <div class="flex min-h-8 items-center gap-2">
          <SheetTitle class="min-w-0 flex-1 truncate">
            {{ t("operations.panelTitle") }}
          </SheetTitle>
          <AlertDialog v-if="hasCompleted">
            <AlertDialogTrigger as-child>
              <Button
                variant="ghost"
                size="icon"
                class="size-8 shrink-0"
                :title="t('operations.clearCompletedTitle')"
                :aria-label="t('operations.clearCompletedTitle')"
              >
                <Trash2 class="size-4" />
              </Button>
            </AlertDialogTrigger>
            <AlertDialogContent>
              <AlertDialogHeader>
                <AlertDialogTitle>
                  {{ t("operations.clearCompletedConfirmTitle") }}
                </AlertDialogTitle>
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
          <SheetHeaderClose />
        </div>
        <SheetDescription v-if="activeCount > 0">
          {{ t("operations.activeCountLabel", { count: activeCount }) }}
        </SheetDescription>
      </SheetHeader>

      <div
        v-if="hasAny"
        class="min-h-0 flex-1 overflow-y-auto px-4 py-4"
      >
        <section v-if="activeOperations.length > 0">
          <InsetGroupLabel>
            {{ t("operations.active") }}
          </InsetGroupLabel>
          <div class="flex flex-col gap-2">
            <OperationItem
              v-for="op in activeOperations"
              :key="op.id"
              :operation="op"
              @retry="handleRetry"
              @stop="handleStop"
              @remove="handleRemove"
            />
          </div>
        </section>

        <section
          v-if="historyOperations.length > 0"
          :class="activeOperations.length > 0 ? 'mt-6' : undefined"
        >
          <InsetGroupLabel>
            {{ t("operations.history") }}
          </InsetGroupLabel>
          <div class="flex flex-col gap-2">
            <OperationItem
              v-for="op in historyOperations"
              :key="op.id"
              :operation="op"
              @retry="handleRetry"
              @stop="handleStop"
              @remove="handleRemove"
            />
          </div>
          <div v-if="historyHasMore" class="flex justify-center pt-3">
            <Button
              variant="ghost"
              size="sm"
              class="text-muted-foreground"
              @click="loadMore"
            >
              {{ t("operations.loadMore") }}
            </Button>
          </div>
        </section>
      </div>

      <EmptyState
        v-else-if="!historyLoaded && activeOperations.length === 0"
        loading
        :framed="false"
        :title="t('operations.loading')"
      />

      <EmptyState
        v-else
        :framed="false"
        :title="t('operations.emptyTitle')"
        :description="t('operations.emptyDescription')"
      >
        <template #icon>
          <Activity />
        </template>
      </EmptyState>
    </SheetContent>
  </Sheet>
</template>
