<script setup>
import { Spinner } from "@/components/ui/spinner";
// BulkAdapterDialog — pick adapter + action for a product selection.
// Shell matches bulkEditDialog / FieldChangesPreviewDialog (bordered header/footer, min-h-0 scroll).
import { ref, watch, computed } from "vue";
import { http } from "@/main";
import { t } from "@/i18n";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { FieldGroup } from "@/components/ui/field";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  ArrowUpToLine,
  ArrowDownToLine,
  DollarSign,
  Package,
  AlertTriangle,
  Link2,
} from "lucide-vue-next";

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  selectedProducts: { type: Array, default: () => [] },
});
const emit = defineEmits(["update:modelValue"]);

const ALL_ACTIONS = [
  {
    key: "export",
    label: t("products.bulkAdapterDialog.actions.export.label"),
    description: t("products.bulkAdapterDialog.actions.export.description"),
    capabilityKey: "exportProductBulk",
    needsLinks: false,
    icon: ArrowUpToLine,
    getEndpoint: (connId) =>
      `/marketplace/connections/${connId}/products/export-batch`,
    getBody: (productIds) => ({ productIds }),
  },
  {
    key: "import",
    label: t("products.bulkAdapterDialog.actions.import.label"),
    description: t("products.bulkAdapterDialog.actions.import.description"),
    capabilityKey: "importProduct",
    needsLinks: true,
    icon: ArrowDownToLine,
    getEndpoint: () => `/marketplace/links/import-bulk`,
    getBody: (_productIds, linkIds) => ({ linkIds }),
  },
  {
    key: "exportPrices",
    label: t("products.bulkAdapterDialog.actions.exportPrices.label"),
    description: t(
      "products.bulkAdapterDialog.actions.exportPrices.description",
    ),
    capabilityKey: "exportPrices",
    needsLinks: true,
    icon: DollarSign,
    getEndpoint: (connId) => `/marketplace/connections/${connId}/prices/export`,
    getBody: (_productIds, linkIds) => ({ linkIds }),
  },
  {
    key: "exportStocks",
    label: t("products.bulkAdapterDialog.actions.exportStocks.label"),
    description: t(
      "products.bulkAdapterDialog.actions.exportStocks.description",
    ),
    capabilityKey: "exportStocks",
    needsLinks: true,
    icon: Package,
    getEndpoint: (connId) => `/marketplace/connections/${connId}/stocks/export`,
    getBody: (_productIds, linkIds) => ({ linkIds }),
  },
  {
    key: "importPrices",
    label: t("products.bulkAdapterDialog.actions.importPrices.label"),
    description: t(
      "products.bulkAdapterDialog.actions.importPrices.description",
    ),
    capabilityKey: "importPrices",
    needsLinks: true,
    icon: DollarSign,
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

const selectedProductIds = computed(() =>
  props.selectedProducts.map((p) => p.id),
);
const selectedCount = computed(() => selectedProductIds.value.length);

const availableActions = computed(() => {
  if (!capabilities.value) return [];
  return ALL_ACTIONS.filter((a) => capabilities.value[a.capabilityKey]);
});

const showLinkStatus = computed(
  () => !!selectedAction.value?.needsLinks && !!selectedConnectionId.value,
);

const showNoneLinkedWarning = computed(
  () =>
    showLinkStatus.value &&
    !isResolving.value &&
    resolvedLinked.value.length === 0,
);

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
  },
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
    const res = await http.get(
      `/marketplace/connections/${connectionId}/capabilities`,
      {
        cache: false,
      },
    );
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
      { cache: false },
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
  if (isRunning.value || isResolving.value || isLoadingCapabilities.value)
    return false;
  if (selectedAction.value.needsLinks && resolvedLinked.value.length === 0)
    return false;
  return true;
});

async function run() {
  if (!canRun.value) return;
  isRunning.value = true;
  try {
    const action = selectedAction.value;
    const linkIds = resolvedLinked.value.map((l) => l.linkId);
    const body = action.getBody(selectedProductIds.value, linkIds);
    const res = await http.post(
      action.getEndpoint(selectedConnectionId.value),
      body,
      {
        cache: false,
      },
    );
    const data = res.data?.value ?? res.data;
    displaySonnerSuccess(
      data?.message ?? t("products.bulkAdapterDialog.operationStarted"),
    );
    emit("update:modelValue", false);
  } catch (e) {
    displaySonnerError(e);
  } finally {
    isRunning.value = false;
  }
}

function actionRowClass(action) {
  const selected = selectedAction.value?.key === action.key;
  return [
    "h-auto min-h-0 w-full flex items-start justify-start gap-3 rounded-md border px-3 py-2.5 text-left font-normal text-foreground whitespace-normal transition-colors",
    selected
      ? "border-primary bg-primary/5"
      : "border-border hover:bg-muted/40",
  ];
}
</script>

<template>
  <Dialog :open="modelValue" @update:open="emit('update:modelValue', $event)">
    <DialogContent
      size="2xl"
      class="flex max-h-[90vh] flex-col gap-0 overflow-hidden p-0"
    >
      <DialogHeader variant="bar">
        <DialogTitle>
          {{ t("products.bulkAdapterDialog.title") }}
        </DialogTitle>
        <DialogDescription>
          {{
            t("products.bulkAdapterDialog.selectedCount", {
              count: selectedCount,
            })
          }}
        </DialogDescription>
      </DialogHeader>

      <div
        v-if="showNoneLinkedWarning"
        class="shrink-0 flex items-start gap-2 border-b border-destructive/20 bg-destructive/5 px-6 py-2.5 text-sm text-destructive"
      >
        <AlertTriangle class="mt-0.5 size-4 shrink-0" />
        <span>{{ t("products.bulkAdapterDialog.noneLinkedWarning") }}</span>
      </div>

      <div class="min-h-0 flex-1 space-y-5 overflow-y-auto px-6 py-4">
        <p class="text-xs text-muted-foreground">
          {{ t("products.bulkAdapterDialog.description") }}
        </p>

        <FieldGroup class="gap-4">
          <SettingsFormField
            :label="t('products.bulkAdapterDialog.adapterLabel')"
            label-for="bulk-adapter-connection"
          >
            <Select
              v-model="selectedConnectionId"
              :disabled="isLoadingConnections"
            >
              <SelectTrigger
                id="bulk-adapter-connection"
                class="w-full min-w-0"
              >
                <SelectValue
                  :placeholder="
                    t('products.bulkAdapterDialog.adapterPlaceholder')
                  "
                />
              </SelectTrigger>
              <SelectContent>
                <SelectItem
                  v-for="conn in connections"
                  :key="conn.id"
                  :value="conn.id"
                >
                  {{ conn.name }}
                </SelectItem>
              </SelectContent>
            </Select>
          </SettingsFormField>

          <div
            v-if="isLoadingCapabilities"
            class="flex items-center gap-2 py-1 text-sm text-muted-foreground"
          >
            <Spinner />
            {{ t("products.bulkAdapterDialog.loadingCapabilities") }}
          </div>

          <div
            v-else-if="selectedConnectionId && availableActions.length === 0"
            class="rounded-md border border-dashed border-border px-3 py-6 text-center text-sm text-muted-foreground"
          >
            {{ t("products.bulkAdapterDialog.noActions") }}
          </div>

          <SettingsFormField
            v-else-if="availableActions.length > 0"
            :label="t('products.bulkAdapterDialog.actionLabel')"
          >
            <div
              class="space-y-1.5"
              role="listbox"
              :aria-label="t('products.bulkAdapterDialog.actionLabel')"
            >
              <Button
                v-for="action in availableActions"
                :key="action.key"
                variant="ghost"
                type="button"
                role="option"
                :aria-selected="selectedAction?.key === action.key"
                :class="actionRowClass(action)"
                @click="selectedAction = action"
              >
                <component
                  :is="action.icon"
                  class="mt-0.5 size-4 shrink-0 text-muted-foreground"
                />
                <div class="min-w-0 flex-1">
                  <div class="text-sm font-medium leading-none">
                    {{ action.label }}
                  </div>
                  <div class="mt-1 text-xs text-muted-foreground">
                    {{ action.description }}
                  </div>
                </div>
              </Button>
            </div>
          </SettingsFormField>
        </FieldGroup>

        <div
          v-if="showLinkStatus"
          class="space-y-2 rounded-md border border-border px-3 py-2.5"
        >
          <div
            class="flex items-center gap-2 text-xs font-medium text-muted-foreground"
          >
            <Link2 class="size-3.5" />
            {{ t("products.bulkAdapterDialog.linksHeading") }}
          </div>

          <div
            v-if="isResolving"
            class="flex items-center gap-2 text-sm text-muted-foreground"
          >
            <Spinner size="sm" />
            {{ t("products.bulkAdapterDialog.checkingLinks") }}
          </div>

          <div v-else class="space-y-1.5 text-sm">
            <div class="flex items-center justify-between gap-3">
              <span class="text-muted-foreground">
                {{ t("products.bulkAdapterDialog.linkedLabel") }}
              </span>
              <Badge variant="secondary" class="tabular-nums">
                {{ resolvedLinked.length }}
              </Badge>
            </div>
            <div
              v-if="resolvedUnlinked.length > 0"
              class="flex items-center justify-between gap-3"
            >
              <span class="text-muted-foreground">
                {{ t("products.bulkAdapterDialog.unlinkedLabel") }}
              </span>
              <Badge variant="outline" class="tabular-nums">
                {{ resolvedUnlinked.length }}
              </Badge>
            </div>
          </div>
        </div>
      </div>

      <DialogFooter variant="bar" class="sm:justify-between">
        <DialogClose as-child>
          <Button type="button" variant="outline" :disabled="isRunning">
            {{ t("common.cancel") }}
          </Button>
        </DialogClose>
        <Button type="button" :disabled="!canRun" @click="run">
          <Spinner v-if="isRunning" class="mr-2" />
          {{
            selectedAction?.label ?? t("products.bulkAdapterDialog.runButton")
          }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
