<script setup>
import { ref, watch, computed } from "vue";
import { http } from "@/main";
import { t } from "@/i18n";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { Button } from "@/components/ui/button";
import {
  Dialog, DialogContent, DialogHeader, DialogTitle,
  DialogDescription, DialogFooter,
} from "@/components/ui/dialog";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select";
import Label from "@/components/ui/label/Label.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import { Loader2, ArrowUpToLine, ArrowDownToLine, DollarSign, Package } from "lucide-vue-next";

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  selectedProducts: { type: Array, default: () => [] },
});
const emit = defineEmits(["update:modelValue"]);

// All possible actions — filtered by adapter capabilities at runtime
const ALL_ACTIONS = [
  {
    key: "export",
    label: t("products.bulkAdapterDialog.actions.export.label"),
    description: t("products.bulkAdapterDialog.actions.export.description"),
    capabilityKey: "exportProductBulk",
    needsLinks: false,
    getEndpoint: (connId) => `/marketplace/connections/${connId}/products/export-batch`,
    getBody: (productIds, _linkIds) => ({ productIds }),
  },
  {
    key: "import",
    label: t("products.bulkAdapterDialog.actions.import.label"),
    description: t("products.bulkAdapterDialog.actions.import.description"),
    capabilityKey: "importProduct",
    needsLinks: true,
    getEndpoint: (_connId) => `/marketplace/links/import-bulk`,
    getBody: (_productIds, linkIds) => ({ linkIds }),
  },
  {
    key: "exportPrices",
    label: t("products.bulkAdapterDialog.actions.exportPrices.label"),
    description: t("products.bulkAdapterDialog.actions.exportPrices.description"),
    capabilityKey: "exportPrices",
    needsLinks: true,
    getEndpoint: (connId) => `/marketplace/connections/${connId}/prices/export`,
    getBody: (_productIds, linkIds) => ({ linkIds }),
  },
  {
    key: "exportStocks",
    label: t("products.bulkAdapterDialog.actions.exportStocks.label"),
    description: t("products.bulkAdapterDialog.actions.exportStocks.description"),
    capabilityKey: "exportStocks",
    needsLinks: true,
    getEndpoint: (connId) => `/marketplace/connections/${connId}/stocks/export`,
    getBody: (_productIds, linkIds) => ({ linkIds }),
  },
  {
    key: "importPrices",
    label: t("products.bulkAdapterDialog.actions.importPrices.label"),
    description: t("products.bulkAdapterDialog.actions.importPrices.description"),
    capabilityKey: "importPrices",
    needsLinks: true,
    getEndpoint: (connId) => `/marketplace/connections/${connId}/prices/import`,
    getBody: (_productIds, linkIds) => ({ linkIds }),
  },
];

const connections = ref([]);
const isLoadingConnections = ref(false);
const selectedConnectionId = ref(null);

const capabilities = ref(null);
const isLoadingCapabilities = ref(false);

const selectedAction = ref(null);

const isResolving = ref(false);
const resolvedLinked = ref([]);
const resolvedUnlinked = ref([]);

const isRunning = ref(false);

const selectedProductIds = computed(() => props.selectedProducts.map((p) => p.id));

const availableActions = computed(() => {
  if (!capabilities.value) return [];
  return ALL_ACTIONS.filter((a) => capabilities.value[a.capabilityKey]);
});

watch(
  () => props.modelValue,
  async (open) => {
    if (!open) return;
    selectedConnectionId.value = null;
    capabilities.value = null;
    selectedAction.value = null;
    resolvedLinked.value = [];
    resolvedUnlinked.value = [];
    await loadConnections();
  }
);

watch(selectedConnectionId, async (connId) => {
  capabilities.value = null;
  selectedAction.value = null;
  resolvedLinked.value = [];
  resolvedUnlinked.value = [];
  if (!connId) return;
  await loadCapabilities(connId);
});

watch(selectedAction, async (action) => {
  resolvedLinked.value = [];
  resolvedUnlinked.value = [];
  if (!action?.needsLinks || !selectedConnectionId.value) return;
  await resolveLinks(selectedConnectionId.value);
});

async function loadConnections() {
  isLoadingConnections.value = true;
  try {
    const res = await http.get("/marketplace/connections", { cache: false });
    connections.value = res.data?.value ?? res.data ?? [];
  } catch (e) {
    displaySonnerError(e);
  } finally {
    isLoadingConnections.value = false;
  }
}

async function loadCapabilities(connectionId) {
  isLoadingCapabilities.value = true;
  try {
    const res = await http.get(`/marketplace/connections/${connectionId}/capabilities`, { cache: false });
    const full = res.data?.value ?? res.data;
    capabilities.value = full?.capabilities ?? full;
    if (availableActions.value.length > 0) {
      selectedAction.value = availableActions.value[0];
    }
  } catch (e) {
    displaySonnerError(e);
  } finally {
    isLoadingCapabilities.value = false;
  }
}

async function resolveLinks(connectionId) {
  isResolving.value = true;
  try {
    const res = await http.post(
      `/marketplace/connections/${connectionId}/resolve-product-links`,
      { productIds: selectedProductIds.value },
      { cache: false }
    );
    const data = res.data?.value ?? res.data;
    resolvedLinked.value = data.linked ?? [];
    resolvedUnlinked.value = data.unlinked ?? [];
  } catch (e) {
    displaySonnerError(e);
  } finally {
    isResolving.value = false;
  }
}

const canRun = computed(() => {
  if (!selectedConnectionId.value || !selectedAction.value) return false;
  if (isRunning.value || isResolving.value || isLoadingCapabilities.value) return false;
  if (selectedAction.value.needsLinks && resolvedLinked.value.length === 0) return false;
  return true;
});

async function run() {
  if (!canRun.value) return;
  isRunning.value = true;
  try {
    const action = selectedAction.value;
    const linkIds = resolvedLinked.value.map((l) => l.linkId);
    const body = action.getBody(selectedProductIds.value, linkIds);
    const res = await http.post(action.getEndpoint(selectedConnectionId.value), body, { cache: false });
    const data = res.data?.value ?? res.data;
    displaySonnerSuccess(data?.message ?? t("products.bulkAdapterDialog.operationStarted"));
    emit("update:modelValue", false);
  } catch (e) {
    displaySonnerError(e);
  } finally {
    isRunning.value = false;
  }
}
</script>

<template>
  <Dialog :open="modelValue" @update:open="emit('update:modelValue', $event)">
    <DialogContent size="md">
      <DialogHeader>
        <DialogTitle>{{ t("products.bulkAdapterDialog.title", { count: selectedProductIds.length }) }}</DialogTitle>
        <DialogDescription>
          {{ t("products.bulkAdapterDialog.description") }}
        </DialogDescription>
      </DialogHeader>

      <div class="space-y-4 py-1">
        <!-- Connection picker -->
        <div class="space-y-1.5">
          <Label>{{ t("products.bulkAdapterDialog.adapterLabel") }}</Label>
          <Select v-model="selectedConnectionId" :disabled="isLoadingConnections">
            <SelectTrigger>
              <SelectValue :placeholder="t('products.bulkAdapterDialog.adapterPlaceholder')" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem v-for="conn in connections" :key="conn.id" :value="conn.id">
                {{ conn.name }}
              </SelectItem>
            </SelectContent>
          </Select>
        </div>

        <!-- Loading capabilities -->
        <div v-if="isLoadingCapabilities" class="flex items-center gap-2 text-sm text-muted-foreground py-1">
          <Loader2 class="size-4 animate-spin" />
          {{ t("products.bulkAdapterDialog.loadingCapabilities") }}
        </div>

        <!-- Action list -->
        <div v-else-if="availableActions.length > 0" class="space-y-1.5">
          <Label>{{ t("products.bulkAdapterDialog.actionLabel") }}</Label>
          <div class="space-y-1.5">
            <Button
              v-for="action in availableActions"
              :key="action.key"
              type="button"
              variant="outline"
              class="w-full h-auto text-left rounded-lg px-3 py-2.5 text-sm font-normal justify-start whitespace-normal transition-colors hover:bg-muted/50"
              :class="selectedAction?.key === action.key
                ? 'border-primary bg-primary/5'
                : 'border-border'"
              @click="selectedAction = action"
            >
              <div class="font-medium">{{ action.label }}</div>
              <div class="text-xs text-muted-foreground mt-0.5">{{ action.description }}</div>
            </Button>
          </div>
        </div>

        <!-- Link resolve status (for link-based actions) -->
        <div
          v-if="selectedAction?.needsLinks && selectedConnectionId"
          class="rounded-lg bg-muted/50 px-3 py-2.5 text-sm space-y-1"
        >
          <div v-if="isResolving" class="flex items-center gap-2 text-muted-foreground">
            <Loader2 class="size-3.5 animate-spin" />
            {{ t("products.bulkAdapterDialog.checkingLinks") }}
          </div>
          <template v-else>
            <div class="flex items-center justify-between">
              <span class="text-muted-foreground">{{ t("products.bulkAdapterDialog.linkedLabel") }}</span>
              <Badge variant="secondary">{{ resolvedLinked.length }}</Badge>
            </div>
            <div v-if="resolvedUnlinked.length > 0" class="flex items-center justify-between">
              <span class="text-muted-foreground">{{ t("products.bulkAdapterDialog.unlinkedLabel") }}</span>
              <Badge variant="outline">{{ resolvedUnlinked.length }}</Badge>
            </div>
            <p v-if="resolvedLinked.length === 0" class="text-destructive text-xs mt-1">
              {{ t("products.bulkAdapterDialog.noneLinkedWarning") }}
            </p>
          </template>
        </div>
      </div>

      <DialogFooter>
        <Button variant="outline" @click="emit('update:modelValue', false)">{{ t("common.cancel") }}</Button>
        <Button :disabled="!canRun" @click="run">
          <Loader2 v-if="isRunning" class="size-4 mr-2 animate-spin" />
          {{ selectedAction?.label ?? t("products.bulkAdapterDialog.runButton") }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
