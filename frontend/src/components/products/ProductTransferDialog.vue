<script setup>
// ProductTransferDialog
// ----------------------
// Dialog for cloning or moving a product to another store. Mode is chosen in
// the row menu (`mode` prop) — the dialog does not switch clone/move itself.
// Driven by the backend preview endpoint (`/products/:id/transfer-preview`) so
// the UI always reflects current server-side data (SKU conflicts, attribute
// mappability, broken marketplace links, available target groups, etc.).
//
// Two-step flow:
//   1. user picks target store → preview loads
//      (same-store clone with a single membership auto-selects and skips the picker)
//   2. user resolves conflicts (SKU override, category action, group action)
//      → confirms → /products/:id/transfer fires
//
// Move = atomic Clone + soft-delete original. Original lands in source-store
// trash; recoverable via /restore. We surface this in the success toast.

import { ref, computed, watch } from "vue";
import { useStore } from "vuex";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { FieldGroup } from "@/components/ui/field";
import { FIELD_LIMITS, isNonEmptyWithinLimit } from "@/lib/fieldLimits";
import { Spinner } from "@/components/ui/spinner";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import {
  AlertTriangle,
  ArrowRight,
  CheckCircle2,
  ExternalLink,
  FileText,
  Image,
  NotebookPen,
  Package,
  XCircle,
} from "lucide-vue-next";
import { t } from "@/i18n";

const props = defineProps({
  open: { type: Boolean, default: false },
  /** Locked transfer mode from the menu — no in-dialog clone/move switch. */
  mode: {
    type: String,
    default: "clone",
    validator: (v) => v === "clone" || v === "move",
  },
  productId: { type: Number, default: null },
  productName: { type: String, default: "" },
  productSku: { type: String, default: "" },
});

const emit = defineEmits(["update:open", "transferred"]);

const store = useStore();

// Russian plural form picker. forms = [one, few, many] - for English callers can reuse the same string 3x.
function pluralize(n, forms) {
  const mod10 = n % 10;
  const mod100 = n % 100;
  if (mod10 === 1 && mod100 !== 11) return forms[0];
  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return forms[1];
  return forms[2];
}

// --- State ---
const memberships = ref([]);
const isLoadingMemberships = ref(false);
const currentStoreId = ref(null);
const targetStoreId = ref(null);
const mode = computed(() => (props.mode === "move" ? "move" : "clone"));

const preview = ref(null);
const isLoadingPreview = ref(false);
const previewError = ref(null);

const newSku = ref("");
const categoryAction = ref("match"); // match | use | create | none
const categoryUseId = ref(null);
const groupAction = ref("none"); // none | use | create
const groupUseId = ref(null);
const groupNewName = ref("");
const confirmBreakLinks = ref(false);
const syncMissingAttrs = ref(true);
const copyFiles = ref(true);

const isSubmitting = ref(false);

const currentStoreName = computed(
  () => memberships.value.find((m) => m.id === currentStoreId.value)?.name || null,
);
const targetStoreName = computed(
  () => memberships.value.find((m) => m.id === targetStoreId.value)?.name || null,
);

const productLabel = computed(
  () => props.productName || props.productSku || `#${props.productId}`,
);

const dialogTitle = computed(() => {
  if (mode.value === "move") {
    return t("products.productTransferDialog.moveDialogTitle");
  }
  if (isSameStoreClone.value) {
    return t("products.productTransferDialog.cloneSameStoreDialogTitle");
  }
  return t("products.productTransferDialog.cloneDialogTitle");
});

const modeHint = computed(() => {
  if (mode.value === "move") {
    return t("products.productTransferDialog.moveModeHint");
  }
  if (isSameStoreClone.value) {
    return t("products.productTransferDialog.cloneSameStoreHint");
  }
  return t("products.productTransferDialog.cloneModeHint");
});

/** Clone with only one accessible store — no destination picker. */
const isSameStoreClone = computed(
  () => mode.value === "clone" && !isLoadingMemberships.value && memberships.value.length <= 1,
);

const syncingMissingAttrs = computed(
  () =>
    syncMissingAttrs.value &&
    (categoryAction.value === "match" || categoryAction.value === "create"),
);

// --- Lifecycle: open ↔ load memberships ---
watch(() => props.open, async (isOpen) => {
  if (!isOpen) {
    resetState();
    return;
  }
  await loadMemberships();
  autoSelectTargetIfNeeded();
});

async function loadMemberships() {
  isLoadingMemberships.value = true;
  // Fetch user (to know currentStoreId) and memberships in parallel.
  await Promise.all([
    store.dispatch("getMe", {
      onSuccess: (response) => {
        currentStoreId.value = response.data.value?.selectedStoreId ?? null;
      },
      onError: () => {
        currentStoreId.value = null;
      },
    }),
    store.dispatch("getMyMemberships", {
      onSuccess: (response) => {
        memberships.value = response.data.value || [];
      },
      onError: (error) => {
        memberships.value = [];
        displaySonnerError(error);
      },
    }),
  ]);
  isLoadingMemberships.value = false;
}

function autoSelectTargetIfNeeded() {
  if (mode.value !== "clone") return;
  if (memberships.value.length === 1) {
    targetStoreId.value = memberships.value[0].id;
    return;
  }
  if (memberships.value.length === 0 && currentStoreId.value != null) {
    targetStoreId.value = currentStoreId.value;
  }
}

// Stores available as transfer target. Move excludes current store; clone allows it
// (in-store duplicate is a useful operation - SKU collision will force a new SKU).
const availableTargetStores = computed(() => {
  if (mode.value === "move" && currentStoreId.value != null) {
    return memberships.value.filter((s) => s.id !== currentStoreId.value);
  }
  return memberships.value;
});

// Move cannot target the current store — clear if mode/memberships make selection invalid.
watch([mode, currentStoreId], () => {
  if (mode.value === "move" && targetStoreId.value === currentStoreId.value) {
    targetStoreId.value = null;
  }
});

function resetState() {
  targetStoreId.value = null;
  preview.value = null;
  previewError.value = null;
  newSku.value = "";
  categoryAction.value = "match";
  categoryUseId.value = null;
  groupAction.value = "none";
  groupUseId.value = null;
  groupNewName.value = "";
  confirmBreakLinks.value = false;
  syncMissingAttrs.value = true;
  copyFiles.value = true;
  isSubmitting.value = false;
}

// --- Reload preview whenever target store changes ---
watch(targetStoreId, async (newId) => {
  preview.value = null;
  previewError.value = null;
  if (!newId) return;

  isLoadingPreview.value = true;
  await store.dispatch("getTransferPreview", {
    productId: props.productId,
    targetStoreId: newId,
    onSuccess: (response) => {
      const data = response.data.value;
      preview.value = data;

      // Smart defaults: if SKU conflict, pre-populate with a "-copy" suggestion
      // (helpful for same-store clone where conflict always fires). User can edit.
      if (data.skuConflict) {
        newSku.value = `${props.productSku}-copy`;
      } else {
        newSku.value = "";
      }

      // Default category action: match if matched, otherwise create
      categoryAction.value = data.matchedCategory ? "match" : (data.sourceProduct?.categoryName ? "create" : "none");

      // No group by default
      groupAction.value = "none";
      groupUseId.value = null;
      groupNewName.value = "";

      confirmBreakLinks.value = false;
    },
    onError: (error) => {
      previewError.value = error?.response?.data?.message || error?.message || t("products.productTransferDialog.previewLoadFailed");
      preview.value = null;
    },
  });
  isLoadingPreview.value = false;
});

// --- Computed: SKU conflict resolved? ---
const skuResolved = computed(() => {
  if (!preview.value?.skuConflict) return false;
  const trimmed = newSku.value?.trim() || "";
  return trimmed.length > 0 && trimmed !== props.productSku;
});

// --- Computed validity ---
const canSubmit = computed(() => {
  if (!preview.value) return false;
  if (isSubmitting.value) return false;

  // SKU conflict requires user to provide a new sku that differs from the original
  if (preview.value.skuConflict) {
    const trimmed = newSku.value?.trim() || "";
    if (!isNonEmptyWithinLimit(trimmed, FIELD_LIMITS.productSku)) return false;
    if (trimmed === props.productSku) return false; // still the conflicting one
  }

  // Category=use needs a categoryId
  if (categoryAction.value === "use" && !categoryUseId.value) return false;

  // Group=use needs a groupId, group=create needs a name
  if (groupAction.value === "use" && !groupUseId.value) return false;
  if (groupAction.value === "create" && !isNonEmptyWithinLimit(groupNewName.value, FIELD_LIMITS.groupName)) return false;

  // For move only: breaking marketplace links must be explicitly acknowledged.
  // Clone never breaks anything (original keeps its links), so no checkbox required.
  if (mode.value === "move" && preview.value.marketplaceLinksToBreak?.length > 0 && !confirmBreakLinks.value) {
    return false;
  }

  return true;
});

// --- Submit ---
async function submit() {
  if (!canSubmit.value) return;
  isSubmitting.value = true;

  const input = {
    targetStoreId: targetStoreId.value,
    mode: mode.value,
    newSku: preview.value.skuConflict ? newSku.value.trim() : null,
    categoryAction: categoryAction.value,
    categoryId: categoryAction.value === "use" ? categoryUseId.value : null,
    groupAction: groupAction.value,
    groupId: groupAction.value === "use" ? groupUseId.value : null,
    groupName: groupAction.value === "create" ? groupNewName.value.trim() : null,
    // Clone never breaks links (only move does); always send true so backend accepts.
    confirmBreakLinks: mode.value === "move" ? confirmBreakLinks.value : true,
    syncMissingCategoryAttributes: syncMissingAttrs.value,
    copyFiles: mode.value === "move" ? true : copyFiles.value,
  };

  await store.dispatch("transferProduct", {
    productId: props.productId,
    input,
    onSuccess: (response) => {
      // Backend now enqueues - returns { queued: true, operationId, totalFiles }.
      // Real progress lives in the operations side panel.
      const result = response.data.value;
      const storeName = preview.value.targetStore?.name || t("products.productTransferDialog.targetStoreFallback");
      const headline = mode.value === "move"
        ? t("products.productTransferDialog.queuedMoveHeadline", { store: storeName })
        : t("products.productTransferDialog.queuedCloneHeadline", { store: storeName });
      const tail = result?.totalFiles
        ? t("products.productTransferDialog.progressWithFiles", { count: result.totalFiles })
        : t("products.productTransferDialog.progressGeneric");
      displaySonnerSuccess(`${headline}. ${tail}`);
      emit("transferred", { queued: true, operationId: result?.operationId, mode: mode.value });
      emit("update:open", false);
    },
    onError: (error) => {
      displaySonnerError(error);
    },
  });

  isSubmitting.value = false;
}

function close() {
  if (isSubmitting.value) return;
  emit("update:open", false);
}
</script>

<template>
  <Dialog :open="open" @update:open="(v) => emit('update:open', v)">
    <DialogContent
      size="2xl"
      class="flex max-h-[85vh] flex-col gap-0 overflow-hidden p-0"
    >
      <DialogHeader variant="bar">
        <DialogTitle>
          {{ dialogTitle }}
        </DialogTitle>
        <DialogDescription class="flex flex-wrap items-baseline gap-x-2 gap-y-0.5">
          <span class="font-medium text-foreground">{{ productLabel }}</span>
          <span
            v-if="productSku && productName"
            class="font-mono text-xs text-muted-foreground tabular-nums"
          >{{ productSku }}</span>
        </DialogDescription>
      </DialogHeader>

      <div class="min-h-0 flex-1 space-y-5 overflow-y-auto px-6 py-4 thin-scrollbar">
        <p class="text-sm text-muted-foreground">{{ modeHint }}</p>

        <FieldGroup v-if="!isSameStoreClone" class="gap-5">
          <SettingsFormField
            :label="t('products.productTransferDialog.targetStoreLabel')"
            label-for="target-store"
          >
            <Select
              :model-value="targetStoreId == null ? '' : String(targetStoreId)"
              :disabled="isLoadingMemberships"
              @update:model-value="(v) => (targetStoreId = v ? Number(v) : null)"
            >
              <SelectTrigger id="target-store" class="w-full">
                <SelectValue
                  :placeholder="
                    isLoadingMemberships
                      ? t('products.productTransferDialog.loadingStoresPlaceholder')
                      : t('products.productTransferDialog.chooseStorePlaceholder')
                  "
                />
              </SelectTrigger>
              <SelectContent>
                <SelectItem
                  v-for="m in availableTargetStores"
                  :key="m.id"
                  :value="String(m.id)"
                  as="button"
                >
                  {{ m.name }}<span
                    v-if="m.id === currentStoreId"
                    class="text-muted-foreground"
                  > · {{ t("products.productTransferDialog.currentStoreSuffix") }}</span>
                </SelectItem>
              </SelectContent>
            </Select>
          </SettingsFormField>
        </FieldGroup>

        <!-- Signature: store route once destination is known (skip for same-store clone) -->
        <div
          v-if="targetStoreName && !isSameStoreClone"
          class="flex min-w-0 items-center gap-2 rounded-xl border border-border bg-muted/40 px-3 py-2.5 text-sm"
          aria-live="polite"
        >
          <span class="min-w-0 truncate text-muted-foreground">
            {{ currentStoreName || t("products.productTransferDialog.currentStoreFallback") }}
          </span>
          <ArrowRight class="size-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
          <span class="min-w-0 truncate font-medium text-foreground">{{ targetStoreName }}</span>
        </div>

        <div
          v-if="isLoadingPreview"
          class="flex items-center gap-2 py-3 text-sm text-muted-foreground"
        >
          <Spinner />
          {{ t("products.productTransferDialog.checkingPreview") }}
        </div>

        <div
          v-else-if="previewError"
          class="rounded-xl border border-destructive/40 bg-destructive/5 p-3 text-sm text-destructive"
          role="alert"
        >
          {{ previewError }}
        </div>

        <div v-else-if="preview" class="space-y-5">
          <!-- SKU collision -->
          <div
            v-if="preview.skuConflict"
            :class="[
              'space-y-3 rounded-xl border p-3 transition-colors',
              skuResolved
                ? 'border-success/40 bg-success-muted/50'
                : 'border-warning/40 bg-warning-muted/50',
            ]"
          >
            <div
              :class="[
                'flex items-center gap-2 text-sm font-medium',
                skuResolved ? 'text-success-foreground' : 'text-warning-foreground',
              ]"
            >
              <CheckCircle2 v-if="skuResolved" class="size-4 shrink-0" />
              <AlertTriangle v-else class="size-4 shrink-0" />
              <span>
                <template v-if="skuResolved">
                  {{ t("products.productTransferDialog.skuResolvedLabel", { sku: newSku.trim() }) }}
                </template>
                <template v-else>
                  {{ t("products.productTransferDialog.skuConflictLabel", { sku: productSku }) }}
                </template>
              </span>
            </div>
            <SettingsFormField
              :label="t('products.productTransferDialog.newSkuPlaceholder')"
              label-for="new-sku"
            >
              <Input
                id="new-sku"
                v-model="newSku"
                :placeholder="t('products.productTransferDialog.newSkuPlaceholder')"
                :maxlength="FIELD_LIMITS.productSku"
              />
            </SettingsFormField>
          </div>

          <FieldGroup class="gap-5">
            <!-- Category resolution -->
            <SettingsFormField
              v-if="preview.sourceProduct.categoryName"
              :label="t('products.productTransferDialog.categoryLabel')"
              label-for="category-action"
            >
              <template #description>
                <span class="inline-flex items-start gap-1.5">
                  <CheckCircle2
                    v-if="preview.matchedCategory"
                    class="mt-0.5 size-3.5 shrink-0 text-success"
                  />
                  <XCircle
                    v-else
                    class="mt-0.5 size-3.5 shrink-0 text-warning"
                  />
                  <span>
                    {{ t("products.productTransferDialog.sourceCategoryLabel") }}
                    <span class="font-medium text-foreground">«{{ preview.sourceProduct.categoryName }}»</span>.
                    <template v-if="preview.matchedCategory">
                      {{ t("products.productTransferDialog.foundSameNameInTarget") }}
                      <span class="text-success-foreground">
                        {{ preview.matchedCategory.attributesMappable }}
                        {{
                          pluralize(preview.matchedCategory.attributesMappable, [
                            t("products.productTransferDialog.attrMappableOne"),
                            t("products.productTransferDialog.attrMappableFew"),
                            t("products.productTransferDialog.attrMappableMany"),
                          ])
                        }}
                      </span>
                      <template v-if="preview.matchedCategory.attributesUnmappable?.length">
                        ,
                        <span
                          :class="
                            syncingMissingAttrs
                              ? 'text-success-foreground'
                              : 'text-warning-foreground'
                          "
                        >
                          {{ preview.matchedCategory.attributesUnmappable.length }}
                          {{
                            pluralize(preview.matchedCategory.attributesUnmappable.length, [
                              syncingMissingAttrs
                                ? t("products.productTransferDialog.attrWillBeAddedOne")
                                : t("products.productTransferDialog.attrWontFitOne"),
                              syncingMissingAttrs
                                ? t("products.productTransferDialog.attrWillBeAddedFew")
                                : t("products.productTransferDialog.attrWontFitFew"),
                              syncingMissingAttrs
                                ? t("products.productTransferDialog.attrWillBeAddedMany")
                                : t("products.productTransferDialog.attrWontFitMany"),
                            ])
                          }}
                        </span>
                      </template>.
                    </template>
                    <template v-else>
                      {{ t("products.productTransferDialog.notFoundInTarget") }}
                    </template>
                  </span>
                </span>
              </template>
              <Select v-model="categoryAction">
                <SelectTrigger id="category-action" class="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem v-if="preview.matchedCategory" value="match" as="button">
                    {{
                      t("products.productTransferDialog.useFoundCategory", {
                        name: preview.matchedCategory.name,
                      })
                    }}
                  </SelectItem>
                  <SelectItem value="create" as="button">
                    {{ t("products.productTransferDialog.createSameCategory") }}
                  </SelectItem>
                  <SelectItem value="none" as="button">
                    {{ t("products.productTransferDialog.noCategory") }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </SettingsFormField>

            <div
              v-if="preview.matchedCategory?.attributesUnmappable?.length"
              class="space-y-2"
            >
              <p
                class="text-xs"
                :class="
                  syncingMissingAttrs
                    ? 'text-success-foreground'
                    : 'text-muted-foreground'
                "
              >
                <template v-if="syncingMissingAttrs">
                  {{ t("products.productTransferDialog.willBeAddedPrefix") }}
                  {{ preview.matchedCategory.attributesUnmappable.slice(0, 5).join(", ") }}
                  <span v-if="preview.matchedCategory.attributesUnmappable.length > 5">…</span>
                </template>
                <template v-else>
                  {{ t("products.productTransferDialog.wontFitPrefix") }}
                  {{ preview.matchedCategory.attributesUnmappable.slice(0, 5).join(", ") }}
                  <span v-if="preview.matchedCategory.attributesUnmappable.length > 5">…</span>
                </template>
              </p>
              <label
                v-if="categoryAction === 'match' || categoryAction === 'create'"
                class="flex cursor-pointer items-start gap-2 text-sm"
              >
                <Checkbox
                  id="sync-missing-attrs"
                  v-model="syncMissingAttrs"
                  class="mt-0.5"
                />
                <span class="text-xs leading-snug">
                  {{ t("products.productTransferDialog.addMissingAttrsPrefix") }}
                  {{ preview.matchedCategory.attributesUnmappable.length }}
                  {{
                    pluralize(preview.matchedCategory.attributesUnmappable.length, [
                      t("products.productTransferDialog.missingAttrOne"),
                      t("products.productTransferDialog.missingAttrFew"),
                      t("products.productTransferDialog.missingAttrMany"),
                    ])
                  }}
                  {{
                    t("products.productTransferDialog.addMissingAttrsSuffix", {
                      name: preview.matchedCategory.name,
                    })
                  }}
                </span>
              </label>
            </div>

            <p
              v-if="preview.storeAttributesMappable + preview.storeAttributesUnmappable.length > 0"
              class="text-xs text-muted-foreground"
            >
              {{
                t("products.productTransferDialog.storeAttributesSummary", {
                  mapped: preview.storeAttributesMappable,
                  total:
                    preview.storeAttributesMappable +
                    preview.storeAttributesUnmappable.length,
                })
              }}
              <template v-if="preview.storeAttributesUnmappable.length">
                .
                {{ t("products.productTransferDialog.storeAttributesMissingInTarget") }}
                {{ preview.storeAttributesUnmappable.slice(0, 3).join(", ") }}
                <span v-if="preview.storeAttributesUnmappable.length > 3">…</span>
              </template>
            </p>

            <SettingsFormField
              :label="t('products.productTransferDialog.groupLabel')"
              label-for="group-action"
            >
              <Select v-model="groupAction">
                <SelectTrigger id="group-action" class="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="none" as="button">
                    {{ t("products.productTransferDialog.noGroup") }}
                  </SelectItem>
                  <SelectItem
                    v-if="preview.targetGroups.length > 0"
                    value="use"
                    as="button"
                  >
                    {{ t("products.productTransferDialog.addToExistingGroup") }}
                  </SelectItem>
                  <SelectItem value="create" as="button">
                    {{ t("products.productTransferDialog.createNewGroup") }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </SettingsFormField>

            <SettingsFormField
              v-if="groupAction === 'use'"
              :label="t('products.productTransferDialog.chooseGroupPlaceholder')"
              label-for="group-use"
            >
              <Select
                :model-value="groupUseId == null ? '' : String(groupUseId)"
                @update:model-value="(v) => (groupUseId = v ? Number(v) : null)"
              >
                <SelectTrigger id="group-use" class="w-full">
                  <SelectValue
                    :placeholder="t('products.productTransferDialog.chooseGroupPlaceholder')"
                  />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem
                    v-for="g in preview.targetGroups"
                    :key="g.id"
                    :value="String(g.id)"
                    as="button"
                  >
                    {{ g.name }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </SettingsFormField>

            <SettingsFormField
              v-if="groupAction === 'create'"
              :label="t('products.productTransferDialog.newGroupNamePlaceholder')"
              label-for="group-new-name"
            >
              <Input
                id="group-new-name"
                v-model="groupNewName"
                :placeholder="t('products.productTransferDialog.newGroupNamePlaceholder')"
                :maxlength="FIELD_LIMITS.groupName"
              />
            </SettingsFormField>
          </FieldGroup>

          <!-- Marketplace links -->
          <div
            v-if="preview.marketplaceLinksToBreak?.length > 0"
            :class="[
              'space-y-3 rounded-xl border p-3',
              mode === 'move'
                ? 'border-warning/40 bg-warning-muted/50'
                : 'border-border bg-muted/40',
            ]"
          >
            <div
              :class="[
                'flex items-center gap-2 text-sm font-medium',
                mode === 'move' ? 'text-warning-foreground' : 'text-foreground',
              ]"
            >
              <AlertTriangle v-if="mode === 'move'" class="size-4 shrink-0" />
              <ExternalLink v-else class="size-4 shrink-0 text-muted-foreground" />
              <span>
                <template v-if="mode === 'move'">
                  {{ t("products.productTransferDialog.linksWillBreakAfterMovePrefix") }}
                  {{ preview.marketplaceLinksToBreak.length }}
                  {{
                    pluralize(preview.marketplaceLinksToBreak.length, [
                      t("products.productTransferDialog.linkOne"),
                      t("products.productTransferDialog.linkFew"),
                      t("products.productTransferDialog.linkMany"),
                    ])
                  }}
                  {{ t("products.productTransferDialog.withAdaptersSuffix") }}
                </template>
                <template v-else>
                  {{ t("products.productTransferDialog.originalHasLinksPrefix") }}
                  {{ preview.marketplaceLinksToBreak.length }}
                  {{
                    pluralize(preview.marketplaceLinksToBreak.length, [
                      t("products.productTransferDialog.linkOne"),
                      t("products.productTransferDialog.linkFew"),
                      t("products.productTransferDialog.linkMany"),
                    ])
                  }}
                  {{ t("products.productTransferDialog.withAdaptersSuffix") }}
                </template>
              </span>
            </div>
            <ul class="space-y-1.5 text-xs text-muted-foreground">
              <li
                v-for="(l, i) in preview.marketplaceLinksToBreak"
                :key="i"
                class="flex min-w-0 items-center gap-2"
              >
                <ExternalLink class="size-3 shrink-0" />
                <span class="truncate">{{ l.connectionName }}</span>
                <span class="text-border">·</span>
                <span class="font-mono tabular-nums">{{ l.externalProductId }}</span>
              </li>
            </ul>
            <p class="text-xs text-muted-foreground">
              <template v-if="mode === 'move'">
                {{ t("products.productTransferDialog.moveLinksExplanation") }}
              </template>
              <template v-else>
                {{ t("products.productTransferDialog.cloneLinksExplanation") }}
              </template>
            </p>
            <label
              v-if="mode === 'move'"
              class="flex cursor-pointer items-start gap-2 text-sm"
            >
              <Checkbox
                id="confirm-break-links"
                v-model="confirmBreakLinks"
                class="mt-0.5"
              />
              <span>{{ t("products.productTransferDialog.confirmBreakLinksLabel") }}</span>
            </label>
          </div>

          <!-- Cargo that travels with the product -->
          <div class="rounded-xl border border-border bg-muted/40 p-3">
            <p class="mb-2 text-sm font-medium text-foreground">
              {{
                mode === "move"
                  ? t("products.productTransferDialog.alsoMovesLabel")
                  : t("products.productTransferDialog.alsoCopiesLabel")
              }}
            </p>
            <label
              v-if="mode === 'clone' && (preview.filesCount > 0 || preview.foldersCount > 0 || preview.imagesCount > 0)"
              class="mb-3 flex cursor-pointer items-start gap-2 text-sm"
            >
              <Checkbox
                id="copy-files"
                v-model="copyFiles"
                class="mt-0.5"
              />
              <span class="leading-snug">
                {{ t("products.productTransferDialog.copyFilesLabel") }}
                <span class="block text-xs font-normal text-muted-foreground">
                  {{ t("products.productTransferDialog.copyFilesHint") }}
                </span>
              </span>
            </label>
            <ul class="grid gap-1.5 text-xs text-muted-foreground sm:grid-cols-2">
              <li
                :class="[
                  'flex items-center gap-2',
                  mode === 'clone' && !copyFiles && 'text-muted-foreground/60 line-through',
                ]"
              >
                <FileText class="size-3.5 shrink-0" />
                <span>
                  {{ preview.filesCount }}
                  {{
                    pluralize(preview.filesCount, [
                      t("products.productTransferDialog.fileOne"),
                      t("products.productTransferDialog.fileFew"),
                      t("products.productTransferDialog.fileMany"),
                    ])
                  }}
                  <template v-if="preview.foldersCount">
                    {{ t("products.productTransferDialog.inFoldersPrefix") }}
                    {{ preview.foldersCount }}
                    {{
                      pluralize(preview.foldersCount, [
                        t("products.productTransferDialog.folderOne"),
                        t("products.productTransferDialog.folderFew"),
                        t("products.productTransferDialog.folderMany"),
                      ])
                    }}
                  </template>
                </span>
              </li>
              <li
                :class="[
                  'flex items-center gap-2',
                  mode === 'clone' && !copyFiles && 'text-muted-foreground/60 line-through',
                ]"
              >
                <Image class="size-3.5 shrink-0" />
                <span>
                  {{ preview.imagesCount }}
                  {{
                    pluralize(preview.imagesCount, [
                      t("products.productTransferDialog.imageOne"),
                      t("products.productTransferDialog.imageFew"),
                      t("products.productTransferDialog.imageMany"),
                    ])
                  }}
                </span>
              </li>
              <li v-if="preview.notesCount > 0" class="flex items-center gap-2">
                <NotebookPen class="size-3.5 shrink-0" />
                <span>
                  {{ preview.notesCount }}
                  {{
                    pluralize(preview.notesCount, [
                      t("products.productTransferDialog.noteOne"),
                      t("products.productTransferDialog.noteFew"),
                      t("products.productTransferDialog.noteMany"),
                    ])
                  }}
                </span>
              </li>
              <li v-if="preview.variantsCount > 0" class="flex items-center gap-2">
                <Package class="size-3.5 shrink-0" />
                <span>
                  {{ preview.variantsCount }}
                  {{
                    pluralize(preview.variantsCount, [
                      t("products.productTransferDialog.variantOne"),
                      t("products.productTransferDialog.variantFew"),
                      t("products.productTransferDialog.variantMany"),
                    ])
                  }}
                </span>
              </li>
            </ul>
          </div>
        </div>
      </div>

      <DialogFooter variant="bar">
        <Button variant="outline" :disabled="isSubmitting" @click="close">
          {{ t("common.cancel") }}
        </Button>
        <Button :disabled="!canSubmit" @click="submit">
          <Spinner v-if="isSubmitting" class="mr-2" />
          {{
            mode === "move"
              ? t("products.productTransferDialog.moveAction")
              : t("products.productTransferDialog.cloneAction")
          }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
