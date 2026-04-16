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
import Button from "@/components/ui/button/Button.vue";
import Input from "@/components/ui/input/Input.vue";
import Label from "@/components/ui/label/Label.vue";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { Loader2, AlertTriangle, ArrowRightLeft, Copy, ExternalLink } from "lucide-vue-next";

const props = defineProps({
  open: { type: Boolean, default: false },
  productId: { type: Number, default: null },
  productName: { type: String, default: "" },
  productSku: { type: String, default: "" },
});

const emit = defineEmits(["update:open", "transferred"]);

const store = useStore();

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
// (in-store duplicate is a useful operation — SKU collision will force a new SKU).
const availableTargetStores = computed(() => {
  if (mode.value === "move" && currentStoreId.value != null) {
    return memberships.value.filter((s) => s.id !== currentStoreId.value);
  }
  return memberships.value;
});

// If user switched to "move" while having current store selected as target — clear it.
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
      previewError.value = error?.response?.data?.message || error?.message || "Не удалось загрузить превью";
      preview.value = null;
    },
  });
  isLoadingPreview.value = false;
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
  };

  await store.dispatch("transferProduct", {
    productId: props.productId,
    input,
    onSuccess: (response) => {
      // Backend now enqueues — returns { queued: true, operationId, totalFiles }.
      // Real progress lives in the operations side panel (SignalR-driven).
      const result = response.data.value;
      const targetStoreName = preview.value.targetStore?.name || "целевой магазин";
      const verb = mode.value === "move" ? "Перенос" : "Клонирование";
      const tail = result?.totalFiles
        ? `Файлов в очереди: ${result.totalFiles}. Прогресс — в боковой панели.`
        : "Прогресс — в боковой панели.";
      displaySonnerSuccess(`${verb} в «${targetStoreName}» поставлено в очередь. ${tail}`);
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
    <DialogContent class="max-w-2xl">
      <DialogHeader>
        <DialogTitle class="flex items-center gap-2">
          <ArrowRightLeft class="size-5" />
          Перенести товар в другой магазин
        </DialogTitle>
        <DialogDescription>
          {{ productName || productSku || `#${productId}` }}
          <span v-if="productSku" class="text-muted-foreground ml-2">[{{ productSku }}]</span>
        </DialogDescription>
      </DialogHeader>

      <div class="space-y-5 py-2">
        <!-- Mode toggle -->
        <div class="space-y-2">
          <Label class="text-sm">Режим</Label>
          <div class="grid grid-cols-2 gap-2">
            <button
              type="button"
              :class="[
                'flex items-center justify-center gap-2 px-3 py-2 rounded-md border text-sm transition',
                mode === 'clone' ? 'border-primary bg-primary/5 text-foreground' : 'border-input text-muted-foreground hover:border-primary/50'
              ]"
              @click="mode = 'clone'"
            >
              <Copy class="size-4" />
              Клонировать
            </button>
            <button
              type="button"
              :class="[
                'flex items-center justify-center gap-2 px-3 py-2 rounded-md border text-sm transition',
                mode === 'move' ? 'border-primary bg-primary/5 text-foreground' : 'border-input text-muted-foreground hover:border-primary/50'
              ]"
              @click="mode = 'move'"
            >
              <ArrowRightLeft class="size-4" />
              Переместить
            </button>
          </div>
          <p class="text-xs text-muted-foreground">
            <template v-if="mode === 'clone'">Создаётся копия в целевом магазине. Оригинал остаётся как есть.</template>
            <template v-else>Создаётся копия в целевом магазине, оригинал отправляется в корзину исходного. Восстановим из корзины при необходимости.</template>
          </p>
        </div>

        <!-- Target store -->
        <div class="space-y-2">
          <Label for="target-store" class="text-sm">Целевой магазин</Label>
          <Select
            :model-value="targetStoreId == null ? '' : String(targetStoreId)"
            @update:model-value="(v) => targetStoreId = v ? Number(v) : null"
            :disabled="isLoadingMemberships"
          >
            <SelectTrigger id="target-store" class="w-full">
              <SelectValue :placeholder="isLoadingMemberships ? 'Загрузка…' : 'Магазин назначения'" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem
                v-for="m in availableTargetStores"
                :key="m.id"
                :value="String(m.id)"
                as="button"
              >
                {{ m.name }}<span v-if="m.id === currentStoreId" class="text-muted-foreground"> · текущий</span>
              </SelectItem>
            </SelectContent>
          </Select>
        </div>

        <!-- Preview area -->
        <div v-if="isLoadingPreview" class="flex items-center gap-2 text-sm text-muted-foreground py-2">
          <Loader2 class="size-4 animate-spin" />
          Анализ переноса…
        </div>

        <div v-else-if="previewError" class="rounded-md border border-destructive/40 bg-destructive/5 p-3 text-sm text-destructive">
          {{ previewError }}
        </div>

        <div v-else-if="preview" class="space-y-4">
          <!-- SKU collision -->
          <div v-if="preview.skuConflict" class="space-y-2 rounded-md border border-amber-500/40 bg-amber-500/5 p-3">
            <div class="flex items-center gap-2 text-sm font-medium text-amber-700 dark:text-amber-400">
              <AlertTriangle class="size-4" />
              SKU «{{ productSku }}» уже занят в целевом магазине
            </div>
            <Label for="new-sku" class="text-xs">Задайте другой SKU</Label>
            <Input id="new-sku" v-model="newSku" placeholder="Новый SKU" />
          </div>

          <!-- Category resolution -->
          <div v-if="preview.sourceProduct.categoryName" class="space-y-2">
            <Label class="text-sm">Категория в целевом магазине</Label>
            <div class="text-xs text-muted-foreground">
              Исходная категория: <span class="font-medium">«{{ preview.sourceProduct.categoryName }}»</span>
              <span v-if="preview.matchedCategory" class="ml-2 text-emerald-600 dark:text-emerald-400">
                ✓ найдена идентичная
                ({{ preview.matchedCategory.attributesMappable }} атрибутов смэппится<span v-if="preview.matchedCategory.attributesUnmappable?.length">, {{ preview.matchedCategory.attributesUnmappable.length }} потеряется</span>)
              </span>
              <span v-else class="ml-2 text-amber-700 dark:text-amber-400">
                ✗ не найдена
              </span>
            </div>
            <Select v-model="categoryAction">
              <SelectTrigger class="w-full">
                <SelectValue placeholder="Действие с категорией" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem v-if="preview.matchedCategory" value="match" as="button">
                  Использовать найденную «{{ preview.matchedCategory.name }}»
                </SelectItem>
                <SelectItem value="create" as="button">Создать идентичную в целевом магазине</SelectItem>
                <SelectItem value="none" as="button">Без категории</SelectItem>
              </SelectContent>
            </Select>
            <div v-if="preview.matchedCategory?.attributesUnmappable?.length" class="text-xs text-muted-foreground">
              Не смэппятся: {{ preview.matchedCategory.attributesUnmappable.slice(0, 5).join(", ") }}<span v-if="preview.matchedCategory.attributesUnmappable.length > 5">…</span>
            </div>
          </div>

          <!-- Store attributes -->
          <div v-if="preview.storeAttributesMappable + preview.storeAttributesUnmappable.length > 0"
               class="text-xs text-muted-foreground">
            Глобальные атрибуты: {{ preview.storeAttributesMappable }} перенесётся,
            {{ preview.storeAttributesUnmappable.length }} не найдено в целевом магазине<span v-if="preview.storeAttributesUnmappable.length">: {{ preview.storeAttributesUnmappable.slice(0, 3).join(", ") }}<span v-if="preview.storeAttributesUnmappable.length > 3">…</span></span>
          </div>

          <!-- Group selection -->
          <div class="space-y-2">
            <Label class="text-sm">Группа в целевом магазине</Label>
            <Select v-model="groupAction">
              <SelectTrigger class="w-full">
                <SelectValue placeholder="Действие с группой" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="none" as="button">Без группы</SelectItem>
                <SelectItem v-if="preview.targetGroups.length > 0" value="use" as="button">Существующая…</SelectItem>
                <SelectItem value="create" as="button">Создать новую…</SelectItem>
              </SelectContent>
            </Select>
            <Select
              v-if="groupAction === 'use'"
              :model-value="groupUseId == null ? '' : String(groupUseId)"
              @update:model-value="(v) => groupUseId = v ? Number(v) : null"
            >
              <SelectTrigger class="w-full">
                <SelectValue placeholder="Группа в целевом магазине" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem v-for="g in preview.targetGroups" :key="g.id" :value="String(g.id)" as="button">
                  {{ g.name }}
                </SelectItem>
              </SelectContent>
            </Select>
            <Input v-if="groupAction === 'create'" v-model="groupNewName" placeholder="Название новой группы" />
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
                Будут разорваны marketplace-связки ({{ preview.marketplaceLinksToBreak.length }})
              </template>
              <template v-else>
                Marketplace-связки исходного товара ({{ preview.marketplaceLinksToBreak.length }})
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
                Карточки на маркетплейсах останутся, но больше не будут связаны с этим товаром в PrintO.
                После перемещения связки нужно завести заново уже для копии.
              </template>
              <template v-else>
                Связки остаются у оригинала. Клон создаётся без marketplace-связок —
                их можно завести вручную для нового товара после клонирования.
              </template>
            </p>
            <label v-if="mode === 'move'" class="flex items-start gap-2 text-sm cursor-pointer">
              <input type="checkbox" v-model="confirmBreakLinks" class="mt-0.5" />
              <span>Я понимаю и подтверждаю разрыв связок</span>
            </label>
          </div>

          <!-- Summary of what will travel -->
          <div class="rounded-md border bg-muted/30 p-3 text-xs text-muted-foreground space-y-0.5">
            <div class="font-medium text-foreground mb-1">Будет перенесено:</div>
            <div>· Файлы: {{ preview.filesCount }}<span v-if="preview.foldersCount"> (в {{ preview.foldersCount }} папках)</span></div>
            <div>· Изображения: {{ preview.imagesCount }}</div>
            <div>· Заметки: {{ preview.notesCount }}</div>
            <div v-if="preview.variantsCount > 0">· Варианты (размеры): {{ preview.variantsCount }}</div>
          </div>
        </div>
      </div>

      <DialogFooter>
        <Button variant="outline" @click="close" :disabled="isSubmitting">Отмена</Button>
        <Button @click="submit" :disabled="!canSubmit">
          <Loader2 v-if="isSubmitting" class="size-4 animate-spin mr-2" />
          {{ mode === "move" ? "Переместить" : "Клонировать" }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
