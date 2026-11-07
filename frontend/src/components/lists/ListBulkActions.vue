<script setup>
import { t } from "@/i18n";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { Pencil, Trash2, X } from "lucide-vue-next";

defineProps({
  count: { type: Number, default: 0 },
  disabled: { type: Boolean, default: false },
  busy: { type: Boolean, default: false },
  editable: { type: Boolean, default: false },
});
defineEmits(["clear", "edit", "delete"]);
</script>

<template>
  <div
    v-if="count > 0"
    data-slot="list-bulk-actions"
    class="flex flex-wrap items-center gap-2 px-1"
  >
    <span role="status" class="text-sm font-medium tabular-nums">{{
      t("components.listSelection.selectedCount", { count })
    }}</span>
    <Button
      type="button"
      variant="ghost"
      size="sm"
      class="h-8"
      :disabled="busy"
      :aria-label="t('components.listSelection.clearSelection')"
      @click="$emit('clear')"
      ><X class="size-4"
    /></Button>
    <Button
      v-if="editable"
      type="button"
      variant="outline"
      size="sm"
      class="h-8"
      :disabled="disabled || busy"
      @click="$emit('edit')"
    >
      <Pencil class="mr-1 size-4" />{{ t("common.edit") }}
    </Button>
    <Button
      type="button"
      variant="destructive"
      size="sm"
      class="h-8"
      :disabled="disabled || busy"
      @click="$emit('delete')"
    >
      <Spinner v-if="busy" class="mr-1" /><Trash2
        v-else
        class="mr-1 size-4"
      />{{ t("common.delete") }}
    </Button>
    <slot />
  </div>
</template>
