<script setup>
/**
 * Shared preview dialog for before/after field changes (adapter import, product import, etc.).
 * Same shell pattern as Excel import: bordered header/footer, scrollable body.
 */
import { computed } from "vue";
import { Download, Loader2 } from "lucide-vue-next";
import { t } from "@/i18n";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import FieldChangesTable from "@/components/products/FieldChangesTable.vue";

const props = defineProps({
  open: { type: Boolean, default: false },
  title: { type: String, required: true },
  description: { type: String, default: "" },
  items: { type: Array, default: () => [] },
  selectable: { type: Boolean, default: false },
  excludedFields: { type: Set, default: () => new Set() },
  irreversibleFields: { type: Array, default: () => [] },
  warning: { type: String, default: null },
  confirmLabel: { type: String, default: "" },
  confirmLoading: { type: Boolean, default: false },
  size: { type: String, default: "3xl" },
});

const emit = defineEmits([
  "update:open",
  "update:excludedFields",
  "confirm",
  "cancel",
]);

const resolvedConfirmLabel = computed(
  () => props.confirmLabel || t("productMarketplaceIntegrations.confirmAction"),
);

const allFieldsExcluded = computed(() => {
  if (!props.selectable || props.items.length === 0) return false;
  const keys = props.items.flatMap((item) =>
    (item.fields || []).map((f) => f.key),
  );
  if (keys.length === 0) return true;
  return keys.every((k) => props.excludedFields.has(k));
});

const confirmDisabled = computed(
  () => props.confirmLoading || allFieldsExcluded.value,
);

function onOpenChange(value) {
  emit("update:open", value);
  if (!value) emit("cancel");
}

function onConfirm() {
  if (confirmDisabled.value) return;
  emit("confirm");
}
</script>

<template>
  <Dialog :open="open" @update:open="onOpenChange">
    <DialogContent
      v-if="open"
      :size="size"
      class="flex max-h-[85vh] flex-col gap-0 overflow-hidden p-0"
    >
      <DialogHeader class="shrink-0 space-y-1 border-b px-6 py-4 text-left">
        <DialogTitle class="flex items-center gap-2">
          <Download class="size-5 text-muted-foreground" />
          {{ title }}
        </DialogTitle>
        <DialogDescription v-if="description">
          {{ description }}
        </DialogDescription>
      </DialogHeader>

      <div class="min-h-0 flex-1 overflow-y-auto px-6 py-5">
        <FieldChangesTable
          v-if="items.length > 0"
          :items="items"
          :selectable="selectable"
          :excluded-fields="excludedFields"
          :irreversible-fields="irreversibleFields"
          :warning="warning"
          @update:excluded-fields="emit('update:excludedFields', $event)"
        />
        <p v-else class="py-8 text-center text-sm text-muted-foreground">
          {{ t("fieldChangesTable.noChangesDescription") }}
        </p>
      </div>

      <DialogFooter class="shrink-0 gap-2 border-t px-6 py-4 sm:justify-between">
        <DialogClose as-child>
          <Button variant="outline" :disabled="confirmLoading">
            {{ t("common.cancel") }}
          </Button>
        </DialogClose>
        <Button :disabled="confirmDisabled" @click="onConfirm">
          <Loader2 v-if="confirmLoading" class="mr-2 size-4 animate-spin" />
          <Download v-else class="mr-2 size-4" />
          {{ resolvedConfirmLabel }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
