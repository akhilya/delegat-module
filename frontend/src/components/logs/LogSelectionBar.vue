<script setup>
import { Button } from "@/components/ui/button";
import { Download, Trash2, Loader2, X } from "lucide-vue-next";
import { t } from "@/i18n";

defineProps({
  count: { type: Number, default: 0 },
  downloading: { type: Boolean, default: false },
  deleting: { type: Boolean, default: false },
});

defineEmits(["download", "delete", "clear"]);
</script>

<template>
  <div v-if="count > 0" class="flex flex-wrap items-center gap-2">
    <div class="h-4 w-px shrink-0 bg-border" />
    <span class="text-sm font-medium whitespace-nowrap">
      {{ t("components.logs.selectedCount", { count }) }}
    </span>
    <Button
      type="button"
      variant="ghost"
      size="sm"
      class="h-8 w-8 p-0"
      :disabled="downloading || deleting"
      :title="t('components.logs.clearSelection')"
      @click="$emit('clear')"
    >
      <X class="size-4" />
    </Button>
    <Button
      type="button"
      variant="outline"
      size="sm"
      class="h-8 gap-1.5"
      :disabled="downloading || deleting"
      @click="$emit('download')"
    >
      <Loader2 v-if="downloading" class="size-3.5 animate-spin" />
      <Download v-else class="size-3.5" />
      {{ t("components.logs.downloadSelected") }}
    </Button>
    <Button
      type="button"
      variant="destructive"
      size="sm"
      class="h-8 gap-1.5"
      :disabled="downloading || deleting"
      @click="$emit('delete')"
    >
      <Loader2 v-if="deleting" class="size-3.5 animate-spin" />
      <Trash2 v-else class="size-3.5" />
      {{ t("components.logs.deleteSelected") }}
    </Button>
  </div>
</template>
