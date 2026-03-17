<script>
// ImportProgressPanel - Floating panel that displays real-time product import progress with per-item status.
import { mapState, mapGetters, mapActions } from "vuex";
import { Loader2, CheckCircle, XCircle, Clock, X, RefreshCw, ChevronDown, ChevronUp } from "lucide-vue-next";
import Button from "@/components/ui/button/Button.vue";
import Progress from "@/components/ui/progress/Progress.vue";
import { displaySonnerSuccess, displaySonnerError } from "@/store/sonnerHelper";

export default {
  components: {
    Loader2,
    CheckCircle,
    XCircle,
    Clock,
    X,
    RefreshCw,
    ChevronDown,
    ChevronUp,
    Button,
    Progress,
  },

  data() {
    return {
      isCollapsed: false,
    };
  },

  computed: {
    ...mapState("importProgress", ["batchProgress", "batchId", "connectionId"]),
    ...mapGetters("importProgress", ["hasActiveImport", "isImportFinished", "progressPercent"]),

    pendingCount() {
      if (!this.batchProgress) return 0;
      return this.batchProgress.pending + (this.batchProgress.inProgress || 0);
    },

    currentProductIndex() {
      if (!this.batchProgress?.products) return -1;
      return this.batchProgress.products.findIndex(p => p.status === 'InProgress');
    },
  },

  watch: {
    isImportFinished(newVal, oldVal) {
      if (newVal && !oldVal && this.batchProgress) {
        displaySonnerSuccess(
          `Импорт завершён: ${this.batchProgress.completed} успешно, ${this.batchProgress.failed} ошибок`
        );
      }
    },

    currentProductIndex(newIndex) {
      if (newIndex >= 0) {
        this.$nextTick(() => {
          this.scrollToCurrentProduct();
        });
      }
    },
  },

  methods: {
    ...mapActions("importProgress", ["closeProgress", "retryFailedImports"]),

    scrollToCurrentProduct() {
      const container = this.$refs.productsList;
      const currentItem = this.$refs.productItems?.[this.currentProductIndex];
      if (container && currentItem) {
        currentItem.scrollIntoView({ behavior: 'smooth', block: 'center' });
      }
    },

    handleClose() {
      this.closeProgress();
    },

    async handleRetry() {
      try {
        const result = await this.retryFailedImports();
        displaySonnerSuccess(result.message || "Повторный импорт запущен");
      } catch (error) {
        displaySonnerError(error.response?.data?.message || "Ошибка при повторе импорта");
      }
    },

    toggleCollapse() {
      this.isCollapsed = !this.isCollapsed;
    },
  },
};
</script>

<template>
  <Teleport to="body">
    <div
      v-if="hasActiveImport"
      class="fixed top-4 right-4 w-80 bg-background border rounded-lg shadow-lg z-toast"
    >
      <div class="p-3">
        <!-- Header -->
        <div class="flex items-center justify-between mb-2">
          <button type="button" class="flex items-center gap-2 cursor-pointer" @click="toggleCollapse">
            <Loader2 v-if="!isImportFinished" class="size-4 animate-spin text-primary" />
            <CheckCircle v-else class="size-4 text-success" />
            <span class="font-medium text-sm">
              {{ isImportFinished ? 'Импорт завершён' : 'Импорт товаров...' }}
            </span>
            <ChevronUp v-if="!isCollapsed" class="size-4 text-muted-foreground" />
            <ChevronDown v-else class="size-4 text-muted-foreground" />
          </button>
          <Button variant="ghost" size="icon" class="h-7 w-7" @click="handleClose">
            <X class="size-4" />
          </Button>
        </div>

        <!-- Progress bar (always visible) -->
        <Progress :model-value="progressPercent" class="mb-2 h-2" />

        <!-- Collapsible content -->
        <div v-if="!isCollapsed">
          <!-- Stats grid -->
          <div class="grid grid-cols-4 gap-1.5 text-center text-xs mb-2">
            <div class="bg-muted/50 rounded p-1.5">
              <div class="font-medium">{{ batchProgress?.total || 0 }}</div>
              <div class="text-xs text-muted-foreground">Всего</div>
            </div>
            <div class="bg-warning/10 rounded p-1.5">
              <div class="font-medium text-warning-foreground">{{ pendingCount }}</div>
              <div class="text-xs text-muted-foreground">В очереди</div>
            </div>
            <div class="bg-success/10 rounded p-1.5">
              <div class="font-medium text-success-foreground">{{ batchProgress?.completed || 0 }}</div>
              <div class="text-xs text-muted-foreground">Готово</div>
            </div>
            <div class="bg-error/10 rounded p-1.5">
              <div class="font-medium text-error-foreground">{{ batchProgress?.failed || 0 }}</div>
              <div class="text-xs text-muted-foreground">Ошибки</div>
            </div>
          </div>

          <!-- Products list -->
          <div
            v-if="batchProgress?.products && batchProgress.products.length > 0"
            ref="productsList"
            class="space-y-0.5 max-h-32 overflow-y-auto text-xs"
          >
            <div
              v-for="product in batchProgress.products"
              :key="product.id"
              ref="productItems"
              class="flex items-center gap-1.5 py-1 px-1.5 rounded"
              :class="{
                'bg-success/10': product.status === 'Completed',
                'bg-error/10': product.status === 'Failed',
                'bg-warning/10': product.status === 'InProgress',
                'bg-muted/30': product.status === 'Pending'
              }"
            >
              <CheckCircle v-if="product.status === 'Completed'" class="size-3 text-success flex-shrink-0" />
              <XCircle v-else-if="product.status === 'Failed'" class="size-3 text-error flex-shrink-0" />
              <Loader2 v-else-if="product.status === 'InProgress'" class="size-3 animate-spin text-warning flex-shrink-0" />
              <Clock v-else class="size-3 text-muted-foreground flex-shrink-0" />
              <span class="truncate flex-1">{{ product.productName || product.externalProductId }}</span>
              <span
                v-if="product.status === 'Failed' && product.lastError"
                class="text-error truncate max-w-[80px]"
                :title="product.lastError"
              >
                {{ product.lastError }}
              </span>
            </div>
          </div>

          <!-- Retry button -->
          <div v-if="isImportFinished && batchProgress?.failed > 0" class="mt-2 pt-2 border-t">
            <Button
              variant="outline"
              size="sm"
              class="w-full h-7 text-xs"
              @click="handleRetry"
            >
              <RefreshCw class="size-3 mr-1.5" />
              Повторить неудачные ({{ batchProgress.failed }})
            </Button>
          </div>
        </div>
      </div>
    </div>
  </Teleport>
</template>
