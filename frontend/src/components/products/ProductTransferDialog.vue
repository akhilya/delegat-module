<script setup>
// ProductTransferDialog
// ----------------------
// Dialog for cloning or moving a product to another store. Driven entirely by
// the backend preview endpoint (`/products/:id/transfer-preview`) so the UI
// always reflects current server-side data (SKU conflicts, attribute
// mappability, broken marketplace links, available target groups, etc.).
//
// Two-step flow:
//   1. user picks target store + mode → preview loads
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
import Label from "@/components/ui/label/Label.vue";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { Loader2, AlertTriangle, ArrowRightLeft, Copy, ExternalLink, CheckCircle2, XCircle } from "lucide-vue-next";
import { t } from "@/i18n";

const props = defineProps({
  open: { type: Boolean, default: false },
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
const mode = ref("clone"); // "clone" | "move"

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

const isSubmitting = ref(false);

// --- Lifecycle: open ↔ load memberships ---
watch(() => props.open, async (isOpen) => {
  if (!isOpen) {
    resetState();
    return;
  }
  await loadMemberships();
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

// Stores available as transfer target. Move excludes current store; clone allows it
// (in-store duplicate is a useful operation - SKU collision will force a new SKU).
const availableTargetStores = computed(() => {
  if (mode.value === "move" && currentStoreId.value != null) {
    return memberships.value.filter((s) => s.id !== currentStoreId.value);
  }
  return memberships.value;
});

// If user switched to "move" while having current store selected as target - clear it.
watch(mode, (newMode) => {
  if (newMode === "move" && targetStoreId.value === currentStoreId.value) {
    targetStoreId.value = null;
  }
});

function resetState() {
  targetStoreId.value = null;
  mode.value = "clone";
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
    if (trimmed.length === 0) return false;
    if (trimmed === props.productSku) return false; // still the conflicting one
  }

  // Category=use needs a categoryId
  if (categoryAction.value === "use" && !categoryUseId.value) return false;

  // Group=use needs a groupId, group=create needs a name
  if (groupAction.value === "use" && !groupUseId.value) return false;
  if (groupAction.value === "create" && (!groupNewName.value || !groupNewName.value.trim())) return false;

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
  };

  await store.dispatch("transferProduct", {
    productId: props.productId,
    input,
    onSuccess: (response) => {
      // Backend now enqueues - returns { queued: true, operationId, totalFiles }.
      // Real progress lives in the operations side panel.
      const result = response.data.value;
      const targetStoreName = preview.value.targetStore?.name || t("products.productTransferDialog.targetStoreFallback");
      const headline = mode.value === "move"
        ? t("products.productTransferDialog.queuedMoveHeadline", { store: targetStoreName })
        : t("products.productTransferDialog.queuedCloneHeadline", { store: targetStoreName });
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
    <DialogContent size="3xl" class="flex max-h-[85vh] flex-col overflow-hidden">
      <DialogHeader>
        <DialogTitle class="flex items-center gap-2">
          <ArrowRightLeft class="size-5" />
          {{ t("products.productTransferDialog.dialogTitle") }}
        </DialogTitle>
        <DialogDescription>
          {{ productName || productSku || `#${productId}` }}
          <span v-if="productSku" class="text-muted-foreground ml-2">[{{ productSku }}]</span>
        </DialogDescription>
      </DialogHeader>

      <div class="-mx-6 min-h-0 flex-1 space-y-5 overflow-y-auto px-6 py-2 thin-scrollbar">
        <!-- Mode toggle -->
        <div class="space-y-2">
          <Label class="text-sm">{{ t("products.productTransferDialog.modeLabel") }}</Label>
          <div class="grid grid-cols-2 gap-2">
            <Button
              type="button"
              variant="outline"
              :class="[
                'h-auto flex items-center justify-center gap-2 px-3 py-2 rounded-md text-sm font-normal transition',
                mode === 'clone' ? 'border-primary bg-primary/5 text-foreground' : 'border-input text-muted-foreground hover:border-primary/50'
              ]"
              @click="mode = 'clone'"
            >
              <Copy class="size-4" />
              {{ t("products.productTransferDialog.cloneAction") }}
            </Button>
            <Button
              type="button"
              variant="outline"
              :class="[
                'h-auto flex items-center justify-center gap-2 px-3 py-2 rounded-md text-sm font-normal transition',
                mode === 'move' ? 'border-primary bg-primary/5 text-foreground' : 'border-input text-muted-foreground hover:border-primary/50'
              ]"
              @click="mode = 'move'"
            >
              <ArrowRightLeft class="size-4" />
              {{ t("products.productTransferDialog.moveAction") }}
            </Button>
          </div>
          <p class="text-xs text-muted-foreground">
            <template v-if="mode === 'clone'">{{ t("products.productTransferDialog.cloneModeHint") }}</template>
            <template v-else>{{ t("products.productTransferDialog.moveModeHint") }}</template>
          </p>
        </div>

        <!-- Target store -->
        <div class="space-y-2">
          <Label for="target-store" class="text-sm">{{ t("products.productTransferDialog.targetStoreLabel") }}</Label>
          <Select
            :model-value="targetStoreId == null ? '' : String(targetStoreId)"
            @update:model-value="(v) => targetStoreId = v ? Number(v) : null"
            :disabled="isLoadingMemberships"
          >
            <SelectTrigger id="target-store" class="w-full">
              <SelectValue :placeholder="isLoadingMemberships ? t('products.productTransferDialog.loadingStoresPlaceholder') : t('products.productTransferDialog.chooseStorePlaceholder')" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem
                v-for="m in availableTargetStores"
                :key="m.id"
                :value="String(m.id)"
                as="button"
              >
                {{ m.name }}<span v-if="m.id === currentStoreId" class="text-muted-foreground"> · {{ t("products.productTransferDialog.currentStoreSuffix") }}</span>
              </SelectItem>
            </SelectContent>
          </Select>
        </div>

        <!-- Preview area -->
        <div v-if="isLoadingPreview" class="flex items-center gap-2 text-sm text-muted-foreground py-2">
          <Loader2 class="size-4 animate-spin" />
          {{ t("products.productTransferDialog.checkingPreview") }}
        </div>

        <div v-else-if="previewError" class="rounded-md border border-destructive/40 bg-destructive/5 p-3 text-sm text-destructive">
          {{ previewError }}
        </div>

        <div v-else-if="preview" class="space-y-4">
          <!-- SKU collision - dynamic: amber while unresolved, green once user entered a valid alternative -->
          <div v-if="preview.skuConflict"
               :class="[
                 'space-y-2 rounded-md border p-3 transition-colors',
                 skuResolved
                   ? 'border-emerald-500/40 bg-emerald-500/5'
                   : 'border-amber-500/40 bg-amber-500/5'
               ]">
            <Label for="new-sku" :class="[
              'flex items-center gap-2 text-sm font-medium',
              skuResolved ? 'text-emerald-700 dark:text-emerald-400' : 'text-amber-700 dark:text-amber-400'
            ]">
              <CheckCircle2 v-if="skuResolved" class="size-4" />
              <AlertTriangle v-else class="size-4" />
              <template v-if="skuResolved">
                {{ t("products.productTransferDialog.skuResolvedLabel", { sku: newSku.trim() }) }}
              </template>
              <template v-else>
                {{ t("products.productTransferDialog.skuConflictLabel", { sku: productSku }) }}
              </template>
            </Label>
            <Input id="new-sku" v-model="newSku" :placeholder="t('products.productTransferDialog.newSkuPlaceholder')" />
          </div>

          <!-- Category resolution -->
          <div v-if="preview.sourceProduct.categoryName" class="space-y-2">
            <Label class="text-sm">{{ t("products.productTransferDialog.categoryLabel") }}</Label>
            <div class="flex items-start gap-1.5 text-xs">
              <CheckCircle2 v-if="preview.matchedCategory" class="size-3.5 mt-0.5 text-emerald-600 dark:text-emerald-400 shrink-0" />
              <XCircle v-else class="size-3.5 mt-0.5 text-amber-700 dark:text-amber-400 shrink-0" />
              <span class="text-muted-foreground">
                {{ t("products.productTransferDialog.sourceCategoryLabel") }} <span class="font-medium text-foreground">«{{ preview.sourceProduct.categoryName }}»</span>.
                <template v-if="preview.matchedCategory">
                  {{ t("products.productTransferDialog.foundSameNameInTarget") }}
                  <span class="text-emerald-700 dark:text-emerald-400">{{ preview.matchedCategory.attributesMappable }} {{ pluralize(preview.matchedCategory.attributesMappable, [t("products.productTransferDialog.attrMappableOne"), t("products.productTransferDialog.attrMappableFew"), t("products.productTransferDialog.attrMappableMany")]) }}</span><template v-if="preview.matchedCategory.attributesUnmappable?.length">,
                    <span v-if="syncMissingAttrs && (categoryAction === 'match' || categoryAction === 'create')" class="text-emerald-700 dark:text-emerald-400">{{ preview.matchedCategory.attributesUnmappable.length }} {{ pluralize(preview.matchedCategory.attributesUnmappable.length, [t("products.productTransferDialog.attrWillBeAddedOne"), t("products.productTransferDialog.attrWillBeAddedFew"), t("products.productTransferDialog.attrWillBeAddedMany")]) }}</span>
                    <span v-else class="text-amber-700 dark:text-amber-400">{{ preview.matchedCategory.attributesUnmappable.length }} {{ pluralize(preview.matchedCategory.attributesUnmappable.length, [t("products.productTransferDialog.attrWontFitOne"), t("products.productTransferDialog.attrWontFitFew"), t("products.productTransferDialog.attrWontFitMany")]) }}</span>
                  </template>.
                </template>
                <template v-else>
                  {{ t("products.productTransferDialog.notFoundInTarget") }}
                </template>
              </span>
            </div>
            <Select v-model="categoryAction">
              <SelectTrigger class="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem v-if="preview.matchedCategory" value="match" as="button">
                  {{ t("products.productTransferDialog.useFoundCategory", { name: preview.matchedCategory.name }) }}
                </SelectItem>
                <SelectItem value="create" as="button">{{ t("products.productTransferDialog.createSameCategory") }}</SelectItem>
                <SelectItem value="none" as="button">{{ t("products.productTransferDialog.noCategory") }}</SelectItem>
              </SelectContent>
            </Select>
            <div v-if="preview.matchedCategory?.attributesUnmappable?.length" class="space-y-2">
              <div class="text-xs" :class="syncMissingAttrs && (categoryAction === 'match' || categoryAction === 'create') ? 'text-emerald-700 dark:text-emerald-400' : 'text-muted-foreground'">
                <template v-if="syncMissingAttrs && (categoryAction === 'match' || categoryAction === 'create')">
                  {{ t("products.productTransferDialog.willBeAddedPrefix") }} {{ preview.matchedCategory.attributesUnmappable.slice(0, 5).join(", ") }}<span v-if="preview.matchedCategory.attributesUnmappable.length > 5">…</span>
                </template>
                <template v-else>
                  {{ t("products.productTransferDialog.wontFitPrefix") }} {{ preview.matchedCategory.attributesUnmappable.slice(0, 5).join(", ") }}<span v-if="preview.matchedCategory.attributesUnmappable.length > 5">…</span>
                </template>
              </div>
              <label v-if="categoryAction === 'match' || categoryAction === 'create'" class="flex items-start gap-2 text-sm cursor-pointer">
                <Checkbox
                  :model-value="syncMissingAttrs"
                  @update:model-value="syncMissingAttrs = $event"
                  class="mt-0.5"
                />
                <span class="text-xs">
                  {{ t("products.productTransferDialog.addMissingAttrsPrefix") }} {{ preview.matchedCategory.attributesUnmappable.length }}
                  {{ pluralize(preview.matchedCategory.attributesUnmappable.length, [t("products.productTransferDialog.missingAttrOne"), t("products.productTransferDialog.missingAttrFew"), t("products.productTransferDialog.missingAttrMany")]) }}
                  {{ t("products.productTransferDialog.addMissingAttrsSuffix", { name: preview.matchedCategory.name }) }}
                </span>
              </label>
            </div>
          </div>

          <!-- Store attributes -->
          <div v-if="preview.storeAttributesMappable + preview.storeAttributesUnmappable.length > 0"
               class="text-xs text-muted-foreground">
            {{ t("products.productTransferDialog.storeAttributesSummary", { mapped: preview.storeAttributesMappable, total: preview.storeAttributesMappable + preview.storeAttributesUnmappable.length }) }}<template v-if="preview.storeAttributesUnmappable.length">. {{ t("products.productTransferDialog.storeAttributesMissingInTarget") }} {{ preview.storeAttributesUnmappable.slice(0, 3).join(", ") }}<span v-if="preview.storeAttributesUnmappable.length > 3">…</span></template>
          </div>

          <!-- Group selection -->
          <div class="space-y-2">
            <Label class="text-sm">{{ t("products.productTransferDialog.groupLabel") }}</Label>
            <Select v-model="groupAction">
              <SelectTrigger class="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="none" as="button">{{ t("products.productTransferDialog.noGroup") }}</SelectItem>
                <SelectItem v-if="preview.targetGroups.length > 0" value="use" as="button">{{ t("products.productTransferDialog.addToExistingGroup") }}</SelectItem>
                <SelectItem value="create" as="button">{{ t("products.productTransferDialog.createNewGroup") }}</SelectItem>
              </SelectContent>
            </Select>
            <Select
              v-if="groupAction === 'use'"
              :model-value="groupUseId == null ? '' : String(groupUseId)"
              @update:model-value="(v) => groupUseId = v ? Number(v) : null"
            >
              <SelectTrigger class="w-full">
                <SelectValue :placeholder="t('products.productTransferDialog.chooseGroupPlaceholder')" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem v-for="g in preview.targetGroups" :key="g.id" :value="String(g.id)" as="button">
                  {{ g.name }}
                </SelectItem>
              </SelectContent>
            </Select>
            <Input v-if="groupAction === 'create'" v-model="groupNewName" :placeholder="t('products.productTransferDialog.newGroupNamePlaceholder')" />
          </div>

          <!-- Marketplace links: behavior differs by mode -->
          <!-- Clone: just informational ("new product won't have links"). No checkbox. -->
          <!-- Move: warning + explicit acknowledgement (original loses its links). -->
          <div v-if="preview.marketplaceLinksToBreak?.length > 0"
               :class="[
                 'space-y-2 rounded-md border p-3',
                 mode === 'move'
                   ? 'border-amber-500/40 bg-amber-500/5'
                   : 'border-input bg-muted/30'
               ]">
            <div :class="[
              'flex items-center gap-2 text-sm font-medium',
              mode === 'move' ? 'text-amber-700 dark:text-amber-400' : 'text-foreground'
            ]">
              <AlertTriangle v-if="mode === 'move'" class="size-4" />
              <ExternalLink v-else class="size-4" />
              <template v-if="mode === 'move'">
                {{ t("products.productTransferDialog.linksWillBreakAfterMovePrefix") }} {{ preview.marketplaceLinksToBreak.length }} {{ pluralize(preview.marketplaceLinksToBreak.length, [t("products.productTransferDialog.linkOne"), t("products.productTransferDialog.linkFew"), t("products.productTransferDialog.linkMany")]) }} {{ t("products.productTransferDialog.withAdaptersSuffix") }}
              </template>
              <template v-else>
                {{ t("products.productTransferDialog.originalHasLinksPrefix") }} {{ preview.marketplaceLinksToBreak.length }} {{ pluralize(preview.marketplaceLinksToBreak.length, [t("products.productTransferDialog.linkOne"), t("products.productTransferDialog.linkFew"), t("products.productTransferDialog.linkMany")]) }} {{ t("products.productTransferDialog.withAdaptersSuffix") }}
              </template>
            </div>
            <ul class="text-xs text-muted-foreground space-y-1">
              <li v-for="(l, i) in preview.marketplaceLinksToBreak" :key="i" class="flex items-center gap-2">
                <ExternalLink class="size-3" />
                <span>{{ l.connectionName }}</span>
                <span class="text-muted-foreground">·</span>
                <span class="font-mono">{{ l.externalProductId }}</span>
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
            <label v-if="mode === 'move'" class="flex items-start gap-2 text-sm cursor-pointer">
              <Checkbox
                :model-value="confirmBreakLinks"
                @update:model-value="confirmBreakLinks = $event"
                class="mt-0.5"
              />
              <span>{{ t("products.productTransferDialog.confirmBreakLinksLabel") }}</span>
            </label>
          </div>

          <!-- Summary of what will travel -->
          <div class="rounded-md border bg-muted/30 p-3 text-xs text-muted-foreground space-y-0.5">
            <div class="font-medium text-foreground mb-1">
              {{ mode === 'move' ? t("products.productTransferDialog.alsoMovesLabel") : t("products.productTransferDialog.alsoCopiesLabel") }}
            </div>
            <div>· {{ preview.filesCount }} {{ pluralize(preview.filesCount, [t("products.productTransferDialog.fileOne"), t("products.productTransferDialog.fileFew"), t("products.productTransferDialog.fileMany")]) }}<span v-if="preview.foldersCount"> {{ t("products.productTransferDialog.inFoldersPrefix") }} {{ preview.foldersCount }} {{ pluralize(preview.foldersCount, [t("products.productTransferDialog.folderOne"), t("products.productTransferDialog.folderFew"), t("products.productTransferDialog.folderMany")]) }}</span></div>
            <div>· {{ preview.imagesCount }} {{ pluralize(preview.imagesCount, [t("products.productTransferDialog.imageOne"), t("products.productTransferDialog.imageFew"), t("products.productTransferDialog.imageMany")]) }}</div>
            <div v-if="preview.notesCount > 0">· {{ preview.notesCount }} {{ pluralize(preview.notesCount, [t("products.productTransferDialog.noteOne"), t("products.productTransferDialog.noteFew"), t("products.productTransferDialog.noteMany")]) }}</div>
            <div v-if="preview.variantsCount > 0">· {{ preview.variantsCount }} {{ pluralize(preview.variantsCount, [t("products.productTransferDialog.variantOne"), t("products.productTransferDialog.variantFew"), t("products.productTransferDialog.variantMany")]) }}</div>
          </div>
        </div>
      </div>

      <DialogFooter>
        <Button variant="outline" @click="close" :disabled="isSubmitting">{{ t("common.cancel") }}</Button>
        <Button @click="submit" :disabled="!canSubmit">
          <Loader2 v-if="isSubmitting" class="size-4 animate-spin mr-2" />
          {{ mode === "move" ? t("products.productTransferDialog.moveAction") : t("products.productTransferDialog.cloneAction") }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
